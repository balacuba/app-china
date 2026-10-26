namespace ChineseLearningApp;

// Updating the app (UpdateService): a quiet check a little after the app opens
// (at most once a day, if allowed), the "Cập nhật" card in Settings, and the
// dialog that offers a new version, downloads it and starts its installer.
public sealed partial class Form1
{
    private bool _updating;

    // Called from the constructor.
    private void InitializeUpdates()
    {
#if !DEBUG
        Shown += async (_, _) =>
        {
            var settings = UpdateService.Load();
            if (!settings.CheckOnStart || DateTimeOffset.Now - settings.LastCheck < TimeSpan.FromDays(1))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(8));
            await CheckForUpdateAsync(fromButton: false, status: null);
        };
#endif
    }

    // Asks for a newer version and, if there is one, offers it. `status`
    // (the Settings card's line) shows how it went.
    private async Task CheckForUpdateAsync(bool fromButton, Action<string, Color>? status)
    {
        if (_updating || IsDisposed)
        {
            return;
        }

        _updating = true;
        try
        {
            status?.Invoke("⏳ Đang kiểm tra bản mới…", MutedTextColor);
            AppRelease? release;
            try
            {
                release = await UpdateService.FindNewerAsync(CancellationToken.None);
            }
            catch (UpdateException ex)
            {
                status?.Invoke($"⚠ {ex.Message}", DangerText);
                return;
            }

            var settings = UpdateService.Load();
            settings.LastCheck = DateTimeOffset.Now;
            UpdateService.Save(settings);
            if (IsDisposed)
            {
                return;
            }

            if (release is null)
            {
                status?.Invoke($"✅ Đang dùng bản mới nhất ({UpdateService.Current}).", SuccessColor);
                return;
            }

            // The quiet check does not ask again about a version the learner skipped.
            if (!fromButton && settings.SkippedVersion == release.Version.ToString())
            {
                return;
            }

            status?.Invoke($"Có bản mới {release.Version}.", Accent);
            OfferUpdate(release, status);
        }
        finally
        {
            _updating = false;
        }
    }

    // "Có bản mới": update now (download with a progress bar, then install),
    // later, or skip this version.
    private void OfferUpdate(AppRelease release, Action<string, Color>? status)
    {
        var updateNow = new TaskDialogButton("Cập nhật ngay") { AllowCloseDialog = false };
        var later = new TaskDialogButton("Để sau");
        var skip = new TaskDialogButton("Bỏ qua bản này");
        var offer = new TaskDialogPage
        {
            Caption = "Cập nhật Học tiếng Trung",
            Heading = $"Có bản mới {release.Version} (đang dùng {UpdateService.Current})",
            Text = (release.Notes.Length > 0 ? release.Notes + "\n\n" : string.Empty) +
                   "App sẽ tải bản mới, tự đóng, cài đặt rồi mở lại. Tiến độ học được giữ nguyên.",
            Icon = TaskDialogIcon.Information,
            Buttons = { updateNow, later, skip }
        };

        string? installer = null;
        string? problem = null;
        var progressBar = new TaskDialogProgressBar { Minimum = 0, Maximum = 100 };
        var cancel = new TaskDialogButton("Hủy");
        var downloading = new TaskDialogPage
        {
            Caption = offer.Caption,
            Heading = $"Đang tải bản {release.Version}…",
            Text = "0%",
            ProgressBar = progressBar,
            Buttons = { cancel }
        };
        using var stop = new CancellationTokenSource();
        cancel.Click += (_, _) => stop.Cancel();
        downloading.Created += async (_, _) =>
        {
            try
            {
                installer = await UpdateService.DownloadAsync(release, new Progress<int>(percent =>
                {
                    if (downloading.BoundDialog is not null)
                    {
                        progressBar.Value = percent;
                        downloading.Text = $"{percent}%";
                    }
                }), stop.Token);
            }
            catch (UpdateException ex)
            {
                problem = ex.Message;
            }
            catch (OperationCanceledException)
            {
                problem = "Đã hủy tải bản cập nhật.";
            }

            downloading.BoundDialog?.Close();
        };
        updateNow.Click += (_, _) => offer.Navigate(downloading);

        var choice = TaskDialog.ShowDialog(this, offer);
        if (choice == skip)
        {
            var settings = UpdateService.Load();
            settings.SkippedVersion = release.Version.ToString();
            UpdateService.Save(settings);
            status?.Invoke($"Đã bỏ qua bản {release.Version}.", MutedTextColor);
            return;
        }

        if (installer is null)
        {
            if (problem is not null)
            {
                status?.Invoke($"⚠ {problem}", DangerText);
            }

            return;
        }

        try
        {
            UpdateService.StartInstaller(installer);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            status?.Invoke("⚠ Không chạy được bộ cài bản mới.", DangerText);
            return;
        }

        // The installer replaces the app's files once it has closed (progress
        // is synced and saved on the way out as usual).
        Close();
    }

    // Settings card: this version, "Kiểm tra cập nhật", and checking on start.
    private RoundedPanel BuildUpdateSettingsCard()
    {
        var status = new Label
        {
            AutoSize = true,
            Font = new Font(UiFontName, 10.5F),
            ForeColor = MutedTextColor,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 10, 0, 0),
            Text = $"Phiên bản đang dùng: {UpdateService.Current}"
        };
        void Show(string text, Color colour)
        {
            if (!status.IsDisposed)
            {
                status.Text = text;
                status.ForeColor = colour;
            }
        }

        var checkOnStart = new CheckBox
        {
            AutoSize = true,
            Checked = UpdateService.Load().CheckOnStart,
            Text = "Tự kiểm tra bản mới khi mở app (mỗi ngày một lần)",
            Font = new Font(UiFontName, 10.5F),
            ForeColor = BodyTextColor,
            BackColor = SurfaceColor,
            Margin = new Padding(0, 2, 0, 8)
        };
        checkOnStart.CheckedChanged += (_, _) =>
        {
            var settings = UpdateService.Load();
            settings.CheckOnStart = checkOnStart.Checked;
            UpdateService.Save(settings);
        };

        var checkButton = CreateButton("🔄  Kiểm tra cập nhật", async (sender, _) =>
        {
            var button = (Control)sender!;
            button.Enabled = false;
            await CheckForUpdateAsync(fromButton: true, Show);
            if (!button.IsDisposed)
            {
                button.Enabled = true;
            }
        });
        checkButton.Margin = new Padding(0, 0, 0, 0);

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = SurfaceColor,
            Padding = new Padding(0, 4, 0, 0)
        };
        body.Controls.AddRange([checkOnStart, checkButton, status]);
        return CreateSettingsSection("⬆", Color.FromArgb(219, 234, 254), "Cập nhật",
            $"Tải và cài bản mới của app từ trang phát hành (github.com/{UpdateService.Repository}).", body);
    }
}
