using ChineseLearning.Sync;

namespace ChineseLearningApp;

// Syncing study progress with the study server (and through it the website):
// the "Đồng bộ" card in Settings, a sync when the app opens, a few seconds
// after progress changes, and a last quick one when it closes. Signed out, or
// with no server, the app works exactly as before.
public sealed partial class Form1
{
    private readonly SyncService _sync = new();
    private readonly System.Windows.Forms.Timer _syncDelay = new() { Interval = 5000 };
    private readonly CancellationTokenSource _syncClosing = new();
    private NumericUpDown? _practiceDurationInput;
    private Label? _syncStatus;
    private Action? _updateSyncCard;
    private bool _syncing;
    private bool _syncAgain;
    private bool _applyingSync;

    // Progress for words this PC's list does not have (added on another device):
    // kept and sent back unchanged, so a sync never reads them as removed here.
    private List<string> _foreignSelected = [];
    private List<string> _foreignTyped = [];
    private Dictionary<string, int> _foreignMissed = [];

    // Called from the constructor before the session state is saved on close.
    private void InitializeSync()
    {
        GeminiAi.StudyServer = _sync;
        _syncDelay.Tick += async (_, _) =>
        {
            _syncDelay.Stop();
            await RunSyncAsync();
        };
        Shown += async (_, _) => await RunSyncAsync();
        FormClosing += (_, _) => FinalSync();
        FormClosed += (_, _) =>
        {
            _syncDelay.Dispose();
            _syncClosing.Dispose();
        };
    }

    // A change to progress syncs a few seconds after the last one.
    private void ScheduleSync(int delay = 5000)
    {
        if (_applyingSync || !_sync.SignedIn || IsDisposed)
        {
            return;
        }

        _syncDelay.Stop();
        _syncDelay.Interval = delay;
        _syncDelay.Start();
    }

    private ProgressDocument CurrentProgress() => new()
    {
        SelectedWords = [.. _words.Where(word => word.IsSelected).Select(word => word.Chinese), .. _foreignSelected],
        TypedCorrectWords = [.. _typedCorrectWords, .. _foreignTyped],
        MissedWords = _incorrectWordCounts.Concat(_foreignMissed).ToDictionary(entry => entry.Key, entry => entry.Value),
        CorrectAnswers = _correctAnswerCount,
        WrongAnswers = _wrongAnswerCount,
        BestGameScore = _ninjaGame.BestScore,
        PracticeSeconds = _practiceDurationSeconds,
        FallSeconds = _ninjaGame.FallSeconds,
        TextScale = _ninjaGame.TextScale,
        PinyinScale = _ninjaGame.PinyinScale
    };

