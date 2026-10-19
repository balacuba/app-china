using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace ChineseLearningApp;

// Anthropic's Claude through the official SDK, with the learner's own API key from
// the Claude Console (paid by use; there is no free tier). Answers are forced into
// the JSON shape each question gives, so they need no clean-up.
internal static partial class ClaudeAi
{
    private const string ModelId = "claude-opus-5-5";

    // What Anthropic API keys look like: "sk-ant-api03-" and a long run of letters,
    // digits, - and _.
    [GeneratedRegex(@"^sk-ant-api\d\d-[A-Za-z0-9_\-]{60,}$")]
    public static partial Regex KeyShape();

    private static AnthropicClient Client(string key) => new() { ApiKey = key, Timeout = TimeSpan.FromSeconds(120) };

    // Asks Claude and returns its answer as a JSON object matching `schema`.
    // `progress` receives `working` while Claude answers.
    public static async Task<JsonElement> AskJsonAsync(
        string system, string question, Dictionary<string, JsonElement> schema,
        string working, IProgress<string> progress, CancellationToken cancellation)
    {
        var key = AiKeyStore.Claude.Load();
        if (key.Length == 0)
        {
            throw new AiException("Hãy nhập API key Claude trước (nút 🔑 API key).", needsKey: true);
        }

        progress.Report(working);
        try
        {
            var response = await Client(key).Beta.Messages.Create(new MessageCreateParams
            {
                Model = ModelId,
                MaxTokens = 16000,
                System = system,
                Messages = [new() { Role = Role.User, Content = question }],
                // A short paragraph or a marking needs little deliberation: low effort
                // keeps answers quick.
                OutputConfig = new BetaOutputConfig
                {
                    Effort = Effort.Low,
                    Format = new BetaJsonOutputFormat { Schema = schema }
                },
                // If a safety check declines the request, another model answers it
                // in the same call instead of the learner getting nothing.
                Betas = ["server-side-fallback-2026-06-01"],
                Fallbacks = new List<BetaFallbackParam> { new() { Model = "claude-opus-4-8" } }
            }, cancellation);

            if (response.StopReason == "refusal")
            {
                throw new AiException("Claude từ chối yêu cầu này. Hãy thử đoạn văn khác.");
            }

            var text = string.Concat(response.Content.Select(block => block.Value).OfType<BetaTextBlock>().Select(block => block.Text));
            using var json = JsonDocument.Parse(text);
            return json.RootElement.Clone();
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new AiException("API key Claude không hợp lệ. Hãy kiểm tra lại key (nút 🔑 API key).", needsKey: true, inner: ex);
        }
        catch (AnthropicForbiddenException ex)
        {
            throw new AiException("API key Claude không có quyền dùng model này.", needsKey: true, inner: ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new AiException("Claude đang giới hạn lượt gọi của tài khoản. Chờ một chút rồi thử lại.", inner: ex);
        }
        catch (AnthropicBadRequestException ex) when (ex.Message.Contains("credit", StringComparison.OrdinalIgnoreCase))
        {
            throw new AiException("Tài khoản Claude đã hết tiền. Hãy nạp thêm credit trong Claude Console.", inner: ex);
        }
        catch (AnthropicApiException ex)
        {
            throw new AiException($"Claude báo lỗi: {ex.Message}", inner: ex);
        }
        catch (JsonException ex)
        {
            throw new AiException("Claude trả lời không đúng dạng. Hãy thử lại.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or AnthropicIOException)
        {
            throw new AiException("Không kết nối được Claude. Kiểm tra mạng Internet rồi thử lại.", inner: ex);
        }
    }

    // Asks Anthropic whether a key works, without spending anything (it only lists
    // models). Returns null when it does, or a message saying why not.
    public static async Task<string?> CheckKeyAsync(string key, CancellationToken cancellation)
    {
        try
        {
            await Client(key).Models.List(cancellationToken: cancellation);
            return null;
        }
        catch (AnthropicUnauthorizedException)
        {
            return "Key không hợp lệ. Hãy sao chép lại key từ Claude Console.";
        }
        catch (AnthropicApiException ex)
        {
            return $"Anthropic từ chối key: {ex.Message}";
        }
        catch (Exception ex) when (ex is HttpRequestException or AnthropicIOException)
        {
            return "Không kết nối được Anthropic để kiểm tra key. Kiểm tra mạng Internet.";
        }
    }
}
