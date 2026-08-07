using System.Globalization;

namespace DragWin;

public sealed class PassResultsForm : Form
{
    private readonly DragSerialClient client;
    private readonly DataGridView resultsGrid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.Fixed3D,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
    };
    private readonly Label sessionSummary = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Text = "No passes armed"
    };
    private readonly Dictionary<int, PassLaneResult> currentResults = [];
    private bool voiceAnnouncementsEnabled;
    private string speechVoiceName;
    private SpeechBackendMode speechBackend;
    private bool passResultAnnounced;
    private int passNumber;

    public PassResultsForm(
        DragSerialClient client,
        bool voiceAnnouncementsEnabled,
        string speechVoiceName,
        SpeechBackendMode speechBackend)
    {
        this.client = client;
        this.voiceAnnouncementsEnabled = voiceAnnouncementsEnabled;
        this.speechVoiceName = speechVoiceName;
        this.speechBackend = speechBackend;
        Text = "Practice Pass Results";
        MinimumSize = new Size(980, 420);
        Size = new Size(1280, 560);
        StartPosition = FormStartPosition.CenterParent;

        AddColumn("Pass", "Pass", 35);
        AddColumn("Time", "Time", 55);
        AddColumn("Lane", "Lane", 35);
        AddColumn("Reaction", "RT", 48);
        AddColumn("Elapsed", "ET", 48);
        AddColumn("Split1", "Interval 1", 48);
        AddColumn("Split2", "Interval 2", 48);
        AddColumn("Split1To2", "I1-I2", 48);
        AddColumn("Split2ToTrap", "I2-Trap", 52);
        AddColumn("TrapToFinish", "Trap-Finish", 58);
        AddColumn("Speed", "MPH", 50);
        AddColumn("Place", "Place", 38);
        AddColumn("Outcome", "Outcome", 100);

        var clearButton = new Button { Text = "Clear", AutoSize = true, MinimumSize = new Size(70, 28) };
        var closeButton = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(80, 28) };
        clearButton.Click += (_, _) => ClearResults();
        closeButton.Click += (_, _) => Close();

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0, 0, 0, 6)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label
        {
            Text = "Pass Results",
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14, FontStyle.Bold)
        }, 0, 0);
        header.Controls.Add(sessionSummary, 1, 0);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(clearButton, 0, 0);
        footer.Controls.Add(closeButton, 1, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(resultsGrid, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
        CancelButton = closeButton;

        client.MessageReceived += ClientOnMessageReceived;
        FormClosed += (_, _) => client.MessageReceived -= ClientOnMessageReceived;
    }

    public void BeginPass(
        IReadOnlyCollection<int> lanes,
        IReadOnlyCollection<int> splitSensorLanes)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        if (lanes.Count == 0)
        {
            throw new ArgumentException("At least one lane is required.", nameof(lanes));
        }

        MarkIncompleteRows();
        currentResults.Clear();
        passResultAnnounced = false;
        passNumber++;
        var startedAt = DateTime.Now;
        foreach (var lane in lanes.Order())
        {
            var rowIndex = resultsGrid.Rows.Add(
                passNumber,
                startedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                lane,
                "",
                "",
                "",
                "",
                "",
                "",
                "",
                "",
                "",
                "Armed");
            currentResults[lane] = new PassLaneResult(resultsGrid.Rows[rowIndex])
            {
                SplitSensorsEnabled = splitSensorLanes.Contains(lane)
            };
        }
        sessionSummary.Text =
            $"Pass {passNumber}  |  Lane{(lanes.Count == 1 ? "" : "s")} {string.Join(", ", lanes.Order())}";
        Speak(RaceAnnouncementText.PracticeArmed(lanes));
    }

    public void UpdateAnnouncementSettings(
        bool enabled,
        string voiceName,
        SpeechBackendMode backend)
    {
        voiceAnnouncementsEnabled = enabled;
        speechVoiceName = voiceName;
        speechBackend = backend;
    }

    public void ProcessMessages(IEnumerable<ProtocolMessage> messages)
    {
        foreach (var message in messages)
        {
            ProcessMessage(message);
        }
    }

    private void AddColumn(string name, string header, float fillWeight) =>
        resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

    private void ClientOnMessageReceived(object? sender, ProtocolMessage message)
    {
        if (message.Type is not ("EVENT" or "RESULT") ||
            IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() => ProcessMessage(message));
    }

    private void ProcessMessage(ProtocolMessage message)
    {
        if (message.Parts.Count >= 4 &&
            message.Parts[1] == "LANE" &&
            int.TryParse(message.Parts[2], out var lane) &&
            currentResults.TryGetValue(lane, out var result))
        {
            var kind = message.Parts[3];
            if (message.Type == "EVENT" && kind == "REACTION_US" &&
                TryReadValue(message, out var reactionUs))
            {
                result.ReactionUs = reactionUs;
            }
            else if (message.Type == "EVENT" && kind == "FOUL")
            {
                result.Fouled = true;
            }
            else if (message.Type == "RESULT" && kind == "ELAPSED_US" &&
                     TryReadValue(message, out var elapsedUs))
            {
                result.ElapsedUs = elapsedUs;
            }
            else if (message.Type == "RESULT" && kind == "SPEED_MPH_X100" &&
                     TryReadValue(message, out var speedMphX100))
            {
                result.SpeedMphX100 = speedMphX100;
            }
            else if (message.Type == "RESULT" && kind == "INTERVAL_1_US" &&
                     TryReadValue(message, out var split1Us))
            {
                result.Split1Us = split1Us;
            }
            else if (message.Type == "RESULT" && kind == "INTERVAL_2_US" &&
                     TryReadValue(message, out var split2Us))
            {
                result.Split2Us = split2Us;
            }
            else if (message.Type == "RESULT" && kind == "SPEED_TRAP_US" &&
                     TryReadValue(message, out var speedTrapUs))
            {
                result.SpeedTrapUs = speedTrapUs;
            }
            else if (message.Type == "RESULT" && kind == "INTERVAL_1_UNAVAILABLE")
            {
                result.Split1Unavailable = true;
            }
            else if (message.Type == "RESULT" && kind == "INTERVAL_2_UNAVAILABLE")
            {
                result.Split2Unavailable = true;
            }
            else if (message.Type == "RESULT" && kind == "BREAKOUT_US" &&
                     TryReadValue(message, out var breakoutUs))
            {
                result.BreakoutUs = breakoutUs;
            }
            else if (message.Type == "RESULT" && kind == "VALID")
            {
                result.Valid = true;
            }
            else if (message.Type == "RESULT" && kind == "DNF")
            {
                result.DidNotFinish = true;
            }
            else if (message.Type == "RESULT" && kind == "ELAPSED_UNAVAILABLE")
            {
                result.ElapsedUnavailable = true;
            }
            else if (message.Type == "RESULT" && kind == "SPEED_UNAVAILABLE")
            {
                result.SpeedUnavailable = true;
            }
            else if (message.Type == "RESULT" && kind == "SPEED_INVALID")
            {
                result.SpeedInvalid = true;
            }
            UpdateRow(result);
        }

        if (message.Type == "RESULT" && message.Parts.Count >= 5 &&
            message.Parts[1] == "PLACE" &&
            int.TryParse(message.Parts[2], out var place) &&
            message.Parts[3] == "LANE" &&
            int.TryParse(message.Parts[4], out var placedLane) &&
            currentResults.TryGetValue(placedLane, out var placedResult))
        {
            placedResult.Place = place;
            UpdateRow(placedResult);
        }
        else if (message.Type == "RESULT" && message.Parts.Count >= 4 &&
                 message.Parts[1] == "WINNER" &&
                 message.Parts[2] == "LANE" &&
                 int.TryParse(message.Parts[3], out var winningLane) &&
                 currentResults.TryGetValue(winningLane, out var winningResult))
        {
            winningResult.Winner = true;
            UpdateRow(winningResult);
        }

        if (message.Type == "EVENT" && message.Parts.Count >= 3 &&
            message.Parts[1] == "TREE" && message.Parts[2] == "RACE_COMPLETE")
        {
            foreach (var laneResult in currentResults.Values)
            {
                laneResult.Complete = true;
                UpdateRow(laneResult);
            }
            if (!passResultAnnounced)
            {
                passResultAnnounced = true;
                Speak(RaceAnnouncementText.PracticeComplete(currentResults.Select(item =>
                    new PracticeAnnouncementResult(
                        item.Key,
                        item.Value.ElapsedUs,
                        item.Value.SpeedMphX100,
                        item.Value.Fouled,
                        item.Value.BreakoutUs.HasValue,
                        item.Value.DidNotFinish))));
            }
        }
        else if (message.Type == "EVENT" && message.Parts.Count >= 3 &&
                 message.Parts[1] == "TREE" && message.Parts[2] == "STAGING_ABORTED")
        {
            foreach (var laneResult in currentResults.Values)
            {
                laneResult.Row.Cells["Outcome"].Value = "Staging aborted";
                laneResult.Row.DefaultCellStyle.BackColor = Color.FromArgb(255, 244, 214);
            }
            Speak("Staging aborted. Please restage.");
        }
    }

    private void Speak(string phrase)
    {
        if (voiceAnnouncementsEnabled)
        {
            SpeechAnnouncer.SpeakAsync(phrase, speechVoiceName, speechBackend);
        }
    }

    private static bool TryReadValue(ProtocolMessage message, out long value)
    {
        value = 0;
        return message.Parts.Count > 4 && long.TryParse(message.Parts[4], out value);
    }

    private static void UpdateRow(PassLaneResult result)
    {
        result.Row.Cells["Reaction"].Value = FormatSeconds(result.ReactionUs);
        result.Row.Cells["Elapsed"].Value = result.ElapsedUnavailable
            ? "N/A"
            : FormatSeconds(result.ElapsedUs);
        result.Row.Cells["Split1"].Value = FormatSplit(
            result.Split1Us, result.Split1Unavailable, result.SplitSensorsEnabled);
        result.Row.Cells["Split2"].Value = FormatSplit(
            result.Split2Us, result.Split2Unavailable, result.SplitSensorsEnabled);
        result.Row.Cells["Split1To2"].Value = FormatSegment(
            result.Split1Us, result.Split2Us, result.SplitSensorsEnabled);
        result.Row.Cells["Split2ToTrap"].Value = FormatSegment(
            result.Split2Us, result.SpeedTrapUs, result.SplitSensorsEnabled);
        result.Row.Cells["TrapToFinish"].Value = FormatSegment(
            result.SpeedTrapUs, result.ElapsedUs, result.SplitSensorsEnabled);
        result.Row.Cells["Speed"].Value = result.SpeedMphX100.HasValue
            ? (result.SpeedMphX100.Value / 100.0).ToString("0.00", CultureInfo.CurrentCulture)
            : result.SpeedInvalid ? "Invalid" : result.SpeedUnavailable ? "N/A" : "";
        result.Row.Cells["Place"].Value = result.Place?.ToString(CultureInfo.CurrentCulture) ?? "";

        var outcome = result.Fouled ? "Red light" :
            result.BreakoutUs.HasValue ? $"Breakout by {FormatSeconds(result.BreakoutUs)}" :
            result.DidNotFinish ? "DNF" :
            result.Winner ? "Winner" :
            result.Place.HasValue ? $"Place {result.Place}" :
            result.Valid ? "Valid" :
            result.Complete ? "No result" :
            result.ReactionUs.HasValue ? "Running" : "Armed";
        result.Row.Cells["Outcome"].Value = outcome;

        result.Row.DefaultCellStyle.BackColor = result.Fouled || result.DidNotFinish
            ? Color.FromArgb(252, 230, 230)
            : result.BreakoutUs.HasValue
                ? Color.FromArgb(255, 244, 214)
                : result.Complete && (result.Valid || result.Place.HasValue || result.Winner)
                    ? Color.FromArgb(218, 242, 225)
                    : SystemColors.Window;
    }

    private static string FormatSeconds(long? microseconds) => microseconds.HasValue
        ? (microseconds.Value / 1_000_000.0).ToString("0.000", CultureInfo.CurrentCulture)
        : "";

    private static string FormatSplit(long? value, bool unavailable, bool enabled) =>
        value.HasValue ? FormatSeconds(value) : unavailable ? "Missed" : enabled ? "" : "N/A";

    private static string FormatSegment(long? start, long? end, bool enabled) =>
        start.HasValue && end.HasValue && end >= start
            ? FormatSeconds(end - start)
            : enabled ? "" : "N/A";

    private void MarkIncompleteRows()
    {
        foreach (var result in currentResults.Values.Where(result => !result.Complete))
        {
            result.Row.Cells["Outcome"].Value = "Incomplete";
            result.Row.DefaultCellStyle.BackColor = Color.FromArgb(245, 246, 247);
        }
    }

    private void ClearResults()
    {
        currentResults.Clear();
        resultsGrid.Rows.Clear();
        passNumber = 0;
        sessionSummary.Text = "No passes armed";
    }

    private sealed class PassLaneResult(DataGridViewRow row)
    {
        public DataGridViewRow Row { get; } = row;
        public long? ReactionUs { get; set; }
        public long? ElapsedUs { get; set; }
        public long? SpeedMphX100 { get; set; }
        public bool SplitSensorsEnabled { get; set; }
        public long? Split1Us { get; set; }
        public long? Split2Us { get; set; }
        public long? SpeedTrapUs { get; set; }
        public bool Split1Unavailable { get; set; }
        public bool Split2Unavailable { get; set; }
        public long? BreakoutUs { get; set; }
        public int? Place { get; set; }
        public bool Fouled { get; set; }
        public bool Valid { get; set; }
        public bool DidNotFinish { get; set; }
        public bool ElapsedUnavailable { get; set; }
        public bool SpeedUnavailable { get; set; }
        public bool SpeedInvalid { get; set; }
        public bool Winner { get; set; }
        public bool Complete { get; set; }
    }
}