    // Puts synced progress into the app and its files (each Save* writes one).
    private void ApplyProgress(ProgressDocument progress, bool refreshScreens)
    {
        _applyingSync = true;
        try
        {
            var known = _words.Select(word => word.Chinese).ToHashSet(StringComparer.Ordinal);
            var selected = progress.SelectedWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectionChanged = false;
            foreach (var word in _words)
            {
                var pick = selected.Contains(word.Chinese);
                selectionChanged |= word.IsSelected != pick;
                word.IsSelected = pick;
            }

            _foreignSelected = progress.SelectedWords.Where(word => !known.Contains(word)).ToList();
            _foreignTyped = progress.TypedCorrectWords.Where(word => !known.Contains(word)).ToList();
            _foreignMissed = progress.MissedWords.Where(entry => !known.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            _typedCorrectWords.Clear();
            _typedCorrectWords.UnionWith(progress.TypedCorrectWords.Where(known.Contains));
            _incorrectWordCounts.Clear();
            foreach (var (chinese, count) in progress.MissedWords.Where(entry => known.Contains(entry.Key) && entry.Value > 0))
            {
                _incorrectWordCounts[chinese] = count;
            }

            _correctAnswerCount = progress.CorrectAnswers;
            _wrongAnswerCount = progress.WrongAnswers;
            _ninjaGame.BestScore = Math.Max(_ninjaGame.BestScore, progress.BestGameScore);
            if (progress.PracticeSeconds is int seconds and >= 1 and <= 300 && seconds != _practiceDurationSeconds)
            {
                if (_practiceDurationInput is not null)
                {
                    // Saves the setting and updates the hint, like a click on − / +.
                    _practiceDurationInput.Value = seconds;
                }
                else
                {
                    _practiceDurationSeconds = seconds;
                }
            }

            var gameChanged = false;
            if (progress.FallSeconds is double fall && Math.Abs(fall - _ninjaGame.FallSeconds) > 0.01)
            {
                _ninjaGame.FallSeconds = (float)Math.Clamp(fall, NinjaGame.MinFallSeconds, NinjaGame.MaxFallSeconds);
                gameChanged = true;
            }

            if (progress.TextScale is int scale && scale != _ninjaGame.TextScale)
            {
                _ninjaGame.TextScale = scale;
                gameChanged = true;
            }

            if (progress.PinyinScale is int pinyinScale && pinyinScale != _ninjaGame.PinyinScale)
            {
                _ninjaGame.PinyinScale = pinyinScale;
                gameChanged = true;
            }

            if (gameChanged)
            {
                SaveNinjaSettings();
            }

            if (selectionChanged)
            {
                if (refreshScreens)
                {
                    // Saves the ticks and restarts Flashcard and practice on the new words.
                    RefreshStudyWords();
                    _grid.Invalidate();
                }
                else
                {
                    SaveSelectedWords();
                }
            }

            UpdatePracticeScore();
        }
        finally
        {
            _applyingSync = false;
        }
    }

    private async Task RunSyncAsync(bool fromButton = false)
    {
        if (!_sync.SignedIn || IsDisposed)
        {
            return;
        }

        if (_syncing)
        {
            _syncAgain = true;
            return;
        }

        _syncing = true;
        ShowSyncStatus("⏳ Đang đồng bộ…", MutedTextColor);
        try
        {
            // The app's own website may still be starting.
            await StudyWebsite.Starting;
            var started = CurrentProgress();
            var synced = await _sync.SyncAsync(started, _syncClosing.Token);
            if (IsDisposed)
            {
                return;
            }

            // Anything studied while the sync ran is folded in and goes up next time.
            var final = ProgressMerge.Merge(started, CurrentProgress(), synced);
            ApplyProgress(final, refreshScreens: true);
            _sync.CommitBase(synced);
            if (!SyncService.Same(final, synced))
            {
                ScheduleSync(1000);
            }

            ShowSyncStatus($"✅ Đã đồng bộ lúc {DateTime.Now:HH:mm}", SuccessColor);
        }
        catch (SyncSignedOutException)
        {
            ShowSyncStatus("Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.", DangerText);
            _updateSyncCard?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            if (!_syncClosing.IsCancellationRequested)
            {
                ShowSyncStatus(fromButton
                    ? $"⚠ Chưa đồng bộ được: {ex.Message}"
                    : "⚠ Chưa đồng bộ được (không kết nối được máy chủ). Tiến độ vẫn được lưu trên máy.", DangerText);
            }
        }
        finally
        {
            _syncing = false;
            if (_syncAgain)
            {
                _syncAgain = false;
                ScheduleSync(500);
            }
        }
    }

    // On close: one quick sync (at most 3 seconds), saved into the files before
    // the app's state is written. If it does not finish, nothing is lost: the
    // next start syncs these changes.
    private void FinalSync()
    {
        _syncDelay.Stop();
        if (!_sync.SignedIn || _syncing)
        {
            return;
        }

        var started = CurrentProgress();
        var task = Task.Run(() => _sync.SyncAsync(started, CancellationToken.None));
        try
        {
            if (task.Wait(TimeSpan.FromSeconds(3)))
            {
                ApplyProgress(task.Result, refreshScreens: false);
                _sync.CommitBase(task.Result);
            }
        }
        catch (AggregateException)
        {
            // Offline or signed out: the changes stay here and sync next time.
        }
        finally
        {
            _syncClosing.Cancel();
        }
    }

    private void ShowSyncStatus(string text, Color colour)
    {
        if (_syncStatus is { IsDisposed: false })
        {
            _syncStatus.Text = text;
            _syncStatus.ForeColor = colour;
        }
    }

    // Settings card: signed out, the server address, name and password with
    // "Đăng nhập" / "Tạo tài khoản"; signed in, who and where, "Đồng bộ ngay"
    // and "Đăng xuất".
    private RoundedPanel BuildSyncSettingsCard()
    {
        TextBox Field(string text, bool password = false) => new()
        {
            Text = text,
            Width = 300,
            Font = new Font(UiFontName, 11F),
            UseSystemPasswordChar = password,
            Margin = new Padding(0, 2, 16, 8)
        };
        Label Caption(string text) => new()
        {
            AutoSize = true,
            Text = text,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = BodyTextColor,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 4, 0, 0)
        };

        var server = Field(_sync.ServerUrl);
        var userName = Field(_sync.UserName);
        var password = Field(string.Empty, password: true);
        var fields = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 2, BackColor = SurfaceColor, Margin = Padding.Empty };
        fields.Controls.Add(Caption("Địa chỉ máy chủ"), 0, 0);
        fields.Controls.Add(Caption("Tên đăng nhập"), 1, 0);
        fields.Controls.Add(Caption("Mật khẩu"), 2, 0);
        fields.Controls.Add(server, 0, 1);
        fields.Controls.Add(userName, 1, 1);
        fields.Controls.Add(password, 2, 1);

