namespace DragWin;

public sealed class SensorTestForm : Form
{
    private const int LaneCount = 4;
    private const int SensorCount = 6;
    private static readonly string[] SensorNames = ["Pre-Stage", "Stage", "Interval 1", "Interval 2", "Speed Trap", "Finish"];
    private static readonly string[] SensorProtocolNames = ["PRESTAGE", "STAGE", "INTERVAL_1", "INTERVAL_2", "SPEED_TRAP", "FINISH"];
    private static readonly string[,] SensorPins =
    {
        { "A0", "A1", "D2", "D3", "A2", "A3" },
        { "A4", "A5", "D4", "D5", "A6", "A7" },
        { "A8", "A9", "D6", "D7", "A10", "A11" },
        { "A12", "A13", "D8", "D9", "A14", "A15" }
    };

    private readonly DragSerialClient client;
    private readonly Label[,] stateLabels = new Label[LaneCount, SensorCount];
    private readonly bool?[,] blockedStates = new bool?[LaneCount, SensorCount];
    private readonly bool?[,] rawBlockedStates = new bool?[LaneCount, SensorCount];
    private readonly ulong[,] blockedEdgeCounts = new ulong[LaneCount, SensorCount];
    private readonly ulong?[,] lastPulseWidthsUs = new ulong?[LaneCount, SensorCount];
    private readonly Label statusLabel = new()
    {
        AutoSize = true,
        Text = "Polling sensor status..."
    };
    private readonly System.Windows.Forms.Timer pollTimer = new()
    {
        Interval = 500
    };
    private readonly Button resetDiagnosticsButton = new()
    {
        AutoSize = true,
        Text = "Reset Counts"
    };

    public SensorTestForm(DragSerialClient client)
    {
        this.client = client;

        Text = "Sensor Test";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1100, 520);
        Size = new Size(1250, 600);

        Controls.Add(CreateLayout());

