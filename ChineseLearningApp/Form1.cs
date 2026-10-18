using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace ChineseLearningApp;

public sealed partial class Form1 : Form
{
    // Theme: one place for the app's colours and fonts.
    private static readonly Color Accent = Color.FromArgb(37, 99, 235);
    private static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
    private static readonly Color AccentPressed = Color.FromArgb(30, 64, 175);
    private static readonly Color AccentSoft = Color.FromArgb(232, 240, 254);
    private static readonly Color AccentSoftHover = Color.FromArgb(214, 228, 253);
    private static readonly Color DangerSoft = Color.FromArgb(254, 236, 236);
    private static readonly Color DangerSoftHover = Color.FromArgb(252, 218, 218);
    private static readonly Color DangerText = Color.FromArgb(185, 28, 28);
    private static readonly Color PageColor = Color.FromArgb(243, 245, 249);
    private static readonly Color SurfaceColor = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(226, 232, 240);
    private static readonly Color HeaderRowColor = Color.FromArgb(241, 245, 249);
    private static readonly Color StripeRowColor = Color.FromArgb(249, 250, 252);
    private static readonly Color TextColor = Color.FromArgb(30, 41, 59);
    private static readonly Color BodyTextColor = Color.FromArgb(51, 65, 85);
    private static readonly Color MutedTextColor = Color.FromArgb(100, 116, 139);
    private static readonly Color SuccessColor = Color.FromArgb(22, 128, 61);
    private const string UiFontName = "Segoe UI";

