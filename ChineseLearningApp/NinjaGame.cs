using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace ChineseLearningApp;

// One word in the bubble game: its Chinese and pinyin, and the caller's own
// object (handed back when the word is hit or lands).
internal sealed record GameWord(string Chinese, string Pinyin, object Tag);

// What falls from the sky, chosen on the start screen.
internal enum NinjaGameMode
{
    Hanzi,
    HanziPinyin,
    Pinyin
}

// "Mèo bắn bong bóng": words float down through a night sky inside soap bubbles.
// Typing a word marks it; the cat on the roof runs under it and throws a ball of yarn
// that pops the bubble (+10 × combo). A word that lands on the roof costs one of
// three lives. Every 8 hits is a new level: faster and more words. Answers are
// always the Chinese characters; when only characters are shown, their pinyin
// (no tones) counts too.
internal sealed class NinjaGame : Panel
{
    private const int MaxLives = 3;
    private const int HitsPerLevel = 8;
    private const float RoofHeight = 26;

    // Words further away than this (sideways) get a diagonal throw instead of a run.
    private const float DiagonalThrowDistance = 300;

    private static readonly Color[] TextColors =
    [
        Color.FromArgb(103, 232, 249), Color.FromArgb(244, 114, 182), Color.FromArgb(253, 224, 71),
        Color.FromArgb(134, 239, 172), Color.FromArgb(196, 181, 253), Color.FromArgb(253, 186, 116)
    ];

    private static readonly Color TypedColor = Color.FromArgb(248, 56, 56);
    private static readonly Font HudFont = new("Segoe UI Semibold", 12F);
    private static readonly Font HudEmojiFont = new("Segoe UI Emoji", 13F);
    private static readonly Font BigFont = new("Segoe UI Semibold", 24F);
    private static readonly Font MidFont = new("Segoe UI", 12F);
    private static readonly Font FloatFont = new("Segoe UI Semibold", 13F);

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    // Pause after typing before it is checked. When the text could still grow into
    // another falling word (你 → 你好, ni → nihao), the shorter word is only shot
    // once the learner has typed nothing for 2 seconds in all: this pause plus the
    // longer one that follows it.
    private const int AutoHitDelay = 350;
    private const int PrefixHitDelay = 2000 - AutoHitDelay;

    private readonly System.Windows.Forms.Timer _autoHit = new() { Interval = AutoHitDelay };
    private readonly Stopwatch _clock = new();
    private readonly Random _random = new();
    private readonly List<FallingWord> _words = [];
    private readonly List<Particle> _particles = [];
    private readonly List<FloatingText> _floats = [];
    private readonly List<Explosion> _explosions = [];
    private readonly List<PointF> _stars = [];
    private readonly List<Snowflake> _snow = [];
    private readonly TextBox _input = new();
    private readonly Button _startButton;
    private readonly Button[] _modeButtons;
    private double _lastTime;
    private double _spawnIn;
    private double _flash;
    private double _twinkle;

    // 0 = pale crescent, 1 = full blood moon; eases towards the lives left.
    private double _bloodMoon;
    private int _score;
    private int _combo;
    private int _lives = MaxLives;
    private int _hits;
    private bool _running;
    private bool _gameOver;
    private bool _prefixWaited;
    private string _composition = string.Empty;

    // The cat on the roof and the balls of yarn in flight.
    private readonly Queue<FallingWord> _targets = new();
    private readonly List<Ball> _balls = [];
    private FallingWord? _aiming;
    private float _ninjaX = 200;
    private float _wanderTo = 200;
    private int _facing = 1;
    private bool _ninjaRunning;
    private double _runPhase;
    private double _throwPose;
    private double _hop;
    private double _hopSpeed;
    private double _squash;
    private double _nextIdleHop;
    private double _love;
    private PointF _yarnAt;

    // Words to play with (read each time a word drops in, so the list can change).
    public Func<IReadOnlyList<GameWord>> Words { get; set; } = () => [];

    // Called when a word is cut down (typed right) or lands (missed).
    public Action<GameWord>? Hit { get; set; }
    public Action<GameWord>? Landed { get; set; }

    public NinjaGameMode Mode { get; private set; } = NinjaGameMode.Hanzi;

    public int BestScore { get; set; }

    public const float DefaultFallSeconds = 12;
    public const int MinFallSeconds = 2;
    public const int MaxFallSeconds = 60;

    // Seconds a word takes to fall from the top to the roof at level 1, set in
    // Settings. New words also come more or less often to match.
    public float FallSeconds { get; set; } = DefaultFallSeconds;

    public const int MinTextScale = 50;
    public const int MaxTextScale = 200;

    // Size of the falling words in percent, set in Settings. The bubbles are built
    // round the words, so they and their pops scale too.
    // Words already falling keep their size; the next ones use the new one.
    public int TextScale
    {
        get => _textScale;
        set
        {
            _textScale = Math.Clamp(value, MinTextScale, MaxTextScale);
            RebuildFonts();
        }
    }

    // Extra size for the pinyin alone, in percent of what the text size gives it,
    // so pinyin can be made easier to read without growing the characters.
    public int PinyinScale
    {
        get => _pinyinScale;
        set
        {
            _pinyinScale = Math.Clamp(value, MinTextScale, MaxTextScale);
            RebuildFonts();
        }
    }

    private int _textScale = 100;
    private int _pinyinScale = 100;
    private Font _hanziFont;
    private Font _pinyinUnderFont;
    private Font _pinyinOnlyFont;

    private float SizeFactor => _textScale / 100f;

    private void RebuildFonts()
    {
        _hanziFont.Dispose();
        _pinyinUnderFont.Dispose();
        _pinyinOnlyFont.Dispose();
        (_hanziFont, _pinyinUnderFont, _pinyinOnlyFont) = CreateFonts(_textScale, _pinyinScale);
        Invalidate();
    }

    private static (Font Hanzi, Font PinyinUnder, Font PinyinOnly) CreateFonts(int scale, int pinyinScale) =>
    (
        new Font("Microsoft YaHei UI", 22F * scale / 100, FontStyle.Bold),
        new Font("Segoe UI", 11F * scale / 100 * pinyinScale / 100),
        new Font("Segoe UI Semibold", 17F * scale / 100 * pinyinScale / 100)
    );

    public NinjaGame()
    {
        (_hanziFont, _pinyinUnderFont, _pinyinOnlyFont) = CreateFonts(_textScale, _pinyinScale);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = Form1.SurfaceColor;

        // No answer box: typing goes into a box kept just outside the game, so it is
        // never seen but still takes keys and the Chinese input method. What has been
        // typed shows on the words themselves (in red) and in a small line above the roof.
        _input.BorderStyle = BorderStyle.None;
        _input.Font = new Font("Microsoft YaHei UI", 16F);
        _input.ImeMode = ImeMode.On;
        _input.Location = new Point(-400, -400);
        _input.Width = 300;
        // Typing is checked a moment after it stops, so a word the input method sends
        // in pieces (你 then 好) arrives whole before it can hit anything. Pinyin still
        // being composed in the input method counts too, so "nihao" fires at 你好
        // without picking characters or pressing Enter.
        _autoHit.Tick += (_, _) =>
        {
            _autoHit.Stop();
            TryHit(fromEnter: false);
        };
        _input.TextChanged += (_, _) =>
        {
            RestartAutoHit();
            CheckTyping();
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                TryHit(fromEnter: true);
                e.SuppressKeyPress = true;
            }
        };
        Controls.Add(_input);