        var signedInLabel = new Label
        {
            AutoSize = true,
            Font = new Font(UiFontName, 11F),
            ForeColor = BodyTextColor,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 6, 0, 10)
        };
        _syncStatus = new Label
        {
            AutoSize = true,
            ForeColor = MutedTextColor,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 10, 0, 0),
            Text = "Đăng nhập cùng tài khoản trên web và app để đồng bộ từ đã chọn, từ gõ đúng/sai, điểm và cài đặt."
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = SurfaceColor, Margin = Padding.Empty };
        async Task SignIn(bool register)
        {
            if (userName.Text.Trim().Length == 0 || password.Text.Length == 0)
            {
                ShowSyncStatus("Hãy nhập tên đăng nhập và mật khẩu.", DangerText);
                return;
            }

            buttons.Enabled = false;
            ShowSyncStatus(register ? "⏳ Đang tạo tài khoản…" : "⏳ Đang đăng nhập…", MutedTextColor);
            var problem = await _sync.SignInAsync(server.Text, userName.Text, password.Text, register, _syncClosing.Token);
            buttons.Enabled = true;
            password.Clear();
            if (problem is not null)
            {
                ShowSyncStatus($"⚠ {problem}", DangerText);
                return;
            }

            _updateSyncCard?.Invoke();
            await RunSyncAsync(fromButton: true);
        }

        // Gmail: signs in on the website in the browser, which hands this PC a token.
        async Task SignInWithGoogle()
        {
            buttons.Enabled = false;
            ShowSyncStatus("⏳ Đã mở trình duyệt: đăng nhập bằng Google rồi bấm “Cho phép”…", MutedTextColor);
            var problem = await _sync.SignInThroughBrowserAsync(server.Text, _syncClosing.Token);
            buttons.Enabled = true;
            if (problem is not null)
            {
                ShowSyncStatus($"⚠ {problem}", DangerText);
                return;
            }

            Activate();
            _updateSyncCard?.Invoke();
            await RunSyncAsync(fromButton: true);

            // The learner's own Gemini key, made in their Google account, so
            // Dịch đoạn văn is ready without typing a key in.
            if (!GeminiAi.HasOwnKey && await GeminiAi.FetchKeyAsync(_syncClosing.Token) is null && !IsDisposed)
            {
                ShowSyncStatus("✅ Đã đồng bộ và lấy API key Gemini tự động từ tài khoản Google của bạn.", SuccessColor);
            }
        }

        var googleButton = CreateButton("G  Đăng nhập bằng Google", async (_, _) => await SignInWithGoogle(), secondary: true);
        var signInButton = CreateButton("🔑  Đăng nhập", async (_, _) => await SignIn(register: false));
        var registerButton = CreateButton("➕  Tạo tài khoản", async (_, _) => await SignIn(register: true), secondary: true);
        var syncButton = CreateButton("🔄  Đồng bộ ngay", async (_, _) => await RunSyncAsync(fromButton: true));
        var signOutButton = CreateButton("Đăng xuất", (_, _) =>
        {
            _sync.SignOut();
            _updateSyncCard?.Invoke();
            ShowSyncStatus("Đã đăng xuất. Tiến độ vẫn được lưu trên máy này.", MutedTextColor);
        }, secondary: true);
        buttons.Controls.AddRange([googleButton, signInButton, registerButton, syncButton, signOutButton]);

        _updateSyncCard = () =>
        {
            var signedIn = _sync.SignedIn;
            // A new sign-in may allow Gemini through the server again.
            GeminiAi.TryServerAgain();
            fields.Visible = !signedIn;
            signInButton.Visible = !signedIn;
            googleButton.Visible = !signedIn;
            registerButton.Visible = !signedIn;
            signedInLabel.Visible = signedIn;
            syncButton.Visible = signedIn;
            signOutButton.Visible = signedIn;
            signedInLabel.Text = $"Đã đăng nhập: {_sync.UserName}  ·  {_sync.ServerUrl}";
        };
        _updateSyncCard();

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = SurfaceColor,
            Padding = new Padding(0, 6, 0, 0)
        };
        body.Controls.AddRange([fields, signedInLabel, buttons, _syncStatus]);
        return CreateSettingsSection("☁", Color.FromArgb(224, 242, 254), "Đồng bộ với web",
            "Học trên web (máy chủ học tiếng Trung) và app cùng một tài khoản; tiến độ tự đồng bộ hai chiều.", body);
    }
}
