using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChineseLearningApp;

// A problem talking to the AI, with a message fit to show the learner.
// `NeedsKey` means the learner has to enter or replace their API key.
internal sealed class AiException(string message, bool needsKey = false, Exception? inner = null) : Exception(message, inner)
{
    public bool NeedsKey { get; } = needsKey;
}

// Google Gemini through its REST API, with the learner's own free API key from
// Google AI Studio. Every question asks for a JSON object back.
internal static partial class GeminiAi
{
    // Always the newest Flash model on the free tier, so the app need not change
    // when Google retires a model version.
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static bool HasKey => GeminiKey.Load().Length > 0;

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
        var key = GeminiKey.Load();
        if (key.Length == 0)
        {
            throw new AiException("Hãy nhập API key Gemini trước (nút 🔑 API key).", needsKey: true);
        }

        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            progress.Report(working);
            try
            {
                return await AskOnceAsync(key, system, question, cancellation);
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

    private static async Task<JsonElement> AskOnceAsync(string key, string system, string question, CancellationToken cancellation)
    {
        var payload = JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = question } } } },
            generationConfig = new { responseMimeType = "application/json", temperature = 1.0 }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", key);
        using var response = await Http.SendAsync(request, cancellation);
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
            return new AiException("API key Gemini không hợp lệ hoặc không có quyền. Hãy kiểm tra lại key (nút 🔑 API key).", needsKey: true);
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

    public static string Text(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
            : string.Empty;
}

// The learner's Gemini API key, kept in the app's data folder encrypted with
// Windows (DPAPI), so it can only be read back by the same Windows user.
internal static class GeminiKey
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        "gemini-key.bin");

    public static string Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser))
                : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return string.Empty;
        }
    }

    // An empty key removes the saved one.
    public static void Save(string key)
    {
        key = key.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        if (key.Length == 0)
        {
            File.Delete(FilePath);
            return;
        }

        File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    }
}
