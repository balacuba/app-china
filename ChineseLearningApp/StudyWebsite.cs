using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ChineseLearningServer;
using LettuceEncrypt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChineseLearningApp;

// The study website (and the server the app syncs with), run inside the app:
// it starts when the app opens and stops when it closes, so there is nothing
// else to start. With "others may open it" on, it listens on every network
// card: phones on the same Wi-Fi can open it, and so can anyone on the Internet
// once the router forwards the port to this PC (NAT). Otherwise only this PC can.
//
// With https on (others allowed), it also answers on ports 80 and 443 under a
// domain name, by default <WAN IP with dashes>.sslip.io, which always points at
// that IP, with a free certificate from Let's Encrypt fetched and renewed by
// LettuceEncrypt. Google sign-in needs that: it refuses pages opened by IP or
// over http. The router must forward ports 80 and 443 to this PC. A watch
// every half minute notices a new certificate (or why Let's Encrypt refused
// one), and every ten minutes whether the WAN IP changed: an sslip.io name
// then follows the new IP (the website starts again under it).
//
// Its settings (port, others allowed, https) are in website-settings.json with
// the app's other data; its database and certificates stay in the server folder there.
internal static class StudyWebsite
{
    public const int DefaultPort = 5080;
    private static readonly string CertificateFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChineseLearningApp", "server", "https");
    private const string FirewallRule = "ChineseLearningApp Web";
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChineseLearningApp", "website-settings.json");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static WebApplication? _app;
    private static Task _starting = Task.CompletedTask;

    public static int Port { get; private set; } = DefaultPort;
    public static bool AllowOthers { get; private set; } = true;
    public static bool Https { get; private set; }
    public static string HttpsDomain { get; private set; } = string.Empty;
    public static bool Running => _app is not null;

    // Whether this run answers over https (on, others allowed, a domain set).
    public static bool ServesHttps { get; private set; }

    public static string HttpsAddress => $"https://{HttpsDomain}";

    // An <IP>.sslip.io name, which follows the WAN IP when it changes.
    public static bool FollowsInternetAddress => HttpsDomain.EndsWith(".sslip.io", StringComparison.Ordinal);

    // What Let's Encrypt (through LettuceEncrypt) last said went wrong, or null.
    public static string? CertificateProblem { get; private set; }

    // Set when the WAN IP changed and the domain followed it: tells the learner
    // to update the addresses in Google Cloud.
    public static string? DomainChange { get; private set; }

    // Finds the domain for the current WAN IP (replaceable by tests).
    internal static Func<Task<string?>> CurrentDomain { get; set; } = DomainForInternetAddressAsync;

    private static System.Threading.Timer? _watch;
    private static int _watching;
    private static DateTime _lastAddressCheck = DateTime.MinValue;

    // Why the website is not running, or null.
    public static string? Problem { get; private set; }

    // Finishes once the website has started (or failed to).
    public static Task Starting => _starting;

    // Raised (on a worker thread) when the website starts, stops or fails.
    public static event Action? Changed;

    public static string LocalAddress => $"http://localhost:{Port}";

