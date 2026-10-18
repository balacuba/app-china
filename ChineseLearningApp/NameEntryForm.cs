namespace ChineseLearningApp;

// Welcome window asking for the user's name: a blue header with the app logo,
// a rounded name box, rounded buttons and the author's details at the bottom.
internal sealed class NameEntryForm : Form
{
    private readonly TextBox _nameTextBox = new();
    private readonly Label _validationLabel = new();
    private readonly Form1.RoundedPanel _nameFrame = new();

    public string EnteredName { get; private set; } = string.Empty;

    public NameEntryForm()
    {
        Text = "Ứng dụng học tiếng trung của Tuấn Anh!";
        StartPosition = FormStartPosition.CenterScreen;
        // Borderless with rounded corners: drag it by the blue header, close
        // it with ✕, Esc or "Thoát". The title still shows in the taskbar.
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(460, 120 + 232 + AuthorFooter.CollapsedHeight);
        Font = new Font(Form1.UiFontName, 10F);
        BackColor = Form1.SurfaceColor;

        // Blue header: logo and welcome.
        var header = new Panel { Dock = DockStyle.Top, Height = 120, BackColor = Form1.Accent };
        var brand = new Form1.HeaderBrand
        {
            Text = "Học tiếng Trung",
            Font = new Font("Segoe UI Semibold", 16F)
        };
        brand.Location = new Point((ClientSize.Width - brand.Width) / 2, 22);
        var welcome = new Label
        {
            AutoSize = false,
            Location = new Point(0, 74),
            Size = new Size(ClientSize.Width, 24),
            Text = "Chào mừng bạn! Cùng học tiếng Trung mỗi ngày nhé 🌱",
            Font = new Font(Form1.UiFontName, 10F),
            ForeColor = Color.FromArgb(219, 234, 254),
            BackColor = Form1.Accent,
            TextAlign = ContentAlignment.MiddleCenter
        };
        var close = new Label
        {
            AutoSize = false,
            Size = new Size(34, 34),
            Location = new Point(ClientSize.Width - 44, 10),
            Text = "✕",
            Font = new Font(Form1.UiFontName, 12F),
            ForeColor = Color.FromArgb(219, 234, 254),
            BackColor = Form1.Accent,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        close.MouseEnter += (_, _) => close.ForeColor = Color.White;
        close.MouseLeave += (_, _) => close.ForeColor = Color.FromArgb(219, 234, 254);
        close.Click += (_, _) => Close();
        header.Controls.AddRange([brand, welcome, close]);
        // Playful icons scattered on the blue around the logo.
        var decorationFont = new Font("Segoe UI Emoji", 15F);
        var decorations = new (string Icon, Point At)[]
        {
            ("🏮", new Point(26, 16)),
            ("🐼", new Point(70, 44)),
            ("🥟", new Point(376, 44)),
            ("🎋", new Point(362, 12))
        };
        header.Paint += (_, e) =>
        {
            foreach (var (icon, at) in decorations)
            {
                TextRenderer.DrawText(e.Graphics, icon, decorationFont, at, Color.FromArgb(191, 219, 254));
            }
        };
        header.Disposed += (_, _) => decorationFont.Dispose();
        foreach (Control dragArea in new Control[] { header, brand, welcome })
        {
            dragArea.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    // Let Windows move the window as if its title bar were dragged.
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
                }
            };
        }

        // The name section fills the white area between header and footer; its
        // four corners are rounded inward, showing the header blue at the top and
        // the footer tint at the bottom.
        var body = new RoundedBody
        {
            Location = new Point(0, 120),
            Size = new Size(ClientSize.Width, 232)
        };

        var prompt = new Label
        {
            AutoSize = true,
            Location = new Point(36, 30),
            Text = "Tên bạn là gì?",
            Font = new Font("Segoe UI Semibold", 11.5F),
            ForeColor = Form1.TextColor,
            BackColor = Form1.SurfaceColor
        };
        var wave = new Label
        {
            AutoSize = true,
            Location = new Point(prompt.Left + prompt.PreferredWidth + 4, 28),
            Text = "👋",
            Font = new Font("Segoe UI Emoji", 12F),
            ForeColor = Color.FromArgb(234, 179, 8),
            BackColor = Form1.SurfaceColor
        };

