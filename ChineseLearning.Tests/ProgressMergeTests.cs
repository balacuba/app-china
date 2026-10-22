using ChineseLearning.Sync;

namespace ChineseLearning.Tests;

public class ProgressMergeTests
{
    private static ProgressDocument Doc(params string[] selected) => new() { SelectedWords = [.. selected] };

    [Fact]
    public void FirstSyncAddsTheDevicesWordsToTheServers()
    {
        var merged = ProgressMerge.Merge(null, Doc("你", "好"), Doc("好", "是"));

        Assert.Equal(["好", "是", "你"], merged.SelectedWords);
    }

    [Fact]
    public void AWordRemovedOnOneSideStaysRemoved()
    {
        var baseline = Doc("你", "好", "是");
        var merged = ProgressMerge.Merge(baseline, Doc("你", "是"), Doc("你", "好", "是", "老师"));

        Assert.Equal(["你", "是", "老师"], merged.SelectedWords);
    }

    [Fact]
    public void BothSidesAddingAndRemovingKeepEachOthersChanges()
    {
        var baseline = Doc("一", "二", "三");
        var local = Doc("一", "三", "四");   // removed 二, added 四
        var remote = Doc("一", "二", "五"); // removed 三, added 五

        var merged = ProgressMerge.Merge(baseline, local, remote);

        Assert.Equal(["一", "五", "四"], merged.SelectedWords);
    }

    [Fact]
    public void AnswerCountsAddUpWhatEachSideDid()
    {
        var baseline = new ProgressDocument { CorrectAnswers = 10, WrongAnswers = 2 };
        var local = new ProgressDocument { CorrectAnswers = 13, WrongAnswers = 3 };
        var remote = new ProgressDocument { CorrectAnswers = 15, WrongAnswers = 2 };

        var merged = ProgressMerge.Merge(baseline, local, remote);

        Assert.Equal(18, merged.CorrectAnswers);
        Assert.Equal(3, merged.WrongAnswers);
    }

    [Fact]
    public void ClearingResultsOnADeviceTakesItsCountOffWithoutGoingNegative()
    {
        var baseline = new ProgressDocument { CorrectAnswers = 10 };
        var merged = ProgressMerge.Merge(baseline, new ProgressDocument(), new ProgressDocument { CorrectAnswers = 4 });

        Assert.Equal(0, merged.CorrectAnswers);
    }

    [Fact]
    public void BestScoreIsTheHigherOne()
    {
        var merged = ProgressMerge.Merge(
            new ProgressDocument { BestGameScore = 100 },
            new ProgressDocument { BestGameScore = 250 },
            new ProgressDocument { BestGameScore = 180 });

        Assert.Equal(250, merged.BestGameScore);
    }

    [Fact]
    public void MissedWordsFollowWhicheverSideChangedThem()
    {
        var baseline = new ProgressDocument { MissedWords = new() { ["你"] = 1, ["好"] = 2 } };
        // Here 你 was typed right (gone) and 是 was missed; on the server 好 was missed again.
        var local = new ProgressDocument { MissedWords = new() { ["好"] = 2, ["是"] = 1 } };
        var remote = new ProgressDocument { MissedWords = new() { ["你"] = 1, ["好"] = 3 } };

        var merged = ProgressMerge.Merge(baseline, local, remote);

        Assert.Equal(new Dictionary<string, int> { ["好"] = 3, ["是"] = 1 }, merged.MissedWords);
    }

    [Fact]
    public void AMissedWordChangedOnBothSidesKeepsTheHigherCount()
    {
        var merged = ProgressMerge.MergeCounts(
            new Dictionary<string, int> { ["你"] = 1 },
            new Dictionary<string, int> { ["你"] = 4 },
            new Dictionary<string, int> { ["你"] = 2 });

        Assert.Equal(4, merged["你"]);
    }

    [Fact]
    public void ASettingChangedHereWinsOtherwiseTheServersStands()
    {
        var baseline = new ProgressDocument { PracticeSeconds = 15, FallSeconds = 12 };
        var local = new ProgressDocument { PracticeSeconds = 20, FallSeconds = 12 };
        var remote = new ProgressDocument { PracticeSeconds = 15, FallSeconds = 8 };

        var merged = ProgressMerge.Merge(baseline, local, remote);

        Assert.Equal(20, merged.PracticeSeconds);
        Assert.Equal(8, merged.FallSeconds);
    }

    [Fact]
    public void ADevicesSettingsFillInWhenTheServerHasNone()
    {
        var merged = ProgressMerge.Merge(null, new ProgressDocument { TextScale = 120 }, new ProgressDocument());

        Assert.Equal(120, merged.TextScale);
    }

    [Fact]
    public void OnADevicesFirstSyncTheServersSettingsStand()
    {
        var merged = ProgressMerge.Merge(
            null,
            new ProgressDocument { PracticeSeconds = 15, TextScale = 100 },
            new ProgressDocument { PracticeSeconds = 120 });

        Assert.Equal(120, merged.PracticeSeconds);
        Assert.Equal(100, merged.TextScale);
    }

    [Fact]
    public void SyncingTwiceWithNoChangesChangesNothing()
    {
        var doc = new ProgressDocument
        {
            SelectedWords = ["你", "好"],
            MissedWords = new() { ["是"] = 2 },
            CorrectAnswers = 5,
            PracticeSeconds = 15
        };

        var merged = ProgressMerge.Merge(doc.Clone(), doc.Clone(), doc.Clone());

        Assert.Equal(doc.SelectedWords, merged.SelectedWords);
        Assert.Equal(doc.MissedWords, merged.MissedWords);
        Assert.Equal(5, merged.CorrectAnswers);
        Assert.Equal(15, merged.PracticeSeconds);
    }
}
