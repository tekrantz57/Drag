using System.Globalization;

namespace DragWin;

public sealed class TournamentRunnerForm : Form
{
    private readonly Tournament tournament;
    private readonly RaceRepository repository;
    private readonly DragSerialClient client;
    private readonly TournamentPlanner planner = new();
    private readonly Label heading = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly DataGridView lanesGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly TextBox eventLog = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
    };
    private readonly Button sendHeatButton = new() { Text = "Send Heat to Controller", AutoSize = true };
    private readonly Button demoHeatButton = new() { Text = "Demo Heat", AutoSize = true };
    private readonly Button confirmLaneChoiceButton = new()
    {
        Text = "Confirm Lane Choice",
        AutoSize = true,
        Visible = false
    };
    private readonly Button confirmButton = new() { Text = "Confirm Results / Advance", AutoSize = true, Enabled = false };
    private RoundPlan round = null!;
    private HeatPlan heat = null!;
    private int heatIndex;
    private readonly Dictionary<int, LiveLaneResult> liveResults = [];
    private int nextFinishOrder = 1;
    private LaneChoiceSession? laneChoiceSession;
    private bool confirmingHeat;
    private bool completionReportShown;
    private bool runnerActionRunning;
    private DateTimeOffset lastRunnerButtonActionAt;

    public TournamentRunnerForm(
        Tournament tournament,
        RaceRepository repository,
        DragSerialClient client)
    {
        this.tournament = tournament;
        this.repository = repository;
        this.client = client;
        Text = $"Run Tournament — {tournament.Name}";
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterParent;

        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Choice", HeaderText = "Choice", ReadOnly = true });
        var laneColumn = new DataGridViewComboBoxColumn
        {
            Name = "Lane",
            HeaderText = "Lane",
            ValueType = typeof(int),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        };
        foreach (var lane in tournament.LaneCount == 2
                     ? new[] { 1, 4 }
                     : new[] { 1, 2, 3, 4 })
        {
            laneColumn.Items.Add(lane);
        }
        lanesGrid.Columns.Add(laneColumn);
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Racer", HeaderText = "Racer", ReadOnly = true });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Car", HeaderText = "Car", ReadOnly = true });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Dial",
            HeaderText = "Dial",
            ToolTipText = "Per-run dial-in override in seconds. This does not change the car default."
        });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reaction", HeaderText = "Reaction (s)", ReadOnly = true });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Elapsed", HeaderText = "ET (s)", ReadOnly = true });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Speed", HeaderText = "MPH", ReadOnly = true });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Result", HeaderText = "Result", ReadOnly = true });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", ReadOnly = true });

        sendHeatButton.Click += (_, _) => RunRunnerButtonAction(sendHeatButton, SendHeat);
        demoHeatButton.Click += (_, _) => RunRunnerButtonAction(demoHeatButton, DemoHeat);
        confirmLaneChoiceButton.Click += (_, _) => RunRunnerButtonAction(confirmLaneChoiceButton, ConfirmLaneChoice);
        confirmButton.Click += (_, _) => ConfirmHeat();
        lanesGrid.DataError += LanesGridOnDataError;
        client.MessageReceived += ClientOnMessageReceived;
        FormClosed += (_, _) => client.MessageReceived -= ClientOnMessageReceived;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([confirmLaneChoiceButton, sendHeatButton, demoHeatButton, confirmButton]);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
        split.Panel1.Controls.Add(lanesGrid);
        split.Panel2.Controls.Add(eventLog);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
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
        nextFinishOrder = 1;
        confirmButton.Enabled = false;
        lanesGrid.Rows.Clear();
        heading.Text = $"{tournament.Name} — Round {round.RoundNumber}, Heat {heat.HeatNumber} — {heat.AdvanceCount} advance";
        foreach (var entry in heat.Entries.OrderBy(entry => entry.LaneChoiceOrder))
        {
            var row = lanesGrid.Rows[lanesGrid.Rows.Add(
                entry.LaneChoiceOrder,
                entry.LaneNumber,
                entry.Car.RacerName,
                entry.Car.Name,
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
            UpdateHeatActionButtons();
            return;
        }

        laneChoiceSession = new LaneChoiceSession(
            heat.Entries,
            tournament.LaneCount == 2 ? [1, 4] : [1, 2, 3, 4]);
        confirmLaneChoiceButton.Visible = true;
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
            }
            cell.Value = lane;
        }

        confirmLaneChoiceButton.Enabled = !laneChoiceSession.IsComplete;
        confirmLaneChoiceButton.Text = laneChoiceSession.IsComplete
            ? "Lane Choices Complete"
            : "Confirm Current Lane Choice";
        UpdateHeatActionButtons();
    }

    private void UpdateHeatActionButtons()
    {
        var laneChoicesComplete = laneChoiceSession is null || laneChoiceSession.IsComplete;
        var canStartHeat = laneChoicesComplete && !confirmingHeat && !runnerActionRunning;
        sendHeatButton.Enabled = canStartHeat;
        demoHeatButton.Enabled = canStartHeat;
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
        Append(displaced.HasValue
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

        client.Send("SET", "LANES", tournament.LaneCount.ToString());
        client.Send("SET", "MODE", "BRACKET");
        client.Send("SET", "HEAT_LANES", string.Join(',', heat.Entries.Select(entry => entry.LaneNumber).Order()));
        foreach (var entry in heat.Entries)
        {
            client.Send("SET", "DIAL", entry.LaneNumber.ToString(), entry.DialMilliseconds.ToString());
        }
        client.Send("RESET");
        Append("Heat configuration sent. Stage only the displayed lanes.");
        sendHeatButton.Enabled = false;
        demoHeatButton.Enabled = false;
        return true;
    }

    private bool DemoHeat()
    {
        if (laneChoiceSession is { IsComplete: false })
        {
            MessageBox.Show(this, "Complete the ordered lane choices first.", Text);
            return false;
        }

        var assignments = ReadAssignments();
        if (assignments is null || !ApplyHeatGridInputs(assignments)) return false;

        Append("DEMO: Simulated heat results generated. Confirm results to advance.");
        sendHeatButton.Enabled = false;
        demoHeatButton.Enabled = false;
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
        nextFinishOrder = 1;
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
        if (message.Parts.Count >= 4 && message.Parts[1] == "LANE" &&
            int.TryParse(message.Parts[2], out var lane) &&
            liveResults.TryGetValue(lane, out var result))
        {
            var kind = message.Parts[3];
            if (message.Type == "EVENT" && kind == "FOUL") result.Fouled = true;
            if (message.Type == "EVENT" && kind == "REACTION_US" && message.Parts.Count > 4 &&
                long.TryParse(message.Parts[4], out var reaction)) result.ReactionUs = reaction;
            if (message.Type == "RESULT" && kind == "ELAPSED_US" && message.Parts.Count > 4 &&
                long.TryParse(message.Parts[4], out var elapsed))
            {
                result.Finished = true;
                result.ElapsedUs = elapsed;
                if (result.FinishOrder == 0)
                {
                    result.FinishOrder = nextFinishOrder++;
                }
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
            UpdateStatusCells();
        }
        if (message.Type == "EVENT" && message.Parts.Count >= 3 &&
            message.Parts[1] == "TREE" && message.Parts[2] == "RACE_COMPLETE")
        {
            confirmButton.Enabled = true;
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
                result.Finished ? $"Finished #{result.FinishOrder}" :
                result.ReactionUs.HasValue ? $"Reaction {FormatSeconds(result.ReactionUs.Value)}" :
                ((RoundEntry)row.Tag!).IsBye ? "BYE PASS" : "Running";
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
        if (result.Fouled)
        {
            return "Red light";
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

    private void ConfirmHeat()
    {
        if (confirmingHeat)
        {
            return;
        }

        confirmingHeat = true;
        confirmButton.Enabled = false;
        sendHeatButton.Enabled = false;
        demoHeatButton.Enabled = false;

        var results = heat.Entries.Select(entry =>
        {
            var live = liveResults.GetValueOrDefault(entry.LaneNumber) ?? new LiveLaneResult();
            var legality = live.Fouled ? RunLegality.RedLight :
                live.BreakoutUs.HasValue ? RunLegality.Breakout :
                live.Finished ? RunLegality.Legal : RunLegality.DidNotFinish;
            return new RunResult(
                entry.Car.Id, legality,
                live.FinishOrder == 0 ? int.MaxValue : live.FinishOrder,
                live.ReactionUs, live.BreakoutUs, entry.IsBye);
        }).ToArray();
        var advancers = planner.SelectAdvancers(heat, results);
        repository.SaveHeatResults(
            tournament.Id, round.RoundNumber, heat.HeatNumber,
            results, advancers.Select(result => result.CarId).ToHashSet());

        if (!repository.IsRoundConfirmed(tournament.Id, round.RoundNumber))
        {
            heatIndex++;
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
