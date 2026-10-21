namespace DragWin;

public static class FirmwareUpdatePolicy
{
    public static string? IdentifyBoard(ControllerIdentity? identity) => identity switch
    {
        null => null,
        { Product: "DRAG_MC", Mcu: "MEGA2560" } => ControllerFirmwarePackage.MegaBoardProfile,
        { Product: "DRAG_MC_DUE" or "DRAG_MC_DUE_LIGHT_TEST", Mcu: "SAM3X8E" } => ControllerFirmwarePackage.DueBoardProfile,
        _ => throw new InvalidOperationException(
            $"Unsupported controller identity: {identity.Product} on {identity.Mcu}. No firmware will be written.")
    };

    public static ControllerFirmwarePackage SelectPackage(IEnumerable<ControllerFirmwarePackage> packages, string boardProfile)
    {
        var matches = packages.Where(package => package.Manifest.BoardProfile == boardProfile).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException("No bundled firmware package matches the selected board."),
            _ => throw new InvalidDataException("Multiple bundled firmware packages match the selected board.")
        };
    }

    public static void CheckTarget(ControllerFirmwarePackage package, ControllerIdentity? identity)
    {
        if (identity is not null && IdentifyBoard(identity) != package.Manifest.BoardProfile)
            throw new InvalidOperationException("Controller identity does not match the selected firmware package. No firmware will be written.");
    }

    public static bool VerifyIdentity(ControllerFirmwarePackage package, ControllerIdentity? identity) =>
        identity is not null && identity.Product == package.Manifest.Product &&
        identity.Mcu == (package.IsDue ? "SAM3X8E" : "MEGA2560") &&
        identity.FirmwareVersion == package.Manifest.FirmwareVersion;
}

internal sealed class FirmwareBoardSelectionForm : Form
{
    public string BoardProfile { get; private set; } = "";

    public FirmwareBoardSelectionForm()
    {
        Text = "Confirm Controller Board";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(520, 210);
        var mega = new RadioButton { Text = "Mega 2560 or compatible clone", AutoSize = true };
        var due = new RadioButton { Text = "Arduino Due - Programming USB port", AutoSize = true };
        var confirm = new Button { Text = "Continue", AutoSize = true, Enabled = false };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        mega.CheckedChanged += (_, _) => confirm.Enabled = mega.Checked || due.Checked;
        due.CheckedChanged += (_, _) => confirm.Enabled = mega.Checked || due.Checked;
        confirm.Click += (_, _) =>
        {
            BoardProfile = due.Checked ? ControllerFirmwarePackage.DueBoardProfile : ControllerFirmwarePackage.MegaBoardProfile;
            DialogResult = DialogResult.OK;
        };
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        layout.Controls.Add(new Label
        {
            Text = "No controller identity received. Physically confirm the board on the selected COM port.",
            AutoSize = true,
            MaximumSize = new Size(475, 0)
        });
        layout.Controls.Add(mega);
        layout.Controls.Add(due);
        layout.Controls.Add(new Label
        {
            Text = "Due: use the connector nearest the round power jack, not Native USB.",
            AutoSize = true,
            MaximumSize = new Size(475, 0)
        });
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        buttons.Controls.AddRange([confirm, cancel]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = confirm;
        CancelButton = cancel;
    }
}
