namespace DragWin;

public sealed class RaceSettingsForm : Form
{
    private const int PhysicalLaneCount = 4;
    private readonly ComboBox modeSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox laneCountSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox treeSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox stagingModeSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox treeStartSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
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
    private readonly CheckBox[] intervalTimerChecks = new CheckBox[PhysicalLaneCount];
    private readonly CheckBox voiceAnnouncementsCheck = new() { Text = "Enabled", AutoSize = true };
    private readonly ComboBox speechBackendSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill
    };
    private readonly ComboBox speechVoiceSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill
    };
    private readonly Button testVoiceButton = new()
    {
        Text = "Test Voice",
        AutoSize = true,
        MinimumSize = new Size(95, 30)
    };
    private string selectedSpeechVoice = "";
    private bool updatingSpeechVoices;
    private bool voiceLoading;
    private int voiceLoadGeneration;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<SpeechBackendMode, Task<IReadOnlyList<string>>> VoiceDiscovery { get; set; } =
        SpeechAnnouncer.GetInstalledVoicesAsync;
    private readonly CheckBox exportJsonCheck = new()
    {
        Text = "Write JSON tournament archive",
        AutoSize = true
    };
    private readonly CheckBox exportCsvCheck = new()
    {
        Text = "Write CSV tournament results",
        AutoSize = true
    };

    public RaceSettingsForm(
        string raceMode,
        int laneCount,
        string treeMode,
        string stagingMode,
        decimal stagedDelaySeconds,
        IReadOnlyList<decimal> dialSeconds,
        decimal trackLengthInches,
        decimal speedTrapLengthInches,
        IReadOnlyCollection<int> intervalTimerLanes,
        bool voiceAnnouncementsEnabled,
        string speechVoiceName,
        SpeechBackendMode speechBackend,
        bool exportTournamentJson,
        bool exportTournamentCsv,
        bool controllerConnected,
        bool manualTreeStart = false)
    {
        Text = "Race and Track Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(540, 490);

        modeSelector.Items.AddRange(["HEADS_UP", "BRACKET"]);
        laneCountSelector.Items.AddRange(["2", "4"]);
        treeSelector.Items.AddRange(["FULL", "PRO"]);
        stagingModeSelector.Items.AddRange(["Both beams blocked", "Pre-stage then stage"]);
        modeSelector.SelectedItem = raceMode;
        laneCountSelector.SelectedItem = laneCount.ToString();
        treeSelector.SelectedItem = treeMode;
        stagingModeSelector.SelectedIndex = stagingMode == "IN_ORDER" ? 1 : 0;
        treeStartSelector.Items.AddRange(["Automatic", "Starter Button"]);
        treeStartSelector.SelectedIndex = manualTreeStart ? 1 : 0;
        stagedDelayInput.Value = Math.Clamp(stagedDelaySeconds, stagedDelayInput.Minimum, stagedDelayInput.Maximum);
        trackLengthInput.Value = Math.Clamp(trackLengthInches, trackLengthInput.Minimum, trackLengthInput.Maximum);
        speedTrapLengthInput.Value = Math.Clamp(speedTrapLengthInches, speedTrapLengthInput.Minimum, speedTrapLengthInput.Maximum);
        exportJsonCheck.Checked = exportTournamentJson;
        exportCsvCheck.Checked = exportTournamentCsv;
        voiceAnnouncementsCheck.Checked = voiceAnnouncementsEnabled;
        LoadSpeechBackends(speechBackend);
        selectedSpeechVoice = speechVoiceName;
        SetSpeechVoices([], speechVoiceName);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateRaceTab(dialSeconds));
        tabs.TabPages.Add(CreateTrackTab(intervalTimerLanes));
        tabs.TabPages.Add(CreateAnnouncementsTab());
        tabs.TabPages.Add(CreateReportsTab());

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
        voiceAnnouncementsCheck.CheckedChanged += async (_, _) => await LoadSpeechVoicesAsync();
        speechBackendSelector.SelectedIndexChanged += async (_, _) => await LoadSpeechVoicesAsync();
        speechVoiceSelector.SelectedIndexChanged += (_, _) =>
        {
            if (!updatingSpeechVoices)
            {
                selectedSpeechVoice = speechVoiceSelector.SelectedIndex <= 0
                    ? "" : speechVoiceSelector.SelectedItem?.ToString() ?? "";
            }
        };
        Shown += async (_, _) => await LoadSpeechVoicesAsync();
        FormClosed += (_, _) => ++voiceLoadGeneration;
        testVoiceButton.Click += (_, _) =>
            SpeechAnnouncer.SpeakAsync(
                "Drag strip announcements are ready.",
                SpeechVoiceName,
                SpeechBackend);
        FormClosing += ValidateBeforeClosing;
        UpdateDialState();
        UpdateVoiceState();
    }

    public string RaceMode => (string)modeSelector.SelectedItem!;
    public int LaneCount => int.Parse((string)laneCountSelector.SelectedItem!);
    public string TreeMode => (string)treeSelector.SelectedItem!;
    public string StagingMode => stagingModeSelector.SelectedIndex == 1 ? "IN_ORDER" : "BOTH_BLOCKED";
    public decimal StagedDelaySeconds => stagedDelayInput.Value;
    public bool ManualTreeStart => treeStartSelector.SelectedIndex == 1;
    public decimal TrackLengthInches => trackLengthInput.Value;
    public decimal SpeedTrapLengthInches => speedTrapLengthInput.Value;
    public IReadOnlyList<decimal> DialSeconds => dialInputs.Select(input => input.Value).ToArray();
    public IReadOnlyList<int> IntervalTimerLanes => intervalTimerChecks
        .Select((check, index) => (check, lane: index + 1))
        .Where(item => item.check.Checked)
        .Select(item => item.lane)
        .ToArray();
    public bool VoiceAnnouncementsEnabled => voiceAnnouncementsCheck.Checked;
    public SpeechBackendMode SpeechBackend =>
        speechBackendSelector.SelectedItem is SpeechBackendOption option
            ? option.Mode
            : SpeechBackendMode.Automatic;
    public string SpeechVoiceName => selectedSpeechVoice;
    public bool ExportTournamentJson => exportJsonCheck.Checked;
    public bool ExportTournamentCsv => exportCsvCheck.Checked;

    private TabPage CreateRaceTab(IReadOnlyList<decimal> dialSeconds)
    {
        var tab = new TabPage("Race");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 10,
            Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddSettingRow(layout, 0, "Race mode", modeSelector);
        AddSettingRow(layout, 1, "Active lanes", laneCountSelector);
        AddSettingRow(layout, 2, "Tree", treeSelector);
        AddSettingRow(layout, 3, "Staging detection", stagingModeSelector);
        AddSettingRow(layout, 4, "Staged delay", stagedDelayInput, "seconds");
        AddSettingRow(layout, 5, "Tree start", treeStartSelector);

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
            AddSettingRow(layout, lane + 6, $"Lane {lane + 1} dial", input, "seconds");
        }
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage CreateTrackTab(IReadOnlyCollection<int> intervalTimerLanes)
    {
        var tab = new TabPage("Track");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 6,
            Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddSettingRow(layout, 0, "Track length", trackLengthInput, "inches");
        AddSettingRow(layout, 1, "Speed trap length", speedTrapLengthInput, "inches");
        for (var lane = 0; lane < PhysicalLaneCount; lane++)
        {
            var check = new CheckBox
            {
                Text = "Installed",
                AutoSize = true,
                Checked = intervalTimerLanes.Contains(lane + 1)
            };
            intervalTimerChecks[lane] = check;
            AddSettingRow(layout, lane + 2, $"Lane {lane + 1} interval timers", check);
        }
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage CreateReportsTab()
    {
        var tab = new TabPage("Reports");
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14)
        };
        layout.Controls.Add(exportJsonCheck);
        layout.Controls.Add(exportCsvCheck);
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage CreateAnnouncementsTab()
    {
        var tab = new TabPage("Announcements");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddSettingRow(layout, 0, "Voice announcements", voiceAnnouncementsCheck);
        AddSettingRow(layout, 1, "Engine", speechBackendSelector);
        AddSettingRow(layout, 2, "Voice", speechVoiceSelector);
        layout.Controls.Add(testVoiceButton, 1, 3);
        tab.Controls.Add(layout);
        return tab;
    }

    private void LoadSpeechBackends(SpeechBackendMode selectedBackend)
    {
        speechBackendSelector.Items.AddRange([
            new SpeechBackendOption(SpeechBackendMode.Automatic, "Automatic"),
            new SpeechBackendOption(SpeechBackendMode.WindowsSapi, "Windows SAPI"),
            new SpeechBackendOption(SpeechBackendMode.Piper, "Piper"),
            new SpeechBackendOption(SpeechBackendMode.LinuxHelper, "eSpeak NG helper"),
            new SpeechBackendOption(SpeechBackendMode.None, "None")
        ]);
        speechBackendSelector.SelectedItem = speechBackendSelector.Items
            .Cast<SpeechBackendOption>()
            .First(option => option.Mode == selectedBackend);
    }

    private async Task LoadSpeechVoicesAsync()
    {
        var generation = ++voiceLoadGeneration;
        var mode = SpeechBackend;
        voiceLoading = voiceAnnouncementsCheck.Checked && mode != SpeechBackendMode.None;
        UpdateVoiceState();
        if (!voiceLoading) return;

        var requestedVoice = selectedSpeechVoice;
        IReadOnlyList<string> voices;
        try
        {
            voices = await VoiceDiscovery(mode);
        }
        catch
        {
            voices = [];
        }
        if (IsDisposed || Disposing || generation != voiceLoadGeneration) return;
        voiceLoading = false;
        SetSpeechVoices(voices, requestedVoice);
        UpdateVoiceState();
    }

    private void SetSpeechVoices(IReadOnlyList<string> installedVoices, string selectedVoice)
    {
        updatingSpeechVoices = true;
        speechVoiceSelector.Items.Clear();
        speechVoiceSelector.Items.Add("Default voice");
        foreach (var voiceName in installedVoices)
        {
            speechVoiceSelector.Items.Add(voiceName);
        }
        if (!string.IsNullOrEmpty(selectedVoice) && !installedVoices.Contains(selectedVoice))
            speechVoiceSelector.Items.Add(selectedVoice);
        speechVoiceSelector.SelectedItem = string.IsNullOrEmpty(selectedVoice)
            ? "Default voice" : installedVoices.FirstOrDefault(voiceName =>
                string.Equals(voiceName, selectedVoice, StringComparison.OrdinalIgnoreCase)) ?? selectedVoice;
        updatingSpeechVoices = false;
    }

    private void UpdateVoiceState()
    {
        speechBackendSelector.Enabled = voiceAnnouncementsCheck.Checked;
        var speechEnabled = voiceAnnouncementsCheck.Checked &&
            SpeechBackend != SpeechBackendMode.None;
        speechVoiceSelector.Enabled = speechEnabled && !voiceLoading;
        testVoiceButton.Enabled = speechEnabled && !voiceLoading;
    }

    private sealed record SpeechBackendOption(SpeechBackendMode Mode, string DisplayName)
    {
        public override string ToString() => DisplayName;
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