    public static void Start()
    {
        LoadSettings();
        _starting = Task.Run(StartCore);
        _watch ??= new System.Threading.Timer(_ => _ = WatchAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    // The https certificate on disk for the domain, newest first; null when
    // Let's Encrypt has not issued one yet.
    public static DateTime? CertificateExpires()
    {
        try
        {
            var folder = Path.Combine(CertificateFolder, "certs");
            if (!Directory.Exists(folder) || HttpsDomain.Length == 0)
            {
                return null;
            }

            DateTime? newest = null;
            foreach (var file in Directory.EnumerateFiles(folder, "*.pfx"))
            {
                using var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(file, (string?)null);
                if (certificate.MatchesHostname(HttpsDomain) && certificate.NotAfter > DateTime.Now && (newest is null || certificate.NotAfter > newest))
                {
                    newest = certificate.NotAfter;
                }
            }

            return newest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    // Every half minute while https is served: tells the card (a certificate
    // may have arrived); every ten minutes, follows a new WAN IP.
    private static async Task WatchAsync()
    {
        if (!ServesHttps || Interlocked.Exchange(ref _watching, 1) == 1)
        {
            return;
        }

        try
        {
            if (FollowsInternetAddress && DateTime.Now - _lastAddressCheck > TimeSpan.FromMinutes(10))
            {
                _lastAddressCheck = DateTime.Now;
                if (await CurrentDomain().ConfigureAwait(false) is { } domain && domain != HttpsDomain)
                {
                    DomainChange = $"IP WAN đã đổi: địa chỉ https mới là https://{domain} (trước là {HttpsAddress}). " +
                                   "Nhớ sửa địa chỉ này trong Google Cloud (Authorized JavaScript origins và redirect URIs).";
                    await RestartAsync(AllowOthers, true, domain).ConfigureAwait(false);
                    return;
                }
            }

            Changed?.Invoke();
        }
        finally
        {
            Interlocked.Exchange(ref _watching, 0);
        }
    }

    // Starts again with others allowed or not.
    public static Task RestartAsync(bool allowOthers) => RestartAsync(allowOthers, Https, HttpsDomain);

    // Starts again with others allowed or not, and https on or off for `domain`.
    public static async Task RestartAsync(bool allowOthers, bool https, string domain)
    {
        AllowOthers = allowOthers;
        Https = https;
        HttpsDomain = domain.Trim().ToLowerInvariant();
        SaveSettings();
        await StopAsync().ConfigureAwait(false);
        _starting = Task.Run(StartCore);
        await _starting.ConfigureAwait(false);
    }

    public static async Task StopAsync()
    {
        await _starting.ConfigureAwait(false);
        if (_app is not { } app)
        {
            return;
        }

        _app = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await app.StopAsync(timeout.Token).ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
        Changed?.Invoke();
    }

    private static async Task StartCore()
    {
        var https = Https && AllowOthers && IsDomainName(HttpsDomain);
        CertificateProblem = null;
        try
        {
            // With https, UseHttps names every port itself.
            var app = StudyServer.Create(https
                ? ["--Logging:LogLevel:Default", "Warning"]
                : ["--urls", AllowOthers ? $"http://*:{Port}" : $"http://localhost:{Port}", "--Logging:LogLevel:Default", "Warning"],
                installed: true, https ? UseHttps : null);
            await app.StartAsync().ConfigureAwait(false);
            _app = app;
            ServesHttps = https;
            Problem = null;
        }
        catch (IOException ex) when (ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException ||
                                     ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
        {
            Problem = https
                ? $"Cổng {Port}, 80 hoặc 443 đang được chương trình khác dùng (IIS, Skype, máy chủ web khác…). Tắt nó, hoặc tắt https."
                : $"Cổng {Port} đang được chương trình khác dùng (có thể máy chủ học bản cũ đang chạy). Tắt nó rồi mở lại app.";
        }
        catch (Exception ex)
        {
            Problem = $"Không chạy được trang web: {ex.Message}";
        }

        Changed?.Invoke();
    }

    // Ports 80 (http, where Let's Encrypt checks the domain) and 443 (https,
    // with the certificate it issues), besides the usual port.
    private static void UseHttps(WebApplicationBuilder builder)
    {
        builder.Services.AddLettuceEncrypt(options =>
            {
                options.AcceptTermsOfService = true;
                options.DomainNames = [HttpsDomain];
            })
            .PersistDataToDirectory(new DirectoryInfo(CertificateFolder), null);
        builder.Logging.AddProvider(new CertificateLog());
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(Port);
            kestrel.ListenAnyIP(80);
            kestrel.ListenAnyIP(443, listen => listen.UseHttps(https => https.UseLettuceEncrypt(kestrel.ApplicationServices)));
        });
    }

    public static bool IsDomainName(string text) =>
        Uri.CheckHostName(text) == UriHostNameType.Dns && text.Contains('.') && !text.EndsWith('.');

    // <WAN IP with dashes>.sslip.io: a free name that always points at the IP
    // the Internet sees for this network. Null when offline.
    public static async Task<string?> DomainForInternetAddressAsync()
    {
        var address = await InternetAddressAsync().ConfigureAwait(false);
        return address is null ? null : $"{new Uri(address).Host.Replace('.', '-')}.sslip.io";
    }

    // This PC's addresses in the home network (Wi-Fi or cable), for phones and
    // other PCs there.
    public static IReadOnlyList<string> NetworkAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(card => card.OperationalStatus == OperationalStatus.Up &&
                           card.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(card => card.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                              !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(address => $"http://{address.Address}:{Port}")
            .Distinct()
            .ToList();

    // The address the Internet sees for this network (asked of api.ipify.org),
    // or null when offline. It works from outside only once the router forwards
    // the port to this PC.
    public static async Task<string?> InternetAddressAsync()
    {
        try
        {
            var ip = (await Http.GetStringAsync("https://api.ipify.org").ConfigureAwait(false)).Trim();
            return System.Net.IPAddress.TryParse(ip, out _) ? $"http://{ip}:{Port}" : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    // Lets other machines through Windows Firewall to the website's port (asks
    // for administrator permission). Returns null when done, or why not.
    public static async Task<string?> OpenFirewallAsync()
    {
        var rule = $"name=\"{FirewallRule}\"";
        var commands = $"/c netsh advfirewall firewall delete rule {rule} >nul & " +
                       $"netsh advfirewall firewall add rule {rule} dir=in action=allow protocol=TCP localport={(Https ? $"{Port},80,443" : $"{Port}")} profile=any";
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", commands)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null)
            {
                return "Không mở được tường lửa.";
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode == 0 ? null : $"Tường lửa báo lỗi ({process.ExitCode}).";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The learner said no to the administrator prompt.
            return "Chưa mở tường lửa (cần đồng ý quyền quản trị).";
        }
    }

    private static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath) &&
                JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath, Encoding.UTF8)) is { } settings)
            {
                Port = settings.Port is > 0 and < 65536 ? settings.Port : DefaultPort;
                AllowOthers = settings.AllowOthers;
                Https = settings.Https;
                HttpsDomain = settings.HttpsDomain ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable: the defaults.
        }
    }

    private static void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(Port, AllowOthers, Https, HttpsDomain)), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept for this run only.
        }
    }

    // Keeps LettuceEncrypt's warnings and errors (Let's Encrypt could not reach
    // the domain, too many certificates…) for the settings card.
    private sealed class CertificateLog : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) =>
            categoryName.StartsWith("LettuceEncrypt", StringComparison.Ordinal) ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var text = formatter(state, exception) + (exception is null ? "" : $" {exception.Message}");
            CertificateProblem = text.Length > 300 ? text[..300] + "…" : text;
            Changed?.Invoke();
        }

        public void Dispose()
        {
        }
    }

    private sealed record Settings(int Port, bool AllowOthers, bool Https = false, string? HttpsDomain = null);
}