        // Rounded name box; the outline turns blue while typing.
        _nameFrame.Location = new Point(36, 62);
        _nameFrame.Size = new Size(body.Width - 72, 44);
        // Pill-shaped: the radius is half the inner height.
        _nameFrame.Radius = 20;
        _nameFrame.FillColor = Form1.PageColor;
        _nameFrame.OutlineColor = Form1.BorderColor;
        _nameFrame.BackColor = Form1.SurfaceColor;
        _nameTextBox.BorderStyle = BorderStyle.None;
        _nameTextBox.BackColor = Form1.PageColor;
        _nameTextBox.Font = new Font(Form1.UiFontName, 12F);
        _nameTextBox.Location = new Point(20, 11);
        _nameTextBox.Width = _nameFrame.Width - 40;
        _nameTextBox.MaxLength = 80;
        _nameTextBox.PlaceholderText = "Nhập tên của bạn ✍️";
        _nameFrame.Controls.Add(_nameTextBox);
        _nameTextBox.GotFocus += (_, _) => SetNameOutline(Form1.Accent, 2F);
        _nameTextBox.LostFocus += (_, _) => SetNameOutline(Form1.BorderColor, 1.5F);
        _nameTextBox.TextChanged += (_, _) => _validationLabel.Text = string.Empty;

        _validationLabel.AutoSize = true;
        _validationLabel.ForeColor = Form1.DangerText;
        _validationLabel.Location = new Point(38, 110);
        _validationLabel.BackColor = Form1.SurfaceColor;

        var continueButton = Form1.CreateButton("🚀  Bắt đầu học", ContinueButton_Click);
        continueButton.AutoSize = false;
        continueButton.Size = new Size(170, 42);
        var cancelButton = Form1.CreateButton("👋  Thoát", (_, _) => Close(), secondary: true);
        cancelButton.AutoSize = false;
        cancelButton.Size = new Size(100, 42);
        // Pill-shaped like the name box.
        ((Form1.RoundedButton)continueButton).Radius = 20;
        ((Form1.RoundedButton)cancelButton).Radius = 20;
        // Both buttons as one group, centred under the name box.
        var buttonsLeft = _nameFrame.Left + (_nameFrame.Width - (cancelButton.Width + 12 + continueButton.Width)) / 2;
        cancelButton.Location = new Point(buttonsLeft, 140);
        continueButton.Location = new Point(cancelButton.Right + 12, 140);
        cancelButton.DialogResult = DialogResult.Cancel;

        // Author's details along the bottom.
        var footer = new AuthorFooter { Dock = DockStyle.Bottom };

