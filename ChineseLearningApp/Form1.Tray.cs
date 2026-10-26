using System.Runtime.InteropServices;

namespace ChineseLearningApp;

// The notification area (tray): closing (X) or minimising (–) the window hides
// it there, so the app and its study website keep running; the tray icon
// opens it again, opens the website, or quits. Windows shutting down, an
// installer closing the app, or an update still close it for real.
public sealed partial class Form1
{
    private NotifyIcon? _tray;
    private IntPtr _trayIconHandle;
    private RegisteredWaitHandle? _showRequestWait;
    private FormWindowState _restoreState = FormWindowState.Normal;
    private bool _exiting;
    private bool _toldAboutTray;

    // Called from the constructor.
    private void InitializeTray()
    {
        var menu = new ContextMenuStrip { Font = new Font(UiFontName, 10F) };
        var open = menu.Items.Add("Mở Học tiếng Trung", null, (_, _) => ShowFromTray());
        open.Font = new Font(UiFontName, 10F, FontStyle.Bold);
        menu.Items.Add("🌐  Mở trang web học", null, (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(StudyWebsite.LocalAddress) { UseShellExecute = true }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát", null, (_, _) => ExitApp());

        _trayIconHandle = DrawTrayIcon();
        _tray = new NotifyIcon
        {
            Icon = Icon.FromHandle(_trayIconHandle),
            Text = "Học tiếng Trung",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowFromTray();
            }
        };

        // Starting the app again while it runs shows this window.
        if (Program.ShowRequested is { } showRequested)
        {
            _showRequestWait = ThreadPool.RegisterWaitForSingleObject(showRequested, (_, _) =>
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(ShowFromTray);
                }
            }, null, Timeout.Infinite, executeOnlyOnce: false);
        }

        FormClosed += (_, _) =>
        {
            _showRequestWait?.Unregister(null);
            _tray.Visible = false;
            _tray.Dispose();
            menu.Dispose();
            DestroyIcon(_trayIconHandle);
        };
    }

    // Quits for real: the last sync and saving run as the window closes.
    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = _restoreState;
        }

        Activate();
    }

    private void HideToTray()
    {
        Hide();
        if (!_toldAboutTray && _tray is not null)
        {
            _toldAboutTray = true;
            _tray.ShowBalloonTip(4000, "Học tiếng Trung vẫn đang chạy",
                "App và trang web học vẫn chạy ở đây. Bấm biểu tượng để mở lại, chuột phải → Thoát để tắt hẳn.", ToolTipIcon.Info);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            if (Visible && _tray is not null)
            {
                HideToTray();
            }
        }
        else
        {
            _restoreState = WindowState;
        }
    }

    // X (or Alt+F4) hides the window; nothing is closed or saved then, so the
    // close handlers (last sync, saving) run only when the app really quits.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_exiting && _tray is not null)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnFormClosing(e);
    }

    // The app's mark, as in the header: a white 汉 on an orange circle.
    private static IntPtr DrawTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var fill = new SolidBrush(Accent);
            g.FillEllipse(fill, 0, 0, 31, 31);
            using var font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("汉", font, Brushes.White, new RectangleF(0, 1, 32, 32), format);
        }

        return bitmap.GetHicon();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
