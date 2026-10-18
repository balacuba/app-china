using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace ChineseLearningApp;

// One word in the ninja game: its Chinese and pinyin, and the caller's own
// object (handed back when the word is hit or lands).
internal sealed record GameWord(string Chinese, string Pinyin, object Tag);

// What falls from the sky, chosen on the start screen.
internal enum NinjaGameMode
{
    Hanzi,
    HanziPinyin,
    Pinyin
}

// "Ninja phi tiêu": words fall through a night sky. Typing a falling word marks
// it; the ninja runs under it and throws a spinning shuriken to cut it down
// (+10 × combo). A word that lands on the roof costs one of three lives. Every
// 8 hits is a new level: faster and more words. Answers are always the Chinese
// characters; when only characters are shown, their pinyin (no tones) counts too.
internal sealed class NinjaGame : Panel
{
    private const int InputHeight = 92;
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

    private static readonly Font HanziFont = new("Microsoft YaHei UI", 22F, FontStyle.Bold);
    private static readonly Font PinyinUnderFont = new("Segoe UI", 11F);
    private static readonly Font PinyinOnlyFont = new("Segoe UI Semibold", 17F);
    private static readonly Font HudFont = new("Segoe UI Semibold", 12F);
    private static readonly Font HudEmojiFont = new("Segoe UI Emoji", 13F);
    private static readonly Font BigFont = new("Segoe UI Semibold", 24F);
    private static readonly Font MidFont = new("Segoe UI", 12F);
    private static readonly Font FloatFont = new("Segoe UI Semibold", 13F);

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = new();
    private readonly Random _random = new();
    private readonly List<FallingWord> _words = [];
    private readonly List<Particle> _particles = [];
    private readonly List<FloatingText> _floats = [];
    private readonly List<PointF> _stars = [];
    private readonly TextBox _input = new();
    private readonly Form1.RoundedPanel _inputFrame = new();
    private readonly Button _startButton;
    private readonly Button[] _modeButtons;
    private double _lastTime;
    private double _spawnIn;
    private double _flash;
    private double _twinkle;
    private int _score;
    private int _combo;
    private int _lives = MaxLives;
    private int _hits;
    private bool _running;
    private bool _gameOver;

    // The ninja on the roof and the shurikens in flight.
    private readonly Queue<FallingWord> _targets = new();
    private readonly List<Shuriken> _shurikens = [];
    private FallingWord? _aiming;
    private float _ninjaX = 200;
    private float _wanderTo = 200;
    private int _facing = 1;
    private bool _ninjaRunning;
    private double _runPhase;
    private double _throwPose;

    // Words to play with (read each time a word drops in, so the list can change).
    public Func<IReadOnlyList<GameWord>> Words { get; set; } = () => [];

    // Called when a word is cut down (typed right) or lands (missed).
    public Action<GameWord>? Hit { get; set; }
    public Action<GameWord>? Landed { get; set; }

    public NinjaGameMode Mode { get; private set; } = NinjaGameMode.Hanzi;

    public int BestScore { get; set; }

    // How fast words fall (and how often new ones come), set in Settings; 1 = normal.
    public float SpeedFactor { get; set; } = 1f;

    public NinjaGame()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = Form1.SurfaceColor;