        body.Controls.AddRange([prompt, wave, _nameFrame, _validationLabel, cancelButton, continueButton]);
        Controls.AddRange([header, body, footer]);
        AcceptButton = continueButton;
        CancelButton = cancelButton;
        // Start with the name from last time, selected so it can be retyped.
        _nameTextBox.Text = AppState.Load().UserName;
        Shown += (_, _) =>
        {
            _nameTextBox.Focus();
            _nameTextBox.SelectAll();
        };
    }

    private static readonly Color FooterTint = Color.FromArgb(232, 238, 247);

    // White middle area with all four corners rounded: the top corners sit on
    // the header blue and the bottom ones on the footer tint.
    private sealed class RoundedBody : Panel
    {
        public RoundedBody()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = Form1.SurfaceColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            using (var top = new SolidBrush(Form1.Accent))
            {
                graphics.FillRectangle(top, 0, 0, Width, Height / 2);
            }

            using (var bottom = new SolidBrush(FooterTint))
            {
                graphics.FillRectangle(bottom, 0, Height / 2, Width, Height - Height / 2);
            }

            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = Form1.RoundedMenuRenderer.CreateRoundedPath(new RectangleF(0, 0, Width, Height), CornerRadius);
            using var white = new SolidBrush(Form1.SurfaceColor);
            graphics.FillPath(white, path);
        }
    }

    // "Made with ♥ by Tuấn Anh" and a small round ✉ button on a soft tinted
    // band. The mail address stays hidden until the button is clicked; the
    // footer (and window) then grows to show it in a rounded chip.
    private sealed class AuthorFooter : Control
    {
        public const int CollapsedHeight = 56;
        public const int ExpandedHeight = 96;
        private const string Mail = "camlinh.tk@gmail.com";
        private static readonly Font PlainFont = new(Form1.UiFontName, 10F);
        private static readonly Font NameFont = new("Segoe UI Semibold", 10.5F);
        private static readonly Font HeartFont = new("Segoe UI Emoji", 10F);
        private static readonly Font ChipFont = new(Form1.UiFontName, 9.5F);
        private static readonly Font IconFont = new("Segoe UI Emoji", 9.5F);
        private static readonly Color MailFill = Color.FromArgb(219, 234, 254);
        private static readonly Color MailInk = Color.FromArgb(29, 78, 216);
        private readonly ToolTip _tip = new();
        private Rectangle _mailButton;
        private bool _showMail;
        private bool _hoverButton;

        public AuthorFooter()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            BackColor = Form1.PageColor;
            Height = CollapsedHeight;
            _tip.SetToolTip(this, "Bấm ✉ để xem email");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tip.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var over = _mailButton.Contains(e.Location);
            if (over != _hoverButton)
            {
                _hoverButton = over;
                Cursor = over ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverButton = false;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        // Clicking ✉ shows or hides the mail; the window grows or shrinks with it.
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!_mailButton.Contains(e.Location))
            {
                return;
            }

            _showMail = !_showMail;
            var change = ExpandedHeight - CollapsedHeight;
            if (FindForm() is { } form)
            {
                form.ClientSize = new Size(form.ClientSize.Width, form.ClientSize.Height + (_showMail ? change : -change));
            }

            Height = _showMail ? ExpandedHeight : CollapsedHeight;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(FooterTint);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Line 1: "Made with ♥ by Tuấn Anh" and the ✉ button, centred together.
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
            var parts = new (string Text, Font Font, Color Color)[]
            {
                ("Made with ", PlainFont, Form1.MutedTextColor),
                ("❤️", HeartFont, Color.FromArgb(225, 29, 72)),
                (" by ", PlainFont, Form1.MutedTextColor),
                ("Tuấn Anh", NameFont, Form1.Accent)
            };
            const int button = 28;
            const int buttonGap = 10;
            const int lineTop = 14;
            var widths = parts.Select(part => TextRenderer.MeasureText(graphics, part.Text, part.Font, Size.Empty, flags).Width).ToArray();
            var x = (Width - widths.Sum() - buttonGap - button) / 2;
            for (var index = 0; index < parts.Length; index++)
            {
                TextRenderer.DrawText(graphics, parts[index].Text, parts[index].Font,
                    new Rectangle(x, lineTop, widths[index] + 2, button), parts[index].Color, flags);
                x += widths[index];
            }

            _mailButton = new Rectangle(x + buttonGap, lineTop, button, button);
            using (var fill = new SolidBrush(_showMail || _hoverButton ? MailInk : MailFill))
            {
                graphics.FillEllipse(fill, _mailButton);
            }

            TextRenderer.DrawText(graphics, "✉", IconFont, _mailButton, _showMail || _hoverButton ? Color.White : MailInk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Line 2 (only once ✉ was clicked): the mail address in a rounded chip.
            if (!_showMail)
            {
                return;
            }

            const int chipHeight = 28;
            var textWidth = TextRenderer.MeasureText(graphics, Mail, ChipFont, Size.Empty, flags).Width;
            var chip = new RectangleF((Width - textWidth - 32) / 2F, 52, textWidth + 32, chipHeight);
            using (var path = Form1.RoundedMenuRenderer.CreateRoundedPath(chip, chipHeight / 2F))
            using (var brush = new SolidBrush(MailFill))
            {
                graphics.FillPath(brush, path);
            }

            TextRenderer.DrawText(graphics, Mail, ChipFont, Rectangle.Round(chip), MailInk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private const int CornerRadius = 18;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    // A soft drop shadow, since there is no window frame.
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return parameters;
        }
    }

    // Clip the window to a rounded rectangle (all four corners).
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        using var path = Form1.RoundedMenuRenderer.CreateRoundedPath(new RectangleF(0, 0, Width, Height), CornerRadius);
        Region = new Region(path);
    }

    private void SetNameOutline(Color color, float width)
    {
        _nameFrame.OutlineColor = color;
        _nameFrame.OutlineWidth = width;
        _nameFrame.Invalidate();
    }

    private void ContinueButton_Click(object? sender, EventArgs e)
    {
        var name = _nameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _validationLabel.Text = "Vui lòng nhập tên trước khi tiếp tục.";
            _nameTextBox.Focus();
            return;
        }

        EnteredName = name;
        DialogResult = DialogResult.OK;
        Close();
    }
}
