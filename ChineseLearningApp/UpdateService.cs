using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChineseLearningApp;

// A newer version on the update page: its number, what changed, and its installer.
internal sealed record AppRelease(Version Version, string Notes, string InstallerUrl, long InstallerSize, string? Sha256);

// Updates come from the project's GitHub Releases page: installer/release.ps1
// builds the installer and publishes it there as a release "v1.2.3" with the
// installer attached and its SHA-256 in the notes. The app asks for the latest
// release, downloads the installer, checks its size and hash, and runs it
// quietly (the installer closes the app, updates it and opens it again).
//
// When it last asked and any version the learner chose to skip are kept in
// update-settings.json with the app's other data.
internal static partial class UpdateService
{
    public const string Repository = "balacuba/app-china";

    // This app's version (set when the installer is built). Before Http, which
    // sends it.
    public static Version Current { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0));

    private static readonly string LatestUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChineseLearningApp", "update-settings.json");
    private static readonly HttpClient Http = CreateClient();

    public static Settings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath, Encoding.UTF8)) ?? new Settings()
                : new Settings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    public static void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Asked again next time.
        }
    }

    // The latest release if it is newer than this app, null if this app is up
    // to date. Throws UpdateException when the update page cannot be read.
    public static async Task<AppRelease?> FindNewerAsync(CancellationToken cancellation)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(LatestUrl, cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellation.IsCancellationRequested))
        {
            throw new UpdateException("Không kết nối được trang cập nhật. Kiểm tra mạng Internet.", ex);
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null; // Nothing released yet.
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"Trang cập nhật báo lỗi {(int)response.StatusCode}. Hãy thử lại sau.");
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
            var release = json.RootElement;
            var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || Normalize(version) <= Current)
            {
                return null;
            }

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (InstallerName().IsMatch(name))
                {
                    var notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
                    return new AppRelease(
                        Normalize(version),
                        ShaLine().Replace(notes, string.Empty).Trim(),
                        asset.GetProperty("browser_download_url").GetString()!,
                        asset.GetProperty("size").GetInt64(),
                        ShaLine().Match(notes) is { Success: true } sha ? sha.Groups[1].Value.ToLowerInvariant() : null);
                }
            }

            return null; // A release without an installer yet.
        }
    }

    // Downloads the installer into the temp folder, reporting 0–100, and checks
    // it is complete and unchanged. Returns its path.
    public static async Task<string> DownloadAsync(AppRelease release, IProgress<int> progress, CancellationToken cancellation)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ChineseLearningApp-update");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"ChineseLearningApp-Setup-{release.Version}.exe");
        try
        {
            using var response = await Http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
            await using (var target = File.Create(path))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    done += read;
                    progress.Report(release.InstallerSize > 0 ? (int)Math.Min(100, done * 100 / release.InstallerSize) : 0);
                }
            }

            if (new FileInfo(path).Length != release.InstallerSize)
            {
                throw new UpdateException("Bản cập nhật tải về không đủ. Hãy thử lại.");
            }

            if (release.Sha256 is { } expected)
            {
                await using var file = File.OpenRead(path);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation).ConfigureAwait(false)).ToLowerInvariant();
                if (actual != expected)
                {
                    throw new UpdateException("Bản cập nhật tải về không khớp mã kiểm tra, đã hủy để an toàn.");
                }
            }

            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is TaskCanceledException && !cancellation.IsCancellationRequested))
        {
            TryDelete(path);
            throw new UpdateException("Không tải được bản cập nhật. Kiểm tra mạng Internet rồi thử lại.", ex);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    // Runs the downloaded installer quietly; it closes this app, updates it
    // and opens it again. The caller closes the app straight after.
    public static void StartInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
        {
            UseShellExecute = true
        });

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub's API needs a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ChineseLearningApp", Current.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    [GeneratedRegex(@"^ChineseLearningApp-Setup-[\d.]+\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex InstallerName();

    // "sha256: <64 hex>", written into the release notes by release.ps1.
    [GeneratedRegex(@"(?im)^\s*sha256:\s*([0-9a-f]{64})\s*$")]
    private static partial Regex ShaLine();

    internal sealed class Settings
    {
        public bool CheckOnStart { get; set; } = true;
        public DateTimeOffset LastCheck { get; set; }
        public string? SkippedVersion { get; set; }
    }
}

internal sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);