        // Rounded answer box under the sky.
        _inputFrame.Size = new Size(420, 50);
        _inputFrame.Radius = 22;
        _inputFrame.FillColor = Form1.SurfaceColor;
        _inputFrame.OutlineColor = Form1.Accent;
        _inputFrame.OutlineWidth = 2F;
        _inputFrame.BackColor = Form1.SurfaceColor;
        _input.BorderStyle = BorderStyle.None;
        _input.Font = new Font("Microsoft YaHei UI", 16F);
        _input.BackColor = Form1.SurfaceColor;
        _input.TextAlign = HorizontalAlignment.Center;
        _input.ImeMode = ImeMode.On;
        _input.Location = new Point(24, 12);
        _input.Width = _inputFrame.Width - 48;
        _input.TextChanged += (_, _) => TryHit(fromEnter: false);
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                TryHit(fromEnter: true);
                e.SuppressKeyPress = true;
            }
        };
        _inputFrame.Controls.Add(_input);
        Controls.Add(_inputFrame);

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

    private RectangleF Sky => new(0, 0, Width, Math.Max(1, Height - InputHeight));

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

        _input.PlaceholderText = mode == NinjaGameMode.Hanzi
            ? "Gõ chữ Hán hoặc pinyin rồi nhấn Enter"
            : "Gõ chữ Hán rồi nhấn Enter";
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        _inputFrame.Location = new Point((Width - _inputFrame.Width) / 2, Height - InputHeight + 18);
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
    }

    public void Start()
    {
        _words.Clear();
        _particles.Clear();
        _floats.Clear();
        _targets.Clear();
        _shurikens.Clear();
        _aiming = null;
        _score = 0;
        _combo = 0;
        _lives = MaxLives;
        _hits = 0;
        _gameOver = false;
        _spawnIn = 0.3;
        _ninjaX = _wanderTo = Width / 2f;
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
        _words.Clear();
        _particles.Clear();
        _floats.Clear();
        _targets.Clear();
        _shurikens.Clear();
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
        _twinkle += dt;
        var sky = Sky;

        // New words drop in from above the sky at a random spot, sized to their text.
        _spawnIn -= dt;
        var maxOnScreen = Math.Min(7, 3 + Level);
        if (_spawnIn <= 0 && _words.Count < maxOnScreen && PickWord() is { } word)
        {
            var size = MeasureWord(word);
            var x = 20 + (float)_random.NextDouble() * Math.Max(1, sky.Width - size.Width - 40);
            _words.Add(new FallingWord(word, x, -size.Height - 4, size, TextColors[_random.Next(TextColors.Length)]));
            _spawnIn = Math.Max(0.8, 2.4 - Level * 0.22) / SpeedFactor;
        }

        var speed = (34 + Level * 10) * SpeedFactor;
        var roofTop = sky.Bottom - RoofHeight;
        for (var index = _words.Count - 1; index >= 0; index--)
        {
            var falling = _words[index];
            if (falling.Targeted)
            {
                // Already answered: it hangs where it is until the shuriken arrives.
                continue;
            }

            falling.Y += (float)(speed * dt);
            if (falling.Y + falling.Size.Height >= roofTop)
            {
                // Landed on the roof: lose a life and the combo.
                _words.RemoveAt(index);
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
        UpdateShurikens(dt);
        foreach (var particle in _particles)
        {
            particle.X += particle.Vx * (float)dt;
            particle.Y += particle.Vy * (float)dt;
            particle.Vy += 260 * (float)dt;
            particle.Life -= (float)dt;
        }

        _particles.RemoveAll(particle => particle.Life <= 0);
        foreach (var text in _floats)
        {
            text.Y -= 40 * (float)dt;
            text.Life -= (float)dt;
        }

        _floats.RemoveAll(text => text.Life <= 0);
        _flash = Math.Max(0, _flash - dt);
        Invalidate();
    }

    // Size of a falling word for the current mode (characters, pinyin, or both).
    private SizeF MeasureWord(GameWord word)
    {
        var hanzi = TextRenderer.MeasureText(word.Chinese, HanziFont);
        var under = TextRenderer.MeasureText(word.Pinyin, PinyinUnderFont);
        var only = TextRenderer.MeasureText(word.Pinyin, PinyinOnlyFont);
        return Mode switch
        {
            NinjaGameMode.Hanzi => new SizeF(hanzi.Width, hanzi.Height),
            NinjaGameMode.HanziPinyin => new SizeF(Math.Max(hanzi.Width, under.Width), hanzi.Height + under.Height),
            _ => new SizeF(only.Width, only.Height)
        };
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

    // The characters always count; with only characters shown, their pinyin does too.
    private bool IsAnswer(FallingWord falling, string typed) =>
        string.Equals(falling.Word.Chinese, typed, StringComparison.Ordinal) ||
        (Mode == NinjaGameMode.Hanzi &&
         Form1.NormalizePinyin(typed) is { Length: > 0 } pinyin &&
         pinyin == Form1.NormalizePinyin(falling.Word.Pinyin));

    private void TryHit(bool fromEnter)
    {
        if (!_running || _gameOver)
        {
            return;
        }

        var typed = _input.Text.Trim();
        if (typed.Length == 0)
        {
            return;
        }

        // Characters hit as soon as they match; pinyin waits for Enter, because a
        // short syllable can be the start of a longer word.
        var target = _words
            .Where(falling => !falling.Targeted &&
                (string.Equals(falling.Word.Chinese, typed, StringComparison.Ordinal) || (fromEnter && IsAnswer(falling, typed))))
            .OrderByDescending(falling => falling.Y)
            .FirstOrDefault();
        if (target is null)
        {
            if (fromEnter)
            {
                // Wrong guess: the combo breaks and the box clears.
                _combo = 0;
                _floats.Add(new FloatingText("Chưa đúng 😅", Width / 2f, Sky.Bottom - 50, Color.FromArgb(252, 165, 165)));
                _input.Clear();
            }

            return;
        }

        // Right answer: the ninja runs under the word and throws.
        target.Targeted = true;
        _targets.Enqueue(target);
        _input.Clear();
        Invalidate();
    }

    // A shuriken reached its word: burst, points, and the word counts as right.
    private void HitWord(FallingWord target)
    {
        _words.Remove(target);
        _combo++;
        _hits++;
        var points = 10 * _combo;
        _score += points;
        var centre = new PointF(target.CentreX, target.CentreY);
        for (var index = 0; index < 22; index++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var power = 80 + _random.NextDouble() * 180;
            _particles.Add(new Particle(centre.X, centre.Y, (float)(Math.Cos(angle) * power), (float)(Math.Sin(angle) * power - 60), target.Color));
        }

        _floats.Add(new FloatingText(_combo > 1 ? $"+{points}  🔥x{_combo}" : $"+{points}", centre.X, centre.Y, Color.FromArgb(253, 224, 71)));
        if (_hits % HitsPerLevel == 0)
        {
            _floats.Add(new FloatingText($"⭐ Lên cấp {Level}!", Width / 2f, Sky.Height / 2, Color.FromArgb(251, 146, 60)));
        }

        Hit?.Invoke(target.Word);
    }

    // The ninja runs to the next marked word and throws once under it; with
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
        // and throw diagonally from where the ninja stands.
        if (_aiming is not null &&
            (Math.Abs(_aiming.CentreX - _ninjaX) < 8 || Math.Abs(_aiming.CentreX - _ninjaX) > DiagonalThrowDistance))
        {
            _facing = Math.Sign(_aiming.CentreX - _ninjaX) is var side and not 0 ? side : _facing;
            _ninjaRunning = false;
            _shurikens.Add(new Shuriken(_ninjaX, sky.Bottom - RoofHeight - 40, _aiming));
            _aiming = null;
            _throwPose = 0.25;
        }
    }

    // Shurikens spin and home in on their word; one that lost its word flies off the top.
    private void UpdateShurikens(double dt)
    {
        const float speed = 1000;
        for (var index = _shurikens.Count - 1; index >= 0; index--)
        {
            var star = _shurikens[index];
            star.Spin += (float)(dt * 900);
            float dx;
            float dy;
            if (_words.Contains(star.Target))
            {
                dx = star.Target.CentreX - star.X;
                dy = star.Target.CentreY - star.Y;
            }
            else
            {
                dx = 0;
                dy = -1;
            }

            var length = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
            if (_words.Contains(star.Target) && length < 24)
            {
                _shurikens.RemoveAt(index);
                HitWord(star.Target);
                continue;
            }

            star.Dx = dx / length;
            star.Dy = dy / length;
            star.X += star.Dx * speed * (float)dt;
            star.Y += star.Dy * speed * (float)dt;
            if (star.Y < -40)
            {
                _shurikens.RemoveAt(index);
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
            using (var gradient = new LinearGradientBrush(sky, Color.FromArgb(15, 23, 42), Color.FromArgb(49, 46, 129), LinearGradientMode.Vertical))
            {
                graphics.FillPath(gradient, skyPath);
            }

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
            var moon = new RectangleF(sky.Width - 160, 84, 58, 58);
            using (var halo = new SolidBrush(Color.FromArgb(22, 254, 249, 195)))
            {
                graphics.FillEllipse(halo, RectangleF.Inflate(moon, 10, 10));
            }

            using (var disc = new GraphicsPath())
            using (var bite = new GraphicsPath())
            using (var moonFill = new SolidBrush(Color.FromArgb(254, 249, 195)))
            {
                disc.AddEllipse(moon);
                bite.AddEllipse(moon.X + 16, moon.Y - 8, 54, 54);
                using var crescent = new Region(disc);
                crescent.Exclude(bite);
                graphics.FillRegion(moonFill, crescent);
            }

            foreach (var falling in _words)
            {
                DrawWord(graphics, falling);
            }

            foreach (var star in _shurikens)
            {
                DrawShuriken(graphics, star.X, star.Y, star.Spin, trailDx: star.Dx, trailDy: star.Dy);
            }

            DrawRoof(graphics, sky);
            DrawNinja(graphics, _ninjaX, sky.Bottom - RoofHeight + 4);

            foreach (var particle in _particles)
            {
                using var brush = new SolidBrush(Color.FromArgb((int)(255 * Math.Clamp(particle.Life / 0.7f, 0, 1)), particle.Color));
                graphics.FillEllipse(brush, particle.X - 3, particle.Y - 3, 6, 6);
            }

            foreach (var text in _floats)
            {
                var size = TextRenderer.MeasureText(text.Text, FloatFont);
                var alpha = (int)(255 * Math.Clamp(text.Life / 1.2f, 0, 1));
                using var brush = new SolidBrush(Color.FromArgb(alpha, text.Color));
                graphics.DrawString(text.Text, FloatFont, brush, text.X - size.Width / 2f, text.Y - size.Height / 2f);
            }

            if (_flash > 0)
            {
                using var red = new SolidBrush(Color.FromArgb((int)(_flash / 0.35 * 80), 239, 68, 68));
                graphics.FillRectangle(red, sky);
            }

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
    }

    // Plain glowing text, no frame: characters, characters over pinyin, or pinyin.
    // A word already answered turns gold while the shuriken is on its way.
    private void DrawWord(Graphics graphics, FallingWord falling)
    {
        var color = falling.Targeted ? Color.FromArgb(250, 204, 21) : falling.Color;
        var x = (int)falling.X;
        var y = (int)falling.Y;
        var width = (int)falling.Size.Width;
        void Glow(string text, Font font, Rectangle box)
        {
            var shadow = box;
            shadow.Offset(0, 2);
            TextRenderer.DrawText(graphics, text, font, shadow, Color.FromArgb(2, 6, 23), TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, text, font, box, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }

        switch (Mode)
        {
            case NinjaGameMode.Hanzi:
                Glow(falling.Word.Chinese, HanziFont, new Rectangle(x, y, width, (int)falling.Size.Height));
                break;
            case NinjaGameMode.HanziPinyin:
                var hanziHeight = TextRenderer.MeasureText(falling.Word.Chinese, HanziFont).Height;
                Glow(falling.Word.Chinese, HanziFont, new Rectangle(x, y, width, hanziHeight));
                Glow(falling.Word.Pinyin, PinyinUnderFont, new Rectangle(x, y + hanziHeight, width, (int)falling.Size.Height - hanziHeight));
                break;
            default:
                Glow(falling.Word.Pinyin, PinyinOnlyFont, new Rectangle(x, y, width, (int)falling.Size.Height));
                break;
        }
    }

    // A dark tiled roof along the bottom of the sky; a word that lands here is lost.
    private static void DrawRoof(Graphics graphics, RectangleF sky)
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

    // A small ninja in black with a red headband; legs swing while running and
    // the throwing arm snaps forward just after a throw.
    private void DrawNinja(Graphics graphics, float x, float feetY)
    {
        var swing = _ninjaRunning ? (float)Math.Sin(_runPhase) * 8 : 0;
        var bob = _ninjaRunning ? Math.Abs((float)Math.Sin(_runPhase)) * 2 : 0;
        var hipY = feetY - 16 - bob;
        var suit = Color.FromArgb(17, 24, 39);
        var red = Color.FromArgb(220, 38, 38);

        using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
        {
            graphics.FillEllipse(shadow, x - 12, feetY - 3, 24, 6);
        }

        using (var leg = new Pen(suit, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawLine(leg, x - 3, hipY, x - 7 + swing, feetY);
            graphics.DrawLine(leg, x + 3, hipY, x + 7 - swing, feetY);
        }

        using (var body = new SolidBrush(suit))
        using (var bodyPath = Form1.RoundedMenuRenderer.CreateRoundedPath(new RectangleF(x - 7, hipY - 19, 14, 21), 6))
        {
            graphics.FillPath(body, bodyPath);
        }

        using (var belt = new SolidBrush(red))
        {
            graphics.FillRectangle(belt, x - 7, hipY - 4, 14, 3);
        }

        // Arms: one forward (throwing), one back.
        var throwReach = _throwPose > 0 ? 14 : 9;
        using (var arm = new Pen(suit, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawLine(arm, x + _facing * 4, hipY - 15, x + _facing * throwReach, hipY - (_throwPose > 0 ? 30 : 10));
            graphics.DrawLine(arm, x - _facing * 4, hipY - 15, x - _facing * 9, hipY - 8);
        }

        // Head with an eye slit and a red headband whose tails flutter.
        var head = new RectangleF(x - 8, hipY - 35, 16, 16);
        using (var hood = new SolidBrush(suit))
        {
            graphics.FillEllipse(hood, head);
        }

        using (var slit = new SolidBrush(Color.FromArgb(253, 224, 186)))
        {
            graphics.FillRectangle(slit, x - 6 + _facing * 1, head.Top + 6, 12, 4);
        }

        using (var eye = new SolidBrush(suit))
        {
            graphics.FillEllipse(eye, x - 3 + _facing * 2, head.Top + 6.5f, 2.5f, 2.5f);
            graphics.FillEllipse(eye, x + 2 + _facing * 2, head.Top + 6.5f, 2.5f, 2.5f);
        }

        using (var band = new SolidBrush(red))
        {
            graphics.FillRectangle(band, x - 8, head.Top + 3, 16, 3);
        }

        var flutter = (float)Math.Sin(_runPhase * 1.3) * 3;
        using (var tails = new Pen(red, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawLine(tails, x - _facing * 8, head.Top + 4, x - _facing * 18, head.Top + 2 + flutter);
            graphics.DrawLine(tails, x - _facing * 8, head.Top + 5, x - _facing * 16, head.Top + 9 - flutter);
        }

        // A shuriken ready in hand when not mid-throw.
        if (_throwPose <= 0)
        {
            DrawShuriken(graphics, x + _facing * 10, hipY - 12, (float)(_runPhase * 40), 0, 0, small: true);
        }
    }

    // A four-pointed throwing star, spun by angle, with a faint streak when flying.
    private static void DrawShuriken(Graphics graphics, float x, float y, float angle, float trailDx, float trailDy, bool small = false)
    {
        var scale = small ? 0.55f : 1f;
        if (trailDx != 0 || trailDy != 0)
        {
            var start = new PointF(x - trailDx * 60, y - trailDy * 60);
            using var fade = new LinearGradientBrush(start, new PointF(x, y), Color.FromArgb(0, 148, 163, 184), Color.FromArgb(160, 226, 232, 240));
            using var streak = new Pen(fade, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(streak, start, new PointF(x, y));
        }

        var state = graphics.Save();
        graphics.TranslateTransform(x, y);
        graphics.RotateTransform(angle);
        graphics.ScaleTransform(scale, scale);
        var points = new PointF[8];
        for (var index = 0; index < 8; index++)
        {
            var radius = index % 2 == 0 ? 13f : 4.5f;
            var a = index * Math.PI / 4;
            points[index] = new PointF((float)(Math.Cos(a) * radius), (float)(Math.Sin(a) * radius));
        }

        using (var steel = new LinearGradientBrush(new RectangleF(-13, -13, 26, 26), Color.FromArgb(241, 245, 249), Color.FromArgb(100, 116, 139), 45f))
        {
            graphics.FillPolygon(steel, points);
        }

        using (var hole = new SolidBrush(Color.FromArgb(30, 41, 59)))
        {
            graphics.FillEllipse(hole, -2.5f, -2.5f, 5, 5);
        }

        graphics.Restore(state);
    }

    private void DrawHud(Graphics graphics, RectangleF sky)
    {
        // Score and combo on the left, level in the middle, lives on the right.
        var pill = Color.FromArgb(60, 255, 255, 255);
        DrawPill(graphics, new RectangleF(16, 14, 150, 34), "⭐", $"{_score} điểm", pill, Color.FromArgb(253, 224, 71));
        if (_combo > 1)
        {
            DrawPill(graphics, new RectangleF(176, 14, 120, 34), "🔥", $"Combo x{_combo}", pill, Color.FromArgb(253, 186, 116));
        }

        DrawPill(graphics, new RectangleF(sky.Width / 2 - 55, 14, 110, 34), "⚔", $"Cấp {Level}", pill, Color.White);
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

    private static void DrawPill(Graphics graphics, RectangleF bounds, string icon, string text, Color fill, Color ink)
    {
        using (var path = Form1.RoundedMenuRenderer.CreateRoundedPath(bounds, bounds.Height / 2))
        using (var brush = new SolidBrush(fill))
        {
            graphics.FillPath(brush, path);
        }

        TextRenderer.DrawText(graphics, icon, HudEmojiFont, new Rectangle((int)bounds.X + 10, (int)bounds.Y, 28, (int)bounds.Height), ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, text, HudFont, new Rectangle((int)bounds.X + 38, (int)bounds.Y, (int)bounds.Width - 42, (int)bounds.Height), ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    // Start screen with the mode picker, or the result after the last life.
    private void DrawOverlay(Graphics graphics, RectangleF sky)
    {
        var hasWords = Words().Count > 0;
        string title;
        string detail;
        if (_gameOver)
        {
            title = "💥 Hết lượt rồi!";
            detail = $"Điểm: {_score}   •   Cao nhất: {BestScore}   •   Chọn chế độ rồi chơi lại";
        }
        else if (!hasWords)
        {
            title = "⚔ Ninja phi tiêu";
            detail = "Hãy chọn từ để học trước (Danh sách từ → Chọn từ để học).";
        }
        else
        {
            title = "⚔ Ninja phi tiêu";
            detail = BestScore > 0
                ? $"Chọn chữ sẽ rơi, gõ chữ Hán để ninja phóng phi tiêu. Cao nhất: {BestScore}"
                : "Chọn chữ sẽ rơi, gõ chữ Hán để ninja phóng phi tiêu trước khi chữ chạm mái nhà!";
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

        // Answered: it stops falling and waits for the shuriken.
        public bool Targeted { get; set; }

        public float CentreX => X + Size.Width / 2;
        public float CentreY => Y + Size.Height / 2;
    }

    private sealed class Shuriken(float x, float y, FallingWord target)
    {
        public float X { get; set; } = x;
        public float Y { get; set; } = y;
        public float Dx { get; set; }
        public float Dy { get; set; } = -1;
        public float Spin { get; set; }
        public FallingWord Target { get; } = target;
    }

    private sealed class Particle(float x, float y, float vx, float vy, Color color)
    {
        public float X { get; set; } = x;
        public float Y { get; set; } = y;
        public float Vx { get; } = vx;
        public float Vy { get; set; } = vy;
        public Color Color { get; } = color;
        public float Life { get; set; } = 0.7f;
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
