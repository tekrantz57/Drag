using System.Globalization;

namespace DragWin;

public sealed class TournamentRunnerForm : Form
{
    private enum RunnerPhase
    {
        ChoosingLanes,
        ReadyToStage,
        WaitingForStage,
        Staged,
        Racing,
        ResultsReady,
        Confirmed
    }

    private readonly Tournament tournament;
    private readonly RaceRepository repository;
    private readonly DragSerialClient client;
    private readonly int stagedDelayMilliseconds;
    private readonly string stagingMode;
    private readonly TournamentPlanner planner = new();
    private readonly Label heading = new()
    {
        AutoSize = true,
        Font = new Font(SystemFonts.DefaultFont.FontFamily, 14, FontStyle.Bold)
    };
    private readonly Label progressLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Label phaseBanner = new()
    {
        Dock = DockStyle.Fill,
        Height = 42,
        Padding = new Padding(12, 10, 12, 8),
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
    };
    private readonly Label connectionLabel = new()
    {
        AutoSize = true,
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
    };
    private readonly Label resultsSummary = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        Padding = new Padding(0, 6, 0, 3),
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        Visible = false
    };
    private readonly DataGridView lanesGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly ListBox timeline = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly TextBox eventLog = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
    };
    private readonly Button sendHeatButton = new()
    {
        Text = "Arm Heat",
        AutoSize = true,
        BackColor = Color.FromArgb(35, 91, 145),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Padding = new Padding(12, 4, 12, 4)
    };
    private readonly Button confirmLaneChoiceButton = new()
    {
        Text = "Confirm Lane Choice",
        AutoSize = true,
        Visible = false
    };
    private readonly Button confirmButton = new()
    {
        Text = "Confirm Results",
        AutoSize = true,
        Enabled = false,
        BackColor = Color.FromArgb(39, 122, 79),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Padding = new Padding(12, 4, 12, 4)
    };
    private readonly Button rerunButton = new() { Text = "Re-run Heat", AutoSize = true, Visible = false };
    private readonly Button historyButton = new() { Text = "Race History", AutoSize = true };
    private readonly SplitContainer detailsSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        SplitterDistance = 125,
        Panel2Collapsed = true
    };
    private readonly System.Windows.Forms.Timer connectionTimer = new() { Interval = 1000 };
    private RoundPlan round = null!;
    private HeatPlan heat = null!;
    private int heatIndex;
    private readonly Dictionary<int, LiveLaneResult> liveResults = [];
    private readonly HashSet<long> resultAdvancerIds = [];
    private LaneChoiceSession? laneChoiceSession;
    private bool confirmingHeat;
    private bool completionReportShown;
    private bool runnerActionRunning;
    private DateTimeOffset lastRunnerButtonActionAt;
    private RunnerPhase phase;

    public TournamentRunnerForm(
        Tournament tournament,
        RaceRepository repository,
        DragSerialClient client,
        int stagedDelayMilliseconds,
        string stagingMode)
    {
        this.tournament = tournament;
        this.repository = repository;
        this.client = client;
        this.stagedDelayMilliseconds = Math.Clamp(stagedDelayMilliseconds, 0, 5000);
        this.stagingMode = stagingMode == "IN_ORDER" ? "IN_ORDER" : "BOTH_BLOCKED";
        Text = $"Run Tournament - {tournament.Name}";
        MinimumSize = new Size(980, 680);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterParent;

        confirmLaneChoiceButton.BackColor = Color.FromArgb(35, 91, 145);
        confirmLaneChoiceButton.ForeColor = Color.White;
        confirmLaneChoiceButton.FlatStyle = FlatStyle.Flat;
        confirmLaneChoiceButton.Padding = new Padding(12, 4, 12, 4);
        UiStyles.ConfigurePrimaryButton(confirmLaneChoiceButton, UiStyles.BlueAction);
        UiStyles.ConfigurePrimaryButton(sendHeatButton, UiStyles.BlueAction);
        UiStyles.ConfigurePrimaryButton(confirmButton, UiStyles.GreenAction);

        lanesGrid.BackgroundColor = SystemColors.Window;
        lanesGrid.BorderStyle = BorderStyle.Fixed3D;
        lanesGrid.RowHeadersVisible = false;
        lanesGrid.AllowUserToResizeRows = false;
        lanesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Choice",
            HeaderText = "Choice",
            ReadOnly = true,
            FillWeight = 45
        });
        var laneColumn = new DataGridViewComboBoxColumn
        {
            Name = "Lane",
            HeaderText = "Lane",
            ValueType = typeof(int),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FillWeight = 38
        };
        foreach (var lane in tournament.LaneCount == 2
                     ? new[] { 1, 4 }
                     : new[] { 1, 2, 3, 4 })
        {
            laneColumn.Items.Add(lane);
        }
        lanesGrid.Columns.Add(laneColumn);
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Entrant",
            HeaderText = "Racer / Car",
            ReadOnly = true,
            FillWeight = 125
        });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Dial",
            HeaderText = "Dial",
            ToolTipText = "Per-run dial-in override in seconds. This does not change the car default.",
            FillWeight = 45
        });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reaction", HeaderText = "RT", ReadOnly = true, FillWeight = 45 });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Elapsed", HeaderText = "ET", ReadOnly = true, FillWeight = 45 });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Speed", HeaderText = "MPH", ReadOnly = true, FillWeight = 45 });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Result", HeaderText = "Outcome", ReadOnly = true, FillWeight = 85 });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", ReadOnly = true, FillWeight = 100 });

        sendHeatButton.Click += (_, _) => RunRunnerButtonAction(sendHeatButton, SendHeat);
        confirmLaneChoiceButton.Click += (_, _) => RunRunnerButtonAction(confirmLaneChoiceButton, ConfirmLaneChoice);
        confirmButton.Click += (_, _) => ConfirmHeat();
        rerunButton.Click += (_, _) => PrepareRerun();
        historyButton.Click += (_, _) => ShowHistory();
        lanesGrid.DataError += LanesGridOnDataError;
        client.MessageReceived += ClientOnMessageReceived;
        connectionTimer.Tick += (_, _) => UpdateConnectionStatus();
        connectionTimer.Start();
        FormClosed += (_, _) =>
        {
            connectionTimer.Stop();
            client.MessageReceived -= ClientOnMessageReceived;
        };

        var menu = new MenuStrip();
        var testMenu = new ToolStripMenuItem("Test");
        var demoMenuItem = new ToolStripMenuItem("Generate Demo Heat Results");
        demoMenuItem.Click += (_, _) => RunRunnerButtonAction(sendHeatButton, DemoHeat);
        testMenu.DropDownItems.Add(demoMenuItem);
        menu.Items.Add(testMenu);

        var showRawCheck = new CheckBox { Text = "Show raw protocol", AutoSize = true, Dock = DockStyle.Right };
        showRawCheck.CheckedChanged += (_, _) => detailsSplit.Panel2Collapsed = !showRawCheck.Checked;
        var timelineHeader = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        timelineHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        timelineHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        timelineHeader.Controls.Add(new Label
        {
            Text = "Race Timeline",
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        }, 0, 0);
        timelineHeader.Controls.Add(showRawCheck, 1, 0);
        var timelinePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        timelinePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        timelinePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        timelinePanel.Controls.Add(timelineHeader, 0, 0);
        timelinePanel.Controls.Add(timeline, 0, 1);
        detailsSplit.Panel1.Controls.Add(timelinePanel);
        detailsSplit.Panel2.Controls.Add(eventLog);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var secondaryButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        secondaryButtons.Controls.AddRange([historyButton, rerunButton]);
        var primaryButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft
        };
        primaryButtons.Controls.AddRange([confirmButton, sendHeatButton, confirmLaneChoiceButton]);
        buttons.Controls.Add(secondaryButtons, 0, 0);
        buttons.Controls.Add(primaryButtons, 1, 0);

        var titleRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titleStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        titleStack.Controls.Add(heading);
        titleStack.Controls.Add(progressLabel);
        titleRow.Controls.Add(titleStack, 0, 0);
        titleRow.Controls.Add(connectionLabel, 1, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };
        split.Panel1.Controls.Add(lanesGrid);
        split.Panel2.Controls.Add(detailsSplit);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(titleRow, 0, 0);
        layout.Controls.Add(phaseBanner, 0, 1);
        layout.Controls.Add(resultsSummary, 0, 2);
        layout.Controls.Add(split, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(menu, 0, 0);
        shell.Controls.Add(layout, 0, 1);
        Controls.Add(shell);
        Shown += (_, _) => UiStyles.SetSplitterDistanceWhenSized(split, 330, 230, 150);
        MainMenuStrip = menu;
        UpdateConnectionStatus();
        LoadCurrentRound();
    }

    private void LoadCurrentRound()
    {
        round = repository.GetLatestRound(tournament.Id);
        var confirmed = repository.GetConfirmedHeatNumbers(tournament.Id, round.RoundNumber);
        heatIndex = Array.FindIndex(round.Heats.ToArray(), item => !confirmed.Contains(item.HeatNumber));
        if (heatIndex < 0) heatIndex = 0;
        LoadHeat();
    }

    private void LoadHeat()
    {
        heat = NormalizeFinalHeat(round.Heats[heatIndex]);
        liveResults.Clear();
        resultAdvancerIds.Clear();
        confirmButton.Enabled = false;
        confirmButton.Visible = false;
        rerunButton.Visible = false;
        resultsSummary.Visible = false;
        timeline.Items.Clear();
        lanesGrid.Rows.Clear();
        heading.Text = tournament.Name;
        progressLabel.Text =
            $"Round {round.RoundNumber}  |  Heat {heat.HeatNumber} of {round.Heats.Count}  |  " +
            $"{heat.AdvanceCount} advance";
        foreach (var entry in heat.Entries.OrderBy(entry => entry.LaneChoiceOrder))
        {
            var row = lanesGrid.Rows[lanesGrid.Rows.Add(
                entry.LaneChoiceOrder,
                entry.LaneNumber,
                $"{entry.Car.RacerName} / {entry.Car.Name}",
                (entry.DialMilliseconds / 1000M).ToString("0.000", CultureInfo.CurrentCulture),
                "",
                "",
                "",
                "",
                entry.IsBye ? "BYE PASS — guaranteed advance" : "Pending")];
            row.Tag = entry;
            liveResults[entry.LaneNumber] = new LiveLaneResult();
        }
        InitializeLaneChoices();
        AddTimeline($"Round {round.RoundNumber}, heat {heat.HeatNumber} loaded with {heat.Entries.Count} entrants.");
        UpdateHeatActionButtons();
    }

    private HeatPlan NormalizeFinalHeat(HeatPlan candidate)
    {
        var normalAdvanceCount = tournament.LaneCount / 2;
        if (round.Heats.Count == 1 &&
            candidate.Entries.Count <= normalAdvanceCount &&
            candidate.AdvanceCount != 1)
        {
            Append(
                $"Corrected final heat advance count from {candidate.AdvanceCount} to 1.");
            return candidate with { AdvanceCount = 1 };
        }

        return candidate;
    }

    private void InitializeLaneChoices()
    {
        if (round.RoundNumber <= 1)
        {
            laneChoiceSession = null;
            confirmLaneChoiceButton.Visible = false;
            foreach (DataGridViewRow row in lanesGrid.Rows)
            {
                row.Cells["Lane"].ReadOnly = true;
                row.Cells["Status"].Value =
                    ((RoundEntry)row.Tag!).IsBye
                        ? "BYE PASS — random lane"
                        : "Round-one random lane";
            }
            SetPhase(RunnerPhase.ReadyToStage);
            UpdateHeatActionButtons();
            return;
        }

        laneChoiceSession = new LaneChoiceSession(
            heat.Entries,
            tournament.LaneCount == 2 ? [1, 4] : [1, 2, 3, 4]);
        confirmLaneChoiceButton.Visible = true;
        SetPhase(RunnerPhase.ChoosingLanes);
        RefreshLaneChoiceGrid();
    }

    private void RefreshLaneChoiceGrid()
    {
        if (laneChoiceSession is null) return;

        foreach (DataGridViewRow row in lanesGrid.Rows)
        {
            var entry = (RoundEntry)row.Tag!;
            var cell = (DataGridViewComboBoxCell)row.Cells["Lane"];
            var lane = laneChoiceSession.GetLane(entry.Car.Id);
            cell.Value = null;
            cell.Items.Clear();

            if (laneChoiceSession.HasChosen(entry.Car.Id))
            {
                cell.Items.Add(lane);
                cell.ReadOnly = true;
                row.Cells["Status"].Value = "Lane choice locked";
                row.DefaultCellStyle.BackColor = Color.FromArgb(235, 245, 238);
            }
            else
            {
                foreach (var availableLane in laneChoiceSession.AvailableLanes)
                {
                    cell.Items.Add(availableLane);
                }
                cell.ReadOnly = laneChoiceSession.CurrentCarId != entry.Car.Id;
                row.Cells["Status"].Value =
                    cell.ReadOnly ? "Waiting for earlier chooser" : "Choose lane now";
                row.DefaultCellStyle.BackColor = cell.ReadOnly
                    ? SystemColors.Window
                    : Color.FromArgb(255, 244, 214);
            }
            cell.Value = lane;
        }

        confirmLaneChoiceButton.Enabled = !laneChoiceSession.IsComplete;
        confirmLaneChoiceButton.Text = laneChoiceSession.IsComplete
            ? "Lane Choices Complete"
            : "Confirm Current Lane Choice";
        if (laneChoiceSession.IsComplete)
        {
            SetPhase(RunnerPhase.ReadyToStage);
        }
        UpdateHeatActionButtons();
    }

    private void UpdateHeatActionButtons()
    {
        var laneChoicesComplete = laneChoiceSession is null || laneChoiceSession.IsComplete;
        var canStartHeat = laneChoicesComplete && !confirmingHeat && !runnerActionRunning &&
            phase == RunnerPhase.ReadyToStage;
        sendHeatButton.Enabled = canStartHeat;
        sendHeatButton.Visible = phase is RunnerPhase.ReadyToStage or RunnerPhase.WaitingForStage or RunnerPhase.Staged or RunnerPhase.Racing;
        confirmLaneChoiceButton.Visible = phase == RunnerPhase.ChoosingLanes;
        confirmButton.Visible = phase == RunnerPhase.ResultsReady;
        rerunButton.Visible = phase == RunnerPhase.ResultsReady;
    }

    private void SetPhase(RunnerPhase nextPhase)
    {
        phase = nextPhase;
        (phaseBanner.Text, phaseBanner.BackColor, phaseBanner.ForeColor) = nextPhase switch
        {
            RunnerPhase.ChoosingLanes => ("CHOOSE LANES  |  The highlighted entrant chooses now", Color.FromArgb(255, 236, 179), Color.FromArgb(97, 66, 0)),
            RunnerPhase.ReadyToStage => ("READY  |  Verify lanes and dial-ins, then arm the heat", Color.FromArgb(218, 235, 250), Color.FromArgb(24, 71, 112)),
            RunnerPhase.WaitingForStage => ("STAGING  |  Waiting for all active lanes", Color.FromArgb(255, 236, 179), Color.FromArgb(97, 66, 0)),
            RunnerPhase.Staged => ("ALL LANES STAGED  |  Tree sequence pending", Color.FromArgb(218, 235, 250), Color.FromArgb(24, 71, 112)),
            RunnerPhase.Racing => ("RACE ACTIVE", Color.FromArgb(218, 235, 250), Color.FromArgb(24, 71, 112)),
            RunnerPhase.ResultsReady => ("RESULTS READY  |  Review advancement before confirming", Color.FromArgb(218, 242, 225), Color.FromArgb(22, 92, 55)),
            RunnerPhase.Confirmed => ("RESULTS CONFIRMED", Color.FromArgb(218, 242, 225), Color.FromArgb(22, 92, 55)),
            _ => ("CONTROLLER ERROR  |  Review the timeline", Color.FromArgb(252, 222, 222), Color.FromArgb(139, 32, 32))
        };

        var showRaceData = nextPhase is RunnerPhase.Racing or RunnerPhase.ResultsReady or RunnerPhase.Confirmed;
        lanesGrid.Columns["Choice"]!.Visible = nextPhase == RunnerPhase.ChoosingLanes;
        lanesGrid.Columns["Reaction"]!.Visible = showRaceData;
        lanesGrid.Columns["Elapsed"]!.Visible = showRaceData;
        lanesGrid.Columns["Speed"]!.Visible = showRaceData;
        lanesGrid.Columns["Result"]!.Visible = showRaceData;
        lanesGrid.Columns["Dial"]!.ReadOnly = nextPhase != RunnerPhase.ReadyToStage;
        UpdateHeatActionButtons();
    }

    private void UpdateConnectionStatus()
    {
        if (!client.IsConnected)
        {
            connectionLabel.Text = "Controller disconnected";
            connectionLabel.ForeColor = Color.FromArgb(158, 45, 45);
            return;
        }

        if (client.LastHeartbeatReceivedAt is not { } heartbeatAt)
        {
            connectionLabel.Text = "Controller connected - waiting for heartbeat";
            connectionLabel.ForeColor = Color.FromArgb(145, 91, 0);
            return;
        }

        var age = DateTimeOffset.Now - heartbeatAt;
        if (age.TotalSeconds > 3)
        {
            connectionLabel.Text = $"Controller stale ({age.TotalSeconds:0}s)";
            connectionLabel.ForeColor = Color.FromArgb(158, 45, 45);
            return;
        }

        connectionLabel.Text = "Controller ready";
        connectionLabel.ForeColor = Color.FromArgb(39, 122, 79);
    }

    private bool ConfirmLaneChoice()
    {
        if (laneChoiceSession?.CurrentCarId is not long carId) return false;
        lanesGrid.EndEdit();
        var row = lanesGrid.Rows.Cast<DataGridViewRow>()
            .Single(item => ((RoundEntry)item.Tag!).Car.Id == carId);
        if (!int.TryParse(row.Cells["Lane"].Value?.ToString(), out var selectedLane))
        {
            MessageBox.Show(this, "Select an available lane.", Text);
            return false;
        }

        var originalLane = laneChoiceSession.GetLane(carId);
        var displaced = laneChoiceSession.Assignments
            .Where(item => item.Key != carId && item.Value == selectedLane)
            .Select(item => (long?)item.Key)
            .SingleOrDefault();
        try
        {
            laneChoiceSession.Choose(carId, selectedLane);
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(this, exception.Message, Text);
            return false;
        }

        var chooser = ((RoundEntry)row.Tag!).Car.DisplayName;
        AddTimeline(displaced.HasValue
            ? $"{chooser} chose lane {selectedLane}; displaced car moved to lane {originalLane}."
            : $"{chooser} chose lane {selectedLane}.");
        RefreshLaneChoiceGrid();
        return false;
    }

    private void RunRunnerButtonAction(Button button, Func<bool> action)
    {
        var now = DateTimeOffset.Now;
        if (now - lastRunnerButtonActionAt < TimeSpan.FromMilliseconds(700))
        {
            return;
        }
        if (runnerActionRunning || confirmingHeat)
        {
            return;
        }

        runnerActionRunning = true;
        lastRunnerButtonActionAt = now;
        var wasEnabled = button.Enabled;
        button.Enabled = false;
        var keepDisabled = false;
        try
        {
            keepDisabled = action();
        }
        finally
        {
            runnerActionRunning = false;
            if (!IsDisposed && wasEnabled && !keepDisabled &&
                (button != confirmLaneChoiceButton || laneChoiceSession is not { IsComplete: true }))
            {
                button.Enabled = true;
            }
            if (!IsDisposed && (!keepDisabled || button == confirmLaneChoiceButton))
            {
                UpdateHeatActionButtons();
            }
        }
    }

    private bool SendHeat()
    {
        if (!client.IsConnected)
        {
            MessageBox.Show(this, "Connect to the controller first.", Text);
            return false;
        }
        if (laneChoiceSession is { IsComplete: false })
        {
            MessageBox.Show(this, "Complete the ordered lane choices first.", Text);
            return false;
        }

        var assignments = ReadAssignments();
        if (assignments is null || !ApplyHeatGridInputs(assignments)) return false;

        var commands = new List<string[]>
        {
            new[] { "SET", "LANES", tournament.LaneCount.ToString() },
            new[] { "SET", "MODE", "BRACKET" },
            new[] { "SET", "TREE", "FULL" },
            new[] { "SET", "STAGING_MODE", stagingMode },
            new[] { "SET", "STAGED_DELAY", stagedDelayMilliseconds.ToString(CultureInfo.InvariantCulture) },
            new[] { "SET", "HEAT_LANES", string.Join(',', heat.Entries.Select(entry => entry.LaneNumber).Order()) }
        };
        foreach (var entry in heat.Entries)
        {
            commands.Add([
                "SET", "DIAL", entry.LaneNumber.ToString(),
                entry.DialMilliseconds.ToString()]);
        }
        commands.Add(["RESET"]);
        client.SendBatch(commands);
        AddTimeline("Heat armed. Stage only the displayed lanes.");
        SetPhase(RunnerPhase.WaitingForStage);
        sendHeatButton.Enabled = false;
        UpdateHeatActionButtons();
        return true;
    }

    private bool DemoHeat()
    {
        if (phase != RunnerPhase.ReadyToStage)
        {
            MessageBox.Show(this, "The demo can only be generated while a heat is ready to stage.", Text);
            return false;
        }
        if (laneChoiceSession is { IsComplete: false })
        {
            MessageBox.Show(this, "Complete the ordered lane choices first.", Text);
            return false;
        }

        var assignments = ReadAssignments();
        if (assignments is null || !ApplyHeatGridInputs(assignments)) return false;

        AddTimeline("TEST: Simulated heat results generated.");
        sendHeatButton.Enabled = false;
        SetPhase(RunnerPhase.Racing);
        foreach (var message in DemoHeatSimulator.CreateBracketHeatMessages(heat))
        {
            ProcessMessage(message);
        }
        return true;
    }

    private bool ApplyHeatGridInputs(IReadOnlyDictionary<long, int> assignments)
    {
        var dialOverrides = ReadDialOverrides();
        if (dialOverrides is null) return false;

        repository.UpdateHeatLanes(tournament.Id, round.RoundNumber, heat.HeatNumber, assignments);
        repository.UpdateHeatDialOverrides(tournament.Id, round.RoundNumber, heat.HeatNumber, dialOverrides);
        heat = heat with
        {
            Entries = heat.Entries.Select(entry =>
                entry with
                {
                    LaneNumber = assignments[entry.Car.Id],
                    DialMilliseconds = dialOverrides[entry.Car.Id]
                }).ToArray()
        };
        liveResults.Clear();
        foreach (var entry in heat.Entries) liveResults[entry.LaneNumber] = new LiveLaneResult();
        UpdateStatusCells();
        return true;
    }

    private Dictionary<long, int>? ReadAssignments()
    {
        var assignments = new Dictionary<long, int>();
        var used = new HashSet<int>();
        foreach (DataGridViewRow row in lanesGrid.Rows)
        {
            var entry = (RoundEntry)row.Tag!;
            if (!int.TryParse(row.Cells["Lane"].Value?.ToString(), out var lane) || !used.Add(lane))
            {
                MessageBox.Show(this, "Every car needs a unique available lane.", Text);
                return null;
            }
            assignments[entry.Car.Id] = lane;
        }
        return assignments;
    }

    private Dictionary<long, int>? ReadDialOverrides()
    {
        var dialOverrides = new Dictionary<long, int>();
        foreach (DataGridViewRow row in lanesGrid.Rows)
        {
            var entry = (RoundEntry)row.Tag!;
            var text = row.Cells["Dial"].Value?.ToString();
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var seconds) &&
                !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out seconds))
            {
                MessageBox.Show(this, $"Enter a valid dial-in for {entry.Car.DisplayName}.", Text);
                return null;
            }

            var milliseconds = decimal.ToInt32(seconds * 1000M);
            if (milliseconds is < 100 or > 60000)
            {
                MessageBox.Show(
                    this,
                    $"Dial-in for {entry.Car.DisplayName} must be between 0.100 and 60.000 seconds.",
                    Text);
                return null;
            }

            row.Cells["Dial"].Value =
                (milliseconds / 1000M).ToString("0.000", CultureInfo.CurrentCulture);
            dialOverrides[entry.Car.Id] = milliseconds;
        }
        return dialOverrides;
    }

    private void LanesGridOnDataError(
        object? sender,
        DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        e.Cancel = false;

        if (lanesGrid.Columns["Lane"] is not DataGridViewColumn laneColumn ||
            e.RowIndex < 0 ||
            e.ColumnIndex != laneColumn.Index ||
            lanesGrid.Rows[e.RowIndex].Tag is not RoundEntry entry)
        {
            Append($"Lane editor rejected an invalid value: {e.Exception?.Message}");
            return;
        }

        var rowIndex = e.RowIndex;
        BeginInvoke(() =>
        {
            if (!IsDisposed && rowIndex < lanesGrid.Rows.Count)
            {
                lanesGrid.Rows[rowIndex].Cells["Lane"].Value =
                    laneChoiceSession?.GetLane(entry.Car.Id) ?? entry.LaneNumber;
            }
        });
        Append(
            $"Lane editor rejected an invalid value for {entry.Car.DisplayName}; " +
            $"restored lane {entry.LaneNumber}.");
    }

    private void ClientOnMessageReceived(object? sender, ProtocolMessage message)
    {
        if (IsDisposed) return;
        BeginInvoke(() => ProcessMessage(message));
    }

    private void ProcessMessage(ProtocolMessage message)
    {
        if (message.Type == "HEARTBEAT")
        {
            return;
        }

        Append(message.Encode());
        if (message.Type == "ERROR")
        {
            phaseBanner.Text = "CONTROLLER ERROR  |  Review the timeline before continuing";
            phaseBanner.BackColor = Color.FromArgb(252, 222, 222);
            phaseBanner.ForeColor = Color.FromArgb(139, 32, 32);
            AddTimeline($"Controller error: {string.Join(' ', message.Parts.Skip(1))}");
        }
        if (message.Parts.Count >= 4 && message.Parts[1] == "LANE" &&
            int.TryParse(message.Parts[2], out var lane) &&
            liveResults.TryGetValue(lane, out var result))
        {
            var kind = message.Parts[3];
            if (message.Type == "EVENT" && kind == "FOUL")
            {
                result.Fouled = true;
                AddTimeline($"Lane {lane} red-lighted.");
            }
            if (message.Type == "EVENT" && kind == "REACTION_US" && message.Parts.Count > 4 &&
                long.TryParse(message.Parts[4], out var reaction)) result.ReactionUs = reaction;
            if (message.Type == "RESULT" && kind == "ELAPSED_US" && message.Parts.Count > 4 &&
                long.TryParse(message.Parts[4], out var elapsed))
            {
                result.Finished = true;
                result.ElapsedUs = elapsed;
                AddTimeline($"Lane {lane} finished in {FormatSeconds(elapsed)} seconds.");
            }
            if (message.Type == "RESULT" && kind == "BREAKOUT_US" && message.Parts.Count > 4 &&
                long.TryParse(message.Parts[4], out var breakout)) result.BreakoutUs = breakout;
            if (message.Type == "RESULT" && kind == "VALID") result.Valid = true;
            if (message.Type == "RESULT" && kind == "DNF") result.DidNotFinish = true;
            if (message.Type == "RESULT" && kind == "ELAPSED_UNAVAILABLE") result.ElapsedUnavailable = true;
            if (message.Type == "RESULT" && kind == "SPEED_UNAVAILABLE") result.SpeedUnavailable = true;
            if (message.Type == "RESULT" && kind == "SPEED_INVALID") result.SpeedInvalid = true;
            if (message.Type == "RESULT" && kind == "SPEED_MPH_X100" && message.Parts.Count > 4 &&
                long.TryParse(message.Parts[4], out var speedMphX100)) result.SpeedMphX100 = speedMphX100;
            UpdateStatusCells();
        }
        if (message.Type == "RESULT" && message.Parts.Count >= 4 &&
            message.Parts[1] == "WINNER" &&
            message.Parts[2] == "LANE" &&
            int.TryParse(message.Parts[3], out var winningLane) &&
            liveResults.TryGetValue(winningLane, out var winner))
        {
            winner.Winner = true;
            UpdateStatusCells();
        }
        if (message.Type == "RESULT" && message.Parts.Count >= 5 &&
            message.Parts[1] == "PLACE" &&
            int.TryParse(message.Parts[2], out var place) &&
            message.Parts[3] == "LANE" &&
            int.TryParse(message.Parts[4], out var placedLane) &&
            liveResults.TryGetValue(placedLane, out var placed))
        {
            placed.Place = place;
            placed.FinishOrder = place;
            AddTimeline($"Lane {placedLane} placed #{place}.");
            UpdateStatusCells();
        }
        if (message.Type == "EVENT" && message.Parts.Count >= 3 &&
            message.Parts[1] == "TREE")
        {
            switch (message.Parts[2])
            {
                case "WAITING_FOR_ALL_LANES":
                    SetPhase(RunnerPhase.WaitingForStage);
                    AddTimeline("Controller is waiting for all active lanes to stage.");
                    break;
                case "ALL_LANES_STAGED":
                    SetPhase(RunnerPhase.Staged);
                    AddTimeline($"All lanes staged. Tree starts after {stagedDelayMilliseconds} ms.");
                    break;
                case "BRACKET_START":
                case "HEADS_UP_START":
                    SetPhase(RunnerPhase.Racing);
                    AddTimeline("Tree sequence started.");
                    break;
                case "STAGING_ABORTED":
                    SetPhase(RunnerPhase.WaitingForStage);
                    AddTimeline("Staging was aborted because a car backed out.");
                    break;
                case "RACE_COMPLETE":
                    SetPhase(RunnerPhase.ResultsReady);
                    ShowResultsSummary();
                    AddTimeline("Race complete. Review the results before confirming advancement.");
                    break;
            }
        }
    }

    private void UpdateStatusCells()
    {
        foreach (DataGridViewRow row in lanesGrid.Rows)
        {
            var lane = Convert.ToInt32(row.Cells["Lane"].Value);
            if (!liveResults.TryGetValue(lane, out var result)) continue;
            row.Cells["Reaction"].Value = FormatSeconds(result.ReactionUs);
            row.Cells["Elapsed"].Value = FormatSeconds(result.ElapsedUs);
            row.Cells["Speed"].Value = FormatSpeed(result);
            row.Cells["Result"].Value = FormatResult(result);
            row.Cells["Status"].Value = result.Fouled ? "FOUL" :
                result.BreakoutUs.HasValue ? $"Breakout by {FormatSeconds(result.BreakoutUs.Value)}" :
                result.DidNotFinish ? "DNF" :
                result.Finished && result.FinishOrder > 0 ? $"Placed #{result.FinishOrder}" :
                result.Finished ? "Finished; awaiting placement" :
                result.ReactionUs.HasValue ? $"Reaction {FormatSeconds(result.ReactionUs.Value)}" :
                ((RoundEntry)row.Tag!).IsBye ? "BYE PASS" : "Running";

            if (phase == RunnerPhase.ResultsReady)
            {
                var entry = (RoundEntry)row.Tag!;
                row.DefaultCellStyle.BackColor = resultAdvancerIds.Contains(entry.Car.Id)
                    ? Color.FromArgb(218, 242, 225)
                    : result.Fouled || result.DidNotFinish
                        ? Color.FromArgb(252, 230, 230)
                        : SystemColors.Window;
            }
        }
    }

    private static string FormatSeconds(long? microseconds) =>
        microseconds.HasValue
            ? (microseconds.Value / 1_000_000.0).ToString("0.000", CultureInfo.CurrentCulture)
            : "";

    private static string FormatSpeed(LiveLaneResult result)
    {
        if (result.SpeedMphX100.HasValue)
        {
            return (result.SpeedMphX100.Value / 100.0).ToString("0.00", CultureInfo.CurrentCulture);
        }
        if (result.SpeedInvalid)
        {
            return "Invalid";
        }
        return result.SpeedUnavailable ? "N/A" : "";
    }

    private static string FormatResult(LiveLaneResult result)
    {
        if (result.Fouled)
        {
            if (result.Winner)
            {
                return "Winner (least red light)";
            }
            return result.Place.HasValue
                ? $"Place {result.Place} (red light)"
                : "Red light";
        }
        if (result.Winner)
        {
            if (result.BreakoutUs.HasValue)
            {
                return "Winner (breakout)";
            }
            if (result.Valid)
            {
                return "Winner (legal)";
            }
            return "Winner";
        }
        if (result.Place.HasValue)
        {
            return $"Place {result.Place}";
        }
        if (result.BreakoutUs.HasValue)
        {
            return "Breakout";
        }
        if (result.Valid)
        {
            return "Legal";
        }
        if (result.DidNotFinish)
        {
            return "DNF";
        }
        if (result.ElapsedUnavailable)
        {
            return "No ET";
        }
        return "";
    }

    private RunResult[] BuildRunResults() => heat.Entries.Select(entry =>
    {
        var live = liveResults.GetValueOrDefault(entry.LaneNumber) ?? new LiveLaneResult();
        var legality = live.Fouled ? RunLegality.RedLight :
            live.BreakoutUs.HasValue ? RunLegality.Breakout :
            live.Finished ? RunLegality.Legal : RunLegality.DidNotFinish;
        return new RunResult(
            entry.Car.Id,
            legality,
            live.FinishOrder == 0 ? int.MaxValue : live.FinishOrder,
            live.ReactionUs,
            live.BreakoutUs,
            entry.IsBye);
    }).ToArray();

    private void ShowResultsSummary()
    {
        resultAdvancerIds.Clear();
        resultAdvancerIds.UnionWith(planner.SelectAdvancers(heat, BuildRunResults())
            .Select(result => result.CarId)
            .ToHashSet());
        var advancing = heat.Entries
            .Where(entry => resultAdvancerIds.Contains(entry.Car.Id))
            .Select(entry => entry.Car.DisplayName)
            .ToArray();
        var eliminated = heat.Entries
            .Where(entry => !resultAdvancerIds.Contains(entry.Car.Id))
            .Select(entry => entry.Car.DisplayName)
            .ToArray();

        resultsSummary.Text = $"Advancing: {string.Join(", ", advancing)}";
        if (eliminated.Length > 0)
        {
            resultsSummary.Text += $"{Environment.NewLine}Eliminated: {string.Join(", ", eliminated)}";
        }
        resultsSummary.Visible = true;
        confirmButton.Text = advancing.Length == 1
            ? "Confirm 1 Advancer"
            : $"Confirm {advancing.Length} Advancers";
        confirmButton.Enabled = true;
        UpdateStatusCells();
    }

    private void PrepareRerun()
    {
        if (MessageBox.Show(
                this,
                "Discard these unconfirmed results and prepare this heat to run again?",
                "Re-run heat",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        liveResults.Clear();
        resultAdvancerIds.Clear();
        foreach (var entry in heat.Entries)
        {
            liveResults[entry.LaneNumber] = new LiveLaneResult();
        }
        foreach (DataGridViewRow row in lanesGrid.Rows)
        {
            foreach (var columnName in new[] { "Reaction", "Elapsed", "Speed", "Result" })
            {
                row.Cells[columnName].Value = "";
            }
            row.Cells["Status"].Value = "Ready to re-run";
            row.DefaultCellStyle.BackColor = SystemColors.Window;
        }
        resultsSummary.Visible = false;
        confirmButton.Enabled = false;
        AddTimeline("Unconfirmed results discarded. Heat is ready to arm again.");
        SetPhase(RunnerPhase.ReadyToStage);
    }

    private void ShowHistory()
    {
        using var form = new TournamentHistoryForm(repository.GetTournamentReport(tournament.Id));
        form.ShowDialog(this);
    }

    private void ConfirmHeat()
    {
        if (confirmingHeat)
        {
            return;
        }

        confirmingHeat = true;
        confirmButton.Enabled = false;
        sendHeatButton.Enabled = false;
        rerunButton.Enabled = false;

        var results = BuildRunResults();
        var advancers = planner.SelectAdvancers(heat, results);
        repository.SaveHeatResults(
            tournament.Id, round.RoundNumber, heat.HeatNumber,
            results, advancers.Select(result => result.CarId).ToHashSet());

        if (!repository.IsRoundConfirmed(tournament.Id, round.RoundNumber))
        {
            heatIndex++;
            SetPhase(RunnerPhase.Confirmed);
            LoadHeat();
            confirmingHeat = false;
            UpdateHeatActionButtons();
            return;
        }

        var (cars, reactions) = repository.GetRoundAdvancers(tournament.Id, round.RoundNumber);
        if (cars.Count <= 1)
        {
            repository.CompleteTournament(tournament.Id);
            ShowCompletedTournamentReport(cars.SingleOrDefault());
            Close();
            return;
        }
        var nextRound = planner.CreateRound(cars, tournament.LaneCount, round.RoundNumber + 1, priorReactionMicroseconds: reactions);
        repository.SaveRound(tournament.Id, nextRound);
        LoadCurrentRound();
        confirmingHeat = false;
        UpdateHeatActionButtons();
    }

    private void AddTimeline(string text)
    {
        timeline.Items.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        timeline.TopIndex = Math.Max(0, timeline.Items.Count - 1);
    }

    private void Append(string text) =>
        eventLog.AppendText($"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");

    private void ShowCompletedTournamentReport(Car? winner)
    {
        if (completionReportShown)
        {
            return;
        }

        completionReportShown = true;
        var summary = winner is null
            ? "Tournament complete with no winner."
            : $"Winner: {winner.DisplayName}";
        try
        {
            var report = repository.GetTournamentReport(tournament.Id);
            var path = TournamentReportWriter.WriteAndOpen(report);
            MessageBox.Show(
                this,
                $"{summary}{Environment.NewLine}{Environment.NewLine}Report opened:{Environment.NewLine}{path}",
                Text);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                $"{summary}{Environment.NewLine}{Environment.NewLine}Could not open the report: {exception.Message}",
                Text);
        }
    }

    private sealed class LiveLaneResult
    {
        public bool Fouled { get; set; }
        public bool Finished { get; set; }
        public int FinishOrder { get; set; }
        public long? ReactionUs { get; set; }
        public long? ElapsedUs { get; set; }
        public long? BreakoutUs { get; set; }
        public long? SpeedMphX100 { get; set; }
        public bool Valid { get; set; }
        public bool DidNotFinish { get; set; }
        public bool ElapsedUnavailable { get; set; }
        public bool SpeedUnavailable { get; set; }
        public bool SpeedInvalid { get; set; }
        public bool Winner { get; set; }
        public int? Place { get; set; }
    }
}
