using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChineseLearningApp;

// Google Gemini through its REST API. Every question asks for a JSON object back.
//
// Nothing to set up: signed in to the study server with Google (Gemini allowed
// there), the server makes the learner's own free API key in their Google
// account, which is kept here and used. Until then, or if that key fails,
// questions go through the server, which asks Gemini with the learner's Google
// account.
internal static partial class GeminiAi
{
    private const string NeedsGoogle =
        "Đăng nhập đồng bộ bằng Google (Cài đặt → Đồng bộ với web, đồng ý quyền Gemini) để dùng Dịch đoạn văn. Không cần nhập API key.";

    // The study server this PC syncs with (set by Form1).
    public static SyncService? StudyServer { get; set; }

    // Set once the server has said it cannot ask Gemini for this learner, until
    // they sign in again.
    private static bool _serverRefused;

    public static void TryServerAgain() => _serverRefused = false;

    // Whether questions can go through the study server, so no key is needed.
    public static bool ThroughServer => StudyServer is { SignedIn: true } && !_serverRefused;

    // Always the newest Flash-Lite model on the free tier, so the app need not change
    // when Google retires a model version. Flash-Lite answers in a second or two;
    // the full Flash model thinks at length and could take minutes.
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-lite-latest:generateContent";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    // The learner's own key, for the account this PC syncs with (kept as "account\nkey").
    private static string OwnKey()
    {
        var stored = AiKeyStore.Gemini.Load().Split('\n', 2);
        return stored.Length == 2 && StudyServer is { } server &&
               string.Equals(stored[0], server.UserName, StringComparison.OrdinalIgnoreCase)
            ? stored[1]
            : string.Empty;
    }

    public static bool HasOwnKey => OwnKey().Length > 0;

    // Gets the learner's own key from the study server (made in their Google
    // account the first time). Null when it worked, otherwise why not.
    public static async Task<string?> FetchKeyAsync(CancellationToken cancellation)
    {
        if (StudyServer is not { SignedIn: true } server)
        {
            return "Chưa đăng nhập đồng bộ.";
        }

        var (key, problem) = await server.GetGeminiKeyAsync(cancellation);
        if (key is not null)
        {
            AiKeyStore.Gemini.Save($"{server.UserName}\n{key}");
        }

        return problem;
    }

    // Sends one question and returns the JSON object in the answer. `progress`
    // receives `working` while Gemini answers, and a countdown if the free tier
    // asks to wait. Busy or briefly rate-limited replies are retried.
    public static async Task<JsonElement> AskJsonAsync(string system, string question, string working, IProgress<string> progress, CancellationToken cancellation)
    {
        var key = OwnKey();
        var throughServer = ThroughServer;
        if (key.Length == 0 && throughServer)
        {
            progress.Report("⏳ Đang lấy API key Gemini từ tài khoản Google của bạn (lần đầu mất khoảng 1 phút)…");
            await FetchKeyAsync(cancellation);
            key = OwnKey();
        }

        if (key.Length == 0 && !throughServer)
        {
            throw new AiException(NeedsGoogle, needsKey: true);
        }

        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            progress.Report(working);
            try
            {
                return await AskOnceAsync(key.Length > 0 ? key : null, system, question, cancellation);
            }
            catch (AiException refusedKey) when (refusedKey.NeedsKey && key.Length > 0 && throughServer)
            {
                // A key Google refuses (just made, or removed since): through the
                // server this time, and a new key is fetched next time.
                AiKeyStore.Gemini.Save(string.Empty);
                key = string.Empty;
                attempt--;
            }
            catch (ServerRefusedException refused)
            {
                _serverRefused = true;
                throw new AiException(refused.Message, needsKey: true);
            }
            catch (RetryLaterException wait)
            {
                last = wait;
                for (var seconds = wait.Seconds; seconds > 0; seconds--)
                {
                    progress.Report($"⏳ Gemini miễn phí đang giới hạn lượt hỏi, chờ {seconds} giây…");
                    await Task.Delay(1000, cancellation);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                       (ex is TaskCanceledException && !cancellation.IsCancellationRequested) ||
                                       (ex is AiException { NeedsKey: false } && attempt < 2))
            {
                last = ex;
            }
        }

        throw last switch
        {
            RetryLaterException => new AiException("Gemini miễn phí đang quá tải. Hãy thử lại sau ít phút."),
            TaskCanceledException => new AiException("Gemini trả lời quá lâu. Hãy thử lại.", inner: last),
            HttpRequestException => new AiException("Không kết nối được Gemini. Kiểm tra mạng Internet rồi thử lại.", inner: last),
            AiException ai => ai,
            _ => new AiException("Gemini trả lời không đúng dạng. Hãy thử lại.", inner: last)
        };
    }

