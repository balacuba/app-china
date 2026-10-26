using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChineseLearningServer;

// A Gemini API key made in the learner's own Google account, as Google AI
// Studio would make one: a Google Cloud project of theirs (labelled
// app=hoc-tieng-trung, made the first time) with the Gemini API switched on and
// a key that only works for Gemini. Gemini then runs on the learner's own free
// quota. It uses the Google access they gave when signing in (cloud-platform);
// the key is handed to them and never kept on this server.
public interface IGeminiKeyMaker
{
    // The learner's key, made (with its project) when they have none yet.
    Task<string> GetOrCreateAsync(string accessToken, CancellationToken cancellation);
}

// Google would not make the key; the message says why, fit to show the learner.
public sealed class GeminiKeyException(string message) : Exception(message);

public sealed class GoogleCloudKeys(HttpClient http) : IGeminiKeyMaker
{
    private const string Label = "hoc-tieng-trung";
    private const string KeyName = "Học tiếng Trung";
    private const string GeminiService = "generativelanguage.googleapis.com";
    private const string Projects = "https://cloudresourcemanager.googleapis.com/v1/";
    private const string ServiceUsage = "https://serviceusage.googleapis.com/v1/";
    private const string ApiKeys = "https://apikeys.googleapis.com/v2/";

    public async Task<string> GetOrCreateAsync(string accessToken, CancellationToken cancellation)
    {
        var project = await FindProjectAsync(accessToken, cancellation) ?? await CreateProjectAsync(accessToken, cancellation);

        // Switching on an API that is already on just says it is done.
        var enabling = await SendAsync(accessToken, HttpMethod.Post,
            $"{ServiceUsage}projects/{project}/services/{GeminiService}:enable", new { }, cancellation);
        await WaitAsync(accessToken, ServiceUsage, enabling, cancellation);

        var keys = $"{ApiKeys}projects/{project}/locations/global/keys";
        var listed = await SendAsync(accessToken, HttpMethod.Get, keys, null, cancellation);
        if (listed.TryGetProperty("keys", out var existing))
        {
            foreach (var key in existing.EnumerateArray())
            {
                if (key.TryGetProperty("displayName", out var name) && name.GetString() == KeyName &&
                    !key.TryGetProperty("deleteTime", out _) && key.TryGetProperty("name", out var id))
                {
                    var secret = await SendAsync(accessToken, HttpMethod.Get, $"{ApiKeys}{id.GetString()}/keyString", null, cancellation);
                    return KeyString(secret);
                }
            }
        }

        var making = await SendAsync(accessToken, HttpMethod.Post, keys, new
        {
            displayName = KeyName,
            restrictions = new { apiTargets = new[] { new { service = GeminiService } } }
        }, cancellation);
        var made = await WaitAsync(accessToken, ApiKeys, making, cancellation);
        return KeyString(made.TryGetProperty("response", out var response) ? response : made);
    }

    private async Task<string?> FindProjectAsync(string accessToken, CancellationToken cancellation)
    {
        var found = await SendAsync(accessToken, HttpMethod.Get,
            $"{Projects}projects?filter={Uri.EscapeDataString($"labels.app:{Label} lifecycleState:ACTIVE")}", null, cancellation);
        return found.TryGetProperty("projects", out var projects)
            ? projects.EnumerateArray().Select(project => project.GetProperty("projectId").GetString()).FirstOrDefault()
            : null;
    }

    private async Task<string> CreateProjectAsync(string accessToken, CancellationToken cancellation)
    {
        // Project IDs are unique across Google: the label plus six random letters or digits.
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var projectId = $"{Label}-{new string(RandomNumberGenerator.GetItems<char>(alphabet, 6))}";
        var creating = await SendAsync(accessToken, HttpMethod.Post, $"{Projects}projects", new
        {
            projectId,
            name = "Hoc tieng Trung",
            labels = new Dictionary<string, string> { ["app"] = Label }
        }, cancellation);
        await WaitAsync(accessToken, Projects, creating, cancellation);
        return projectId;
    }

    // Google makes projects and keys in the background: asks until it is done.
    private async Task<JsonElement> WaitAsync(string accessToken, string service, JsonElement operation, CancellationToken cancellation)
    {
        for (var asked = 0; ; asked++)
        {
            if (operation.TryGetProperty("error", out var error))
            {
                throw new GeminiKeyException(Explain(error.TryGetProperty("message", out var message) ? message.GetString() ?? "" : ""));
            }

            if (operation.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True ||
                !operation.TryGetProperty("name", out var name))
            {
                return operation;
            }

            if (asked == 60)
            {
                throw new GeminiKeyException("Google tạo API key quá lâu. Hãy thử lại sau ít phút.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellation);
            operation = await SendAsync(accessToken, HttpMethod.Get, $"{service}{name.GetString()}", null, cancellation);
        }
    }

    private async Task<JsonElement> SendAsync(string accessToken, HttpMethod method, string address, object? body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, cancellation);
        var reply = await response.Content.ReadFromJsonAsync<JsonElement>(cancellation);
        if (!response.IsSuccessStatusCode)
        {
            var message = reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("error", out var error) &&
                          error.TryGetProperty("message", out var text) ? text.GetString() ?? "" : "";
            throw new GeminiKeyException(Explain(message));
        }

        return reply;
    }

    private static string KeyString(JsonElement reply) =>
        reply.TryGetProperty("keyString", out var key) && key.GetString() is { Length: > 0 } text
            ? text
            : throw new GeminiKeyException("Google không trả về API key. Hãy thử lại.");

    // What the learner (or whoever runs the server) can do about Google's answer.
    private static string Explain(string message)
    {
        if (message.Contains("Terms of Service", StringComparison.OrdinalIgnoreCase))
        {
            return "Tài khoản Google này chưa đồng ý điều khoản Google Cloud. Mở console.cloud.google.com một lần, " +
                   "đồng ý điều khoản rồi thử lại.";
        }

        if (message.Contains("has not been used in project", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("is disabled", StringComparison.OrdinalIgnoreCase))
        {
            return "Máy chủ chưa bật đủ API trong Google Cloud (Cloud Resource Manager, Service Usage, API Keys). " +
                   "Xem HUONG-DAN-DANG-NHAP-GOOGLE.md.";
        }

        return $"Google không tạo được API key Gemini{(message.Length > 0 ? $": {message}" : ".")}";
    }
}
