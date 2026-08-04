namespace DragWin;

public sealed class LightTreeTestForm : Form
{
    public const int RequiredProtocolVersion = 8;

    private sealed record LightDefinition(string Name, string ProtocolName, Color Color);

    private static readonly LightDefinition[] Lights =
    [
        new("Pre-Stage", "PRESTAGE", Color.FromArgb(238, 193, 46)),
        new("Stage", "STAGE", Color.FromArgb(238, 193, 46)),
        new("Amber 1", "AMBER_1", Color.FromArgb(220, 126, 25)),
        new("Amber 2", "AMBER_2", Color.FromArgb(220, 126, 25)),
        new("Amber 3", "AMBER_3", Color.FromArgb(220, 126, 25)),
        new("Green", "GREEN", Color.FromArgb(31, 135, 76)),
        new("Red", "RED", Color.FromArgb(185, 45, 50))
    ];

    private readonly DragSerialClient client;
    private readonly ComboBox laneSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 150
    };
    private readonly Label statusLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(500, 0),
        Text = "Starting light test..."
    };
    private readonly Button[] lightButtons = new Button[Lights.Length];
    private readonly bool[] lightStates = new bool[Lights.Length];
    private readonly Button sequenceButton = new()
    {
        Text = "Run Sequence",
        AutoSize = true,
        MinimumSize = new Size(120, 34),
        Enabled = false
    };
    private readonly Button allOffButton = new()
    {
        Text = "All Off",
        AutoSize = true,
        MinimumSize = new Size(90, 34),
        Enabled = false
    };
    private readonly System.Windows.Forms.Timer sequenceTimer = new() { Interval = 450 };
    private bool testActive;
    private bool closing;
    private int sequenceStep = -1;

    public LightTreeTestForm(DragSerialClient client)
    {
        this.client = client;
        Text = "Light Tree Test";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 650);
        Size = new Size(620, 720);

        for (var lane = 1; lane <= 4; lane++)
        {
            laneSelector.Items.Add($"Lane {lane}");
        }
        laneSelector.SelectedIndex = 0;

        Controls.Add(CreateLayout());
        client.MessageReceived += ClientOnMessageReceived;
        laneSelector.SelectedIndexChanged += (_, _) => ChangeLane();
        sequenceButton.Click += (_, _) => StartSequence();
        allOffButton.Click += (_, _) => AllOff();
        sequenceTimer.Tick += (_, _) => AdvanceSequence();
        Shown += (_, _) => SendStart();
        FormClosing += (_, _) => StopTest();
        FormClosed += (_, _) =>
        {
            client.MessageReceived -= ClientOnMessageReceived;
            sequenceTimer.Dispose();
        };
    }

    private Control CreateLayout()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        header.Controls.Add(new Label
        {
            Text = "Test lane:",
            AutoSize = true,
            Margin = new Padding(0, 7, 8, 0)
        });
        header.Controls.Add(laneSelector);
        outer.Controls.Add(header, 0, 0);
        outer.Controls.Add(CreateLightPanel(), 0, 1);
        outer.Controls.Add(CreateFooter(), 0, 2);
        return outer;
    }

    private Control CreateLightPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = Lights.Length,
            Padding = new Padding(6)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        for (var index = 0; index < Lights.Length; index++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / Lights.Length));
            var lightIndex = index;
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Enabled = false,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold),
                Margin = new Padding(8),
                MinimumSize = new Size(270, 54),
                UseVisualStyleBackColor = false
            };
            button.Click += (_, _) => ToggleLight(lightIndex);
            lightButtons[index] = button;
            panel.Controls.Add(button, 1, index);
        }
        RefreshLightButtons();
        return panel;
    }

    private Control CreateFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(statusLabel, 0, 0);
        footer.SetColumnSpan(statusLabel, 2);

        var commands = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 10, 0, 0)
        };
        UiStyles.ConfigurePrimaryButton(sequenceButton, UiStyles.BlueAction);
        commands.Controls.Add(sequenceButton);
        commands.Controls.Add(allOffButton);
        footer.Controls.Add(commands, 0, 1);

        var closeButton = new Button
        {
            Text = "Close",
            AutoSize = true,
            MinimumSize = new Size(90, 34),
            Margin = new Padding(8, 10, 0, 0)
        };
        closeButton.Click += (_, _) => Close();
        footer.Controls.Add(closeButton, 1, 1);
        AcceptButton = sequenceButton;
        CancelButton = closeButton;
        return footer;
    }

    private int SelectedLane => laneSelector.SelectedIndex + 1;

    private void SendStart()
    {
        if (closing || !client.IsConnected)
        {
            SetUnavailable("Serial port disconnected.");
            return;
        }

        try
        {
            client.Send("LIGHT_TEST", "START");
        }
        catch (Exception exception)
        {
            SetUnavailable($"Could not start light test: {exception.Message}");
        }
    }

    private void ToggleLight(int index)
    {
        if (!testActive || sequenceTimer.Enabled) return;
        var newState = !lightStates[index];
        try
        {
            client.Send(
                "LIGHT_TEST", "SET", SelectedLane.ToString(),
                Lights[index].ProtocolName, newState ? "1" : "0");
            lightStates[index] = newState;
            RefreshLightButton(index);
            statusLabel.Text = $"Lane {SelectedLane} {Lights[index].Name} {(newState ? "on" : "off")}.";
        }
        catch (Exception exception)
        {
            statusLabel.Text = $"Could not change light: {exception.Message}";
        }
    }

    private void ChangeLane()
    {
        if (!testActive) return;
        AllOff();
        RefreshLightButtons();
    }

    private void AllOff()
    {
        sequenceTimer.Stop();
        sequenceStep = -1;
        Array.Fill(lightStates, false);
        RefreshLightButtons();
        SetControlsEnabled(testActive);
        try
        {
            if (testActive) client.Send("LIGHT_TEST", "OFF");
            statusLabel.Text = "All tree lights off.";
        }
        catch (Exception exception)
        {
            statusLabel.Text = $"Could not turn lights off: {exception.Message}";
        }
    }

    private void StartSequence()
    {
        if (!testActive) return;
        try
        {
            Array.Fill(lightStates, false);
            RefreshLightButtons();
            client.Send("LIGHT_TEST", "OFF");
            sequenceStep = -1;
            SetControlsEnabled(false);
            sequenceTimer.Start();
            AdvanceSequence();
        }
        catch (Exception exception)
        {
            SetUnavailable($"Could not run sequence: {exception.Message}");
        }
    }

    private void AdvanceSequence()
    {
        try
        {
            if (sequenceStep >= 0 && sequenceStep < Lights.Length)
            {
                client.Send(
                    "LIGHT_TEST", "SET", SelectedLane.ToString(),
                    Lights[sequenceStep].ProtocolName, "0");
                lightStates[sequenceStep] = false;
            }

            sequenceStep++;
            if (sequenceStep >= Lights.Length)
            {
                sequenceTimer.Stop();
                sequenceStep = -1;
                client.Send("LIGHT_TEST", "OFF");
                RefreshLightButtons();
                SetControlsEnabled(true);
                statusLabel.Text = $"Lane {SelectedLane} sequence complete.";
                return;
            }

            client.Send(
                "LIGHT_TEST", "SET", SelectedLane.ToString(),
                Lights[sequenceStep].ProtocolName, "1");
            lightStates[sequenceStep] = true;
            RefreshLightButtons();
            statusLabel.Text = $"Testing lane {SelectedLane}: {Lights[sequenceStep].Name}.";
        }
        catch (Exception exception)
        {
            SetUnavailable($"Sequence stopped: {exception.Message}");
        }
    }

    private void ClientOnMessageReceived(object? sender, ProtocolMessage message)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() => HandleMessage(message));
        }
        catch (InvalidOperationException) when (closing || IsDisposed || Disposing)
        {
            // The dialog closed between checking the handle and dispatching.
        }
    }

    private void HandleMessage(ProtocolMessage message)
    {
        if (closing || IsDisposed) return;
        if (message.Type == "ACK" && message.Parts.Count >= 3 &&
            message.Parts[1] == "LIGHT_TEST" && message.Parts[2] == "START")
        {
            if (!testActive)
            {
                testActive = true;
                SetControlsEnabled(true);
                statusLabel.Text = "Light test ready.";
            }
            return;
        }

        if (message.Type == "ERROR" && message.Parts.Contains("RACE_ACTIVE"))
        {
            SetUnavailable("The controller is staging or running a race. Reset it before testing lights.");
        }
        else if (message.Type == "ERROR" && message.Parts.Contains("LIGHT_TEST_INACTIVE"))
        {
            testActive = false;
            SendStart();
        }
        else if (message.Type == "ERROR" && message.Parts.Count >= 2 &&
                 message.Parts[1] == "COMMAND")
        {
            SetUnavailable("Controller firmware 0.6.4 or newer is required for light testing.");
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        laneSelector.Enabled = enabled;
        sequenceButton.Enabled = enabled;
        allOffButton.Enabled = enabled;
        foreach (var button in lightButtons) button.Enabled = enabled;
    }

    private void SetUnavailable(string message)
    {
        testActive = false;
        sequenceTimer.Stop();
        SetControlsEnabled(false);
        statusLabel.Text = message;
    }

    private void RefreshLightButtons()
    {
        for (var index = 0; index < Lights.Length; index++) RefreshLightButton(index);
    }

    private void RefreshLightButton(int index)
    {
        var definition = Lights[index];
        var pin = 22 + (SelectedLane - 1) * Lights.Length + index;
        var on = lightStates[index];
        var button = lightButtons[index];
        button.Text = $"{definition.Name}  D{pin}  {(on ? "ON" : "OFF")}";
        button.BackColor = on ? definition.Color : Color.FromArgb(242, 243, 245);
        button.ForeColor = on && index >= 5 ? Color.White : Color.FromArgb(35, 39, 43);
        button.FlatAppearance.BorderColor = on
            ? ControlPaint.Dark(definition.Color)
            : Color.FromArgb(155, 160, 166);
        button.FlatAppearance.BorderSize = on ? 2 : 1;
    }

    private void StopTest()
    {
        if (closing) return;
        closing = true;
        sequenceTimer.Stop();
        try
        {
            if (client.IsConnected) client.Send("LIGHT_TEST", "STOP");
        }
        catch
        {
            // The firmware lease still guarantees that test outputs turn off.
        }
    }
}
