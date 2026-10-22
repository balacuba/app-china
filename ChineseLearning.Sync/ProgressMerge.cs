namespace ChineseLearning.Sync;

// Three-way merge of progress. Each device keeps the document as it was at its
// last sync (the base). To sync, it merges what changed on the device since
// then (local) into what is on the server now (remote), so neither side's
// changes are lost, even after studying offline. The website's sync.js does
// exactly the same; keep the two in step.
public static class ProgressMerge
{
    public static ProgressDocument Merge(ProgressDocument? baseline, ProgressDocument local, ProgressDocument remote)
    {
        // A device's first sync: its settings are only its defaults or old
        // choices, so the ones already on the server stand; it fills in the rest.
        if (baseline is null)
        {
            baseline = new ProgressDocument();
            local = local.Clone();
            local.PracticeSeconds = remote.PracticeSeconds ?? local.PracticeSeconds;
            local.FallSeconds = remote.FallSeconds ?? local.FallSeconds;
            local.TextScale = remote.TextScale ?? local.TextScale;
            local.PinyinScale = remote.PinyinScale ?? local.PinyinScale;
            baseline.PracticeSeconds = local.PracticeSeconds;
            baseline.FallSeconds = local.FallSeconds;
            baseline.TextScale = local.TextScale;
            baseline.PinyinScale = local.PinyinScale;
        }

        return new ProgressDocument
        {
            SelectedWords = MergeSet(baseline.SelectedWords, local.SelectedWords, remote.SelectedWords),
            TypedCorrectWords = MergeSet(baseline.TypedCorrectWords, local.TypedCorrectWords, remote.TypedCorrectWords),
            MissedWords = MergeCounts(baseline.MissedWords, local.MissedWords, remote.MissedWords),
            // Answers given on this device are added to the server's count; a
            // reset here (fewer than at the last sync) takes that many off.
            CorrectAnswers = Math.Max(0, remote.CorrectAnswers + local.CorrectAnswers - baseline.CorrectAnswers),
            WrongAnswers = Math.Max(0, remote.WrongAnswers + local.WrongAnswers - baseline.WrongAnswers),
            BestGameScore = Math.Max(local.BestGameScore, remote.BestGameScore),
            PracticeSeconds = MergeSetting(baseline.PracticeSeconds, local.PracticeSeconds, remote.PracticeSeconds),
            FallSeconds = MergeSetting(baseline.FallSeconds, local.FallSeconds, remote.FallSeconds),
            TextScale = MergeSetting(baseline.TextScale, local.TextScale, remote.TextScale),
            PinyinScale = MergeSetting(baseline.PinyinScale, local.PinyinScale, remote.PinyinScale)
        };
    }

    // The server's set, plus what this device added, minus what it removed.
    // Order: the server's order, then this device's additions in its order.
    public static List<string> MergeSet(IEnumerable<string> baseline, IEnumerable<string> local, IEnumerable<string> remote)
    {
        var before = baseline.ToHashSet(StringComparer.Ordinal);
        var now = local.ToHashSet(StringComparer.Ordinal);
        var removed = before.Where(item => !now.Contains(item)).ToHashSet(StringComparer.Ordinal);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in remote.Concat(local.Where(item => !before.Contains(item))))
        {
            if (!removed.Contains(item) && seen.Add(item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    // Per word: whichever side changed it wins. When both changed it, a word
    // still missed on either side stays missed, with the higher count.
    public static Dictionary<string, int> MergeCounts(
        IReadOnlyDictionary<string, int> baseline,
        IReadOnlyDictionary<string, int> local,
        IReadOnlyDictionary<string, int> remote)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in baseline.Keys.Concat(local.Keys).Concat(remote.Keys).Distinct(StringComparer.Ordinal))
        {
            int? before = baseline.TryGetValue(key, out var b) ? b : null;
            int? mine = local.TryGetValue(key, out var l) ? l : null;
            int? theirs = remote.TryGetValue(key, out var r) ? r : null;
            var merged = mine == before ? theirs
                : theirs == before ? mine
                : mine is null ? theirs
                : theirs is null ? mine
                : Math.Max(mine.Value, theirs.Value);
            if (merged is > 0)
            {
                result[key] = merged.Value;
            }
        }

        return result;
    }

    // A setting changed on this device since the last sync wins; otherwise the
    // server's stands (or this device's, when the server has none yet).
    public static T? MergeSetting<T>(T? baseline, T? local, T? remote) where T : struct =>
        !Nullable.Equals(local, baseline) ? local : remote ?? local;
}
