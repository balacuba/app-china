namespace ChineseLearning.Sync;

// One learner's study progress, as the desktop app and the website share it
// through the server. Only progress travels: the word list itself ships with
// both, and display settings stay on each device.
public sealed class ProgressDocument
{
    // Words picked for Flashcard and typing practice, in the order they were picked.
    public List<string> SelectedWords { get; set; } = [];

    // Words the learner has typed correctly at least once.
    public List<string> TypedCorrectWords { get; set; } = [];

    // Words typed wrong, with how many times; a word leaves once typed right.
    public Dictionary<string, int> MissedWords { get; set; } = [];

    // Typing practice answers since the learner last cleared the results.
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }

    public int BestGameScore { get; set; }

    // Settings; null = never set on any device, so each keeps its own default.
    public int? PracticeSeconds { get; set; }
    public double? FallSeconds { get; set; }
    public int? TextScale { get; set; }
    public int? PinyinScale { get; set; }

    public ProgressDocument Clone() => new()
    {
        SelectedWords = [.. SelectedWords],
        TypedCorrectWords = [.. TypedCorrectWords],
        MissedWords = new Dictionary<string, int>(MissedWords),
        CorrectAnswers = CorrectAnswers,
        WrongAnswers = WrongAnswers,
        BestGameScore = BestGameScore,
        PracticeSeconds = PracticeSeconds,
        FallSeconds = FallSeconds,
        TextScale = TextScale,
        PinyinScale = PinyinScale
    };
}

// What the server hands out and takes back: the document and its version,
// which goes up by one on every accepted save.
public sealed record ProgressEnvelope(int Version, ProgressDocument Progress);
