using System.Globalization;

namespace DragWin;

public sealed class MainForm : Form
{
    private const int LaneCount = 4;

    private readonly DragSerialClient client = new();
    private readonly ComboBox portSelector = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button refreshButton = new() { Text = "Refresh" };
    private readonly Button connectButton = new() { Text = "Connect" };
    private readonly Button pingButton = new() { Text = "Ping", Enabled = false };
    private readonly Button statusButton = new() { Text = "Status", Enabled = false };
    private readonly Button resetButton = new() { Text = "Reset", Enabled = false };
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
    private readonly NumericUpDown[] dialInputs = new NumericUpDown[LaneCount];
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
        MinimumSize = new Size(820, 540);
        StartPosition = FormStartPosition.CenterScreen;

        modeSelector.Items.AddRange(["HEADS_UP", "BRACKET"]);
        modeSelector.SelectedIndex = 0;
        laneCountSelector.Items.AddRange(["2", "4"]);
        laneCountSelector.SelectedItem = "4";

        var connectionControls = CreateConnectionControls();
        var raceSettings = CreateRaceSettings();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(connectionControls, 0, 0);
        layout.Controls.Add(raceSettings, 0, 1);
        layout.Controls.Add(logTextBox, 0, 2);
        Controls.Add(layout);

        refreshButton.Click += (_, _) => RefreshPorts();
        connectButton.Click += (_, _) => ToggleConnection();
        pingButton.Click += (_, _) => SendCommand("PING");
        statusButton.Click += (_, _) => SendCommand("STATUS");
        resetButton.Click += (_, _) => SendCommand("RESET");
        applySettingsButton.Click += (_, _) => ApplyRaceSettings();
        modeSelector.SelectedIndexChanged += (_, _) => UpdateDialInputState();
        laneCountSelector.SelectedIndexChanged += (_, _) => UpdateDialInputState();
        client.MessageReceived += (_, message) =>
            PostToUi(() => HandleMessage(message));
        client.ProtocolError += (_, error) =>
            PostToUi(() => AppendLog($"! {error}"));

        RefreshPorts();
        UpdateDialInputState();
    }

    private Control CreateConnectionControls()
    {
        var controls = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Padding = new Padding(8)
        };

        portSelector.Width = 110;
        controls.Controls.AddRange(
            [new Label { AutoSize = true, Text = "Serial port:", Margin = new Padding(3, 8, 3, 3) },
             portSelector,
             refreshButton,
             connectButton,
             pingButton,
             statusButton,
             resetButton,
             connectionLabel]);
        return controls;
    }

    private Control CreateRaceSettings()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Padding = new Padding(8)
        };

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Race mode:",
            Margin = new Padding(3, 8, 3, 3)
        });
        panel.Controls.Add(modeSelector);
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Lanes:",
            Margin = new Padding(10, 8, 3, 3)
        });
        panel.Controls.Add(laneCountSelector);

        for (var lane = 0; lane < LaneCount; lane++)
        {
            panel.Controls.Add(new Label
            {
                AutoSize = true,
                Text = $"Lane {lane + 1}:",
                Margin = new Padding(10, 8, 3, 3)
            });

            var input = new NumericUpDown
            {
                DecimalPlaces = 3,
                Increment = 0.001M,
                Minimum = 0.100M,
                Maximum = 60.000M,
                Value = 10.000M,
                Width = 78,
                Enabled = false
            };
            dialInputs[lane] = input;
            panel.Controls.Add(input);
        }

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "seconds",
            Margin = new Padding(3, 8, 8, 3)
        });
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Track length:",
            Margin = new Padding(10, 8, 3, 3)
        });
        panel.Controls.Add(trackLengthInput);
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "in",
            Margin = new Padding(3, 8, 3, 3)
        });
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Speed trap:",
            Margin = new Padding(10, 8, 3, 3)
        });
        panel.Controls.Add(speedTrapLengthInput);
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "in",
            Margin = new Padding(3, 8, 8, 3)
        });
        panel.Controls.Add(applySettingsButton);
        return panel;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
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

    private void ToggleConnection()
    {
        try
        {
            if (client.IsConnected)
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

    private void ApplyRaceSettings()
    {
        if (modeSelector.SelectedItem is not string mode)
        {
            return;
        }

        if (speedTrapLengthInput.Value >= trackLengthInput.Value)
        {
            MessageBox.Show(
                this,
                "Speed-trap length must be shorter than the track length.",
                "Invalid distances");
            return;
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
    }

    private void HandleMessage(ProtocolMessage message)
    {
        AppendLog($"< {message.Encode()}");

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
        connectionLabel.Text = connected ? "Connected" : "Disconnected";
        connectButton.Text = connected ? "Disconnect" : "Connect";
        portSelector.Enabled = !connected;
        refreshButton.Enabled = !connected;
        pingButton.Enabled = connected;
        statusButton.Enabled = connected;
        resetButton.Enabled = connected;
        modeSelector.Enabled = connected;
        laneCountSelector.Enabled = connected;
        trackLengthInput.Enabled = connected;
        speedTrapLengthInput.Enabled = connected;
        applySettingsButton.Enabled = connected;
        UpdateDialInputState();
    }

    private void UpdateDialInputState()
    {
        var enabled = client.IsConnected &&
            string.Equals(modeSelector.SelectedItem as string, "BRACKET", StringComparison.Ordinal);
        var laneCount = SelectedLaneCount();
        for (var lane = 0; lane < dialInputs.Length; lane++)
        {
            var input = dialInputs[lane];
            if (input is not null)
            {
                input.Enabled = enabled && LaneIsActive(lane, laneCount);
            }
        }
    }

    private int SelectedLaneCount() =>
        int.TryParse(laneCountSelector.SelectedItem as string, out var count) ? count : 4;

    private static bool LaneIsActive(int zeroBasedLane, int laneCount) =>
        laneCount == 4 || zeroBasedLane is 0 or 3;

    private static string ToThousandthsOfAnInch(decimal inches) =>
        decimal.ToInt32(inches * 1000M).ToString(CultureInfo.InvariantCulture);

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
}
