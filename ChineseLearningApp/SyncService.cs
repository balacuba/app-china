using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ChineseLearning.Sync;

namespace ChineseLearningApp;

// Keeps this PC's study progress in step with the study server, where the
// website syncs too. The sign-in token is kept encrypted for this Windows user
// (like the Gemini key); the server address and user name in sync-settings.json;
// the progress as it was at the last sync in sync-base.json.
//
// A sync merges what changed here since the last sync into what is on the
// server (ChineseLearning.Sync.ProgressMerge) and saves the result there. The
// caller then writes the result into the app's own files and only then calls
// CommitBase, so the "last sync" mark never runs ahead of what is on disk.
internal sealed class SyncService
{
    public const string DefaultServer = "http://localhost:5080";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChineseLearningApp");

    private readonly AiKeyStore _token = new("sync-token.bin");
    private readonly string _settingsPath = Path.Combine(Folder, "sync-settings.json");
    private readonly string _basePath = Path.Combine(Folder, "sync-base.json");

    public SyncService()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath, Encoding.UTF8));
                ServerUrl = string.IsNullOrWhiteSpace(settings?.ServerUrl) ? DefaultServer : settings.ServerUrl;
                UserName = settings?.UserName ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings: start signed out with the default server.
        }
    }

    public string ServerUrl { get; private set; } = DefaultServer;
    public string UserName { get; private set; } = string.Empty;
    public bool SignedIn => _token.HasKey && UserName.Length > 0;

    // Signs in (or creates the account) and remembers it. Returns null when it
    // worked, otherwise what to tell the learner.
    public async Task<string?> SignInAsync(string server, string userName, string password, bool register, CancellationToken cancellation)
    {
        server = server.Trim().TrimEnd('/');
        if (!Uri.TryCreate(server, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
        {
            return "Địa chỉ máy chủ không hợp lệ (ví dụ http://localhost:5080).";
        }

        try
        {
            using var response = await Http.PostAsJsonAsync(
                $"{server}/api/{(register ? "register" : "login")}",
                new { userName = userName.Trim(), password },
                Json,
                cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await ErrorText(response, cancellation).ConfigureAwait(false);
            }

            var signedIn = await response.Content.ReadFromJsonAsync<SignInReply>(Json, cancellation).ConfigureAwait(false)
                ?? throw new JsonException();
            // Another account's progress must not be merged into this one.
            if (!string.Equals(signedIn.UserName, UserName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(server, ServerUrl, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(_basePath);
            }

            ServerUrl = server;
            UserName = signedIn.UserName;
            _token.Save(signedIn.Token);
            SaveSettings();
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return $"Không kết nối được máy chủ {server}.";
        }
    }

    public void SignOut()
    {
        var token = _token.Load();
        if (token.Length > 0)
        {
            // Ending the token on the server is a courtesy; it expires anyway.
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/api/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _ = Http.SendAsync(request).ContinueWith(task => request.Dispose(), TaskScheduler.Default);
        }

        _token.Save(string.Empty);
        File.Delete(_basePath);
    }

    // Merges `local` with the server and saves the result there. Returns the
    // document as the server now has it; call CommitBase with it once it has
    // been written into the app's files.
    public async Task<ProgressDocument> SyncAsync(ProgressDocument local, CancellationToken cancellation)
    {
        var token = _token.Load();
        if (token.Length == 0)
        {
            throw new SyncSignedOutException();
        }

        var baseline = LoadBase();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var remote = await Get<ProgressEnvelope>(token, cancellation).ConfigureAwait(false);
            var merged = ProgressMerge.Merge(baseline, local, remote.Progress);
            if (Same(merged, remote.Progress))
            {
                return remote.Progress;
            }

            var (saved, conflict) = await Put(token, merged, remote.Version, cancellation).ConfigureAwait(false);
            if (!conflict)
            {
                return saved!.Progress;
            }
        }

        throw new HttpRequestException("Máy chủ đang bận, hãy thử lại.");
    }

    public void CommitBase(ProgressDocument synced)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(_basePath, JsonSerializer.Serialize(synced, Json), new UTF8Encoding(false));
    }

    public static bool Same(ProgressDocument a, ProgressDocument b) =>
        JsonSerializer.Serialize(Normalized(a), Json) == JsonSerializer.Serialize(Normalized(b), Json);

    // Word order in the missed list carries no meaning; compare without it.
    private static ProgressDocument Normalized(ProgressDocument document)
    {
        var copy = document.Clone();
        copy.MissedWords = copy.MissedWords.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        return copy;
    }

    private ProgressDocument? LoadBase()
    {
        try
        {
            return File.Exists(_basePath)
                ? JsonSerializer.Deserialize<ProgressDocument>(File.ReadAllText(_basePath, Encoding.UTF8), Json)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private async Task<T> Get<T>(string token, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ServerUrl}/api/progress");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _token.Save(string.Empty);
            throw new SyncSignedOutException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await ErrorText(response, cancellation).ConfigureAwait(false));
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, cancellation).ConfigureAwait(false)
            ?? throw new JsonException("Máy chủ trả lời trống.");
    }

    // Saves over `version`; a conflict (409) means someone synced first, so the
    // caller fetches again and merges once more.
    private async Task<(ProgressEnvelope? Saved, bool Conflict)> Put(string token, ProgressDocument document, int version, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{ServerUrl}/api/progress")
        {
            Content = JsonContent.Create(document, options: Json)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("If-Match", version.ToString());
        using var response = await Http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return (null, true);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _token.Save(string.Empty);
            throw new SyncSignedOutException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await ErrorText(response, cancellation).ConfigureAwait(false));
        }

        return (await response.Content.ReadFromJsonAsync<ProgressEnvelope>(Json, cancellation).ConfigureAwait(false), false);
    }

    private static async Task<string> ErrorText(HttpResponseMessage response, CancellationToken cancellation)
    {
        try
        {
            var reply = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellation).ConfigureAwait(false);
            if (reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("error", out var error) && error.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
        }

        return $"Máy chủ báo lỗi {(int)response.StatusCode}.";
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new Settings(ServerUrl, UserName)), new UTF8Encoding(false));
    }

    private sealed record Settings(string? ServerUrl, string? UserName);

    private sealed record SignInReply(string Token, string UserName);
}

// The server no longer accepts this PC's sign-in; it has to sign in again.
internal sealed class SyncSignedOutException : Exception
{
}
