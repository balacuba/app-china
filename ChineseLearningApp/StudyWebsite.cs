using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ChineseLearningServer;
using Microsoft.AspNetCore.Builder;

namespace ChineseLearningApp;

// The study website (and the server the app syncs with), run inside the app:
// it starts when the app opens and stops when it closes, so there is nothing
// else to start. With "others may open it" on, it listens on every network
// card: phones on the same Wi-Fi can open it, and so can anyone on the Internet
// once the router forwards the port to this PC (NAT). Otherwise only this PC can.
//
// Its settings (port, others allowed) are in website-settings.json with the
// app's other data; its database stays in the server folder there.
internal static class StudyWebsite
{
    public const int DefaultPort = 5080;
    private const string FirewallRule = "ChineseLearningApp Web";
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChineseLearningApp", "website-settings.json");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static WebApplication? _app;
    private static Task _starting = Task.CompletedTask;

    public static int Port { get; private set; } = DefaultPort;
    public static bool AllowOthers { get; private set; } = true;
    public static bool Running => _app is not null;

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
    }

    // Starts again with others allowed or not.
    public static async Task RestartAsync(bool allowOthers)
    {
        AllowOthers = allowOthers;
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
        try
        {
            var app = StudyServer.Create(
            [
                "--urls", AllowOthers ? $"http://*:{Port}" : $"http://localhost:{Port}",
                "--Logging:LogLevel:Default", "Warning"
            ], installed: true);
            await app.StartAsync().ConfigureAwait(false);
            _app = app;
            Problem = null;
        }
        catch (IOException ex) when (ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException ||
                                     ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
        {
            Problem = $"Cổng {Port} đang được chương trình khác dùng (có thể máy chủ học bản cũ đang chạy). Tắt nó rồi mở lại app.";
        }
        catch (Exception ex)
        {
            Problem = $"Không chạy được trang web: {ex.Message}";
        }

        Changed?.Invoke();
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
                       $"netsh advfirewall firewall add rule {rule} dir=in action=allow protocol=TCP localport={Port} profile=any";
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
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(Port, AllowOthers)), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept for this run only.
        }
    }

    private sealed record Settings(int Port, bool AllowOthers);
}
