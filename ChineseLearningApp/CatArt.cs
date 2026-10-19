using System.Drawing.Drawing2D;

namespace ChineseLearningApp;

// Everything cat in the app, drawn with GDI+ so there are no image files: soft
// "mochi" cats (grey and white, with moods, poses and gentle animation), paw
// prints, cat icons hugging a small prop for each page, and a faint cat-and-paw
// wallpaper for page backgrounds.
internal static class CatArt
{
    public static readonly Color Ink = Color.FromArgb(92, 58, 42);
    public static readonly Color Pink = Color.FromArgb(246, 156, 168);

    // The two mochi cats every picture is drawn with: soft grey and milky white.
    public enum Coat
    {
        Grey,
        White
    }

    public enum Mood
    {
        Smile,
        Happy,
        Love,
        Sleepy,
        Surprised,
        Sad,
        Wink
    }

    public static readonly Color GreyFur = Color.FromArgb(158, 149, 148);
    public static readonly Color WhiteFur = Color.FromArgb(255, 253, 250);
    public static readonly Color FurLine = Color.FromArgb(112, 96, 92);
    public static readonly Color EyeInk = Color.FromArgb(64, 52, 50);
    public static readonly Color EarPink = Color.FromArgb(244, 168, 172);
    public static readonly Color HeartRed = Color.FromArgb(240, 92, 110);

    // The thing a cat icon holds, one per page or statistic.
    public enum Prop
    {
        None,
        Book,
        Card,
        Keyboard,
        Pencil,
        Gear,
        Check,
        Cross,
        Target,
        Box,
        Clock,
        Eye,
        Arrows,
        Letters
    }

    // Icons were emoji before; each known emoji now stands for a mochi cat
    // hugging a prop.
    private static readonly Dictionary<string, (Prop Prop, Coat Coat, Mood Mood)> IconsByEmoji = new()
    {
        ["📚"] = (Prop.Book, Coat.White, Mood.Smile),
        ["📖"] = (Prop.Book, Coat.Grey, Mood.Smile),
        ["🃏"] = (Prop.Card, Coat.Grey, Mood.Wink),
        ["⌨"] = (Prop.Keyboard, Coat.White, Mood.Happy),
        ["📝"] = (Prop.Pencil, Coat.Grey, Mood.Smile),
        ["⚙"] = (Prop.Gear, Coat.White, Mood.Sleepy),
        ["✅"] = (Prop.Check, Coat.White, Mood.Happy),
        ["❌"] = (Prop.Cross, Coat.Grey, Mood.Sad),
        ["🎯"] = (Prop.Target, Coat.White, Mood.Love),
        ["📦"] = (Prop.Box, Coat.Grey, Mood.Happy),
        ["⏱"] = (Prop.Clock, Coat.White, Mood.Surprised),
        ["👁"] = (Prop.Eye, Coat.Grey, Mood.Smile),
        ["↕"] = (Prop.Arrows, Coat.White, Mood.Wink),
        ["🔠"] = (Prop.Letters, Coat.Grey, Mood.Happy)
    };

    // One clock for every moving cat, and the controls showing them: while one
    // is on screen, the part with the cat (or all of it) is repainted 15 times a second,
    // but only while the app is the active window, so it costs nothing in the background.
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly List<(WeakReference<Control> Control, Func<Control, Rectangle>? Area)> Animated = [];
    private static System.Windows.Forms.Timer? _ticker;

    public static float Now => (float)Clock.Elapsed.TotalSeconds;

    public static void Animate(Control control, Func<Control, Rectangle>? area = null)
    {
        Animated.Add((new WeakReference<Control>(control), area));
        if (_ticker is not null)
        {
            return;
        }

        _ticker = new System.Windows.Forms.Timer { Interval = 66 };
        _ticker.Tick += (_, _) =>
        {
            Animated.RemoveAll(entry => !entry.Control.TryGetTarget(out var target) || target.IsDisposed);
            if (Form.ActiveForm is null)
            {
                return;
            }

            foreach (var (reference, area) in Animated)
            {
                if (reference.TryGetTarget(out var target) && target.Visible && target.IsHandleCreated &&
                    target.FindForm() is { Visible: true, WindowState: not FormWindowState.Minimized })
                {
                    if (area is null)
                    {
                        target.Invalidate();
                    }
                    else
                    {
                        target.Invalidate(area(target));
                    }
                }
            }
        };
        _ticker.Start();
    }

    // Draws the cat icon standing for `emoji` inside a round badge, if there is
    // one; returns false for an emoji with no cat, so the caller can draw it as text.
    // `excited` (the mouse is over it) makes the cat bounce and send up hearts.
    public static bool TryDrawIcon(Graphics graphics, RectangleF badge, string emoji, bool excited = false)
    {
        var key = emoji.Trim();
        if (!IconsByEmoji.TryGetValue(key, out var icon))
        {
            return false;
        }

        // Each icon runs a little out of step with the others.
        var offset = key.Sum(character => character) % 97 * 0.37f;
        DrawIcon(graphics, badge, icon.Prop, icon.Coat, icon.Mood, Now + offset, excited);
        return true;
    }

