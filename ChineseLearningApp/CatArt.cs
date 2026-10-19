using System.Drawing.Drawing2D;

namespace ChineseLearningApp;

// Everything cat in the app, drawn with GDI+ so there are no image files: cat
// heads (with a few moods), paw prints, cat icons holding a small prop for each
// page, a faint cat-and-paw wallpaper for page backgrounds, and a sitting cat that
// waves.
internal static class CatArt
{
    public static readonly Color Ginger = Color.FromArgb(247, 170, 108);
    public static readonly Color GingerStripe = Color.FromArgb(222, 124, 68);
    public static readonly Color Cream = Color.FromArgb(255, 233, 205);
    public static readonly Color Grey = Color.FromArgb(190, 184, 184);
    public static readonly Color GreyStripe = Color.FromArgb(150, 142, 142);
    public static readonly Color Ink = Color.FromArgb(92, 58, 42);
    public static readonly Color Pink = Color.FromArgb(246, 156, 168);

    public enum Mood
    {
        Happy,
        Smile,
        Sad,
        Surprised
    }

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

    // Icons were emoji before; each known emoji now stands for a cat with a prop.
    private static readonly Dictionary<string, (Prop Prop, Color Fur, Mood Mood)> IconsByEmoji = new()
    {
        ["📚"] = (Prop.Book, Ginger, Mood.Smile),
        ["📖"] = (Prop.Book, Cream, Mood.Smile),
        ["🃏"] = (Prop.Card, Grey, Mood.Happy),
        ["⌨"] = (Prop.Keyboard, Ginger, Mood.Happy),
        ["📝"] = (Prop.Pencil, Cream, Mood.Smile),
        ["⚙"] = (Prop.Gear, Grey, Mood.Smile),
        ["✅"] = (Prop.Check, Ginger, Mood.Happy),
        ["❌"] = (Prop.Cross, Grey, Mood.Sad),
        ["🎯"] = (Prop.Target, Ginger, Mood.Surprised),
        ["📦"] = (Prop.Box, Ginger, Mood.Happy),
        ["⏱"] = (Prop.Clock, Cream, Mood.Surprised),
        ["👁"] = (Prop.Eye, Grey, Mood.Smile),
        ["↕"] = (Prop.Arrows, Ginger, Mood.Smile),
        ["🔠"] = (Prop.Letters, Cream, Mood.Smile)
    };

    // Draws the cat icon standing for `emoji` inside a round badge, if there is
    // one; returns false for an emoji with no cat, so the caller can draw it as text.
    public static bool TryDrawIcon(Graphics graphics, RectangleF badge, string emoji)
    {
        if (!IconsByEmoji.TryGetValue(emoji.Trim(), out var icon))
        {
            return false;
        }

        DrawIcon(graphics, badge, icon.Prop, icon.Fur, icon.Mood);
        return true;
    }

    // A chibi cat filling the badge: a big round head, and under it two little
    // front paws hugging the prop.
    public static void DrawIcon(Graphics graphics, RectangleF badge, Prop prop, Color fur, Mood mood)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = badge.Width * (prop == Prop.None ? 0.84f : 0.78f);
        var head = new RectangleF(
            badge.X + (badge.Width - size) / 2,
            badge.Y + (badge.Height - size) / 2 - (prop == Prop.None ? 0 : badge.Height * 0.1f),
            size, size);
        DrawCatHead(graphics, head, fur, mood);
        if (prop != Prop.None)
        {
            var propSize = badge.Width * 0.4f;
            var propBox = new RectangleF(badge.X + (badge.Width - propSize) / 2, badge.Bottom - propSize * 1.02f, propSize, propSize);
            DrawProp(graphics, propBox, prop);

            // Two round paws holding the prop from either side.
            var paw = propSize * 0.36f;
            using var pawFur = new SolidBrush(fur == Grey ? Color.FromArgb(222, 216, 214) : Cream);
            using var outline = new Pen(Ink, Math.Max(1f, badge.Width * 0.022f));
            using var bean = new SolidBrush(Pink);
            foreach (var side in new[] { -1f, 1f })
            {
                var pawBox = new RectangleF(
                    propBox.X + propBox.Width / 2 + side * propBox.Width * 0.46f - paw / 2,
                    propBox.Y + propBox.Height * 0.38f,
                    paw, paw * 0.86f);
                graphics.FillEllipse(pawFur, pawBox);
                graphics.DrawEllipse(outline, pawBox);
                graphics.FillEllipse(bean, pawBox.X + paw * 0.3f, pawBox.Y + paw * 0.32f, paw * 0.4f, paw * 0.3f);
            }
        }