        client.MessageReceived += ClientOnMessageReceived;
        pollTimer.Tick += (_, _) => PollStatus();
        resetDiagnosticsButton.Click += (_, _) => ResetDiagnostics();
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
        outer.Controls.Add(CreateFooter(), 0, 2);
        return outer;
    }

    private Control CreateFooter()
    {
        var footer = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(statusLabel, 0, 0);
        footer.Controls.Add(resetDiagnosticsButton, 1, 0);
        return footer;
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
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / SensorCount));
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
            Font = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold),
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
            client.Send("SENSOR_DIAGNOSTICS");
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
        if (message.Type == "ACK" &&
            message.Parts.Count >= 2 &&
            message.Parts[1] == "RESET_SENSOR_DIAGNOSTICS")
        {
            ClearLocalDiagnostics();
            statusLabel.Text = "Sensor counters reset.";
            return;
        }

        if (message.Type == "SENSOR")
        {
            HandleSensorDiagnostic(message);
            return;
        }

        if (message.Type == "STATUS" && message.Parts.Count >= 6 &&
            message.Parts[1] == "INTERVALS" && message.Parts[2] == "LANE" &&
            int.TryParse(message.Parts[3], out var splitLaneNumber) &&
            splitLaneNumber is >= 1 and <= LaneCount)
        {
            var splitFields = ParseFields(message, 4);
            var laneIndex = splitLaneNumber - 1;
            if (splitFields.GetValueOrDefault("ENABLED") == "1")
            {
                UpdateSensorState(splitFields, laneIndex, "INTERVAL_1", 2);
                UpdateSensorState(splitFields, laneIndex, "INTERVAL_2", 3);
            }
            else
            {
                SetSensorNotInstalled(laneIndex, 2);
                SetSensorNotInstalled(laneIndex, 3);
            }
            return;
        }

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
        UpdateSensorState(fields, laneNumber - 1, "SPEED_TRAP", 4);
        UpdateSensorState(fields, laneNumber - 1, "FINISH", 5);
        statusLabel.Text = $"Last sensor update {DateTime.Now:T}";
    }

    private void HandleSensorDiagnostic(ProtocolMessage message)
    {
        if (message.Parts.Count < 5 ||
            !int.TryParse(message.Parts[1], out var laneNumber) ||
            laneNumber is < 1 or > LaneCount)
        {
            return;
        }

        var sensorIndex = Array.IndexOf(SensorProtocolNames, message.Parts[2]);
        if (sensorIndex < 0)
        {
            return;
        }

        var fields = ParseFields(message, 3);
        var laneIndex = laneNumber - 1;
        if (fields.TryGetValue("RAW", out var rawBlocked) &&
            rawBlocked is "0" or "1")
        {
            rawBlockedStates[laneIndex, sensorIndex] = rawBlocked == "1";
        }
        if (fields.TryGetValue("EDGES", out var edgeCount) &&
            ulong.TryParse(edgeCount, out var parsedEdgeCount))
        {
            blockedEdgeCounts[laneIndex, sensorIndex] = parsedEdgeCount;
        }
        if (fields.TryGetValue("PULSE_US", out var pulseWidth) &&
            pulseWidth == "NONE")
        {
            lastPulseWidthsUs[laneIndex, sensorIndex] = null;
        }
        else if (pulseWidth is not null &&
                 ulong.TryParse(pulseWidth, out var parsedPulseWidth))
        {
            lastPulseWidthsUs[laneIndex, sensorIndex] = parsedPulseWidth;
        }

        RefreshSensorLabel(laneIndex, sensorIndex);
        statusLabel.Text = $"Last diagnostic update {DateTime.Now:T}";
    }

    private void SetSensorNotInstalled(int laneIndex, int sensorIndex)
    {
        blockedStates[laneIndex, sensorIndex] = null;
        rawBlockedStates[laneIndex, sensorIndex] = null;
        var label = stateLabels[laneIndex, sensorIndex];
        label.Text = $"{SensorPins[laneIndex, sensorIndex]}\r\nNot installed";
        label.BackColor = Color.FromArgb(235, 235, 235);
        label.ForeColor = Color.FromArgb(90, 90, 90);
    }

    private static Dictionary<string, string> ParseFields(
        ProtocolMessage message,
        int startIndex = 3)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = startIndex; index + 1 < message.Parts.Count; index += 2)
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
        blockedStates[laneIndex, sensorIndex] = blocked;
        RefreshSensorLabel(laneIndex, sensorIndex);
    }

    private void RefreshSensorLabel(int laneIndex, int sensorIndex)
    {
        var blocked = blockedStates[laneIndex, sensorIndex];
        var rawBlocked = rawBlockedStates[laneIndex, sensorIndex];
        var stateText = blocked.HasValue ? (blocked.Value ? "BLOCKED" : "clear") : "?";
        if (rawBlocked.HasValue && rawBlocked != blocked)
        {
            stateText += rawBlocked.Value ? " (raw blocked)" : " (raw clear)";
        }

        var pulseText = lastPulseWidthsUs[laneIndex, sensorIndex].HasValue
            ? $"{lastPulseWidthsUs[laneIndex, sensorIndex]!.Value:N0} us"
            : "none";
        var label = stateLabels[laneIndex, sensorIndex];
        label.Text =
            $"{SensorPins[laneIndex, sensorIndex]}\r\n{stateText}\r\n" +
            $"Edges {blockedEdgeCounts[laneIndex, sensorIndex]:N0}\r\nPulse {pulseText}";
        label.BackColor = blocked == true
            ? Color.LimeGreen
            : blocked == false
                ? Color.FromArgb(235, 235, 235)
                : Color.LightYellow;
        label.ForeColor = blocked == false
            ? Color.FromArgb(70, 70, 70)
            : Color.Black;
    }

    private void ResetDiagnostics()
    {
        if (!client.IsConnected)
        {
            statusLabel.Text = "Serial port disconnected.";
            return;
        }

        try
        {
            client.Send("RESET_SENSOR_DIAGNOSTICS");
            statusLabel.Text = "Resetting sensor counters...";
        }
        catch (Exception exception)
        {
            statusLabel.Text = $"Could not reset counters: {exception.Message}";
        }
    }

    private void ClearLocalDiagnostics()
    {
        for (var lane = 0; lane < LaneCount; lane++)
        {
            for (var sensor = 0; sensor < SensorCount; sensor++)
            {
                blockedEdgeCounts[lane, sensor] = 0;
                lastPulseWidthsUs[lane, sensor] = null;
                RefreshSensorLabel(lane, sensor);
            }
        }
    }
}