    // A mochi cat standing in the badge, hugging its prop. It breathes, sways,
    // blinks and now and then gives a little hop.
    public static void DrawIcon(Graphics graphics, RectangleF badge, Prop prop, Coat coat, Mood mood, float time, bool excited = false)
    {
        var breathe = MathF.Sin(time * 2.6f);
        var pose = new Pose
        {
            Time = time,
            SquashX = 1 - 0.025f * breathe,
            SquashY = 1 + 0.035f * breathe,
            Lean = 5 * MathF.Sin(time * 1.3f),
            Hearts = excited || mood == Mood.Love
        };
        Hop(pose, excited ? time * 1.7f % 1 : time / 4.2f % 1, excited ? 0.1f : 0.06f);
        if (prop != Prop.None)
        {
            pose.Hold = (canvas, area) =>
            {
                var side = Math.Min(area.Width, area.Height) * 0.98f;
                DrawProp(canvas, new RectangleF(area.X + (area.Width - side) / 2, area.Y + (area.Height - side) / 2, side, side), prop);
            };
        }

        var size = badge.Width * 0.92f;
        DrawCat(graphics, new RectangleF(badge.X + (badge.Width - size) / 2, badge.Bottom - size - badge.Height * 0.03f, size, size), coat, mood, pose);
    }

    // A quick hop at the start of each cycle (0–1): up and down in the first
    // fifth, with a squash as it lands.
    private static void Hop(Pose pose, float cycle, float height)
    {
        if (cycle < 0.2f)
        {
            pose.Hop = MathF.Sin(cycle / 0.2f * MathF.PI) * height;
            pose.SquashX *= 0.96f;
            pose.SquashY *= 1.05f;
        }
        else if (cycle < 0.3f)
        {
            var land = 1 - (cycle - 0.2f) / 0.1f;
            pose.SquashX *= 1 + 0.1f * land;
            pose.SquashY *= 1 - 0.12f * land;
        }
    }

    // How a cat is drawn this frame. Sizes are fractions of the cat's box.
    public sealed class Pose
    {
        // Seconds, for blinking, the tail and floating hearts.
        public float Time { get; set; }
        public float Hop { get; set; }
        public float SquashX { get; set; } = 1;
        public float SquashY { get; set; } = 1;
        // Tilt in degrees around the feet.
        public float Lean { get; set; }
        public bool Running { get; set; }
        // -1 to 1: which foot is forward while running.
        public float Step { get; set; }
        // 0, or the side (-1 left, 1 right) of a paw flung up, at ArmAngle degrees out from straight up.
        public int RaiseArm { get; set; }
        public float ArmAngle { get; set; } = 30;
        // Something hugged to the belly, drawn into the given area (unscaled).
        public Action<Graphics, RectangleF>? Hold { get; set; }
        public int TailSide { get; set; } = 1;
        public bool Hearts { get; set; }
        // Width of a white sticker edge round the cat (0 for none).
        public float Sticker { get; set; }
    }

    // A mochi cat: one soft bean of a body with a wide round head, small rounded
    // ears with pink insides, dot eyes, rosy cheeks, a tiny ω mouth, stubby paws
    // and a curled tail, in the square `box` (its feet on the bottom edge).
    // Returns the middle of its belly on screen, where it hugs things.
    public static PointF DrawCat(Graphics graphics, RectangleF box, Coat coat, Mood mood, Pose pose)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = box.Width;
        // Everything below is drawn in a 1 × 1 square, feet at y = 0.97.
        graphics.TranslateTransform(box.X + size / 2, box.Bottom - pose.Hop * size);
        graphics.RotateTransform(pose.Lean);
        graphics.ScaleTransform(size * pose.SquashX, size * pose.SquashY);
        graphics.TranslateTransform(-0.5f, -0.97f);
        var unit = graphics.Transform;
        var t = pose.Time;
        var fur = coat == Coat.Grey ? GreyFur : WhiteFur;

