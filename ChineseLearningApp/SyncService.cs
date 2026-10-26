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
    // Answers from Gemini take longer than a sync.
    private static readonly HttpClient GeminiHttp = new() { Timeout = TimeSpan.FromSeconds(90) };
    // Making a Gemini key in a Google account the first time can take a couple of minutes.
    private static readonly HttpClient KeyHttp = new() { Timeout = TimeSpan.FromMinutes(3) };
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

    // Opens a page in the learner's browser (replaceable in tests).
    public Action<string> OpenInBrowser { get; set; } = url =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

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

    // Signs in with Google (or any way the website offers) through the browser:
    // waits on a free port of this PC, opens the website's page for signing the
    // app in, and takes the token the page posts back once the learner allows
    // it. The random state proves the answer is to this request. Returns null
    // when it worked, otherwise what to tell the learner.
    public async Task<string?> SignInThroughBrowserAsync(string server, CancellationToken cancellation)
    {
        server = server.Trim().TrimEnd('/');
        if (!Uri.TryCreate(server, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
        {
            return "Địa chỉ máy chủ không hợp lệ (ví dụ http://localhost:5080).";
        }

        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var state = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            OpenInBrowser($"{server}/#/desktop-login?port={port}&state={state}");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                var form = await ReadPostedForm(client, timeout.Token).ConfigureAwait(false);
                var matches = form is not null && form.GetValueOrDefault("state") == state &&
                    form.GetValueOrDefault("token") is { Length: > 0 } && form.GetValueOrDefault("userName") is { Length: > 0 };
                await Reply(client, matches
                    ? "Đã kết nối app với tài khoản của bạn. Bạn có thể đóng trang này và quay lại app."
                    : "Yêu cầu không khớp với app đang chờ. Hãy bấm đăng nhập trong app một lần nữa.").ConfigureAwait(false);
                if (!matches)
                {
                    continue;
                }

                if (!string.Equals(form!["userName"], UserName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(server, ServerUrl, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(_basePath);
                }

                ServerUrl = server;
                UserName = form["userName"];
                _token.Save(form["token"]);
                SaveSettings();
                return null;
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return "Hết thời gian chờ đăng nhập trong trình duyệt. Hãy thử lại.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return $"Không mở được trình duyệt. Hãy mở {server} rồi thử lại.";
        }
        finally
        {
            listener.Stop();
        }
    }

    // The fields of a small form POST (application/x-www-form-urlencoded), or
    // null for anything else.
    private static async Task<Dictionary<string, string>?> ReadPostedForm(System.Net.Sockets.TcpClient client, CancellationToken cancellation)
    {
        var stream = client.GetStream();
        var buffer = new byte[16 * 1024];
        var length = 0;
        int headerEnd;
        while ((headerEnd = IndexOf(buffer, length, "\r\n\r\n"u8)) < 0)
        {
            if (length == buffer.Length)
            {
                return null;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellation).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            length += read;
        }

        var headers = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        if (!headers[0].StartsWith("POST /callback ", StringComparison.Ordinal))
        {
            return null;
        }

        var contentLength = headers.Skip(1)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(parts => int.TryParse(parts[1].Trim(), out var value) ? value : -1)
            .FirstOrDefault(-1);
        var bodyStart = headerEnd + 4;
        if (contentLength < 0 || bodyStart + contentLength > buffer.Length)
        {
            return null;
        }

        while (length < bodyStart + contentLength)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellation).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            length += read;
        }

        var body = Encoding.ASCII.GetString(buffer, bodyStart, contentLength);
        return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')))
            .ToDictionary(group => group.Key, group => Uri.UnescapeDataString(group.First()[1].Replace('+', ' ')));
    }

    private static int IndexOf(byte[] buffer, int length, ReadOnlySpan<byte> pattern) =>
        buffer.AsSpan(0, length).IndexOf(pattern);

    private static async Task Reply(System.Net.Sockets.TcpClient client, string message)
    {
        var page = Encoding.UTF8.GetBytes(
            "<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>Học tiếng Trung</title>" +
            "<body style=\"font-family:Segoe UI,sans-serif;background:#fff7f0;color:#4a2f23;display:grid;place-items:center;height:100vh;margin:0\">" +
            $"<p style=\"font-size:20px;max-width:520px;text-align:center\">{System.Net.WebUtility.HtmlEncode(message)}</p></body></html>");
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {page.Length}\r\nConnection: close\r\n\r\n");
        var stream = client.GetStream();
        await stream.WriteAsync(head).ConfigureAwait(false);
        await stream.WriteAsync(page).ConfigureAwait(false);
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

    // A Gemini request sent through the study server, which asks Gemini with the
    // learner's Google account (signed in with Google there, Gemini allowed).
    // Null when signed out. The caller reads and disposes the reply.
    public async Task<HttpResponseMessage?> SendGeminiAsync(string payload, CancellationToken cancellation)
    {
        var token = _token.Load();
        if (!SignedIn || token.Length == 0)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/api/gemini/generate")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await GeminiHttp.SendAsync(request, cancellation).ConfigureAwait(false);
    }

    // The learner's own Gemini API key, which the study server makes in their
    // Google account the first time (signed in with Google, Gemini allowed).
    // Either the key or what to tell the learner.
    public async Task<(string? Key, string? Problem)> GetGeminiKeyAsync(CancellationToken cancellation)
    {
        var token = _token.Load();
        if (!SignedIn || token.Length == 0)
        {
            return (null, "Chưa đăng nhập đồng bộ.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/api/gemini/key");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await KeyHttp.SendAsync(request, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, await ErrorText(response, cancellation).ConfigureAwait(false));
            }

            var reply = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellation).ConfigureAwait(false);
            return reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("key", out var key) && key.GetString() is { Length: > 0 } text
                ? (text, null)
                : (null, "Máy chủ không trả về API key Gemini.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                   (ex is TaskCanceledException && !cancellation.IsCancellationRequested))
        {
            return (null, $"Không kết nối được máy chủ {ServerUrl}.");
        }
    }

    public static async Task<string> ErrorText(HttpResponseMessage response, CancellationToken cancellation)
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
