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
        var certificate = Line(new Font(UiFontName, 10.5F), BodyTextColor, new Padding(0, 0, 0, 6));
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

        // https under a domain name, so Google sign-in works from outside.
        var https = new CheckBox
        {
            AutoSize = true,
            Checked = StudyWebsite.Https,
            Text = "Bật https (đăng nhập Google từ xa), tên miền:",
            Font = new Font(UiFontName, 10.5F),
            ForeColor = BodyTextColor,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 6, 6, 0)
        };
        var domain = new TextBox
        {
            Width = 250,
            Text = StudyWebsite.HttpsDomain,
            PlaceholderText = "tự lấy từ IP WAN (…sslip.io)",
            Font = new Font(UiFontName, 10.5F),
            Margin = new Padding(0, 3, 10, 0)
        };
        // Let's Encrypt refuses to issue certificates to an account without one.
        var emailLabel = new Label
        {
            AutoSize = true,
            Text = "Email:",
            Font = new Font(UiFontName, 10.5F),
            ForeColor = BodyTextColor,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 6, 4, 0)
        };
        var email = new TextBox
        {
            Width = 230,
            Text = StudyWebsite.HttpsEmail,
            PlaceholderText = "email nhận tin về chứng chỉ",
            Font = new Font(UiFontName, 10.5F),
            Margin = new Padding(0, 3, 6, 0)
        };
        var httpsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = SurfaceColor, Margin = new Padding(0, 0, 0, 6) };

        string HttpsNote() =>
            $"https: router cần chuyển thêm cổng 80 và 443 (TCP) về máy này; lần đầu mở {StudyWebsite.HttpsAddress}, chứng chỉ Let's Encrypt " +
            "được xin tự động (khoảng 1 phút). Để đăng nhập Google: trong Google Cloud thêm " +
            $"{StudyWebsite.HttpsAddress} vào Authorized JavaScript origins và {StudyWebsite.HttpsAddress}/api/oauth/google/callback vào Authorized redirect URIs.";

        void Show()
        {
            var network = StudyWebsite.NetworkAddresses();
            status.Text = StudyWebsite.Running
                ? $"🟢 Trang web đang chạy (cổng {StudyWebsite.Port}{(StudyWebsite.ServesHttps ? ", https 443" : "")})"
                : StudyWebsite.Problem is { } problem ? $"⚠ {problem}" : "⏳ Đang khởi động trang web…";
            status.ForeColor = StudyWebsite.Running ? SuccessColor : StudyWebsite.Problem is null ? MutedTextColor : DangerText;
            addresses.Text = $"Trên máy này:  {StudyWebsite.LocalAddress}" + (StudyWebsite.AllowOthers
                ? $"\nTrong mạng nhà (Wi-Fi/LAN):  {(network.Count > 0 ? string.Join("   ", network) : "(chưa kết nối mạng)")}" +
                  $"\nTừ Internet:  {internetAddress ?? "bấm “Xem địa chỉ Internet”"}" +
                  (StudyWebsite.ServesHttps ? $"\nQua https (đăng nhập Google được):  {StudyWebsite.HttpsAddress}" : "")
                : "\n(Chỉ máy này mở được trang web.)");
            httpsRow.Enabled = StudyWebsite.AllowOthers;

            // The https certificate: valid until when, or why there is none yet.
            var needsEmail = StudyWebsite.Https && StudyWebsite.AllowOthers && !StudyWebsite.IsEmail(StudyWebsite.HttpsEmail);
            certificate.Visible = (StudyWebsite.ServesHttps || needsEmail) && StudyWebsite.Running;
            if (needsEmail)
            {
                (certificate.Text, certificate.ForeColor) =
                    ("⚠ https chưa bật: Let's Encrypt cần một email để cấp chứng chỉ. Nhập email rồi bấm “Áp dụng”.", DangerText);
            }
            else if (certificate.Visible)
            {
                var expires = StudyWebsite.CertificateExpires();
                (certificate.Text, certificate.ForeColor) = expires is { } until
                    ? ($"🔒 Chứng chỉ https hợp lệ đến {until:dd/MM/yyyy} (tự gia hạn).", SuccessColor)
                    : StudyWebsite.CertificateProblem is { } why
                        ? ($"⚠ Chưa có chứng chỉ https. Let's Encrypt báo: {why}\nKiểm tra router đã chuyển cổng 80 và 443 (TCP) về máy này, và tường lửa đã mở.", DangerText)
                        : ("⏳ Đang xin chứng chỉ https từ Let's Encrypt… (cần router chuyển cổng 80 và 443 về máy này)", MutedTextColor);
            }

            if (StudyWebsite.DomainChange is { } change)
            {
                note.Text = change;
            }

            // An sslip.io name may have followed a new WAN IP.
            if (!domain.Focused)
            {
                domain.Text = StudyWebsite.HttpsDomain;
            }
        }

        // Starts the website again with https as chosen; the domain is the WAN
        // IP's sslip.io name unless one is typed in.
        async Task ApplyHttps()
        {
            httpsRow.Enabled = false;
            allowOthers.Enabled = false;
            var name = domain.Text.Trim().ToLowerInvariant();
            if (https.Checked && name.Length == 0)
            {
                status.Text = "⏳ Đang lấy tên miền theo địa chỉ Internet…";
                name = await StudyWebsite.DomainForInternetAddressAsync() ?? "";
                domain.Text = name;
            }

            if (https.Checked && !StudyWebsite.IsDomainName(name))
            {
                note.Text = name.Length == 0
                    ? "⚠ Không lấy được địa chỉ Internet. Kiểm tra mạng, hoặc gõ tên miền (ví dụ ten-ban.duckdns.org)."
                    : "⚠ Tên miền không hợp lệ (ví dụ 14-241-68-10.sslip.io hoặc ten-ban.duckdns.org).";
                https.Checked = false;
                httpsRow.Enabled = true;
                allowOthers.Enabled = true;
                return;
            }

            var address = email.Text.Trim();
            if (https.Checked && !StudyWebsite.IsEmail(address))
            {
                note.Text = address.Length == 0
                    ? "⚠ Nhập email của bạn rồi bấm “Áp dụng”: Let's Encrypt cần email để cấp chứng chỉ https."
                    : "⚠ Email không hợp lệ.";
                httpsRow.Enabled = true;
                allowOthers.Enabled = true;
                Show();
                email.Focus();
                return;
            }

            status.Text = "⏳ Đang khởi động lại trang web…";
            await StudyWebsite.RestartAsync(StudyWebsite.AllowOthers, https.Checked, name, address);
            note.Text = StudyWebsite.ServesHttps ? HttpsNote() : "Đã tắt https.";
            httpsRow.Enabled = true;
            allowOthers.Enabled = true;
            Show();
        }

        var applyHttps = CreateButton("Áp dụng", async (_, _) => await ApplyHttps(), secondary: true);
        applyHttps.Margin = Padding.Empty;
        https.CheckedChanged += async (_, _) =>
        {
            if (httpsRow.Enabled && https.Checked != StudyWebsite.Https)
            {
                await ApplyHttps();
            }
        };
        httpsRow.Controls.AddRange([https, domain, emailLabel, email, applyHttps]);

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
            var text = StudyWebsite.ServesHttps ? StudyWebsite.HttpsAddress
                : internetAddress?.StartsWith("http", StringComparison.Ordinal) == true ? internetAddress : address;
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
            note.Text = problem ?? $"✅ Tường lửa Windows đã cho phép cổng {StudyWebsite.Port}{(StudyWebsite.Https ? ", 80 và 443" : "")}.";
            button.Enabled = true;
        }, secondary: true);
        buttons.Controls.AddRange([openButton, internetButton, copyButton, firewallButton]);

        note.Text = StudyWebsite.ServesHttps
            ? HttpsNote()
            : "Trang web chạy cùng app: tắt app thì trang web cũng tắt. Truy cập từ xa qua http không mã hóa: bật https để mã hóa và để đăng nhập Google từ xa.";
        Show();

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = SurfaceColor,
            Padding = new Padding(0, 4, 0, 0)
        };
        body.Controls.AddRange([status, addresses, certificate, allowOthers, httpsRow, buttons, note]);
        return CreateSettingsSection("🌐", Color.FromArgb(254, 226, 226), "Trang web học",
            "App chạy luôn trang web học: mở trên trình duyệt máy này, điện thoại cùng Wi-Fi, hoặc từ xa qua Internet.", body);
    }
}
