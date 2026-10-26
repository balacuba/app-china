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

    // Let's Encrypt needs an email address for the account it issues certificates to.
    public static string HttpsEmail { get; private set; } = string.Empty;
    public static bool Running => _app is not null;

    // Whether this run answers over https (on, others allowed, a domain set).
    public static bool ServesHttps { get; private set; }

    public static string HttpsAddress => $"https://{HttpsDomain}";

    // Whether the domain was filled in from the WAN IP (the box left empty),
    // rather than typed in: only such a domain follows the IP.
    public static bool HttpsDomainAuto { get; private set; } = true;

    // An <IP>.sslip.io name picked from the WAN IP, which follows it when it changes.
    public static bool FollowsInternetAddress => HttpsDomainAuto && HttpsDomain.EndsWith(".sslip.io", StringComparison.Ordinal);

    // How many checks in a row (ten minutes apart) must see the same new WAN IP
    // before the domain follows it. A network with two Internet lines can show
    // either address from one check to the next; following each would restart
    // the website and its certificate request every time.
    internal static int ChecksBeforeFollowing { get; set; } = 3;

    private static string? _newDomain;
    private static int _newDomainSeen;

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

    // One restart at a time: the watch (following the WAN IP) and the
    // settings card can both ask for one.
    private static readonly SemaphoreSlim RestartGate = new(1, 1);

    // Counts the runs, so a stopping run's certificate log cannot speak for the next one.
    private static int _generation;

    // Let's Encrypt allows only 5 failed checks of a domain per hour: the
    // failures seen per domain, and until when it said to wait.
    private static readonly Dictionary<string, List<DateTime>> Failures = new();
    private static readonly Dictionary<string, DateTime> BlockedUntil = new();
    private const int FailuresBeforeWaiting = 3;

    // Self-signed certificates answered on 443 until Let's Encrypt issues the real one.
    private static readonly Dictionary<string, System.Security.Cryptography.X509Certificates.X509Certificate2> Fallbacks = new();

    // Until when the certificate for the domain is valid (null: none yet),
    // read by the watch so the card never touches the files.
    public static DateTime? CertificateValidUntil { get; private set; }

    // Why https was asked for but is not served this run, or null.
    public static string? HttpsOffReason { get; private set; }

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
    // Let's Encrypt has not issued one yet. Only the expiry is read: the key
    // is loaded ephemerally, never into a key container.
    private static DateTime? CertificateExpires()
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
                using var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(file, (string?)null,
                    System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
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
                var domain = await CurrentDomain().ConfigureAwait(false);
                if (domain is null || domain == HttpsDomain)
                {
                    _newDomain = null;
                    _newDomainSeen = 0;
                }
                else
                {
                    _newDomainSeen = domain == _newDomain ? _newDomainSeen + 1 : 1;
                    _newDomain = domain;
                    if (_newDomainSeen >= ChecksBeforeFollowing)
                    {
                        _newDomain = null;
                        _newDomainSeen = 0;
                        var before = HttpsAddress;
                        await RestartAsync(AllowOthers, true, domain, HttpsEmail, auto: true).ConfigureAwait(false);
                        DomainChange = $"IP WAN đã đổi: địa chỉ https mới là https://{domain} (trước là {before}). " +
                                       "Nhớ sửa địa chỉ này trong Google Cloud (Authorized JavaScript origins và redirect URIs).";
                        Changed?.Invoke();
                        return;
                    }
                }
            }

            CertificateValidUntil = CertificateExpires();
            Changed?.Invoke();
        }
        finally
        {
            Interlocked.Exchange(ref _watching, 0);
        }
    }

    // Starts again with others allowed or not.
    public static Task RestartAsync(bool allowOthers) => RestartAsync(allowOthers, Https, HttpsDomain, HttpsEmail, HttpsDomainAuto);

    // Starts again with others allowed or not, and https on or off for
    // `domain` (`auto`: picked from the WAN IP, so it follows it).
    public static async Task RestartAsync(bool allowOthers, bool https, string domain, string email, bool auto)
    {
        await RestartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            AllowOthers = allowOthers;
            Https = https;
            HttpsDomain = domain.Trim().ToLowerInvariant();
            HttpsEmail = email.Trim();
            HttpsDomainAuto = auto;
            DomainChange = null;
            _newDomain = null;
            _newDomainSeen = 0;
            SaveSettings();
            await StopAsync().ConfigureAwait(false);
            _starting = Task.Run(StartCore);
            await _starting.ConfigureAwait(false);
        }
        finally
        {
            RestartGate.Release();
        }
    }

    // Why asking Let's Encrypt for `domain` now would only add a failure (it
    // allows 5 failed checks an hour), with when to try again; null when fine
    // or when a valid certificate is already there.
    public static string? CertificateWait(string domain)
    {
        domain = domain.Trim().ToLowerInvariant();
        if (domain == HttpsDomain && CertificateValidUntil is not null)
        {
            return null;
        }

        lock (Failures)
        {
            if (BlockedUntil.TryGetValue(domain, out var until) && until > DateTime.Now)
            {
                return $"Let's Encrypt đang tạm khóa {domain} vì xác minh thất bại nhiều lần; thử lại sau {until:HH:mm dd/MM}.";
            }

            if (Failures.TryGetValue(domain, out var times))
            {
                times.RemoveAll(time => DateTime.Now - time > TimeSpan.FromHours(1));
                if (times.Count >= FailuresBeforeWaiting)
                {
                    return $"Let's Encrypt đã không xác minh được {domain} {times.Count} lần trong 1 giờ (giới hạn 5 lần). " +
                           $"Hãy kiểm tra cổng 80 từ 4G (mở http://{domain}/ phải ra trang học) rồi thử lại sau {times.Min().AddHours(1):HH:mm}.";
                }
            }
        }

        return null;
    }

    // What the certificate log said about `domain`: a failed check, or Let's
    // Encrypt saying to wait ("retry after 2026-10-26 10:37:28 UTC").
    private static void NoteCertificateLog(string domain, string text)
    {
        lock (Failures)
        {
            var retry = System.Text.RegularExpressions.Regex.Match(text, @"retry after (\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) UTC");
            if (retry.Success && DateTime.TryParse(retry.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var until))
            {
                BlockedUntil[domain] = until;
            }
            else if (text.Contains("Failed to validate ownership", StringComparison.OrdinalIgnoreCase))
            {
                if (!Failures.TryGetValue(domain, out var times))
                {
                    Failures[domain] = times = [];
                }

                times.Add(DateTime.Now);
            }
        }
    }

    // A self-signed certificate for `domain`, so 443 answers (with a browser
    // warning) instead of hanging until Let's Encrypt issues the real one.
    private static System.Security.Cryptography.X509Certificates.X509Certificate2 FallbackFor(string domain)
    {
        lock (Fallbacks)
        {
            if (Fallbacks.TryGetValue(domain, out var known))
            {
                return known;
            }

            using var key = System.Security.Cryptography.RSA.Create(2048);
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest($"CN={domain}", key,
                System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            var names = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
            names.AddDnsName(domain);
            request.CertificateExtensions.Add(names.Build());
            using var made = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));
            // Windows' TLS needs the key in a key container, which an export and import gives it.
            var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                made.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx));
            Fallbacks[domain] = certificate;
            return certificate;
        }
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
        var generation = Interlocked.Increment(ref _generation);
        HttpsOffReason = !Https || !AllowOthers ? null
            : !IsDomainName(HttpsDomain) ? "https chưa bật: tên miền chưa hợp lệ. Nhập tên miền (hoặc để trống để tự lấy) rồi bấm “Áp dụng”."
            : !IsEmail(HttpsEmail) ? "https chưa bật: Let's Encrypt cần một email để cấp chứng chỉ. Nhập email rồi bấm “Áp dụng”."
            : Port == 443 ? "https chưa bật: cổng trang web đang là 443, trùng cổng https. Đổi Port trong website-settings.json sang cổng khác (ví dụ 5080)."
            : null;
        var https = Https && AllowOthers && HttpsOffReason is null;
        CertificateProblem = null;
        CertificateValidUntil = https ? CertificateExpires() : null;
        ServesHttps = false;
        try
        {
            // With https, UseHttps names every port itself.
            var app = StudyServer.Create(https
                ? ["--Logging:LogLevel:Default", "Warning"]
                : ["--urls", AllowOthers ? $"http://*:{Port}" : $"http://localhost:{Port}", "--Logging:LogLevel:Default", "Warning"],
                installed: true, https ? builder => UseHttps(builder, generation) : null);
            try
            {
                await app.StartAsync().ConfigureAwait(false);
            }
            catch
            {
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }

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
    private static void UseHttps(WebApplicationBuilder builder, int generation)
    {
        var domain = HttpsDomain;
        builder.Services.AddLettuceEncrypt(options =>
            {
                options.AcceptTermsOfService = true;
                options.DomainNames = [domain];
                options.EmailAddress = HttpsEmail;
                options.FallbackCertificate = FallbackFor(domain);
            })
            .PersistDataToDirectory(new DirectoryInfo(CertificateFolder), null);
        builder.Logging.AddProvider(new CertificateLog(generation, domain));
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // The usual port may itself be 80.
            foreach (var port in new[] { Port, 80 }.Distinct())
            {
                kestrel.ListenAnyIP(port);
            }

            kestrel.ListenAnyIP(443, listen => listen.UseHttps(https => https.UseLettuceEncrypt(kestrel.ApplicationServices)));
        });
    }

    public static bool IsEmail(string text) =>
        System.Net.Mail.MailAddress.TryCreate(text, out var address) && address.Address == text && IsDomainName(address.Host);

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
                HttpsEmail = settings.HttpsEmail ?? string.Empty;
                HttpsDomainAuto = settings.HttpsDomainAuto ?? true;
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
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(Port, AllowOthers, Https, HttpsDomain, HttpsEmail, HttpsDomainAuto)), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept for this run only.
        }
    }

    // Keeps LettuceEncrypt's warnings and errors (Let's Encrypt could not reach
    // the domain, too many certificates…) for the settings card, for the run
    // it belongs to only: a run being stopped may still log about its domain.
    private sealed class CertificateLog(int generation, string domain) : ILoggerProvider, ILogger
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
            NoteCertificateLog(domain, text);
            if (generation != Volatile.Read(ref _generation))
            {
                return;
            }

            CertificateProblem = text.Length > 300 ? text[..300] + "…" : text;
            Changed?.Invoke();
        }

        public void Dispose()
        {
        }
    }

    private sealed record Settings(int Port, bool AllowOthers, bool Https = false, string? HttpsDomain = null, string? HttpsEmail = null,
        bool? HttpsDomainAuto = null);
}
