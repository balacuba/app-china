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

// One AI service the translation page can use, with what the key box needs to
// help the learner get and check a key for it.
internal sealed record AiProvider(
    string Name,
    string KeyPage,
    string Steps,
    Func<Regex> KeyShape,
    Func<string, CancellationToken, Task<string?>> CheckKeyAsync,
    AiKeyStore Keys);

// The AI behind the paragraph translation page. Claude is used when a Claude key
// is saved (it is paid, so having one is a deliberate choice); otherwise Gemini,
// which has a free tier.
internal static class TutorAi
{
    public static readonly AiProvider Claude = new(
        "Claude (Anthropic)",
        "https://platform.claude.com/settings/keys",
        "1. Bấm nút dưới đây để mở Claude Console, đăng nhập tài khoản Anthropic của bạn.\n" +
        "2. Nạp credit (Claude API trả theo lượng dùng, không có gói miễn phí), rồi bấm “Create Key” và Copy.\n" +
        "3. Xong! App tự nhận key vừa copy, kiểm tra và lưu lại.",
        ClaudeAi.KeyShape,
        ClaudeAi.CheckKeyAsync,
        AiKeyStore.Claude);

    public static readonly AiProvider Gemini = new(
        "Gemini (Google)",
        "https://aistudio.google.com/app/apikey",
        "1. Bấm nút dưới đây để mở Google AI Studio, đăng nhập tài khoản Google của bạn.\n" +
        "2. Bấm “Create API key” (miễn phí), rồi bấm nút Copy bên cạnh key.\n" +
        "3. Xong! App tự nhận key vừa copy, kiểm tra và lưu lại.",
        GeminiAi.KeyShape,
        GeminiAi.CheckKeyAsync,
        AiKeyStore.Gemini);

    public static AiProvider Active => AiKeyStore.Claude.HasKey ? Claude : Gemini;

    public static bool HasKey => AiKeyStore.Claude.HasKey || AiKeyStore.Gemini.HasKey;

    // Short name for status lines, e.g. "⏳ Claude đang viết…".
    public static string ShortName => Active == Claude ? "Claude" : "Gemini";

    // Asks the active AI for a JSON object. Claude is held to `schema`; Gemini is
    // asked for JSON and the prompt describes the fields.
    public static Task<JsonElement> AskJsonAsync(
        string system, string question, Dictionary<string, JsonElement> schema,
        string working, IProgress<string> progress, CancellationToken cancellation) =>
        Active == Claude
            ? ClaudeAi.AskJsonAsync(system, question, schema, working, progress, cancellation)
            : GeminiAi.AskJsonAsync(system, question, working, progress, cancellation);

    // A JSON schema for an object whose fields are all required strings.
    public static Dictionary<string, JsonElement> StringObject(params string[] fields) => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(fields.ToDictionary(field => field, _ => new { type = "string" })),
        ["required"] = JsonSerializer.SerializeToElement(fields),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
    };

    public static string Text(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
            : string.Empty;
}

// An AI API key, kept in the app's data folder encrypted with Windows (DPAPI), so
// it can only be read back by the same Windows user.
internal sealed class AiKeyStore(string fileName)
{
    public static readonly AiKeyStore Claude = new("claude-key.bin");
    public static readonly AiKeyStore Gemini = new("gemini-key.bin");

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        fileName);

    public bool HasKey => Load().Length > 0;

    public string Load()
    {
        try
        {
            return File.Exists(_path)
                ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser))
                : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return string.Empty;
        }
    }

    // An empty key removes the saved one.
    public void Save(string key)
    {
        key = key.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (key.Length == 0)
        {
            File.Delete(_path);
            return;
        }

        File.WriteAllBytes(_path, ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    }
}
