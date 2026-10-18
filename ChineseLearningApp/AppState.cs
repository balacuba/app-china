using System.Text;
using System.Text.Json;

namespace ChineseLearningApp;

// Study data from the last session that is not stored elsewhere: the user's
// name, the typing score and the best ninja game score. Only data is kept;
// the app always reopens on Home with its usual window and no filters. Saved on
// close, read at startup.
internal sealed class AppState
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        "app-state.json");

    public string UserName { get; set; } = string.Empty;
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int BestNinjaScore { get; set; }

    // A missing or unreadable file just means starting fresh.
    public static AppState Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new AppState()
                : new AppState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppState();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the session is not worth interrupting closing.
        }
    }
}
