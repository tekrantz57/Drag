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
    private readonly Button confirmButton = new() { Text = "Confirm Results / Advance", AutoSize = true, Enabled = false };
    private RoundPlan round = null!;
    private HeatPlan heat = null!;
    private int heatIndex;
    private readonly Dictionary<int, LiveLaneResult> liveResults = [];
    private int nextFinishOrder = 1;

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
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Dial", HeaderText = "Dial", ReadOnly = true });
        lanesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", ReadOnly = true });

        sendHeatButton.Click += (_, _) => SendHeat();
        confirmButton.Click += (_, _) => ConfirmHeat();
        lanesGrid.DataError += LanesGridOnDataError;
        client.MessageReceived += ClientOnMessageReceived;
        FormClosed += (_, _) => client.MessageReceived -= ClientOnMessageReceived;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([sendHeatButton, confirmButton]);
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
        heat = round.Heats[heatIndex];
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
                entry.Car.DefaultDialMilliseconds / 1000.0,
                entry.IsBye ? "BYE PASS — guaranteed advance" : "Pending")];
            row.Tag = entry;
            liveResults[entry.LaneNumber] = new LiveLaneResult();
        }
    }

    private void SendHeat()
    {
        if (!client.IsConnected)
        {
            MessageBox.Show(this, "Connect to the controller first.", Text);
            return;
        }

        var assignments = ReadAssignments();
        if (assignments is null) return;
        repository.UpdateHeatLanes(tournament.Id, round.RoundNumber, heat.HeatNumber, assignments);
        heat = heat with
        {
            Entries = heat.Entries.Select(entry =>
                entry with { LaneNumber = assignments[entry.Car.Id] }).ToArray()
        };
        liveResults.Clear();
        foreach (var entry in heat.Entries) liveResults[entry.LaneNumber] = new LiveLaneResult();

        client.Send("SET", "LANES", tournament.LaneCount.ToString());
        client.Send("SET", "MODE", "BRACKET");
        client.Send("SET", "HEAT_LANES", string.Join(',', heat.Entries.Select(entry => entry.LaneNumber).Order()));
        foreach (var entry in heat.Entries)
        {
            client.Send("SET", "DIAL", entry.LaneNumber.ToString(), entry.Car.DefaultDialMilliseconds.ToString());
        }
        client.Send("RESET");
        Append("Heat configuration sent. Stage only the displayed lanes.");
        sendHeatButton.Enabled = false;
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
                lanesGrid.Rows[rowIndex].Cells["Lane"].Value = entry.LaneNumber;
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
        Append(message.Encode());
        if (message.Parts.Count >= 4 && message.Parts[1] == "LANE" &&
            int.TryParse(message.Parts[2], out var lane) &&
            liveResults.TryGetValue(lane, out var result))
        {
            var kind = message.Parts[3];
            if (message.Type == "EVENT" && kind == "FOUL") result.Fouled = true;
            if (message.Type == "EVENT" && kind == "REACTION_US" && message.Parts.Count > 4 &&
                long.TryParse(message.Parts[4], out var reaction)) result.ReactionUs = reaction;
            if (message.Type == "RESULT" && kind == "ELAPSED_US")
            {
                result.Finished = true;
                result.FinishOrder = nextFinishOrder++;
            }
            if (message.Type == "RESULT" && kind == "BREAKOUT_US" && message.Parts.Count > 4 &&
                long.TryParse(message.Parts[4], out var breakout)) result.BreakoutUs = breakout;
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
            row.Cells["Status"].Value = result.Fouled ? "FOUL" :
                result.BreakoutUs.HasValue ? $"Breakout {result.BreakoutUs / 1000.0:0.000} ms" :
                result.Finished ? $"Finished #{result.FinishOrder}" :
                result.ReactionUs.HasValue ? $"Reaction {result.ReactionUs / 1000.0:0.000} ms" :
                ((RoundEntry)row.Tag!).IsBye ? "BYE PASS" : "Running";
        }
    }

    private void ConfirmHeat()
    {
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
            sendHeatButton.Enabled = true;
            return;
        }

        var (cars, reactions) = repository.GetRoundAdvancers(tournament.Id, round.RoundNumber);
        if (cars.Count <= 1)
        {
            repository.CompleteTournament(tournament.Id);
            MessageBox.Show(this, cars.Count == 1 ? $"Winner: {cars[0].DisplayName}" : "Tournament complete with no winner.", Text);
            Close();
            return;
        }
        var nextRound = planner.CreateRound(cars, tournament.LaneCount, round.RoundNumber + 1, priorReactionMicroseconds: reactions);
        repository.SaveRound(tournament.Id, nextRound);
        LoadCurrentRound();
        sendHeatButton.Enabled = true;
    }

    private void Append(string text) =>
        eventLog.AppendText($"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");

    private sealed class LiveLaneResult
    {
        public bool Fouled { get; set; }
        public bool Finished { get; set; }
        public int FinishOrder { get; set; }
        public long? ReactionUs { get; set; }
        public long? BreakoutUs { get; set; }
    }
}
