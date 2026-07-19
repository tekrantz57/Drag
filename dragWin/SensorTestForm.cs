namespace DragWin;

public sealed class SensorTestForm : Form
{
    private const int LaneCount = 4;
    private const int SensorCount = 4;
    private static readonly string[] SensorNames = ["Pre-Stage", "Stage", "Speed Trap", "Finish"];
    private static readonly string[,] SensorPins =
    {
        { "A0", "A1", "A2", "A3" },
        { "A4", "A5", "A6", "A7" },
        { "A8", "A9", "A10", "A11" },
        { "A12", "A13", "A14", "A15" }
    };

    private readonly DragSerialClient client;
    private readonly Label[,] stateLabels = new Label[LaneCount, SensorCount];
    private readonly Label statusLabel = new()
    {
        AutoSize = true,
        Text = "Polling sensor status..."
    };
    private readonly System.Windows.Forms.Timer pollTimer = new()
    {
        Interval = 500
    };

    public SensorTestForm(DragSerialClient client)
    {
        this.client = client;

        Text = "Sensor Test";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 520);
        Size = new Size(980, 560);

        Controls.Add(CreateLayout());

        client.MessageReceived += ClientOnMessageReceived;
        pollTimer.Tick += (_, _) => PollStatus();
        FormClosed += (_, _) =>
        {
            pollTimer.Stop();
            client.MessageReceived -= ClientOnMessageReceived;
            pollTimer.Dispose();
        };
        Shown += (_, _) =>
        {
            PollStatus();
            pollTimer.Start();
        };
    }

    private Control CreateLayout()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        outer.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(860, 0),
            Text = "Break each beam and watch the matching box change to BLOCKED. Pin names match the Mega analog header. Unwired Mega inputs can float, so an empty pin may randomly show blocked until a sensor or pull-down is connected.",
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);
        outer.Controls.Add(CreateSensorGrid(), 0, 1);
        outer.Controls.Add(statusLabel, 0, 2);
        return outer;
    }

    private Control CreateSensorGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = SensorCount + 1,
            RowCount = LaneCount + 1,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        for (var column = 0; column < SensorCount; column++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        for (var row = 0; row < LaneCount; row++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        }

        grid.Controls.Add(CreateHeaderLabel("Lane"), 0, 0);
        for (var sensor = 0; sensor < SensorCount; sensor++)
        {
            grid.Controls.Add(CreateHeaderLabel(SensorNames[sensor]), sensor + 1, 0);
        }

        for (var lane = 0; lane < LaneCount; lane++)
        {
            grid.Controls.Add(CreateHeaderLabel($"{lane + 1}"), 0, lane + 1);
            for (var sensor = 0; sensor < SensorCount; sensor++)
            {
                var label = CreateStateLabel(SensorPins[lane, sensor]);
                stateLabels[lane, sensor] = label;
                grid.Controls.Add(label, sensor + 1, lane + 1);
            }
        }

        return grid;
    }

    private static Label CreateHeaderLabel(string text) =>
        new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = text,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold),
            Margin = new Padding(4)
        };

    private static Label CreateStateLabel(string pin) =>
        new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = $"{pin}\r\n?",
            BackColor = Color.LightYellow,
            ForeColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold),
            Margin = new Padding(6)
        };

    private void PollStatus()
    {
        if (!client.IsConnected)
        {
            statusLabel.Text = "Serial port disconnected.";
            return;
        }

        try
        {
            client.Send("STATUS");
            statusLabel.Text = $"Requested status at {DateTime.Now:T}";
        }
        catch (Exception exception)
        {
            statusLabel.Text = $"Could not request status: {exception.Message}";
        }
    }

    private void ClientOnMessageReceived(object? sender, ProtocolMessage message)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() => HandleMessage(message));
    }

    private void HandleMessage(ProtocolMessage message)
    {
        if (message.Type != "STATUS" ||
            message.Parts.Count < 4 ||
            message.Parts[1] != "LANE" ||
            !int.TryParse(message.Parts[2], out var laneNumber) ||
            laneNumber is < 1 or > LaneCount)
        {
            return;
        }

        var fields = ParseFields(message);
        UpdateSensorState(fields, laneNumber - 1, "PRESTAGE", 0);
        UpdateSensorState(fields, laneNumber - 1, "STAGE", 1);
        UpdateSensorState(fields, laneNumber - 1, "SPEED_TRAP", 2);
        UpdateSensorState(fields, laneNumber - 1, "FINISH", 3);
        statusLabel.Text = $"Last sensor update {DateTime.Now:T}";
    }

    private static Dictionary<string, string> ParseFields(ProtocolMessage message)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 3; index + 1 < message.Parts.Count; index += 2)
        {
            fields[message.Parts[index]] = message.Parts[index + 1];
        }

        return fields;
    }

    private void UpdateSensorState(
        IReadOnlyDictionary<string, string> fields,
        int laneIndex,
        string fieldName,
        int sensorIndex)
    {
        if (!fields.TryGetValue(fieldName, out var value))
        {
            return;
        }

        if (value is not ("0" or "1"))
        {
            return;
        }

        var blocked = value == "1";
        var label = stateLabels[laneIndex, sensorIndex];
        label.Text = $"{SensorPins[laneIndex, sensorIndex]}\r\n{(blocked ? "BLOCKED" : "clear")}";
        label.BackColor = blocked ? Color.LimeGreen : Color.FromArgb(235, 235, 235);
        label.ForeColor = blocked ? Color.Black : Color.FromArgb(70, 70, 70);
    }
}
