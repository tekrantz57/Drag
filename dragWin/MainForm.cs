using System.Globalization;

namespace DragWin;

public sealed class MainForm : Form
{
    private const int LaneCount = 4;

    private readonly DragSerialClient client = new();
    private readonly RaceRepository raceRepository = new();
    private bool connectionRequested;
    private bool mainActionRunning;
    private DateTimeOffset lastMainButtonActionAt;
    private readonly ToolStripMenuItem configureDistancesMenuItem =
        new("Track distances...");
    private readonly ComboBox portSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button refreshButton = new() { Text = "Refresh" };
    private readonly Button connectButton = new() { Text = "Connect" };
    private readonly Button pingButton = new() { Text = "Ping", Enabled = false };
    private readonly Button statusButton = new() { Text = "Status", Enabled = false };
    private readonly Button resetButton = new() { Text = "Reset", Enabled = false };
    private readonly Button tournamentButton = new()
    {
        Text = "Racers / Tournament",
        AutoSize = true,
        MinimumSize = new Size(150, 0)
    };
    private readonly ComboBox tournamentSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 180,
        DisplayMember = nameof(Tournament.Name)
    };
    private readonly Button runTournamentButton = new()
    {
        Text = "Run / Resume",
        AutoSize = true,
        MinimumSize = new Size(110, 0)
    };
    private readonly Label connectionLabel = new() { AutoSize = true, Text = "Disconnected" };
    private readonly ComboBox modeSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 120,
        Enabled = false
    };
    private readonly ComboBox laneCountSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 50,
        Enabled = false
    };
    private readonly ToolTip toolTip = new();
    private readonly NumericUpDown[] dialInputs = new NumericUpDown[LaneCount];
    private readonly CheckBox[] practiceLaneChecks = new CheckBox[LaneCount];
    private readonly NumericUpDown trackLengthInput = new()
    {
        DecimalPlaces = 3,
        Increment = 1.000M,
        Minimum = 1.000M,
        Maximum = 10000.000M,
        Value = 660.000M,
        Width = 90,
        Enabled = false
    };
    private readonly NumericUpDown speedTrapLengthInput = new()
    {
        DecimalPlaces = 3,
        Increment = 0.100M,
        Minimum = 0.100M,
        Maximum = 9999.999M,
        Value = 12.000M,
        Width = 90,
        Enabled = false
    };
    private readonly Button applySettingsButton = new()
    {
        Text = "Apply Settings",
        AutoSize = true,
        Enabled = false
    };
    private readonly Button startPracticeButton = new()
    {
        Text = "Start Practice Setup",
        AutoSize = true,
        Enabled = false
    };
    private readonly Button demoPracticeButton = new()
    {
        Text = "Demo Practice Run",
        AutoSize = true
    };
    private readonly System.Windows.Forms.Timer heartbeatTimer = new()
    {
        Interval = 1000
    };
    private readonly TextBox logTextBox = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font(FontFamily.GenericMonospace, 9)
    };

    public MainForm()
    {
        Text = "Drag Strip Controller";
        MinimumSize = new Size(980, 600);
        StartPosition = FormStartPosition.CenterScreen;

        modeSelector.Items.AddRange(["HEADS_UP", "BRACKET"]);
        modeSelector.SelectedItem = "BRACKET";
        modeSelector.Enabled = true;
        laneCountSelector.Items.AddRange(["2", "4"]);
        laneCountSelector.SelectedItem = "4";
        laneCountSelector.Enabled = true;

        var connectionControls = CreateConnectionControls();
        var raceSettings = CreateRaceSettings();
        var menuStrip = CreateMenuStrip();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(menuStrip, 0, 0);
        layout.Controls.Add(connectionControls, 0, 1);
        layout.Controls.Add(raceSettings, 0, 2);
        layout.Controls.Add(logTextBox, 0, 3);
        Controls.Add(layout);
        MainMenuStrip = menuStrip;

        refreshButton.Click += (_, _) => RunMainButtonAction(refreshButton, RefreshPorts);
        connectButton.Click += (_, _) => RunMainButtonAction(connectButton, ToggleConnection);
        pingButton.Click += (_, _) => RunMainButtonAction(pingButton, () => SendCommand("PING"));
        statusButton.Click += (_, _) => RunMainButtonAction(statusButton, () => SendCommand("STATUS"));
        resetButton.Click += (_, _) => RunMainButtonAction(resetButton, () => SendCommand("RESET"));
        tournamentButton.Click += (_, _) => RunMainButtonAction(tournamentButton, () =>
        {
            new TournamentSetupForm(raceRepository).ShowDialog(this);
            RefreshTournaments();
        });
        runTournamentButton.Click += (_, _) => RunMainButtonAction(runTournamentButton, RunSelectedTournament);
        applySettingsButton.Click += (_, _) => RunMainButtonAction(applySettingsButton, () => ApplyRaceSettings());
        startPracticeButton.Click += (_, _) => RunMainButtonAction(startPracticeButton, StartPracticeSetup);
        demoPracticeButton.Click += (_, _) => RunMainButtonAction(demoPracticeButton, DemoPracticeRun);
        configureDistancesMenuItem.Click += (_, _) => ShowDistanceSettings();
        modeSelector.SelectedIndexChanged += (_, _) => UpdateDialInputState();
        laneCountSelector.SelectedIndexChanged += (_, _) => UpdateDialInputState();
        client.MessageReceived += (_, message) =>
            PostToUi(() => HandleMessage(message));
        client.ProtocolError += (_, error) =>
            PostToUi(() => AppendLog($"! {error}"));
        heartbeatTimer.Tick += (_, _) => UpdateConnectionLabel();
        heartbeatTimer.Start();

        RefreshPorts();
        RefreshTournaments();
        UpdateDialInputState();
    }

    private MenuStrip CreateMenuStrip()
    {
        var menuStrip = new MenuStrip { Dock = DockStyle.Top };
        var configureMenu = new ToolStripMenuItem("Configure");
        configureMenu.DropDownItems.Add(configureDistancesMenuItem);
        menuStrip.Items.Add(configureMenu);
        return menuStrip;
    }

    private Control CreateConnectionControls()
    {
        var group = new GroupBox
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Connection and Tournament",
            Padding = new Padding(8)
        };
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 8,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < layout.RowCount; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        portSelector.Width = 110;
        tournamentSelector.Width = 210;
        var labelMargin = new Padding(3, 8, 6, 3);

        layout.Controls.Add(new Label { AutoSize = true, Text = "Serial port:", Margin = labelMargin }, 0, 0);
        layout.Controls.Add(portSelector, 1, 0);
        layout.Controls.Add(refreshButton, 2, 0);
        layout.Controls.Add(connectButton, 3, 0);
        layout.Controls.Add(connectionLabel, 4, 0);
        layout.SetColumnSpan(connectionLabel, 4);

        layout.Controls.Add(pingButton, 1, 1);
        layout.Controls.Add(statusButton, 2, 1);
        layout.Controls.Add(resetButton, 3, 1);

        layout.Controls.Add(new Label { AutoSize = true, Text = "Tournament:", Margin = labelMargin }, 0, 2);
        layout.Controls.Add(tournamentButton, 1, 2);
        layout.SetColumnSpan(tournamentButton, 3);
        layout.Controls.Add(tournamentSelector, 4, 2);
        layout.SetColumnSpan(tournamentSelector, 2);
        layout.Controls.Add(runTournamentButton, 6, 2);

        group.Controls.Add(layout);
        return group;
    }

    private Control CreateRaceSettings()
    {
        var group = new GroupBox
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Manual Controller Settings",
            Padding = new Padding(8)
        };

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 12,
            RowCount = 3
        };
        for (var column = 0; column < layout.ColumnCount; column++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        for (var row = 0; row < layout.RowCount; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        var labelMargin = new Padding(3, 8, 6, 3);
        var controlMargin = new Padding(3, 3, 12, 3);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Race mode:",
            Margin = labelMargin
        }, 0, 0);
        modeSelector.Margin = controlMargin;
        layout.Controls.Add(modeSelector, 1, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Lanes:",
            Margin = labelMargin
        }, 2, 0);
        laneCountSelector.Margin = controlMargin;
        layout.Controls.Add(laneCountSelector, 3, 0);
        layout.Controls.Add(applySettingsButton, 4, 0);
        layout.SetColumnSpan(applySettingsButton, 2);
        layout.Controls.Add(startPracticeButton, 6, 0);
        layout.SetColumnSpan(startPracticeButton, 2);
        layout.Controls.Add(demoPracticeButton, 8, 0);
        layout.SetColumnSpan(demoPracticeButton, 3);

        for (var lane = 0; lane < LaneCount; lane++)
        {
            var column = lane * 3;
            layout.Controls.Add(new Label
            {
                AutoSize = true,
                Text = $"Lane {lane + 1} dial:",
                Margin = labelMargin
            }, column, 1);

            var input = new NumericUpDown
            {
                DecimalPlaces = 3,
                Increment = 0.001M,
                Minimum = 0.100M,
                Maximum = 60.000M,
                Value = 10.000M,
                Width = 78,
                Enabled = false,
                Margin = new Padding(3, 3, 3, 3)
            };
            toolTip.SetToolTip(
                input,
                "Manual per-lane dial-in for non-tournament bracket runs. Tournament heats use the runner grid instead.");
            dialInputs[lane] = input;
            layout.Controls.Add(input, column + 1, 1);
            layout.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "sec",
                Margin = new Padding(0, 8, 12, 3)
            }, column + 2, 1);
        }

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Practice lanes:",
            Margin = labelMargin
        }, 0, 2);
        var practiceLanePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0)
        };
        for (var lane = 0; lane < LaneCount; lane++)
        {
            var check = new CheckBox
            {
                AutoSize = true,
                Checked = true,
                Enabled = false,
                Margin = new Padding(3, 6, 16, 3),
                Text = $"Lane {lane + 1}"
            };
            toolTip.SetToolTip(
                check,
                "Select the lanes that must stage for the next manual practice run.");
            practiceLaneChecks[lane] = check;
            practiceLanePanel.Controls.Add(check);
        }
        layout.Controls.Add(practiceLanePanel, 1, 2);
        layout.SetColumnSpan(practiceLanePanel, 6);

        group.Controls.Add(layout);
        return group;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        heartbeatTimer.Stop();
        heartbeatTimer.Dispose();
        client.Dispose();
        base.OnFormClosed(e);
    }

    private void RefreshPorts()
    {
        var selectedPort = portSelector.SelectedItem as string;
        var ports = DragSerialClient.GetPortNames();
        portSelector.Items.Clear();
        portSelector.Items.AddRange(ports);

        if (selectedPort is not null && ports.Contains(selectedPort))
        {
            portSelector.SelectedItem = selectedPort;
        }
        else if (ports.Length > 0)
        {
            portSelector.SelectedIndex = 0;
        }
    }

    private void RunMainButtonAction(Button button, Action action)
    {
        var now = DateTimeOffset.Now;
        if (now - lastMainButtonActionAt < TimeSpan.FromMilliseconds(700))
        {
            return;
        }
        if (mainActionRunning)
        {
            return;
        }

        mainActionRunning = true;
        lastMainButtonActionAt = now;
        var wasEnabled = button.Enabled;
        button.Enabled = false;
        try
        {
            action();
        }
        finally
        {
            mainActionRunning = false;
            if (!IsDisposed)
            {
                button.Enabled = wasEnabled;
            }
            UpdateDialInputState();
        }
    }

    private void RefreshTournaments()
    {
        var selectedId = (tournamentSelector.SelectedItem as Tournament)?.Id;
        tournamentSelector.DataSource = raceRepository.GetTournaments().ToList();
        if (selectedId.HasValue)
        {
            tournamentSelector.SelectedItem = tournamentSelector.Items
                .Cast<Tournament>()
                .FirstOrDefault(item => item.Id == selectedId);
        }
    }

    private void RunSelectedTournament()
    {
        if (tournamentSelector.SelectedItem is not Tournament tournament)
        {
            MessageBox.Show(this, "Create or select a tournament first.", Text);
            return;
        }
        new TournamentRunnerForm(tournament, raceRepository, client).ShowDialog(this);
        RefreshTournaments();
    }

    private void ToggleConnection()
    {
        try
        {
            if (connectionRequested)
            {
                client.Disconnect();
                SetConnectedState(false);
                return;
            }

            if (portSelector.SelectedItem is not string portName)
            {
                MessageBox.Show(this, "Select a serial port first.", Text);
                return;
            }

            client.Connect(portName);
            SetConnectedState(true);
            AppendLog($"Connected to {portName} at 115200 baud.");
            AppendLog($"Serial log: {client.LogPath}");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Serial connection failed");
            SetConnectedState(false);
        }
    }

    private bool ApplyRaceSettings()
    {
        if (modeSelector.SelectedItem is not string mode)
        {
            return false;
        }

        if (speedTrapLengthInput.Value >= trackLengthInput.Value)
        {
            MessageBox.Show(
                this,
                "Speed-trap length must be shorter than the track length.",
                "Invalid distances");
            return false;
        }

        var laneCount = SelectedLaneCount();
        SendCommand("SET", "LANES", laneCount.ToString(CultureInfo.InvariantCulture));
        SendCommand("SET", "MODE", mode);
        SendCommand(
            "SET",
            "DISTANCES",
            ToThousandthsOfAnInch(trackLengthInput.Value),
            ToThousandthsOfAnInch(speedTrapLengthInput.Value));
        for (var lane = 0; lane < LaneCount; lane++)
        {
            if (!LaneIsActive(lane, laneCount))
            {
                continue;
            }

            var dialMilliseconds = decimal.ToInt32(dialInputs[lane].Value * 1000M);
            SendCommand(
                "SET",
                "DIAL",
                (lane + 1).ToString(CultureInfo.InvariantCulture),
                dialMilliseconds.ToString(CultureInfo.InvariantCulture));
        }
        return true;
    }

    private void StartPracticeSetup()
    {
        if (!client.IsConnected)
        {
            MessageBox.Show(this, "Connect to the controller first.", Text);
            return;
        }

        var laneCount = SelectedLaneCount();
        var selectedLanes = SelectedPracticeLanes(laneCount).ToArray();
        if (selectedLanes.Length == 0)
        {
            MessageBox.Show(this, "Select at least one practice lane.", Text);
            return;
        }

        if (!ApplyRaceSettings())
        {
            return;
        }
        SendCommand("SET", "HEAT_LANES", string.Join(',', selectedLanes));
        SendCommand("RESET");
        AppendLog($"Practice setup sent for lane(s): {string.Join(", ", selectedLanes)}.");
    }

    private void DemoPracticeRun()
    {
        var laneCount = SelectedLaneCount();
        var selectedLanes = SelectedPracticeLanes(laneCount).ToArray();
        if (selectedLanes.Length == 0)
        {
            MessageBox.Show(this, "Select at least one practice lane.", Text);
            return;
        }

        var bracketMode = string.Equals(
            modeSelector.SelectedItem as string,
            "BRACKET",
            StringComparison.Ordinal);
        var laneDialMilliseconds = selectedLanes.ToDictionary(
            lane => lane,
            lane => decimal.ToInt32(dialInputs[lane - 1].Value * 1000M));
        var messages = DemoHeatSimulator.CreatePracticeMessages(
            laneDialMilliseconds,
            bracketMode).ToArray();

        AppendLog($"DEMO: Practice run started for lane(s): {string.Join(", ", selectedLanes)}.");
        foreach (var message in messages)
        {
            AppendLog($"DEMO < {message.Encode()}");
        }

        AppendPracticeSummary(messages);
    }

    private void AppendPracticeSummary(IReadOnlyList<ProtocolMessage> messages)
    {
        var results = new Dictionary<int, PracticeDemoResult>();
        int ResultForLane(int lane)
        {
            if (!results.ContainsKey(lane))
            {
                results[lane] = new PracticeDemoResult();
            }
            return lane;
        }

        foreach (var message in messages)
        {
            if (message.Parts.Count >= 4 &&
                message.Parts[1] == "LANE" &&
                int.TryParse(message.Parts[2], out var lane))
            {
                _ = ResultForLane(lane);
                var result = results[lane];
                var kind = message.Parts[3];
                if (message.Type == "EVENT" && kind == "FOUL")
                {
                    result.Fouled = true;
                }
                else if (message.Type == "EVENT" && kind == "REACTION_US" &&
                         message.Parts.Count > 4 &&
                         long.TryParse(message.Parts[4], out var reactionUs))
                {
                    result.ReactionUs = reactionUs;
                }
                else if (message.Type == "RESULT" && kind == "ELAPSED_US" &&
                         message.Parts.Count > 4 &&
                         long.TryParse(message.Parts[4], out var elapsedUs))
                {
                    result.ElapsedUs = elapsedUs;
                }
                else if (message.Type == "RESULT" && kind == "BREAKOUT_US" &&
                         message.Parts.Count > 4 &&
                         long.TryParse(message.Parts[4], out var breakoutUs))
                {
                    result.BreakoutUs = breakoutUs;
                }
                else if (message.Type == "RESULT" && kind == "VALID")
                {
                    result.Valid = true;
                }
                else if (message.Type == "RESULT" && kind == "SPEED_MPH_X100" &&
                         message.Parts.Count > 4 &&
                         long.TryParse(message.Parts[4], out var speedMphX100))
                {
                    result.SpeedMphX100 = speedMphX100;
                }
                continue;
            }

            if (message.Type == "RESULT" &&
                message.Parts.Count >= 4 &&
                message.Parts[1] == "WINNER" &&
                message.Parts[2] == "LANE" &&
                int.TryParse(message.Parts[3], out var winningLane))
            {
                _ = ResultForLane(winningLane);
                results[winningLane].Winner = true;
            }
            else if (message.Type == "RESULT" &&
                     message.Parts.Count >= 5 &&
                     message.Parts[1] == "PLACE" &&
                     int.TryParse(message.Parts[2], out var place) &&
                     message.Parts[3] == "LANE" &&
                     int.TryParse(message.Parts[4], out var placedLane))
            {
                _ = ResultForLane(placedLane);
                results[placedLane].Place = place;
            }
        }

        foreach (var laneResult in results.OrderBy(item => item.Key))
        {
            AppendLog($"DEMO: {FormatPracticeSummary(laneResult.Key, laneResult.Value)}");
        }

        var winner = results
            .Where(item => item.Value.Winner || item.Value.Place == 1)
            .OrderBy(item => item.Key)
            .FirstOrDefault();
        AppendLog(winner.Value is null
            ? "DEMO: Practice complete — no winner."
            : $"DEMO: Practice complete — lane {winner.Key} wins.");
    }

    private void ShowDistanceSettings()
    {
        using var dialog = new Form
        {
            Text = "Track distances",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(330, 135)
        };

        var trackInput = new NumericUpDown
        {
            DecimalPlaces = 3,
            Increment = 1.000M,
            Minimum = trackLengthInput.Minimum,
            Maximum = trackLengthInput.Maximum,
            Value = trackLengthInput.Value,
            Width = 100
        };
        var trapInput = new NumericUpDown
        {
            DecimalPlaces = 3,
            Increment = 0.100M,
            Minimum = speedTrapLengthInput.Minimum,
            Maximum = speedTrapLengthInput.Maximum,
            Value = speedTrapLengthInput.Value,
            Width = 100
        };
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { AutoSize = true, Text = "Track length:", Margin = new Padding(3, 8, 6, 3) }, 0, 0);
        layout.Controls.Add(trackInput, 1, 0);
        layout.Controls.Add(new Label { AutoSize = true, Text = "in", Margin = new Padding(3, 8, 3, 3) }, 2, 0);
        layout.Controls.Add(new Label { AutoSize = true, Text = "Speed trap:", Margin = new Padding(3, 8, 6, 3) }, 0, 1);
        layout.Controls.Add(trapInput, 1, 1);
        layout.Controls.Add(new Label { AutoSize = true, Text = "in", Margin = new Padding(3, 8, 3, 3) }, 2, 1);
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.AddRange([cancelButton, okButton]);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 3);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        if (trapInput.Value >= trackInput.Value)
        {
            MessageBox.Show(
                this,
                "Speed-trap length must be shorter than the track length.",
                "Invalid distances");
            return;
        }

        trackLengthInput.Value = trackInput.Value;
        speedTrapLengthInput.Value = trapInput.Value;
        AppendLog(
            $"Configured distances: track {trackLengthInput.Value:0.###} in, " +
            $"speed trap {speedTrapLengthInput.Value:0.###} in.");
        if (client.IsConnected)
        {
            SendCommand(
                "SET",
                "DISTANCES",
                ToThousandthsOfAnInch(trackLengthInput.Value),
                ToThousandthsOfAnInch(speedTrapLengthInput.Value));
        }
    }

    private void HandleMessage(ProtocolMessage message)
    {
        if (message.Type == "HEARTBEAT")
        {
            UpdateConnectionLabel();
            return;
        }

        AppendLog($"< {message.Encode()}");

        if (message.Type == "HELLO")
        {
            UpdateConnectionLabel();
            return;
        }

        if (message.Type != "STATUS")
        {
            return;
        }

        if (message.Parts.Count >= 5 &&
            message.Parts[1] == "TREE" &&
            message.Parts[3] == "MODE")
        {
            modeSelector.SelectedItem = message.Parts[4];
            var lanesIndex = -1;
            for (var index = 5; index < message.Parts.Count; index++)
            {
                if (message.Parts[index] == "LANES")
                {
                    lanesIndex = index;
                    break;
                }
            }
            if (lanesIndex >= 0 && lanesIndex + 1 < message.Parts.Count)
            {
                laneCountSelector.SelectedItem = message.Parts[lanesIndex + 1];
            }
            UpdateDistanceFromStatus(message, "TRACK_IN_X1000", trackLengthInput);
            UpdateDistanceFromStatus(message, "TRAP_IN_X1000", speedTrapLengthInput);
            UpdateDialInputState();
            return;
        }

        if (message.Parts.Count < 5 ||
            message.Parts[1] != "LANE" ||
            message.Parts[3] != "DIAL_MS" ||
            !int.TryParse(message.Parts[2], out var laneNumber) ||
            !int.TryParse(message.Parts[4], out var dialMilliseconds) ||
            laneNumber is < 1 or > LaneCount)
        {
            return;
        }

        var seconds = dialMilliseconds / 1000M;
        dialInputs[laneNumber - 1].Value = Math.Clamp(
            seconds,
            dialInputs[laneNumber - 1].Minimum,
            dialInputs[laneNumber - 1].Maximum);
    }

    private void SendCommand(params string[] parts)
    {
        try
        {
            var line = ProtocolMessage.Create(parts).Encode();
            client.Send(parts);
            AppendLog($"> {line}");
        }
        catch (Exception exception)
        {
            AppendLog($"! {exception.Message}");
        }
    }

    private void SetConnectedState(bool connected)
    {
        connectionRequested = connected;
        connectButton.Text = connected ? "Disconnect" : "Connect";
        portSelector.Enabled = !connected;
        refreshButton.Enabled = !connected;
        pingButton.Enabled = connected;
        statusButton.Enabled = connected;
        resetButton.Enabled = connected;
        modeSelector.Enabled = true;
        laneCountSelector.Enabled = true;
        applySettingsButton.Enabled = connected;
        startPracticeButton.Enabled = connected;
        UpdateDialInputState();
        UpdateConnectionLabel();
        connectionLabel.Refresh();
    }

    private void UpdateConnectionLabel()
    {
        if (!connectionRequested)
        {
            connectionLabel.Text = "Disconnected";
            return;
        }

        if (!client.IsConnected)
        {
            connectionLabel.Text = "Connected — serial port not open";
            return;
        }

        if (client.LastHeartbeatReceivedAt is not { } heartbeatAt)
        {
            connectionLabel.Text = client.LastHelloReceivedAt.HasValue
                ? "Connected — waiting for heartbeat"
                : "Connected";
            return;
        }

        var age = DateTimeOffset.Now - heartbeatAt;
        connectionLabel.Text = age.TotalSeconds > 3
            ? $"Connected — controller stale {age.TotalSeconds:0}s"
            : "Connected — controller heartbeat OK";
    }

    private void UpdateDialInputState()
    {
        var enabled = string.Equals(modeSelector.SelectedItem as string, "BRACKET", StringComparison.Ordinal);
        var laneCount = SelectedLaneCount();
        for (var lane = 0; lane < dialInputs.Length; lane++)
        {
            var input = dialInputs[lane];
            if (input is not null)
            {
                input.Enabled = enabled && LaneIsActive(lane, laneCount);
            }
            var practiceLane = practiceLaneChecks[lane];
            if (practiceLane is not null)
            {
                var laneActive = LaneIsActive(lane, laneCount);
                practiceLane.Enabled = laneActive;
                if (!laneActive)
                {
                    practiceLane.Checked = false;
                }
                else if (laneCount == 2)
                {
                    practiceLane.Checked = lane is 0 or 3 && practiceLane.Checked;
                }
            }
        }
    }

    private IEnumerable<int> SelectedPracticeLanes(int laneCount)
    {
        for (var lane = 0; lane < practiceLaneChecks.Length; lane++)
        {
            if (LaneIsActive(lane, laneCount) && practiceLaneChecks[lane].Checked)
            {
                yield return lane + 1;
            }
        }
    }

    private int SelectedLaneCount() =>
        int.TryParse(laneCountSelector.SelectedItem as string, out var count) ? count : 4;

    private static bool LaneIsActive(int zeroBasedLane, int laneCount) =>
        laneCount == 4 || zeroBasedLane is 0 or 3;

    private static string ToThousandthsOfAnInch(decimal inches) =>
        decimal.ToInt32(inches * 1000M).ToString(CultureInfo.InvariantCulture);

    private static string FormatPracticeSummary(int lane, PracticeDemoResult result)
    {
        if (result.Fouled)
        {
            return $"Lane {lane}: RED LIGHT";
        }

        var parts = new List<string> { $"Lane {lane}:" };
        if (result.ReactionUs.HasValue)
        {
            parts.Add($"reaction {FormatSeconds(result.ReactionUs.Value)}s");
        }
        if (result.ElapsedUs.HasValue)
        {
            parts.Add($"ET {FormatSeconds(result.ElapsedUs.Value)}s");
        }
        if (result.SpeedMphX100.HasValue)
        {
            parts.Add($"MPH {result.SpeedMphX100.Value / 100.0:0.00}");
        }
        if (result.BreakoutUs.HasValue)
        {
            parts.Add($"breakout by {FormatSeconds(result.BreakoutUs.Value)}s");
        }
        else if (result.Valid)
        {
            parts.Add("legal");
        }
        if (result.Winner)
        {
            parts.Add("winner");
        }
        else if (result.Place.HasValue)
        {
            parts.Add($"place {result.Place.Value}");
        }
        return string.Join(", ", parts);
    }

    private static string FormatSeconds(long microseconds) =>
        (microseconds / 1_000_000.0).ToString("0.000", CultureInfo.CurrentCulture);

    private static void UpdateDistanceFromStatus(
        ProtocolMessage message,
        string fieldName,
        NumericUpDown input)
    {
        for (var index = 0; index + 1 < message.Parts.Count; index++)
        {
            if (message.Parts[index] != fieldName ||
                !int.TryParse(message.Parts[index + 1], out var value))
            {
                continue;
            }

            input.Value = Math.Clamp(value / 1000M, input.Minimum, input.Maximum);
            return;
        }
    }

    private void AppendLog(string text)
    {
        logTextBox.AppendText($"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");
    }

    private void PostToUi(Action action)
    {
        if (!IsDisposed)
        {
            BeginInvoke(action);
        }
    }

    private sealed class PracticeDemoResult
    {
        public bool Fouled { get; set; }
        public bool Valid { get; set; }
        public bool Winner { get; set; }
        public int? Place { get; set; }
        public long? ReactionUs { get; set; }
        public long? ElapsedUs { get; set; }
        public long? BreakoutUs { get; set; }
        public long? SpeedMphX100 { get; set; }
    }
}
