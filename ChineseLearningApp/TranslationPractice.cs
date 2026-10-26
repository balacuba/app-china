using System.Text.Json;

namespace ChineseLearningApp;

// Paragraph translation practice. Google Gemini (with a free key made in the
// learner's Google account when they sign in to sync with Google, see GeminiAi)
// writes a short Chinese paragraph at the chosen HSK level (using some of the
// words being studied when it can), the learner types a Vietnamese translation,
// and Gemini marks it out of 10 with comments, corrections and a model translation.
internal sealed class TranslationPractice : Panel
{
    private const string TeacherRole =
        "Bạn là giáo viên tiếng Trung giỏi, dạy người Việt Nam. Bạn luôn chỉ trả lời bằng một đối tượng JSON hợp lệ, không thêm chữ nào khác.";

    // Everyday topics picked at random so new paragraphs do not repeat themselves.
    private static readonly string[] Topics =
    [
        "gia đình", "trường học", "đi chợ", "ăn uống", "thời tiết", "du lịch", "bạn bè", "công việc",
        "sở thích", "mua sắm", "giao thông", "sức khỏe", "ngày nghỉ cuối tuần", "sinh nhật", "thể thao",
        "nấu ăn", "thú cưng", "học tiếng Trung", "điện thoại", "phim ảnh", "âm nhạc", "nhà cửa", "mùa xuân", "tết"
    ];

    private static readonly Font ChineseFont = new("Microsoft YaHei UI", 18F);
    private static readonly Font PinyinFont = new("Segoe UI", 11.5F);

    private readonly Button[] _levelButtons;
    private readonly Button[] _lengthButtons;
    private readonly Button _newButton;
    private readonly Button _gradeButton;
    private readonly Button _answerButton;
    private readonly Button _pinyinButton;
    private readonly Button _speakButton;
    private readonly Label _topic = new();
    private readonly Label _chinese = new();
    private readonly Label _pinyin = new();
    private readonly TextBox _translation = new();
    private readonly RichTextBox _result = new();
    private readonly Label _status = new();
    private readonly TableLayoutPanel _layout;
    private readonly Form1.RoundedPanel _paragraphCard = new();
    private CancellationTokenSource? _busy;
    private int _level = 2;
    private int _length;

    // Paragraph lengths: the pill's name and how long to ask the AI for. Only a
    // number of sentences: asked for a character count, the AI spends its whole
    // (limited) reply counting characters and never writes the paragraph.
    private static readonly (string Name, string Size)[] Lengths =
    [
        ("Ngắn", "dài 2 đến 3 câu ngắn"),
        ("Vừa", "dài 4 đến 5 câu"),
        ("Dài", "dài 6 đến 8 câu")
    ];
    private bool _showPinyin;
    private string _reference = string.Empty;

    // Supplied by the main window: the words being studied, and reading Chinese aloud.
    public Func<IReadOnlyList<string>> StudyWords { get; set; } = () => [];
    public Action<string> Speak { get; set; } = _ => { };

    public TranslationPractice()
    {
        Dock = DockStyle.Fill;
        // A long paragraph can make the page taller than the window, so it scrolls.
        AutoScroll = true;
        BackColor = Form1.SurfaceColor;

        // Top row: level and length pills, and the button for a new paragraph.
        _levelButtons = Enumerable.Range(1, 9).Select(level => Pill($"HSK {level}", () => { _level = level; ShowChoices(); }, 72)).ToArray();
        _lengthButtons = Lengths.Select((length, index) => Pill(length.Name, () => { _length = index; ShowChoices(); }, 64)).ToArray();
        _newButton = Form1.CreateButton("✨  Đoạn văn mới", async (_, _) => await NewParagraphAsync());
        _newButton.Margin = new Padding(16, 0, 0, 0);
        var choices = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Form1.SurfaceColor, Margin = Padding.Empty };
        choices.Controls.Add(Caption("Trình độ"));
        choices.Controls.AddRange(_levelButtons);
        choices.Controls.Add(Caption("Độ dài", 18));
        choices.Controls.AddRange(_lengthButtons);
        choices.Controls.Add(_newButton);

