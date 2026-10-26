namespace ChineseLearningApp;

// Settings card for the study website the app runs (StudyWebsite): whether it
// is running, its addresses on this PC, in the home network and on the
// Internet, whether other machines may open it, and opening the firewall.
public sealed partial class Form1
{
    private RoundedPanel BuildWebsiteSettingsCard()
    {
        Label Line(Font font, Color colour, Padding margin) => new()
        {
            AutoSize = true,
            MaximumSize = new Size(1080, 0),
            Font = font,
            ForeColor = colour,
            BackColor = SurfaceColor,
            Margin = margin
        };

        var status = Line(new Font("Segoe UI Semibold", 11F), BodyTextColor, new Padding(0, 4, 0, 4));
        var addresses = Line(new Font(UiFontName, 10.5F), BodyTextColor, new Padding(0, 2, 0, 6));
        var note = Line(new Font(UiFontName, 9.5F), MutedTextColor, new Padding(0, 8, 0, 0));
        var allowOthers = new CheckBox
        {
            AutoSize = true,
            Checked = StudyWebsite.AllowOthers,
            Text = "Cho máy khác mở trang web (điện thoại cùng Wi-Fi, hoặc từ xa qua Internet khi đã mở cổng NAT trên router)",
            Font = new Font(UiFontName, 10.5F),
            ForeColor = BodyTextColor,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 2, 0, 6)
        };
        string? internetAddress = null;

        void Show()
        {
            var network = StudyWebsite.NetworkAddresses();
            status.Text = StudyWebsite.Running
                ? $"🟢 Trang web đang chạy (cổng {StudyWebsite.Port})"
                : StudyWebsite.Problem is { } problem ? $"⚠ {problem}" : "⏳ Đang khởi động trang web…";
            status.ForeColor = StudyWebsite.Running ? SuccessColor : StudyWebsite.Problem is null ? MutedTextColor : DangerText;
            addresses.Text = $"Trên máy này:  {StudyWebsite.LocalAddress}" + (StudyWebsite.AllowOthers
                ? $"\nTrong mạng nhà (Wi-Fi/LAN):  {(network.Count > 0 ? string.Join("   ", network) : "(chưa kết nối mạng)")}" +
                  $"\nTừ Internet:  {internetAddress ?? "bấm “Xem địa chỉ Internet”"}"
                : "\n(Chỉ máy này mở được trang web.)");
        }

        void Refresh()
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(Show);
            }
        }

        StudyWebsite.Changed += Refresh;
        FormClosed += (_, _) => StudyWebsite.Changed -= Refresh;

        allowOthers.CheckedChanged += async (_, _) =>
        {
            allowOthers.Enabled = false;
            status.Text = "⏳ Đang khởi động lại trang web…";
            await StudyWebsite.RestartAsync(allowOthers.Checked);
            allowOthers.Enabled = true;
            Show();
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = SurfaceColor, Margin = Padding.Empty };
        var openButton = CreateButton("🌐  Mở trang web", (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(StudyWebsite.LocalAddress) { UseShellExecute = true }));
        var internetButton = CreateButton("🌍  Xem địa chỉ Internet", async (sender, _) =>
        {
            var button = (Control)sender!;
            button.Enabled = false;
            note.Text = "⏳ Đang hỏi địa chỉ Internet của mạng này…";
            internetAddress = await StudyWebsite.InternetAddressAsync() ?? "(không lấy được, kiểm tra mạng Internet)";
            note.Text = $"Mở được từ xa sau khi router chuyển cổng {StudyWebsite.Port} (TCP) về máy này. Cách mở cổng: xem HUONG-DAN-TRUY-CAP-TU-XA.md trong thư mục cài app.";
            button.Enabled = true;
            Show();
        }, secondary: true);
        var copyButton = CreateButton("📋  Sao chép địa chỉ", (_, _) =>
        {
            var address = StudyWebsite.AllowOthers
                ? StudyWebsite.NetworkAddresses().FirstOrDefault() ?? StudyWebsite.LocalAddress
                : StudyWebsite.LocalAddress;
            var text = internetAddress?.StartsWith("http", StringComparison.Ordinal) == true ? internetAddress : address;
            try
            {
                // Another program (a clipboard manager, Remote Desktop…) may hold
                // the clipboard for a moment: try a few times.
                Clipboard.SetDataObject(text, copy: true, retryTimes: 10, retryDelay: 100);
                note.Text = $"Đã sao chép: {text}";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                note.Text = $"Chưa sao chép được (bộ nhớ tạm đang bận). Địa chỉ: {text}";
            }
        }, secondary: true);
        var firewallButton = CreateButton("🛡  Mở cổng tường lửa", async (sender, _) =>
        {
            var button = (Control)sender!;
            button.Enabled = false;
            note.Text = "⏳ Windows sẽ hỏi quyền quản trị để mở cổng trong tường lửa…";
            var problem = await StudyWebsite.OpenFirewallAsync();
            note.Text = problem ?? $"✅ Tường lửa Windows đã cho phép cổng {StudyWebsite.Port}.";
            button.Enabled = true;
        }, secondary: true);
        buttons.Controls.AddRange([openButton, internetButton, copyButton, firewallButton]);

        note.Text = "Trang web chạy cùng app: tắt app thì trang web cũng tắt. Truy cập từ xa qua http không mã hóa, hãy dùng mật khẩu riêng cho trang học.";
        Show();

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = SurfaceColor,
            Padding = new Padding(0, 4, 0, 0)
        };
        body.Controls.AddRange([status, addresses, allowOthers, buttons, note]);
        return CreateSettingsSection("🌐", Color.FromArgb(254, 226, 226), "Trang web học",
            "App chạy luôn trang web học: mở trên trình duyệt máy này, điện thoại cùng Wi-Fi, hoặc từ xa qua Internet.", body);
    }
}
