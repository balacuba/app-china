using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinFormsApp1
{
    public class Form1 : Form
    {
        private readonly ProductionService _prodService = new();
        private readonly HttpClient _httpClient;
        private readonly Dictionary<Control, float> _originalFontSizes = new();
        private ProductionApiClient? _productionApiClient;
        private int _passCount = 0;
        private bool _isProcessing;
        private Size _originalFormSize;
        private IContainer components = new Container();

        private Panel panelHeader = null!;
        private PictureBox picLogo = null!;
        private Label lblCongDoan = null!;
        private ComboBox cboCongDoan = null!;
        private TableLayoutPanel tableLayoutPanelMain = null!;
        private Label lblSN = null!;
        private TextBox txtSN = null!;
        private Label lblSNPass = null!;
        private Label lblCongDoanHienTaiTitle = null!;
        private Label lblCongDoanHienTaiValue = null!;
        private Label lblWO = null!;
        private Label lblWOValue = null!;
        private Button btnResetWO = null!;

        public Form1()
        {
            InitializeComponent();
            _httpClient = _prodService.CreateHttpClient();
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            _originalFormSize = ClientSize;
            MinimumSize = new Size(400, 250);
            MinimizeBox = true;
            MaximizeBox = true;
            SaveOriginalFontSizes(this);
            LoadLogoFromDirectory();
            UpdateCountUI();
            txtSN.Focus();
        }

        private void UpdateCountUI()
        {
            lblSNPass.Text = $"Pass : {_passCount}";
            lblSNPass.ForeColor = Color.ForestGreen;
        }

        private void LoadLogoFromDirectory()
        {
            try
            {
                var path = "D:\\C#\\WinFormsApp1 - Copy\\WinFormsApp1\\Logo MKV_pdf in chuẩn.png";
                if (!File.Exists(path))
                {
                    path = Path.Combine(Application.StartupPath, "Logo MKV_pdf in chuẩn.png");
                }

                if (File.Exists(path))
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                    picLogo.Image = Image.FromStream(stream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi load logo: " + ex.Message);
            }
        }

        private void SaveOriginalFontSizes(Control container)
        {
            foreach (Control c in container.Controls)
            {
                if (!_originalFontSizes.ContainsKey(c))
                {
                    _originalFontSizes.Add(c, c.Font.Size);
                }

                if (c.HasChildren)
                {
                    SaveOriginalFontSizes(c);
                }
            }
        }

        private void Form1_Resize(object? sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized || ClientSize.Width < 20 || ClientSize.Height < 20 || _originalFormSize.Width == 0)
            {
                return;
            }

            var scaleX = (float)ClientSize.Width / _originalFormSize.Width;
            var scaleY = (float)ClientSize.Height / _originalFormSize.Height;
            var scaleFont = Math.Min(scaleX, scaleY);

            SuspendLayout();
            ResizeFontsWithBounds(this, scaleFont);
            ResumeLayout();
        }

        private void ResizeFontsWithBounds(Control container, float scaleFont)
        {
            foreach (Control c in container.Controls)
            {
                if (_originalFontSizes.TryGetValue(c, out var originalSize))
                {
                    var newSize = Math.Max(8f, Math.Min(24f, originalSize * scaleFont));
                    if (Math.Abs(c.Font.Size - newSize) > 0.01f)
                    {
                        var oldFont = c.Font;
                        c.Font = new Font(oldFont.FontFamily, newSize, oldFont.Style);
                        if (oldFont != SystemFonts.DefaultFont)
                        {
                            oldFont.Dispose();
                        }
                    }
                }

                if (c.HasChildren)
                {
                    ResizeFontsWithBounds(c, scaleFont);
                }
            }
        }

        private void txtSN_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || _isProcessing)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = ProcessSerialNumberAsync();
        }

        private async Task ProcessSerialNumberAsync()
        {
            if (string.IsNullOrWhiteSpace(txtSN.Text))
            {
                txtSN.Focus();
                return;
            }

            _isProcessing = true;
            txtSN.Enabled = false;
            btnResetWO.Enabled = false;
            try
            {
                var serial = txtSN.Text.Trim();
                var previousWorkOrder = lblWOValue.Text;
                var selectedStageCode = (cboCongDoan.SelectedItem as ProductionStage)?.StageCode;
                _productionApiClient ??= new ProductionApiClient(_httpClient);
                var profile = await _productionApiClient.GetSerialProductionInfoAsync(serial);

                lblWOValue.Text = profile.WorkOrderCode;
                cboCongDoan.DataSource = null;
                cboCongDoan.DisplayMember = nameof(ProductionStage.StageName);
                cboCongDoan.ValueMember = nameof(ProductionStage.StageCode);
                cboCongDoan.DataSource = profile.Stages.ToList();
                cboCongDoan.SelectedIndex = -1;

                if (string.Equals(previousWorkOrder, profile.WorkOrderCode, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(selectedStageCode))
                {
                    cboCongDoan.SelectedValue = selectedStageCode;
                }

                var selectedStage = cboCongDoan.SelectedItem as ProductionStage;
                lblCongDoanHienTaiValue.Text = profile.CurrentStageName ??
                    profile.Stages.FirstOrDefault(stage =>
                        string.Equals(stage.StageCode, profile.CurrentStageCode, StringComparison.OrdinalIgnoreCase))
                    ?.StageName ??
                    profile.CurrentStageCode ??
                    string.Empty;

                if (selectedStage is null)
                {
                    MessageBox.Show(
                        "Vui lòng chọn công đoạn trước khi quét SN.",
                        "Chưa chọn công đoạn",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    txtSN.Clear();
                    txtSN.Focus();
                    return;
                }

                var stageCheck = await _productionApiClient.CheckCanTestAsync(
                    selectedStage.StageCode,
                    serial);
                if (!stageCheck.CanTest)
                {
                    if (!string.Equals(stageCheck.Message, "NO", StringComparison.OrdinalIgnoreCase))
                    {
                        lblCongDoanHienTaiValue.Text = stageCheck.Message;
                    }
                    else if (string.IsNullOrWhiteSpace(lblCongDoanHienTaiValue.Text))
                    {
                        lblCongDoanHienTaiValue.Text = "Sai công đoạn";
                    }

                    txtSN.Clear();
                    return;
                }

                await _productionApiClient.SubmitResultAsync(
                    selectedStage.StageCode,
                    serial,
                    passed: true);
                _passCount++;
                UpdateCountUI();
                txtSN.Clear();
                txtSN.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xử lý SN: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isProcessing = false;
                btnResetWO.Enabled = true;
                txtSN.Enabled = true;
                cboCongDoan.Enabled = cboCongDoan.Items.Count > 0;
            }
        }

        private void btnResetWO_Click(object? sender, EventArgs e)
        {
            lblWOValue.Text = string.Empty;
            lblCongDoanHienTaiValue.Text = string.Empty;
            cboCongDoan.DataSource = null;
            cboCongDoan.Items.Clear();
            cboCongDoan.SelectedIndex = -1;
            txtSN.Clear();
            txtSN.Enabled = true;
            cboCongDoan.Enabled = true;
            UpdateCountUI();
            txtSN.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }

            if (disposing)
            {
                _httpClient.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            panelHeader = new Panel();
            picLogo = new PictureBox();
            lblCongDoan = new Label();
            cboCongDoan = new ComboBox();
            tableLayoutPanelMain = new TableLayoutPanel();
            lblSN = new Label();
            txtSN = new TextBox();
            lblSNPass = new Label();
            lblCongDoanHienTaiTitle = new Label();
            lblCongDoanHienTaiValue = new Label();
            lblWO = new Label();
            lblWOValue = new Label();
            btnResetWO = new Button();

            panelHeader.SuspendLayout();
            tableLayoutPanelMain.SuspendLayout();
            SuspendLayout();

            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            MinimumSize = new Size(420, 260);

            panelHeader.BackColor = Color.FromArgb(232, 239, 246);
            panelHeader.BorderStyle = BorderStyle.FixedSingle;
            panelHeader.Controls.Add(cboCongDoan);
            panelHeader.Controls.Add(lblCongDoan);
            panelHeader.Controls.Add(picLogo);
            panelHeader.Dock = DockStyle.Top;
            panelHeader.Location = new Point(0, 0);
            panelHeader.Name = "panelHeader";
            panelHeader.Padding = new Padding(12, 8, 12, 8);
            panelHeader.Size = new Size(540, 58);
            panelHeader.TabIndex = 0;

            picLogo.BackColor = Color.Transparent;
            picLogo.Dock = DockStyle.Left;
            picLogo.Location = new Point(12, 8);
            picLogo.Margin = new Padding(0, 0, 10, 0);
            picLogo.Name = "picLogo";
            picLogo.Size = new Size(110, 42);
            picLogo.SizeMode = PictureBoxSizeMode.Zoom;
            picLogo.TabIndex = 1;
            picLogo.TabStop = false;

            lblCongDoan.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            lblCongDoan.AutoSize = true;
            lblCongDoan.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            lblCongDoan.ForeColor = Color.FromArgb(47, 79, 79);
            lblCongDoan.Location = new Point(339, 18);
            lblCongDoan.Name = "lblCongDoan";
            lblCongDoan.Size = new Size(89, 19);
            lblCongDoan.TabIndex = 2;
            lblCongDoan.Text = "Công Đoạn :";

            cboCongDoan.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            cboCongDoan.DropDownStyle = ComboBoxStyle.DropDownList;
            cboCongDoan.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            cboCongDoan.ForeColor = Color.FromArgb(35, 35, 35);
            cboCongDoan.FormattingEnabled = true;
            cboCongDoan.Location = new Point(429, 15);
            cboCongDoan.Name = "cboCongDoan";
            cboCongDoan.Size = new Size(90, 25);
            cboCongDoan.TabIndex = 3;

            tableLayoutPanelMain.ColumnCount = 5;
            tableLayoutPanelMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            tableLayoutPanelMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            tableLayoutPanelMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10F));
            tableLayoutPanelMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21F));
            tableLayoutPanelMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 9F));
            tableLayoutPanelMain.Dock = DockStyle.Fill;
            tableLayoutPanelMain.Location = new Point(0, 58);
            tableLayoutPanelMain.Name = "tableLayoutPanelMain";
            tableLayoutPanelMain.Padding = new Padding(18, 18, 18, 18);
            tableLayoutPanelMain.RowCount = 2;
            tableLayoutPanelMain.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanelMain.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanelMain.Size = new Size(540, 142);
            tableLayoutPanelMain.TabIndex = 1;

            lblSN.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            lblSN.TextAlign = ContentAlignment.MiddleLeft;
            lblSN.AutoSize = true;
            lblSN.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            lblSN.ForeColor = Color.FromArgb(26, 35, 48);
            lblSN.Location = new Point(21, 31);
            lblSN.Name = "lblSN";
            lblSN.Size = new Size(157, 20);
            lblSN.TabIndex = 0;
            lblSN.Text = "SN :";
            tableLayoutPanelMain.Controls.Add(lblSN, 0, 0);

            txtSN.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtSN.BackColor = Color.White;
            txtSN.BorderStyle = BorderStyle.FixedSingle;
            txtSN.Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            txtSN.Location = new Point(184, 28);
            txtSN.Margin = new Padding(3, 3, 10, 3);
            txtSN.Name = "txtSN";
            txtSN.Size = new Size(196, 27);
            txtSN.TabIndex = 1;
            txtSN.KeyDown += txtSN_KeyDown;
            tableLayoutPanelMain.Controls.Add(txtSN, 1, 0);
            tableLayoutPanelMain.SetColumnSpan(txtSN, 2);

            lblSNPass.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            lblSNPass.AutoSize = true;
            lblSNPass.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            lblSNPass.ForeColor = Color.ForestGreen;
            lblSNPass.Location = new Point(390, 31);
            lblSNPass.Name = "lblSNPass";
            lblSNPass.Size = new Size(132, 20);
            lblSNPass.TabIndex = 2;
            lblSNPass.Text = "Pass : 0";
            tableLayoutPanelMain.Controls.Add(lblSNPass, 3, 0);
            tableLayoutPanelMain.SetColumnSpan(lblSNPass, 2);

            lblCongDoanHienTaiTitle.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            lblCongDoanHienTaiTitle.AutoSize = true;
            lblCongDoanHienTaiTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            lblCongDoanHienTaiTitle.ForeColor = Color.FromArgb(18, 90, 58);
            lblCongDoanHienTaiTitle.Location = new Point(21, 97);
            lblCongDoanHienTaiTitle.Margin = new Padding(0, 0, 0, 0);
            lblCongDoanHienTaiTitle.Name = "lblCongDoanHienTaiTitle";
            lblCongDoanHienTaiTitle.Size = new Size(157, 20);
            lblCongDoanHienTaiTitle.TabIndex = 3;
            lblCongDoanHienTaiTitle.Text = "Công đoạn hiện tại :";
            tableLayoutPanelMain.Controls.Add(lblCongDoanHienTaiTitle, 0, 1);

            lblCongDoanHienTaiValue.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            lblCongDoanHienTaiValue.AutoSize = true;
            lblCongDoanHienTaiValue.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            lblCongDoanHienTaiValue.ForeColor = Color.DarkRed;
            lblCongDoanHienTaiValue.Location = new Point(184, 97);
            lblCongDoanHienTaiValue.Name = "lblCongDoanHienTaiValue";
            lblCongDoanHienTaiValue.Size = new Size(151, 20);
            lblCongDoanHienTaiValue.TabIndex = 4;
            lblCongDoanHienTaiValue.Text = string.Empty;
            tableLayoutPanelMain.Controls.Add(lblCongDoanHienTaiValue, 1, 1);

            lblWO.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            lblWO.TextAlign = ContentAlignment.MiddleLeft;
            lblWO.AutoSize = true;
            lblWO.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            lblWO.ForeColor = Color.FromArgb(26, 35, 48);
            lblWO.Location = new Point(341, 97);
            lblWO.Name = "lblWO";
            lblWO.Size = new Size(43, 20);
            lblWO.TabIndex = 5;
            lblWO.Text = "WO:";
            tableLayoutPanelMain.Controls.Add(lblWO, 2, 1);

            lblWOValue.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            lblWOValue.AutoSize = true;
            lblWOValue.Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
            lblWOValue.ForeColor = Color.FromArgb(34, 34, 34);
            lblWOValue.Location = new Point(390, 97);
            lblWOValue.Name = "lblWOValue";
            lblWOValue.Size = new Size(123, 20);
            lblWOValue.TabIndex = 6;
            lblWOValue.Text = string.Empty;
            tableLayoutPanelMain.Controls.Add(lblWOValue, 3, 1);

            btnResetWO.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            btnResetWO.BackColor = Color.FromArgb(35, 120, 255);
            btnResetWO.FlatAppearance.BorderSize = 0;
            btnResetWO.FlatStyle = FlatStyle.Flat;
            btnResetWO.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnResetWO.ForeColor = Color.White;
            btnResetWO.Location = new Point(519, 94);
            btnResetWO.Margin = new Padding(3, 3, 5, 3);
            btnResetWO.Name = "btnResetWO";
            btnResetWO.Size = new Size(33, 28);
            btnResetWO.TabIndex = 7;
            btnResetWO.Text = "Đổi";
            btnResetWO.UseVisualStyleBackColor = false;
            btnResetWO.Click += btnResetWO_Click;
            tableLayoutPanelMain.Controls.Add(btnResetWO, 4, 1);

            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(540, 200);
            Controls.Add(tableLayoutPanelMain);
            Controls.Add(panelHeader);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "SN Check Pass";
            Load += Form1_Load;
            Resize += Form1_Resize;

            panelHeader.ResumeLayout(false);
            panelHeader.PerformLayout();
            tableLayoutPanelMain.ResumeLayout(false);
            tableLayoutPanelMain.PerformLayout();
            ResumeLayout(false);
        }
    }

    public class ProductionService
    {
        public HttpClient CreateHttpClient()
        {
            return new HttpClient();
        }
    }
}
