using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChineseLearningApp;

// Google Gemini through its REST API, with the learner's own free API key from
// Google AI Studio. Every question asks for a JSON object back.
//
// Signed in to the study server with Google (Gemini allowed there), no key is
// needed: questions go through the server, which asks Gemini with the learner's
// own Google account. If the server cannot, the key is used.
internal static partial class GeminiAi
{
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

    // What Gemini API keys look like: "AIza" and 35 more letters, digits, - or _.
    [GeneratedRegex(@"^AIza[0-9A-Za-z_\-]{35}$")]
    public static partial Regex KeyShape();

    // Asks Gemini whether a key works (listing models costs nothing). Returns null
    // when it does, or a message saying why not.
    public static async Task<string?> CheckKeyAsync(string key, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1");
            request.Headers.Add("x-goog-api-key", key);
            using var response = await Http.SendAsync(request, cancellation);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellation);
            return body.Contains("API_KEY_INVALID", StringComparison.Ordinal)
                ? "Key không hợp lệ. Hãy sao chép lại key từ Google AI Studio."
                : $"Google từ chối key ({(int)response.StatusCode}). Hãy kiểm tra key hoặc thử lại.";
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellation.IsCancellationRequested))
        {
            return "Không kết nối được Google để kiểm tra key. Kiểm tra mạng Internet.";
        }
    }

    // Sends one question and returns the JSON object in the answer. `progress`
    // receives `working` while Gemini answers, and a countdown if the free tier
    // asks to wait. Busy or briefly rate-limited replies are retried.
    public static async Task<JsonElement> AskJsonAsync(string system, string question, string working, IProgress<string> progress, CancellationToken cancellation)
    {
        var key = AiKeyStore.Gemini.Load();
        var throughServer = ThroughServer;
        if (!throughServer && key.Length == 0)
        {
            throw new AiException("Hãy nhập API key Gemini trước (nút 🔑), hoặc đăng nhập đồng bộ bằng Google.", needsKey: true);
        }

        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            progress.Report(working);
            try
            {
                return await AskOnceAsync(throughServer ? null : key, system, question, cancellation);
            }
            catch (ServerRefusedException refused)
            {
                _serverRefused = true;
                throughServer = false;
                if (key.Length == 0)
                {
                    throw new AiException($"{refused.Message} Hoặc nhập API key Gemini (nút 🔑).", needsKey: true);
                }

                // Asked again with the key, without counting this try.
                attempt--;
            }
            catch (HttpRequestException) when (throughServer && key.Length > 0)
            {
                // The study server is not running: the key works without it.
                throughServer = false;
                attempt--;
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
            ? await (StudyServer?.SendGeminiAsync(payload, cancellation) ?? Task.FromResult<HttpResponseMessage?>(null)) ?? throw new ServerRefusedException("Chưa đăng nhập đồng bộ bằng Google.")
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
            return new AiException("API key Gemini không hợp lệ hoặc không có quyền. Hãy kiểm tra lại key (nút 🔑).", needsKey: true);
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