    private enum ButtonKind
    {
        Primary,
        Secondary,
        Danger
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _dataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        "vocabulary.json");
    private readonly string _columnSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        "column-settings.json");
    private readonly string _columnOrderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        "column-order.json");
    private readonly string _practiceSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        "practice-settings.json");
    // The ticked "Học" words, kept apart from vocabulary.json so ticking a box
    // does not rewrite the whole word list.
    private readonly string _selectedWordsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        "selected-words.json");
    // Off until the saved selection was read, so a failed read is never overwritten.
    private bool _saveSelectedWords;
    // Typing-practice words answered wrong (with how often), kept until answered right.
    private readonly string _missedWordsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        "missed-words.json");
    private bool _saveMissedWords;
    // Words whose latest typing-practice answer was right (HSK progress on Home).
    private readonly HashSet<string> _typedCorrectWords = new(StringComparer.Ordinal);
    private readonly string _typedCorrectWordsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        "typed-correct-words.json");
    private bool _saveTypedCorrectWords;
    private readonly Dictionary<string, bool> _columnVisibility = new(StringComparer.Ordinal);
    // Per-column text size in points; columns not listed use DefaultCellFontSize.
    private readonly Dictionary<string, float> _columnFontSizes = new(StringComparer.Ordinal);
    private const float DefaultCellFontSize = 11F;
    private const float MinimumCellFontSize = 8F;
    private const float MaximumCellFontSize = 28F;
    private readonly string _columnFontsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChineseLearningApp",
        "column-fonts.json");
    private readonly List<string> _columnOrder = new();
    private readonly Dictionary<string, int> _baseColumnWidths = new(StringComparer.Ordinal);
    private bool _fittingColumns;
    private readonly Dictionary<string, HashSet<string>> _columnFilters = new(StringComparer.Ordinal);
    private readonly BindingList<VocabularyWord> _words = new();
    private readonly BindingList<VocabularyWord> _visibleWords = new();
    private readonly List<VocabularyWord> _studyWords = new();
    private List<VocabularyWord>? _practiceReviewWords;
    private readonly Dictionary<string, int> _incorrectWordCounts = new(StringComparer.Ordinal);
    private readonly Random _random = new();
    private readonly List<int> _flashcardOrder = new();
    private readonly List<int> _questionOrder = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _searchBox = new();
    private readonly Panel _flashcardHost = new();
    private readonly RoundedPanel _flashcardCard = new();
    private readonly RoundedPanel _answerFrame = new();
    private readonly Panel _flashcardBackLayout = new();
    private readonly Label _flashcardFace = new();
    private readonly PillLabel _flashcardPinyin = new();
    private readonly FlashcardDetailsView _flashcardDetailsLeft = new();
    private readonly FlashcardDetailsView _flashcardDetailsRight = new();
    private readonly FlashcardDetailsView _flashcardDetailsBottom = new();
    private readonly Label _practiceMeaning = new();
    private readonly HighlightLabel _practiceEnglishMeaning = new();
    private readonly HighlightLabel _practiceFeedback = new();
    private readonly Label _practiceTimerLabel = new();
    private readonly Label _practiceScoreLabel = new();
    private readonly Label _practiceHint = new();
    private readonly RoundedButton _reviewIncorrectWordsButton = new();
    private readonly RoundedButton _pausePracticeButton = new();
    private readonly RoundedButton _skipPracticeButton = new();
    private string _lastAnswerText = string.Empty;
    private bool _typingSpeechFailed;
    private readonly TextBox _answerBox = new();
    private readonly System.Windows.Forms.Timer _practiceTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _flashcardFlipTimer = new() { Interval = 20 };
    private readonly Stopwatch _questionStopwatch = new();
    private object? _speechVoice;
    private object? _speechToken;
    private int? _speechLanguageId;
    private TabControl? _tabs;
    private TabPage? _practicePage;
    private int _flashcardIndex;
    private int _flashcardOrderPosition = -1;
    private int _questionIndex = -1;
    private int _questionOrderPosition;
    private int _flashcardAnimationFrame;
    private int _practiceDurationSeconds = 10;
    private int _correctAnswerCount;
    private int _wrongAnswerCount;
    private long _advanceAt;
    private long _pausedAdvanceRemaining;
    private bool _showingMeaning;
    private bool _answerChecked;
    private bool _practicePaused;
    private bool _restoringColumnSetting;
    private string? _editingVocabularyCellValue;
    private bool _refreshingVocabulary;
    private IReadOnlyList<VocabularyWord> PracticeWords => _practiceReviewWords ?? _studyWords;
    // Example sentences get one extra second per character to type.
    private long PracticeDurationMilliseconds =>
        _practiceDurationSeconds * 1000L +
        (_practiceExampleMode && CurrentPracticeWord is { } word
            ? NormalizePracticeAnswer(GetPracticeAnswer(word)).Length * 1000L
            : 0);
    private VocabularyWord? CurrentPracticeWord =>
        _questionIndex >= 0 && _questionIndex < PracticeWords.Count ? PracticeWords[_questionIndex] : null;
    private bool _practiceExampleMode;
    private readonly string _userName;
    private Label? _practiceTitle;

    public Form1(string userName)
    {
        InitializeComponent();
        _userName = userName;
        Text = $"Cùng học tiếng trung nhé '{userName}'";
        // Open at ~85% of the current screen's working area so the window suits
        // both small laptops and large monitors.
        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(Math.Min(760, workingArea.Width), Math.Min(560, workingArea.Height));
        Size = new Size(
            Math.Clamp((int)(workingArea.Width * 0.85), MinimumSize.Width, workingArea.Width),
            Math.Clamp((int)(workingArea.Height * 0.85), MinimumSize.Height, workingArea.Height));
        BackColor = PageColor;
        Font = new Font("Segoe UI", 10F);

        try
        {
            LoadVocabulary();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Shown += (_, _) => MessageBox.Show(
                this,
                $"Không thể đọc dữ liệu từ vựng đã lưu: {ex.Message}",
                "Lỗi đọc dữ liệu",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        try
        {
            LoadColumnVisibilitySettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Shown += (_, _) => MessageBox.Show(
                this,
                $"Không thể đọc cài đặt hiển thị cột: {ex.Message}",
                "Lỗi đọc cài đặt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        try
        {
            LoadColumnOrderSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Shown += (_, _) => MessageBox.Show(
                this,
                $"Không thể đọc thứ tự cột: {ex.Message}",
                "Lỗi đọc cài đặt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        try
        {
            LoadPracticeSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Shown += (_, _) => MessageBox.Show(
                this,
                $"Không thể đọc cài đặt thời gian luyện gõ: {ex.Message}",
                "Lỗi đọc cài đặt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        try
        {
            LoadColumnFontSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Shown += (_, _) => MessageBox.Show(
                this,
                $"Không thể đọc cỡ chữ các cột: {ex.Message}",
                "Lỗi đọc cài đặt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        try
        {
            LoadTypedCorrectWords();
            _saveTypedCorrectWords = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Shown += (_, _) => MessageBox.Show(
                this,
                $"Không thể đọc danh sách từ đã gõ đúng: {ex.Message}",
                "Lỗi đọc cài đặt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        try
        {
            LoadMissedWords();
            _saveMissedWords = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Shown += (_, _) => MessageBox.Show(
                this,
                $"Không thể đọc danh sách từ đã sai: {ex.Message}",
                "Lỗi đọc cài đặt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        try
        {
            LoadSelectedWords();
            _saveSelectedWords = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Shown += (_, _) => MessageBox.Show(
                this,
                $"Không thể đọc danh sách từ đã chọn học: {ex.Message}",
                "Lỗi đọc cài đặt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        BuildInterface();
        _practiceTimer.Tick += PracticeTimer_Tick;
        _flashcardFlipTimer.Tick += FlashcardFlipTimer_Tick;
        FormClosed += (_, _) =>
        {
            _practiceTimer.Dispose();
            _flashcardFlipTimer.Dispose();
            ReleaseSpeechVoice();
        };
        UpdatePracticeScore();
        RefreshVocabulary();
        RefreshStudyWords();
        AdjustColumnWidths();
    }

    // Sizes each text column to fit ~95% of its values (and its header), capped so
    // long example sentences do not stretch the grid; longer cells show a tooltip.
    // Pass a column to re-measure only that one (e.g. after its text size changed).
    private void AdjustColumnWidths(DataGridViewColumn? onlyColumn = null)
    {
        if (_words.Count == 0)
        {
            return;
        }

        var headerFont = _grid.ColumnHeadersDefaultCellStyle.Font ?? _grid.Font;
        var cellPadding = _grid.DefaultCellStyle.Padding.Horizontal + 12;
        var properties = TypeDescriptor.GetProperties(typeof(VocabularyWord));
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            if (column is not DataGridViewTextBoxColumn ||
                (onlyColumn is not null && column != onlyColumn) ||
                properties[column.DataPropertyName] is not { } property)
            {
                continue;
            }

            // Bigger text gets proportionally more room.
            var cellFont = column.DefaultCellStyle.Font ?? _grid.DefaultCellStyle.Font ?? _grid.Font;
            var scale = cellFont.Size / DefaultCellFontSize;
            var maxWidth = (int)(520 * scale);

            var values = _words
                .Select(word => Convert.ToString(property.GetValue(word), CultureInfo.CurrentCulture) ?? string.Empty)
                .OrderBy(value => value.Length)
                .ToList();
            // Short columns cannot wrap, so size them for their longest value.
            var typical = values[(int)((values.Count - 1) * (IsWrappingColumn(column.DataPropertyName) ? 0.95 : 1.0))];
            var contentWidth = TextRenderer.MeasureText(typical, cellFont).Width + cellPadding;
            var headerWidth = TextRenderer.MeasureText(column.HeaderText, headerFont).Width + 20;
            _baseColumnWidths[column.Name] = Math.Clamp(
                Math.Max(contentWidth, headerWidth),
                60,
                IsWrappingColumn(column.DataPropertyName) ? maxWidth : (int)(240 * scale));
        }

        FitColumnsToGrid();
    }

    // Text columns are scaled from their base widths so together they exactly fill
    // the grid (no sideways scrolling); each keeps at least MinimumFitColumnWidth.
    private const int MinimumFitColumnWidth = 60;

    private static bool IsWrappingColumn(string propertyName) =>
        propertyName is
            nameof(VocabularyWord.Vietnamese) or
            nameof(VocabularyWord.English) or
            nameof(VocabularyWord.EnglishExample) or
            nameof(VocabularyWord.ChineseExample) or
            nameof(VocabularyWord.VietnameseExample) or
            nameof(VocabularyWord.EnglishGrammar) or
            nameof(VocabularyWord.VietnameseGrammar);

    private void FitColumnsToGrid()
    {
        if (_baseColumnWidths.Count == 0 || _grid.ClientSize.Width <= 0)
        {
            return;
        }

        // Short columns (word, pinyin, level…) keep their natural width because their
        // text cannot wrap; only the long-text columns shrink or stretch.
        _fittingColumns = true;
        try
        {
            foreach (var column in _grid.Columns.Cast<DataGridViewColumn>()
                         .Where(column => _baseColumnWidths.ContainsKey(column.Name) && !IsWrappingColumn(column.Name)))
            {
                column.Width = _baseColumnWidths[column.Name];
            }
        }
        finally
        {
            _fittingColumns = false;
        }

        var columns = _grid.Columns.Cast<DataGridViewColumn>()
            .Where(column => column.Visible && _baseColumnWidths.ContainsKey(column.Name) && IsWrappingColumn(column.Name))
            .ToList();
        var fixedWidth = _grid.Columns.Cast<DataGridViewColumn>()
            .Where(column => column.Visible && !columns.Contains(column))
            .Sum(column => column.Width);
        // The grid has no scrollbars (it scrolls with the mouse wheel), so the
        // columns can use the full width.
        var available = _grid.ClientSize.Width - fixedWidth - 2;
        var baseTotal = columns.Sum(column => _baseColumnWidths[column.Name]);
        if (columns.Count == 0 || baseTotal <= 0)
        {
            return;
        }

        var target = Math.Max(available, columns.Count * MinimumFitColumnWidth);
        var widths = columns.ToDictionary(column => column.Name, column => (double)_baseColumnWidths[column.Name]);
        for (var pass = 0; pass < 5; pass++)
        {
            // Columns already at the minimum stay there; the rest share what is left.
            var flexible = widths.Where(pair => pair.Value > MinimumFitColumnWidth).Select(pair => pair.Key).ToList();
            var pinnedTotal = widths.Where(pair => pair.Value <= MinimumFitColumnWidth).Sum(pair => pair.Value);
            var flexibleTotal = flexible.Sum(name => widths[name]);
            if (flexibleTotal <= 0)
            {
                break;
            }

            var factor = (target - pinnedTotal) / flexibleTotal;
            if (Math.Abs(factor - 1) < 0.001)
            {
                break;
            }

            foreach (var name in flexible)
            {
                widths[name] = Math.Max(MinimumFitColumnWidth, widths[name] * factor);
            }
        }

        _fittingColumns = true;
        try
        {
            var assigned = 0;
            for (var i = 0; i < columns.Count; i++)
            {
                var width = i == columns.Count - 1
                    ? Math.Max(MinimumFitColumnWidth, target - assigned)
                    : (int)widths[columns[i].Name];
                columns[i].Width = width;
                assigned += width;
            }
        }
        finally
        {
            _fittingColumns = false;
        }
    }

    private void VocabularyGrid_ColumnWidthChanged(object? sender, DataGridViewColumnEventArgs e)
    {
        // A width the user drags becomes that column's new base width.
        if (!_fittingColumns && _baseColumnWidths.ContainsKey(e.Column.Name))
        {
            _baseColumnWidths[e.Column.Name] = e.Column.Width;
        }
    }

    private void BuildInterface()
    {
        var practicePage = BuildPracticePage();
        // Pages open from the Home cards; the header button returns to Home.
        var tabs = new PageHost { Dock = DockStyle.Fill, Font = new Font(UiFontName, 10.5F) };
        var vocabularyPage = BuildVocabularyPage();
        var flashcardPage = BuildFlashcardPage();
        var settingsPage = BuildColumnSettingsPage();
        // The app opens on Home, which leads to the other pages.
        tabs.TabPages.Add(BuildHomePage(tabs,
        [
            new HomeDestination(vocabularyPage, "📚", "Xem, tìm kiếm và tích chọn từ để học", Color.FromArgb(219, 234, 254)),
            new HomeDestination(flashcardPage, "🃏", "Lật thẻ để nhớ nghĩa, ví dụ và ngữ pháp", Color.FromArgb(252, 231, 243)),
            new HomeDestination(practicePage, "⌨", "Gõ lại từ và câu ví dụ bằng tiếng Trung", Color.FromArgb(220, 252, 231)),
            new HomeDestination(settingsPage, "⚙", "Nhập, thêm từ và chọn cột hiển thị", Color.FromArgb(254, 243, 199))
        ]));
        tabs.TabPages.Add(vocabularyPage);
        tabs.TabPages.Add(flashcardPage);
        tabs.TabPages.Add(practicePage);
        tabs.TabPages.Add(settingsPage);
        tabs.SelectedIndexChanged += PracticeTab_SelectedIndexChanged;
        _tabs = tabs;
        _practicePage = practicePage;
        Controls.Add(tabs);
        Controls.Add(BuildHeaderBar(tabs));
        tabs.BringToFront();
    }

    // Accent-coloured app bar: the Home button on the left and the app logo and
    // name centred.
    private Panel BuildHeaderBar(TabControl tabs)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Accent, Padding = new Padding(20, 0, 20, 0) };
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Accent };
        // Equal side columns keep the app name centred in the bar.
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var appName = new HeaderBrand
        {
            Anchor = AnchorStyles.None,
            Text = "Học tiếng Trung",
            Font = new Font("Segoe UI Semibold", 15F),
            Margin = new Padding(24, 0, 24, 0)
        };

        // A lighter-blue pill that returns to Home from any page.
        var homeButton = new RoundedButton();
        StyleButton(homeButton, ButtonKind.Primary);
        homeButton.BackColor = Color.FromArgb(59, 130, 246);
        homeButton.ForeColor = Color.White;
        homeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(96, 165, 250);
        homeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(29, 78, 216);
        homeButton.FlatAppearance.BorderColor = Color.FromArgb(147, 197, 253);
        homeButton.Font = new Font("Segoe UI Semibold", 10.5F);
        homeButton.AutoSize = false;
        homeButton.Size = new Size(190, 40);
        homeButton.Anchor = AnchorStyles.Left;
        homeButton.Margin = Padding.Empty;
        // The button goes straight back to Home (page 0); Home's cards open the other pages.
        homeButton.Click += (_, _) => tabs.SelectedIndex = 0;
        void SyncHomeButton()
        {
            // SelectedIndex is still -1 before the control has a handle.
            homeButton.Text = tabs.SelectedIndex <= 0 ? "🏠   Trang chủ" : "←   Về trang chủ";
        }

        tabs.SelectedIndexChanged += (_, _) => SyncHomeButton();
        SyncHomeButton();

        row.Controls.Add(homeButton, 0, 0);
        row.Controls.Add(appName, 1, 0);
        header.Controls.Add(row);
        return header;
    }

    private sealed record HomeDestination(TabPage Page, string Icon, string Description, Color BadgeColor);

    // Home: a greeting, a short summary of the word list and one big card per page.
    // Home: a greeting, study statistics, one big card per page and the study
    // progress for each HSK level. Everything is refreshed on every visit.
    private TabPage BuildHomePage(TabControl tabs, IReadOnlyList<HomeDestination> destinations)
    {
        var page = CreatePage("Trang chủ");
        var host = new Panel { Dock = DockStyle.Fill, BackColor = PageColor, AutoScroll = true };
        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 26F),
            ForeColor = TextColor,
            Text = $"Xin chào, {_userName}! 👋"
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Font = new Font(UiFontName, 13F),
            ForeColor = MutedTextColor,
            Text = "Hôm nay bạn muốn học gì?"
        };

        var totalCard = new StatCard { Icon = "📖", BadgeColor = Color.FromArgb(219, 234, 254), Caption = "Tổng từ vựng" };
        var studyingCard = new StatCard { Icon = "✅", BadgeColor = Color.FromArgb(220, 252, 231), Caption = "Đang học" };
        var missedCard = new StatCard { Icon = "❌", BadgeColor = Color.FromArgb(254, 226, 226), Caption = "Từ gõ sai" };
        var practiceCard = new StatCard { Icon = "🎯", BadgeColor = Color.FromArgb(254, 243, 199), Caption = "Luyện gõ phiên này" };
        var statCards = new List<StatCard> { totalCard, studyingCard, missedCard, practiceCard };
        var hskProgress = new HskProgressView();

        void UpdateStatistics()
        {
            var selected = _words.Count(word => word.IsSelected);
            totalCard.Value = $"{_words.Count}";
            totalCard.Note = "từ HSK 1 đến HSK 7-9";
            studyingCard.Value = $"{selected}";
            studyingCard.Note = _words.Count == 0 ? "Chưa có từ" : $"{selected * 100.0 / _words.Count:0.#}% tổng số từ";
            missedCard.Value = $"{_incorrectWordCounts.Count}";
            missedCard.Note = _incorrectWordCounts.Count == 0 ? "Không có từ nào sai 🎉" : "Gõ đúng để xóa khỏi danh sách";
            var answered = _correctAnswerCount + _wrongAnswerCount;
            practiceCard.Value = answered == 0 ? "—" : $"{_correctAnswerCount}/{answered}";
            practiceCard.Note = answered == 0 ? "Chưa luyện trong phiên này" : $"Chính xác {_correctAnswerCount * 100.0 / answered:0}%";
            statCards.ForEach(card => card.Invalidate());

            var levels = HskFilterLevels.Select(level => (Name: level, Words: _words.Where(word => word.HskLevel == level).ToList()))
                .Append((Name: "Khác", Words: _words.Where(word => !HskFilterLevels.Contains(word.HskLevel)).ToList()))
                .Where(level => level.Words.Count > 0)
                .Select(level => new HskProgressRow(level.Name, level.Words.Count,
                    level.Words.Count(word => _typedCorrectWords.Contains(word.Chinese))))
                .ToList();
            hskProgress.SetRows(levels);
        }

        var tiles = destinations.Select(destination =>
        {
            var tile = new HomeTile
            {
                Icon = destination.Icon,
                Title = destination.Page.Text,
                Description = destination.Description,
                BadgeColor = destination.BadgeColor
            };
            tile.Click += (_, _) => tabs.SelectedTab = destination.Page;
            return tile;
        }).ToList();

        host.Controls.Add(title);
        host.Controls.Add(subtitle);
        host.Controls.AddRange(statCards.ToArray());
        host.Controls.AddRange(tiles.ToArray());
        host.Controls.Add(hskProgress);

        // Centre everything; four cards per row when there is room, else two.
        void LayoutHome()
        {
            UpdateStatistics();
            const int tileWidth = 280;
            const int tileHeight = 190;
            const int statHeight = 104;
            const int gap = 24;
            var columns = host.ClientSize.Width >= 4 * tileWidth + 3 * gap + 80 ? 4 : 2;
            var gridWidth = columns * tileWidth + (columns - 1) * gap;
            int GridHeight(int count, int itemHeight)
            {
                var rows = (count + columns - 1) / columns;
                return rows * itemHeight + (rows - 1) * gap;
            }

            var statsHeight = GridHeight(statCards.Count, statHeight);
            var tilesHeight = GridHeight(tiles.Count, tileHeight);
            var progressHeight = hskProgress.GetPreferredHeight(gridWidth);
            var totalHeight = title.Height + 8 + subtitle.Height + 28 + statsHeight + gap + tilesHeight + gap + progressHeight;
            var scroll = host.AutoScrollPosition;
            var top = Math.Max(20, (host.ClientSize.Height - totalHeight) / 2) + scroll.Y;
            var centerX = host.ClientSize.Width / 2 + scroll.X;
            title.Location = new Point(centerX - title.Width / 2, top);
            top += title.Height + 8;
            subtitle.Location = new Point(centerX - subtitle.Width / 2, top);
            top += subtitle.Height + 28;
            var left = centerX - gridWidth / 2;
            void PlaceGrid(IReadOnlyList<Control> items, int itemHeight)
            {
                for (var index = 0; index < items.Count; index++)
                {
                    items[index].Bounds = new Rectangle(
                        left + index % columns * (tileWidth + gap),
                        top + index / columns * (itemHeight + gap),
                        tileWidth,
                        itemHeight);
                }
            }

            PlaceGrid(statCards, statHeight);
            top += statsHeight + gap;
            PlaceGrid(tiles, tileHeight);
            top += tilesHeight + gap;
            hskProgress.Bounds = new Rectangle(left, top, gridWidth, progressHeight);
            host.AutoScrollMinSize = new Size(0, totalHeight + 40);
        }

        host.Resize += (_, _) => LayoutHome();
        // The counts change while the user studies, so refresh them on every visit.
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab == page)
            {
                LayoutHome();
            }
        };
        page.Controls.Add(host);
        return page;
    }

    private sealed record HskProgressRow(string Level, int Total, int TypedCorrect);

    // A rounded white statistics card: emoji badge, a big value, a caption and
    // a muted note underneath.
    private sealed class StatCard : Control
    {
        private static readonly Font IconFont = new("Segoe UI Emoji", 15F);
        private static readonly Font ValueFont = new("Segoe UI Semibold", 20F);
        private static readonly Font CaptionFont = new("Segoe UI Semibold", 10F);
        private static readonly Font NoteFont = new(UiFontName, 9F);
        public string Icon { get; set; } = "";
        public Color BadgeColor { get; set; } = AccentSoft;
        public string Caption { get; set; } = "";
        public string Value { get; set; } = "";
        public string Note { get; set; } = "";

        public StatCard()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = PageColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = RoundedMenuRenderer.CreateRoundedPath(new RectangleF(1, 1, Width - 3, Height - 3), 18))
            using (var fill = new SolidBrush(SurfaceColor))
            using (var outline = new Pen(BorderColor, 1.5F))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(outline, path);
            }

            const int badge = 46;
            var badgeBounds = new Rectangle(18, (Height - badge) / 2, badge, badge);
            using (var badgeFill = new SolidBrush(BadgeColor))
            {
                graphics.FillEllipse(badgeFill, badgeBounds);
            }

            TextRenderer.DrawText(graphics, Icon, IconFont, badgeBounds, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var textLeft = badgeBounds.Right + 14;
            var textWidth = Width - textLeft - 12;
            TextRenderer.DrawText(graphics, Caption, CaptionFont, new Rectangle(textLeft, 14, textWidth, 20),
                MutedTextColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(graphics, Value, ValueFont, new Rectangle(textLeft, 32, textWidth, 38),
                TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(graphics, Note, NoteFont, new Rectangle(textLeft, 72, textWidth, 20),
                MutedTextColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    // A rounded card listing each HSK level with a rounded progress bar of how
    // many of its words were typed correctly in practice; two per row.
    private sealed class HskProgressView : Control
    {
        private const int HeaderHeight = 58;
        private const int RowHeight = 40;
        private const int Padding = 22;
        private static readonly Font TitleFont = new("Segoe UI Semibold", 13F);
        private static readonly Font LevelFont = new("Segoe UI Semibold", 10.5F);
        private static readonly Font CountFont = new(UiFontName, 9.5F);
        private static readonly Color[] BarColors =
        [
            Color.FromArgb(34, 197, 94), Color.FromArgb(20, 184, 166), Color.FromArgb(59, 130, 246),
            Color.FromArgb(99, 102, 241), Color.FromArgb(168, 85, 247), Color.FromArgb(236, 72, 153),
            Color.FromArgb(249, 115, 22), Color.FromArgb(148, 163, 184)
        ];
        private IReadOnlyList<HskProgressRow> _rows = [];

        public HskProgressView()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = PageColor;
        }

        public void SetRows(IReadOnlyList<HskProgressRow> rows)
        {
            _rows = rows;
            Invalidate();
        }

        public int GetPreferredHeight(int width) =>
            HeaderHeight + (_rows.Count + 1) / 2 * RowHeight + Padding;

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = RoundedMenuRenderer.CreateRoundedPath(new RectangleF(1, 1, Width - 3, Height - 3), 18))
            using (var fill = new SolidBrush(SurfaceColor))
            using (var outline = new Pen(BorderColor, 1.5F))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(outline, path);
            }

            TextRenderer.DrawText(graphics, "📊  Tiến độ luyện gõ theo cấp HSK  ·  số từ đã gõ đúng", TitleFont, new Rectangle(Padding, 16, Width - 2 * Padding, 28),
                TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            const int columnGap = 36;
            var columnWidth = (Width - 2 * Padding - columnGap) / 2;
            for (var index = 0; index < _rows.Count; index++)
            {
                var row = _rows[index];
                var left = Padding + index % 2 * (columnWidth + columnGap);
                var top = HeaderHeight + index / 2 * RowHeight;
                const int levelWidth = 78;
                const int countWidth = 150;
                TextRenderer.DrawText(graphics, row.Level, LevelFont, new Rectangle(left, top, levelWidth, 26),
                    TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                var track = new RectangleF(left + levelWidth, top + 8, columnWidth - levelWidth - countWidth - 10, 10);
                using (var trackPath = RoundedMenuRenderer.CreateRoundedPath(track, 5))
                using (var trackFill = new SolidBrush(Color.FromArgb(241, 245, 249)))
                {
                    graphics.FillPath(trackFill, trackPath);
                }

                if (row.Total > 0 && row.TypedCorrect > 0)
                {
                    var filled = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * row.TypedCorrect / row.Total), track.Height);
                    using var barPath = RoundedMenuRenderer.CreateRoundedPath(filled, 5);
                    using var barFill = new SolidBrush(BarColors[index % BarColors.Length]);
                    graphics.FillPath(barFill, barPath);
                }

                var percent = row.Total == 0 ? 0 : row.TypedCorrect * 100.0 / row.Total;
                TextRenderer.DrawText(graphics, $"{row.TypedCorrect}/{row.Total} đúng • {percent:0}%", CountFont,
                    new Rectangle(left + columnWidth - countWidth, top, countWidth, 26),
                    MutedTextColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }
    }

    private TabPage BuildVocabularyPage()
    {
        var page = CreatePage("Danh sách từ");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20)
        };
        // Import/add/delete live on the Settings page; this page is search,
        // HSK filter and the list.
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var searchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = Padding.Empty
        };
        searchRow.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "🔍 Tìm từ:",
            ForeColor = BodyTextColor,
            Margin = new Padding(0, 9, 8, 0)
        });
        _searchBox.Width = 320;
        _searchBox.Font = new Font(UiFontName, 11F);
        _searchBox.BorderStyle = BorderStyle.FixedSingle;
        _searchBox.PlaceholderText = "Tiếng Trung, pinyin, nghĩa, ví dụ...";
        _searchBox.Margin = new Padding(0, 4, 16, 0);
        _searchBox.TextChanged += (_, _) => RefreshVocabulary();
        searchRow.Controls.Add(_searchBox);
        // Select-all / clear-all live in the "Tiếng Trung" header filter menu.

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ShowCellToolTips = true;
        _grid.BackgroundColor = SurfaceColor;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.GridColor = BorderColor;
        _grid.ColumnHeadersHeight = 42;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ReadOnly = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _grid.CellClick += VocabularyGrid_CellClick;
        _grid.CellDoubleClick += VocabularyGrid_CellDoubleClick;
        _grid.CellPainting += VocabularyGrid_CellPainting;
        _grid.ColumnWidthChanged += VocabularyGrid_ColumnWidthChanged;
        _grid.Resize += (_, _) => FitColumnsToGrid();
        _grid.ColumnStateChanged += (_, e) =>
        {
            if (e.StateChanged == DataGridViewElementStates.Visible)
            {
                FitColumnsToGrid();
            }
        };
        _grid.ColumnHeaderMouseClick += VocabularyGrid_ColumnHeaderMouseClick;
        _grid.CellBeginEdit += VocabularyGrid_CellBeginEdit;
        _grid.CellEndEdit += VocabularyGrid_CellEndEdit;
        _grid.EditingControlShowing += VocabularyGrid_EditingControlShowing;
        _grid.CurrentCellDirtyStateChanged += VocabularyGrid_CurrentCellDirtyStateChanged;
        _grid.CellValueChanged += VocabularyGrid_CellValueChanged;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(VocabularyWord.IsSelected),
            HeaderText = "Học",
            Name = nameof(VocabularyWord.IsSelected),
            FillWeight = 7,
            MinimumWidth = 82,
            Width = 90,
            FlatStyle = FlatStyle.Standard,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(CreateColumn(nameof(VocabularyWord.Chinese), "Tiếng Trung"));
        _grid.Columns.Add(CreateColumn(nameof(VocabularyWord.HskLevel), "Cấp HSK"));
        _grid.Columns.Add(CreateColumn(nameof(VocabularyWord.Pinyin), "Pinyin"));
        _grid.Columns.Add(CreateColumn(nameof(VocabularyWord.Vietnamese), "Nghĩa tiếng Việt"));
        _grid.Columns.Add(CreateColumn(nameof(VocabularyWord.English), "Nghĩa tiếng Anh"));
        _grid.Columns.Add(CreateColumn(nameof(VocabularyWord.EnglishExample), "Ví dụ tiếng Anh"));
        _grid.Columns.Add(CreateColumn(nameof(VocabularyWord.ChineseExample), "Ví dụ tiếng Trung"));
        _grid.Columns.Add(CreateColumn(nameof(VocabularyWord.VietnameseExample), "Ví dụ tiếng Việt"));
        _grid.Columns.Add(CreateColumn(nameof(VocabularyWord.EnglishGrammar), "Ngữ pháp"));
        _grid.Columns.Add(CreateColumn(nameof(VocabularyWord.VietnameseGrammar), "Ngữ pháp tiếng Việt"));
        ApplyColumnOrder();
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            // The study tick column is hidden: words are picked from the Tiếng Trung header menu.
            column.Visible = column.Name != nameof(VocabularyWord.IsSelected) &&
                (!_columnVisibility.TryGetValue(column.Name, out var visible) || visible);
            _columnVisibility[column.Name] = column.Visible;
            column.Tag = column.HeaderText;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.FillWeight = 1;
            column.ReadOnly = column.DataPropertyName != nameof(VocabularyWord.IsSelected) &&
                !IsEditableTextColumn(column.DataPropertyName);
            ApplyColumnFont(column);
            if (IsFilterableColumn(column.DataPropertyName))
            {
                column.HeaderText = $"{column.HeaderText}  ▼";
            }
        }
        _grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderRowColor;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = BodyTextColor;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderRowColor;
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = BodyTextColor;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 10F);
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 6, 4, 6);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.DefaultCellStyle.Font = new Font(UiFontName, 11F);
        _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.DefaultCellStyle.BackColor = SurfaceColor;
        _grid.DefaultCellStyle.ForeColor = TextColor;
        _grid.DefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
        _grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
        _grid.DefaultCellStyle.SelectionForeColor = TextColor;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = StripeRowColor;
        _grid.RowTemplate.Height = 38;
        _grid.RowTemplate.MinimumHeight = 38;
        // Columns are squeezed to fit the view, so long text wraps and rows grow.
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
        // No scrollbar: the list scrolls freely with the mouse wheel instead.
        _grid.ScrollBars = ScrollBars.None;
        _grid.MouseWheel += VocabularyGrid_MouseWheel;
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Bấm tiêu đề “Tiếng Trung” để chọn từ muốn học (theo tìm kiếm và lọc HSK). Flashcard và luyện gõ chỉ dùng các từ đã chọn.",
            ForeColor = MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft
        };

        layout.Controls.Add(searchRow, 0, 0);
        layout.Controls.Add(BuildHskFilterRow(), 0, 1);
        layout.Controls.Add(CreateCard(_grid, Padding.Empty), 0, 2);
        layout.Controls.Add(hint, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    // Settings: four rounded section cards in a centred 2x2 grid (word management,
    // typing practice, visible columns, column order). Changes save automatically.
    private TabPage BuildColumnSettingsPage()
    {
        var page = CreatePage("Cài đặt");
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = PageColor };

        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = TextColor,
            Text = "⚙  Cài đặt",
            Margin = new Padding(10, 0, 0, 0)
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Font = new Font(UiFontName, 11F),
            ForeColor = MutedTextColor,
            Text = "Mọi thay đổi được lưu tự động.",
            Margin = new Padding(14, 0, 0, 0)
        };
        var heading = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = PageColor
        };
        heading.Controls.Add(title);
        heading.Controls.Add(subtitle);

        // Word management: import, add and delete all.
        var manageButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            BackColor = SurfaceColor,
            Padding = new Padding(0, 6, 0, 0)
        };
        manageButtons.Controls.Add(CreateButton("📥  Nhập CSV / TXT", ImportVocabulary));
        manageButtons.Controls.Add(CreateButton("➕  Thêm từ", AddWord));
        manageButtons.Controls.Add(CreateButton("🗑  Xóa toàn bộ", DeleteAllWords, danger: true));
        var manageCard = CreateSettingsSection("📦", Color.FromArgb(219, 234, 254), "Quản lý từ vựng",
            "Nhập danh sách từ từ tệp, thêm từ mới hoặc xóa toàn bộ.", manageButtons);

        // Typing practice: seconds per question with a − / + stepper. The hidden
        // NumericUpDown keeps the value, its limits and the save handler.
        var practiceDurationInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 300,
            Value = _practiceDurationSeconds,
            Visible = false
        };
        practiceDurationInput.ValueChanged += PracticeDurationInput_ValueChanged;
        var durationValue = new PillLabel
        {
            AutoSize = false,
            Size = new Size(130, 40),
            Font = new Font(UiFontName, 13F, FontStyle.Bold),
            ForeColor = Accent,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 10, 0)
        };
        void ShowDuration() => durationValue.Text = $"{practiceDurationInput.Value} giây";
        practiceDurationInput.ValueChanged += (_, _) => ShowDuration();
        ShowDuration();
        Button StepButton(string text, int step)
        {
            var button = CreateButton(text, (_, _) => practiceDurationInput.Value =
                Math.Clamp(practiceDurationInput.Value + step, practiceDurationInput.Minimum, practiceDurationInput.Maximum),
                secondary: true);
            button.AutoSize = false;
            button.Size = new Size(44, 40);
            button.Padding = Padding.Empty;
            button.Font = new Font(UiFontName, 14F, FontStyle.Bold);
            return button;
        }

        var stepper = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            BackColor = SurfaceColor,
            Padding = new Padding(0, 6, 0, 0)
        };
        stepper.Controls.Add(StepButton("−", -1));
        stepper.Controls.Add(durationValue);
        stepper.Controls.Add(StepButton("+", 1));
        stepper.Controls.Add(practiceDurationInput);
        stepper.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = MutedTextColor,
            Text = "Từ 1 đến 300 giây",
            Margin = new Padding(8, 11, 0, 0)
        });
        var practiceCard = CreateSettingsSection("⏱", Color.FromArgb(220, 252, 231), "Luyện gõ",
            "Thời gian cho mỗi câu khi luyện gõ.", stepper);

        // Visible columns: rounded checkboxes in two columns.
        var columnChecks = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = SurfaceColor };
        columnChecks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columnChecks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var checkIndex = 0;
        foreach (DataGridViewColumn column in _grid.Columns.Cast<DataGridViewColumn>()
                     .Where(column => column.Name != nameof(VocabularyWord.IsSelected)))
        {
            var checkBox = new RoundedCheckBox
            {
                AutoSize = true,
                Checked = column.Visible,
                Font = new Font("Segoe UI", 11F),
                ForeColor = BodyTextColor,
                Text = Convert.ToString(column.Tag, CultureInfo.CurrentCulture) ?? column.HeaderText,
                Tag = column.Name,
                Margin = new Padding(0, 6, 0, 6)
            };
            checkBox.CheckedChanged += ColumnSettingsCheckBox_CheckedChanged;
            columnChecks.Controls.Add(checkBox, checkIndex % 2, checkIndex / 2);
            checkIndex++;
        }

        var columnsCard = CreateSettingsSection("👁", Color.FromArgb(252, 231, 243), "Cột hiển thị",
            "Tích chọn cột muốn hiện trong danh sách từ.", columnChecks);

        // Column order: a numbered list with ▲ / ▼ buttons beside it.
        var orderList = new ListBox
        {
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            Font = new Font(UiFontName, 10.5F),
            ForeColor = BodyTextColor,
            FormattingEnabled = true,
            IntegralHeight = false,
            ItemHeight = 30,
            Dock = DockStyle.Fill
        };
        // Rows with a rounded accent pill for the selected column.
        orderList.DrawItem += (_, e) =>
        {
            if (e.Index < 0)
            {
                return;
            }

            var graphics = e.Graphics;
            using (var background = new SolidBrush(SurfaceColor))
            {
                graphics.FillRectangle(background, e.Bounds);
            }

            var selected = (e.State & DrawItemState.Selected) != 0;
            if (selected)
            {
                var smoothing = graphics.SmoothingMode;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var pill = new RectangleF(e.Bounds.Left + 2, e.Bounds.Top + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);
                using var path = RoundedMenuRenderer.CreateRoundedPath(pill, 8);
                using var fill = new SolidBrush(AccentSoft);
                graphics.FillPath(fill, path);
                graphics.SmoothingMode = smoothing;
            }

            var text = orderList.GetItemText(orderList.Items[e.Index]);
            using var font = new Font(orderList.Font, selected ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(graphics, $"{e.Index + 1}.  {text}", font,
                new Rectangle(e.Bounds.Left + 12, e.Bounds.Top, e.Bounds.Width - 16, e.Bounds.Height),
                selected ? Accent : BodyTextColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        // Show the plain column title (Tag), without the filter marks in the header.
        orderList.Format += (_, e) =>
        {
            if (e.ListItem is DataGridViewColumn column)
            {
                e.Value = Convert.ToString(column.Tag, CultureInfo.CurrentCulture) ?? column.HeaderText;
            }
        };
        RefreshColumnOrderList(orderList);
        var orderFrame = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6),
            Margin = new Padding(0, 4, 12, 0)
        };
        orderFrame.Controls.Add(orderList);
        var orderButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 4, 0, 0)
        };
        foreach (var (text, direction) in new[] { ("▲  Lên", -1), ("▼  Xuống", 1) })
        {
            var button = CreateButton(text, (_, _) => MoveSelectedColumn(orderList, direction), secondary: true);
            button.AutoSize = false;
            button.Size = new Size(120, 40);
            button.Margin = new Padding(0, 0, 0, 10);
            orderButtons.Controls.Add(button);
        }

        var orderBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = SurfaceColor };
        orderBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        orderBody.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        orderBody.Controls.Add(orderFrame, 0, 0);
        orderBody.Controls.Add(orderButtons, 1, 0);
        var orderCard = CreateSettingsSection("↕", Color.FromArgb(254, 243, 199), "Thứ tự cột",
            "Chọn một cột rồi bấm Lên / Xuống để đổi vị trí.", orderBody);

        // Text size per column: − / size / + for every text column, three per row.
        var textColumns = _grid.Columns.Cast<DataGridViewColumn>()
            .Where(column => column is DataGridViewTextBoxColumn)
            .OrderBy(column => column.DisplayIndex)
            .ToList();
        const int fontColumns = 3;
        var fontRows = (textColumns.Count + fontColumns - 1) / fontColumns;
        var fontGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = fontColumns, RowCount = fontRows + 1, BackColor = SurfaceColor };
        for (var column = 0; column < fontColumns; column++)
        {
            fontGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / fontColumns));
        }

        var fontRefreshers = new List<Action>();
        for (var index = 0; index < textColumns.Count; index++)
        {
            var column = textColumns[index];
            // Each column sits in its own light rounded box so its name and controls read as one.
            var itemFill = Color.FromArgb(248, 250, 252);
            var box = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Radius = 14,
                FillColor = itemFill,
                OutlineColor = BorderColor,
                BackColor = SurfaceColor,
                Margin = new Padding(0, 4, 14, 4),
                Padding = new Padding(14, 3, 8, 3)
            };
            var item = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, BackColor = itemFill, Margin = Padding.Empty };
            item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            item.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            item.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            item.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var name = new Label
            {
                Dock = DockStyle.Fill,
                Text = Convert.ToString(column.Tag, CultureInfo.CurrentCulture) ?? column.HeaderText,
                Font = new Font(UiFontName, 11F),
                ForeColor = BodyTextColor,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var value = new PillLabel
            {
                AutoSize = false,
                Size = new Size(70, 34),
                Font = new Font(UiFontName, 10.5F, FontStyle.Bold),
                ForeColor = Accent,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(6, 3, 6, 3)
            };
            void ShowSize() => value.Text = $"{GetColumnFontSize(column):0.#} pt";
            ShowSize();
            fontRefreshers.Add(ShowSize);
            Button SizeButton(string text, float step)
            {
                var button = CreateButton(text, (_, _) =>
                {
                    SetColumnFontSize(column, GetColumnFontSize(column) + step);
                    ShowSize();
                }, secondary: true);
                button.AutoSize = false;
                button.Size = new Size(36, 34);
                button.Padding = Padding.Empty;
                button.Margin = new Padding(0, 3, 0, 3);
                button.Font = new Font(UiFontName, 12F, FontStyle.Bold);
                return button;
            }

            item.Controls.Add(name, 0, 0);
            item.Controls.Add(SizeButton("−", -1), 1, 0);
            item.Controls.Add(value, 2, 0);
            item.Controls.Add(SizeButton("+", 1), 3, 0);
            box.Controls.Add(item);
            fontGrid.Controls.Add(box, index % fontColumns, index / fontColumns);
        }

        for (var row = 0; row < fontRows; row++)
        {
            fontGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        }

        fontGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var resetFonts = CreateButton("↺  Đặt lại cỡ chữ mặc định", (_, _) =>
        {
            foreach (var column in textColumns)
            {
                SetColumnFontSize(column, DefaultCellFontSize);
            }

            fontRefreshers.ForEach(refresh => refresh());
        }, secondary: true);
        resetFonts.Margin = new Padding(0, 12, 0, 0);
        fontGrid.Controls.Add(resetFonts, 0, fontRows);
        var fontsCard = CreateSettingsSection("🔠", Color.FromArgb(237, 233, 254), "Cỡ chữ từng cột",
            $"Phóng to hoặc thu nhỏ chữ của mỗi cột trong danh sách từ ({MinimumCellFontSize:0}–{MaximumCellFontSize:0} pt).", fontGrid);

        const int headingHeight = 72;
        const int topRowHeight = 168;
        const int bottomRowHeight = 470;
        var fontsRowHeight = 62 + fontRows * 54 + 52 + 34 + 20;
        var grid = new TableLayoutPanel { ColumnCount = 2, RowCount = 4, BackColor = PageColor };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, headingHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, topRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, bottomRowHeight));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, fontsRowHeight));
        grid.Controls.Add(heading, 0, 0);
        grid.SetColumnSpan(heading, 2);
        grid.Controls.Add(manageCard, 0, 1);
        grid.Controls.Add(practiceCard, 1, 1);
        grid.Controls.Add(columnsCard, 0, 2);
        grid.Controls.Add(orderCard, 1, 2);
        grid.Controls.Add(fontsCard, 0, 3);
        grid.SetColumnSpan(fontsCard, 2);
        grid.Height = headingHeight + topRowHeight + bottomRowHeight + fontsRowHeight;
        scroller.Controls.Add(grid);

        // Centred, at most 1180px wide; the panel scrolls when the window is short.
        void LayoutSettings()
        {
            var width = Math.Min(1180, Math.Max(700, scroller.ClientSize.Width - 40));
            grid.Width = width;
            grid.Location = new Point(
                Math.Max(0, (scroller.ClientSize.Width - width) / 2) + scroller.AutoScrollPosition.X,
                Math.Max(16, (scroller.ClientSize.Height - grid.Height) / 2) + scroller.AutoScrollPosition.Y);
        }

        scroller.Resize += (_, _) => LayoutSettings();
        LayoutSettings();
        page.Controls.Add(scroller);
        return page;
    }

    // A rounded white card with a pastel emoji badge, a title, a short
    // description and the section's controls below.
    private static RoundedPanel CreateSettingsSection(string icon, Color badgeColor, string title, string description, Control body)
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 18,
            FillColor = SurfaceColor,
            OutlineColor = BorderColor,
            Margin = new Padding(10),
            Padding = new Padding(22, 18, 22, 16)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = SurfaceColor };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new SectionHeader
        {
            Dock = DockStyle.Fill,
            Icon = icon,
            BadgeColor = badgeColor,
            Title = title,
            Description = description
        }, 0, 0);
        body.Margin = Padding.Empty;
        layout.Controls.Add(body, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private void RefreshColumnOrderList(ListBox list)
    {
        var selectedColumn = list.SelectedItem as DataGridViewColumn;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (DataGridViewColumn column in _grid.Columns.Cast<DataGridViewColumn>()
                     .Where(column => column.Name != nameof(VocabularyWord.IsSelected))
                     .OrderBy(column => column.DisplayIndex))
        {
            list.Items.Add(column);
        }

        if (selectedColumn is not null)
        {
            list.SelectedItem = selectedColumn;
        }

        if (list.SelectedIndex < 0 && list.Items.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        list.EndUpdate();
    }

    private void MoveSelectedColumn(ListBox list, int direction)
    {
        var selectedIndex = list.SelectedIndex;
        var targetIndex = selectedIndex + direction;
        if (selectedIndex < 0 || targetIndex < 0 || targetIndex >= list.Items.Count)
        {
            return;
        }

        var previousOrder = _grid.Columns.Cast<DataGridViewColumn>()
            .OrderBy(column => column.DisplayIndex)
            .Select(column => column.Name)
            .ToList();
        var movedColumn = (DataGridViewColumn)list.Items[selectedIndex]!;
        list.Items.RemoveAt(selectedIndex);
        list.Items.Insert(targetIndex, movedColumn);
        list.SelectedIndex = targetIndex;
        ApplyColumnOrderFromList(list);

        try
        {
            SaveColumnOrderSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ApplyColumnOrder(previousOrder);
            RefreshColumnOrderList(list);
            MessageBox.Show(
                this,
                $"Không thể lưu thứ tự cột: {ex.Message}",
                "Lỗi lưu cài đặt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void UpdatePracticeHint() =>
        _practiceHint.Text = _practiceExampleMode
            ? $"Mỗi câu có {_practiceDurationSeconds} giây, cộng thêm 1 giây cho mỗi chữ. Không cần gõ dấu câu. Nhấn Enter để kiểm tra."
            : $"Mỗi câu có {_practiceDurationSeconds} giây. Nhấn Enter để kiểm tra; sau đó câu tiếp theo sẽ hiện tự động.";

    // In example mode the learner types the word's Chinese example sentence.
    private string GetPracticeAnswer(VocabularyWord word) =>
        _practiceExampleMode && !string.IsNullOrWhiteSpace(word.ChineseExample)
            ? word.ChineseExample
            : word.Chinese;

    private (string Prompt, string Detail) GetPracticePrompt(VocabularyWord word)
    {
        if (!_practiceExampleMode || string.IsNullOrWhiteSpace(word.ChineseExample))
        {
            return (word.Vietnamese, FormatEnglishMeaning(word.English));
        }

        var prompt = string.IsNullOrWhiteSpace(word.VietnameseExample) ? word.EnglishExample : word.VietnameseExample;
        var pinyin = string.IsNullOrWhiteSpace(word.Pinyin) ? string.Empty : $" ({word.Pinyin})";
        return (prompt, $"Từ cần dùng: {word.Chinese}{pinyin} – {word.Vietnamese}");
    }

    private string FormatPracticeAnswer(VocabularyWord word)
    {
        if (_practiceExampleMode && !string.IsNullOrWhiteSpace(word.ChineseExample))
        {
            return word.ChineseExample;
        }

        return string.IsNullOrWhiteSpace(word.Pinyin) ? word.Chinese : $"{word.Chinese} ({word.Pinyin})";
    }

    // Sentences are compared without spaces and punctuation; words must match exactly.
    private bool IsPracticeAnswerCorrect(string typed, VocabularyWord word) =>
        _practiceExampleMode
            ? NormalizePracticeAnswer(typed) == NormalizePracticeAnswer(GetPracticeAnswer(word))
            : string.Equals(typed.Trim(), word.Chinese, StringComparison.Ordinal);

    private static string NormalizePracticeAnswer(string value) =>
        string.Concat(value.Where(character =>
            !char.IsWhiteSpace(character) && !char.IsPunctuation(character) && !char.IsSymbol(character)));

    private void SetPracticeExampleMode(bool exampleMode)
    {
        if (_practiceExampleMode == exampleMode)
        {
            return;
        }

        _practiceExampleMode = exampleMode;
        if (_practiceTitle is not null)
        {
            _practiceTitle.Text = exampleMode
                ? "Nhìn câu tiếng Việt, gõ lại câu ví dụ tiếng Trung"
                : "Nhìn nghĩa tiếng Việt và tiếng Anh, gõ từ tiếng Trung";
        }

        _practiceMeaning.Font = new Font("Segoe UI", exampleMode ? 18F : 24F, FontStyle.Bold);
        _answerFrame.Anchor = exampleMode ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.None;
        _answerFrame.Width = 430;
        UpdatePracticeHint();
        StartPracticeWithWords(PracticeWords.ToList());
    }

    private void ApplyColumnOrderFromList(ListBox list)
    {
        // The hidden study tick column stays first.
        var orderedNames = list.Items.Cast<DataGridViewColumn>().Select(column => column.Name)
            .Prepend(nameof(VocabularyWord.IsSelected)).ToList();
        ApplyColumnOrder(orderedNames);
    }

    private void ApplyColumnOrder()
    {
        var currentNames = _grid.Columns.Cast<DataGridViewColumn>()
            .OrderBy(column => column.DisplayIndex)
            .Select(column => column.Name)
            .ToList();
        var orderedNames = _columnOrder
            .Where(currentNames.Contains)
            .Distinct(StringComparer.Ordinal)
            .Concat(currentNames.Where(name => !_columnOrder.Contains(name, StringComparer.Ordinal)))
            .ToList();
        ApplyColumnOrder(orderedNames);
    }

    private void ApplyColumnOrder(IReadOnlyList<string> orderedNames)
    {
        for (var displayIndex = 0; displayIndex < orderedNames.Count; displayIndex++)
        {
            if (_grid.Columns[orderedNames[displayIndex]] is { } column)
            {
                column.DisplayIndex = displayIndex;
            }
        }

        _columnOrder.Clear();
        _columnOrder.AddRange(orderedNames);
    }

    private void LoadColumnOrderSettings()
    {
        if (!File.Exists(_columnOrderPath))
        {
            return;
        }

        var json = File.ReadAllText(_columnOrderPath, Encoding.UTF8);
        var savedOrder = JsonSerializer.Deserialize<List<string>>(json)
            ?? throw new InvalidDataException("Tệp thứ tự cột không chứa danh sách hợp lệ.");
        _columnOrder.AddRange(savedOrder);
    }

    private void SaveColumnOrderSettings()
    {
        var directory = Path.GetDirectoryName(_columnOrderPath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu cài đặt.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            _columnOrderPath,
            JsonSerializer.Serialize(_columnOrder, JsonOptions),
            new UTF8Encoding(false));
    }

    private void SetColumnVisibility(DataGridViewColumn column, CheckBox checkBox)
    {
        var previousVisibility = column.Visible;
        column.Visible = checkBox.Checked;
        _columnVisibility[column.Name] = column.Visible;

        try
        {
            SaveColumnVisibilitySettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            column.Visible = previousVisibility;
            _columnVisibility[column.Name] = previousVisibility;
            _restoringColumnSetting = true;
            checkBox.Checked = previousVisibility;
            _restoringColumnSetting = false;
            MessageBox.Show(
                this,
                $"Không thể lưu cài đặt hiển thị cột: {ex.Message}",
                "Lỗi lưu cài đặt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ColumnSettingsCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (!_restoringColumnSetting &&
            sender is CheckBox { Tag: string columnName } checkBox &&
            _grid.Columns[columnName] is { } column)
        {
            SetColumnVisibility(column, checkBox);
        }
    }

    private void LoadColumnVisibilitySettings()
    {
        if (!File.Exists(_columnSettingsPath))
        {
            return;
        }

        var json = File.ReadAllText(_columnSettingsPath, Encoding.UTF8);
        var savedSettings = JsonSerializer.Deserialize<Dictionary<string, bool>>(json)
            ?? throw new InvalidDataException("Tệp cài đặt không chứa danh sách cột hợp lệ.");
        foreach (var setting in savedSettings)
        {
            _columnVisibility[setting.Key] = setting.Value;
        }
    }

    private void SaveColumnVisibilitySettings()
    {
        var directory = Path.GetDirectoryName(_columnSettingsPath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu cài đặt.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            _columnSettingsPath,
            JsonSerializer.Serialize(_columnVisibility, JsonOptions),
            new UTF8Encoding(false));
    }

    private void LoadColumnFontSettings()
    {
        if (!File.Exists(_columnFontsPath))
        {
            return;
        }

        var json = File.ReadAllText(_columnFontsPath, Encoding.UTF8);
        var saved = JsonSerializer.Deserialize<Dictionary<string, float>>(json)
            ?? throw new InvalidDataException("Tệp cỡ chữ không chứa danh sách cột hợp lệ.");
        foreach (var (name, size) in saved)
        {
            _columnFontSizes[name] = Math.Clamp(size, MinimumCellFontSize, MaximumCellFontSize);
        }
    }

    private float GetColumnFontSize(DataGridViewColumn column) =>
        _columnFontSizes.TryGetValue(column.Name, out var size) ? size : DefaultCellFontSize;

    // Columns at the default size inherit the grid font; others get their own.
    private void ApplyColumnFont(DataGridViewColumn column)
    {
        var size = GetColumnFontSize(column);
        var old = column.DefaultCellStyle.Font;
        column.DefaultCellStyle.Font = Math.Abs(size - DefaultCellFontSize) < 0.01F ? null : new Font(UiFontName, size);
        old?.Dispose();
    }

    // Changes one column's text size, saves it and re-measures that column.
    private void SetColumnFontSize(DataGridViewColumn column, float size)
    {
        size = Math.Clamp(size, MinimumCellFontSize, MaximumCellFontSize);
        if (Math.Abs(size - DefaultCellFontSize) < 0.01F)
        {
            _columnFontSizes.Remove(column.Name);
        }
        else
        {
            _columnFontSizes[column.Name] = size;
        }

        ApplyColumnFont(column);
        AdjustColumnWidths(column);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_columnFontsPath)!);
            File.WriteAllText(_columnFontsPath, JsonSerializer.Serialize(_columnFontSizes, JsonOptions), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Không thể lưu cỡ chữ: {ex.Message}", "Lỗi lưu cài đặt",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadTypedCorrectWords()
    {
        if (!File.Exists(_typedCorrectWordsPath))
        {
            return;
        }

        var json = File.ReadAllText(_typedCorrectWordsPath, Encoding.UTF8);
        var saved = JsonSerializer.Deserialize<List<string>>(json)
            ?? throw new InvalidDataException("Tệp từ đã gõ đúng không chứa danh sách hợp lệ.");
        var known = _words.Select(word => word.Chinese).ToHashSet(StringComparer.Ordinal);
        _typedCorrectWords.UnionWith(saved.Where(known.Contains));
    }

    private void SaveTypedCorrectWords()
    {
        if (!_saveTypedCorrectWords)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_typedCorrectWordsPath)!);
            File.WriteAllText(_typedCorrectWordsPath, JsonSerializer.Serialize(_typedCorrectWords, JsonOptions), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Progress still counts for this session; it just will not be remembered.
        }
    }

    private void LoadMissedWords()
    {
        if (!File.Exists(_missedWordsPath))
        {
            return;
        }

        var json = File.ReadAllText(_missedWordsPath, Encoding.UTF8);
        var saved = JsonSerializer.Deserialize<Dictionary<string, int>>(json)
            ?? throw new InvalidDataException("Tệp từ đã sai không chứa danh sách hợp lệ.");
        var known = _words.Select(word => word.Chinese).ToHashSet(StringComparer.Ordinal);
        foreach (var (chinese, count) in saved.Where(entry => known.Contains(entry.Key) && entry.Value > 0))
        {
            _incorrectWordCounts[chinese] = count;
        }
    }

    private void SaveMissedWords()
    {
        if (!_saveMissedWords)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_missedWordsPath)!);
            File.WriteAllText(_missedWordsPath, JsonSerializer.Serialize(_incorrectWordCounts, JsonOptions), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The list still works for this session; it just will not be remembered.
        }
    }

    private void LoadSelectedWords()
    {
        if (!File.Exists(_selectedWordsPath))
        {
            return;
        }

        var json = File.ReadAllText(_selectedWordsPath, Encoding.UTF8);
        var saved = JsonSerializer.Deserialize<List<string>>(json)
            ?? throw new InvalidDataException("Tệp từ đã chọn không chứa danh sách hợp lệ.");
        var keys = saved.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var word in _words)
        {
            word.IsSelected = keys.Contains(word.Chinese);
        }
    }

    private void SaveSelectedWords()
    {
        if (!_saveSelectedWords)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_selectedWordsPath)!);
            var keys = _words.Where(word => word.IsSelected).Select(word => word.Chinese).ToList();
            File.WriteAllText(_selectedWordsPath, JsonSerializer.Serialize(keys, JsonOptions), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remembering the ticks is a convenience; studying goes on without it.
        }
    }

    private void LoadPracticeSettings()
    {
        if (!File.Exists(_practiceSettingsPath))
        {
            return;
        }

        var json = File.ReadAllText(_practiceSettingsPath, Encoding.UTF8);
        var savedDuration = JsonSerializer.Deserialize<int>(json);
        if (savedDuration is < 1 or > 300)
        {
            throw new InvalidDataException("Thời gian luyện gõ phải nằm trong khoảng 1 đến 300 giây.");
        }

        _practiceDurationSeconds = savedDuration;
    }

    private void PracticeDurationInput_ValueChanged(object? sender, EventArgs e)
    {
        if (sender is not NumericUpDown input)
        {
            return;
        }

        var previousDuration = _practiceDurationSeconds;
        _practiceDurationSeconds = decimal.ToInt32(input.Value);
        UpdatePracticeHint();
        try
        {
            var directory = Path.GetDirectoryName(_practiceSettingsPath)
                ?? throw new InvalidOperationException("Không xác định được thư mục lưu cài đặt.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                _practiceSettingsPath,
                JsonSerializer.Serialize(_practiceDurationSeconds, JsonOptions),
                new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _practiceDurationSeconds = previousDuration;
            input.Value = previousDuration;
            UpdatePracticeHint();
            MessageBox.Show(
                this,
                $"Không thể lưu thời gian luyện gõ: {ex.Message}",
                "Lỗi lưu cài đặt",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private TabPage BuildFlashcardPage()
    {
        var page = CreatePage("Flashcard");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 14));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 12));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 9));

        var title = CreateTitle("Nhấn vào thẻ để lật và xem nghĩa từ mới");
        title.Dock = DockStyle.Fill;
        _flashcardHost.Dock = DockStyle.Fill;
        _flashcardHost.BackColor = Color.Transparent;
        _flashcardHost.Resize += (_, _) => CenterFlashcardCard();
        // Rounded white card with an accent outline all around; the padding keeps
        // the content inside the rounded corners.
        _flashcardCard.Radius = 28;
        _flashcardCard.OutlineColor = Accent;
        _flashcardCard.OutlineWidth = 2.5F;
        _flashcardCard.Padding = new Padding(16);
        _flashcardCard.Cursor = Cursors.Hand;
        _flashcardCard.Click += ToggleFlashcard;
        _flashcardFace.BackColor = SurfaceColor;
        _flashcardBackLayout.BackColor = SurfaceColor;
        _flashcardFace.Dock = DockStyle.Fill;
        _flashcardFace.Font = new Font("Segoe UI", 16F);
        _flashcardFace.ForeColor = BodyTextColor;
        _flashcardFace.TextAlign = ContentAlignment.MiddleCenter;
        _flashcardFace.Cursor = Cursors.Hand;
        _flashcardFace.Click += ToggleFlashcard;
        _flashcardBackLayout.Dock = DockStyle.Fill;
        _flashcardBackLayout.Visible = false;
        _flashcardBackLayout.Resize += (_, _) => CenterFlashcardMeaningContent();
        _flashcardPinyin.AutoSize = false;
        _flashcardPinyin.Font = new Font("Segoe UI", 36F, FontStyle.Bold);
        _flashcardPinyin.ForeColor = Accent;
        _flashcardPinyin.TextAlign = ContentAlignment.MiddleCenter;
        _flashcardPinyin.Margin = Padding.Empty;
        _flashcardPinyin.Cursor = Cursors.Hand;
        _flashcardPinyin.Click += ToggleFlashcard;
        // Meanings sit side by side under the pinyin; the other blocks are single columns.
        _flashcardDetailsBottom.TwoColumnMinWidth = 300;
        foreach (var details in new[] { _flashcardDetailsLeft, _flashcardDetailsRight, _flashcardDetailsBottom })
        {
            details.Margin = Padding.Empty;
            details.MaxColumns = details == _flashcardDetailsBottom ? 2 : 1;
            details.RowGap = details == _flashcardDetailsBottom ? 10 : 24;
            details.Cursor = Cursors.Hand;
            details.Click += ToggleFlashcard;
            _flashcardBackLayout.Controls.Add(details);
        }

        _flashcardBackLayout.Click += ToggleFlashcard;
        _flashcardBackLayout.Controls.Add(_flashcardPinyin);
        _flashcardCard.Controls.Add(_flashcardBackLayout);
        _flashcardCard.Controls.Add(_flashcardFace);
        _flashcardHost.Controls.Add(_flashcardCard);
        CenterFlashcardCard();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.None,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.None,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        buttons.Controls.Add(CreateButton("🔊 Đọc từ", SpeakFlashcardWord));
        buttons.Controls.Add(CreateButton("Từ tiếp theo →", (_, _) => MoveFlashcard(1), secondary: true));

        var instruction = new Label
        {
            Text = "Nhấn vào thẻ để xem nghĩa hoặc nhấn “Đọc từ” để nghe phát âm tiếng Trung.",
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
            TextAlign = ContentAlignment.TopCenter
        };
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(_flashcardHost, 0, 1);
        layout.SetRowSpan(_flashcardHost, 2);
        layout.Controls.Add(buttons, 0, 3);
        layout.Controls.Add(instruction, 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildPracticePage()
    {
        var page = CreatePage("Luyện gõ");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            Padding = new Padding(32)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 11));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 11));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 9));

        var title = CreateTitle("Nhìn nghĩa tiếng Việt và tiếng Anh, gõ từ tiếng Trung");
        title.Dock = DockStyle.Fill;
        _practiceTitle = title;
        _practiceMeaning.Dock = DockStyle.Fill;
        _practiceMeaning.Font = new Font("Segoe UI", 24F, FontStyle.Bold);
        _practiceMeaning.ForeColor = Accent;
        _practiceMeaning.TextAlign = ContentAlignment.MiddleCenter;
        _practiceEnglishMeaning.Dock = DockStyle.Fill;
        _practiceEnglishMeaning.Font = new Font("Segoe UI", 15F);
        _practiceEnglishMeaning.ForeColor = MutedTextColor;
        _practiceEnglishMeaning.TextAlign = ContentAlignment.MiddleCenter;
        // The borderless answer box sits centred inside a rounded frame whose outline
        // turns accent while typing and grey when the box is disabled.
        _answerBox.Font = new Font("Microsoft YaHei UI", 22F);
        _answerBox.BorderStyle = BorderStyle.None;
        _answerBox.BackColor = SurfaceColor;
        _answerBox.ForeColor = TextColor;
        _answerBox.ImeMode = ImeMode.On;
        _answerBox.MaxLength = 100;
        _answerBox.TextAlign = HorizontalAlignment.Center;
        _answerFrame.Anchor = AnchorStyles.None;
        _answerFrame.Size = new Size(430, 60);
        _answerFrame.Radius = 12;
        _answerFrame.OutlineColor = Color.FromArgb(148, 163, 184);
        _answerFrame.Controls.Add(_answerBox);
        _answerFrame.Layout += (_, _) =>
        {
            const int sidePadding = 16;
            var height = _answerBox.PreferredHeight;
            _answerBox.Bounds = new Rectangle(
                sidePadding,
                Math.Max(0, (_answerFrame.ClientSize.Height - height) / 2),
                Math.Max(10, _answerFrame.ClientSize.Width - sidePadding * 2),
                height);
        };
        _answerFrame.Click += (_, _) => _answerBox.Focus();
        void UpdateAnswerFrame()
        {
            var background = _answerBox.Enabled ? SurfaceColor : Color.FromArgb(241, 245, 249);
            _answerBox.BackColor = background;
            _answerFrame.FillColor = background;
            _answerFrame.OutlineColor = !_answerBox.Enabled
                ? Color.FromArgb(203, 213, 225)
                : _answerBox.Focused ? Accent : Color.FromArgb(148, 163, 184);
            _answerFrame.OutlineWidth = _answerBox.Focused ? 2F : 1.5F;
            _answerFrame.Invalidate();
        }

        _answerBox.Enter += (_, _) => UpdateAnswerFrame();
        _answerBox.Leave += (_, _) => UpdateAnswerFrame();
        _answerBox.EnabledChanged += (_, _) => UpdateAnswerFrame();
        _answerBox.TextChanged += AnswerBox_TextChanged;
        _answerBox.KeyDown += AnswerBox_KeyDown;

        var continueStudyButton = CreateButton(
            "Học lại",
            (_, _) => StartPracticeWithWords(_words.ToList()),
            secondary: false);
        continueStudyButton.Anchor = AnchorStyles.None;
        _pausePracticeButton.Text = "Tạm dừng";
        _pausePracticeButton.Anchor = AnchorStyles.None;
        StyleButton(_pausePracticeButton, ButtonKind.Secondary);
        _pausePracticeButton.Click += PausePracticeButton_Click;
        _skipPracticeButton.Text = "Bỏ qua";
        _skipPracticeButton.Anchor = AnchorStyles.None;
        StyleButton(_skipPracticeButton, ButtonKind.Secondary);
        _skipPracticeButton.Click += SkipPracticeButton_Click;
        var practiceActions = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.None,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        // Mode picker: a rounded button (same look as the others) that opens a menu.
        string[] practiceModes = ["Gõ từ vựng", "Gõ câu ví dụ"];
        var practiceModeBox = new RoundedButton();
        StyleButton(practiceModeBox, ButtonKind.Secondary);
        Action<int>? checkPracticeMode = null;
        void SelectPracticeMode(int index)
        {
            practiceModeBox.Text = $"{practiceModes[index]}  ▾";
            checkPracticeMode?.Invoke(index);
            SetPracticeExampleMode(index == 1);
        }

        checkPracticeMode = AttachRoundedMenu(practiceModeBox, practiceModes, SelectPracticeMode);
        SelectPracticeMode(0);
        practiceActions.Controls.AddRange([continueStudyButton, _pausePracticeButton, _skipPracticeButton]);
        _practiceTimerLabel.Dock = DockStyle.Fill;
        _practiceTimerLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _practiceTimerLabel.ForeColor = Accent;
        _practiceTimerLabel.TextAlign = ContentAlignment.MiddleCenter;
        _practiceScoreLabel.Dock = DockStyle.Fill;
        _practiceScoreLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _practiceScoreLabel.TextAlign = ContentAlignment.MiddleCenter;
        _reviewIncorrectWordsButton.Text = "Xem từ đã sai";
        StyleButton(_reviewIncorrectWordsButton, ButtonKind.Secondary);
        _reviewIncorrectWordsButton.Click += ReviewIncorrectWordsButton_Click;
        _practiceFeedback.Dock = DockStyle.Fill;
        _practiceFeedback.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        _practiceFeedback.TextAlign = ContentAlignment.TopCenter;
        UpdatePracticeHint();
        _practiceHint.Dock = DockStyle.Fill;
        _practiceHint.ForeColor = MutedTextColor;
        _practiceHint.TextAlign = ContentAlignment.TopCenter;

        // Top row: title on the left, mode picker and missed-words button top right.
        var topRightTools = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        // Both top-right buttons share one fixed size so they line up.
        foreach (var button in new Button[] { practiceModeBox, _reviewIncorrectWordsButton })
        {
            button.AutoSize = false;
            button.Size = new Size(200, 40);
        }

        practiceModeBox.Margin = new Padding(0, 3, 0, 3);
        _reviewIncorrectWordsButton.Margin = new Padding(10, 3, 0, 3);
        topRightTools.Controls.AddRange([practiceModeBox, _reviewIncorrectWordsButton]);
        var topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        // Tools sit on their own row in the top-right corner; the title below gets
        // the full width so it stays on one centred line.
        topBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        topBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        topBar.Controls.Add(topRightTools, 0, 0);
        topBar.Controls.Add(title, 0, 1);

        layout.Controls.Add(topBar, 0, 0);
        layout.Controls.Add(_practiceMeaning, 0, 1);
        layout.Controls.Add(_practiceEnglishMeaning, 0, 2);
        layout.Controls.Add(_answerFrame, 0, 3);
        layout.Controls.Add(practiceActions, 0, 4);
        layout.Controls.Add(_practiceTimerLabel, 0, 5);
        layout.Controls.Add(_practiceScoreLabel, 0, 6);
        layout.Controls.Add(_practiceFeedback, 0, 8);
        layout.Controls.Add(_practiceHint, 0, 9);
        page.Padding = new Padding(20);
        page.Controls.Add(CreateCard(layout, new Padding(8)));
        return page;
    }

    private void LoadVocabulary()
    {
        if (File.Exists(_dataPath))
        {
            var json = File.ReadAllText(_dataPath, Encoding.UTF8);
            var saved = JsonSerializer.Deserialize<List<VocabularyWord>>(json)
                ?? throw new InvalidDataException("Tệp từ vựng không chứa danh sách hợp lệ.");
            var starterEnglish = StarterWords().ToDictionary(word => word.Chinese, word => word.English);
            var migrated = false;
            foreach (var word in saved.Where(IsValid))
            {
                migrated |= MoveLegacyNotes(word);
                if (string.IsNullOrWhiteSpace(word.English) &&
                    starterEnglish.TryGetValue(word.Chinese, out var english))
                {
                    word.English = english;
                    migrated = true;
                }

                _words.Add(word);
            }

            if (migrated)
            {
                SaveWords(_words.ToList());
            }

            return;
        }

        foreach (var word in StarterWords())
        {
            _words.Add(word);
        }

        SaveWords(_words.ToList());
    }

    private void SaveWords(IReadOnlyCollection<VocabularyWord> words)
    {
        var directory = Path.GetDirectoryName(_dataPath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu dữ liệu.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            _dataPath,
            JsonSerializer.Serialize(words, JsonOptions),
            new UTF8Encoding(false));
    }

    private void ImportVocabulary(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn danh sách từ vựng",
            Filter = "Danh sách từ vựng (*.csv;*.txt;*.xlsx)|*.csv;*.txt;*.xlsx|Excel (*.xlsx)|*.xlsx|CSV hoặc TXT (*.csv;*.txt)|*.csv;*.txt|Mọi tệp (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var imported = string.Equals(
                Path.GetExtension(dialog.FileName),
                ".xlsx",
                StringComparison.OrdinalIgnoreCase)
                ? ParseExcelFile(dialog.FileName)
                : ParseFile(dialog.FileName);
            var updated = _words.ToList();
            var knownWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < updated.Count; index++)
            {
                knownWords.TryAdd(updated[index].Chinese.Trim(), index);
            }

            var importedCount = 0;
            var enrichedCount = 0;
            var duplicateCount = 0;
            foreach (var word in imported)
            {
                var key = word.Chinese.Trim();
                if (knownWords.TryGetValue(key, out var existingIndex))
                {
                    var merged = MergeExamplesAndGrammar(updated[existingIndex], word);
                    if (merged is null)
                    {
                        duplicateCount++;
                    }
                    else
                    {
                        updated[existingIndex] = merged;
                        enrichedCount++;
                    }

                    continue;
                }

                knownWords.Add(key, updated.Count);
                updated.Add(word);
                importedCount++;
            }

            if ((importedCount > 0 || enrichedCount > 0) && !SaveAndReplace(updated))
            {
                return;
            }

            MessageBox.Show(
                this,
                $"Đã nhập {importedCount} từ vựng mới. " +
                $"Bổ sung ví dụ/cấu trúc cho {enrichedCount} từ đã có. " +
                $"Bỏ qua {duplicateCount} từ trùng lặp.",
                "Kết quả nhập từ vựng",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            FormatException or
            InvalidDataException or
            System.Xml.XmlException)
        {
            MessageBox.Show(
                this,
                GetImportErrorMessage(dialog.FileName, ex),
                "Lỗi nhập từ vựng",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static VocabularyWord? MergeExamplesAndGrammar(VocabularyWord existing, VocabularyWord imported)
    {
        var merged = new VocabularyWord(
            existing.Chinese,
            existing.Pinyin,
            existing.Vietnamese,
            existing.English,
            existing.Example,
            existing.Grammar,
            FillIfEmpty(existing.EnglishExample, imported.EnglishExample),
            FillIfEmpty(existing.ChineseExample, imported.ChineseExample),
            FillIfEmpty(existing.EnglishGrammar, imported.EnglishGrammar),
            FillIfEmpty(existing.ChineseGrammar, imported.ChineseGrammar))
        {
            VietnameseGrammar = FillIfEmpty(existing.VietnameseGrammar, imported.VietnameseGrammar),
            VietnameseExample = FillIfEmpty(existing.VietnameseExample, imported.VietnameseExample),
            HskLevel = existing.HskLevel,
            IsSelected = existing.IsSelected
        };

        var changed =
            merged.EnglishExample != existing.EnglishExample ||
            merged.ChineseExample != existing.ChineseExample ||
            merged.EnglishGrammar != existing.EnglishGrammar ||
            merged.VietnameseGrammar != existing.VietnameseGrammar ||
            merged.VietnameseExample != existing.VietnameseExample ||
            merged.ChineseGrammar != existing.ChineseGrammar;
        return changed ? merged : null;

        static string FillIfEmpty(string current, string incoming) =>
            string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(incoming)
                ? incoming.Trim()
                : current;
    }

    private static List<VocabularyWord> ParseFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        var firstLine = lines.FindIndex(line => !string.IsNullOrWhiteSpace(line));
        if (firstLine < 0)
        {
            throw new FormatException("Tệp không có dữ liệu.");
        }

        var firstFields = SplitLine(lines[firstLine]);
        var hasHeader = firstFields.Any(field => HeaderType(field) is not null);
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        if (hasHeader)
        {
            for (var i = 0; i < firstFields.Length; i++)
            {
                var type = HeaderType(firstFields[i]);
                if (type is not null)
                {
                    headers[type] = i;
                }
            }

            if (!headers.ContainsKey("chinese") || !headers.ContainsKey("vietnamese"))
            {
                throw new FormatException("Dòng đầu phải có cột Tiếng Trung và Nghĩa tiếng Việt.");
            }
        }

        var result = new List<VocabularyWord>();
        var start = hasHeader ? firstLine + 1 : firstLine;
        for (var i = start; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var fields = SplitLine(lines[i]);
            string chinese;
            string pinyin;
            string vietnamese;
            string english;
            string legacyExample;
            string legacyGrammar;
            string englishExample;
            string chineseExample;
            string englishGrammar;
            string chineseGrammar;
            if (hasHeader)
            {
                chinese = GetField(fields, headers["chinese"]);
                vietnamese = GetField(fields, headers["vietnamese"]);
                pinyin = headers.TryGetValue("pinyin", out var index) ? GetField(fields, index) : string.Empty;
                english = headers.TryGetValue("english", out var englishIndex)
                    ? GetField(fields, englishIndex)
                    : string.Empty;
                legacyExample = headers.TryGetValue("example", out var exampleIndex)
                    ? GetField(fields, exampleIndex)
                    : string.Empty;
                legacyGrammar = headers.TryGetValue("grammar", out var grammarIndex)
                    ? GetField(fields, grammarIndex)
                    : string.Empty;
                englishExample = GetHeaderField(fields, headers, "englishExample");
                chineseExample = GetHeaderField(fields, headers, "chineseExample");
                englishGrammar = GetHeaderField(fields, headers, "englishGrammar");
                chineseGrammar = GetHeaderField(fields, headers, "chineseGrammar");
            }
            else
            {
                var chineseIndex = Array.FindIndex(fields, ContainsChinese);
                if (chineseIndex < 0)
                {
                    throw new FormatException($"Dòng {i + 1}: không tìm thấy từ tiếng Trung.");
                }

                chinese = fields[chineseIndex];
                var remainder = fields.Where((_, index) => index != chineseIndex).ToArray();
                pinyin = remainder.Length > 1 ? remainder[0] : string.Empty;
                vietnamese = remainder.Length > 1 ? remainder[1] : remainder.FirstOrDefault() ?? string.Empty;
                english = remainder.Length > 2 ? remainder[2] : string.Empty;
                legacyExample = remainder.Length > 3 ? remainder[3] : string.Empty;
                legacyGrammar = remainder.Length > 4 ? remainder[4] : string.Empty;
                var hasSeparatedNotes = remainder.Length >= 7;
                englishExample = hasSeparatedNotes && remainder.Length > 3 ? remainder[3] : string.Empty;
                chineseExample = hasSeparatedNotes && remainder.Length > 4 ? remainder[4] : string.Empty;
                englishGrammar = hasSeparatedNotes && remainder.Length > 5 ? remainder[5] : string.Empty;
                chineseGrammar = hasSeparatedNotes && remainder.Length > 6 ? remainder[6] : string.Empty;
                if (hasSeparatedNotes)
                {
                    legacyExample = string.Empty;
                    legacyGrammar = string.Empty;
                }
            }

            if (!ContainsChinese(chinese) || string.IsNullOrWhiteSpace(vietnamese))
            {
                throw new FormatException($"Dòng {i + 1}: cần có từ tiếng Trung và nghĩa tiếng Việt.");
            }

            var word = new VocabularyWord(
                chinese.Trim(),
                pinyin.Trim(),
                vietnamese.Trim(),
                english.Trim(),
                legacyExample.Trim(),
                legacyGrammar.Trim(),
                englishExample.Trim(),
                chineseExample.Trim(),
                englishGrammar.Trim(),
                chineseGrammar.Trim())
            {
                VietnameseGrammar = GetHeaderField(fields, headers, "vietnameseGrammar").Trim(),
                VietnameseExample = GetHeaderField(fields, headers, "vietnameseExample").Trim()
            };
            MoveLegacyNotes(word);
            result.Add(word);
        }

        if (result.Count == 0)
        {
            throw new FormatException("Tệp không có từ vựng nào để nhập.");
        }

        return result;
    }

    private static List<VocabularyWord> ParseExcelFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var workbook = LoadExcelXml(archive, "xl/workbook.xml");
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelationship = "http://schemas.openxmlformats.org/package/2006/relationships";

        var firstSheet = workbook.Root?
            .Element(spreadsheet + "sheets")?
            .Elements(spreadsheet + "sheet")
            .FirstOrDefault();
        var relationshipId = (string?)firstSheet?.Attribute(relationship + "id");
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            throw new FormatException("Tệp Excel không có trang tính nào để nhập.");
        }

        var relationships = LoadExcelXml(archive, "xl/_rels/workbook.xml.rels");
        var sheetTarget = (string?)relationships.Root?
            .Elements(packageRelationship + "Relationship")
            .FirstOrDefault(item => (string?)item.Attribute("Id") == relationshipId)?
            .Attribute("Target");
        if (string.IsNullOrWhiteSpace(sheetTarget))
        {
            throw new FormatException("Không tìm thấy trang tính đầu tiên trong tệp Excel.");
        }

        var worksheetPath = NormalizeArchivePath(
            sheetTarget.StartsWith("/", StringComparison.Ordinal)
                ? sheetTarget.TrimStart('/')
                : $"xl/{sheetTarget}");
        var worksheet = LoadExcelXml(archive, worksheetPath);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetData = worksheet.Root?.Element(spreadsheet + "sheetData")
            ?? throw new FormatException("Trang tính Excel không chứa dữ liệu.");

        var rows = new List<(string RowNumber, Dictionary<int, string> Cells)>();
        foreach (var row in sheetData.Elements(spreadsheet + "row"))
        {
            var cells = new Dictionary<int, string>();
            foreach (var cell in row.Elements(spreadsheet + "c"))
            {
                var reference = (string?)cell.Attribute("r");
                if (string.IsNullOrWhiteSpace(reference))
                {
                    continue;
                }

                var columnIndex = GetExcelColumnIndex(reference);
                var value = ReadExcelCell(cell, spreadsheet, sharedStrings);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    cells[columnIndex] = value.Trim();
                }
            }

            if (cells.Count > 0)
            {
                rows.Add(((string?)row.Attribute("r") ?? "?", cells));
            }
        }

        if (rows.Count == 0)
        {
            throw new FormatException("Tệp Excel không có dữ liệu từ vựng.");
        }

        var firstRow = rows[0].Cells;
        var hasHeader = firstRow.Values.Any(value => HeaderType(value) is not null);
        var hasSeparatedNotes = hasHeader || rows.Any(row => row.Cells.Keys.Any(column => column >= 6));
        var headers = hasHeader
            ? firstRow
                .Select(cell => (cell.Key, Type: HeaderType(cell.Value)))
                .Where(cell => cell.Type is not null)
                .ToDictionary(cell => cell.Type!, cell => cell.Key, StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);

        if (hasHeader && (!headers.ContainsKey("chinese") || !headers.ContainsKey("vietnamese")))
        {
            throw new FormatException("Dòng tiêu đề Excel cần có cột Tiếng Trung và Nghĩa tiếng Việt.");
        }

        var result = new List<VocabularyWord>();
        foreach (var (rowNumber, cells) in rows.Skip(hasHeader ? 1 : 0))
        {
            var chinese = GetExcelField(cells, headers, hasHeader, "chinese", 0);
            var pinyin = GetExcelField(cells, headers, hasHeader, "pinyin", 1);
            var vietnamese = GetExcelField(cells, headers, hasHeader, "vietnamese", 2);
            var english = GetExcelField(cells, headers, hasHeader, "english", 3);
            var legacyExample = hasSeparatedNotes
                ? GetExcelField(cells, headers, hasHeader, "example", -1)
                : GetExcelField(cells, headers, hasHeader, "example", 4);
            var legacyGrammar = hasSeparatedNotes
                ? GetExcelField(cells, headers, hasHeader, "grammar", -1)
                : GetExcelField(cells, headers, hasHeader, "grammar", 5);
            var englishExample = GetExcelField(cells, headers, hasHeader, "englishExample", 4);
            var chineseExample = GetExcelField(cells, headers, hasHeader, "chineseExample", 5);
            var englishGrammar = GetExcelField(cells, headers, hasHeader, "englishGrammar", 6);
            var chineseGrammar = GetExcelField(cells, headers, hasHeader, "chineseGrammar", 7);
            if (!ContainsChinese(chinese) || string.IsNullOrWhiteSpace(vietnamese))
            {
                throw new FormatException(
                    $"Dòng {rowNumber}: cần có từ tiếng Trung ở cột A và nghĩa tiếng Việt ở cột C.");
            }

            var word = new VocabularyWord(
                chinese,
                pinyin,
                vietnamese,
                english,
                legacyExample,
                legacyGrammar,
                englishExample,
                chineseExample,
                englishGrammar,
                chineseGrammar)
            {
                VietnameseGrammar = GetExcelField(cells, headers, hasHeader, "vietnameseGrammar", -1),
                VietnameseExample = GetExcelField(cells, headers, hasHeader, "vietnameseExample", -1)
            };
            MoveLegacyNotes(word);
            result.Add(word);
        }

        if (result.Count == 0)
        {
            throw new FormatException("Tệp Excel không có từ vựng nào để nhập.");
        }

        return result;
    }

    private static XDocument LoadExcelXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new FormatException($"Tệp Excel thiếu thành phần “{entryName}”.");
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    private static string[] ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return Array.Empty<string>();
        }

        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var entryStream = entry.Open();
        var document = XDocument.Load(entryStream);
        return document.Root?
            .Elements(spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(spreadsheet + "t").Select(text => text.Value)))
            .ToArray()
            ?? Array.Empty<string>();
    }

    private static string ReadExcelCell(
        XElement cell,
        XNamespace spreadsheet,
        IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(spreadsheet + "t").Select(text => text.Value));
        }

        var value = cell.Element(spreadsheet + "v")?.Value ?? string.Empty;
        if (type == "s")
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index < 0 ||
                index >= sharedStrings.Count)
            {
                throw new FormatException("Tệp Excel có chỉ mục chuỗi dùng chung không hợp lệ.");
            }

            return sharedStrings[index];
        }

        return value;
    }

    private static int GetExcelColumnIndex(string cellReference)
    {
        var columnIndex = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsAsciiLetter(character))
            {
                break;
            }

            columnIndex = (columnIndex * 26) + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        if (columnIndex == 0)
        {
            throw new FormatException($"Vị trí ô Excel không hợp lệ: {cellReference}.");
        }

        return columnIndex - 1;
    }

    private static string NormalizeArchivePath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/'))
        {
            if (part is "" or ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    throw new FormatException("Đường dẫn trang tính Excel không hợp lệ.");
                }

                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join("/", parts);
    }

    private static string GetExcelField(
        IReadOnlyDictionary<int, string> cells,
        IReadOnlyDictionary<string, int> headers,
        bool hasHeader,
        string field,
        int defaultColumn)
    {
        if (hasHeader && !headers.TryGetValue(field, out _))
        {
            return string.Empty;
        }

        var column = hasHeader && headers.TryGetValue(field, out var headerColumn)
            ? headerColumn
            : defaultColumn;
        return column >= 0 && cells.TryGetValue(column, out var value) ? value : string.Empty;
    }

    private static string GetImportErrorMessage(string path, Exception exception)
    {
        var message = exception is IOException ioException &&
                      (ioException.HResult & 0xFFFF) is 32 or 33
            ? "Tệp đang bị khóa bởi một ứng dụng khác. Hãy đóng tệp trong Excel hoặc ứng dụng đang sử dụng rồi thử nhập lại."
            : exception.Message;
        return $"Không thể nhập danh sách từ vựng “{path}”: {message}";
    }

    private static string[] SplitLine(string line)
    {
        var separator = line.Contains('\t') ? '\t' : ',';
        var result = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == separator && !quoted)
            {
                result.Add(value.ToString().Trim());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }

        if (quoted)
        {
            throw new FormatException("Có dấu ngoặc kép chưa được đóng trong tệp.");
        }

        result.Add(value.ToString().Trim());
        return result.ToArray();
    }

    private static string? HeaderType(string header)
    {
        var normalized = RemoveDiacritics(header.Trim().TrimStart('\uFEFF')).ToLowerInvariant();
        if (normalized is "chinese" or "hanzi" or "han" or "tieng trung" or "tu tieng trung" or "中文")
        {
            return "chinese";
        }

        if (normalized is "pinyin" or "phien am")
        {
            return "pinyin";
        }

        if (normalized is "vietnamese" or "meaning" or "translation" or "nghia" or "nghia tieng viet" or "tieng viet")
        {
            return "vietnamese";
        }

        if (normalized is "english" or "en" or "nghia tieng anh" or "tieng anh")
        {
            return "english";
        }

        if (normalized is "english example" or "example english" or "vi du tieng anh" or "cau vi du tieng anh")
        {
            return "englishExample";
        }

        if (normalized is "chinese example" or "example chinese" or "vi du tieng trung" or "cau vi du tieng trung")
        {
            return "chineseExample";
        }

        if (normalized is "english grammar" or "grammar english" or "ngu phap tieng anh" or "cau truc tieng anh")
        {
            return "englishGrammar";
        }

        if (normalized is "vietnamese example" or "example vietnamese" or "vi du tieng viet" or "cau vi du tieng viet")
        {
            return "vietnameseExample";
        }

        if (normalized is "vietnamese grammar" or "grammar vietnamese" or "ngu phap tieng viet" or "cau truc tieng viet")
        {
            return "vietnameseGrammar";
        }

        if (normalized is "chinese grammar" or "grammar chinese" or "ngu phap tieng trung" or "cau truc tieng trung")
        {
            return "chineseGrammar";
        }

        if (normalized is "example" or "examples" or "sentence" or "example sentence" or "vi du" or "cau vi du")
        {
            return "example";
        }

        if (normalized is "grammar" or "structure" or "ngu phap" or "cau truc")
        {
            return "grammar";
        }

        return null;
    }

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                result.Append(character);
            }
        }

        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string GetField(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index].Trim() : string.Empty;

    private static string GetHeaderField(
        string[] fields,
        IReadOnlyDictionary<string, int> headers,
        string name) =>
        headers.TryGetValue(name, out var index) ? GetField(fields, index) : string.Empty;

    private static bool ContainsChinese(string value) =>
        value.EnumerateRunes().Any(rune =>
            (rune.Value >= 0x3400 && rune.Value <= 0x4DBF) ||
            (rune.Value >= 0x4E00 && rune.Value <= 0x9FFF) ||
            (rune.Value >= 0x20000 && rune.Value <= 0x2FA1F));

    private void AddWord(object? sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "Thêm từ vựng",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(430, 508),
            Font = new Font("Segoe UI", 10F)
        };
        var inputs = new[]
        {
            AddEntryRow(dialog, 0, "Tiếng Trung *", new Font("Microsoft YaHei UI", 12F)),
            AddEntryRow(dialog, 1, "Pinyin", null),
            AddEntryRow(dialog, 2, "Nghĩa tiếng Việt *", null),
            AddEntryRow(dialog, 3, "Nghĩa tiếng Anh", null),
            AddEntryRow(dialog, 4, "Ví dụ tiếng Anh", null),
            AddEntryRow(dialog, 5, "Ví dụ tiếng Trung", new Font("Microsoft YaHei UI", 11F)),
            AddEntryRow(dialog, 6, "Ngữ pháp", new Font("Microsoft YaHei UI", 11F)),
            AddEntryRow(dialog, 7, "Ngữ pháp tiếng Việt", new Font("Microsoft YaHei UI", 11F)),
            AddEntryRow(dialog, 8, "Ví dụ tiếng Việt", null)
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Location = new Point(244, 452)
        };
        var save = new Button { AutoSize = true, Text = "Lưu" };
        var cancel = new Button { AutoSize = true, Text = "Hủy", DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            if (!ContainsChinese(inputs[0].Text) || string.IsNullOrWhiteSpace(inputs[2].Text))
            {
                MessageBox.Show(
                    dialog,
                    "Hãy nhập từ tiếng Trung có chữ Hán và nghĩa tiếng Việt.",
                    "Thông tin chưa hợp lệ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var word = new VocabularyWord(
                inputs[0].Text.Trim(),
                inputs[1].Text.Trim(),
                inputs[2].Text.Trim(),
                inputs[3].Text.Trim(),
                string.Empty,
                string.Empty,
                inputs[4].Text.Trim(),
                inputs[5].Text.Trim(),
                inputs[6].Text.Trim())
            {
                VietnameseGrammar = inputs[7].Text.Trim(),
                VietnameseExample = inputs[8].Text.Trim()
            };
            var updated = _words.ToList();
            var existing = updated.FindIndex(item =>
                string.Equals(item.Chinese, word.Chinese, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                updated[existing] = word;
            }
            else
            {
                updated.Add(word);
            }

            if (SaveAndReplace(updated))
            {
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            }
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        dialog.ShowDialog(this);
    }

    private static TextBox AddEntryRow(Form dialog, int index, string title, Font? inputFont)
    {
        var label = new Label
        {
            Text = title,
            Location = new Point(18, 18 + index * 48),
            Size = new Size(150, 32),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var input = new TextBox
        {
            Location = new Point(168, 18 + index * 48),
            Size = new Size(240, 32),
            Font = inputFont ?? dialog.Font,
            ImeMode = index is 0 or 5 ? ImeMode.On : ImeMode.NoControl
        };
        dialog.Controls.Add(label);
        dialog.Controls.Add(input);
        return input;
    }

    private void DeleteAllWords(object? sender, EventArgs e)
    {
        if (_words.Count == 0)
        {
            MessageBox.Show(
                this,
                "Danh sách từ vựng hiện đã trống.",
                "Không có từ để xóa",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Bạn có chắc muốn xóa toàn bộ {_words.Count} từ vựng không? Thao tác này không thể hoàn tác.",
            "Xác nhận xóa toàn bộ",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        SaveAndReplace(new List<VocabularyWord>());
    }

    private bool SaveAndReplace(List<VocabularyWord> updated)
    {
        try
        {
            var selectedWordKeys = _words
                .Where(word => word.IsSelected)
                .Select(word => word.Chinese)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var word in updated)
            {
                word.IsSelected = selectedWordKeys.Contains(word.Chinese);
            }

            SaveWords(updated);
            _words.RaiseListChangedEvents = false;
            _words.Clear();
            foreach (var word in updated)
            {
                _words.Add(word);
            }

            _words.RaiseListChangedEvents = true;
            _words.ResetBindings();
            var remainingWordKeys = _words
                .Select(word => word.Chinese)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var wordKey in _incorrectWordCounts.Keys
                         .Where(wordKey => !remainingWordKeys.Contains(wordKey))
                         .ToArray())
            {
                _incorrectWordCounts.Remove(wordKey);
            }

            _typedCorrectWords.IntersectWith(remainingWordKeys);
            SaveMissedWords();
            SaveTypedCorrectWords();

            _flashcardIndex = -1;
            _flashcardOrder.Clear();
            _flashcardOrderPosition = -1;
            _questionIndex = -1;
            _questionOrder.Clear();
            _questionOrderPosition = 0;
            RefreshVocabulary();
            RefreshStudyWords();
            AdjustColumnWidths();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _words.RaiseListChangedEvents = true;
            MessageBox.Show(this, $"Không thể lưu từ vựng: {ex.Message}", "Lỗi lưu dữ liệu",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void RefreshVocabulary()
    {
        _refreshingVocabulary = true;
        try
        {
            _visibleWords.RaiseListChangedEvents = false;
            _visibleWords.Clear();
            var searchText = _searchBox.Text.Trim();
            foreach (var word in _words)
            {
                if (MatchesVocabularySearch(word, searchText) &&
                    MatchesColumnFilters(word))
                {
                    _visibleWords.Add(word);
                }
            }

            _visibleWords.RaiseListChangedEvents = true;
            _visibleWords.ResetBindings();
            if (!ReferenceEquals(_grid.DataSource, _visibleWords))
            {
                _grid.DataSource = _visibleWords;
            }

            SyncHskFilterChecks();

        }
        finally
        {
            _visibleWords.RaiseListChangedEvents = true;
            _refreshingVocabulary = false;
        }
    }

    // HSK quick filter: one tick box per level plus "Khác" for words with any
    // other (or no) level. It edits the same filter as the "Cấp HSK" header menu.
    private static readonly string[] HskFilterLevels = ["HSK 1", "HSK 2", "HSK 3", "HSK 4", "HSK 5", "HSK 6", "HSK 7-9"];
    private readonly List<(RoundedCheckBox Box, string? Level, string Caption)> _hskFilterChecks = [];
    private bool _syncingHskFilterChecks;

    private FlowLayoutPanel BuildHskFilterRow()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = Padding.Empty };
        row.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "🎯 Lọc HSK:",
            ForeColor = BodyTextColor,
            Margin = new Padding(0, 11, 12, 0)
        });
        foreach (var (level, caption) in HskFilterLevels.Select(level => ((string?)level, level)).Append((null, "Khác")))
        {
            var box = new RoundedCheckBox
            {
                AutoSize = true,
                Text = caption,
                Font = new Font(UiFontName, 10.5F),
                ForeColor = BodyTextColor,
                BackColor = PageColor,
                Margin = new Padding(0, 8, 18, 0)
            };
            box.CheckedChanged += (_, _) => ApplyHskFilterChecks();
            _hskFilterChecks.Add((box, level, caption));
            row.Controls.Add(box);
        }

        return row;
    }

    private void ApplyHskFilterChecks()
    {
        if (_syncingHskFilterChecks)
        {
            return;
        }

        var property = nameof(VocabularyWord.HskLevel);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (box, level, _) in _hskFilterChecks.Where(item => item.Box.Checked))
        {
            if (level is not null)
            {
                selected.Add(level);
            }
            else
            {
                // "Khác": every level value that is not one of the standard ones.
                selected.UnionWith(_words.Select(word => word.HskLevel).Where(value => !HskFilterLevels.Contains(value)));
            }
        }

        // Nothing ticked means no HSK filter at all.
        if (selected.Count == 0)
        {
            _columnFilters.Remove(property);
        }
        else
        {
            _columnFilters[property] = selected;
        }

        if (_grid.Columns[property] is { } column)
        {
            UpdateColumnFilterHeader(column);
        }

        RefreshVocabulary();
        // A new set of words starts from its first row.
        if (_grid.RowCount > 0)
        {
            _grid.FirstDisplayedScrollingRowIndex = 0;
        }
    }

    // Mirrors the HSK filter (also set from the header menu) on the tick boxes,
    // and shows how many words each level has.
    private void SyncHskFilterChecks()
    {
        if (_hskFilterChecks.Count == 0)
        {
            return;
        }

        var counts = _words.GroupBy(word => HskFilterLevels.Contains(word.HskLevel) ? word.HskLevel : "")
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        _columnFilters.TryGetValue(nameof(VocabularyWord.HskLevel), out var filter);
        _syncingHskFilterChecks = true;
        try
        {
            foreach (var (box, level, caption) in _hskFilterChecks)
            {
                box.Text = $"{caption} ({counts.GetValueOrDefault(level ?? "")})";
                box.Checked = filter is not null && (level is not null
                    ? filter.Contains(level)
                    : filter.Any(value => !HskFilterLevels.Contains(value)));
            }
        }
        finally
        {
            _syncingHskFilterChecks = false;
        }
    }

    private static bool MatchesVocabularySearch(VocabularyWord word, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return word.Chinese.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
            word.Pinyin.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
            word.Vietnamese.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
            word.English.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
            word.EnglishExample.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
            word.ChineseExample.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
            word.EnglishGrammar.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
            word.VietnameseGrammar.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
            word.VietnameseExample.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
            word.HskLevel.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool MatchesColumnFilters(VocabularyWord word)
    {
        return _columnFilters.All(filter => filter.Value.Contains(GetFilterValue(word, filter.Key)));
    }

    private static bool IsFilterableColumn(string propertyName) =>
        propertyName is nameof(VocabularyWord.Chinese) or nameof(VocabularyWord.HskLevel);

    private static string GetFilterValue(VocabularyWord word, string propertyName) => propertyName switch
    {
        nameof(VocabularyWord.HskLevel) => word.HskLevel,
        _ => word.Chinese
    };

    private void VocabularyGrid_ColumnHeaderMouseClick(
        object? sender,
        DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0)
        {
            return;
        }

        var column = _grid.Columns[e.ColumnIndex];
        if (!IsFilterableColumn(column.DataPropertyName))
        {
            return;
        }

        ShowColumnFilter(column);
    }

    // "Tiếng Trung" lists the words left by the search and the HSK ticks, each
    // with its study tick, plus tick-all / untick-all for those words. It is a
    // rounded card: rounded search box, rounded list with rounded tick boxes.
    private void ShowStudyWordMenu(DataGridViewColumn column)
    {
        const int menuWidth = 340;
        const int menuHeight = 480;
        var dropDown = new ToolStripDropDown { AutoClose = true, Padding = Padding.Empty, DropShadowEnabled = true };
        var card = new RoundedPanel
        {
            Size = new Size(menuWidth, menuHeight),
            Radius = 16,
            FillColor = SurfaceColor,
            OutlineColor = Color.FromArgb(203, 213, 225),
            BackColor = SurfaceColor
        };

        // Rounded search box with a magnifier.
        var searchFrame = new RoundedPanel
        {
            Location = new Point(14, 14),
            Size = new Size(menuWidth - 28, 38),
            Radius = 19,
            FillColor = PageColor,
            OutlineColor = BorderColor,
            BackColor = SurfaceColor,
        };
        var searchIcon = new Label
        {
            AutoSize = false,
            Location = new Point(12, 8),
            Size = new Size(24, 22),
            Text = "🔍",
            Font = new Font("Segoe UI Emoji", 10F),
            ForeColor = MutedTextColor,
            BackColor = PageColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        var search = new TextBox
        {
            Location = new Point(40, 10),
            Width = searchFrame.Width - 56,
            BorderStyle = BorderStyle.None,
            BackColor = PageColor,
            Font = new Font(UiFontName, 10.5F),
            PlaceholderText = "Tìm chữ Hán hoặc pinyin..."
        };
        searchFrame.Controls.Add(searchIcon);
        searchFrame.Controls.Add(search);

        var summary = new PillLabel
        {
            AutoSize = false,
            Location = new Point(14, 62),
            Size = new Size(menuWidth - 28, 30),
            Font = new Font(UiFontName, 9.5F, FontStyle.Bold),
            ForeColor = Accent,
            TextAlign = ContentAlignment.MiddleCenter
        };

        // Owner-drawn list in a rounded frame: a click anywhere on a row toggles it.
        var listFrame = new RoundedPanel
        {
            Location = new Point(14, 102),
            Size = new Size(menuWidth - 28, 280),
            Radius = 14,
            FillColor = SurfaceColor,
            OutlineColor = BorderColor,
            BackColor = SurfaceColor,
            Padding = new Padding(6)
        };
        var wordList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            IntegralHeight = false,
            ItemHeight = 34,
            BackColor = SurfaceColor,
            SelectionMode = SelectionMode.None
        };
        listFrame.Controls.Add(wordList);
        var hoverIndex = -1;
        var hanziFont = new Font("Microsoft YaHei UI", 11.5F);
        var pinyinFont = new Font(UiFontName, 9.5F);
        wordList.Disposed += (_, _) => { pinyinFont.Dispose(); hanziFont.Dispose(); };
        wordList.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || wordList.Items[e.Index] is not StudyWordOption option)
            {
                return;
            }

            var graphics = e.Graphics;
            using (var background = new SolidBrush(SurfaceColor))
            {
                graphics.FillRectangle(background, e.Bounds);
            }

            var word = option.Word;
            if (e.Index == hoverIndex || word.IsSelected)
            {
                var smoothing = graphics.SmoothingMode;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var pill = new RectangleF(e.Bounds.Left + 1, e.Bounds.Top + 2, e.Bounds.Width - 3, e.Bounds.Height - 4);
                using var path = RoundedMenuRenderer.CreateRoundedPath(pill, 9);
                using var fill = new SolidBrush(word.IsSelected ? AccentSoft : Color.FromArgb(241, 245, 249));
                graphics.FillPath(fill, path);
                graphics.SmoothingMode = smoothing;
            }

            DrawRoundedTickBox(graphics, new RectangleF(e.Bounds.Left + 10, e.Bounds.Top + (e.Bounds.Height - 20) / 2F, 20, 20), word.IsSelected);
            var textLeft = e.Bounds.Left + 42;
            var hanziWidth = TextRenderer.MeasureText(graphics, word.Chinese, hanziFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(graphics, word.Chinese, hanziFont,
                new Rectangle(textLeft, e.Bounds.Top, hanziWidth + 4, e.Bounds.Height),
                word.IsSelected ? Accent : TextColor, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, word.Pinyin, pinyinFont,
                new Rectangle(textLeft + hanziWidth + 12, e.Bounds.Top, e.Bounds.Right - textLeft - hanziWidth - 16, e.Bounds.Height),
                MutedTextColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        };

        void UpdateSummary()
        {
            var words = wordList.Items.OfType<StudyWordOption>().Select(option => option.Word).ToList();
            var hskLevels = _hskFilterChecks.Where(item => item.Box.Checked).Select(item => item.Level ?? "Khác").ToList();
            var source = hskLevels.Count == 0 ? "Tất cả HSK" : string.Join(", ", hskLevels);
            summary.Text = $"{source}  •  {words.Count} từ  •  ✅ {words.Count(word => word.IsSelected)} đang học";
        }

        void FillList()
        {
            var query = search.Text.Trim();
            var words = _visibleWords.Where(word => string.IsNullOrEmpty(query) ||
                word.Chinese.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                word.Pinyin.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
            wordList.BeginUpdate();
            try
            {
                wordList.Items.Clear();
                foreach (var word in words)
                {
                    wordList.Items.Add(new StudyWordOption(word));
                }
            }
            finally
            {
                wordList.EndUpdate();
            }

            UpdateSummary();
        }

        // Toggling a row works like ticking "Học" in the grid.
        void Toggle(int index)
        {
            if (index < 0 || index >= wordList.Items.Count || wordList.Items[index] is not StudyWordOption option)
            {
                return;
            }

            option.Word.IsSelected = !option.Word.IsSelected;
            wordList.Invalidate(wordList.GetItemRectangle(index));
            _grid.InvalidateColumn(_grid.Columns[nameof(VocabularyWord.IsSelected)]!.Index);
            RefreshStudyWords();
            UpdateSummary();
        }

        search.TextChanged += (_, _) => FillList();
        wordList.MouseDown += (_, e) => Toggle(wordList.IndexFromPoint(e.Location));
        wordList.MouseMove += (_, e) =>
        {
            var index = wordList.IndexFromPoint(e.Location);
            if (index != hoverIndex)
            {
                var previous = hoverIndex;
                hoverIndex = index;
                if (previous >= 0 && previous < wordList.Items.Count)
                {
                    wordList.Invalidate(wordList.GetItemRectangle(previous));
                }

                if (index >= 0)
                {
                    wordList.Invalidate(wordList.GetItemRectangle(index));
                }
            }
        };
        wordList.MouseLeave += (_, _) =>
        {
            if (hoverIndex >= 0 && hoverIndex < wordList.Items.Count)
            {
                var previous = hoverIndex;
                hoverIndex = -1;
                wordList.Invalidate(wordList.GetItemRectangle(previous));
            }
        };

        var buttonsTitle = new Label
        {
            AutoSize = false,
            Location = new Point(16, 392),
            Size = new Size(menuWidth - 32, 20),
            ForeColor = MutedTextColor,
            BackColor = SurfaceColor,
            Text = "Chọn học các từ đang hiển thị:"
        };
        var studyButtons = new[]
        {
            CreateButton("✔  Tích chọn tất cả", (s, e) => { SelectAllWords(s, e); wordList.Invalidate(); UpdateSummary(); }),
            CreateButton("✖  Bỏ tích tất cả", (s, e) => { ClearWordSelection(s, e); wordList.Invalidate(); UpdateSummary(); }, secondary: true)
        };
        var buttonWidth = (menuWidth - 28 - 10) / 2;
        for (var index = 0; index < studyButtons.Length; index++)
        {
            studyButtons[index].AutoSize = false;
            studyButtons[index].Size = new Size(buttonWidth, 40);
            studyButtons[index].Margin = Padding.Empty;
            studyButtons[index].Location = new Point(14 + index * (buttonWidth + 10), 418);
        }

        card.Controls.AddRange([searchFrame, summary, listFrame, buttonsTitle, .. studyButtons]);
        dropDown.Items.Add(new ToolStripControlHost(card)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = card.Size
        });
        // Clip the popup itself to the card's rounded shape.
        dropDown.Opened += (_, _) =>
        {
            using var path = RoundedMenuRenderer.CreateRoundedPath(new RectangleF(0, 0, dropDown.Width, dropDown.Height), 16);
            dropDown.Region = new Region(path);
        };
        dropDown.Closed += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke((Action)dropDown.Dispose);
            }
        };
        FillList();

        var headerBounds = _grid.GetCellDisplayRectangle(column.Index, -1, true);
        dropDown.Show(_grid.PointToScreen(new Point(headerBounds.Left, headerBounds.Bottom + 4)));
    }

    private sealed record StudyWordOption(VocabularyWord Word)
    {
        public override string ToString() =>
            string.IsNullOrWhiteSpace(Word.Pinyin) ? Word.Chinese : $"{Word.Chinese}   {Word.Pinyin}";
    }

    private void ShowColumnFilter(DataGridViewColumn column)
    {
        if (column.DataPropertyName == nameof(VocabularyWord.Chinese))
        {
            ShowStudyWordMenu(column);
            return;
        }

        var propertyName = column.DataPropertyName;
        var values = _words
            .Select(word => GetFilterValue(word, propertyName))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var checkedValues = _columnFilters.TryGetValue(propertyName, out var currentFilter)
            ? new HashSet<string>(currentFilter, StringComparer.Ordinal)
            : new HashSet<string>(values, StringComparer.Ordinal);

        var dropDown = new ToolStripDropDown
        {
            AutoClose = true,
            Padding = Padding.Empty
        };
        var panel = new Panel
        {
            BackColor = SystemColors.Window,
            Size = new Size(300, 360)
        };
        var search = new TextBox
        {
            Location = new Point(10, 10),
            PlaceholderText = "Tìm trong các giá trị...",
            Size = new Size(280, 28)
        };
        var selectAll = new CheckBox
        {
            AutoCheck = false,
            AutoSize = true,
            Location = new Point(10, 44),
            Text = "Chọn tất cả"
        };
        // One list control instead of a checkbox per value: the word list can hold
        // more values than the per-process window handle limit allows.
        var optionsList = new CheckedListBox
        {
            CheckOnClick = true,
            IntegralHeight = false,
            Location = new Point(10, 70),
            Size = new Size(280, 230)
        };
        var updatingOptions = false;

        static string DisplayText(string value) => string.IsNullOrEmpty(value) ? "(Trống)" : value;

        void UpdateSelectAll()
        {
            selectAll.CheckState = checkedValues.Count switch
            {
                0 => CheckState.Unchecked,
                _ when checkedValues.Count == values.Count => CheckState.Checked,
                _ => CheckState.Indeterminate
            };
        }

        void UpdateOptions()
        {
            var query = search.Text.Trim();
            updatingOptions = true;
            optionsList.BeginUpdate();
            try
            {
                optionsList.Items.Clear();
                foreach (var value in values.Where(value =>
                             DisplayText(value).Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                {
                    optionsList.Items.Add(new FilterOption(value, DisplayText(value)), checkedValues.Contains(value));
                }
            }
            finally
            {
                optionsList.EndUpdate();
                updatingOptions = false;
            }

            UpdateSelectAll();
        }

        search.TextChanged += (_, _) => UpdateOptions();
        optionsList.ItemCheck += (_, e) =>
        {
            if (updatingOptions || optionsList.Items[e.Index] is not FilterOption option)
            {
                return;
            }

            if (e.NewValue == CheckState.Checked)
            {
                checkedValues.Add(option.Value);
            }
            else
            {
                checkedValues.Remove(option.Value);
            }

            UpdateSelectAll();
        };

        selectAll.Click += (_, _) =>
        {
            if (checkedValues.Count < values.Count)
            {
                checkedValues.UnionWith(values);
            }
            else
            {
                checkedValues.Clear();
            }

            UpdateOptions();
        };

        var apply = new Button
        {
            Location = new Point(150, 315),
            Size = new Size(66, 32),
            Text = "OK"
        };
        apply.Click += (_, _) =>
        {
            var selected = new HashSet<string>(checkedValues, StringComparer.Ordinal);
            if (selected.Count == values.Count)
            {
                _columnFilters.Remove(propertyName);
            }
            else
            {
                _columnFilters[propertyName] = selected;
            }

            UpdateColumnFilterHeader(column);
            dropDown.Close();
            RefreshVocabulary();
        };

        var clear = new Button
        {
            Location = new Point(78, 315),
            Size = new Size(66, 32),
            Text = "Xóa lọc"
        };
        clear.Click += (_, _) =>
        {
            _columnFilters.Remove(propertyName);
            UpdateColumnFilterHeader(column);
            dropDown.Close();
            RefreshVocabulary();
        };

        panel.Controls.AddRange([search, selectAll, optionsList, clear, apply]);

        dropDown.Items.Add(new ToolStripControlHost(panel)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = panel.Size
        });
        dropDown.Closed += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke((Action)dropDown.Dispose);
            }
        };
        UpdateOptions();

        var headerBounds = _grid.GetCellDisplayRectangle(column.Index, -1, true);
        dropDown.Show(_grid.PointToScreen(new Point(headerBounds.Left, headerBounds.Bottom)));
    }

    private void UpdateColumnFilterHeader(DataGridViewColumn column)
    {
        var title = Convert.ToString(column.Tag, CultureInfo.CurrentCulture) ?? column.Name;
        var filterMark = _columnFilters.ContainsKey(column.DataPropertyName) ? "  ●" : string.Empty;
        column.HeaderText = $"{title}{filterMark}  ▼";
    }

    private void VocabularyGrid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_grid.IsCurrentCellDirty && _grid.CurrentCell is { OwningColumn.DataPropertyName: nameof(VocabularyWord.IsSelected) })
        {
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void VocabularyGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_refreshingVocabulary ||
            e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            _grid.Columns[e.ColumnIndex].DataPropertyName != nameof(VocabularyWord.IsSelected))
        {
            return;
        }

        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed)
                {
                    return;
                }

                RefreshStudyWords();
            }));
        }
    }

    private void SelectAllWords(object? sender, EventArgs e) =>
        SetWordSelection(true);

    private void ClearWordSelection(object? sender, EventArgs e) =>
        SetWordSelection(false);

    // Applies only to the rows left by search and column filters, so a filter
    // such as "HSK 1" can be used to tick exactly that group of words.
    private void SetWordSelection(bool selected)
    {
        foreach (var word in _visibleWords)
        {
            word.IsSelected = selected;
        }

        _visibleWords.ResetBindings();
        RefreshStudyWords();
    }

    private void RefreshStudyWords()
    {
        _flashcardFlipTimer.Stop();
        _studyWords.Clear();
        var selectedWords = _words.Where(word => word.IsSelected).ToList();
        _studyWords.AddRange(selectedWords);
        // Every tick, select-all and list change ends here, so remember the choice.
        SaveSelectedWords();
        _practiceReviewWords = null;
        _showingMeaning = false;

        _flashcardIndex = -1;
        _flashcardOrder.Clear();
        _flashcardOrderPosition = -1;
        _questionIndex = -1;
        _questionOrder.Clear();
        _questionOrderPosition = 0;
        ShowFlashcard();
        ShowNextQuestion();
    }

    private void VocabularyGrid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 &&
            e.ColumnIndex >= 0 &&
            _grid.Rows[e.RowIndex].DataBoundItem is VocabularyWord word)
        {
            SpeakVocabularyCell(word, _grid.Columns[e.ColumnIndex].DataPropertyName);
        }
    }

    private static bool IsEditableTextColumn(string propertyName) =>
        propertyName is
            nameof(VocabularyWord.Vietnamese) or
            nameof(VocabularyWord.English) or
            nameof(VocabularyWord.HskLevel) or
            nameof(VocabularyWord.EnglishExample) or
            nameof(VocabularyWord.ChineseExample) or
            nameof(VocabularyWord.VietnameseExample) or
            nameof(VocabularyWord.EnglishGrammar) or
            nameof(VocabularyWord.VietnameseGrammar);

    private static readonly Color HighlightWordColor = Color.FromArgb(200, 30, 30);

    // Example and grammar cells draw the row's vocabulary word in red.
    private void VocabularyGrid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex >= 0 &&
            e.ColumnIndex >= 0 &&
            _grid.Columns[e.ColumnIndex].DataPropertyName == nameof(VocabularyWord.IsSelected))
        {
            PaintRoundedCheckBox(e);
            return;
        }

        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            e.CellStyle is null ||
            _grid.Columns[e.ColumnIndex].DataPropertyName is not
                (nameof(VocabularyWord.ChineseExample) or
                 nameof(VocabularyWord.EnglishGrammar) or
                 nameof(VocabularyWord.VietnameseGrammar)) ||
            _grid.Rows[e.RowIndex].DataBoundItem is not VocabularyWord word ||
            Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture) is not { Length: > 0 } text)
        {
            return;
        }

        var segments = SplitHighlightedWord(text, GetHighlightKeys(word.Chinese));
        if (!segments.Any(segment => segment.Highlight))
        {
            return;
        }

        e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var normalColor = selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
        var bounds = new Rectangle(
            e.CellBounds.Left + e.CellStyle.Padding.Left,
            e.CellBounds.Top + e.CellStyle.Padding.Top,
            Math.Max(1, e.CellBounds.Width - e.CellStyle.Padding.Horizontal),
            Math.Max(1, e.CellBounds.Height - e.CellStyle.Padding.Vertical - 1));
        DrawHighlightedSegments(e.Graphics!, segments, e.CellStyle.Font ?? _grid.Font, bounds, normalColor, centerVertically: true);
        e.Handled = true;
    }

    // Without scrollbars the grid ignores the wheel, so scroll rows by hand
    // (the system's lines-per-notch setting, at least one row per notch).
    private void VocabularyGrid_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (_grid.RowCount == 0 || _grid.IsCurrentCellInEditMode)
        {
            return;
        }

        var linesPerNotch = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        var rows = -e.Delta * linesPerNotch / 120;
        if (rows == 0)
        {
            rows = -Math.Sign(e.Delta);
        }

        var first = Math.Max(0, _grid.FirstDisplayedScrollingRowIndex);
        var target = Math.Clamp(first + rows, 0, GetLastFirstRow());
        if (target != _grid.FirstDisplayedScrollingRowIndex)
        {
            _grid.FirstDisplayedScrollingRowIndex = target;
        }

        // Rows only get their real (wrapped) height when painted, so at the end of
        // the list repaint and pull the view down until no blank space is left below.
        for (var pass = 0; pass < 3 && rows > 0 && _grid.FirstDisplayedScrollingRowIndex + 60 >= _grid.RowCount; pass++)
        {
            _grid.Refresh();
            var lastFirstRow = GetLastFirstRow();
            if (lastFirstRow >= _grid.FirstDisplayedScrollingRowIndex)
            {
                break;
            }

            _grid.FirstDisplayedScrollingRowIndex = lastFirstRow;
        }

        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }
    }

    // The first row index at which the remaining rows exactly fill the view.
    private int GetLastFirstRow()
    {
        var room = _grid.ClientSize.Height - _grid.ColumnHeadersHeight;
        var first = _grid.RowCount;
        var used = 0;
        while (first > 0 && used + _grid.Rows[first - 1].Height <= room)
        {
            used += _grid.Rows[first - 1].Height;
            first--;
        }

        return Math.Clamp(first, 0, Math.Max(0, _grid.RowCount - 1));
    }

    // The "Học" tick boxes are drawn as rounded squares: accent fill with a white
    // tick when checked, a light outline when not.
    private static void PaintRoundedCheckBox(DataGridViewCellPaintingEventArgs e)
    {
        e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
        var isChecked = e.FormattedValue is true || (e.Value is bool value && value);
        const int size = 20;
        var box = new RectangleF(
            e.CellBounds.Left + (e.CellBounds.Width - size) / 2F,
            e.CellBounds.Top + (e.CellBounds.Height - size) / 2F,
            size,
            size);
        DrawRoundedTickBox(e.Graphics!, box, isChecked);
        e.Handled = true;
    }

    // A 20px rounded tick box: accent fill with a white tick when checked, a
    // light outline when not. Shared by the grid and the settings checkboxes.
    private static void DrawRoundedTickBox(Graphics graphics, RectangleF box, bool isChecked)
    {
        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            const float diameter = 10F;
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            if (isChecked)
            {
                using var fill = new SolidBrush(Accent);
                graphics.FillPath(fill, path);
                using var tick = new Pen(Color.White, 2.4F)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round,
                    LineJoin = System.Drawing.Drawing2D.LineJoin.Round
                };
                graphics.DrawLines(tick,
                [
                    new PointF(box.Left + 5F, box.Top + 10.5F),
                    new PointF(box.Left + 8.5F, box.Top + 14F),
                    new PointF(box.Left + 15F, box.Top + 6.5F)
                ]);
            }
            else
            {
                using var fill = new SolidBrush(SurfaceColor);
                graphics.FillPath(fill, path);
                using var outline = new Pen(Color.FromArgb(148, 163, 184), 1.5F);
                graphics.DrawPath(outline, path);
            }
        }

        graphics.SmoothingMode = smoothing;
    }

    // Draws wrapped, horizontally centred text with highlighted segments in red.
    private static void DrawHighlightedSegments(
        Graphics graphics,
        IReadOnlyList<(string Text, bool Highlight)> segments,
        Font font,
        Rectangle bounds,
        Color normalColor,
        bool centerVertically,
        bool centerHorizontally = true)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var (lines, lineHeight) = LayoutHighlightedSegments(graphics, segments, font, bounds.Width);
        var clip = graphics.Clip;
        graphics.SetClip(bounds);
        var y = centerVertically
            ? bounds.Top + Math.Max(0, (bounds.Height - lines.Count * lineHeight) / 2)
            : bounds.Top;
        foreach (var line in lines)
        {
            var x = centerHorizontally
                ? bounds.Left + Math.Max(0, (bounds.Width - line.Sum(unit => unit.Width)) / 2)
                : bounds.Left;
            foreach (var unit in line)
            {
                TextRenderer.DrawText(
                    graphics,
                    unit.Text,
                    font,
                    new Point(x, y),
                    unit.Highlight ? HighlightWordColor : normalColor,
                    flags);
                x += unit.Width;
            }

            y += lineHeight;
        }

        graphics.Clip = clip;
    }

    // Wraps like the grid does: CJK characters break anywhere, other text at spaces.
    private static (List<List<(string Text, bool Highlight, int Width)>> Lines, int LineHeight) LayoutHighlightedSegments(
        IDeviceContext graphics,
        IReadOnlyList<(string Text, bool Highlight)> segments,
        Font font,
        int maxWidth)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var spaceWidth = TextRenderer.MeasureText(graphics, "a a", font, Size.Empty, flags).Width -
                         TextRenderer.MeasureText(graphics, "aa", font, Size.Empty, flags).Width;
        var lineHeight = TextRenderer.MeasureText(graphics, "Ag汉", font, Size.Empty, flags).Height;
        var lines = new List<List<(string Text, bool Highlight, int Width)>> { new() };
        var lineWidth = 0;
        foreach (var segment in segments)
        {
            foreach (System.Text.RegularExpressions.Match unit in
                     System.Text.RegularExpressions.Regex.Matches(
                         segment.Text,
                         @"\p{IsCJKUnifiedIdeographs}|[^\s\p{IsCJKUnifiedIdeographs}]+\s*|\s+"))
            {
                var trimmed = unit.Value.TrimEnd();
                var width = (trimmed.Length == 0
                                ? 0
                                : TextRenderer.MeasureText(graphics, trimmed, font, Size.Empty, flags).Width) +
                            (unit.Value.Length - trimmed.Length) * spaceWidth;
                if (lineWidth + width > maxWidth && lines[^1].Count > 0)
                {
                    lines.Add(new List<(string Text, bool Highlight, int Width)>());
                    lineWidth = 0;
                }

                lines[^1].Add((unit.Value, segment.Highlight, width));
                lineWidth += width;
            }
        }

        return (lines, lineHeight);
    }

    // App logo for the header: a white circle with "汉" in accent blue, then
    // the app name (Text) in white.
    private sealed class HeaderBrand : Control
    {
        private const int Logo = 38;
        private const int Gap = 12;
        private static readonly Font LogoFont = new("Microsoft YaHei UI", 15F, FontStyle.Bold);

        public HeaderBrand()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = Accent;
            ForeColor = Color.White;
        }

        public override Size GetPreferredSize(Size proposedSize) =>
            new(Logo + Gap + TextRenderer.MeasureText(Text, Font).Width + 2, Logo + 4);

        protected override void OnTextChanged(EventArgs e)
        {
            Size = GetPreferredSize(Size.Empty);
            base.OnTextChanged(e);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            Size = GetPreferredSize(Size.Empty);
            base.OnFontChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var circle = new Rectangle(0, (Height - Logo) / 2, Logo, Logo);
            graphics.FillEllipse(Brushes.White, circle);
            TextRenderer.DrawText(graphics, "汉", LogoFont, circle, Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, Text, Font, new Rectangle(Logo + Gap, 0, Width - Logo - Gap, Height), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // Header of a Settings card: a round pastel badge with an emoji, then the
    // title and a muted one-line description.
    private sealed class SectionHeader : Control
    {
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public Color BadgeColor { get; set; } = AccentSoft;

        public SectionHeader()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = SurfaceColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            const int badge = 44;
            var badgeBounds = new Rectangle(0, 2, badge, badge);
            using (var fill = new SolidBrush(BadgeColor))
            {
                graphics.FillEllipse(fill, badgeBounds);
            }

            using var iconFont = new Font("Segoe UI Emoji", 16F);
            TextRenderer.DrawText(graphics, Icon, iconFont, badgeBounds, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using var titleFont = new Font("Segoe UI Semibold", 13F);
            using var descriptionFont = new Font(UiFontName, 10F);
            var textLeft = badge + 14;
            TextRenderer.DrawText(graphics, Title, titleFont, new Rectangle(textLeft, 0, Width - textLeft, 26),
                TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(graphics, Description, descriptionFont, new Rectangle(textLeft, 26, Width - textLeft, 22),
                MutedTextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // A large rounded card on the Home page: emoji badge, title and a short
    // description. It lifts to an accent outline while the mouse is over it.
    private sealed class HomeTile : Control
    {
        private bool _hover;
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public Color BadgeColor { get; set; } = AccentSoft;

        public HomeTile()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            Cursor = Cursors.Hand;
            BackColor = PageColor;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var card = new RectangleF(2, 2, Width - 5, Height - 5);
            using (var path = RoundedMenuRenderer.CreateRoundedPath(card, 22))
            using (var fill = new SolidBrush(_hover ? Color.FromArgb(248, 251, 255) : SurfaceColor))
            using (var outline = new Pen(_hover ? Accent : BorderColor, _hover ? 2.5F : 1.5F))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(outline, path);
            }

            // Round pastel badge with the emoji, centred near the top.
            const int badge = 64;
            var badgeBounds = new RectangleF((Width - badge) / 2F, 24, badge, badge);
            using (var badgeFill = new SolidBrush(BadgeColor))
            {
                graphics.FillEllipse(badgeFill, badgeBounds);
            }

            using var iconFont = new Font("Segoe UI Emoji", 24F);
            TextRenderer.DrawText(graphics, Icon, iconFont, Rectangle.Round(badgeBounds), TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            using var titleFont = new Font("Segoe UI Semibold", 14F);
            var titleTop = (int)badgeBounds.Bottom + 14;
            TextRenderer.DrawText(graphics, Title, titleFont, new Rectangle(12, titleTop, Width - 24, 30),
                _hover ? Accent : TextColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            using var descriptionFont = new Font(UiFontName, 10F);
            TextRenderer.DrawText(graphics, Description, descriptionFont,
                new Rectangle(20, titleTop + 34, Width - 40, Height - titleTop - 44), MutedTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }

    // A checkbox drawn with the same rounded tick box as the word list.
    private sealed class RoundedCheckBox : CheckBox
    {
        public RoundedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size(30 + text.Width, Math.Max(26, text.Height + 4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? SurfaceColor);
            DrawRoundedTickBox(e.Graphics, new RectangleF(1, (Height - 20) / 2F, 20, 20), Checked);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(30, 0, Width - 30, Height), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(28, 2, Width - 28, Height - 4));
            }
        }
    }

    // A TabControl without its tab strip or border: the pages fill the whole
    // control at any size. Pages open from the Home cards.
    private sealed class PageHost : TabControl
    {
        private const int TCM_ADJUSTRECT = 0x1328;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TCM_ADJUSTRECT && !DesignMode)
            {
                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);
        }
    }

    // A panel drawn as an anti-aliased rounded rectangle with an outline and an
    // optional accent strip along the top (used for cards and input frames).
    private sealed class RoundedPanel : Panel
    {
        public int Radius { get; set; } = 12;
        public Color FillColor { get; set; } = SurfaceColor;
        public Color OutlineColor { get; set; } = BorderColor;
        public float OutlineWidth { get; set; } = 1.5F;
        public Color StripColor { get; set; } = Color.Empty;
        public int StripHeight { get; set; }

        public RoundedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(GetSolidParentColor());
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var inset = OutlineWidth / 2F;
            var bounds = new RectangleF(inset, inset, Width - OutlineWidth - 1, Height - OutlineWidth - 1);
            using var path = RoundedMenuRenderer.CreateRoundedPath(bounds, Math.Min(Radius, Height / 2F));
            using (var fill = new SolidBrush(FillColor))
            {
                graphics.FillPath(fill, path);
            }

            if (StripHeight > 0 && !StripColor.IsEmpty)
            {
                var clip = graphics.Clip;
                graphics.SetClip(path);
                using var strip = new SolidBrush(StripColor);
                graphics.FillRectangle(strip, 0, 0, Width, StripHeight);
                graphics.Clip = clip;
            }

            using var outline = new Pen(OutlineColor, OutlineWidth);
            graphics.DrawPath(outline, path);
        }

        // Corners show the first non-transparent background up the parent chain.
        private Color GetSolidParentColor()
        {
            for (var parent = Parent; parent is not null; parent = parent.Parent)
            {
                if (parent.BackColor.A == 255)
                {
                    return parent.BackColor;
                }
            }

            return PageColor;
        }
    }

    // A flat button drawn with anti-aliased rounded corners; colours come from
    // BackColor and FlatAppearance (hover/pressed), as set by StyleButton.
    private sealed class RoundedButton : Button
    {
        private const int Radius = 10;
        private bool _hovered;
        private bool _pressed;

        public RoundedButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = e.Button == MouseButtons.Left;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var fill = !Enabled
                ? Color.FromArgb(241, 245, 249)
                : _pressed
                    ? FlatAppearance.MouseDownBackColor
                    : _hovered
                        ? FlatAppearance.MouseOverBackColor
                        : BackColor;
            var bounds = new RectangleF(1F, 1F, Width - 2.5F, Height - 2.5F);
            using (var path = CreateRoundedPath(bounds, Math.Min(Radius, (int)(Height / 2F))))
            using (var brush = new SolidBrush(fill.IsEmpty ? BackColor : fill))
            {
                graphics.FillPath(brush, path);
                var borderColor = !Enabled
                    ? Color.FromArgb(203, 213, 225)
                    : FlatAppearance.BorderColor.IsEmpty ? BorderColor : FlatAppearance.BorderColor;
                using (var borderPen = new Pen(borderColor, 1.5F))
                {
                    graphics.DrawPath(borderPen, path);
                }

                if (Focused && ShowFocusCues)
                {
                    using var focusPen = new Pen(Color.FromArgb(147, 197, 253), 2F);
                    graphics.DrawPath(focusPen, path);
                }
            }

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                ClientRectangle,
                Enabled ? ForeColor : Color.FromArgb(148, 163, 184),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedPath(RectangleF bounds, int radius)
        {
            var diameter = radius * 2F;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Menu look matching the rounded buttons: white body with a thin border,
    // rounded soft-blue hover, and the checked item in bold accent text.
    private sealed class RoundedMenuRenderer : ToolStripProfessionalRenderer
    {
        public static System.Drawing.Drawing2D.GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
        {
            var diameter = radius * 2F;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
            e.Graphics.Clear(SurfaceColor);

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = CreateRoundedPath(
                new RectangleF(0.5F, 0.5F, e.ToolStrip.Width - 1.5F, e.ToolStrip.Height - 1.5F),
                10);
            using var pen = new Pen(Color.FromArgb(147, 187, 250), 1.5F);
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var isChecked = e.Item is ToolStripMenuItem { Checked: true };
            if (!e.Item.Selected && !isChecked)
            {
                return;
            }

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = CreateRoundedPath(new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2), 8);
            using var brush = new SolidBrush(e.Item.Selected ? AccentSoftHover : AccentSoft);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripMenuItem { Checked: true })
            {
                e.TextColor = Accent;
                e.TextFont = new Font("Segoe UI Semibold", e.TextFont?.Size ?? 10.5F);
            }
            else
            {
                e.TextColor = TextColor;
            }

            e.TextRectangle = new Rectangle(Point.Empty, e.Item.Size);
            e.TextFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
        }
    }

    // A label drawn as a rounded "pill" with a soft fill and border, text centred.
    private sealed class PillLabel : Label
    {
        public Color FillColor { get; set; } = AccentSoft;
        public Color OutlineColor { get; set; } = Color.FromArgb(147, 187, 250);

        public PillLabel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(Parent?.BackColor ?? SurfaceColor);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds = new RectangleF(1F, 1F, Width - 2.5F, Height - 2.5F);
            using (var path = RoundedMenuRenderer.CreateRoundedPath(bounds, bounds.Height / 2F))
            using (var fill = new SolidBrush(FillColor))
            using (var outline = new Pen(OutlineColor, 1.5F))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(outline, path);
            }

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    private sealed record FlashcardDetailRow(
        string Icon,
        Color BadgeColor,
        Color IconColor,
        string Title,
        string Value,
        bool Centered = false);

    // Flashcard back side: one row per detail with a round pastel icon badge, a
    // small grey title and the value below it (the word drawn in red).
    private sealed class FlashcardDetailsView : Control
    {
        private const int BadgeSize = 30;
        private const int TextIndent = 42;
        public int RowGap { get; set; } = 10;
        private const int ColumnGap = 28;
        private const int MaxContentWidth = 940;
        private static readonly Font TitleFont = new("Segoe UI Semibold", 9F);
        private static readonly Font ValueFont = new(UiFontName, 12F);
        private static readonly Font IconFont = new("Segoe UI Emoji", 12F);
        private IReadOnlyList<FlashcardDetailRow> _rows = [];
        public int MaxColumns { get; set; } = 2;
        public int TwoColumnMinWidth { get; set; } = 620;
        private IReadOnlyList<string> _highlightKeys = [];

        public FlashcardDetailsView()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = SurfaceColor;
        }

        public void SetContent(IReadOnlyList<FlashcardDetailRow> rows, IReadOnlyList<string> highlightKeys)
        {
            _rows = rows;
            _highlightKeys = highlightKeys;
            Invalidate();
        }

        public int GetContentHeight(int width)
        {
            using var bitmap = new Bitmap(1, 1);
            using var graphics = Graphics.FromImage(bitmap);
            var cells = MeasureCells(graphics, Math.Min(width, MaxContentWidth), out _, out _);
            return cells.Count == 0 ? 0 : cells.Max(cell => cell.Top + cell.Height);
        }

        // Wide cards show the rows in two columns (filled left to right, top to bottom).
        private List<(FlashcardDetailRow Row, int Left, int Top, int Height, int TitleHeight, int ValueHeight)> MeasureCells(
            IDeviceContext graphics,
            int contentWidth,
            out int columns,
            out int columnWidth)
        {
            columns = MaxColumns > 1 && contentWidth >= TwoColumnMinWidth ? 2 : 1;
            columnWidth = (contentWidth - ColumnGap * (columns - 1)) / columns;
            var titleHeight = TextRenderer.MeasureText(graphics, "Ag", TitleFont).Height;
            var width = columnWidth;
            var measured = _rows.Select(row =>
            {
                var (lines, lineHeight) = LayoutHighlightedSegments(
                    graphics,
                    SplitHighlightedWord(row.Value, _highlightKeys),
                    ValueFont,
                    Math.Max(40, width - TextIndent));
                var valueHeight = lines.Count * lineHeight;
                var textWidth = Math.Max(
                    TextRenderer.MeasureText(graphics, row.Title, TitleFont).Width,
                    lines.Max(line => line.Sum(unit => unit.Width)));
                return (
                    Row: row,
                    Height: Math.Max(BadgeSize, titleHeight + valueHeight),
                    ValueHeight: valueHeight,
                    BlockWidth: Math.Min(width, TextIndent + textWidth));
            }).ToList();

            // Each column's block (as wide as its widest item) is centred in its half,
            // so both columns sit evenly in the card while staying left-aligned inside.
            // Rows pair up (left, right); a Centered row takes a band of its own in the middle.
            var bands = new List<List<(FlashcardDetailRow Row, int Height, int ValueHeight, int BlockWidth)>>();
            var index = 0;
            while (index < measured.Count)
            {
                var band = new List<(FlashcardDetailRow Row, int Height, int ValueHeight, int BlockWidth)> { measured[index++] };
                while (band.Count < columns &&
                       !band[0].Row.Centered &&
                       index < measured.Count &&
                       !measured[index].Row.Centered)
                {
                    band.Add(measured[index++]);
                }

                bands.Add(band);
            }

            var columnCount = columns;
            var columnOffsets = new int[columns];
            for (var column = 0; column < columns; column++)
            {
                var current = column;
                var widest = bands
                    .Where(band => band.Count == columnCount)
                    .Select(band => band[current].BlockWidth)
                    .DefaultIfEmpty(columnWidth)
                    .Max();
                columnOffsets[column] = column * (columnWidth + ColumnGap) + Math.Max(0, (columnWidth - widest) / 2);
            }

            var cells = new List<(FlashcardDetailRow Row, int Left, int Top, int Height, int TitleHeight, int ValueHeight)>();
            var top = 0;
            foreach (var band in bands)
            {
                var bandHeight = band.Max(item => item.Height);
                for (var column = 0; column < band.Count; column++)
                {
                    // A row on its own is centred by the real width of its icon and text.
                    var left = band[column].Row.Centered || (columns > 1 && band.Count == 1)
                        ? (contentWidth - band[column].BlockWidth) / 2
                        : columnOffsets[column];
                    cells.Add((band[column].Row, left, top, bandHeight, titleHeight, band[column].ValueHeight));
                }

                top += bandHeight + RowGap;
            }

            return cells;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            var contentWidth = Math.Min(Width, MaxContentWidth);
            var origin = (Width - contentWidth) / 2;
            foreach (var (row, cellLeft, y, height, titleHeight, valueHeight) in
                     MeasureCells(graphics, contentWidth, out _, out var columnWidth))
            {
                var left = origin + cellLeft;
                // Each cell is aligned to the top of its band, centred on its own content.
                var ownHeight = Math.Min(height, Math.Max(BadgeSize, titleHeight + valueHeight));
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var badge = new Rectangle(left, y + (ownHeight - BadgeSize) / 2, BadgeSize, BadgeSize);
                using (var badgeBrush = new SolidBrush(row.BadgeColor))
                {
                    graphics.FillEllipse(badgeBrush, badge);
                }

                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
                TextRenderer.DrawText(
                    graphics,
                    row.Icon,
                    IconFont,
                    badge,
                    row.IconColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                var textTop = y + (ownHeight - titleHeight - valueHeight) / 2;
                TextRenderer.DrawText(
                    graphics,
                    row.Title,
                    TitleFont,
                    new Point(left + TextIndent, textTop),
                    MutedTextColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                DrawHighlightedSegments(
                    graphics,
                    SplitHighlightedWord(row.Value, _highlightKeys),
                    ValueFont,
                    new Rectangle(left + TextIndent, textTop + titleHeight, columnWidth - TextIndent, valueHeight + 2),
                    BodyTextColor,
                    centerVertically: false,
                    centerHorizontally: false);
            }
        }
    }

    // A label that draws the given Chinese keys in red (plain Label when there are none).
    private sealed class HighlightLabel : Label
    {
        private IReadOnlyList<string> _highlightKeys = [];

        public IReadOnlyList<string> HighlightKeys
        {
            get => _highlightKeys;
            set
            {
                _highlightKeys = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var segments = _highlightKeys.Count == 0
                ? null
                : SplitHighlightedWord(Text, _highlightKeys);
            if (segments is null || !segments.Any(segment => segment.Highlight))
            {
                base.OnPaint(e);
                return;
            }

            var bounds = new Rectangle(
                Padding.Left,
                Padding.Top,
                Math.Max(1, ClientSize.Width - Padding.Horizontal),
                Math.Max(1, ClientSize.Height - Padding.Vertical));
            var centerVertically = TextAlign is ContentAlignment.MiddleCenter or
                ContentAlignment.MiddleLeft or ContentAlignment.MiddleRight;
            DrawHighlightedSegments(e.Graphics, segments, Font, bounds, ForeColor, centerVertically);
        }
    }

    // Keys are the Chinese runs of the word, with and without bracketed parts
    // (有(一)点儿 → 有一点儿, 有点儿; 太...了 → 太, 了), longest first.
    private static List<string> GetHighlightKeys(string chinese)
    {
        var withContent = chinese.Replace("(", "").Replace(")", "").Replace("（", "").Replace("）", "");
        var withoutContent = System.Text.RegularExpressions.Regex.Replace(chinese, @"[\(（][^\)）]*[\)）]", "");
        return new[] { withContent, withoutContent }
            .SelectMany(value => System.Text.RegularExpressions.Regex.Matches(value, @"\p{IsCJKUnifiedIdeographs}+"))
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(key => key.Length)
            .ToList();
    }

    private static List<(string Text, bool Highlight)> SplitHighlightedWord(string text, IReadOnlyList<string> keys)
    {
        var segments = new List<(string Text, bool Highlight)>();
        var plain = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            var key = keys.FirstOrDefault(candidate =>
                string.CompareOrdinal(text, index, candidate, 0, candidate.Length) == 0);
            if (key is null)
            {
                plain.Append(text[index]);
                index++;
                continue;
            }

            if (plain.Length > 0)
            {
                segments.Add((plain.ToString(), false));
                plain.Clear();
            }

            segments.Add((key, true));
            index += key.Length;
        }

        if (plain.Length > 0)
        {
            segments.Add((plain.ToString(), false));
        }

        return segments;
    }

    private void VocabularyGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            !IsEditableTextColumn(_grid.Columns[e.ColumnIndex].DataPropertyName))
        {
            return;
        }

        _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        _grid.BeginEdit(selectAll: false);
    }

    private void VocabularyGrid_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        _editingVocabularyCellValue = e.RowIndex >= 0 && e.ColumnIndex >= 0
            ? Convert.ToString(_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value, CultureInfo.CurrentCulture)
            : null;
    }

    private void VocabularyGrid_EditingControlShowing(
        object? sender,
        DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is not TextBox textBox || _grid.CurrentCell is not { } cell)
        {
            return;
        }

        textBox.ImeMode = cell.OwningColumn.DataPropertyName is
            nameof(VocabularyWord.Chinese) or
            nameof(VocabularyWord.ChineseExample)
                ? ImeMode.On
                : ImeMode.NoControl;
    }

    private void VocabularyGrid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.RowIndex >= _visibleWords.Count ||
            e.ColumnIndex < 0 ||
            e.ColumnIndex >= _grid.Columns.Count)
        {
            _editingVocabularyCellValue = null;
            return;
        }

        var column = _grid.Columns[e.ColumnIndex];
        if (!IsEditableTextColumn(column.DataPropertyName))
        {
            _editingVocabularyCellValue = null;
            return;
        }

        var word = _visibleWords[e.RowIndex];
        if (column.DataPropertyName == nameof(VocabularyWord.Vietnamese) &&
            string.IsNullOrWhiteSpace(word.Vietnamese))
        {
            // A word without a Vietnamese meaning is dropped on the next load.
            SetEditableVocabularyValue(word, column.DataPropertyName, _editingVocabularyCellValue ?? string.Empty);
            _editingVocabularyCellValue = null;
            _visibleWords.ResetItem(e.RowIndex);
            MessageBox.Show(
                this,
                "Nghĩa tiếng Việt không được để trống.",
                "Thông tin chưa hợp lệ",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SaveWords(_words.ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetEditableVocabularyValue(word, column.DataPropertyName, _editingVocabularyCellValue ?? string.Empty);
            MessageBox.Show(
                this,
                $"Không thể lưu nội dung đã sửa: {ex.Message}",
                "Lỗi lưu dữ liệu",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _editingVocabularyCellValue = null;
        }
    }

    private static void SetEditableVocabularyValue(VocabularyWord word, string propertyName, string value)
    {
        if (propertyName == nameof(VocabularyWord.Vietnamese))
        {
            word.Vietnamese = value;
        }
        else if (propertyName == nameof(VocabularyWord.English))
        {
            word.English = value;
        }
        else if (propertyName == nameof(VocabularyWord.HskLevel))
        {
            word.HskLevel = value;
        }
        else if (propertyName == nameof(VocabularyWord.EnglishExample))
        {
            word.EnglishExample = value;
        }
        else if (propertyName == nameof(VocabularyWord.ChineseExample))
        {
            word.ChineseExample = value;
        }
        else if (propertyName == nameof(VocabularyWord.EnglishGrammar))
        {
            word.EnglishGrammar = value;
        }
        else if (propertyName == nameof(VocabularyWord.VietnameseGrammar))
        {
            word.VietnameseGrammar = value;
        }
        else if (propertyName == nameof(VocabularyWord.VietnameseExample))
        {
            word.VietnameseExample = value;
        }
    }

    private void SpeakVocabularyCell(VocabularyWord word, string propertyName)
    {
        if (propertyName == nameof(VocabularyWord.Pinyin))
        {
            return;
        }

        var text = propertyName switch
        {
            nameof(VocabularyWord.Chinese) => word.Chinese,
            nameof(VocabularyWord.Vietnamese) => word.Vietnamese,
            nameof(VocabularyWord.English) => word.English,
            nameof(VocabularyWord.EnglishExample) => word.EnglishExample,
            nameof(VocabularyWord.ChineseExample) => word.ChineseExample,
            nameof(VocabularyWord.EnglishGrammar) => word.EnglishGrammar,
            nameof(VocabularyWord.VietnameseGrammar) => word.VietnameseGrammar,
            nameof(VocabularyWord.VietnameseExample) => word.VietnameseExample,
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var languageId = propertyName switch
        {
            nameof(VocabularyWord.Chinese) => 0x0004,
            nameof(VocabularyWord.Vietnamese) or
            nameof(VocabularyWord.VietnameseGrammar) or
            nameof(VocabularyWord.VietnameseExample) => 0x002A,
            nameof(VocabularyWord.English) => 0x0009,
            nameof(VocabularyWord.EnglishExample) or nameof(VocabularyWord.EnglishGrammar) => 0x0009,
            nameof(VocabularyWord.ChineseExample) => 0x0004,
            _ => 0
        };

        try
        {
            EnsureSpeechVoice(languageId);
            dynamic voice = _speechVoice!;
            voice.Speak(text, 3);
        }
        catch (Exception ex) when (
            ex is COMException or
            InvalidOperationException or
            Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or
            System.Reflection.TargetInvocationException)
        {
            ReleaseSpeechVoice();
            MessageBox.Show(
                this,
                $"Không thể phát âm bằng giọng {GetLanguageName(languageId)}: {ex.Message}",
                "Lỗi phát âm",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void EnsureSpeechVoice(int languageId)
    {
        if (_speechVoice is not null && _speechLanguageId == languageId)
        {
            return;
        }

        if (languageId == 0)
        {
            throw new InvalidOperationException("Không xác định được ngôn ngữ của ô đã chọn.");
        }

        if (_speechVoice is null)
        {
            var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice")
                ?? throw new InvalidOperationException("Windows Speech API (SAPI) không khả dụng trên máy này.");
            _speechVoice = Activator.CreateInstance(voiceType)
                ?? throw new InvalidOperationException("Không thể khởi tạo bộ đọc văn bản của Windows.");
        }

        object? voicesObject = null;
        object? selectedToken = null;

        try
        {
            dynamic voice = _speechVoice;
            voicesObject = voice.GetVoices("", "");
            dynamic voices = voicesObject;
            var voiceCount = Convert.ToInt32(voices.Count, CultureInfo.InvariantCulture);
            for (var index = 0; index < voiceCount; index++)
            {
                object? tokenObject = voices.Item(index);
                if (tokenObject is null)
                {
                    continue;
                }

                try
                {
                    dynamic token = tokenObject;
                    var languages = Convert.ToString(
                        token.GetAttribute("Language"),
                        CultureInfo.InvariantCulture) ?? string.Empty;
                    if (!HasLanguage(languages, languageId))
                    {
                        continue;
                    }

                    voice.Voice = tokenObject;
                    selectedToken = tokenObject;
                    tokenObject = null;
                    break;
                }
                finally
                {
                    ReleaseComObject(tokenObject);
                }
            }

            if (selectedToken is null)
            {
                throw new InvalidOperationException(
                    $"Windows chưa cài giọng đọc {GetLanguageName(languageId)}. Hãy cài ngôn ngữ tương ứng trong Settings > Time & language > Speech rồi thử lại.");
            }

            ReleaseComObject(_speechToken);
            _speechToken = selectedToken;
            selectedToken = null;
            _speechLanguageId = languageId;
        }
        finally
        {
            ReleaseComObject(selectedToken);
            ReleaseComObject(voicesObject);
        }
    }

    private static bool HasLanguage(string languageAttribute, int requestedLanguageId)
    {
        foreach (var language in languageAttribute.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (uint.TryParse(
                    language.Trim(),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var installedLanguageId) &&
                (installedLanguageId & 0x03FF) == requestedLanguageId)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetLanguageName(int languageId) => languageId switch
    {
        0x0004 => "tiếng Trung",
        0x002A => "tiếng Việt",
        0x0009 => "tiếng Anh",
        _ => "ngôn ngữ đã chọn"
    };

    private void ReleaseSpeechVoice()
    {
        ReleaseComObject(_speechToken);
        _speechToken = null;
        _speechLanguageId = null;
        ReleaseComObject(_speechVoice);
        _speechVoice = null;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private void ShowFlashcard()
    {
        if (_studyWords.Count == 0)
        {
            _flashcardFace.Text = "Tích chọn các từ trong danh sách để bắt đầu flashcard.";
            _flashcardFace.Font = new Font("Segoe UI", 16F);
            _flashcardFace.Visible = true;
            _flashcardBackLayout.Visible = false;
            CenterFlashcardCard();
            return;
        }

        if (_flashcardOrder.Count != _studyWords.Count || _flashcardOrderPosition < 0)
        {
            RefillFlashcardOrder();
        }

        _flashcardIndex = ((_flashcardIndex % _studyWords.Count) + _studyWords.Count) % _studyWords.Count;
        var word = _studyWords[_flashcardIndex];
        if (_showingMeaning)
        {
            _flashcardPinyin.Text = word.Pinyin;
            var (leftRows, rightRows, bottomRows) = BuildFlashcardDetailRows(word);
            var highlightKeys = GetHighlightKeys(word.Chinese);
            _flashcardDetailsLeft.SetContent(leftRows, highlightKeys);
            _flashcardDetailsRight.SetContent(rightRows, highlightKeys);
            _flashcardDetailsBottom.SetContent(bottomRows, highlightKeys);
            _flashcardFace.Visible = false;
            _flashcardBackLayout.Visible = true;
            CenterFlashcardMeaningContent();
        }
        else
        {
            _flashcardFace.Text = word.Chinese;
            _flashcardFace.Font = new Font("Microsoft YaHei UI", 48F, FontStyle.Bold);
            _flashcardFace.ForeColor = Accent;
            _flashcardFace.Visible = true;
            _flashcardBackLayout.Visible = false;
        }

        CenterFlashcardCard();
    }

    private void ToggleFlashcard(object? sender, EventArgs e)
    {
        if (_studyWords.Count == 0 || _flashcardFlipTimer.Enabled)
        {
            return;
        }

        _flashcardAnimationFrame = 0;
        _flashcardFlipTimer.Start();
    }

    private void SpeakFlashcardWord(object? sender, EventArgs e)
    {
        if (_studyWords.Count == 0)
        {
            return;
        }

        SpeakVocabularyCell(_studyWords[_flashcardIndex], nameof(VocabularyWord.Chinese));
    }

    private void FlashcardFlipTimer_Tick(object? sender, EventArgs e)
    {
        const int animationFrames = 10;
        _flashcardAnimationFrame++;
        var scale = Math.Abs(animationFrames / 2f - _flashcardAnimationFrame) / (animationFrames / 2f);
        // Front and back have different sizes: shrink with the old one, grow into the new one.
        var size = GetFlashcardCardSize();
        _flashcardCard.Width = Math.Max(2, (int)(size.Width * scale));
        _flashcardCard.Height = size.Height;
        _flashcardCard.Left = (_flashcardHost.ClientSize.Width - _flashcardCard.Width) / 2;
        _flashcardCard.Top = (_flashcardHost.ClientSize.Height - size.Height) / 2;

        if (_flashcardAnimationFrame == animationFrames / 2)
        {
            _showingMeaning = !_showingMeaning;
            ShowFlashcard();
        }

        if (_flashcardAnimationFrame >= animationFrames)
        {
            _flashcardFlipTimer.Stop();
            CenterFlashcardCard();
        }
    }

    private Size GetFlashcardCardSize()
    {
        // The card grows with the window (at least 620x300 when there is room).
        var hostWidth = _flashcardHost.ClientSize.Width;
        var hostHeight = _flashcardHost.ClientSize.Height;
        var width = Math.Max(2, Math.Min(hostWidth - 48, Math.Clamp((int)(hostWidth * 0.75), 620, 1100)));
        var height = Math.Max(2, Math.Min(hostHeight - 24, Math.Clamp((int)(hostHeight * 0.8), 300, 700)));
        if (_showingMeaning)
        {
            return new Size(width, height);
        }

        // The front only shows the word, so the card shrinks to fit it.
        var text = TextRenderer.MeasureText(
            string.IsNullOrEmpty(_flashcardFace.Text) ? " " : _flashcardFace.Text,
            _flashcardFace.Font,
            new Size(Math.Max(100, width - 120), 10_000),
            TextFormatFlags.WordBreak);
        return new Size(
            Math.Min(width, Math.Max(260, text.Width + 140)),
            Math.Min(height, Math.Max(180, text.Height + 110)));
    }

    private void CenterFlashcardCard()
    {
        if (_flashcardFlipTimer.Enabled)
        {
            return;
        }

        var size = GetFlashcardCardSize();
        _flashcardCard.Size = size;
        _flashcardCard.Location = new Point(
            (_flashcardHost.ClientSize.Width - size.Width) / 2,
            (_flashcardHost.ClientSize.Height - size.Height) / 2);
    }

    // Picks the largest pinyin font (56pt down to 24pt) whose roomy pill fits the
    // given space, applies it, and returns the pill size.
    private Size FitFlashcardPinyin(int maxWidth, int maxHeight)
    {
        var text = string.IsNullOrEmpty(_flashcardPinyin.Text) ? " " : _flashcardPinyin.Text;
        Size PillFor(Size textSize) => new(textSize.Width + textSize.Height * 2, (int)(textSize.Height * 1.6F));
        float fontSize = 56;
        Size pill;
        while (true)
        {
            using var probe = new Font("Segoe UI", fontSize, FontStyle.Bold);
            pill = PillFor(TextRenderer.MeasureText(text, probe));
            if (fontSize <= 24 || (pill.Width <= maxWidth && pill.Height <= maxHeight))
            {
                break;
            }

            fontSize -= 2;
        }

        if (Math.Abs(_flashcardPinyin.Font.Size - fontSize) > 0.1F)
        {
            var old = _flashcardPinyin.Font;
            _flashcardPinyin.Font = new Font("Segoe UI", fontSize, FontStyle.Bold);
            old.Dispose();
        }

        return new Size(Math.Min(pill.Width, maxWidth), pill.Height);
    }

    private void CenterFlashcardMeaningContent()
    {
        if (_flashcardBackLayout.ClientSize.Width <= 0 || _flashcardBackLayout.ClientSize.Height <= 0)
        {
            return;
        }

        var maximumTextWidth = Math.Max(100, _flashcardBackLayout.ClientSize.Width - 32);
        _flashcardPinyin.MaximumSize = new Size(maximumTextWidth, 0);
        var width = _flashcardBackLayout.ClientSize.Width;
        var height = _flashcardBackLayout.ClientSize.Height;
        const int sidePadding = 24;
        const int gap = 28;
        const int bottomMargin = 12;
        var sideWidth = Math.Min(360, (int)((width - 2 * sidePadding) * 0.28));
        var middleWidth = width - 2 * (sidePadding + sideWidth + gap);
        var wide = sideWidth >= 220 && middleWidth >= 300;
        var pinyinSize = FitFlashcardPinyin(wide ? middleWidth : maximumTextWidth, wide ? height / 2 : height / 3);
        if (wide)
        {
            // Wide card: grammar | big pinyin pill | examples, meanings along the bottom.
            var leftHeight = _flashcardDetailsLeft.GetContentHeight(sideWidth);
            var rightHeight = _flashcardDetailsRight.GetContentHeight(sideWidth);
            var bottomHeight = _flashcardDetailsBottom.GetContentHeight(middleWidth);
            var bottomTop = Math.Max(pinyinSize.Height + 16, height - bottomMargin - bottomHeight);
            // The pinyin is centred in the space above the meanings, a little high.
            var pinyinTop = Math.Max(0, Math.Min((bottomTop - pinyinSize.Height) / 2 - 8, bottomTop - pinyinSize.Height - 16));
            _flashcardPinyin.Bounds = new Rectangle(
                (width - pinyinSize.Width) / 2,
                pinyinTop,
                pinyinSize.Width,
                pinyinSize.Height);
            _flashcardDetailsBottom.Bounds = new Rectangle(
                (width - middleWidth) / 2,
                bottomTop,
                middleWidth,
                Math.Max(0, Math.Min(bottomHeight, height - bottomTop)));
            _flashcardDetailsLeft.Bounds = new Rectangle(
                sidePadding,
                Math.Max(0, (height - leftHeight) / 2),
                sideWidth,
                Math.Min(height, leftHeight));
            _flashcardDetailsRight.Bounds = new Rectangle(
                width - sidePadding - sideWidth,
                Math.Max(0, (height - rightHeight) / 2),
                sideWidth,
                Math.Min(height, rightHeight));
            return;
        }

        // Narrow card: pinyin on top, then the meanings, grammar and examples.
        var stackWidth = maximumTextWidth;
        var bottomStack = _flashcardDetailsBottom.GetContentHeight(stackWidth);
        var leftStack = _flashcardDetailsLeft.GetContentHeight(stackWidth);
        var rightStack = _flashcardDetailsRight.GetContentHeight(stackWidth);
        var totalHeight = pinyinSize.Height + 14 + bottomStack + 10 + leftStack + 10 + rightStack;
        var top = Math.Max(0, (height - totalHeight) / 2);
        _flashcardPinyin.Bounds = new Rectangle((width - pinyinSize.Width) / 2, top, pinyinSize.Width, pinyinSize.Height);
        top += pinyinSize.Height + 14;
        _flashcardDetailsBottom.Bounds = new Rectangle(16, top, stackWidth, bottomStack);
        top += bottomStack + 10;
        _flashcardDetailsLeft.Bounds = new Rectangle(16, top, stackWidth, leftStack);
        top += leftStack + 10;
        _flashcardDetailsRight.Bounds = new Rectangle(16, top, stackWidth, rightStack);
    }

    private void MoveFlashcard(int offset)
    {
        if (_studyWords.Count == 0)
        {
            return;
        }

        _flashcardFlipTimer.Stop();
        if (_flashcardOrder.Count != _studyWords.Count || _flashcardOrderPosition < 0)
        {
            RefillFlashcardOrder();
        }

        _flashcardOrderPosition = (_flashcardOrderPosition + offset + _flashcardOrder.Count) %
                                  _flashcardOrder.Count;
        if (offset > 0 && _flashcardOrderPosition == 0)
        {
            RefillFlashcardOrder();
        }

        _flashcardIndex = _flashcardOrder[_flashcardOrderPosition];
        _showingMeaning = false;
        ShowFlashcard();
    }

    private void RefillFlashcardOrder()
    {
        var previousIndex = _flashcardIndex;
        _flashcardOrder.Clear();
        for (var index = 0; index < _studyWords.Count; index++)
        {
            _flashcardOrder.Add(index);
        }

        ShuffleWordIndexes(_flashcardOrder, previousIndex);
        _flashcardOrderPosition = 0;
        _flashcardIndex = _flashcardOrder[0];
    }

    private void ShowNextQuestion()
    {
        var practiceWords = PracticeWords;
        if (practiceWords.Count == 0)
        {
            _practiceMeaning.Text = "Chưa chọn từ học";
            _practiceEnglishMeaning.Text = string.Empty;
            _practiceFeedback.Text = "Tích chọn các từ trong danh sách để bắt đầu luyện gõ.";
            _practiceTimerLabel.Text = string.Empty;
            UpdatePracticeScore();
            _answerBox.Clear();
            _answerBox.Enabled = false;
            _pausePracticeButton.Enabled = false;
            _skipPracticeButton.Enabled = false;
            _practiceTimer.Stop();
            _questionStopwatch.Reset();
            return;
        }

        _answerBox.Enabled = true;
        _pausePracticeButton.Enabled = true;
        _skipPracticeButton.Enabled = true;
        if (_questionOrderPosition >= _questionOrder.Count)
        {
            RefillQuestionOrder();
        }

        _questionIndex = _questionOrder[_questionOrderPosition++];

        var (prompt, detail) = GetPracticePrompt(practiceWords[_questionIndex]);
        _practiceMeaning.Text = prompt;
        _practiceEnglishMeaning.Text = detail;
        // In example mode the word is drawn in red in the hint and in the shown answer.
        IReadOnlyList<string> highlightKeys = _practiceExampleMode
            ? GetHighlightKeys(practiceWords[_questionIndex].Chinese)
            : [];
        _practiceEnglishMeaning.HighlightKeys = highlightKeys;
        _practiceFeedback.HighlightKeys = highlightKeys;
        _practiceFeedback.Text = string.Empty;
        _practiceFeedback.ForeColor = BodyTextColor;
        _answerChecked = false;
        _answerBox.Clear();
        _questionStopwatch.Restart();
        if (_tabs?.SelectedTab == _practicePage)
        {
            _practiceTimer.Start();
        }
        else
        {
            _questionStopwatch.Stop();
        }

        _answerBox.Focus();
    }

    private void RefillQuestionOrder()
    {
        _questionOrder.Clear();
        for (var index = 0; index < PracticeWords.Count; index++)
        {
            _questionOrder.Add(index);
        }

        ShuffleWordIndexes(_questionOrder, _questionIndex);
        _questionOrderPosition = 0;
    }

    private void ShuffleWordIndexes(List<int> indexes, int previousIndex)
    {
        for (var index = indexes.Count - 1; index > 0; index--)
        {
            var swapIndex = _random.Next(index + 1);
            (indexes[index], indexes[swapIndex]) = (indexes[swapIndex], indexes[index]);
        }

        if (indexes.Count > 1 && indexes[0] == previousIndex)
        {
            var swapIndex = _random.Next(1, indexes.Count);
            (indexes[0], indexes[swapIndex]) = (indexes[swapIndex], indexes[0]);
        }
    }

    private void AnswerBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        CheckAnswer(sender, e);
    }

    private void AnswerBox_TextChanged(object? sender, EventArgs e)
    {
        // Read out the Chinese characters the IME has just committed.
        var currentText = _answerBox.Text;
        var commonPrefix = 0;
        while (commonPrefix < currentText.Length &&
               commonPrefix < _lastAnswerText.Length &&
               currentText[commonPrefix] == _lastAnswerText[commonPrefix])
        {
            commonPrefix++;
        }

        var addedChinese = string.Concat(currentText[commonPrefix..].Where(character =>
            System.Text.RegularExpressions.Regex.IsMatch(character.ToString(), @"\p{IsCJKUnifiedIdeographs}")));
        _lastAnswerText = currentText;
        if (_answerBox.Enabled && addedChinese.Length > 0)
        {
            SpeakTypedChinese(addedChinese);
        }

        var practiceWords = PracticeWords;
        if (_questionIndex >= 0 &&
            _questionIndex < practiceWords.Count &&
            !_answerChecked &&
            IsPracticeAnswerCorrect(_answerBox.Text, practiceWords[_questionIndex]))
        {
            CompleteAnswer(timedOut: false);
        }
    }

    private void CheckAnswer(object? sender, EventArgs e)
    {
        CompleteAnswer(timedOut: false);
    }

    // Skipping shows (and reads) the answer, adds the word to the review list,
    // and moves on without counting it as right or wrong.
    private void SkipPracticeButton_Click(object? sender, EventArgs e)
    {
        var practiceWords = PracticeWords;
        if (_practicePaused || _answerChecked || _questionIndex < 0 || _questionIndex >= practiceWords.Count)
        {
            return;
        }

        var word = practiceWords[_questionIndex];
        _incorrectWordCounts[word.Chinese] = _incorrectWordCounts.GetValueOrDefault(word.Chinese) + 1;
        _typedCorrectWords.Remove(word.Chinese);
        _practiceFeedback.Text = $"Đã bỏ qua. Đáp án: {FormatPracticeAnswer(word)}";
        _practiceFeedback.ForeColor = Color.DarkOrange;
        _answerChecked = true;
        _answerBox.Enabled = false;
        _questionStopwatch.Stop();
        _advanceAt = Environment.TickCount64 + 1800;
        UpdatePracticeScore();
        SpeakTypedChinese(GetPracticeAnswer(word));
    }

    // Reads Chinese aloud while practising; a missing voice is reported only once.
    private void SpeakTypedChinese(string text)
    {
        if (_typingSpeechFailed || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            EnsureSpeechVoice(0x0004);
            dynamic voice = _speechVoice!;
            voice.Speak(text, 3);
        }
        catch (Exception ex) when (
            ex is COMException or
            InvalidOperationException or
            Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or
            System.Reflection.TargetInvocationException)
        {
            _typingSpeechFailed = true;
            ReleaseSpeechVoice();
            MessageBox.Show(
                this,
                $"Không thể phát âm bằng giọng {GetLanguageName(0x0004)}: {ex.Message}",
                "Lỗi phát âm",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void CompleteAnswer(bool timedOut)
    {
        var practiceWords = PracticeWords;
        if (_questionIndex < 0 || _questionIndex >= practiceWords.Count || _answerChecked)
        {
            return;
        }

        if (!timedOut && _questionStopwatch.ElapsedMilliseconds >= PracticeDurationMilliseconds)
        {
            timedOut = true;
        }

        var word = practiceWords[_questionIndex];
        var isCorrect = !timedOut && IsPracticeAnswerCorrect(_answerBox.Text, word);
        if (isCorrect)
        {
            _correctAnswerCount++;
            // A right answer clears the word from the saved missed list and
            // counts it towards its HSK level's progress.
            var wasMissed = _incorrectWordCounts.Remove(word.Chinese);
            _typedCorrectWords.Add(word.Chinese);
            _practiceFeedback.Text = wasMissed
                ? "Chính xác! Đã xóa từ này khỏi danh sách từ sai."
                : "Chính xác! Giỏi lắm.";
            _practiceFeedback.ForeColor = SuccessColor;
        }
        else
        {
            _wrongAnswerCount++;
            _incorrectWordCounts[word.Chinese] = _incorrectWordCounts.GetValueOrDefault(word.Chinese) + 1;
            _typedCorrectWords.Remove(word.Chinese);
            _practiceFeedback.Text = timedOut
                ? $"Hết giờ! Đáp án: {FormatPracticeAnswer(word)}"
                : $"Chưa đúng. Đáp án: {FormatPracticeAnswer(word)}";
            // In example mode the word inside the answer is red, so the rest stays dark.
            _practiceFeedback.ForeColor = _practiceExampleMode ? BodyTextColor : Color.Firebrick;
        }

        _answerChecked = true;
        _answerBox.Enabled = false;
        _questionStopwatch.Stop();
        _advanceAt = Environment.TickCount64 + 1200;
        UpdatePracticeScore();
    }

    // Every change to the missed list goes through here, so save it as well.
    private void UpdatePracticeScore()
    {
        SaveMissedWords();
        SaveTypedCorrectWords();
        _practiceScoreLabel.Text = $"Đúng: {_correctAnswerCount}     Sai: {_wrongAnswerCount}";
        _practiceScoreLabel.ForeColor = Color.FromArgb(48, 68, 88);
        _reviewIncorrectWordsButton.Text = $"Xem từ đã sai ({_incorrectWordCounts.Count})";
        _reviewIncorrectWordsButton.Enabled = _incorrectWordCounts.Count > 0;
    }

    private void ReviewIncorrectWordsButton_Click(object? sender, EventArgs e)
    {
        var missedWords = _words
            .Where(word => _incorrectWordCounts.ContainsKey(word.Chinese))
            .OrderByDescending(word => _incorrectWordCounts[word.Chinese])
            .ThenBy(word => word.Chinese, StringComparer.CurrentCulture)
            .ToList();
        if (missedWords.Count == 0)
        {
            MessageBox.Show(
                this,
                "Chưa có từ nào trả lời sai.",
                "Từ đã sai",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new Form
        {
            Text = "Danh sách từ đã trả lời sai",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(720, 360),
            Size = new Size(820, 440),
            Font = Font
        };
        var results = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Dock = DockStyle.Fill,
            ReadOnly = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };
        results.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Retest",
            HeaderText = "Ôn lại",
            FillWeight = 8,
            MinimumWidth = 60
        });
        results.Columns.Add("Chinese", "Tiếng Trung");
        results.Columns.Add("Pinyin", "Pinyin");
        results.Columns.Add("Vietnamese", "Nghĩa tiếng Việt");
        results.Columns.Add("English", "Nghĩa tiếng Anh");
        results.Columns.Add("WrongCount", "Số lần sai");
        foreach (DataGridViewColumn column in results.Columns)
        {
            column.ReadOnly = column.Name != "Retest";
        }

        results.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (results.IsCurrentCellDirty && results.CurrentCell?.OwningColumn.Name == "Retest")
            {
                results.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        var missedWordSelection = new List<VocabularyWord>();
        foreach (var word in missedWords)
        {
            var rowIndex = results.Rows.Add(
                false,
                word.Chinese,
                word.Pinyin,
                word.Vietnamese,
                word.English,
                _incorrectWordCounts[word.Chinese]);
            results.Rows[rowIndex].Tag = word;
        }

        var selectAllButton = new Button
        {
            Location = new Point(12, 8),
            Size = new Size(118, 32),
            Text = "Chọn tất cả"
        };
        selectAllButton.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in results.Rows)
            {
                row.Cells["Retest"].Value = true;
            }
        };
        var retestButton = new Button
        {
            Location = new Point(525, 8),
            Size = new Size(170, 32),
            Text = "Luyện lại đã chọn"
        };
        retestButton.Click += (_, _) =>
        {
            if (!GetSelectedMissedWords(results, missedWordSelection, dialog))
            {
                return;
            }

            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };
        var closeButton = new Button
        {
            DialogResult = DialogResult.Cancel,
            Size = new Size(82, 32),
            Margin = new Padding(4, 4, 12, 4),
            Text = "Đóng"
        };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 48 };
        var actionButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        actionButtons.Controls.AddRange([closeButton, retestButton]);
        footer.Controls.Add(selectAllButton);
        footer.Controls.Add(actionButtons);
        dialog.Controls.Add(results);
        dialog.Controls.Add(footer);
        dialog.CancelButton = closeButton;
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            StartPracticeWithWords(missedWordSelection);
        }
    }

    private static bool GetSelectedMissedWords(
        DataGridView results,
        List<VocabularyWord> selection,
        IWin32Window owner)
    {
        results.EndEdit();
        selection.Clear();
        foreach (DataGridViewRow row in results.Rows)
        {
            if (row.Cells["Retest"].Value is true && row.Tag is VocabularyWord word)
            {
                selection.Add(word);
            }
        }

        if (selection.Count > 0)
        {
            return true;
        }

        MessageBox.Show(
            owner,
            "Hãy tích chọn ít nhất một từ.",
            "Chưa chọn từ",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return false;
    }

    private void StartPracticeWithWords(IReadOnlyCollection<VocabularyWord> words)
    {
        _practiceReviewWords = words.ToList();
        _questionIndex = -1;
        _questionOrder.Clear();
        _questionOrderPosition = 0;
        _answerChecked = false;
        _practicePaused = false;
        _pausedAdvanceRemaining = 0;
        _pausePracticeButton.Text = "Tạm dừng";
        _practiceTimer.Stop();
        _questionStopwatch.Reset();
        _tabs!.SelectedTab = _practicePage;
        ShowNextQuestion();
    }

    private void PausePracticeButton_Click(object? sender, EventArgs e)
    {
        if (_questionIndex < 0 || _questionIndex >= PracticeWords.Count)
        {
            return;
        }

        if (!_practicePaused)
        {
            _practicePaused = true;
            _practiceTimer.Stop();
            if (_answerChecked)
            {
                _pausedAdvanceRemaining = Math.Max(0, _advanceAt - Environment.TickCount64);
            }
            else
            {
                _questionStopwatch.Stop();
            }

            _answerBox.Enabled = false;
            _pausePracticeButton.Text = "Tiếp tục";
            _practiceTimerLabel.Text = _answerChecked
                ? "Đã tạm dừng"
                : $"Đã tạm dừng · còn {Math.Max(0, (int)Math.Ceiling((PracticeDurationMilliseconds - _questionStopwatch.ElapsedMilliseconds) / 1000d))} giây";
            return;
        }

        _practicePaused = false;
        _pausePracticeButton.Text = "Tạm dừng";
        if (_answerChecked)
        {
            _advanceAt = Environment.TickCount64 + _pausedAdvanceRemaining;
        }
        else
        {
            _questionStopwatch.Start();
            _answerBox.Enabled = true;
        }

        if (_tabs?.SelectedTab == _practicePage)
        {
            _practiceTimer.Start();
        }
    }

    private void PracticeTimer_Tick(object? sender, EventArgs e)
    {
        if (_practicePaused)
        {
            return;
        }

        if (_questionIndex < 0 || _questionIndex >= PracticeWords.Count)
        {
            _practiceTimer.Stop();
            return;
        }

        if (_answerChecked)
        {
            if (Environment.TickCount64 >= _advanceAt)
            {
                ShowNextQuestion();
            }

            return;
        }

        var remainingMilliseconds = PracticeDurationMilliseconds - _questionStopwatch.ElapsedMilliseconds;
        if (remainingMilliseconds <= 0)
        {
            CompleteAnswer(timedOut: true);
            return;
        }

        var remainingSeconds = (int)Math.Ceiling(remainingMilliseconds / 1000d);
        _practiceTimerLabel.Text = $"Thời gian còn lại: {remainingSeconds} giây";
        _practiceTimerLabel.ForeColor = remainingSeconds <= 3 ? Color.Firebrick : Accent;
    }

    private static string FormatEnglishMeaning(string english) =>
        string.IsNullOrWhiteSpace(english) ? "(chưa có)" : english;

    // Left side: the Vietnamese details; right side: the Chinese example and English.
    // Left: grammar. Right: the three examples. Bottom (under the pinyin): the meanings.
    private static (List<FlashcardDetailRow> Left, List<FlashcardDetailRow> Right, List<FlashcardDetailRow> Bottom)
        BuildFlashcardDetailRows(VocabularyWord word)
    {
        var left = new List<FlashcardDetailRow>();
        var right = new List<FlashcardDetailRow>();
        var bottom = new List<FlashcardDetailRow>();
        void Add(List<FlashcardDetailRow> side, string icon, Color badge, Color iconColor, string title, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                side.Add(new FlashcardDetailRow(icon, badge, iconColor, title, value));
            }
        }

        Add(left, "🧩", Color.FromArgb(237, 233, 254), Color.FromArgb(124, 58, 237), "Ngữ pháp", word.EnglishGrammar);
        Add(left, "💡", Color.FromArgb(254, 249, 195), Color.FromArgb(202, 138, 4), "Ngữ pháp tiếng Việt", word.VietnameseGrammar);
        Add(right, "🏮", Color.FromArgb(254, 226, 226), Color.FromArgb(220, 38, 38), "Ví dụ tiếng Trung", word.ChineseExample);
        Add(right, "✏", Color.FromArgb(254, 243, 199), Color.FromArgb(217, 119, 6), "Ví dụ tiếng Anh", word.EnglishExample);
        Add(right, "🍀", Color.FromArgb(220, 252, 231), Color.FromArgb(22, 163, 74), "Ví dụ tiếng Việt", word.VietnameseExample);
        Add(bottom, "🌸", Color.FromArgb(252, 231, 243), Color.FromArgb(219, 39, 119), "Nghĩa tiếng Việt", word.Vietnamese);
        Add(bottom, "🌍", Color.FromArgb(219, 234, 254), Color.FromArgb(37, 99, 235), "Nghĩa tiếng Anh", FormatEnglishMeaning(word.English));
        return (left, right, bottom);
    }

    private void PracticeTab_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_tabs?.SelectedTab == _practicePage)
        {
            if (_practicePaused)
            {
                return;
            }

            if (_answerChecked && Environment.TickCount64 >= _advanceAt)
            {
                ShowNextQuestion();
            }
            else
            {
                if (!_answerChecked)
                {
                    _questionStopwatch.Start();
                }

                _practiceTimer.Start();
            }

            _answerBox.Focus();
            return;
        }

        _practiceTimer.Stop();
        _questionStopwatch.Stop();
    }

    private static DataGridViewTextBoxColumn CreateColumn(string property, string title) =>
        new()
        {
            DataPropertyName = property,
            HeaderText = title,
            Name = property,
            SortMode = DataGridViewColumnSortMode.Automatic,
            // Clearing a cell must store "" rather than DBNull in a string property.
            DefaultCellStyle = { DataSourceNullValue = string.Empty }
        };

    private static TabPage CreatePage(string title) =>
        new(title) { BackColor = PageColor, Padding = new Padding(4) };

    private static Label CreateTitle(string text) =>
        new()
        {
            Text = text,
            Font = new Font("Segoe UI Semibold", 17F),
            ForeColor = TextColor,
            TextAlign = ContentAlignment.MiddleCenter
        };

    private static Button CreateButton(
        string text,
        EventHandler onClick,
        bool secondary = false,
        bool danger = false)
    {
        var button = new RoundedButton { Text = text, Margin = new Padding(0, 0, 10, 0) };
        StyleButton(button, danger ? ButtonKind.Danger : secondary ? ButtonKind.Secondary : ButtonKind.Primary);
        button.Click += onClick;
        return button;
    }

    // Flat, borderless buttons with hover/pressed colours shared across the app.
    private static void StyleButton(Button button, ButtonKind kind)
    {
        var (back, hover, pressed, fore, border) = kind switch
        {
            ButtonKind.Primary => (Accent, AccentHover, AccentPressed, Color.White, AccentPressed),
            ButtonKind.Danger => (DangerSoft, DangerSoftHover, DangerSoftHover, DangerText, Color.FromArgb(248, 150, 150)),
            _ => (AccentSoft, AccentSoftHover, AccentSoftHover, Accent, Color.FromArgb(147, 187, 250))
        };
        // RoundedButton draws this as its outline so soft buttons stand out on light pages.
        button.FlatAppearance.BorderColor = border;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.BackColor = back;
        button.ForeColor = fore;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = pressed;
        button.Font = new Font("Segoe UI Semibold", 10F);
        button.Padding = new Padding(14, 7, 14, 7);
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    // A rounded dropdown that opens just below `owner` at the same width; picking an
    // item calls onSelect. Returns a function that marks the current item.
    private static Action<int> AttachRoundedMenu(Button owner, IReadOnlyList<string> labels, Action<int> onSelect)
    {
        var menu = new ContextMenuStrip
        {
            Font = new Font(UiFontName, 10.5F),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            DropShadowEnabled = false,
            Padding = new Padding(4),
            Renderer = new RoundedMenuRenderer()
        };
        // Rounded outline for the menu window itself.
        menu.SizeChanged += (_, _) =>
        {
            using var path = RoundedMenuRenderer.CreateRoundedPath(new RectangleF(0, 0, menu.Width, menu.Height), 10);
            menu.Region = new Region(path);
        };
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            menu.Items.Add(new ToolStripMenuItem(labels[i], null, (_, _) => onSelect(index))
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter
            });
        }

        owner.Click += (_, _) =>
        {
            foreach (ToolStripItem item in menu.Items)
            {
                item.Size = new Size(owner.Width - menu.Padding.Horizontal, 36);
            }

            menu.MinimumSize = new Size(owner.Width, 0);
            menu.MaximumSize = new Size(owner.Width, 0);
            menu.Show(owner, new Point(0, owner.Height + 4));
        };
        owner.Disposed += (_, _) => menu.Dispose();
        return selected =>
        {
            for (var i = 0; i < menu.Items.Count; i++)
            {
                ((ToolStripMenuItem)menu.Items[i]).Checked = i == selected;
            }
        };
    }

    // A white panel with a 1px border, used to group content into cards.
    private static Panel CreateCard(Control content, Padding padding)
    {
        var inner = new Panel { Dock = DockStyle.Fill, BackColor = SurfaceColor, Padding = padding };
        inner.Controls.Add(content);
        var border = new Panel { Dock = DockStyle.Fill, BackColor = BorderColor, Padding = new Padding(1) };
        border.Controls.Add(inner);
        return border;
    }

    private static bool IsValid(VocabularyWord word) =>
        ContainsChinese(word.Chinese) && !string.IsNullOrWhiteSpace(word.Vietnamese);

    private static bool MoveLegacyNotes(VocabularyWord word)
    {
        var changed = false;
        if (!string.IsNullOrWhiteSpace(word.Example))
        {
            if (ContainsChinese(word.Example))
            {
                if (string.IsNullOrWhiteSpace(word.ChineseExample))
                {
                    word.ChineseExample = word.Example;
                }
            }
            else if (string.IsNullOrWhiteSpace(word.EnglishExample))
            {
                word.EnglishExample = word.Example;
            }

            word.Example = string.Empty;
            changed = true;
        }

        // Grammar is shown in a single column (EnglishGrammar: English labels with
        // the Chinese words); older or imported notes fill it when it is empty.
        if (!string.IsNullOrWhiteSpace(word.Grammar))
        {
            if (string.IsNullOrWhiteSpace(word.EnglishGrammar))
            {
                word.EnglishGrammar = word.Grammar;
            }

            word.Grammar = string.Empty;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(word.EnglishGrammar) &&
            !string.IsNullOrWhiteSpace(word.ChineseGrammar))
        {
            word.EnglishGrammar = word.ChineseGrammar;
            changed = true;
        }

        return changed;
    }

    private static List<VocabularyWord> StarterWords() =>
    [
        new("你好", "nǐ hǎo", "xin chào", "hello"),
        new("谢谢", "xièxie", "cảm ơn", "thank you"),
        new("再见", "zàijiàn", "tạm biệt", "goodbye"),
        new("请", "qǐng", "mời, xin", "please"),
        new("对不起", "duìbuqǐ", "xin lỗi", "sorry"),
        new("没关系", "méi guānxi", "không sao", "it's okay"),
        new("是", "shì", "là, đúng", "to be; yes"),
        new("不是", "bú shì", "không phải", "is not; no"),
        new("我", "wǒ", "tôi", "I; me"),
        new("你", "nǐ", "bạn", "you"),
        new("他", "tā", "anh ấy", "he; him"),
        new("我们", "wǒmen", "chúng tôi, chúng ta", "we; us"),
        new("中国", "Zhōngguó", "Trung Quốc", "China"),
        new("中文", "Zhōngwén", "tiếng Trung", "Chinese language"),
        new("学习", "xuéxí", "học tập", "to study; to learn"),
        new("朋友", "péngyou", "bạn bè", "friend"),
        new("水", "shuǐ", "nước", "water"),
        new("吃饭", "chīfàn", "ăn cơm", "to eat a meal"),
        new("今天", "jīntiān", "hôm nay", "today"),
        new("明天", "míngtiān", "ngày mai", "tomorrow")
    ];

    private sealed record FilterOption(string Value, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed class VocabularyWord
    {
        public VocabularyWord()
        {
        }

        public VocabularyWord(
            string Chinese,
            string Pinyin,
            string Vietnamese,
            string English = "",
            string Example = "",
            string Grammar = "",
            string EnglishExample = "",
            string ChineseExample = "",
            string EnglishGrammar = "",
            string ChineseGrammar = "")
        {
            this.Chinese = Chinese;
            this.Pinyin = Pinyin;
            this.Vietnamese = Vietnamese;
            this.English = English;
            this.Example = Example;
            this.Grammar = Grammar;
            this.EnglishExample = EnglishExample;
            this.ChineseExample = ChineseExample;
            this.EnglishGrammar = EnglishGrammar;
            this.ChineseGrammar = ChineseGrammar;
        }

        public string Chinese { get; set; } = string.Empty;
        public string Pinyin { get; set; } = string.Empty;
        public string Vietnamese { get; set; } = string.Empty;
        public string English { get; set; } = string.Empty;
        public string Example { get; set; } = string.Empty;
        public string Grammar { get; set; } = string.Empty;
        public string EnglishExample { get; set; } = string.Empty;
        public string ChineseExample { get; set; } = string.Empty;
        public string EnglishGrammar { get; set; } = string.Empty;
        public string VietnameseGrammar { get; set; } = string.Empty;
        public string VietnameseExample { get; set; } = string.Empty;
        public string ChineseGrammar { get; set; } = string.Empty;
        public string HskLevel { get; set; } = string.Empty;
        [JsonIgnore]
        public bool IsSelected { get; set; }
    }
}
