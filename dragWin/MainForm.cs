using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DragWin;

public sealed class MainForm : Form
{
    private const int LaneCount = 4;
    private const int MaximumProtocolLogCharacters = 150_000;
    private const int TrimmedProtocolLogCharacters = 120_000;

    private readonly DragSerialClient client = new();
    private readonly RaceRepository raceRepository = new();
    private PassResultsForm? passResultsForm;
    private Form? controllerActivityForm;
    private bool connectionRequested;
    private bool controllerReady;
    private bool mainActionRunning;
    private DateTimeOffset lastMainButtonActionAt;
    private DateTimeOffset? autoConnectDeadline;
    private string? rememberedControllerPortThisSession;
    private AppSettings persistedSettings = AppSettingsStore.Load();
    private bool savedSettingsAppliedToController;
    private readonly ToolStripMenuItem configureRaceMenuItem = new("Race and track settings...");
    private readonly ToolStripMenuItem updateControllerFirmwareMenuItem = new("Update Controller Firmware...");
    private readonly ToolStripMenuItem backupDatabaseMenuItem = new("Back Up Database...");
    private readonly ToolStripMenuItem restoreDatabaseMenuItem = new("Restore Database...");
    private readonly ToolStripMenuItem openDatabaseFolderMenuItem = new("Open Database Folder");
    private readonly ToolStripMenuItem openBackupFolderMenuItem = new("Open Backup Folder");
    private readonly ToolStripMenuItem pingMenuItem = new("Ping controller") { Enabled = false };
    private readonly ToolStripMenuItem statusMenuItem = new("Request controller status") { Enabled = false };
    private readonly ToolStripMenuItem resetMenuItem = new("Reset controller") { Enabled = false };
    private readonly ToolStripMenuItem testSensorsMenuItem = new("Sensor test...") { Enabled = false };
    private readonly ToolStripMenuItem protocolLogMenuItem = new("Controller activity...");
    private readonly ToolStripMenuItem demoPracticeMenuItem = new("Generate demo practice run");
    private readonly ComboBox portSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill,
        MinimumSize = new Size(130, 0)
    };
    private readonly Button refreshButton = new()
    {
        Text = "Refresh Ports",
        AutoSize = true,
        MinimumSize = new Size(100, 30)
    };
    private readonly Button connectButton = new()
    {
        Text = "Connect",
        AutoSize = true,
        MinimumSize = new Size(105, 30),
        BackColor = Color.FromArgb(35, 91, 145),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button tournamentButton = new()
    {
        Text = "New Tournament",
        AutoSize = true,
        MinimumSize = new Size(125, 32)
    };
    private readonly ComboBox tournamentSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill,
        MinimumSize = new Size(180, 0),
        DisplayMember = nameof(Tournament.Name)
    };
    private readonly Button runTournamentButton = new()
    {
        Text = "Run / Resume Tournament",
        AutoSize = true,
        MinimumSize = new Size(180, 32),
        BackColor = Color.FromArgb(39, 122, 79),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Label connectionLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Text = "Disconnected",
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(10, 0, 10, 0),
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
    };
    private readonly Label settingsSummaryLabel = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText
    };
    private readonly Label versionLabel = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(1, 0, 0, 0)
    };
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
    private readonly ComboBox treeModeSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 70,
        Enabled = false
    };
    private readonly ComboBox stagingModeSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Enabled = false
    };
    private readonly NumericUpDown stagedDelayInput = new()
    {
        DecimalPlaces = 3,
        Increment = 0.050M,
        Minimum = 0M,
        Maximum = 5.000M,
        Value = 0.500M,
        Width = 78,
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
    private readonly Button startPracticeButton = new()
    {
        Text = "Arm Practice Run",
        AutoSize = true,
        Enabled = false,
        MinimumSize = new Size(150, 32),
        BackColor = Color.FromArgb(35, 91, 145),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly System.Windows.Forms.Timer heartbeatTimer = new()
    {
        Interval = 1000
    };
    private readonly StringBuilder protocolLogBuffer = new();
    private readonly List<string> activityEntries = [];
    private int diagnosticsVersion;
    private bool firmwareUpdateActive;

    public MainForm()
    {
        string displayVersion = BuildIdentity.Current;
        Text = $"Drag Strip Controller {displayVersion}";
        versionLabel.Text = displayVersion;
        MinimumSize = new Size(900, 430);
        Size = new Size(1100, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Window;

        modeSelector.Items.AddRange(["HEADS_UP", "BRACKET"]);
        modeSelector.SelectedItem = "BRACKET";
        modeSelector.Enabled = true;
        laneCountSelector.Items.AddRange(["2", "4"]);
        laneCountSelector.SelectedItem = "4";
        laneCountSelector.Enabled = true;
        treeModeSelector.Items.AddRange(["FULL", "PRO"]);
        treeModeSelector.SelectedItem = "FULL";
        treeModeSelector.Enabled = true;
        stagingModeSelector.Items.AddRange(["BOTH_BLOCKED", "IN_ORDER"]);
        stagingModeSelector.SelectedItem = "BOTH_BLOCKED";
        stagingModeSelector.Enabled = true;
        InitializeDialInputs();
        LoadPersistedSettingsIntoControls();
        if (persistedSettings.VoiceAnnouncementsEnabled)
        {
            SpeechAnnouncer.WarmUpAsync(persistedSettings.SpeechVoiceName);
        }
        UiStyles.ConfigurePrimaryButton(startPracticeButton, UiStyles.BlueAction);
        UiStyles.ConfigurePrimaryButton(runTournamentButton, UiStyles.GreenAction);
        connectButton.UseVisualStyleBackColor = false;
        connectButton.EnabledChanged += (_, _) => UpdateConnectButtonAppearance();
        UpdateConnectButtonAppearance();

        var menuStrip = CreateMenuStrip();
        var title = CreateTitleBar();
        var connectionControls = CreateConnectionControls();
        var operations = CreateOperationsPanel();
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
        layout.Controls.Add(title, 0, 1);
        layout.Controls.Add(connectionControls, 0, 2);
        layout.Controls.Add(operations, 0, 3);
        Controls.Add(layout);
        MainMenuStrip = menuStrip;

        refreshButton.Click += (_, _) => RunMainButtonAction(refreshButton, RefreshPorts);
        connectButton.Click += (_, _) => RunMainButtonAction(connectButton, ToggleConnection);
        tournamentButton.Click += (_, _) => RunMainButtonAction(tournamentButton, () =>
        {
            new TournamentSetupForm(raceRepository).ShowDialog(this);
            RefreshTournaments();
        });
        runTournamentButton.Click += (_, _) => RunMainButtonAction(runTournamentButton, RunSelectedTournament);
        startPracticeButton.Click += (_, _) => RunMainButtonAction(startPracticeButton, StartPracticeSetup);
        configureRaceMenuItem.Click += (_, _) => ShowRaceSettings();
        updateControllerFirmwareMenuItem.Click += async (_, _) =>
            await UpdateControllerFirmwareAsync();
        backupDatabaseMenuItem.Click += (_, _) => BackUpDatabase();
        restoreDatabaseMenuItem.Click += (_, _) => RestoreDatabase();
        openDatabaseFolderMenuItem.Click += (_, _) => OpenDatabaseFolder();
        openBackupFolderMenuItem.Click += (_, _) => OpenBackupFolder();
        pingMenuItem.Click += (_, _) => SendCommand("PING");
        statusMenuItem.Click += (_, _) => SendCommand("STATUS");
        resetMenuItem.Click += (_, _) => ResetController();
        testSensorsMenuItem.Click += (_, _) => ShowSensorTest();
        protocolLogMenuItem.Click += (_, _) => ShowControllerActivity();
        demoPracticeMenuItem.Click += (_, _) => DemoPracticeRun();
        modeSelector.SelectedIndexChanged += (_, _) =>
        {
            UpdateDialInputState();
            UpdateSettingsSummary();
        };
        laneCountSelector.SelectedIndexChanged += (_, _) =>
        {
            UpdateDialInputState();
            UpdateSettingsSummary();
        };
        treeModeSelector.SelectedIndexChanged += (_, _) => UpdateSettingsSummary();
        client.MessageReceived += (_, message) =>
            PostToUi(() => HandleMessage(message));
        client.ProtocolError += (_, error) =>
            PostToUi(() => AppendLog($"! {error}"));
        heartbeatTimer.Tick += (_, _) =>
        {
            UpdateConnectionLabel();
            CheckAutoConnectAttempt();
        };
        heartbeatTimer.Start();
        Shown += (_, _) =>
        {
            BeginInvoke(TryAutoConnect);
            BeginInvoke(CreateAutomaticDatabaseBackup);
        };

        RefreshPorts();
        RefreshTournaments();
        UpdateDialInputState();
        UpdateSettingsSummary();
        UpdateConnectionLabel();
    }

    private MenuStrip CreateMenuStrip()
    {
        var menuStrip = new MenuStrip { Dock = DockStyle.Top };
        var fileMenu = new ToolStripMenuItem("File");
        var exitMenuItem = new ToolStripMenuItem("Exit");
        exitMenuItem.Click += (_, _) => Close();
        fileMenu.DropDownItems.AddRange([
            updateControllerFirmwareMenuItem,
            new ToolStripSeparator(),
            backupDatabaseMenuItem,
            restoreDatabaseMenuItem,
            new ToolStripSeparator(),
            openDatabaseFolderMenuItem,
            openBackupFolderMenuItem,
            new ToolStripSeparator(),
            exitMenuItem
        ]);
        var configureMenu = new ToolStripMenuItem("Configure");
        configureMenu.DropDownItems.Add(configureRaceMenuItem);
        var diagnosticsMenu = new ToolStripMenuItem("Diagnostics");
        diagnosticsMenu.DropDownItems.AddRange([
            pingMenuItem,
            statusMenuItem,
            new ToolStripSeparator(),
            testSensorsMenuItem,
            resetMenuItem,
            new ToolStripSeparator(),
            protocolLogMenuItem
        ]);
        var testMenu = new ToolStripMenuItem("Test");
        testMenu.DropDownItems.Add(demoPracticeMenuItem);
        menuStrip.Items.AddRange([fileMenu, configureMenu, diagnosticsMenu, testMenu]);
        return menuStrip;
    }

    private async Task UpdateControllerFirmwareAsync()
    {
        if (!CanUpdateControllerFirmware(out var reason))
        {
            MessageBox.Show(
                this, reason, "Controller Firmware", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ControllerFirmwarePackage package;
        AvrDudeTool? tool;
        var provider = new AvrDudeProvider();
        try
        {
            var packages = ControllerFirmwarePackage.LoadBundledPackages();
            package = packages.Count switch
            {
                0 => throw new FileNotFoundException(
                    "The bundled DragMC firmware package is missing from this installation."),
                1 => packages[0],
                _ => throw new InvalidDataException(
                    "This installation contains more than one DragMC Mega firmware package.")
            };
            tool = provider.FindExisting();
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this, exception.Message, "Controller Firmware", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var portName = (string)portSelector.SelectedItem!;
        var identity = client.CurrentControllerIdentity;
        if (identity is not null &&
            (!string.Equals(identity.Product, ControllerFirmwarePackage.ProductName, StringComparison.Ordinal) ||
             !string.Equals(identity.Mcu, "MEGA2560", StringComparison.Ordinal)))
        {
            MessageBox.Show(
                this,
                $"The selected controller identifies itself as {identity.Product} on {identity.Mcu}, " +
                "not DragMC on a Mega 2560.",
                "Controller Firmware",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var identityText = identity is null
            ? "DragWin cannot identify the selected board. Continue only after physically confirming " +
              "that it is an Arduino Mega 2560 or compatible clone with a working bootloader."
            : $"Connected controller: {identity.Product} {identity.FirmwareVersion}, " +
              $"protocol {identity.ProtocolVersion}, {identity.Mcu}.";
        var toolText = tool is null
            ? "DragWin will download and verify the official Arduino avrdude uploader before releasing the COM port."
            : $"Uploader: avrdude {tool.Version} from {tool.Source}.";
        var confirmation =
            $"Install DragMC {package.Manifest.FirmwareVersion} for " +
            $"{package.Manifest.BoardDisplayName} on {portName}?{Environment.NewLine}{Environment.NewLine}" +
            identityText + Environment.NewLine + Environment.NewLine +
            toolText + Environment.NewLine + Environment.NewLine +
            "Controller outputs will be unavailable during the update. Do not disconnect USB or close DragWin. " +
            "This updater cannot repair a missing or damaged bootloader.";
        if (MessageBox.Show(
                this,
                confirmation,
                "Update Controller Firmware?",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.OK)
        {
            return;
        }

        controllerActivityForm?.Close();
        using var progressForm = new FirmwareUpdateProgressForm { Icon = Icon };
        progressForm.Show(this);
        Enabled = false;
        firmwareUpdateActive = true;
        var serialWasConnected = client.IsConnected || connectionRequested;
        var serialReleased = false;
        var uploadSucceeded = false;
        var verified = false;
        Exception? failure = null;
        try
        {
            var progress = progressForm.CreateProgress();
            progress.Report($"Package: {Path.GetFileName(package.PackagePath)}");
            progress.Report($"Image SHA-256: {package.Manifest.Sha256}");
            if (tool is null)
            {
                progressForm.SetStatus("Downloading Arduino uploader...");
                tool = await provider.DownloadOfficialAsync(progress);
            }

            progressForm.SetStatus("Releasing controller serial port...");
            client.Disconnect();
            SetConnectedState(false);
            serialReleased = true;
            await Task.Delay(500);

            progressForm.SetStatus("Writing and verifying controller firmware...");
            var flasher = new ArduinoMegaFirmwareFlasher(tool);
            await flasher.FlashAsync(package, portName, progress);
            uploadSucceeded = true;

            progressForm.SetStatus("Firmware written; waiting for DragMC identity...");
            verified = await ReconnectAndVerifyFirmwareAsync(
                portName,
                package.Manifest.FirmwareVersion,
                progress);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException or
                UnauthorizedAccessException or HttpRequestException or
                System.Security.Cryptography.CryptographicException or TimeoutException)
        {
            failure = exception;
        }
        finally
        {
            if (serialReleased && !client.IsConnected && (serialWasConnected || uploadSucceeded))
            {
                try
                {
                    client.Connect(portName);
                    SetConnectedState(true);
                }
                catch (Exception reconnectException) when (
                    reconnectException is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    AppendLog($"Firmware update reconnect failed: {reconnectException.Message}");
                }
            }
            firmwareUpdateActive = false;
            progressForm.Complete();
            progressForm.Close();
            Enabled = true;
            Activate();
        }

        if (failure is not null)
        {
            AppendLog($"Firmware update failed: {failure.Message}");
            MessageBox.Show(
                this,
                failure.Message,
                "Controller Firmware Update Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }
        if (!verified)
        {
            var received = client.CurrentControllerIdentity;
            var detail = received is null
                ? "No controller identity was received."
                : $"Received {received.Product} {received.FirmwareVersion} on {received.Mcu}.";
            MessageBox.Show(
                this,
                "The firmware was written and verified by avrdude, but DragWin did not receive the " +
                $"expected DragMC identity within 20 seconds. {detail}",
                "Controller Firmware Written",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        AppendLog($"Controller firmware updated to DragMC {package.Manifest.FirmwareVersion}.");
        MessageBox.Show(
            this,
            $"The controller is running DragMC {package.Manifest.FirmwareVersion}.",
            "Controller Firmware Updated",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private bool CanUpdateControllerFirmware(out string reason)
    {
        if (firmwareUpdateActive)
        {
            reason = "A controller firmware update is already running.";
            return false;
        }
        if (mainActionRunning)
        {
            reason = "Wait for the current DragWin operation to finish.";
            return false;
        }
        if (passResultsForm is { IsDisposed: false })
        {
            reason = "Close Practice Pass Results before updating controller firmware.";
            return false;
        }
        if (portSelector.SelectedItem is not string)
        {
            reason = "Select the Mega's COM port before updating controller firmware.";
            return false;
        }

        reason = "";
        return true;
    }

    private async Task<bool> ReconnectAndVerifyFirmwareAsync(
        string portName,
        string expectedVersion,
        IProgress<string> progress)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(20);
        while (DateTimeOffset.Now < deadline)
        {
            if (!client.IsConnected)
            {
                try
                {
                    client.Connect(portName);
                    SetConnectedState(true);
                    progress.Report($"Reconnected to {portName}; requesting controller identity...");
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    progress.Report($"Waiting for {portName}: {exception.Message}");
                    await Task.Delay(500);
                    continue;
                }
            }

            try
            {
                client.Send("IDENTIFY");
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or TimeoutException)
            {
                progress.Report($"Identity request failed: {exception.Message}");
            }
            await Task.Delay(350);
            if (client.CurrentControllerIdentity?.IsExpectedDragMc(expectedVersion) == true)
            {
                return true;
            }
        }
        return false;
    }

    private void BackUpDatabase()
    {
        try
        {
            var backupDirectory = RaceRepository.GetDefaultBackupDirectory();
            Directory.CreateDirectory(backupDirectory);

            using var dialog = new SaveFileDialog
            {
                Title = "Back Up dragWin Database",
                InitialDirectory = backupDirectory,
                FileName = $"dragWin-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db",
                Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
                DefaultExt = "db",
                AddExtension = true,
                OverwritePrompt = true,
                RestoreDirectory = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            UseWaitCursor = true;
            var result = raceRepository.CreateBackup(dialog.FileName);
            MessageBox.Show(
                this,
                $"Database backup verified and saved.\n\n" +
                $"Racers: {result.RacerCount}\n" +
                $"Cars: {result.CarCount}\n" +
                $"Tournaments: {result.TournamentCount}\n\n" +
                result.Path,
                "Backup Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Database Backup Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void OpenDatabaseFolder()
    {
        var directory = Path.GetDirectoryName(raceRepository.DatabasePath)
            ?? throw new InvalidOperationException("The database folder could not be found.");
        OpenFolder(directory, "Database Folder Could Not Be Opened");
    }

    private void OpenBackupFolder()
    {
        OpenFolder(
            RaceRepository.GetDefaultBackupDirectory(),
            "Backup Folder Could Not Be Opened");
    }

    private void RestoreDatabase()
    {
        try
        {
            var backupDirectory = RaceRepository.GetDefaultBackupDirectory();
            Directory.CreateDirectory(backupDirectory);

            using var dialog = new OpenFileDialog
            {
                Title = "Restore dragWin Database",
                InitialDirectory = backupDirectory,
                Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var confirmation = MessageBox.Show(
                this,
                "Restore the selected database?\n\n" +
                "The current database will be backed up automatically before it is replaced.",
                "Confirm Database Restore",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            var safetyBackupPath = Path.Combine(
                backupDirectory,
                $"dragWin-before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            UseWaitCursor = true;
            var result = raceRepository.RestoreBackup(dialog.FileName, safetyBackupPath);
            RefreshTournaments();

            MessageBox.Show(
                this,
                $"Database restored and verified.\n\n" +
                $"Racers: {result.RacerCount}\n" +
                $"Cars: {result.CarCount}\n" +
                $"Tournaments: {result.TournamentCount}\n\n" +
                $"Previous database saved to:\n{result.SafetyBackupPath}",
                "Restore Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Database Restore Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void CreateAutomaticDatabaseBackup()
    {
        try
        {
            _ = raceRepository.CreateAutomaticBackup();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "dragWin could not create today's automatic database backup.\n\n" +
                exception.Message,
                "Automatic Backup Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void OpenFolder(string directory, string errorTitle)
    {
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                errorTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private Control CreateTitleBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(16, 12, 16, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = "Race Control",
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 45, 55)
        }, 0, 0);
        panel.Controls.Add(settingsSummaryLabel, 1, 0);
        panel.Controls.Add(versionLabel, 0, 1);
        return panel;
    }

    private Control CreateConnectionControls()
    {
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            Padding = new Padding(16, 8, 16, 10),
            BackColor = Color.FromArgb(242, 244, 246)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Controller port",
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 10, 3)
        }, 0, 0);
        layout.Controls.Add(portSelector, 1, 0);
        layout.Controls.Add(refreshButton, 2, 0);
        layout.Controls.Add(connectButton, 3, 0);
        layout.Controls.Add(connectionLabel, 4, 0);
        return layout;
    }

    private void InitializeDialInputs()
    {
        for (var lane = 0; lane < LaneCount; lane++)
        {
            dialInputs[lane] = new NumericUpDown
            {
                DecimalPlaces = 3,
                Increment = 0.001M,
                Minimum = 0.100M,
                Maximum = 60.000M,
                Value = 10.000M,
                Width = 90
            };
        }
    }

    private Control CreateOperationsPanel()
    {
        var practicePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(16, 12, 20, 14)
        };
        practicePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        practicePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        practicePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        practicePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        practicePanel.Controls.Add(CreateSectionHeading("Practice Racing", Color.FromArgb(35, 91, 145)), 0, 0);
        practicePanel.Controls.Add(new Label
        {
            Text = "Active lanes",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 3)
        }, 0, 1);
        var lanePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        for (var lane = 0; lane < LaneCount; lane++)
        {
            var check = new CheckBox
            {
                AutoSize = true,
                Checked = persistedSettings.PracticeLanes.Contains(lane + 1),
                Enabled = false,
                Margin = new Padding(0, 5, 18, 5),
                Text = $"Lane {lane + 1}"
            };
            toolTip.SetToolTip(check, "Select the lanes that must stage for the next practice run.");
            practiceLaneChecks[lane] = check;
            lanePanel.Controls.Add(check);
        }
        practicePanel.Controls.Add(lanePanel, 0, 2);
        practicePanel.Controls.Add(startPracticeButton, 0, 3);

        var tournamentPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            RowCount = 4,
            ColumnCount = 2,
            Padding = new Padding(20, 12, 16, 14),
            BackColor = Color.FromArgb(248, 249, 250)
        };
        tournamentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tournamentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tournamentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tournamentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tournamentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tournamentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var tournamentHeading = CreateSectionHeading("Tournament Racing", Color.FromArgb(39, 122, 79));
        tournamentPanel.Controls.Add(tournamentHeading, 0, 0);
        tournamentPanel.SetColumnSpan(tournamentHeading, 2);
        tournamentPanel.Controls.Add(new Label
        {
            Text = "Active tournament",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 3)
        }, 0, 1);
        tournamentPanel.SetColumnSpan(tournamentPanel.GetControlFromPosition(0, 1)!, 2);
        tournamentPanel.Controls.Add(tournamentSelector, 0, 2);
        tournamentPanel.Controls.Add(tournamentButton, 1, 2);
        tournamentPanel.Controls.Add(runTournamentButton, 0, 3);
        tournamentPanel.SetColumnSpan(runTournamentButton, 2);

        var operations = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2
        };
        operations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        operations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        operations.Controls.Add(practicePanel, 0, 0);
        operations.Controls.Add(tournamentPanel, 1, 0);
        return operations;
    }

    private static Label CreateSectionHeading(string text, Color color) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(SystemFonts.DefaultFont.FontFamily, 12, FontStyle.Bold),
        ForeColor = color,
        Margin = new Padding(0)
    };

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveCurrentSettings();
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

    private void TryAutoConnect()
    {
        if (connectionRequested)
        {
            return;
        }

        var ports = portSelector.Items.Cast<string>().ToArray();
        var rememberedPort = ReadRememberedControllerPort();
        var candidate = rememberedPort is not null
            ? ports.FirstOrDefault(port => string.Equals(port, rememberedPort, StringComparison.OrdinalIgnoreCase))
            : null;
        candidate ??= ports.Length == 1 ? ports[0] : null;
        if (candidate is null)
        {
            if (ports.Length > 1)
            {
                AppendLog("Multiple serial ports found. Select the controller port to connect.");
            }
            return;
        }

        portSelector.SelectedItem = candidate;
        AppendLog($"Trying controller on {candidate}...");
        ToggleConnection();
        if (client.IsConnected)
        {
            autoConnectDeadline = DateTimeOffset.Now.AddSeconds(6);
        }
    }

    private void CheckAutoConnectAttempt()
    {
        if (autoConnectDeadline is not { } deadline || DateTimeOffset.Now < deadline)
        {
            return;
        }

        autoConnectDeadline = null;
        if (client.LastHelloReceivedAt.HasValue || client.LastHeartbeatReceivedAt.HasValue)
        {
            RememberConnectedControllerPort();
            return;
        }

        var port = portSelector.SelectedItem as string ?? "selected port";
        client.Disconnect();
        SetConnectedState(false);
        AppendLog($"No controller response on {port}; automatic connection stopped.");
    }

    private void RememberConnectedControllerPort()
    {
        if (portSelector.SelectedItem is not string portName ||
            string.Equals(portName, rememberedControllerPortThisSession, StringComparison.OrdinalIgnoreCase))
        {
            autoConnectDeadline = null;
            return;
        }

        autoConnectDeadline = null;
        try
        {
            var path = ControllerPortPreferencePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, portName);
            rememberedControllerPortThisSession = portName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppendLog($"Could not remember controller port: {exception.Message}");
        }
    }

    private static string? ReadRememberedControllerPort()
    {
        try
        {
            var path = ControllerPortPreferencePath();
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ControllerPortPreferencePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DragWin",
        "controller-port.txt");

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
        runTournamentButton.Enabled = tournamentSelector.Items.Count > 0;
    }

    private void RunSelectedTournament()
    {
        if (tournamentSelector.SelectedItem is not Tournament tournament)
        {
            MessageBox.Show(this, "Create or select a tournament first.", Text);
            return;
        }
        new TournamentRunnerForm(
            tournament,
            raceRepository,
            client,
            decimal.ToInt32(stagedDelayInput.Value * 1000M),
            stagingModeSelector.SelectedItem as string ?? "BOTH_BLOCKED",
            persistedSettings.IntervalTimerLanes,
            persistedSettings.VoiceAnnouncementsEnabled,
            persistedSettings.SpeechVoiceName,
            new TournamentReportExportOptions(
                persistedSettings.ExportTournamentJson,
                persistedSettings.ExportTournamentCsv)).ShowDialog(this);
        RefreshTournaments();
    }

    private void ToggleConnection()
    {
        try
        {
            if (connectionRequested)
            {
                autoConnectDeadline = null;
                client.Disconnect();
                SetConnectedState(false);
                AppendLog("Controller disconnected.");
                return;
            }

            if (portSelector.SelectedItem is not string portName)
            {
                MessageBox.Show(this, "Select a serial port first.", Text);
                return;
            }

            client.Connect(portName);
            savedSettingsAppliedToController = false;
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
        return TryBuildRaceSettingsCommands(out var commands) &&
               SendCommandBatch(commands);
    }

    private bool TryBuildRaceSettingsCommands(out List<string[]> commands)
    {
        commands = [];
        if (modeSelector.SelectedItem is not string mode ||
            treeModeSelector.SelectedItem is not string treeMode)
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
        commands.Add(["SET", "LANES", laneCount.ToString(CultureInfo.InvariantCulture)]);
        commands.Add(["SET", "MODE", mode]);
        commands.Add(["SET", "TREE", treeMode]);
        commands.Add([
            "SET", "STAGING_MODE",
            stagingModeSelector.SelectedItem as string ?? "BOTH_BLOCKED"]);
        commands.Add([
            "SET", "STAGED_DELAY",
            decimal.ToInt32(stagedDelayInput.Value * 1000M)
                .ToString(CultureInfo.InvariantCulture)]);
        commands.Add([
            "SET", "DISTANCES",
            ToThousandthsOfAnInch(trackLengthInput.Value),
            ToThousandthsOfAnInch(speedTrapLengthInput.Value)]);
        commands.Add([
            "SET", "INTERVAL_LANES",
            persistedSettings.IntervalTimerLanes.Length == 0
                ? "NONE"
                : string.Join(',', persistedSettings.IntervalTimerLanes)]);
        for (var lane = 0; lane < LaneCount; lane++)
        {
            if (!LaneIsActive(lane, laneCount))
            {
                continue;
            }

            var dialMilliseconds = decimal.ToInt32(dialInputs[lane].Value * 1000M);
            commands.Add([
                "SET", "DIAL",
                (lane + 1).ToString(CultureInfo.InvariantCulture),
                dialMilliseconds.ToString(CultureInfo.InvariantCulture)]);
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

        if (!TryBuildRaceSettingsCommands(out var commands))
        {
            return;
        }
        commands.Add(["SET", "HEAT_LANES", string.Join(',', selectedLanes)]);
        commands.Add(["RESET"]);
        if (!SendCommandBatch(commands))
        {
            return;
        }
        SaveCurrentSettings();
        BeginPracticePassResults(selectedLanes);
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
            bracketMode,
            splitSensorLanes: persistedSettings.IntervalTimerLanes).ToArray();
        var resultsForm = BeginPracticePassResults(selectedLanes);
        resultsForm.ProcessMessages(messages);

        AppendLog($"DEMO: Practice run started for lane(s): {string.Join(", ", selectedLanes)}.");
        foreach (var message in messages)
        {
            AppendLog($"DEMO < {message.Encode()}");
        }

        AppendPracticeSummary(messages);
    }

    private PassResultsForm BeginPracticePassResults(IReadOnlyCollection<int> lanes)
    {
        if (passResultsForm is null || passResultsForm.IsDisposed)
        {
            passResultsForm = new PassResultsForm(
                client,
                persistedSettings.VoiceAnnouncementsEnabled,
                persistedSettings.SpeechVoiceName);
            passResultsForm.FormClosed += (_, _) => passResultsForm = null;
            passResultsForm.Show(this);
        }
        else
        {
            passResultsForm.BringToFront();
        }

        passResultsForm.UpdateAnnouncementSettings(
            persistedSettings.VoiceAnnouncementsEnabled,
            persistedSettings.SpeechVoiceName);
        passResultsForm.BeginPass(lanes, persistedSettings.IntervalTimerLanes);
        return passResultsForm;
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
                else if (message.Type == "RESULT" && kind == "INTERVAL_1_US" &&
                         message.Parts.Count > 4 &&
                         long.TryParse(message.Parts[4], out var interval1Us))
                {
                    result.Interval1Us = interval1Us;
                }
                else if (message.Type == "RESULT" && kind == "INTERVAL_2_US" &&
                         message.Parts.Count > 4 &&
                         long.TryParse(message.Parts[4], out var interval2Us))
                {
                    result.Interval2Us = interval2Us;
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

    private void ShowRaceSettings()
    {
        using var dialog = new RaceSettingsForm(
            modeSelector.SelectedItem as string ?? "BRACKET",
            SelectedLaneCount(),
            treeModeSelector.SelectedItem as string ?? "FULL",
            stagingModeSelector.SelectedItem as string ?? "BOTH_BLOCKED",
            stagedDelayInput.Value,
            dialInputs.Select(input => input.Value).ToArray(),
            trackLengthInput.Value,
            speedTrapLengthInput.Value,
            persistedSettings.IntervalTimerLanes,
            persistedSettings.VoiceAnnouncementsEnabled,
            persistedSettings.SpeechVoiceName,
            persistedSettings.ExportTournamentJson,
            persistedSettings.ExportTournamentCsv,
            client.IsConnected);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        modeSelector.SelectedItem = dialog.RaceMode;
        laneCountSelector.SelectedItem = dialog.LaneCount.ToString(CultureInfo.InvariantCulture);
        treeModeSelector.SelectedItem = dialog.TreeMode;
        stagingModeSelector.SelectedItem = dialog.StagingMode;
        stagedDelayInput.Value = dialog.StagedDelaySeconds;
        trackLengthInput.Value = dialog.TrackLengthInches;
        speedTrapLengthInput.Value = dialog.SpeedTrapLengthInches;
        persistedSettings.ExportTournamentJson = dialog.ExportTournamentJson;
        persistedSettings.ExportTournamentCsv = dialog.ExportTournamentCsv;
        persistedSettings.IntervalTimerLanes = dialog.IntervalTimerLanes.ToArray();
        persistedSettings.VoiceAnnouncementsEnabled = dialog.VoiceAnnouncementsEnabled;
        persistedSettings.SpeechVoiceName = dialog.SpeechVoiceName;
        for (var lane = 0; lane < LaneCount; lane++)
        {
            dialInputs[lane].Value = dialog.DialSeconds[lane];
        }

        UpdateDialInputState();
        SaveCurrentSettings();
        if (persistedSettings.VoiceAnnouncementsEnabled)
        {
            SpeechAnnouncer.WarmUpAsync(persistedSettings.SpeechVoiceName);
        }
        UpdateSettingsSummary();
        AppendLog(
            $"Settings saved: {dialog.LaneCount} lanes, {dialog.RaceMode}, " +
            $"{dialog.TreeMode} Tree, {StagingModeLabel(dialog.StagingMode)}, " +
            $"{dialog.StagedDelaySeconds:0.000}s staged delay.");
        if (client.IsConnected)
        {
            ApplyRaceSettings();
        }
    }

    private void ResetController()
    {
        if (MessageBox.Show(
                this,
                "Reset the controller and clear its current race state?",
                "Reset controller",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes)
        {
            SendCommand("RESET");
        }
    }

    private void HandleMessage(ProtocolMessage message)
    {
        MarkControllerReady();
        if (message.Type == "HEARTBEAT")
        {
            ApplySavedSettingsAfterConnect(message);
            UpdateConnectionLabel();
            return;
        }

        AppendLog($"< {message.Encode()}");

        if (message.Type == "HELLO")
        {
            savedSettingsAppliedToController = false;
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
            UpdateSettingsSummary();
            return;
        }

        if (message.Parts.Count >= 6 && message.Parts[1] == "SETTINGS")
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 2; index + 1 < message.Parts.Count; index += 2)
            {
                fields[message.Parts[index]] = message.Parts[index + 1];
            }
            if (fields.TryGetValue("TREE", out var treeMode))
            {
                treeModeSelector.SelectedItem = treeMode;
            }
            if (fields.TryGetValue("STAGED_DELAY_MS", out var stagedDelayText) &&
                int.TryParse(stagedDelayText, out var stagedDelayMs))
            {
                stagedDelayInput.Value = Math.Clamp(
                    stagedDelayMs / 1000M,
                    stagedDelayInput.Minimum,
                    stagedDelayInput.Maximum);
            }
            if (fields.TryGetValue("STAGING_MODE", out var stagingMode) &&
                stagingModeSelector.Items.Contains(stagingMode))
            {
                stagingModeSelector.SelectedItem = stagingMode;
            }
            UpdateSettingsSummary();
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

    private void ShowSensorTest()
    {
        if (!client.IsConnected)
        {
            MessageBox.Show(
                this,
                "Connect to the Mega before opening the sensor test.",
                "Sensor Test",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var form = new SensorTestForm(client);
        form.ShowDialog(this);
    }

    private void ShowControllerActivity()
    {
        if (controllerActivityForm is { IsDisposed: false })
        {
            controllerActivityForm.BringToFront();
            return;
        }

        var dialog = new Form
        {
            Text = "Controller Diagnostics",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(760, 480),
            Size = new Size(900, 620)
        };
        controllerActivityForm = dialog;
        var activityViewer = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        var protocolViewer = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9)
        };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var activityTab = new TabPage("Activity");
        var protocolTab = new TabPage("Protocol");
        activityTab.Controls.Add(activityViewer);
        protocolTab.Controls.Add(protocolViewer);
        tabs.TabPages.AddRange([activityTab, protocolTab]);

        var clearButton = new Button { Text = "Clear Current", AutoSize = true, MinimumSize = new Size(100, 30) };
        var closeButton = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(80, 30) };
        clearButton.Click += (_, _) =>
        {
            if (tabs.SelectedTab == activityTab)
            {
                activityEntries.Clear();
            }
            else
            {
                protocolLogBuffer.Clear();
            }
            diagnosticsVersion++;
        };
        closeButton.Click += (_, _) => dialog.Close();
        var pathLabel = new Label
        {
            Text = client.LogPath,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(pathLabel, 0, 0);
        footer.Controls.Add(clearButton, 1, 0);
        footer.Controls.Add(closeButton, 2, 0);

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
        dialog.Controls.Add(layout);
        dialog.CancelButton = closeButton;

        var displayedActivityVersion = -1;
        var displayedProtocolVersion = -1;
        void RefreshViewers()
        {
            if (tabs.SelectedTab == activityTab)
            {
                if (displayedActivityVersion == diagnosticsVersion)
                {
                    return;
                }

                activityViewer.BeginUpdate();
                activityViewer.Items.Clear();
                activityViewer.Items.AddRange(activityEntries.Cast<object>().ToArray());
                activityViewer.EndUpdate();
                if (activityViewer.Items.Count > 0)
                {
                    activityViewer.TopIndex = activityViewer.Items.Count - 1;
                }
                displayedActivityVersion = diagnosticsVersion;
                return;
            }

            if (displayedProtocolVersion == diagnosticsVersion)
            {
                return;
            }

            var atProtocolEnd = protocolViewer.SelectionStart >= protocolViewer.TextLength - 1;
            protocolViewer.Text = protocolLogBuffer.ToString();
            if (atProtocolEnd)
            {
                protocolViewer.SelectionStart = protocolViewer.TextLength;
                protocolViewer.ScrollToCaret();
            }
            displayedProtocolVersion = diagnosticsVersion;
        }

        var refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        refreshTimer.Tick += (_, _) => RefreshViewers();
        tabs.SelectedIndexChanged += (_, _) => RefreshViewers();
        dialog.FormClosed += (_, _) =>
        {
            refreshTimer.Stop();
            refreshTimer.Dispose();
            controllerActivityForm = null;
        };
        RefreshViewers();
        refreshTimer.Start();
        dialog.Show(this);
    }

    private void SetConnectedState(bool connected)
    {
        connectionRequested = connected;
        if (!connected)
        {
            controllerReady = false;
            savedSettingsAppliedToController = false;
        }
        connectButton.Text = connected ? "Disconnect" : "Connect";
        UpdateConnectButtonAppearance();
        portSelector.Enabled = !connected;
        refreshButton.Enabled = !connected;
        pingMenuItem.Enabled = connected;
        statusMenuItem.Enabled = connected;
        resetMenuItem.Enabled = connected;
        testSensorsMenuItem.Enabled = connected && controllerReady;
        modeSelector.Enabled = true;
        laneCountSelector.Enabled = true;
        treeModeSelector.Enabled = true;
        stagingModeSelector.Enabled = true;
        stagedDelayInput.Enabled = true;
        startPracticeButton.Enabled = connected && controllerReady;
        UpdateDialInputState();
        UpdateConnectionLabel();
        connectionLabel.Refresh();
    }

    private void MarkControllerReady()
    {
        if (!client.IsConnected)
        {
            return;
        }

        controllerReady = true;
        startPracticeButton.Enabled = true;
        testSensorsMenuItem.Enabled = true;
        RememberConnectedControllerPort();
    }

    private void ApplySavedSettingsAfterConnect(ProtocolMessage heartbeat)
    {
        if (firmwareUpdateActive || savedSettingsAppliedToController)
        {
            return;
        }

        var stateIndex = -1;
        for (var index = 0; index < heartbeat.Parts.Count; index++)
        {
            if (heartbeat.Parts[index] == "STATE")
            {
                stateIndex = index;
                break;
            }
        }
        if (stateIndex < 0 || stateIndex + 1 >= heartbeat.Parts.Count ||
            heartbeat.Parts[stateIndex + 1] is not (
                "WAITING_FOR_ALL_LANES" or "RACE_COMPLETE" or "WAITING_FOR_CLEAR"))
        {
            return;
        }

        if (ApplyRaceSettings())
        {
            savedSettingsAppliedToController = true;
            AppendLog("Saved race and track settings applied to controller.");
        }
    }

    private bool SendCommandBatch(IEnumerable<string[]> commands)
    {
        var commandList = commands.ToArray();
        try
        {
            client.SendBatch(commandList);
            foreach (var parts in commandList)
            {
                AppendLog($"> {ProtocolMessage.Create(parts).Encode()}");
            }
            return true;
        }
        catch (Exception exception)
        {
            AppendLog($"! {exception.Message}");
            return false;
        }
    }

    private void LoadPersistedSettingsIntoControls()
    {
        modeSelector.SelectedItem = persistedSettings.RaceMode;
        laneCountSelector.SelectedItem = persistedSettings.LaneCount.ToString(CultureInfo.InvariantCulture);
        treeModeSelector.SelectedItem = persistedSettings.TreeMode;
        stagingModeSelector.SelectedItem = persistedSettings.StagingMode;
        stagedDelayInput.Value = Math.Clamp(
            persistedSettings.StagedDelaySeconds,
            stagedDelayInput.Minimum,
            stagedDelayInput.Maximum);
        trackLengthInput.Value = Math.Clamp(
            persistedSettings.TrackLengthInches,
            trackLengthInput.Minimum,
            trackLengthInput.Maximum);
        speedTrapLengthInput.Value = Math.Clamp(
            persistedSettings.SpeedTrapLengthInches,
            speedTrapLengthInput.Minimum,
            speedTrapLengthInput.Maximum);
        for (var lane = 0; lane < LaneCount; lane++)
        {
            dialInputs[lane].Value = Math.Clamp(
                persistedSettings.DialSeconds[lane],
                dialInputs[lane].Minimum,
                dialInputs[lane].Maximum);
        }
    }

    private void SaveCurrentSettings()
    {
        persistedSettings = new AppSettings
        {
            RaceMode = modeSelector.SelectedItem as string ?? "BRACKET",
            LaneCount = SelectedLaneCount(),
            TreeMode = treeModeSelector.SelectedItem as string ?? "FULL",
            StagingMode = stagingModeSelector.SelectedItem as string ?? "BOTH_BLOCKED",
            StagedDelaySeconds = stagedDelayInput.Value,
            TrackLengthInches = trackLengthInput.Value,
            SpeedTrapLengthInches = speedTrapLengthInput.Value,
            DialSeconds = dialInputs.Select(input => input.Value).ToArray(),
            PracticeLanes = practiceLaneChecks
                .Select((input, index) => (input, lane: index + 1))
                .Where(item => item.input.Checked)
                .Select(item => item.lane)
                .ToArray(),
            IntervalTimerLanes = persistedSettings.IntervalTimerLanes.ToArray(),
            VoiceAnnouncementsEnabled = persistedSettings.VoiceAnnouncementsEnabled,
            SpeechVoiceName = persistedSettings.SpeechVoiceName,
            ExportTournamentJson = persistedSettings.ExportTournamentJson,
            ExportTournamentCsv = persistedSettings.ExportTournamentCsv
        };

        try
        {
            AppSettingsStore.Save(persistedSettings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppendLog($"Could not save race settings: {exception.Message}");
        }
    }

    private void UpdateConnectButtonAppearance()
    {
        if (!connectButton.Enabled)
        {
            connectButton.BackColor = Color.FromArgb(245, 246, 247);
            connectButton.ForeColor = Color.FromArgb(54, 60, 66);
            connectButton.FlatAppearance.BorderColor = Color.FromArgb(160, 166, 172);
        }
        else if (connectionRequested)
        {
            connectButton.BackColor = SystemColors.Control;
            connectButton.ForeColor = Color.FromArgb(139, 32, 32);
            connectButton.FlatAppearance.BorderColor = Color.FromArgb(158, 45, 45);
        }
        else
        {
            connectButton.BackColor = UiStyles.BlueAction;
            connectButton.ForeColor = Color.White;
            connectButton.FlatAppearance.BorderColor = ControlPaint.Dark(UiStyles.BlueAction);
        }
    }

    private void UpdateConnectionLabel()
    {
        if (!connectionRequested)
        {
            connectionLabel.Text = "Disconnected";
            connectionLabel.BackColor = Color.FromArgb(235, 237, 239);
            connectionLabel.ForeColor = Color.FromArgb(92, 99, 106);
            return;
        }

        if (!client.IsConnected)
        {
            connectionLabel.Text = "Connected — serial port not open";
            connectionLabel.BackColor = Color.FromArgb(252, 222, 222);
            connectionLabel.ForeColor = Color.FromArgb(139, 32, 32);
            return;
        }

        if (client.LastHeartbeatReceivedAt is not { } heartbeatAt)
        {
            connectionLabel.Text = client.LastHelloReceivedAt.HasValue
                ? "Connected — waiting for heartbeat"
                : "Connected";
            connectionLabel.BackColor = Color.FromArgb(255, 236, 179);
            connectionLabel.ForeColor = Color.FromArgb(97, 66, 0);
            return;
        }

        var age = DateTimeOffset.Now - heartbeatAt;
        if (age.TotalSeconds > 3)
        {
            connectionLabel.Text = $"Connected — controller stale {age.TotalSeconds:0}s";
            connectionLabel.BackColor = Color.FromArgb(252, 222, 222);
            connectionLabel.ForeColor = Color.FromArgb(139, 32, 32);
        }
        else
        {
            connectionLabel.Text = "Connected — controller ready";
            connectionLabel.BackColor = Color.FromArgb(218, 242, 225);
            connectionLabel.ForeColor = Color.FromArgb(22, 92, 55);
        }
    }

    private void UpdateSettingsSummary()
    {
        var mode = modeSelector.SelectedItem as string ?? "BRACKET";
        var tree = treeModeSelector.SelectedItem as string ?? "FULL";
        settingsSummaryLabel.Text =
            $"{SelectedLaneCount()} lanes  |  {mode.Replace('_', ' ')}  |  " +
            $"{tree} Tree  |  " +
            $"{StagingModeLabel(stagingModeSelector.SelectedItem as string)}  |  " +
            $"{stagedDelayInput.Value:0.000}s delay";
    }

    private static string StagingModeLabel(string? stagingMode) =>
        stagingMode == "IN_ORDER" ? "Pre-stage then stage" : "Both beams blocked";

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
        if (result.Interval1Us.HasValue)
        {
            parts.Add($"interval 1 {FormatSeconds(result.Interval1Us.Value)}s");
        }
        if (result.Interval2Us.HasValue)
        {
            parts.Add($"interval 2 {FormatSeconds(result.Interval2Us.Value)}s");
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
        protocolLogBuffer.AppendLine($"{DateTime.Now:HH:mm:ss.fff} {text}");
        TrimProtocolLog();
        diagnosticsVersion++;
        if (text.StartsWith("< ", StringComparison.Ordinal) ||
            text.StartsWith("> ", StringComparison.Ordinal) ||
            text.StartsWith("DEMO < ", StringComparison.Ordinal))
        {
            return;
        }

        activityEntries.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        while (activityEntries.Count > 200)
        {
            activityEntries.RemoveAt(0);
        }
    }

    private void TrimProtocolLog()
    {
        if (protocolLogBuffer.Length <= MaximumProtocolLogCharacters)
        {
            return;
        }

        var removeThrough = protocolLogBuffer.Length - TrimmedProtocolLogCharacters;
        while (removeThrough < protocolLogBuffer.Length &&
               protocolLogBuffer[removeThrough] != '\n')
        {
            removeThrough++;
        }
        if (removeThrough < protocolLogBuffer.Length)
        {
            removeThrough++;
        }
        protocolLogBuffer.Remove(0, removeThrough);
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
        public long? Interval1Us { get; set; }
        public long? Interval2Us { get; set; }
        public long? BreakoutUs { get; set; }
        public long? SpeedMphX100 { get; set; }
    }
}
