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
    private static readonly Color Accent = Color.FromArgb(33, 103, 153);
    private static readonly Color PageColor = Color.FromArgb(245, 247, 250);
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
    private readonly Dictionary<string, bool> _columnVisibility = new(StringComparer.Ordinal);
    private readonly List<string> _columnOrder = new();
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
    private readonly Label _countLabel = new();
    private readonly Panel _flashcardHost = new();
    private readonly Panel _flashcardCard = new();
    private readonly Panel _flashcardBackLayout = new();
    private readonly Label _flashcardFace = new();
    private readonly Label _flashcardPinyin = new();
    private readonly Label _flashcardDetails = new();
    private readonly Label _practiceMeaning = new();
    private readonly Label _practiceEnglishMeaning = new();
    private readonly Label _practiceFeedback = new();
    private readonly Label _practiceTimerLabel = new();
    private readonly Label _practiceScoreLabel = new();
    private readonly Label _practiceHint = new();
    private readonly Button _reviewIncorrectWordsButton = new();
    private readonly Button _pausePracticeButton = new();
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
    private long PracticeDurationMilliseconds => _practiceDurationSeconds * 1000L;

    public Form1(string userName)
    {
        InitializeComponent();
        Text = $"Cùng học tiếng trung nhé '{userName}'";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(1000, 720);
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
    }

    private void BuildInterface()
    {
        var practicePage = BuildPracticePage();
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F),
            Padding = new Point(16, 7)
        };
        tabs.TabPages.Add(BuildVocabularyPage());
        tabs.TabPages.Add(BuildFlashcardPage());
        tabs.TabPages.Add(practicePage);
        tabs.TabPages.Add(BuildColumnSettingsPage());
        tabs.SelectedIndexChanged += PracticeTab_SelectedIndexChanged;
        _tabs = tabs;
        _practicePage = practicePage;
        Controls.Add(tabs);
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            Padding = new Padding(0, 6, 0, 0)
        };
        toolbar.Controls.Add(CreateButton("Nhập CSV / TXT", ImportVocabulary));
        toolbar.Controls.Add(CreateButton("Thêm từ", AddWord));
        toolbar.Controls.Add(CreateButton("Xóa từ", DeleteWord, secondary: true));
        toolbar.Controls.Add(CreateButton("Xóa toàn bộ", DeleteAllWords, secondary: true));
        _countLabel.AutoSize = true;
        _countLabel.ForeColor = Color.FromArgb(90, 100, 112);
        _countLabel.Margin = new Padding(14, 11, 0, 0);
        toolbar.Controls.Add(_countLabel);

        var searchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = Padding.Empty
        };
        searchRow.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Tìm từ:",
            Margin = new Padding(0, 7, 8, 0)
        });
        _searchBox.Width = 190;
        _searchBox.PlaceholderText = "Tiếng Trung, pinyin, nghĩa, ví dụ...";
        _searchBox.Margin = new Padding(0, 2, 12, 0);
        _searchBox.TextChanged += (_, _) => RefreshVocabulary();
        searchRow.Controls.Add(_searchBox);
        var selectAllButton = CreateButton("Tích chọn tất cả", SelectAllWords, secondary: true);
        var clearSelectionButton = CreateButton("Bỏ tích chọn tất cả", ClearWordSelection, secondary: true);
        foreach (var button in new[] { selectAllButton, clearSelectionButton })
        {
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.FlatAppearance.BorderSize = 2;
            button.FlatAppearance.BorderColor = Color.FromArgb(31, 78, 121);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(213, 229, 243);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(181, 210, 235);
            button.Margin = new Padding(0, 0, 10, 0);
            button.Padding = new Padding(12, 4, 12, 4);
        }

        searchRow.Controls.Add(selectAllButton);
        searchRow.Controls.Add(clearSelectionButton);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.ColumnHeadersHeight = 42;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ReadOnly = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _grid.CellClick += VocabularyGrid_CellClick;
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
            column.Visible = !_columnVisibility.TryGetValue(column.Name, out var visible) || visible;
            _columnVisibility[column.Name] = column.Visible;
            column.Tag = column.HeaderText;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.FillWeight = 1;
            column.ReadOnly = column.DataPropertyName is not
                (nameof(VocabularyWord.IsSelected) or
                 nameof(VocabularyWord.EnglishExample) or
                 nameof(VocabularyWord.ChineseExample) or
                 nameof(VocabularyWord.EnglishGrammar) or
                 nameof(VocabularyWord.VietnameseGrammar) or
                 nameof(VocabularyWord.VietnameseExample));
            if (IsFilterableColumn(column.DataPropertyName))
            {
                column.HeaderText = $"{column.HeaderText}  ▼";
            }
        }
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 239, 246);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.DefaultCellStyle.Font = new Font("Segoe UI", 11F);
        _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.DefaultCellStyle.BackColor = Color.White;
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(26, 45, 62);
        _grid.DefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(208, 230, 247);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(26, 45, 62);
        _grid.RowTemplate.Height = 38;

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Tích chọn những từ muốn học. Flashcard và luyện gõ chỉ dùng các từ đã tích. Nút tích/bỏ tích tất cả chỉ áp dụng cho các từ đang hiển thị sau khi tìm kiếm và lọc.",
            ForeColor = Color.FromArgb(90, 100, 112),
            TextAlign = ContentAlignment.MiddleLeft
        };

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(searchRow, 0, 1);
        layout.Controls.Add(_grid, 0, 2);
        layout.Controls.Add(hint, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildColumnSettingsPage()
    {
        var page = CreatePage("Cài đặt");
        var layout = new TableLayoutPanel
        {
            AutoScroll = true,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(28),
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12F),
            ForeColor = Color.FromArgb(90, 100, 112),
            Text = "Tích chọn cột muốn hiển thị trong danh sách từ vựng. Thay đổi được lưu tự động.",
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var columnOptions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            var checkBox = new CheckBox
            {
                AutoSize = true,
                Checked = column.Visible,
                Font = new Font("Segoe UI", 11F),
                Text = column.HeaderText,
                Tag = column.Name,
                Margin = new Padding(0, 8, 0, 8)
            };
            checkBox.CheckedChanged += ColumnSettingsCheckBox_CheckedChanged;
            columnOptions.Controls.Add(checkBox);
        }

        var practiceDurationSetting = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 16, 0, 8),
            WrapContents = false
        };
        practiceDurationSetting.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 11F),
            Margin = new Padding(0, 5, 8, 0),
            Text = "Thời gian mỗi câu luyện gõ (giây):"
        });
        var practiceDurationInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 300,
            Value = _practiceDurationSeconds,
            Width = 80,
            Font = new Font("Segoe UI", 11F)
        };
        practiceDurationInput.ValueChanged += PracticeDurationInput_ValueChanged;
        practiceDurationSetting.Controls.Add(practiceDurationInput);
        columnOptions.Controls.Add(practiceDurationSetting);

        layout.Controls.Add(columnOptions, 0, 1);

        var orderPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        var orderTitle = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Text = "Thứ tự cột (chọn cột rồi di chuyển):"
        };
        var orderList = new ListBox
        {
            DisplayMember = nameof(DataGridViewColumn.HeaderText),
            IntegralHeight = false,
            Location = new Point(0, 32),
            Size = new Size(320, 190)
        };
        RefreshColumnOrderList(orderList);
        var orderButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Location = new Point(332, 32),
            WrapContents = false
        };
        orderButtons.Controls.Add(CreateButton("Lên", (_, _) => MoveSelectedColumn(orderList, -1), secondary: true));
        orderButtons.Controls.Add(CreateButton("Xuống", (_, _) => MoveSelectedColumn(orderList, 1), secondary: true));
        orderPanel.Controls.Add(orderTitle);
        orderPanel.Controls.Add(orderList);
        orderPanel.Controls.Add(orderButtons);
        layout.Controls.Add(orderPanel, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private void RefreshColumnOrderList(ListBox list)
    {
        var selectedColumn = list.SelectedItem as DataGridViewColumn;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (DataGridViewColumn column in _grid.Columns.Cast<DataGridViewColumn>()
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
        _practiceHint.Text = $"Mỗi câu có {_practiceDurationSeconds} giây. Nhấn Enter để kiểm tra; sau đó câu tiếp theo sẽ hiện tự động.";

    private void ApplyColumnOrderFromList(ListBox list)
    {
        var orderedNames = list.Items.Cast<DataGridViewColumn>().Select(column => column.Name).ToList();
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
        _flashcardCard.BackColor = Color.FromArgb(224, 239, 252);
        _flashcardCard.BorderStyle = BorderStyle.FixedSingle;
        _flashcardCard.Cursor = Cursors.Hand;
        _flashcardCard.Click += ToggleFlashcard;
        _flashcardFace.Dock = DockStyle.Fill;
        _flashcardFace.Font = new Font("Segoe UI", 16F);
        _flashcardFace.ForeColor = Color.FromArgb(60, 70, 82);
        _flashcardFace.TextAlign = ContentAlignment.MiddleCenter;
        _flashcardFace.Cursor = Cursors.Hand;
        _flashcardFace.Click += ToggleFlashcard;
        _flashcardBackLayout.Dock = DockStyle.Fill;
        _flashcardBackLayout.Visible = false;
        _flashcardBackLayout.Resize += (_, _) => CenterFlashcardMeaningContent();
        _flashcardPinyin.AutoSize = false;
        _flashcardPinyin.Font = new Font("Segoe UI", 32F, FontStyle.Bold);
        _flashcardPinyin.ForeColor = Accent;
        _flashcardPinyin.TextAlign = ContentAlignment.MiddleCenter;
        _flashcardPinyin.Margin = Padding.Empty;
        _flashcardDetails.AutoSize = false;
        _flashcardDetails.Font = new Font("Segoe UI", 16F);
        _flashcardDetails.ForeColor = Color.FromArgb(60, 70, 82);
        _flashcardDetails.TextAlign = ContentAlignment.MiddleCenter;
        _flashcardDetails.Margin = Padding.Empty;
        _flashcardPinyin.Cursor = Cursors.Hand;
        _flashcardPinyin.Click += ToggleFlashcard;
        _flashcardDetails.Cursor = Cursors.Hand;
        _flashcardDetails.Click += ToggleFlashcard;
        _flashcardBackLayout.Click += ToggleFlashcard;
        _flashcardBackLayout.Controls.Add(_flashcardPinyin);
        _flashcardBackLayout.Controls.Add(_flashcardDetails);
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
            ForeColor = Color.FromArgb(90, 100, 112),
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
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 14));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 11));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 11));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 9));

        var title = CreateTitle("Nhìn nghĩa tiếng Việt và tiếng Anh, gõ từ tiếng Trung");
        title.Dock = DockStyle.Fill;
        _practiceMeaning.Dock = DockStyle.Fill;
        _practiceMeaning.Font = new Font("Segoe UI", 24F, FontStyle.Bold);
        _practiceMeaning.ForeColor = Accent;
        _practiceMeaning.TextAlign = ContentAlignment.MiddleCenter;
        _practiceEnglishMeaning.Dock = DockStyle.Fill;
        _practiceEnglishMeaning.Font = new Font("Segoe UI", 15F);
        _practiceEnglishMeaning.ForeColor = Color.FromArgb(90, 100, 112);
        _practiceEnglishMeaning.TextAlign = ContentAlignment.MiddleCenter;
        _answerBox.Anchor = AnchorStyles.None;
        _answerBox.Font = new Font("Microsoft YaHei UI", 22F);
        _answerBox.ImeMode = ImeMode.On;
        _answerBox.MaxLength = 100;
        _answerBox.Size = new Size(430, 48);
        _answerBox.TextAlign = HorizontalAlignment.Center;
        _answerBox.TextChanged += AnswerBox_TextChanged;
        _answerBox.KeyDown += AnswerBox_KeyDown;

        var continueStudyButton = CreateButton(
            "Học lại",
            (_, _) => StartPracticeWithWords(_words.ToList()),
            secondary: false);
        continueStudyButton.Anchor = AnchorStyles.None;
        _pausePracticeButton.Text = "Tạm dừng";
        _pausePracticeButton.Anchor = AnchorStyles.None;
        _pausePracticeButton.AutoSize = true;
        _pausePracticeButton.FlatStyle = FlatStyle.Flat;
        _pausePracticeButton.BackColor = Color.FromArgb(232, 239, 246);
        _pausePracticeButton.ForeColor = Color.FromArgb(35, 48, 62);
        _pausePracticeButton.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _pausePracticeButton.Padding = new Padding(12, 7, 12, 7);
        _pausePracticeButton.UseVisualStyleBackColor = false;
        _pausePracticeButton.Click += PausePracticeButton_Click;
        var practiceActions = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.None,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        practiceActions.Controls.AddRange([continueStudyButton, _pausePracticeButton]);
        _practiceTimerLabel.Dock = DockStyle.Fill;
        _practiceTimerLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _practiceTimerLabel.ForeColor = Accent;
        _practiceTimerLabel.TextAlign = ContentAlignment.MiddleCenter;
        _practiceScoreLabel.Dock = DockStyle.Fill;
        _practiceScoreLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _practiceScoreLabel.TextAlign = ContentAlignment.MiddleCenter;
        _reviewIncorrectWordsButton.Text = "Xem từ đã sai";
        _reviewIncorrectWordsButton.Anchor = AnchorStyles.None;
        _reviewIncorrectWordsButton.AutoSize = true;
        _reviewIncorrectWordsButton.Click += ReviewIncorrectWordsButton_Click;
        _practiceFeedback.Dock = DockStyle.Fill;
        _practiceFeedback.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        _practiceFeedback.TextAlign = ContentAlignment.TopCenter;
        _practiceHint.Text = $"Mỗi câu có {_practiceDurationSeconds} giây. Nhấn Enter để kiểm tra; sau đó câu tiếp theo sẽ hiện tự động.";
        _practiceHint.Dock = DockStyle.Fill;
        _practiceHint.ForeColor = Color.FromArgb(90, 100, 112);
        _practiceHint.TextAlign = ContentAlignment.TopCenter;

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(_practiceMeaning, 0, 1);
        layout.Controls.Add(_practiceEnglishMeaning, 0, 2);
        layout.Controls.Add(_answerBox, 0, 3);
        layout.Controls.Add(practiceActions, 0, 4);
        layout.Controls.Add(_practiceTimerLabel, 0, 5);
        layout.Controls.Add(_practiceScoreLabel, 0, 6);
        layout.Controls.Add(_reviewIncorrectWordsButton, 0, 7);
        layout.Controls.Add(_practiceFeedback, 0, 8);
        layout.Controls.Add(_practiceHint, 0, 9);
        page.Controls.Add(layout);
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

    private void DeleteWord(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem is not VocabularyWord word)
        {
            MessageBox.Show(this, "Hãy chọn một từ trong danh sách.", "Chưa chọn từ",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this, $"Bạn có chắc muốn xóa “{word.Chinese}” không?", "Xác nhận xóa",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        SaveAndReplace(_words.Where(item => item != word).ToList());
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

            _flashcardIndex = -1;
            _flashcardOrder.Clear();
            _flashcardOrderPosition = -1;
            _questionIndex = -1;
            _questionOrder.Clear();
            _questionOrderPosition = 0;
            RefreshVocabulary();
            RefreshStudyWords();
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

            UpdateVocabularyCount();
        }
        finally
        {
            _visibleWords.RaiseListChangedEvents = true;
            _refreshingVocabulary = false;
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

    private void ShowColumnFilter(DataGridViewColumn column)
    {
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

        UpdateVocabularyCount();
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
        UpdateVocabularyCount();
        RefreshStudyWords();
    }

    private void RefreshStudyWords()
    {
        _flashcardFlipTimer.Stop();
        _studyWords.Clear();
        var selectedWords = _words.Where(word => word.IsSelected).ToList();
        _studyWords.AddRange(selectedWords);
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

    private void UpdateVocabularyCount()
    {
        var selectedCount = _words.Count(word => word.IsSelected);
        _countLabel.Text = selectedCount == 0
            ? $"Hiển thị: {_visibleWords.Count}/{_words.Count} | Chưa chọn từ học"
            : $"Hiển thị: {_visibleWords.Count}/{_words.Count} | Đã chọn học: {selectedCount}";
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
        if (column.DataPropertyName is not
            (nameof(VocabularyWord.EnglishExample) or
             nameof(VocabularyWord.ChineseExample) or
             nameof(VocabularyWord.EnglishGrammar) or
             nameof(VocabularyWord.VietnameseGrammar) or
             nameof(VocabularyWord.VietnameseExample)))
        {
            _editingVocabularyCellValue = null;
            return;
        }

        var word = _visibleWords[e.RowIndex];
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
        if (propertyName == nameof(VocabularyWord.EnglishExample))
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
            _flashcardDetails.Text = FormatFlashcardDetails(word);
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
        _flashcardCard.Width = Math.Max(2, (int)(GetFlashcardCardSize().Width * scale));
        _flashcardCard.Left = (_flashcardHost.ClientSize.Width - _flashcardCard.Width) / 2;

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
        var width = Math.Max(2, Math.Min(620, _flashcardHost.ClientSize.Width - 48));
        var height = Math.Max(2, Math.Min(300, _flashcardHost.ClientSize.Height - 24));
        return new Size(width, height);
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

    private void CenterFlashcardMeaningContent()
    {
        if (_flashcardBackLayout.ClientSize.Width <= 0 || _flashcardBackLayout.ClientSize.Height <= 0)
        {
            return;
        }

        var maximumTextWidth = Math.Max(100, _flashcardBackLayout.ClientSize.Width - 32);
        var maximumTextSize = new Size(maximumTextWidth, 0);
        _flashcardDetails.MaximumSize = maximumTextSize;
        _flashcardPinyin.MaximumSize = maximumTextSize;
        var textFlags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
        var pinyinSize = TextRenderer.MeasureText(
            string.IsNullOrEmpty(_flashcardPinyin.Text) ? " " : _flashcardPinyin.Text,
            _flashcardPinyin.Font,
            new Size(maximumTextWidth, 10_000),
            textFlags);
        var detailsSize = TextRenderer.MeasureText(
            string.IsNullOrEmpty(_flashcardDetails.Text) ? " " : _flashcardDetails.Text,
            _flashcardDetails.Font,
            new Size(maximumTextWidth, 10_000),
            textFlags);
        var gap = 4;
        var totalHeight = pinyinSize.Height + gap + detailsSize.Height;
        var top = Math.Max(0, (_flashcardBackLayout.ClientSize.Height - totalHeight) / 2);
        _flashcardPinyin.Bounds = new Rectangle(16, top, maximumTextWidth, pinyinSize.Height);
        _flashcardDetails.Bounds = new Rectangle(
            16,
            top + pinyinSize.Height + gap,
            maximumTextWidth,
            detailsSize.Height);
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
            _practiceTimer.Stop();
            _questionStopwatch.Reset();
            return;
        }

        _answerBox.Enabled = true;
        _pausePracticeButton.Enabled = true;
        if (_questionOrderPosition >= _questionOrder.Count)
        {
            RefillQuestionOrder();
        }

        _questionIndex = _questionOrder[_questionOrderPosition++];

        _practiceMeaning.Text = practiceWords[_questionIndex].Vietnamese;
        _practiceEnglishMeaning.Text = FormatEnglishMeaning(practiceWords[_questionIndex].English);
        _practiceFeedback.Text = string.Empty;
        _practiceFeedback.ForeColor = Color.FromArgb(60, 70, 82);
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
        var practiceWords = PracticeWords;
        if (_questionIndex >= 0 &&
            _questionIndex < practiceWords.Count &&
            !_answerChecked &&
            string.Equals(_answerBox.Text.Trim(), practiceWords[_questionIndex].Chinese, StringComparison.Ordinal))
        {
            CompleteAnswer(timedOut: false);
        }
    }

    private void CheckAnswer(object? sender, EventArgs e)
    {
        CompleteAnswer(timedOut: false);
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
        var isCorrect = !timedOut &&
                        string.Equals(_answerBox.Text.Trim(), word.Chinese, StringComparison.Ordinal);
        if (isCorrect)
        {
            _correctAnswerCount++;
            _practiceFeedback.Text = "Chính xác! Giỏi lắm.";
            _practiceFeedback.ForeColor = Color.ForestGreen;
        }
        else
        {
            _wrongAnswerCount++;
            _incorrectWordCounts[word.Chinese] = _incorrectWordCounts.GetValueOrDefault(word.Chinese) + 1;
            var pinyin = string.IsNullOrWhiteSpace(word.Pinyin) ? string.Empty : $" ({word.Pinyin})";
            _practiceFeedback.Text = timedOut
                ? $"Hết giờ! Đáp án: {word.Chinese}{pinyin}"
                : $"Chưa đúng. Đáp án: {word.Chinese}{pinyin}";
            _practiceFeedback.ForeColor = Color.Firebrick;
        }

        _answerChecked = true;
        _answerBox.Enabled = false;
        _questionStopwatch.Stop();
        _advanceAt = Environment.TickCount64 + 1200;
        UpdatePracticeScore();
    }

    private void UpdatePracticeScore()
    {
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

    private static string FormatFlashcardDetails(VocabularyWord word)
    {
        var details = new List<string>
        {
            $"Tiếng Việt: {word.Vietnamese}",
            $"Tiếng Anh: {FormatEnglishMeaning(word.English)}"
        };
        if (!string.IsNullOrWhiteSpace(word.EnglishExample))
        {
            details.Add($"Ví dụ tiếng Anh: {word.EnglishExample}");
        }

        if (!string.IsNullOrWhiteSpace(word.ChineseExample))
        {
            details.Add($"Ví dụ tiếng Trung: {word.ChineseExample}");
        }

        if (!string.IsNullOrWhiteSpace(word.VietnameseExample))
        {
            details.Add($"Ví dụ tiếng Việt: {word.VietnameseExample}");
        }

        if (!string.IsNullOrWhiteSpace(word.EnglishGrammar))
        {
            details.Add($"Ngữ pháp: {word.EnglishGrammar}");
        }

        if (!string.IsNullOrWhiteSpace(word.VietnameseGrammar))
        {
            details.Add($"Ngữ pháp tiếng Việt: {word.VietnameseGrammar}");
        }

        return string.Join(Environment.NewLine, details);
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
            SortMode = DataGridViewColumnSortMode.Automatic
        };

    private static TabPage CreatePage(string title) =>
        new(title) { BackColor = PageColor, Padding = new Padding(4) };

    private static Label CreateTitle(string text) =>
        new()
        {
            Text = text,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 48, 62),
            TextAlign = ContentAlignment.MiddleCenter
        };

    private static Button CreateButton(string text, EventHandler onClick, bool secondary = false)
    {
        var button = new Button
        {
            AutoSize = true,
            BackColor = secondary ? Color.FromArgb(232, 239, 246) : Accent,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = secondary ? Color.FromArgb(35, 48, 62) : Color.White,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(12, 7, 12, 7),
            Text = text,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += onClick;
        return button;
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