        // Mode pills and the start button live on the start / game-over screen.
        (string Text, NinjaGameMode Mode)[] modes =
        [
            ("汉字", NinjaGameMode.Hanzi),
            ("汉字 + Pinyin", NinjaGameMode.HanziPinyin),
            ("Pinyin", NinjaGameMode.Pinyin)
        ];
        _modeButtons = modes.Select(item =>
        {
            var button = Form1.CreateButton(item.Text, (_, _) => SetMode(item.Mode), secondary: true);
            button.AutoSize = false;
            button.Size = new Size(150, 40);
            ClipToPill(button, 20);
            Controls.Add(button);
            return button;
        }).ToArray();

        _startButton = Form1.CreateButton("▶  Bắt đầu chơi", (_, _) => Start());
        _startButton.AutoSize = false;
        _startButton.Size = new Size(200, 46);
        ClipToPill(_startButton, 22);
        Controls.Add(_startButton);
        SetMode(NinjaGameMode.Hanzi);

        _timer.Tick += (_, _) => Step();
    }

    private RectangleF Sky => new(0, 0, Width, Math.Max(1, Height));

    // Clicking anywhere in the game puts the keyboard back on the hidden box.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _input.Focus();
    }

    private void RestartAutoHit()
    {
        _autoHit.Stop();
        _prefixWaited = false;
        _autoHit.Interval = AutoHitDelay;
        _autoHit.Start();
    }

    // Everything typed so far: committed text plus pinyin still being composed.
    private string Typed => (_input.Text + _composition).Trim();

    private int Level => 1 + _hits / HitsPerLevel;

    // Pill-shaped button; the region lets the night sky show at its corners.
    private static void ClipToPill(Button button, int radius)
    {
        ((Form1.RoundedButton)button).Radius = radius;
        using var pill = Form1.RoundedMenuRenderer.CreateRoundedPath(new RectangleF(0, 0, button.Width, button.Height), radius);
        button.Region = new Region(pill);
    }

    private void SetMode(NinjaGameMode mode)
    {
        Mode = mode;
        for (var index = 0; index < _modeButtons.Length; index++)
        {
            var selected = index == (int)mode;
            _modeButtons[index].BackColor = selected ? Form1.Accent : Form1.AccentSoft;
            _modeButtons[index].ForeColor = selected ? Color.White : Form1.Accent;
        }

        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _autoHit.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        var centreY = (int)(Sky.Height / 2);
        var modesWidth = _modeButtons.Sum(button => button.Width) + 12 * (_modeButtons.Length - 1);
        var left = (Width - modesWidth) / 2;
        foreach (var button in _modeButtons)
        {
            button.Location = new Point(left, centreY + 34);
            left += button.Width + 12;
        }

        _startButton.Location = new Point((Width - _startButton.Width) / 2, centreY + 92);

        // A fixed scatter of stars for this size.
        _stars.Clear();
        var random = new Random(7);
        for (var index = 0; index < 70; index++)
        {
            _stars.Add(new PointF((float)random.NextDouble() * Width, (float)random.NextDouble() * (Sky.Height - RoofHeight - 20)));
        }

        // Snow spread over the whole sky so it is already falling when the game starts.
        _snow.Clear();
        for (var index = 0; index < 90; index++)
        {
            _snow.Add(new Snowflake(
                (float)random.NextDouble() * Width,
                (float)random.NextDouble() * Sky.Height,
                3f + (float)random.NextDouble() * 4f,
                (float)(random.NextDouble() * Math.PI * 2)));
        }
    }

    public void Start()
    {
        ClearWords();
        _particles.Clear();
        _explosions.Clear();
        _floats.Clear();
        _targets.Clear();
        _balls.Clear();
        _aiming = null;
        _score = 0;
        _combo = 0;
        _lives = MaxLives;
        _hits = 0;
        _bloodMoon = 0;
        _gameOver = false;
        _spawnIn = 0.3;
        _ninjaX = _wanderTo = Width / 2f;
        _hop = _hopSpeed = _squash = _love = 0;
        _yarnAt = new PointF(_ninjaX, Height - RoofHeight - 30);
        ShowMenu(false);
        _input.Clear();
        _running = true;
        _clock.Restart();
        _lastTime = 0;
        _timer.Start();
        _input.Focus();
    }

    private void ShowMenu(bool visible)
    {
        _startButton.Visible = visible;
        foreach (var button in _modeButtons)
        {
            button.Visible = visible;
        }
    }

    // Pause while the page is hidden; carry on where it was when shown again.
    public void SetActive(bool active)
    {
        if (!_running)
        {
            return;
        }

        if (active)
        {
            _clock.Start();
            _timer.Start();
            _input.Focus();
        }
        else
        {
            _clock.Stop();
            _timer.Stop();
        }
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
        _clock.Stop();
        ClearWords();
        _particles.Clear();
        _explosions.Clear();
        _floats.Clear();
        _targets.Clear();
        _balls.Clear();
        _aiming = null;
        _gameOver = false;
        _startButton.Text = "▶  Bắt đầu chơi";
        ShowMenu(true);
        Invalidate();
    }

    private void Step()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(0.05, now - _lastTime);
        _lastTime = now;
        if (Height < 120)
        {
            // Minimised (or squeezed to nothing): there is no sky for words to fall
            // through, so the game holds still instead of every word landing at once.
            return;
        }

        _twinkle += dt;
        var sky = Sky;

        // New words drop in from above the sky at a random spot, sized to their text.
        _spawnIn -= dt;
        var maxOnScreen = Math.Min(7, 3 + Level);
        if (_spawnIn <= 0 && _words.Count < maxOnScreen && PickWord() is { } word)
        {
            var size = MeasureWord(word);
            var x = PickSpawnX(size, sky);
            _words.Add(new FallingWord(word, x, -size.Height / 2 - BubbleRadii(size).Height - 2, size, TextColors[_random.Next(TextColors.Length)]));
            _spawnIn = Math.Max(0.8, 2.4 - Level * 0.22) * FallSeconds / DefaultFallSeconds;
        }

        // At level 1 a word takes FallSeconds to drop from the top to the roof, on any
        // window size; each level after that speeds it up a little.
        var roofTop = sky.Bottom - RoofHeight;
        var speed = roofTop / FallSeconds * (34 + Level * 10) / 44;
        for (var index = _words.Count - 1; index >= 0; index--)
        {
            // Answered words keep falling too; the ball homes in on them as they go.
            var falling = _words[index];
            falling.Y += (float)(speed * dt);
            if (falling.CentreY + BubbleRadii(falling).Height >= roofTop && falling.Targeted)
            {
                // Answered, but the ball is still on its way: the answer was right,
                // so it blows up here as a hit rather than costing a life.
                if (_aiming == falling)
                {
                    _aiming = null;
                }

                HitWord(falling);
            }
            else if (falling.CentreY + BubbleRadii(falling).Height >= roofTop)
            {
                // Landed on the roof: the bubble splashes; lose a life and the combo.
                _words.RemoveAt(index);
                falling.Image?.Dispose();
                Pop(falling.CentreX, roofTop, BubbleRadii(falling), ground: true);

                _lives--;
                _combo = 0;
                _flash = 0.35;
                Landed?.Invoke(falling.Word);
                if (_lives <= 0)
                {
                    EndGame();
                    return;
                }
            }
        }

        UpdateNinja(dt, sky);
        UpdateHops(dt);
        UpdateBalls(dt);
        foreach (var particle in _particles)
        {
            particle.X += particle.Vx * (float)dt;
            particle.Y += particle.Vy * (float)dt;
            particle.Vy += (particle.Bubble ? -20 : 260) * (float)dt;
            particle.Life -= (float)dt;
        }

        _particles.RemoveAll(particle => particle.Life <= 0);
        foreach (var blast in _explosions)
        {
            blast.Age += dt;
        }

        _explosions.RemoveAll(blast => blast.Age >= Explosion.Duration);
        foreach (var text in _floats)
        {
            text.Y -= 40 * (float)dt;
            text.Life -= (float)dt;
        }

        _floats.RemoveAll(text => text.Life <= 0);
        _flash = Math.Max(0, _flash - dt);

        // Pinyin being composed in the input method changes without any text event,
        // so it is looked at every frame; a change restarts the wait before checking.
        var composition = ReadComposition();
        if (composition != _composition)
        {
            _composition = composition;
            RestartAutoHit();
            CheckTyping();
        }

        UpdateSnow(dt, sky);

        // The moon slowly reddens as hearts run out: a hint at 2, a blood moon at 1.
        var moonTarget = _lives >= MaxLives ? 0 : _lives == 2 ? 0.45 : 1;
        _bloodMoon += Math.Clamp(moonTarget - _bloodMoon, -dt * 0.5, dt * 0.5);
        Invalidate();
    }

    // The input method's unfinished text, e.g. "ni'hao" not yet picked as 你好.
    private string ReadComposition()
    {
        if (!_input.IsHandleCreated || !_input.Focused)
        {
            return string.Empty;
        }

        var context = ImmGetContext(_input.Handle);
        if (context == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var bytes = ImmGetCompositionStringW(context, GcsCompStr, IntPtr.Zero, 0);
            if (bytes <= 0)
            {
                return string.Empty;
            }

            var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes);
            try
            {
                ImmGetCompositionStringW(context, GcsCompStr, buffer, bytes);
                return System.Runtime.InteropServices.Marshal.PtrToStringUni(buffer, bytes / 2);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ImmReleaseContext(_input.Handle, context);
        }
    }

    // Throws away what the input method is composing, so a shot word's pinyin does
    // not turn into characters afterwards.
    private void CancelComposition()
    {
        _composition = string.Empty;
        if (!_input.IsHandleCreated)
        {
            return;
        }

        var context = ImmGetContext(_input.Handle);
        if (context == IntPtr.Zero)
        {
            return;
        }

        ImmNotifyIME(context, NiCompositionStr, CpsCancel, 0);
        ImmReleaseContext(_input.Handle, context);
    }

    private const int GcsCompStr = 0x0008;
    private const int NiCompositionStr = 0x0015;
    private const int CpsCancel = 0x0004;

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmNotifyIME(IntPtr hIMC, int dwAction, int dwIndex, int dwValue);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [System.Runtime.InteropServices.DllImport("imm32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int ImmGetCompositionStringW(IntPtr hIMC, int dwIndex, IntPtr lpBuf, int dwBufLen);

    // Bigger flakes fall faster; each sways on its own phase and wraps back to the top.
    private void UpdateSnow(double dt, RectangleF sky)
    {
        foreach (var flake in _snow)
        {
            flake.Phase += (float)dt * 1.4f;
            flake.Y += (12 + flake.Size * 8) * (float)dt;
            flake.X += (float)(Math.Sin(flake.Phase) * 14 * dt) + 6 * (float)dt;
            if (flake.Y > sky.Bottom)
            {
                flake.Y = -flake.Size;
                flake.X = (float)_random.NextDouble() * sky.Width;
            }
            else if (flake.X > sky.Width + 4)
            {
                flake.X = -4;
            }
        }
    }

    // Size of a falling word for the current mode (characters, pinyin, or both).
    private SizeF MeasureWord(GameWord word)
    {
        var hanzi = TextRenderer.MeasureText(word.Chinese, _hanziFont);
        var under = TextRenderer.MeasureText(word.Pinyin, _pinyinUnderFont);
        var only = TextRenderer.MeasureText(word.Pinyin, _pinyinOnlyFont);
        return Mode switch
        {
            NinjaGameMode.Hanzi => new SizeF(hanzi.Width, hanzi.Height),
            NinjaGameMode.HanziPinyin => new SizeF(Math.Max(hanzi.Width, under.Width), hanzi.Height + under.Height),
            _ => new SizeF(only.Width, only.Height)
        };
    }

    // Words all fall at one speed, so a new bubble only collides with ones still
    // near the top: of a few random spots, take the one farthest from those.
    private float PickSpawnX(SizeF size, RectangleF sky)
    {
        var range = Math.Max(1, sky.Width - size.Width - 140);
        var nearTop = _words.Where(falling => falling.Y < 280).Select(falling => falling.CentreX).ToList();
        var best = 70 + (float)_random.NextDouble() * range;
        var bestGap = -1f;
        for (var attempt = 0; attempt < 10 && nearTop.Count > 0; attempt++)
        {
            var x = 70 + (float)_random.NextDouble() * range;
            var gap = nearTop.Min(centre => Math.Abs(centre - (x + size.Width / 2)));
            if (gap > bestGap)
            {
                best = x;
                bestGap = gap;
            }
        }

        return best;
    }

    // A word not already on screen, picked at random.
    private GameWord? PickWord()
    {
        var words = Words();
        if (words.Count == 0)
        {
            return null;
        }

        var flying = _words.Select(falling => falling.Word.Chinese).ToHashSet(StringComparer.Ordinal);
        var free = words.Where(word => !flying.Contains(word.Chinese)).ToList();
        var pool = free.Count > 0 ? free : words;
        return pool[_random.Next(pool.Count)];
    }

    // Pinyin reduced to its bare letters: no tones (marks or numbers), spaces or
    // apostrophes, lower case, ü as u.
    private static string PinyinKey(string text) =>
        string.Concat(Form1.NormalizePinyin(text).Where(char.IsLetter));

    // A word is answered by its characters, or in any mode by its pinyin typed
    // without tones.
    private static bool IsAnswer(FallingWord falling, string typed, string typedKey) =>
        string.Equals(falling.Word.Chinese, typed, StringComparison.Ordinal) ||
        (typedKey.Length > 0 && typedKey == PinyinKey(falling.Word.Pinyin));

    // A wrong key wipes the typing: as soon as what has been typed cannot lead to any
    // falling word, it is cleared (input method composition too), the combo breaks,
    // and the learner starts that word again from the beginning. With nothing
    // falling there is nothing to be wrong about, so typing is left alone.
    private void CheckTyping()
    {
        var typed = Typed;
        if (!_running || _gameOver || typed.Length == 0 || !_words.Any(falling => !falling.Targeted) || OnTrack(typed))
        {
            return;
        }

        _combo = 0;
        const string Wrong = "Sai rồi! Gõ lại từ đầu ✋";
        // One message at a time, however many slips come in a row.
        _floats.RemoveAll(text => text.Text == Wrong);
        _floats.Add(new FloatingText(Wrong, Width / 2f, Sky.Bottom - 90, Color.FromArgb(252, 165, 165)));
        _autoHit.Stop();
        _input.Clear();
        CancelComposition();
    }

    // Is the typing still on its way to some falling word? Its characters must start
    // the word, and any pinyin letters after them must start the pinyin of the
    // syllables still to come (tones, spaces and apostrophes don't count), so 你,
    // 你h, nih and nihao all lead to 你好.
    private bool OnTrack(string typed)
    {
        var characters = string.Concat(typed.Where(IsHanzi));
        var letters = PinyinKey(string.Concat(typed.Where(character => !IsHanzi(character))));
        return _words.Any(falling => !falling.Targeted && Fits(falling.Word, characters, letters));
    }

    private static bool Fits(GameWord word, string characters, string letters)
    {
        if (!word.Chinese.StartsWith(characters, StringComparison.Ordinal))
        {
            return false;
        }

        if (letters.Length == 0)
        {
            return true;
        }

        // With one syllable per character the pinyin still to come is easy to find;
        // otherwise only pinyin typed on its own can be checked, against all of it.
        var syllables = word.Pinyin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rest = syllables.Length == word.Chinese.Length
            ? string.Concat(syllables.Skip(characters.Length))
            : characters.Length == 0 ? word.Pinyin : string.Empty;
        return PinyinKey(rest).StartsWith(letters, StringComparison.Ordinal);
    }

    private static bool IsHanzi(char character) =>
        character is >= '㐀' and <= '鿿' or >= '豈' and <= '﫿';

    // Could what has been typed still grow into this (longer) word: 你 → 你好, ni → nihao.
    private static bool CouldBecome(FallingWord falling, string typed, string typedKey)
    {
        var chinese = falling.Word.Chinese;
        if (chinese.Length > typed.Length && chinese.StartsWith(typed, StringComparison.Ordinal))
        {
            return true;
        }

        var key = PinyinKey(falling.Word.Pinyin);
        return typedKey.Length > 0 && key.Length > typedKey.Length && key.StartsWith(typedKey, StringComparison.Ordinal);
    }

    private void TryHit(bool fromEnter)
    {
        if (!_running || _gameOver)
        {
            return;
        }

        var typed = Typed;
        var typedKey = PinyinKey(typed);
        if (typed.Length == 0)
        {
            return;
        }

        // When a longer falling word starts the same way (你 while 你好 is falling, or
        // ni while nihao is), give the learner a little longer to finish it before the
        // shorter one is shot, so neither needs Enter.
        if (!fromEnter && !_prefixWaited && _words.Any(falling => !falling.Targeted && CouldBecome(falling, typed, typedKey)))
        {
            _prefixWaited = true;
            _autoHit.Interval = PrefixHitDelay;
            _autoHit.Start();
            return;
        }

        var target = _words
            .Where(falling => !falling.Targeted && IsAnswer(falling, typed, typedKey))
            .OrderByDescending(falling => falling.Y)
            .FirstOrDefault();
        if (target is null)
        {
            if (fromEnter)
            {
                // Wrong guess: the combo breaks and the typing clears.
                _combo = 0;
                _floats.Add(new FloatingText("Chưa đúng 😅", Width / 2f, Sky.Bottom - 70, Color.FromArgb(252, 165, 165)));
                _input.Clear();
                CancelComposition();
            }

            return;
        }

        // Right answer: the cat runs under the word and fires.
        target.Targeted = true;
        _targets.Enqueue(target);
        _input.Clear();
        CancelComposition();
        Invalidate();
    }

    // A ball reached its bubble: pop, points, and the word counts as right.
    private void HitWord(FallingWord target)
    {
        _words.Remove(target);
        target.Image?.Dispose();
        _combo++;
        _hits++;
        var points = 10 * _combo;
        _score += points;
        var centre = new PointF(target.CentreX, target.CentreY);
        Pop(centre.X, centre.Y, BubbleRadii(target), ground: false);
        _love = 0.9;

        _floats.Add(new FloatingText(_combo > 1 ? $"+{points}  ✨x{_combo}" : $"+{points}", centre.X, centre.Y, Color.FromArgb(253, 224, 71)));
        if (_hits % HitsPerLevel == 0)
        {
            _floats.Add(new FloatingText($"⭐ Lên cấp {Level}!", Width / 2f, Sky.Height / 2, Color.FromArgb(251, 146, 60)));
        }

        Hit?.Invoke(target.Word);
    }

    // The cat runs to the next marked word and fires once under it; with
    // nothing to hit it patrols the roof.
    private void UpdateNinja(double dt, RectangleF sky)
    {
        while (_aiming is null && _targets.Count > 0)
        {
            var next = _targets.Dequeue();
            if (_words.Contains(next))
            {
                _aiming = next;
            }
        }

        float goal;
        float speed;
        if (_aiming is not null)
        {
            goal = _aiming.CentreX;
            speed = 900;
        }
        else
        {
            if (Math.Abs(_wanderTo - _ninjaX) < 4)
            {
                _wanderTo = 40 + (float)_random.NextDouble() * Math.Max(1, sky.Width - 80);
            }

            goal = _wanderTo;
            speed = 120;
        }

        var step = (float)(speed * dt);
        var distance = goal - _ninjaX;
        _ninjaRunning = Math.Abs(distance) > 2;
        if (_ninjaRunning)
        {
            _facing = Math.Sign(distance);
            _ninjaX += Math.Clamp(distance, -step, step);
            _runPhase += dt * (speed > 200 ? 20 : 9);
        }

        _throwPose = Math.Max(0, _throwPose - dt);
        // Close words: run underneath and throw straight up. Far words: turn
        // and throw diagonally from where the cat stands.
        if (_aiming is not null &&
            (Math.Abs(_aiming.CentreX - _ninjaX) < 8 || Math.Abs(_aiming.CentreX - _ninjaX) > DiagonalThrowDistance))
        {
            _facing = Math.Sign(_aiming.CentreX - _ninjaX) is var side and not 0 ? side : _facing;
            _ninjaRunning = false;
            _balls.Add(new Ball(_yarnAt.X, _yarnAt.Y, _aiming));
            _hopSpeed = Math.Max(_hopSpeed, 340);
            _aiming = null;
            _throwPose = 0.3;
        }
    }
    // Balls of yarn spin and home in on their bubble; one that lost its bubble flies off the top.
    private void UpdateBalls(double dt)
    {
        const float speed = 1000;
        for (var index = _balls.Count - 1; index >= 0; index--)
        {
            var ball = _balls[index];
            ball.Spin += (float)(dt * 720);
            float dx;
            float dy;
            if (_words.Contains(ball.Target))
            {
                dx = ball.Target.CentreX - ball.X;
                dy = ball.Target.CentreY - ball.Y;
            }
            else
            {
                dx = 0;
                dy = -1;
            }

            var length = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
            // The bubble pops the moment the ball reaches its film.
            if (_words.Contains(ball.Target) && length < HitRadius(ball.Target))
            {
                _balls.RemoveAt(index);
                HitWord(ball.Target);
                continue;
            }

            ball.Dx = dx / length;
            ball.Dy = dy / length;
            ball.X += ball.Dx * speed * (float)dt;
            ball.Y += ball.Dy * speed * (float)dt;
            if (ball.Y < -40)
            {
                _balls.RemoveAt(index);
            }
        }
    }

    private void EndGame()
    {
        _flash = 0;
        _gameOver = true;
        _running = false;
        _timer.Stop();
        _clock.Stop();
        BestScore = Math.Max(BestScore, _score);
        _startButton.Text = "🔁  Chơi lại";
        ShowMenu(true);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var sky = Sky;

        using (var skyPath = Form1.RoundedMenuRenderer.CreateRoundedPath(new RectangleF(1, 1, sky.Width - 2, sky.Height - 2), 22))
        {
            // Night sky: deep blue gradient, twinkling stars and a glowing moon.
            // Under a blood moon the whole night turns crimson.
            var blood = (float)_bloodMoon;
            graphics.DrawImageUnscaled(SkyBackdrop(sky, skyPath, blood), 0, 0);

            var clip = graphics.Clip;
            graphics.SetClip(skyPath);
            for (var index = 0; index < _stars.Count; index++)
            {
                var glow = 120 + (int)(100 * Math.Abs(Math.Sin(_twinkle * 1.5 + index)));
                using var star = new SolidBrush(Color.FromArgb(Math.Min(255, glow), 255, 255, 255));
                var size = index % 5 == 0 ? 3f : 2f;
                graphics.FillEllipse(star, _stars[index].X, _stars[index].Y, size, size);
            }

            // A crescent moon below the lives: a disc with a second disc cut out of it.
            // As hearts run out it fills into a full, red blood moon with a wider glow.
            var moon = new RectangleF(sky.Width - 160, 84, 58, 58);
            var moonColor = Blend(Color.FromArgb(254, 249, 195), Color.FromArgb(220, 38, 38), blood);
            using (var halo = new SolidBrush(Color.FromArgb(22 + (int)(28 * blood), moonColor)))
            {
                graphics.FillEllipse(halo, RectangleF.Inflate(moon, 10 + 8 * blood, 10 + 8 * blood));
            }

            using (var disc = new GraphicsPath())
            using (var bite = new GraphicsPath())
            using (var moonFill = new SolidBrush(moonColor))
            {
                disc.AddEllipse(moon);
                bite.AddEllipse(moon.X + 16 + 60 * blood, moon.Y - 8 - 30 * blood, 54, 54);
                using var crescent = new Region(disc);
                crescent.Exclude(bite);
                graphics.FillRegion(moonFill, crescent);
            }

            // Soft flakes: a faint glow around a brighter core, bigger than the stars.
            using (var glow = new SolidBrush(Color.FromArgb(45, 241, 245, 249)))
            using (var core = new SolidBrush(Color.FromArgb(225, 241, 245, 249)))
            {
                foreach (var flake in _snow)
                {
                    graphics.FillEllipse(glow, flake.X - flake.Size / 2, flake.Y - flake.Size / 2, flake.Size * 2, flake.Size * 2);
                    graphics.FillEllipse(core, flake.X, flake.Y, flake.Size, flake.Size);
                }
            }

            // Bubbles first, then the words floating inside them.
            foreach (var falling in _words)
            {
                DrawBubble(graphics, falling);
            }

            foreach (var falling in _words)
            {
                DrawWord(graphics, falling);
            }

            foreach (var ball in _balls)
            {
                DrawBall(graphics, ball);
            }

            DrawRoof(graphics, sky);
            DrawCat(graphics, _ninjaX, sky.Bottom - RoofHeight + 4);
            foreach (var pop in _explosions)
            {
                DrawPop(graphics, pop);
            }

            // Soapy droplets, and tiny bubbles drifting off.
            foreach (var particle in _particles)
            {
                var alpha = (int)(255 * Math.Clamp(particle.Life / 0.7f, 0, 1));
                if (particle.Bubble)
                {
                    using var ring = new Pen(Color.FromArgb(alpha * 3 / 4, 220, 240, 255), 1.4f);
                    graphics.DrawEllipse(ring, particle.X - particle.Size / 2, particle.Y - particle.Size / 2, particle.Size, particle.Size);
                    using var shine = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                    graphics.FillEllipse(shine, particle.X - particle.Size / 4, particle.Y - particle.Size / 4, 2, 2);
                }
                else
                {
                    using var brush = new SolidBrush(Color.FromArgb(alpha, particle.Color));
                    graphics.FillEllipse(brush, particle.X - 2.5f, particle.Y - 2.5f, 5, 5);
                }
            }

            foreach (var text in _floats)
            {
                var size = TextRenderer.MeasureText(text.Text, FloatFont);
                var alpha = (int)(255 * Math.Clamp(text.Life / 1.2f, 0, 1));
                using var brush = new SolidBrush(Color.FromArgb(alpha, text.Color));
                graphics.DrawString(text.Text, FloatFont, brush, text.X - size.Width / 2f, text.Y - size.Height / 2f);
            }

            // A red wash over everything in the sky: words, snow, roof and cat.
            if (blood > 0)
            {
                using var wash = new SolidBrush(Color.FromArgb((int)(55 * blood), 220, 38, 38));
                graphics.FillRectangle(wash, sky);
            }

            if (_flash > 0)
            {
                using var red = new SolidBrush(Color.FromArgb((int)(_flash / 0.35 * 80), 239, 68, 68));
                graphics.FillRectangle(red, sky);
            }

            graphics.ResetTransform();
            if (!_running)
            {
                using var veil = new SolidBrush(Color.FromArgb(150, 2, 6, 23));
                graphics.FillRectangle(veil, sky);
            }

            graphics.Clip = clip;
        }

        DrawHud(graphics, sky);
        if (!_running)
        {
            DrawOverlay(graphics, sky);
        }
        else
        {
            DrawTyped(graphics, sky);
        }
    }

    private static readonly Font TypedFont = new("Microsoft YaHei UI", 14F, FontStyle.Bold);

    // What is being typed, in a faint pill just above the roof, so a learner can see
    // their keys without an answer box. Hidden while nothing is typed.
    private void DrawTyped(Graphics graphics, RectangleF sky)
    {
        var typed = Typed;
        if (typed.Length == 0)
        {
            return;
        }

        var size = TextRenderer.MeasureText(graphics, typed, TypedFont, Size.Empty, TextFormatFlags.NoPadding);
        var bounds = new RectangleF((sky.Width - size.Width) / 2 - 18, sky.Bottom - RoofHeight - size.Height - 34, size.Width + 36, size.Height + 14);
        using (var pill = Form1.RoundedMenuRenderer.CreateRoundedPath(bounds, bounds.Height / 2))
        using (var fill = new SolidBrush(Color.FromArgb(110, 2, 6, 23)))
        {
            graphics.FillPath(fill, pill);
        }

        TextRenderer.DrawText(graphics, typed, TypedFont, Rectangle.Round(bounds), Color.FromArgb(226, 232, 240),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    // Plain glowing text, no frame: characters, characters over pinyin, or pinyin.
    // A word already answered turns gold while the ball is on its way.
    private void DrawWord(Graphics graphics, FallingWord falling)
    {
        // The part typed so far turns red: leading characters, or the matching pinyin.
        var typed = falling.Targeted ? string.Empty : Typed;
        var hanziTyped = TypedHanziLength(falling.Word.Chinese, typed);
        var pinyinTyped = TypedPinyinLength(falling.Word, typed, hanziTyped);

        // Outlined text is costly to draw, so each word keeps a picture of itself and
        // only redraws it when its red part or its colour changes.
        var key = (hanziTyped, pinyinTyped, falling.Targeted);
        if (falling.Image is null || falling.ImageKey != key)
        {
            falling.Image?.Dispose();
            falling.Image = RenderWord(falling, hanziTyped, pinyinTyped);
            falling.ImageKey = key;
        }

        graphics.DrawImageUnscaled(falling.Image, (int)falling.X - WordPad, (int)falling.Y - WordPad);
    }

    private const int WordPad = 4;

    private void ClearWords()
    {
        foreach (var falling in _words)
        {
            falling.Image?.Dispose();
        }

        _words.Clear();
    }

    // A transparent picture of the word: each line centred, with a dark outline all
    // round so it reads on the bubble, and its typed start redrawn in red.
    private Bitmap RenderWord(FallingWord falling, int hanziTyped, int pinyinTyped)
    {
        var color = falling.Targeted ? Color.FromArgb(250, 204, 21) : falling.Color;
        var width = (int)falling.Size.Width;
        var height = (int)falling.Size.Height;
        var image = new Bitmap(width + WordPad * 2, height + WordPad * 2, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(image);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        using var outline = new SolidBrush(Color.FromArgb(28, 14, 8));
        using var fill = new SolidBrush(color);
        using var red = new SolidBrush(TypedColor);
        void Line(string text, Font font, int top, int lineHeight, int typedLength)
        {
            var size = graphics.MeasureString(text, font, PointF.Empty, format);
            var left = WordPad + (width - size.Width) / 2;
            var y = WordPad + top + (lineHeight - size.Height) / 2;
            foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1), (0, 2), (2, 0), (-2, 0), (0, -2) })
            {
                graphics.DrawString(text, font, outline, left + dx, y + dy, format);
            }

            graphics.DrawString(text, font, fill, left, y, format);
            if (typedLength > 0)
            {
                graphics.DrawString(text[..typedLength], font, red, left, y, format);
            }
        }

        switch (Mode)
        {
            case NinjaGameMode.Hanzi:
                Line(falling.Word.Chinese, _hanziFont, 0, height, hanziTyped);
                break;
            case NinjaGameMode.HanziPinyin:
                var hanziHeight = TextRenderer.MeasureText(falling.Word.Chinese, _hanziFont).Height;
                Line(falling.Word.Chinese, _hanziFont, 0, hanziHeight, hanziTyped);
                Line(falling.Word.Pinyin, _pinyinUnderFont, hanziHeight, height - hanziHeight, pinyinTyped);
                break;
            default:
                Line(falling.Word.Pinyin, _pinyinOnlyFont, 0, height, pinyinTyped);
                break;
        }

        return image;
    }

    // The sky's gradient fill, kept as a picture and only redrawn when the size
    // changes or the blood moon has moved on a step (1/50 of the way).
    private Bitmap? _skyBackdrop;
    private (Size Size, int Blood) _skyBackdropKey;

    private Bitmap SkyBackdrop(RectangleF sky, GraphicsPath skyPath, float blood)
    {
        var key = (new Size(Width, Height), (int)MathF.Round(blood * 50));
        if (_skyBackdrop is not null && _skyBackdropKey == key)
        {
            return _skyBackdrop;
        }

        _skyBackdrop?.Dispose();
        _skyBackdrop = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        _skyBackdropKey = key;
        var step = key.Item2 / 50f;
        using var graphics = Graphics.FromImage(_skyBackdrop);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var gradient = new LinearGradientBrush(
            sky,
            Blend(Color.FromArgb(15, 23, 42), Color.FromArgb(42, 6, 10), step),
            Blend(Color.FromArgb(49, 46, 129), Color.FromArgb(127, 21, 35), step),
            LinearGradientMode.Vertical);
        graphics.FillPath(gradient, skyPath);
        return _skyBackdrop;
    }

    // How many leading characters of the word the typed text matches.
    private static int TypedHanziLength(string chinese, string typed) =>
        typed.Length > 0 && chinese.StartsWith(typed, StringComparison.Ordinal) ? typed.Length : 0;

    // How much of the pinyin to colour: the syllables of the characters typed so far,
    // or, when pinyin is typed, the letters it matches (tones and spaces ignored).
    private static int TypedPinyinLength(GameWord word, string typed, int hanziTyped)
    {
        if (hanziTyped > 0)
        {
            var syllables = word.Pinyin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (syllables.Length != word.Chinese.Length)
            {
                return hanziTyped == word.Chinese.Length ? word.Pinyin.Length : 0;
            }

            var end = 0;
            for (var index = 0; index < hanziTyped; index++)
            {
                end = word.Pinyin.IndexOf(syllables[index], end, StringComparison.Ordinal) + syllables[index].Length;
            }

            return end;
        }

        var letters = typed.Where(char.IsLetter).Select(BaseLetter).ToArray();
        if (letters.Length == 0)
        {
            return 0;
        }

        var matched = 0;
        for (var position = 0; position < word.Pinyin.Length; position++)
        {
            var letter = word.Pinyin[position];
            if (!char.IsLetter(letter))
            {
                continue;
            }

            if (BaseLetter(letter) != letters[matched])
            {
                return 0;
            }

            if (++matched == letters.Length)
            {
                return position + 1;
            }
        }

        return 0;
    }

    // A pinyin letter without its tone mark, lower case; v stands for ü.
    private static char BaseLetter(char letter)
    {
        var plain = letter.ToString().Normalize(System.Text.NormalizationForm.FormD)[0];
        plain = char.ToLowerInvariant(plain);
        return plain == 'v' ? 'u' : plain;
    }

    // A dark tiled roof along the bottom of the sky; a word that lands here is lost.
    private void DrawRoof(Graphics graphics, RectangleF sky)
    {
        var top = sky.Bottom - RoofHeight;
        using (var roof = new LinearGradientBrush(new RectangleF(0, top - 1, sky.Width, RoofHeight + 2),
                   Color.FromArgb(71, 85, 105), Color.FromArgb(30, 41, 59), LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(roof, 0, top, sky.Width, RoofHeight);
        }

        using var tile = new Pen(Color.FromArgb(100, 15, 23, 42), 2);
        graphics.DrawLine(tile, 0, top + 2, sky.Width, top + 2);
        for (var x = 0f; x < sky.Width; x += 28)
        {
            graphics.DrawArc(tile, x, top + 4, 28, 16, 0, 180);
        }
    }

    // A soap bubble round the word: slightly oval for long words, big enough for
    // the whole word to float inside it.
    private static SizeF BubbleRadii(FallingWord falling) => BubbleRadii(falling.Size);

    private static SizeF BubbleRadii(SizeF word)
    {
        var across = word.Width / 2 * 1.18f + 14;
        var down = Math.Max(word.Height / 2 * 1.18f + 14, across * 0.72f);
        return new SizeF(Math.Max(across, down * 0.92f), down);
    }

    // How close the ball must get to pop the bubble.
    private static float HitRadius(FallingWord falling)
    {
        var radii = BubbleRadii(falling);
        return Math.Min(radii.Width, radii.Height) * 1.05f;
    }

    // The bubble itself never changes, so each size is drawn once into a picture:
    // a clear film tinted blue towards its edge, a rainbow rim, a faint pink sheen
    // and white window reflections.
    private static readonly Dictionary<(int Across, int Down), Bitmap> BubbleCache = [];

    private static Bitmap BubbleImage(int across, int down)
    {
        if (BubbleCache.TryGetValue((across, down), out var cached))
        {
            return cached;
        }

        var image = new Bitmap(across * 2 + 8, down * 2 + 8, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var body = new RectangleF(4, 4, across * 2, down * 2);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(body);
            using var film = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(12, 220, 240, 255),
                SurroundColors = [Color.FromArgb(100, 165, 215, 255)],
                FocusScales = new PointF(0.7f, 0.7f)
            };
            graphics.FillPath(film, path);
        }

        using (var sheen = new LinearGradientBrush(body, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(60, 255, 150, 215), 45f))
        {
            graphics.FillEllipse(sheen, body);
        }

        using (var rainbow = new LinearGradientBrush(body, Color.White, Color.White, 45f)
               {
                   InterpolationColors = new ColorBlend
                   {
                       Colors =
                       [
                           Color.FromArgb(215, 255, 140, 200), Color.FromArgb(215, 255, 230, 120),
                           Color.FromArgb(215, 120, 230, 255), Color.FromArgb(215, 185, 140, 255)
                       ],
                       Positions = [0f, 0.35f, 0.7f, 1f]
                   }
               })
        using (var rim = new Pen(rainbow, 2.6f))
        {
            graphics.DrawEllipse(rim, body);
        }

        using (var inner = new Pen(Color.FromArgb(70, 255, 255, 255), 1.2f))
        {
            graphics.DrawEllipse(inner, RectangleF.Inflate(body, -4, -4));
        }

        // A window-shaped shine at the top left, a dot under it, and a thin glint
        // along the bottom right.
        var state = graphics.Save();
        graphics.TranslateTransform(4 + across * 0.55f, 4 + down * 0.42f);
        graphics.RotateTransform(-38);
        using (var shine = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
        {
            graphics.FillEllipse(shine, -across * 0.24f, -down * 0.09f, across * 0.48f, down * 0.18f);
        }

        graphics.Restore(state);
        using (var dot = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
        {
            var size = Math.Max(3f, across * 0.09f);
            graphics.FillEllipse(dot, 4 + across * 0.3f, 4 + down * 0.66f, size, size);
        }

        using (var glint = new Pen(Color.FromArgb(120, 255, 255, 255), 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawArc(glint, RectangleF.Inflate(body, -6, -6), 15, 50);
        }

        BubbleCache[(across, down)] = image;
        return image;
    }

    // A word's bubble, with a sparkle running round its rim. A bubble the ball is
    // on its way to glows pink.
    private void DrawBubble(Graphics graphics, FallingWord falling)
    {
        var radii = BubbleRadii(falling);
        var across = (int)radii.Width;
        var down = (int)radii.Height;
        var centreX = falling.CentreX;
        var centreY = falling.CentreY;
        graphics.DrawImageUnscaled(BubbleImage(across, down), (int)centreX - across - 4, (int)centreY - down - 4);
        var body = new RectangleF((int)centreX - across, (int)centreY - down, across * 2, down * 2);
        using (var sparkle = new Pen(Color.FromArgb(110, 255, 255, 255), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawArc(sparkle, body, (float)(_twinkle * 70 + falling.X) % 360, 24);
        }

        if (falling.Targeted)
        {
            using var locked = new Pen(Color.FromArgb(170, 255, 120, 200), 3f);
            graphics.DrawEllipse(locked, RectangleF.Inflate(body, 3, 3));
        }
    }

    private static readonly Color[] FoamColors =
    [
        Color.FromArgb(200, 238, 255), Color.FromArgb(255, 186, 222), Color.FromArgb(255, 240, 165),
        Color.FromArgb(214, 196, 255), Color.White
    ];

    // A bubble bursting: soapy droplets spray out from its rim and a few tiny
    // bubbles float off. One that reached the roof splashes upwards instead.
    private void Pop(float x, float y, SizeF radii, bool ground)
    {
        _explosions.Add(new Explosion(x, y, Math.Max(radii.Width, radii.Height), ground));
        for (var index = 0; index < (ground ? 22 : 20); index++)
        {
            var angle = ground ? Math.PI + _random.NextDouble() * Math.PI : _random.NextDouble() * Math.PI * 2;
            var power = 90 + _random.NextDouble() * 220;
            var startX = ground ? x + (float)(Math.Cos(angle) * radii.Width * 0.6) : x + (float)Math.Cos(angle) * radii.Width;
            var startY = ground ? y : y + (float)Math.Sin(angle) * radii.Height;
            _particles.Add(new Particle(startX, startY, (float)(Math.Cos(angle) * power), (float)(Math.Sin(angle) * power - 40),
                FoamColors[_random.Next(FoamColors.Length)]));
        }

        for (var index = 0; index < 6; index++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            _particles.Add(new Particle(
                x + (float)Math.Cos(angle) * radii.Width * 0.5f,
                y + (float)Math.Sin(angle) * radii.Height * 0.5f,
                (float)(Math.Cos(angle) * 50),
                -30 - (float)_random.NextDouble() * 50,
                Color.White)
            {
                Bubble = true,
                Size = 5 + (float)_random.NextDouble() * 7,
                Life = 1.3f
            });
        }
    }

    // The burst itself: the film tears into curling rainbow shreds that fly apart
    // and vanish, after a quick white flash. On the roof it leaves a flat ripple.
    private void DrawPop(Graphics graphics, Explosion pop)
    {
        var t = (float)(pop.Age / Explosion.Duration);
        var grow = 1 - (1 - t) * (1 - t);
        var fade = 1 - t;
        if (pop.Ground)
        {
            var spread = pop.Size * (0.6f + 1.2f * grow);
            using var ripple = new Pen(Color.FromArgb((int)(200 * fade), 200, 238, 255), 3 * fade + 1);
            graphics.DrawEllipse(ripple, pop.X - spread, pop.Y - spread * 0.18f, spread * 2, spread * 0.36f);
            return;
        }

        if (t < 0.2f)
        {
            using var flash = new Pen(Color.FromArgb((int)(220 * (1 - t / 0.2f)), 255, 255, 255), 3);
            var size = pop.Size * (0.9f + 0.3f * t);
            graphics.DrawEllipse(flash, pop.X - size, pop.Y - size, size * 2, size * 2);
        }

        var radius = pop.Size * (1 + 0.6f * grow);
        for (var index = 0; index < 12; index++)
        {
            var start = index * 30 + (float)Math.Sin(index * 7.3) * 12 + t * 40;
            using var shred = new Pen(Color.FromArgb((int)(230 * fade), FoamColors[index % (FoamColors.Length - 1)]), 2.6f * fade + 0.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var curl = radius * (0.92f + 0.12f * (index % 3));
            graphics.DrawArc(shred, pop.X - curl, pop.Y - curl, curl * 2, curl * 2, start, 16 * fade + 2);
        }
    }

    private const float CatSize = 84;
    private const float YarnRadius = 9;

    // The cat bounces along: little hops while it runs, a bigger jump to throw,
    // the odd hop while it waits, and a squash each time it lands.
    private void UpdateHops(double dt)
    {
        if (_hop <= 0 && _hopSpeed <= 0)
        {
            if (_ninjaRunning)
            {
                _hopSpeed = 210;
            }
            else if (_twinkle >= _nextIdleHop)
            {
                _hopSpeed = 190;
                _nextIdleHop = _twinkle + 1.8 + _random.NextDouble() * 2.5;
            }
        }

        _hopSpeed -= 1500 * dt;
        _hop += _hopSpeed * dt;
        if (_hop <= 0)
        {
            if (_hopSpeed < -80)
            {
                _squash = 1;
            }

            _hop = 0;
            _hopSpeed = 0;
        }

        _squash = Math.Max(0, _squash - dt * 5);
        _love = Math.Max(0, _love - dt);
    }

    // The white mochi cat on the roof. It hugs a ball of yarn while it runs and
    // hops about; to throw, it jumps and flings a paw up. It beams with hearts
    // after a pop and looks sad for a moment when a bubble lands.
    private void DrawCat(Graphics graphics, float x, float feetY)
    {
        var throwing = _throwPose > 0;
        var lift = (float)_hop;
        using (var shadow = new SolidBrush(Color.FromArgb(Math.Max(20, 70 - (int)lift), 0, 0, 0)))
        {
            var width = 50 - Math.Min(20, lift * 0.3f);
            graphics.FillEllipse(shadow, x - width / 2, feetY - 4, width, 8);
        }

        var squash = (float)_squash;
        var airborne = lift > 0;
        var pose = new CatArt.Pose
        {
            Time = (float)_twinkle,
            Hop = lift / CatSize,
            SquashX = 1 + 0.14f * squash - (airborne ? 0.04f : 0),
            SquashY = 1 - 0.16f * squash + (airborne ? 0.06f : 0),
            Lean = _ninjaRunning ? _facing * 8 : 0,
            Running = _ninjaRunning,
            Step = (float)Math.Sin(_runPhase),
            TailSide = -_facing,
            Hearts = _love > 0,
            Sticker = 0.03f
        };
        if (throwing)
        {
            pose.RaiseArm = _facing;
            pose.ArmAngle = 20 + 50 * (1 - (float)(_throwPose / 0.3));
        }
        else
        {
            pose.Hold = (canvas, area) => CatArt.DrawYarn(canvas, area.X + area.Width / 2, area.Y + area.Height / 2,
                YarnRadius, (float)(_twinkle * 40));
        }

        var mood = throwing || _love > 0 ? CatArt.Mood.Happy : _flash > 0 ? CatArt.Mood.Sad : CatArt.Mood.Smile;
        var belly = CatArt.DrawCat(graphics, new RectangleF(x - CatSize / 2, feetY - CatSize, CatSize, CatSize), CatArt.Coat.White, mood, pose);
        if (!throwing)
        {
            _yarnAt = belly;
        }
    }

    // A thrown ball of yarn, spinning as it flies, with its loose end of thread
    // trailing in a wavy line behind it.
    private void DrawBall(Graphics graphics, Ball ball)
    {
        var travelled = MathF.Sqrt((ball.X - ball.StartX) * (ball.X - ball.StartX) + (ball.Y - ball.StartY) * (ball.Y - ball.StartY));
        var length = Math.Min(70, travelled);
        if (length > 4)
        {
            var points = new PointF[9];
            for (var index = 0; index < points.Length; index++)
            {
                var along = length * index / (points.Length - 1);
                var wave = MathF.Sin(index * 1.3f + (float)_twinkle * 18) * 4 * index / (points.Length - 1);
                points[index] = new PointF(ball.X - ball.Dx * along - ball.Dy * wave, ball.Y - ball.Dy * along + ball.Dx * wave);
            }

            using var thread = new Pen(Color.FromArgb(220, 255, 170, 205), 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawCurve(thread, points);
        }

        CatArt.DrawYarn(graphics, ball.X, ball.Y, YarnRadius * 1.15f, ball.Spin);
    }

    private void DrawHud(Graphics graphics, RectangleF sky)
    {
        // Score and combo on the left, level in the middle, lives on the right.
        // Each pill is as wide as its text, so large scores and combos fit.
        var pill = Color.FromArgb(60, 255, 255, 255);
        var scoreWidth = DrawPill(graphics, 16, "⭐", $"{_score} điểm", pill, Color.FromArgb(253, 224, 71));
        if (_combo > 1)
        {
            DrawPill(graphics, 16 + scoreWidth + 10, "✨", $"Combo x{_combo}", pill, Color.FromArgb(253, 186, 116));
        }

        var levelText = $"Cấp {Level}";
        DrawPill(graphics, sky.Width / 2 - PillWidth(graphics, levelText) / 2, "🐾", levelText, pill, Color.White);
        const float heart = 24;
        for (var index = 0; index < MaxLives; index++)
        {
            var bounds = new RectangleF(sky.Width - 20 - (MaxLives - index) * (heart + 8), 19, heart, heart);
            DrawHeart(graphics, bounds, index < _lives ? Color.FromArgb(244, 63, 94) : Color.FromArgb(71, 85, 105));
        }
    }

    private static void DrawHeart(Graphics graphics, RectangleF bounds, Color color)
    {
        using var path = new GraphicsPath();
        var half = bounds.Width / 2;
        path.AddArc(bounds.Left, bounds.Top, half, half, 150, 210);
        path.AddArc(bounds.Left + half, bounds.Top, half, half, 180, 210);
        path.AddLine(bounds.Right - 1, bounds.Top + half * 0.75f, bounds.Left + half, bounds.Bottom - 2);
        path.CloseFigure();
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }

    private const float PillHeight = 34;

    // Icon space on the left, the text, and padding on the right.
    private static float PillWidth(Graphics graphics, string text) =>
        38 + TextRenderer.MeasureText(graphics, text, HudFont, Size.Empty, TextFormatFlags.NoPadding).Width + 16;

    // Draws a rounded HUD pill at (x, 14) sized to its text; returns its width.
    private static float DrawPill(Graphics graphics, float x, string icon, string text, Color fill, Color ink)
    {
        var bounds = new RectangleF(x, 14, PillWidth(graphics, text), PillHeight);
        using (var path = Form1.RoundedMenuRenderer.CreateRoundedPath(bounds, bounds.Height / 2))
        using (var brush = new SolidBrush(fill))
        {
            graphics.FillPath(brush, path);
        }

        TextRenderer.DrawText(graphics, icon, HudEmojiFont, new Rectangle((int)bounds.X + 10, (int)bounds.Y, 28, (int)bounds.Height), ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, text, HudFont, new Rectangle((int)bounds.X + 38, (int)bounds.Y, (int)bounds.Width - 38, (int)bounds.Height), ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return bounds.Width;
    }

    // Start screen with the mode picker, or the result after the last life.
    private void DrawOverlay(Graphics graphics, RectangleF sky)
    {
        var hasWords = Words().Count > 0;
        string title;
        string detail;
        if (_gameOver)
        {
            title = "🫧 Hết lượt rồi!";
            detail = $"Điểm: {_score}   •   Cao nhất: {BestScore}   •   Chọn chế độ rồi chơi lại";
        }
        else if (!hasWords)
        {
            title = "🐱 Mèo bắn bong bóng";
            detail = "Hãy chọn từ để học trước (Danh sách từ → Chọn từ để học).";
        }
        else
        {
            title = "🐱 Mèo bắn bong bóng";
            detail = BestScore > 0
                ? $"Gõ chữ Hán hoặc pinyin không dấu của chữ trong bong bóng để mèo ném cuộn len làm vỡ bóng. Cao nhất: {BestScore}"
                : "Gõ chữ Hán hoặc pinyin không dấu của chữ trong bong bóng để mèo ném cuộn len làm vỡ bóng trước khi bóng chạm mái nhà!";
        }

        var centreY = (int)(sky.Height / 2);
        TextRenderer.DrawText(graphics, title, BigFont, new Rectangle(0, centreY - 96, (int)sky.Width, 46), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(graphics, detail, MidFont, new Rectangle(20, centreY - 44, (int)sky.Width - 40, 52), Color.FromArgb(203, 213, 225),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        _startButton.Enabled = hasWords;
    }

    private sealed class FallingWord(GameWord word, float x, float y, SizeF size, Color color)
    {
        public GameWord Word { get; } = word;
        public float X { get; } = x;
        public float Y { get; set; } = y;
        public SizeF Size { get; } = size;
        public Color Color { get; } = color;

        // Answered: it stops falling and waits for the ball.
        public bool Targeted { get; set; }

        // The drawn word and what it shows (typed lengths, answered), made on demand.
        public Bitmap? Image { get; set; }
        public (int Hanzi, int Pinyin, bool Targeted) ImageKey { get; set; }

        public float CentreX => X + Size.Width / 2;
        public float CentreY => Y + Size.Height / 2;
    }

    private sealed class Ball(float x, float y, FallingWord target)
    {
        public float X { get; set; } = x;
        public float Y { get; set; } = y;
        public float Dx { get; set; }
        public float Dy { get; set; } = -1;
        public float StartX { get; } = x;
        public float StartY { get; } = y;
        public float Spin { get; set; }
        public FallingWord Target { get; } = target;
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    private sealed class Explosion(float x, float y, float size, bool ground)
    {
        public const double Duration = 0.45;
        public float X { get; } = x;
        public float Y { get; } = y;
        public float Size { get; } = size;
        public bool Ground { get; } = ground;
        public double Age { get; set; }
    }

    private sealed class Snowflake(float x, float y, float size, float phase)
    {
        public float X { get; set; } = x;
        public float Y { get; set; } = y;
        public float Size { get; } = size;
        public float Phase { get; set; } = phase;
    }

    private sealed class Particle(float x, float y, float vx, float vy, Color color)
    {
        public float X { get; set; } = x;
        public float Y { get; set; } = y;
        public float Vx { get; } = vx;
        public float Vy { get; set; } = vy;
        public Color Color { get; } = color;
        public float Life { get; set; } = 0.7f;

        // A tiny floating bubble rather than a droplet.
        public bool Bubble { get; init; }
        public float Size { get; init; } = 5;
    }

    private sealed class FloatingText(string text, float x, float y, Color color)
    {
        public string Text { get; } = text;
        public float X { get; } = x;
        public float Y { get; set; } = y;
        public Color Color { get; } = color;
        public float Life { get; set; } = 1.2f;
    }
}
