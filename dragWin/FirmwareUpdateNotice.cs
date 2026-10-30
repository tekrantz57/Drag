namespace DragWin;

public sealed class FirmwareUpdateNotice : UserControl
{
    private readonly Label message = new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 3, 12, 3),
        Font = new Font(SystemFonts.DefaultFont.FontFamily, 10, FontStyle.Bold)
    };
    private readonly Button update = new()
    {
        Text = "Update firmware...",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(145, 32),
        BackColor = Color.FromArgb(35, 91, 145),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Anchor = AnchorStyles.Right
    };
    private readonly ToolTip tip = new();
    public event EventHandler? UpdateRequested;

    public FirmwareUpdateNotice()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = Color.FromArgb(255, 242, 204);
        ForeColor = Color.FromArgb(82, 57, 0);
        var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16, 6, 16, 6) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(message, 0, 0);
        layout.Controls.Add(update, 1, 0);
        Controls.Add(layout);
        layout.SizeChanged += (_, _) => message.MaximumSize = new Size(
            Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal - update.Width - update.Margin.Horizontal - message.Margin.Horizontal), 0);
        update.Click += (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        Visible = false;
    }

    public void SetAvailability(AvailableFirmwareUpdate? available, bool canUpdate, string reason)
    {
        message.Text = available is null ? "" :
            $"Controller update available: {available.BoardName} {available.InstalledVersion} -> {available.AvailableVersion}";
        update.Enabled = available is not null && canUpdate;
        tip.SetToolTip(update, canUpdate ? "Install the bundled controller firmware" : reason);
        tip.SetToolTip(message, canUpdate ? "Newer firmware is bundled with this copy of DragWin" : reason);
        Visible = available is not null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}