    // `key` is the learner's API key, or null to go through the study server.
    private static async Task<JsonElement> AskOnceAsync(string? key, string system, string question, CancellationToken cancellation)
    {
        var payload = JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = question } } } },
            // A little below the default: still varied, but steadier and more coherent.
            generationConfig = new { responseMimeType = "application/json", temperature = 0.7 }
        });
        using var response = key is null
            ? await (StudyServer?.SendGeminiAsync(payload, cancellation) ?? Task.FromResult<HttpResponseMessage?>(null)) ?? throw new ServerRefusedException(NeedsGoogle)
            : await SendWithKey(key, payload, cancellation);
        if (key is null && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            // Signed out there, Gemini not allowed (any more), or not switched on
            // for the server's Google project.
            throw new ServerRefusedException(await SyncService.ErrorText(response, cancellation));
        }

        var body = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
        {
            throw Failure(response.StatusCode, body);
        }

        // The answer is the text of the first candidate's parts, leaving out any
        // "thought" parts the model shows while thinking.
        using var envelope = JsonDocument.Parse(body);
        var answer = new StringBuilder();
        if (envelope.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) &&
                    !(part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True))
                {
                    answer.Append(text.GetString());
                }
            }
        }

        var reply = answer.ToString();
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new AiException("Gemini chưa trả lời xong. Hãy thử lại.");
        }

        using var json = JsonDocument.Parse(reply[start..(end + 1)]);
        return json.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> SendWithKey(string key, string payload, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", key);
        return await Http.SendAsync(request, cancellation);
    }

    // Turns an error reply into something the learner can act on.
    private static Exception Failure(HttpStatusCode status, string body)
    {
        var reason = string.Empty;
        try
        {
            using var error = JsonDocument.Parse(body);
            reason = error.RootElement.GetProperty("error").GetProperty("status").GetString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
        }

        if (body.Contains("API_KEY_INVALID", StringComparison.Ordinal) || status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new AiException("API key Gemini tự tạo không dùng được. Hãy thử lại sau ít phút.", needsKey: true);
        }

        if (status == HttpStatusCode.TooManyRequests || reason == "RESOURCE_EXHAUSTED")
        {
            // A short wait (e.g. "retryDelay": "21s") is waited out; the daily limit is not.
            var delay = RetryDelay().Match(body);
            return delay.Success && int.Parse(delay.Groups[1].Value) <= 60
                ? new RetryLaterException(int.Parse(delay.Groups[1].Value) + 1)
                : new AiException("Đã hết lượt dùng Gemini miễn phí lúc này (giới hạn theo phút hoặc theo ngày). Hãy thử lại sau.");
        }

        if ((int)status >= 500)
        {
            return new RetryLaterException(3);
        }

        return new AiException($"Gemini báo lỗi ({(int)status} {reason}). Hãy thử lại.");
    }

    [GeneratedRegex(@"""retryDelay""\s*:\s*""(\d+)(?:\.\d+)?s""")]
    private static partial Regex RetryDelay();

    private sealed class RetryLaterException(int seconds) : Exception
    {
        public int Seconds { get; } = seconds;
    }

    private sealed class ServerRefusedException(string message) : Exception(message);
}