        graphics.Restore(state);
    }

    // A chibi cat head in the square `r`: a wide, soft face like a mochi, small
    // rounded ears with pink insides, huge sparkly eyes set low, rosy cheeks, a
    // tiny nose and ω mouth, short whiskers, and tabby marks on ginger and grey fur.
    public static void DrawCatHead(Graphics graphics, RectangleF r, Color fur, Mood mood)
    {
        var s = r.Width;
        float X(float f) => r.X + f * s;
        float Y(float f) => r.Y + f * s;
        var line = Math.Max(1f, s * 0.04f);
        using var outline = new Pen(Ink, line) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var furBrush = new SolidBrush(fur);
        using var pink = new SolidBrush(Pink);

        // Ears: soft rounded triangles leaning outwards.
        foreach (var side in new[] { -1f, 1f })
        {
            PointF P(float fx, float fy) => new(side < 0 ? X(fx) : X(1 - fx), Y(fy));
            using var ear = new GraphicsPath();
            ear.AddBezier(P(0.08f, 0.44f), P(0.04f, 0.2f), P(0.08f, 0.06f), P(0.16f, 0.07f));
            ear.AddBezier(P(0.16f, 0.07f), P(0.24f, 0.08f), P(0.36f, 0.17f), P(0.44f, 0.26f));
            ear.CloseFigure();
            graphics.FillPath(furBrush, ear);
            graphics.DrawPath(outline, ear);
            using var inner = new GraphicsPath();
            inner.AddBezier(P(0.14f, 0.36f), P(0.12f, 0.22f), P(0.14f, 0.15f), P(0.18f, 0.15f));
            inner.AddBezier(P(0.18f, 0.15f), P(0.23f, 0.16f), P(0.3f, 0.21f), P(0.35f, 0.27f));
            inner.CloseFigure();
            graphics.FillPath(pink, inner);
        }

        // Face: wider than tall.
        var face = new RectangleF(X(0.02f), Y(0.2f), s * 0.96f, s * 0.76f);
        graphics.FillEllipse(furBrush, face);
        if (fur == Ginger || fur == Grey)
        {
            using var stripe = new Pen(fur == Ginger ? GingerStripe : GreyStripe, line * 1.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(stripe, X(0.5f), Y(0.25f), X(0.5f), Y(0.33f));
            graphics.DrawLine(stripe, X(0.41f), Y(0.27f), X(0.43f), Y(0.33f));
            graphics.DrawLine(stripe, X(0.59f), Y(0.27f), X(0.57f), Y(0.33f));
        }

        graphics.DrawEllipse(outline, face);

        // Rosy cheeks, each with a little shine.
        using (var blush = new SolidBrush(Color.FromArgb(150, 250, 140, 160)))
        using (var blushShine = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
        {
            foreach (var cx in new[] { 0.19f, 0.81f })
            {
                graphics.FillEllipse(blush, X(cx - 0.09f), Y(0.69f), s * 0.18f, s * 0.1f);
                graphics.FillEllipse(blushShine, X(cx - 0.04f), Y(0.705f), s * 0.035f, s * 0.025f);
            }
        }

        // Eyes: big and low on the face, with two sparkles each.
        using var ink = new SolidBrush(Ink);
        using var white = new SolidBrush(Color.White);
        foreach (var cx in new[] { 0.31f, 0.69f })
        {
            if (mood == Mood.Happy)
            {
                // Closed, smiling ^ ^ eyes.
                graphics.DrawArc(outline, X(cx - 0.09f), Y(0.5f), s * 0.18f, s * 0.15f, 200, 140);
                continue;
            }

            var wide = mood == Mood.Surprised ? 0.19f : 0.17f;
            var tall = mood == Mood.Surprised ? 0.22f : 0.2f;
            var eye = new RectangleF(X(cx - wide / 2), Y(0.6f - tall / 2), s * wide, s * tall);
            graphics.FillEllipse(ink, eye);
            using (var iris = new LinearGradientBrush(RectangleF.Inflate(eye, 0, 1), Color.FromArgb(0, 120, 70, 50), Color.FromArgb(190, 160, 96, 64), LinearGradientMode.Vertical))
            {
                graphics.FillEllipse(iris, eye);
            }

            graphics.FillEllipse(white, eye.X + eye.Width * 0.14f, eye.Y + eye.Height * 0.12f, eye.Width * 0.46f, eye.Height * 0.4f);
            graphics.FillEllipse(white, eye.X + eye.Width * 0.6f, eye.Y + eye.Height * 0.62f, eye.Width * 0.2f, eye.Height * 0.17f);
            if (mood == Mood.Sad)
            {
                // Worried brows and a tear.
                graphics.DrawLine(outline, X(cx - 0.08f), Y(cx < 0.5f ? 0.47f : 0.43f), X(cx + 0.08f), Y(cx < 0.5f ? 0.43f : 0.47f));
                using var tear = new SolidBrush(Color.FromArgb(220, 140, 200, 250));
                graphics.FillEllipse(tear, X(cx + (cx < 0.5f ? -0.11f : 0.06f)), Y(0.68f), s * 0.05f, s * 0.07f);
            }
        }

        // Tiny nose and mouth.
        graphics.FillEllipse(pink, X(0.47f), Y(0.68f), s * 0.06f, s * 0.04f);
        if (mood == Mood.Surprised)
        {
            graphics.DrawEllipse(outline, X(0.465f), Y(0.74f), s * 0.07f, s * 0.07f);
        }
        else if (mood == Mood.Sad)
        {
            graphics.DrawArc(outline, X(0.44f), Y(0.76f), s * 0.12f, s * 0.08f, 200, 140);
        }
        else
        {
            graphics.DrawArc(outline, X(0.44f), Y(0.69f), s * 0.06f, s * 0.07f, 10, 160);
            graphics.DrawArc(outline, X(0.5f), Y(0.69f), s * 0.06f, s * 0.07f, 10, 160);
        }

        // Short whiskers at the edges of the cheeks.
        using var whisker = new Pen(Color.FromArgb(160, Ink), Math.Max(1f, line * 0.55f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        foreach (var side in new[] { -1f, 1f })
        {
            for (var row = 0; row < 2; row++)
            {
                var y = Y(0.64f + row * 0.06f);
                var startX = side < 0 ? X(0.1f) : X(0.9f);
                var endX = side < 0 ? X(-0.04f) : X(1.04f);
                graphics.DrawLine(whisker, startX, y, endX, y + (row == 0 ? -s * 0.025f : s * 0.025f));
            }
        }
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

    // Puts the cat wallpaper behind a page: the page (or form) gets the tiled
    // pattern as its background image, and every container, label or custom
    // control on it painted in the page colour turns see-through, so the pattern
    // runs on unbroken behind the white cards.
    public static void Wallpaper(Control page, Color pageColour)
    {
        page.BackgroundImage = PatternTile(pageColour);
        page.BackgroundImageLayout = ImageLayout.Tile;
        Follow(page, pageColour);
    }

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
            if (ancestor.BackgroundImage is { } tile && ancestor.BackgroundImageLayout == ImageLayout.Tile)
            {
                var offset = ancestor.PointToClient(control.PointToScreen(Point.Empty));
                using var brush = new TextureBrush(tile, WrapMode.Tile);
                brush.TranslateTransform(-offset.X, -offset.Y);
                graphics.FillRectangle(brush, control.ClientRectangle);
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
        if (control.BackColor.ToArgb() == pageColour.ToArgb() && control is not TextBoxBase and not DataGridView and not ListBox and not ComboBox)
        {
            try
            {
                control.BackColor = Color.Transparent;
            }
            catch (ArgumentException)
            {
                // The control cannot be see-through; it keeps the plain page colour.
            }
        }

        // Inside a card, or anything with a colour of its own, the page colour is
        // meant as a fill, so leave what is in there alone.
        if (control is Form1.RoundedPanel || control.BackColor.A == 255)
        {
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

    // A ginger cat sitting up, tail curled round, waving its raised right paw
    // (`wave` tilts it, in degrees).
    public static void DrawWavingCat(Graphics graphics, RectangleF r, float wave)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var s = r.Height;
        float X(float f) => r.X + f * s;
        float Y(float f) => r.Y + f * s;
        var line = Math.Max(1.2f, s * 0.022f);
        using var outline = new Pen(Ink, line) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fur = new SolidBrush(Ginger);
        using var belly = new SolidBrush(Cream);

        // Tail.
        using (var tail = new Pen(Ginger, s * 0.07f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var tailEdge = new Pen(Ink, s * 0.07f + line * 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            using var curve = new GraphicsPath();
            curve.AddBezier(X(0.62f), Y(0.9f), X(0.95f), Y(0.95f), X(0.95f), Y(0.6f), X(0.82f), Y(0.52f));
            graphics.DrawPath(tailEdge, curve);
            graphics.DrawPath(tail, curve);
        }

        // Body and belly.
        var body = new RectangleF(X(0.12f), Y(0.45f), s * 0.6f, s * 0.52f);
        graphics.FillEllipse(fur, body);
        graphics.DrawEllipse(outline, body);
        graphics.FillEllipse(belly, X(0.27f), Y(0.58f), s * 0.3f, s * 0.34f);

        // Front paws at the bottom.
        foreach (var px in new[] { 0.24f, 0.46f })
        {
            var paw = new RectangleF(X(px), Y(0.88f), s * 0.14f, s * 0.1f);
            graphics.FillEllipse(belly, paw);
            graphics.DrawEllipse(outline, paw);
        }

        // Waving arm, pivoting at the shoulder.
        var shoulder = new PointF(X(0.6f), Y(0.62f));
        graphics.TranslateTransform(shoulder.X, shoulder.Y);
        graphics.RotateTransform(28 + wave);
        var arm = new RectangleF(-s * 0.06f, -s * 0.3f, s * 0.12f, s * 0.32f);
        graphics.FillEllipse(fur, arm);
        graphics.DrawEllipse(outline, arm);
        DrawPaw(graphics, 0, -s * 0.26f, s * 0.09f, Pink);
        graphics.RotateTransform(-28 - wave);
        graphics.TranslateTransform(-shoulder.X, -shoulder.Y);

        // Head on top.
        DrawCatHead(graphics, new RectangleF(X(0.02f), Y(0.0f), s * 0.6f, s * 0.6f), Ginger, Mood.Happy);
        graphics.Restore(state);
    }
}

// The ginger cat on Home, sitting next to the greeting and waving its paw.
internal sealed class WavingCat : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    private float _phase;

    public WavingCat()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = Form1.PageColor;
        _timer.Tick += (_, _) =>
        {
            _phase += 0.16f;
            Invalidate();
        };
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _timer.Enabled = Visible;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        CatArt.PaintBehind(this, e.Graphics, BackColor);
        var size = Math.Min(Width, Height) - 4;
        CatArt.DrawWavingCat(e.Graphics, new RectangleF((Width - size) / 2f, Height - size - 2, size, size), (float)Math.Sin(_phase) * 22);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