        using var outline = new Pen(FurLine, 0.028f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var furBrush = new SolidBrush(fur);
        using var body = Silhouette();
        using var tail = new GraphicsPath();
        float Tx(float x) => pose.TailSide >= 0 ? x : 1 - x;
        var swish = MathF.Sin(t * 3) * 0.03f;
        tail.AddBezier(Tx(0.76f), 0.9f, Tx(0.96f), 0.94f, Tx(1.0f), 0.76f + swish, Tx(0.91f + swish), 0.68f + swish);

        if (pose.Sticker > 0)
        {
            using var edge = new Pen(Color.White, pose.Sticker * 2 + 0.03f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var tailEdge = new Pen(Color.White, pose.Sticker * 2 + 0.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawPath(tailEdge, tail);
            graphics.DrawPath(edge, body);
        }

        // Tail behind the body.
        using (var tailLine = new Pen(FurLine, 0.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var tailFur = new Pen(fur, 0.044f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            graphics.DrawPath(tailLine, tail);
            graphics.DrawPath(tailFur, tail);
        }

        // Feet: little bumps that patter while it runs, under the body.
        if (pose.Running)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                var lift = Math.Max(0, pose.Step * side) * 0.06f;
                var foot = new RectangleF(0.5f + side * 0.15f - 0.08f + pose.Step * side * 0.04f, 0.89f - lift, 0.16f, 0.1f);
                graphics.FillEllipse(furBrush, foot);
                graphics.DrawEllipse(outline, foot);
            }
        }

        graphics.FillPath(furBrush, body);
        if (coat == Coat.Grey)
        {
            // A paler muzzle and tummy.
            using var pale = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
            graphics.FillEllipse(pale, 0.3f, 0.66f, 0.4f, 0.26f);
        }

        using (var ear = new SolidBrush(EarPink))
        {
            graphics.FillPolygon(ear, [new PointF(0.21f, 0.16f), new PointF(0.21f, 0.27f), new PointF(0.31f, 0.22f)]);
            graphics.FillPolygon(ear, [new PointF(0.79f, 0.16f), new PointF(0.79f, 0.27f), new PointF(0.69f, 0.22f)]);
        }

        graphics.DrawPath(outline, body);
        if (!pose.Running)
        {
            graphics.DrawLine(outline, 0.43f, 0.97f, 0.44f, 0.9f);
            graphics.DrawLine(outline, 0.57f, 0.97f, 0.56f, 0.9f);
        }

        DrawFace(graphics, mood, t);

        // Paws: one flung up, hugging something, or resting on the tummy.
        void Paw(float x, float y)
        {
            var paw = new RectangleF(x - 0.065f, y - 0.055f, 0.13f, 0.11f);
            graphics.FillEllipse(furBrush, paw);
            graphics.DrawEllipse(outline, paw);
        }

        if (pose.RaiseArm != 0)
        {
            var side = pose.RaiseArm;
            var shoulder = new PointF(0.5f + side * 0.22f, 0.7f);
            var angle = pose.ArmAngle * MathF.PI / 180;
            var tip = new PointF(shoulder.X + side * MathF.Sin(angle) * 0.26f, shoulder.Y - MathF.Cos(angle) * 0.26f);
            using (var armLine = new Pen(FurLine, 0.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var armFur = new Pen(fur, 0.095f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                graphics.DrawLine(armLine, shoulder, tip);
                graphics.DrawLine(armFur, shoulder, tip);
            }

            Paw(tip.X, tip.Y);
            Paw(0.5f - side * 0.12f, 0.78f);
        }
        else if (pose.Hold is { } hold)
        {
            PointF[] corners = [new(0.3f, 0.6f), new(0.7f, 0.92f)];
            unit.TransformPoints(corners);
            graphics.Restore(state);
            var held = graphics.Save();
            hold(graphics, RectangleF.FromLTRB(
                Math.Min(corners[0].X, corners[1].X), Math.Min(corners[0].Y, corners[1].Y),
                Math.Max(corners[0].X, corners[1].X), Math.Max(corners[0].Y, corners[1].Y)));
            graphics.Restore(held);
            state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Transform = unit;
            Paw(0.29f, 0.76f);
            Paw(0.71f, 0.76f);
        }
        else
        {
            Paw(0.39f, 0.8f);
            Paw(0.61f, 0.8f);
        }

        // Hearts drifting up beside the head, or z's when sleepy.
        if (pose.Hearts)
        {
            for (var index = 0; index < 2; index++)
            {
                var rise = (t * 0.55f + index * 0.5f) % 1;
                using var heart = HeartPath(0.84f + 0.06f * MathF.Sin(rise * 6 + index), 0.24f - rise * 0.28f, 0.16f * (1 - rise * 0.35f));
                using var red = new SolidBrush(Color.FromArgb((int)(255 * (1 - rise * rise)), HeartRed));
                graphics.FillPath(red, heart);
            }
        }

        if (mood == Mood.Sleepy)
        {
            var rise = t * 0.4f % 1;
            using var zed = new Pen(Color.FromArgb((int)(220 * (1 - rise)), 120, 140, 200), 0.025f) { LineJoin = LineJoin.Round };
            var x = 0.82f + rise * 0.06f;
            var y = 0.2f - rise * 0.2f;
            graphics.DrawLines(zed, [new PointF(x, y), new PointF(x + 0.08f, y), new PointF(x, y + 0.08f), new PointF(x + 0.08f, y + 0.08f)]);
        }

        PointF[] belly = [new(0.5f, 0.76f)];
        unit.TransformPoints(belly);
        graphics.Restore(state);
        return belly[0];
    }

    // Body outline: a wide round head with two small ears, narrowing into a
    // round bottom.
    private static GraphicsPath Silhouette()
    {
        var path = new GraphicsPath();
        path.AddLine(0.16f, 0.3f, 0.2f, 0.11f);
        path.AddLine(0.2f, 0.11f, 0.36f, 0.2f);
        path.AddBezier(0.36f, 0.2f, 0.45f, 0.16f, 0.55f, 0.16f, 0.64f, 0.2f);
        path.AddLine(0.64f, 0.2f, 0.8f, 0.11f);
        path.AddLine(0.8f, 0.11f, 0.84f, 0.3f);
        path.AddBezier(0.84f, 0.3f, 0.95f, 0.42f, 0.94f, 0.58f, 0.83f, 0.64f);
        path.AddBezier(0.83f, 0.64f, 0.88f, 0.74f, 0.85f, 0.91f, 0.74f, 0.96f);
        path.AddBezier(0.74f, 0.96f, 0.6f, 0.99f, 0.4f, 0.99f, 0.26f, 0.96f);
        path.AddBezier(0.26f, 0.96f, 0.15f, 0.91f, 0.12f, 0.74f, 0.17f, 0.64f);
        path.AddBezier(0.17f, 0.64f, 0.06f, 0.58f, 0.05f, 0.42f, 0.16f, 0.3f);
        path.CloseFigure();
        return path;
    }

    // Dot eyes (blinking every few seconds), rosy cheeks and a tiny mouth.
    private static void DrawFace(Graphics graphics, Mood mood, float time)
    {
        using var ink = new SolidBrush(EyeInk);
        using var pen = new Pen(EyeInk, 0.024f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var shine = new SolidBrush(Color.White);
        using (var blush = new SolidBrush(Color.FromArgb(170, 250, 150, 162)))
        {
            graphics.FillEllipse(blush, 0.19f, 0.52f, 0.13f, 0.065f);
            graphics.FillEllipse(blush, 0.68f, 0.52f, 0.13f, 0.065f);
        }

        var blink = time % 3.4f < 0.14f;
        foreach (var x in new[] { 0.37f, 0.63f })
        {
            var right = x > 0.5f;
            if (mood == Mood.Happy || (mood == Mood.Wink && right))
            {
                graphics.DrawArc(pen, x - 0.04f, 0.455f, 0.08f, 0.06f, 200, 140);
            }
            else if (mood == Mood.Sleepy || blink)
            {
                graphics.DrawArc(pen, x - 0.04f, 0.43f, 0.08f, 0.06f, 30, 120);
            }
            else
            {
                var dot = mood == Mood.Surprised ? 0.075f : 0.06f;
                graphics.FillEllipse(ink, x - dot / 2, 0.48f - dot / 2, dot, dot * 1.1f);
                graphics.FillEllipse(shine, x - dot * 0.3f, 0.48f - dot * 0.35f, dot * 0.36f, dot * 0.36f);
            }
        }

        if (mood == Mood.Sad)
        {
            using var tear = new SolidBrush(Color.FromArgb(220, 140, 200, 250));
            graphics.FillEllipse(tear, 0.33f, 0.52f, 0.04f, 0.06f);
            graphics.DrawArc(pen, 0.47f, 0.545f, 0.06f, 0.04f, 200, 140);
        }
        else if (mood == Mood.Surprised)
        {
            graphics.DrawEllipse(pen, 0.48f, 0.52f, 0.04f, 0.045f);
        }
        else
        {
            graphics.DrawArc(pen, 0.455f, 0.495f, 0.045f, 0.045f, 0, 180);
            graphics.DrawArc(pen, 0.5f, 0.495f, 0.045f, 0.045f, 0, 180);
        }
    }

    private static GraphicsPath HeartPath(float x, float y, float size)
    {
        var path = new GraphicsPath();
        path.AddBezier(x, y + size * 0.35f, x - size * 0.6f, y - size * 0.05f, x - size * 0.35f, y - size * 0.55f, x, y - size * 0.25f);
        path.AddBezier(x, y - size * 0.25f, x + size * 0.35f, y - size * 0.55f, x + size * 0.6f, y - size * 0.05f, x, y + size * 0.35f);
        path.CloseFigure();
        return path;
    }

    // A pink ball of yarn: wound threads curving across it, turned by `spin`.
    public static void DrawYarn(Graphics graphics, float x, float y, float radius, float spin)
    {
        var ball = new RectangleF(x - radius, y - radius, radius * 2, radius * 2);
        using (var wool = new SolidBrush(Color.FromArgb(250, 128, 170)))
        {
            graphics.FillEllipse(wool, ball);
        }

        var state = graphics.Save();
        graphics.SetClip(ball);
        graphics.TranslateTransform(x, y);
        graphics.RotateTransform(spin);
        using (var thread = new Pen(Color.FromArgb(255, 200, 222), radius * 0.16f))
        {
            for (var index = -1; index <= 1; index++)
            {
                graphics.DrawArc(thread, -radius * 1.6f, -radius + index * radius * 0.5f, radius * 2.4f, radius * 1.6f, 200, 140);
            }

            graphics.DrawArc(thread, -radius * 0.2f, -radius * 1.4f, radius * 1.6f, radius * 2.6f, 100, 140);
        }

        graphics.Restore(state);
        using var edge = new Pen(FurLine, radius * 0.16f);
        graphics.DrawEllipse(edge, ball);
    }

    // A paw print: a round main pad and four toe beans, centred on (x, y).
    public static void DrawPaw(Graphics graphics, float x, float y, float size, Color colour, float angleDegrees = 0)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(x, y);
        graphics.RotateTransform(angleDegrees);
        using var brush = new SolidBrush(colour);
        graphics.FillEllipse(brush, -size * 0.32f, -size * 0.05f, size * 0.64f, size * 0.5f);
        foreach (var (tx, ty) in new[] { (-0.38f, -0.25f), (-0.14f, -0.44f), (0.14f, -0.44f), (0.38f, -0.25f) })
        {
            graphics.FillEllipse(brush, size * tx - size * 0.11f, size * ty - size * 0.13f, size * 0.22f, size * 0.26f);
        }

        graphics.Restore(state);
    }

    // Small props in a white disc, so they read on any badge colour.
    private static void DrawProp(Graphics graphics, RectangleF r, Prop prop)
    {
        var s = r.Width;
        float X(float f) => r.X + f * s;
        float Y(float f) => r.Y + f * s;
        var line = Math.Max(1f, s * 0.07f);
        using var outline = new Pen(Ink, line) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using (var disc = new SolidBrush(Color.White))
        {
            graphics.FillEllipse(disc, r);
        }

        using (var ring = new Pen(Color.FromArgb(240, 214, 196), line * 0.6f))
        {
            graphics.DrawEllipse(ring, r);
        }

        Brush Fill(Color colour) => new SolidBrush(colour);
        switch (prop)
        {
            case Prop.Book:
            {
                using var cover = Fill(Color.FromArgb(234, 108, 70));
                using var page = Fill(Color.White);
                graphics.FillRectangle(cover, X(0.2f), Y(0.3f), s * 0.6f, s * 0.44f);
                graphics.FillRectangle(page, X(0.25f), Y(0.33f), s * 0.23f, s * 0.36f);
                graphics.FillRectangle(page, X(0.52f), Y(0.33f), s * 0.23f, s * 0.36f);
                graphics.DrawLine(outline, X(0.5f), Y(0.32f), X(0.5f), Y(0.72f));
                break;
            }
            case Prop.Card:
            {
                using var card = Fill(Color.White);
                using var heart = Fill(Color.FromArgb(232, 80, 90));
                var bounds = new RectangleF(X(0.3f), Y(0.2f), s * 0.4f, s * 0.58f);
                graphics.FillRectangle(card, bounds);
                graphics.DrawRectangle(outline, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                graphics.FillEllipse(heart, X(0.4f), Y(0.4f), s * 0.2f, s * 0.18f);
                break;
            }
            case Prop.Keyboard:
            {
                using var board = Fill(Color.FromArgb(92, 70, 60));
                using var key = Fill(Color.White);
                graphics.FillRectangle(board, X(0.16f), Y(0.32f), s * 0.68f, s * 0.38f);
                for (var row = 0; row < 2; row++)
                {
                    for (var col = 0; col < 4; col++)
                    {
                        graphics.FillRectangle(key, X(0.21f + col * 0.155f), Y(0.37f + row * 0.15f), s * 0.11f, s * 0.1f);
                    }
                }

                break;
            }
            case Prop.Pencil:
            {
                var state = graphics.Save();
                graphics.TranslateTransform(X(0.5f), Y(0.5f));
                graphics.RotateTransform(-45);
                using var body = Fill(Color.FromArgb(252, 200, 70));
                using var eraser = Fill(Pink);
                using var tip = Fill(Color.FromArgb(240, 214, 180));
                graphics.FillRectangle(body, -s * 0.28f, -s * 0.09f, s * 0.42f, s * 0.18f);
                graphics.FillRectangle(eraser, -s * 0.36f, -s * 0.09f, s * 0.09f, s * 0.18f);
                graphics.FillPolygon(tip, new PointF[] { new(s * 0.14f, -s * 0.09f), new(s * 0.32f, 0), new(s * 0.14f, s * 0.09f) });
                graphics.Restore(state);
                break;
            }
            case Prop.Gear:
            {
                using var metal = Fill(Color.FromArgb(160, 150, 150));
                var centre = new PointF(X(0.5f), Y(0.5f));
                var teeth = new List<PointF>();
                for (var index = 0; index < 16; index++)
                {
                    var angle = index * Math.PI / 8;
                    var radius = s * (index % 2 == 0 ? 0.32f : 0.24f);
                    teeth.Add(new PointF(centre.X + (float)Math.Cos(angle) * radius, centre.Y + (float)Math.Sin(angle) * radius));
                }

                graphics.FillPolygon(metal, teeth.ToArray());
                using var hole = Fill(Color.White);
                graphics.FillEllipse(hole, centre.X - s * 0.09f, centre.Y - s * 0.09f, s * 0.18f, s * 0.18f);
                break;
            }
            case Prop.Check:
            case Prop.Cross:
            {
                using var dot = Fill(prop == Prop.Check ? Color.FromArgb(34, 170, 90) : Color.FromArgb(232, 80, 90));
                graphics.FillEllipse(dot, X(0.17f), Y(0.17f), s * 0.66f, s * 0.66f);
                using var mark = new Pen(Color.White, line * 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                if (prop == Prop.Check)
                {
                    graphics.DrawLines(mark, new PointF[] { new(X(0.34f), Y(0.5f)), new(X(0.46f), Y(0.62f)), new(X(0.67f), Y(0.39f)) });
                }
                else
                {
                    graphics.DrawLine(mark, X(0.37f), Y(0.37f), X(0.63f), Y(0.63f));
                    graphics.DrawLine(mark, X(0.63f), Y(0.37f), X(0.37f), Y(0.63f));
                }

                break;
            }
            case Prop.Target:
            {
                using var red = Fill(Color.FromArgb(232, 80, 90));
                using var white = Fill(Color.White);
                graphics.FillEllipse(red, X(0.16f), Y(0.16f), s * 0.68f, s * 0.68f);
                graphics.FillEllipse(white, X(0.28f), Y(0.28f), s * 0.44f, s * 0.44f);
                graphics.FillEllipse(red, X(0.39f), Y(0.39f), s * 0.22f, s * 0.22f);
                break;
            }
            case Prop.Box:
            {
                using var box = Fill(Color.FromArgb(214, 160, 108));
                using var tape = Fill(Color.FromArgb(240, 214, 180));
                graphics.FillRectangle(box, X(0.2f), Y(0.3f), s * 0.6f, s * 0.44f);
                graphics.FillRectangle(tape, X(0.44f), Y(0.3f), s * 0.12f, s * 0.44f);
                graphics.DrawRectangle(outline, X(0.2f), Y(0.3f), s * 0.6f, s * 0.44f);
                break;
            }
            case Prop.Clock:
            {
                graphics.DrawEllipse(outline, X(0.2f), Y(0.2f), s * 0.6f, s * 0.6f);
                graphics.DrawLine(outline, X(0.5f), Y(0.5f), X(0.5f), Y(0.3f));
                graphics.DrawLine(outline, X(0.5f), Y(0.5f), X(0.64f), Y(0.56f));
                break;
            }
            case Prop.Eye:
            {
                using var path = new GraphicsPath();
                path.AddBezier(X(0.15f), Y(0.5f), X(0.35f), Y(0.25f), X(0.65f), Y(0.25f), X(0.85f), Y(0.5f));
                path.AddBezier(X(0.85f), Y(0.5f), X(0.65f), Y(0.75f), X(0.35f), Y(0.75f), X(0.15f), Y(0.5f));
                graphics.DrawPath(outline, path);
                using var iris = Fill(Color.FromArgb(90, 160, 220));
                graphics.FillEllipse(iris, X(0.39f), Y(0.39f), s * 0.22f, s * 0.22f);
                break;
            }
            case Prop.Arrows:
            {
                using var arrow = new Pen(Color.FromArgb(234, 108, 70), line * 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                graphics.DrawLines(arrow, new PointF[] { new(X(0.28f), Y(0.4f)), new(X(0.4f), Y(0.24f)), new(X(0.52f), Y(0.4f)) });
                graphics.DrawLine(arrow, X(0.4f), Y(0.26f), X(0.4f), Y(0.74f));
                graphics.DrawLines(arrow, new PointF[] { new(X(0.48f), Y(0.6f)), new(X(0.6f), Y(0.76f)), new(X(0.72f), Y(0.6f)) });
                graphics.DrawLine(arrow, X(0.6f), Y(0.26f), X(0.6f), Y(0.74f));
                break;
            }
            case Prop.Letters:
            {
                using var font = new Font("Segoe UI", Math.Max(5f, s * 0.36f), FontStyle.Bold, GraphicsUnit.Pixel);
                using var colour = Fill(Color.FromArgb(234, 108, 70));
                using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                graphics.DrawString("Aa", font, colour, r, centred);
                break;
            }
        }
    }

    // A faint wallpaper tile of outlined cat faces and paw prints on `background`.
    private static readonly Dictionary<int, Bitmap> Tiles = [];

    public static Bitmap PatternTile(Color background)
    {
        if (Tiles.TryGetValue(background.ToArgb(), out var cached))
        {
            return cached;
        }

        const int size = 240;
        var tile = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(tile))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(background);
            var faint = Color.FromArgb(26, 234, 108, 70);
            DrawCatOutline(graphics, new RectangleF(24, 26, 46, 46), faint);
            DrawCatOutline(graphics, new RectangleF(148, 132, 52, 52), faint);
            DrawPaw(graphics, 162, 50, 20, faint, 20);
            DrawPaw(graphics, 190, 78, 16, faint, 32);
            DrawPaw(graphics, 66, 170, 18, faint, -25);
            DrawPaw(graphics, 96, 200, 14, faint, -10);
            DrawPaw(graphics, 112, 108, 12, Color.FromArgb(18, 234, 108, 70), 60);
        }

        Tiles[background.ToArgb()] = tile;
        return tile;
    }

    // Just the line drawing of a cat face, for the wallpaper.
    private static void DrawCatOutline(Graphics graphics, RectangleF r, Color colour)
    {
        var s = r.Width;
        float X(float f) => r.X + f * s;
        float Y(float f) => r.Y + f * s;
        using var pen = new Pen(colour, Math.Max(1.5f, s * 0.06f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var path = new GraphicsPath();
        path.AddLine(X(0.1f), Y(0.45f), X(0.17f), Y(0.05f));
        path.AddLine(X(0.17f), Y(0.05f), X(0.42f), Y(0.22f));
        path.AddArc(X(0.04f), Y(0.17f), s * 0.92f, s * 0.78f, 245, 50);
        path.AddLine(X(0.58f), Y(0.22f), X(0.83f), Y(0.05f));
        path.AddLine(X(0.83f), Y(0.05f), X(0.9f), Y(0.45f));
        path.AddArc(X(0.04f), Y(0.17f), s * 0.92f, s * 0.78f, -5, 190);
        graphics.DrawPath(pen, path);
        using var brush = new SolidBrush(colour);
        graphics.FillEllipse(brush, X(0.29f), Y(0.5f), s * 0.09f, s * 0.12f);
        graphics.FillEllipse(brush, X(0.62f), Y(0.5f), s * 0.09f, s * 0.12f);
        graphics.FillEllipse(brush, X(0.46f), Y(0.64f), s * 0.08f, s * 0.05f);
    }

    // Lets the cat wallpaper of a PatternPage show through: every container,
    // label or custom control on it painted in the page colour turns see-through,
    // so the pattern runs on unbroken behind the white cards.
    public static void Wallpaper(Control page, Color pageColour) => Follow(page, pageColour);

    // Children added later (rebuilt lists, new cards) get the same treatment.
    private static void Follow(Control container, Color pageColour)
    {
        container.ControlAdded += (_, e) =>
        {
            if (e.Control is { } child)
            {
                SeeThrough(child, pageColour);
            }
        };
        foreach (Control child in container.Controls)
        {
            SeeThrough(child, pageColour);
        }
    }

    // Fills a custom-drawn control's background before it paints itself.
    // `colour` is what it would have cleared to: a colour of its own is kept, but
    // the page colour (or see-through) shows whatever lies behind instead: the
    // cat wallpaper on a page, or the fill of the card it sits in.
    public static void PaintBehind(Control control, Graphics graphics, Color colour)
    {
        var page = Form1.PageColor.ToArgb();
        if (colour.A == 255 && colour.ToArgb() != page)
        {
            graphics.Clear(colour);
            return;
        }

        for (var ancestor = control.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is PatternPage patterned)
            {
                var offset = patterned.PointToClient(control.PointToScreen(Point.Empty));
                // Only the part being repainted (an animated icon is a small corner).
                var area = Rectangle.Intersect(control.ClientRectangle, Rectangle.Ceiling(graphics.ClipBounds));
                patterned.CopyPattern(graphics, area, new Point(offset.X + area.X, offset.Y + area.Y));
                return;
            }

            if (ancestor is Form1.RoundedPanel card)
            {
                graphics.Clear(colour.A == 255 ? colour : card.FillColor);
                return;
            }

            if (ancestor.BackColor.A == 255 && ancestor.BackColor.ToArgb() != page)
            {
                graphics.Clear(colour.A == 255 ? colour : ancestor.BackColor);
                return;
            }
        }

        graphics.Clear(Form1.PageColor);
    }

    private static void SeeThrough(Control control, Color pageColour)
    {
        // Inside a card, or anything with a colour of its own, the page colour is
        // meant as a fill, so leave what is in there alone. Cards and the app's own
        // drawn controls already paint the pattern behind themselves.
        if (control is Form1.RoundedPanel || control.BackColor.ToArgb() != pageColour.ToArgb() ||
            control is TextBoxBase or DataGridView or ListBox or ComboBox)
        {
            return;
        }

        if (control is Panel or TableLayoutPanel or FlowLayoutPanel or UserControl)
        {
            // Containers stay solid and copy the pattern over their own background.
            // (A see-through container would make Windows repaint everything
            // behind it the slow way, which made switching pages lag.)
            // Drawn off screen first, the copy is a fast memory-to-memory one.
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(control, true);
            control.Paint += (_, e) => PaintBehind(control, e.Graphics, Color.Transparent);
        }
        else
        {
            try
            {
                // Small things such as labels and check boxes simply see through.
                control.BackColor = Color.Transparent;
            }
            catch (ArgumentException)
            {
                // The control cannot be see-through; it keeps the plain page colour.
            }

            return;
        }

        // Scrolling copies the old pixels, which would drag the wallpaper along
        // with the content; repaint the whole view instead.
        if (control is ScrollableControl { AutoScroll: true } scroller)
        {
            scroller.Scroll += (_, _) => scroller.Invalidate(true);
            scroller.MouseWheel += (_, _) => scroller.Invalidate(true);
        }

        Follow(control, pageColour);
    }

    // The pair on Home: the white cat waves, the grey one leans in beside it with
    // hearts floating up, both breathing and bobbing out of step.
    public static void DrawGreetingCats(Graphics graphics, RectangleF r, float time)
    {
        var size = r.Height;
        var grey = new Pose
        {
            Time = time + 0.7f,
            SquashX = 1 - 0.02f * MathF.Sin(time * 2.4f + 1),
            SquashY = 1 + 0.03f * MathF.Sin(time * 2.4f + 1),
            Lean = -6 + 3 * MathF.Sin(time * 1.2f),
            Hearts = true,
            TailSide = 1
        };
        Hop(grey, (time + 2.1f) / 3.8f % 1, 0.05f);
        DrawCat(graphics, new RectangleF(r.Right - size, r.Y, size, size), Coat.Grey, Mood.Smile, grey);

        var white = new Pose
        {
            Time = time,
            SquashX = 1 - 0.02f * MathF.Sin(time * 2.4f),
            SquashY = 1 + 0.03f * MathF.Sin(time * 2.4f),
            Lean = 4 + 2 * MathF.Sin(time * 1.2f),
            RaiseArm = -1,
            ArmAngle = 25 + 25 * MathF.Sin(time * 7),
            TailSide = -1
        };
        Hop(white, time / 3.8f % 1, 0.05f);
        DrawCat(graphics, new RectangleF(r.X, r.Y, size, size), Coat.White, Mood.Happy, white);
    }
}

// The mochi cats on Home, next to the greeting.
internal sealed class WavingCat : Control
{
    public WavingCat()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = Form1.PageColor;
        CatArt.Animate(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        CatArt.PaintBehind(this, e.Graphics, BackColor);
        CatArt.DrawGreetingCats(e.Graphics, new RectangleF(2, 4, Width - 4, Height - 6), CatArt.Now);
    }
}

// A page with the faint cat wallpaper behind it. The pattern is drawn once into
// a picture the size of the page, and painting the background (for the page or
// any see-through control on it) is a single copy of the part that shows, which
// keeps switching pages quick.
internal sealed class PatternPage : TabPage
{
    private Bitmap? _canvas;

    public PatternPage(string title) : base(title)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public Bitmap Canvas
    {
        get
        {
            var size = new Size(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            if (_canvas is null || _canvas.Size != size)
            {
                _canvas?.Dispose();
                _canvas = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using var graphics = Graphics.FromImage(_canvas);
                using var pattern = new TextureBrush(CatArt.PatternTile(BackColor), WrapMode.Tile);
                graphics.FillRectangle(pattern, 0, 0, size.Width, size.Height);
            }

            return _canvas;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        CopyPattern(e.Graphics, e.ClipRectangle, e.ClipRectangle.Location);

    // Copies the pattern starting at `source` (page coordinates) into `area`, as a
    // straight pixel copy: no blending or smoothing to slow it down.
    public void CopyPattern(Graphics graphics, Rectangle area, Point source)
    {
        var state = graphics.Save();
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(Canvas, area, new Rectangle(source, area.Size), GraphicsUnit.Pixel);
        graphics.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _canvas?.Dispose();
        }

        base.Dispose(disposing);
    }
}
