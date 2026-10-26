using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChineseLearningApp;

// A problem talking to the AI, with a message fit to show the learner.
// `NeedsKey` means Gemini cannot be used until the learner signs in with Google.
internal sealed class AiException(string message, bool needsKey = false, Exception? inner = null) : Exception(message, inner)
{
    public bool NeedsKey { get; } = needsKey;
}

// The AI behind the paragraph translation page: Google Gemini, on its free
// tier, with a key made automatically in the learner's Google account.
internal static class TutorAi
{
    public static bool HasKey => GeminiAi.HasOwnKey;

    // The learner's own key, or the study server's Google sign-in instead of one.
    public static bool CanAsk => HasKey || GeminiAi.ThroughServer;

    // Asks Gemini for a JSON object; the prompt describes the fields.
    public static Task<JsonElement> AskJsonAsync(string system, string question, string working, IProgress<string> progress, CancellationToken cancellation) =>
        GeminiAi.AskJsonAsync(system, question, working, progress, cancellation);

    public static string Text(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
            : string.Empty;
}

// A secret (a sign-in token, an API key), kept in the app's data folder
// encrypted with Windows (DPAPI), so it can only be read back by the same
// Windows user.
internal sealed class AiKeyStore(string fileName)
{
    // The Gemini key made in the learner's Google account, as "account\nkey".
    // (Keys typed in by hand were kept in gemini-key.bin, no longer used.)
    public static readonly AiKeyStore Gemini = new("gemini-auto-key.bin");

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
