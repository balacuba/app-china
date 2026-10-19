using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChineseLearningApp;

// A problem talking to the AI, with a message fit to show the learner.
internal sealed class FreeAiException(string message, Exception? inner = null) : Exception(message, inner);

// Pollinations' free text AI: an OpenAI-style chat endpoint that needs no account
// or key. Every question asks for a JSON object back.
//
// Without a key it answers one request every 30 seconds per IP address, so calls
// are spaced out here (with a countdown for the learner) and a "too soon" reply is
// waited out and tried again.
internal static partial class FreeAi
{
    private const string Endpoint = "https://text.pollinations.ai/openai";
    private static readonly TimeSpan Spacing = TimeSpan.FromSeconds(31);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private static DateTime _nextAllowed = DateTime.MinValue;

    // Sends one question and returns the JSON object in the answer. `progress`
    // receives short Vietnamese status lines while it waits, then `working` while
    // the AI answers.
    public static async Task<JsonElement> AskJsonAsync(string system, string question, string working, IProgress<string> progress, CancellationToken cancellation)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await WaitForTurnAsync(progress, cancellation);
            progress.Report(working);
            try
            {
                return await AskOnceAsync(system, question, cancellation);
            }
            catch (TooSoonException)
            {
                // The wait it asked for is already recorded in _nextAllowed.
                last = null;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or FreeAiException ||
                                       (ex is TaskCanceledException && !cancellation.IsCancellationRequested))
            {
                last = ex;
            }
        }

        throw last switch
        {
            null => new FreeAiException("AI miễn phí đang bận. Hãy thử lại sau ít phút."),
            TaskCanceledException => new FreeAiException("AI trả lời quá lâu. Hãy thử lại sau ít phút.", last),
            HttpRequestException => new FreeAiException("Không kết nối được AI. Kiểm tra mạng Internet rồi thử lại.", last),
            FreeAiException ai => ai,
            _ => new FreeAiException("AI trả lời không đúng dạng. Hãy thử lại.", last)
        };
    }

    // Counts down to the next request the free service will accept.
    private static async Task WaitForTurnAsync(IProgress<string> progress, CancellationToken cancellation)
    {
        while (DateTime.UtcNow < _nextAllowed)
        {
            var seconds = (int)Math.Ceiling((_nextAllowed - DateTime.UtcNow).TotalSeconds);
            progress.Report($"⏳ AI miễn phí chỉ trả lời 30 giây một lần, chờ {seconds} giây…");
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(1, (_nextAllowed - DateTime.UtcNow).TotalSeconds)), cancellation);
        }
    }

    private static async Task<JsonElement> AskOnceAsync(string system, string question, CancellationToken cancellation)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = "openai",
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = question }
            },
            response_format = new { type = "json_object" },
            // Free replies stop at about 1,500 tokens, thinking included, so keep the
            // thinking short to leave room for the answer.
            reasoning_effort = "low",
            seed = Random.Shared.Next(1, int.MaxValue)
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        string body;
        HttpStatusCode status;
        try
        {
            using var response = await Http.PostAsync(Endpoint, content, cancellation);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellation);
        }
        finally
        {
            _nextAllowed = DateTime.UtcNow + Spacing;
        }

        if (status is HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests)
        {
            // "Retry after 9s": wait that long (plus a moment) before the next try.
            var match = RetryAfter().Match(body);
            var seconds = match.Success ? int.Parse(match.Groups[1].Value) + 2 : (int)Spacing.TotalSeconds;
            _nextAllowed = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
            throw new TooSoonException();
        }

        if ((int)status >= 400)
        {
            throw new HttpRequestException($"AI replied {(int)status}", null, status);
        }

        // Read the reply defensively: an error object, or a reply cut short while the
        // model was still thinking (no "content"), is treated as a failed try.
        using var envelope = JsonDocument.Parse(body);
        var answer = envelope.RootElement.ValueKind == JsonValueKind.Object &&
                     envelope.RootElement.TryGetProperty("choices", out var choices) &&
                     choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
                     choices[0].TryGetProperty("message", out var message) &&
                     message.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new FreeAiException("AI chưa trả lời xong. Hãy thử lại.");
        }

        // Keep just the outermost { ... } in case the model wraps it in words or ``` fences.
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new FreeAiException("AI trả lời không đúng dạng. Hãy thử lại.");
        }

        using var json = JsonDocument.Parse(answer[start..(end + 1)]);
        return json.RootElement.Clone();
    }

    [GeneratedRegex(@"[Rr]etry after (\d+)\s*s")]
    private static partial Regex RetryAfter();

    private sealed class TooSoonException : Exception;

    public static string Text(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
            : string.Empty;
}
