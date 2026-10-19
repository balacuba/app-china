using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ChineseLearningApp;

// Getting the learner's free Gemini API key with as little effort as possible:
// one button opens Google AI Studio in the browser, where they sign in to their
// own Google account and create and copy a key. While this box is open it watches
// the clipboard, so a copied key is picked up, checked with Google and saved
// without pasting. A key can also be pasted by hand.
internal sealed class GeminiKeyDialog : Form
{
    private const string KeyPage = "https://aistudio.google.com/app/apikey";

    private readonly TextBox _key = new();
    private readonly Label _status = new();
    private readonly Button _save;
    private readonly System.Windows.Forms.Timer _clipboardWatch = new() { Interval = 500 };
    private readonly CancellationTokenSource _closing = new();
    private string _lastClipboard = string.Empty;
    private bool _checking;

    public GeminiKeyDialog()
    {
        Text = "API key Gemini";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Form1.SurfaceColor;
        Font = new Font(Form1.UiFontName, 10.5F);
        ClientSize = new Size(580, 380);
        Padding = new Padding(24, 20, 24, 20);

        var title = new Label
        {
            AutoSize = true,
            Text = "🔑  Kết nối Google Gemini",
            Font = new Font("Segoe UI Semibold", 14F),
            ForeColor = Form1.TextColor
        };
        var steps = new Label
        {
            AutoSize = false,
            Size = new Size(530, 72),
            ForeColor = Form1.BodyTextColor,
            Text = "1. Bấm nút dưới đây để mở Google AI Studio, đăng nhập tài khoản Google của bạn.\n" +
                   "2. Bấm “Create API key” (miễn phí), rồi bấm nút Copy bên cạnh key.\n" +
                   "3. Xong! App tự nhận key vừa copy, kiểm tra và lưu lại."
        };
        var signIn = Form1.CreateButton("🌐  Đăng nhập Google và lấy key", (_, _) => OpenKeyPage());
        signIn.Margin = new Padding(0, 4, 0, 10);

        _key.Width = 420;
        _key.UseSystemPasswordChar = true;
        _key.PlaceholderText = "…hoặc dán key vào đây (bắt đầu bằng AIza)";
        _key.Text = GeminiKey.Load();
        var show = new CheckBox { AutoSize = true, Text = "Hiện key", Margin = new Padding(12, 4, 0, 0) };
        show.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !show.Checked;
        var keyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        keyRow.Controls.AddRange([_key, show]);

        _status.AutoSize = false;
        _status.Size = new Size(530, 44);
        _status.ForeColor = Form1.MutedTextColor;
        _status.Text = "Key được mã hoá và chỉ lưu trên máy này, cho tài khoản Windows của bạn.";

        _save = Form1.CreateButton("Lưu", async (_, _) => await CheckAndSaveAsync(_key.Text));
        var cancel = Form1.CreateButton("Hủy", (_, _) => Close(), secondary: true);
        var remove = Form1.CreateButton("Xóa key", (_, _) => Remove(), secondary: true, danger: true);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom };
        buttons.Controls.AddRange([cancel, _save, remove]);
        AcceptButton = _save;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.AddRange([title, steps, signIn, keyRow, _status]);
        Controls.Add(layout);
        Controls.Add(buttons);

        // Note what is on the clipboard now, so only a key copied from here on is
        // taken up automatically.
        _lastClipboard = ReadClipboard();
        _clipboardWatch.Tick += async (_, _) => await WatchClipboardAsync();
        _clipboardWatch.Start();
    }

    private void OpenKeyPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(KeyPage) { UseShellExecute = true });
            ShowStatus("⏳ Đang chờ bạn đăng nhập, tạo key và bấm Copy trong trình duyệt…", Form1.Accent);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowStatus($"Không mở được trình duyệt. Hãy tự mở {KeyPage}", Form1.DangerText);
        }
    }

    // A newly copied Gemini key goes straight into the box and is checked and saved.
    private async Task WatchClipboardAsync()
    {
        if (_checking)
        {
            return;
        }

        var copied = ReadClipboard();
        if (copied == _lastClipboard)
        {
            return;
        }

        _lastClipboard = copied;
        if (GeminiAi.KeyShape().IsMatch(copied))
        {
            _key.Text = copied;
            Activate();
            await CheckAndSaveAsync(copied);
        }
    }

    private static string ReadClipboard()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
        }
        catch (ExternalException)
        {
            // Another program has the clipboard open; look again on the next tick.
            return string.Empty;
        }
    }

    private async Task CheckAndSaveAsync(string key)
    {
        key = key.Trim();
        if (key.Length == 0)
        {
            ShowStatus("Chưa có key. Bấm “Đăng nhập Google và lấy key” hoặc dán key vào ô.", Form1.DangerText);
            return;
        }

        _checking = true;
        _save.Enabled = false;
        ShowStatus("⏳ Đang kiểm tra key với Google…", Form1.Accent);
        try
        {
            var problem = await GeminiAi.CheckKeyAsync(key, _closing.Token);
            if (problem is not null)
            {
                ShowStatus("⚠ " + problem, Form1.DangerText);
                return;
            }

            GeminiKey.Save(key);
            ShowStatus("✅ Key hợp lệ, đã lưu!", Color.FromArgb(22, 163, 74));
            await Task.Delay(700, _closing.Token);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            ShowStatus($"⚠ Không thể lưu key: {ex.Message}", Form1.DangerText);
        }
        finally
        {
            _checking = false;
            if (!IsDisposed)
            {
                _save.Enabled = true;
            }
        }
    }

    private void Remove()
    {
        try
        {
            GeminiKey.Save(string.Empty);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"⚠ Không thể xóa key: {ex.Message}", Form1.DangerText);
        }
    }

    private void ShowStatus(string text, Color colour)
    {
        if (IsDisposed)
        {
            return;
        }

        _status.Text = text;
        _status.ForeColor = colour;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _clipboardWatch.Stop();
        _closing.Cancel();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clipboardWatch.Dispose();
            _closing.Dispose();
        }

        base.Dispose(disposing);
    }
}
