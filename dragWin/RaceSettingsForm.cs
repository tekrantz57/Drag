namespace DragWin;

public sealed class RaceSettingsForm : Form
{
    private const int PhysicalLaneCount = 4;
    private readonly ComboBox modeSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox laneCountSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox treeSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown stagedDelayInput = new()
    {
        DecimalPlaces = 3,
        Increment = 0.050M,
        Minimum = 0M,
        Maximum = 5.000M,
        Dock = DockStyle.Fill
    };
    private readonly NumericUpDown trackLengthInput = new()
    {
        DecimalPlaces = 3,
        Increment = 1.000M,
        Minimum = 1.000M,
        Maximum = 10000.000M,
        Dock = DockStyle.Fill
    };
    private readonly NumericUpDown speedTrapLengthInput = new()
    {
        DecimalPlaces = 3,
        Increment = 0.100M,
        Minimum = 0.100M,
        Maximum = 9999.999M,
        Dock = DockStyle.Fill
    };
    private readonly NumericUpDown[] dialInputs = new NumericUpDown[PhysicalLaneCount];

    public RaceSettingsForm(
        string raceMode,
        int laneCount,
        string treeMode,
        decimal stagedDelaySeconds,
        IReadOnlyList<decimal> dialSeconds,
        decimal trackLengthInches,
        decimal speedTrapLengthInches,
        bool controllerConnected)
    {
        Text = "Race and Track Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(540, 430);

        modeSelector.Items.AddRange(["HEADS_UP", "BRACKET"]);
        laneCountSelector.Items.AddRange(["2", "4"]);
        treeSelector.Items.AddRange(["FULL", "PRO"]);
        modeSelector.SelectedItem = raceMode;
        laneCountSelector.SelectedItem = laneCount.ToString();
        treeSelector.SelectedItem = treeMode;
        stagedDelayInput.Value = Math.Clamp(stagedDelaySeconds, stagedDelayInput.Minimum, stagedDelayInput.Maximum);
        trackLengthInput.Value = Math.Clamp(trackLengthInches, trackLengthInput.Minimum, trackLengthInput.Maximum);
        speedTrapLengthInput.Value = Math.Clamp(speedTrapLengthInches, speedTrapLengthInput.Minimum, speedTrapLengthInput.Maximum);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateRaceTab(dialSeconds));
        tabs.TabPages.Add(CreateTrackTab());

        var saveButton = new Button
        {
            Text = controllerConnected ? "Save and Apply" : "Save",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(105, 32),
            BackColor = Color.FromArgb(35, 91, 145),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(80, 32)
        };
        UiStyles.ConfigurePrimaryButton(saveButton, UiStyles.BlueAction);
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        footer.Controls.Add(saveButton);
        footer.Controls.Add(cancelButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(tabs, 0, 0);
        layout.Controls.Add(footer, 0, 1);
        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        modeSelector.SelectedIndexChanged += (_, _) => UpdateDialState();
        laneCountSelector.SelectedIndexChanged += (_, _) => UpdateDialState();
        FormClosing += ValidateBeforeClosing;
        UpdateDialState();
    }

    public string RaceMode => (string)modeSelector.SelectedItem!;
    public int LaneCount => int.Parse((string)laneCountSelector.SelectedItem!);
    public string TreeMode => (string)treeSelector.SelectedItem!;
    public decimal StagedDelaySeconds => stagedDelayInput.Value;
    public decimal TrackLengthInches => trackLengthInput.Value;
    public decimal SpeedTrapLengthInches => speedTrapLengthInput.Value;
    public IReadOnlyList<decimal> DialSeconds => dialInputs.Select(input => input.Value).ToArray();

    private TabPage CreateRaceTab(IReadOnlyList<decimal> dialSeconds)
    {
        var tab = new TabPage("Race");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 8,
            Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddSettingRow(layout, 0, "Race mode", modeSelector);
        AddSettingRow(layout, 1, "Active lanes", laneCountSelector);
        AddSettingRow(layout, 2, "Tree", treeSelector);
        AddSettingRow(layout, 3, "Staged delay", stagedDelayInput, "seconds");

        for (var lane = 0; lane < PhysicalLaneCount; lane++)
        {
            var input = new NumericUpDown
            {
                DecimalPlaces = 3,
                Increment = 0.001M,
                Minimum = 0.100M,
                Maximum = 60.000M,
                Value = Math.Clamp(dialSeconds[lane], 0.100M, 60.000M),
                Dock = DockStyle.Fill
            };
            dialInputs[lane] = input;
            AddSettingRow(layout, lane + 4, $"Lane {lane + 1} dial", input, "seconds");
        }
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage CreateTrackTab()
    {
        var tab = new TabPage("Track");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddSettingRow(layout, 0, "Track length", trackLengthInput, "inches");
        AddSettingRow(layout, 1, "Speed trap length", speedTrapLengthInput, "inches");
        tab.Controls.Add(layout);
        return tab;
    }

    private static void AddSettingRow(
        TableLayoutPanel layout,
        int row,
        string label,
        Control control,
        string? units = null)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Margin = new Padding(0, 8, 12, 8)
        }, 0, row);
        control.Margin = new Padding(0, 4, 8, 4);
        layout.Controls.Add(control, 1, row);
        if (units is not null)
        {
            layout.Controls.Add(new Label
            {
                Text = units,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 8)
            }, 2, row);
        }
    }

    private void UpdateDialState()
    {
        var bracket = string.Equals(modeSelector.SelectedItem as string, "BRACKET", StringComparison.Ordinal);
        var laneCount = int.TryParse(laneCountSelector.SelectedItem as string, out var count) ? count : 4;
        for (var lane = 0; lane < dialInputs.Length; lane++)
        {
            dialInputs[lane].Enabled = bracket && (laneCount == 4 || lane is 0 or 3);
        }
    }

    private void ValidateBeforeClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK || speedTrapLengthInput.Value < trackLengthInput.Value)
        {
            return;
        }
        MessageBox.Show(
            this,
            "Speed-trap length must be shorter than the track length.",
            "Invalid distances",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        e.Cancel = true;
    }
}
