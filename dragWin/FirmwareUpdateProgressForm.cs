namespace DragWin;

public sealed class FirmwareUpdateProgressForm : Form
{
    private readonly Label statusLabel = new();
    private readonly TextBox outputTextBox = new();
    private readonly ProgressBar progressBar = new();
    private bool operationComplete;

    public FirmwareUpdateProgressForm()
    {
        Text = "Controller Firmware Update";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 430);
        MinimumSize = new Size(640, 360);
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold);
        statusLabel.Text = "Preparing controller firmware update...";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        progressBar.Dock = DockStyle.Fill;
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.MarqueeAnimationSpeed = 25;
        outputTextBox.Dock = DockStyle.Fill;
        outputTextBox.Multiline = true;
        outputTextBox.ReadOnly = true;
        outputTextBox.ScrollBars = ScrollBars.Vertical;
        outputTextBox.Font = new Font(FontFamily.GenericMonospace, 9);
        outputTextBox.BackColor = SystemColors.Window;

        layout.Controls.Add(statusLabel, 0, 0);
        layout.Controls.Add(progressBar, 0, 1);
        layout.Controls.Add(outputTextBox, 0, 2);
        Controls.Add(layout);
        FormClosing += (_, args) =>
        {
            if (!operationComplete)
            {
                args.Cancel = true;
            }
        };
    }

    public IProgress<string> CreateProgress() => new Progress<string>(AppendOutput);

    public void SetStatus(string status) => statusLabel.Text = status;

    public void Complete()
    {
        operationComplete = true;
        progressBar.Style = ProgressBarStyle.Blocks;
        progressBar.Value = 100;
    }

    private void AppendOutput(string line)
    {
        outputTextBox.AppendText(line + Environment.NewLine);
        outputTextBox.SelectionStart = outputTextBox.TextLength;
        outputTextBox.ScrollToCaret();
    }
}