        // The paragraph, on a soft blue card, with listen / pinyin buttons.
        _topic.AutoSize = true;
        _topic.Font = new Font("Segoe UI Semibold", 10F);
        _topic.ForeColor = Form1.Accent;
        _topic.BackColor = Form1.AccentSoft;
        _chinese.Font = ChineseFont;
        _chinese.ForeColor = Form1.TextColor;
        _chinese.BackColor = Form1.AccentSoft;
        _chinese.Dock = DockStyle.Fill;
        _chinese.Text = "Bấm “Đoạn văn mới” để AI viết một đoạn văn ngắn cho bạn dịch.";
        _pinyin.Font = PinyinFont;
        _pinyin.ForeColor = Form1.MutedTextColor;
        _pinyin.BackColor = Form1.AccentSoft;
        _pinyin.Dock = DockStyle.Fill;
        _speakButton = Form1.CreateButton("🔊  Nghe", (_, _) => Speak(_chinese.Text), secondary: true);
        _pinyinButton = Form1.CreateButton("Hiện pinyin", (_, _) => { _showPinyin = !_showPinyin; ShowPinyin(); }, secondary: true);
        // Fixed-size pills: left to size themselves they come out taller than the
        // card's top row and lose their bottom edge.
        foreach (var (button, width) in new[] { (_speakButton, 104), (_pinyinButton, 124) })
        {
            button.AutoSize = false;
            button.Size = new Size(width, 34);
            button.Padding = Padding.Empty;
            button.Margin = new Padding(8, 2, 0, 0);
            ((Form1.RoundedButton)button).Radius = 17;
        }

