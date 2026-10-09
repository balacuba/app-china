namespace ChineseLearningApp;

internal sealed class NameEntryForm : Form
{
    private readonly TextBox _nameTextBox = new();
    private readonly Label _validationLabel = new();

    public string EnteredName { get; private set; } = string.Empty;

    public NameEntryForm()
    {
        Text = "Ứng dụng học tiếng trung của Tuấn Anh!";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(380, 190);
        Font = new Font("Segoe UI", 10F);

        var prompt = new Label
        {
            AutoSize = true,
            Location = new Point(24, 22),
            Text = "Tên bạn là gì ?"
        };
        _nameTextBox.Location = new Point(24, 56);
        _nameTextBox.Size = new Size(332, 30);
        _nameTextBox.MaxLength = 80;
        _nameTextBox.PlaceholderText = "Nhập tên của bạn";

        _validationLabel.AutoSize = true;
        _validationLabel.ForeColor = Color.Firebrick;
        _validationLabel.Location = new Point(24, 91);

        var continueButton = new Button
        {
            Location = new Point(190, 132),
            Size = new Size(78, 32),
            Text = "Tiếp tục"
        };
        continueButton.Click += ContinueButton_Click;

        var cancelButton = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(278, 132),
            Size = new Size(78, 32),
            Text = "Thoát"
        };

        Controls.AddRange([prompt, _nameTextBox, _validationLabel, continueButton, cancelButton]);
        AcceptButton = continueButton;
        CancelButton = cancelButton;
        Shown += (_, _) => _nameTextBox.Focus();
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