        var cardTools = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Form1.AccentSoft, Dock = DockStyle.Right, Margin = Padding.Empty };
        cardTools.Controls.AddRange([_speakButton, _pinyinButton]);
        var cardTop = new Panel { Dock = DockStyle.Fill, BackColor = Form1.AccentSoft };
        cardTop.Controls.Add(_topic);
        cardTop.Controls.Add(cardTools);
        var cardBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Form1.AccentSoft };
        cardBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        cardBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        cardBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        cardBody.Controls.Add(cardTop, 0, 0);
        cardBody.Controls.Add(_chinese, 0, 1);
        cardBody.Controls.Add(_pinyin, 0, 2);
        _paragraphCard.Dock = DockStyle.Fill;
        _paragraphCard.Radius = 16;
        _paragraphCard.FillColor = Form1.AccentSoft;
        _paragraphCard.OutlineColor = Color.FromArgb(251, 203, 180);
        _paragraphCard.Padding = new Padding(20, 12, 20, 12);
        _paragraphCard.Controls.Add(cardBody);
        // The card grows with the paragraph: rows are sized to the wrapped text.
        _paragraphCard.Resize += (_, _) => FitParagraph();

        // The learner's translation.
        _translation.Multiline = true;
        _translation.BorderStyle = BorderStyle.None;
        _translation.Font = new Font(Form1.UiFontName, 12F);
        _translation.ForeColor = Form1.TextColor;
        _translation.ScrollBars = ScrollBars.Vertical;
        _translation.Dock = DockStyle.Fill;
        _translation.PlaceholderText = "Gõ bản dịch tiếng Việt của bạn ở đây…";
        var translationFrame = new Form1.RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 14,
            OutlineColor = Form1.Accent,
            Padding = new Padding(14, 10, 14, 10)
        };
        translationFrame.Controls.Add(_translation);

        _gradeButton = Form1.CreateButton("✅  Chấm bài", async (_, _) => await GradeAsync());
        _answerButton = Form1.CreateButton("👁  Xem bản dịch mẫu", (_, _) => ShowReference(), secondary: true);
        _status.AutoSize = true;
        _status.ForeColor = Form1.MutedTextColor;
        _status.Margin = new Padding(8, 10, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Form1.SurfaceColor, Margin = Padding.Empty };
        actions.Controls.AddRange([_gradeButton, _answerButton, _status]);

        // Marking, corrections and the model translation.
        _result.ReadOnly = true;
        _result.BorderStyle = BorderStyle.None;
        _result.BackColor = Form1.SurfaceColor;
        _result.Font = new Font(Form1.UiFontName, 11F);
        _result.Dock = DockStyle.Fill;
        _result.DetectUrls = false;

        _layout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 6, Padding = new Padding(24, 16, 24, 16), BackColor = Form1.SurfaceColor };
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));
        _layout.Controls.Add(choices, 0, 0);
        _layout.Controls.Add(_paragraphCard, 0, 1);
        _layout.Controls.Add(Caption("Bản dịch của bạn (tiếng Việt)", 0, top: 10), 0, 2);
        _layout.Controls.Add(translationFrame, 0, 3);
        _layout.Controls.Add(actions, 0, 4);
        _layout.Controls.Add(_result, 0, 5);
        Controls.Add(_layout);

        ShowChoices();
        ShowPinyin();
        SetBusy(false, ReadyText());
    }

    // Whether Gemini can be used, for the status line before anything is asked.
    public static string ReadyText() =>
        TutorAi.HasKey ? "✅ Gemini sẵn sàng (API key lấy tự động từ tài khoản Google của bạn)."
        : GeminiAi.ThroughServer ? "✅ Dùng Gemini qua tài khoản Google đã đăng nhập đồng bộ; API key riêng được lấy tự động."
        : "Đăng nhập đồng bộ bằng Google (Cài đặt → Đồng bộ với web) để dùng Dịch đoạn văn. Không cần nhập API key.";

    private static Label Caption(string text, int left = 0, int top = 9) => new()
    {
        AutoSize = true,
        Text = text,
        Font = new Font("Segoe UI Semibold", 10F),
        ForeColor = Form1.BodyTextColor,
        Margin = new Padding(left, top, 8, 0)
    };

    private static Button Pill(string text, Action onClick, int width)
    {
        var button = Form1.CreateButton(text, (_, _) => onClick(), secondary: true);
        button.AutoSize = false;
        button.Size = new Size(width, 36);
        button.Margin = new Padding(0, 0, 6, 0);
        ((Form1.RoundedButton)button).Radius = 18;
        return button;
    }

    // The chosen level and length are filled in; the others stay soft.
    private void ShowChoices()
    {
        for (var index = 0; index < _levelButtons.Length; index++)
        {
            Mark(_levelButtons[index], index + 1 == _level);
        }

        for (var index = 0; index < _lengthButtons.Length; index++)
        {
            Mark(_lengthButtons[index], index == _length);
        }

        Relayout();
    }

    private static void Mark(Button button, bool chosen)
    {
        button.BackColor = chosen ? Form1.Accent : Form1.AccentSoft;
        button.ForeColor = chosen ? Color.White : Form1.Accent;
    }

    private void ShowPinyin()
    {
        _pinyinButton.Text = _showPinyin ? "Ẩn pinyin" : "Hiện pinyin";
        FitParagraph();
    }

    // Sizes the paragraph and pinyin rows to their wrapped text, and the card to both.
    private void FitParagraph()
    {
        if (_chinese.Parent is not TableLayoutPanel body || _layout is null)
        {
            return;
        }

        var width = Math.Max(100, body.ClientSize.Width);
        // Ask each label how tall it needs to be at this width, so the measure matches
        // how the label itself wraps (Chinese breaks between any two characters).
        // The labels lose their cell margin, so measure 6px narrower to be safe.
        var chineseHeight = _chinese.GetPreferredSize(new Size(width - 6, 0)).Height + 10;
        var pinyinHeight = _showPinyin && _pinyin.Text.Length > 0
            ? _pinyin.GetPreferredSize(new Size(width - 6, 0)).Height + 8
            : 0;
        body.RowStyles[1].Height = chineseHeight;
        body.RowStyles[2].Height = pinyinHeight;
        _layout.RowStyles[1].Height = 44 + chineseHeight + pinyinHeight + _paragraphCard.Padding.Vertical + 4;
        Relayout();
    }

    // The page is as tall as the window, or taller when the paragraph needs it: the
    // marking area gets whatever room is left, but never less than a readable height.
    // A long paragraph also gets a taller box to type its translation in.
    private void Relayout()
    {
        if (_layout is null)
        {
            return;
        }

        _layout.RowStyles[3].Height = _length == 2 ? 190 : 120;
        var above = _layout.Padding.Vertical + Enumerable.Range(0, 5).Sum(row => (int)_layout.RowStyles[row].Height);
        var result = Math.Max(300, ClientSize.Height - above);
        _layout.RowStyles[5].Height = result;
        _layout.Height = above + result;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Relayout();
    }

    private void SetBusy(bool busy, string status = "")
    {
        _newButton.Enabled = !busy;
        _gradeButton.Enabled = !busy && _reference.Length > 0;
        _answerButton.Enabled = !busy && _reference.Length > 0;
        _speakButton.Enabled = _reference.Length > 0;
        _pinyinButton.Enabled = _reference.Length > 0;
        _status.Text = status;
        UseWaitCursor = busy;
    }

    private async Task NewParagraphAsync()
    {
        _busy?.Cancel();
        _busy = new CancellationTokenSource();
        SetBusy(true, "⏳ Gemini đang viết đoạn văn…");
        // A couple of the learner's words are only offered, never required: forcing
        // them in (老师, 你好, 不客气 in a paragraph about the weather) made the
        // paragraphs stray into greetings and stop making sense.
        var words = StudyWords().Where(word => word.Length > 0).OrderBy(_ => Random.Shared.Next()).Take(2).ToList();
        var topic = Topics[Random.Shared.Next(Topics.Length)];
        var question =
            $"Viết một đoạn văn tiếng Trung giản thể {Lengths[_length].Size}, " +
            $"chỉ dùng từ vựng và ngữ pháp ở trình độ HSK {_level}{(_level >= 7 ? " (bậc cao cấp 7–9 của chuẩn HSK 3.0)" : string.Empty)}, về chủ đề \"{topic}\". Không cần đếm chữ. " +
            "Đoạn văn kể một tình huống cụ thể, liền mạch, có nhân vật và sự việc rõ ràng (ví dụ: ai, ở đâu, làm gì, vì sao), " +
            "các câu nối tiếp nhau hợp lý như một bài đọc trong giáo trình. Mọi câu đều phải nói về chủ đề; " +
            "không chào hỏi, không cảm ơn, không nói chuyện với người đọc, không chèn câu lạc đề. " +
            (words.Count > 0 ? $"Chỉ khi thật sự hợp với tình huống thì mới dùng từ sau, nếu không hợp thì bỏ qua: {string.Join("、", words)}. " : string.Empty) +
            "Trả về JSON: {\"topic\": \"tên chủ đề bằng tiếng Việt\", \"chinese\": \"đoạn văn chữ Hán\", " +
            "\"pinyin\": \"pinyin có dấu thanh của cả đoạn\", \"vietnamese\": \"bản dịch tiếng Việt tự nhiên, sát nghĩa\"}";
        try
        {
            var answer = await TutorAi.AskJsonAsync(TeacherRole, question, "⏳ Gemini đang viết đoạn văn…", new Progress<string>(text => _status.Text = text), _busy.Token);
            var chinese = answer.Text("chinese").Trim();
            if (chinese.Length == 0)
            {
                throw new AiException("AI chưa viết được đoạn văn. Hãy thử lại.");
            }

            _reference = answer.Text("vietnamese").Trim();
            _topic.Text = $"📝  Chủ đề: {answer.Text("topic").Trim()}   •   HSK {_level}";
            _chinese.Text = chinese;
            _pinyin.Text = answer.Text("pinyin").Trim();
            _translation.Clear();
            _result.Clear();
            FitParagraph();
            SetBusy(false);
            _translation.Focus();
        }
        catch (AiException ex)
        {
            SetBusy(false, "⚠ " + ex.Message);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // An answer in a shape nobody expected: say so rather than crash the app.
            SetBusy(false, "⚠ AI trả lời không đúng dạng. Hãy thử lại.");
        }
    }

    private async Task GradeAsync()
    {
        var translation = _translation.Text.Trim();
        if (translation.Length == 0)
        {
            _status.Text = "Hãy gõ bản dịch trước khi chấm.";
            _translation.Focus();
            return;
        }

        _busy?.Cancel();
        _busy = new CancellationTokenSource();
        SetBusy(true, "⏳ Gemini đang chấm bài…");
        var question =
            $"Đoạn văn tiếng Trung: {_chinese.Text}\n" +
            $"Bản dịch tham khảo: {_reference}\n" +
            $"Bản dịch tiếng Việt của học viên: {translation}\n\n" +
            "Hãy chấm bản dịch của học viên theo thang điểm 0 đến 10, dựa trên việc dịch đúng nghĩa, đủ ý và tự nhiên " +
            "(không trừ điểm vì cách diễn đạt khác nhưng đúng nghĩa). Viết nhận xét ngắn gọn, thân thiện bằng tiếng Việt. " +
            "Trả về JSON: {\"score\": số từ 0 đến 10, \"feedback\": \"nhận xét 1 đến 3 câu\", " +
            "\"mistakes\": [{\"chinese\": \"cụm chữ Hán bị dịch sai hoặc bỏ sót\", \"yours\": \"phần học viên đã dịch (để trống nếu bỏ sót)\", \"better\": \"cách dịch đúng\"}], " +
            "\"better_translation\": \"bản dịch hoàn chỉnh, sửa lại từ bản của học viên\"}";
        try
        {
            var answer = await TutorAi.AskJsonAsync(TeacherRole, question, "⏳ Gemini đang chấm bài…", new Progress<string>(text => _status.Text = text), _busy.Token);
            ShowGrade(answer);
            SetBusy(false);
        }
        catch (AiException ex)
        {
            SetBusy(false, "⚠ " + ex.Message);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // An answer in a shape nobody expected: say so rather than crash the app.
            SetBusy(false, "⚠ AI trả lời không đúng dạng. Hãy thử lại.");
        }
    }

    // Score with a word of encouragement, the comment, each correction, and the
    // corrected and model translations.
    private void ShowGrade(JsonElement answer)
    {
        var score = double.TryParse(answer.Text("score"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, 0, 10) : -1;
        var (cheer, colour) = score switch
        {
            >= 9 => ("Xuất sắc! 🎉", Color.FromArgb(22, 163, 74)),
            >= 7 => ("Tốt lắm! 👍", Color.FromArgb(22, 163, 74)),
            >= 5 => ("Khá rồi, cố thêm chút nữa 💪", Color.FromArgb(217, 119, 6)),
            >= 0 => ("Cần luyện thêm 📚", Form1.DangerText),
            _ => ("Đã chấm xong", Form1.Accent)
        };

        _result.Clear();
        Append(score >= 0 ? $"{score:0.#}/10   " : string.Empty, new Font(Form1.UiFontName, 20F, FontStyle.Bold), colour);
        Append(cheer + "\n", new Font("Segoe UI Semibold", 13F), colour);
        Append(answer.Text("feedback").Trim() + "\n", _result.Font, Form1.BodyTextColor);

        if (answer.TryGetProperty("mistakes", out var mistakes) && mistakes.ValueKind == JsonValueKind.Array && mistakes.GetArrayLength() > 0)
        {
            Append("\nCần sửa\n", new Font("Segoe UI Semibold", 11.5F), Form1.TextColor);
            foreach (var mistake in mistakes.EnumerateArray())
            {
                Append("•  " + mistake.Text("chinese"), new Font("Microsoft YaHei UI", 11F, FontStyle.Bold), Form1.TextColor);
                var yours = mistake.Text("yours").Trim();
                Append(yours.Length > 0 ? $"   bạn dịch: “{yours}”" : "   (bỏ sót)", _result.Font, Form1.DangerText);
                Append($"   →  {mistake.Text("better").Trim()}\n", _result.Font, Color.FromArgb(22, 163, 74));
            }
        }

        var better = answer.Text("better_translation").Trim();
        if (better.Length > 0)
        {
            Append("\nBản dịch của bạn sau khi sửa\n", new Font("Segoe UI Semibold", 11.5F), Form1.TextColor);
            Append(better + "\n", _result.Font, Form1.BodyTextColor);
        }

        AppendReference();
        _result.SelectionStart = 0;
        _result.ScrollToCaret();
    }

    private void ShowReference()
    {
        _result.Clear();
        AppendReference();
    }

    private void AppendReference()
    {
        Append("\nBản dịch mẫu\n", new Font("Segoe UI Semibold", 11.5F), Form1.TextColor);
        Append(_reference + "\n", _result.Font, Form1.BodyTextColor);
    }

    private void Append(string text, Font font, Color colour)
    {
        _result.SelectionStart = _result.TextLength;
        _result.SelectionLength = 0;
        _result.SelectionFont = font;
        _result.SelectionColor = colour;
        _result.AppendText(text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _busy?.Cancel();
            _busy?.Dispose();
        }

        base.Dispose(disposing);
    }
}
