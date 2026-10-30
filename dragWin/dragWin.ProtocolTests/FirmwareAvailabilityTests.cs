using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using DragWin;

internal static class FirmwareAvailabilityTests
{
    public static void Run()
    {
        var packages = ControllerFirmwarePackage.LoadBundledPackages();
        var mega = packages.Single(package => !package.IsDue);
        var due = packages.Single(package => package.IsDue);
        var identity = new ControllerIdentity("DRAG_MC", "0.6.7", 10, "MEGA2560");
        AvailableFirmwareUpdate? Check(ControllerIdentity? candidate, IReadOnlyList<ControllerFirmwarePackage>? choices = null) =>
            FirmwareAvailability.Check(candidate, () => choices ?? packages, out _);
        var update = Check(identity);
        Verify(update is { InstalledVersion: "0.6.7", AvailableVersion: "0.6.8" }, "Older Mega did not get its own package.");
        Verify(Check(identity with { FirmwareVersion = mega.Manifest.FirmwareVersion }) is null, "Equal firmware advertised an update.");
        Verify(Check(identity with { FirmwareVersion = "0.6.9" }) is null &&
            Check(identity with { FirmwareVersion = "0.6.10" }) is null, "Newer numeric firmware advertised a downgrade.");
        var dueIdentity = new ControllerIdentity("DRAG_MC_DUE", "0.1.1", 11, "SAM3X8E");
        Verify(Check(dueIdentity)?.AvailableVersion == due.Manifest.FirmwareVersion, "Due was compared against Mega.");
        Verify(Check(dueIdentity with { FirmwareVersion = due.Manifest.FirmwareVersion }) is null, "Equal Due advertised update.");
        Verify(Check(dueIdentity with { FirmwareVersion = "0.2.0" }) is null, "Newer Due advertised update.");
        Verify(Check(dueIdentity with { Product = "DRAG_MC_DUE_LIGHT_TEST" }) is null, "Different product series compared numerically.");
        Verify(Check(identity with { Mcu = "SAM3X8E" }) is null && Check(identity with { Product = "OTHER" }) is null,
            "Unknown identity guessed an update.");
        Verify(Check(identity, []) is null && Check(identity, [due]) is null && Check(identity, [mega, mega]) is null,
            "Missing or ambiguous package guessed an update.");
        foreach (var text in new[] { "", "bad", "0.6.7-beta.1", " 0.6.7", "0.-6.7", "0.6.7.", "0.6.2147483648", "0.6.7+dev" })
        {
            Verify(!FirmwareAvailability.TryVersion(text, out _), $"Malformed version accepted: {text}");
            Verify(Check(identity with { FirmwareVersion = text }) is null, "Malformed identity guessed an update.");
        }
        Verify(FirmwareAvailability.TryVersion("0.6.8", out var three) &&
            FirmwareAvailability.TryVersion("0.6.8.0", out var four) && three == four, "Trailing zero normalization failed.");
        Verify(FirmwareAvailability.TryVersion("0.6.9", out var nine) &&
            FirmwareAvailability.TryVersion("0.6.10", out var ten) && ten > nine, "Version comparison is lexical.");
        var loads = 0;
        var absent = FirmwareAvailability.Check(null, () => { ++loads; return packages; }, out _);
        Verify(absent is null && loads == 0, "Disconnected check loaded packages.");
        absent = FirmwareAvailability.Check(identity, () => throw new InvalidDataException("bad package"), out var error);
        Verify(absent is null && error == "bad package", "Invalid package was not handled non-modally.");
        var root = Path.Combine(Path.GetTempPath(), "drag-notice-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Firmware"));
            File.WriteAllText(Path.Combine(root, "Firmware", "broken.dragfw"), "not a zip");
            absent = FirmwareAvailability.Check(identity, () => ControllerFirmwarePackage.LoadBundledPackages(root), out error);
            Verify(absent is null && error is not null, "Corrupt bundled package escaped validation.");
        }
        finally { Directory.Delete(root, true); }

        var guard = new FirmwareUpdateGuard();
        Verify(!guard.CanUpdate, "Unknown controller state allowed update.");
        void Heartbeat(string state) => guard.Observe(ProtocolMessage.Create("HEARTBEAT", "1000", "STATE", state));
        Heartbeat("WAITING_FOR_ALL_LANES");
        Verify(guard.CanUpdate, "Idle controller did not allow update.");
        guard.CommandSent(["ARM", "MANUAL"]);
        Heartbeat("WAITING_FOR_ALL_LANES");
        Verify(!guard.CanUpdate, "Armed controller awaiting staging allowed update.");
        guard.Observe(ProtocolMessage.Create("EVENT", "TREE", "STAGING_ABORTED"));
        Verify(!guard.CanUpdate, "Rollout cleared armed guard.");
        foreach (var state in new[] { "STAGING_HOLD", "WAITING_FOR_START", "START_DELAY", "RACE_ACTIVE" })
        {
            guard.Reset();
            Heartbeat(state);
            Verify(!guard.CanUpdate, "Live controller state allowed update: " + state);
        }
        Heartbeat("RACE_COMPLETE");
        Verify(guard.CanUpdate, "Completed pass retained guard.");
        guard.Observe(ProtocolMessage.Create("ACK", "ARM", "AUTO"));
        Heartbeat("WAITING_FOR_ALL_LANES");
        Verify(!guard.CanUpdate, "Tournament/external arm acknowledgement was ignored.");
        Heartbeat("WAITING_FOR_CLEAR");
        Verify(guard.CanUpdate, "Result hold retained guard.");
        guard.CommandSent(["RESET"]);
        Verify(!guard.CanUpdate, "Legacy reset/arm allowed update.");
        guard.Reset();
        Verify(!guard.CanUpdate, "Reconnect assumed idle before heartbeat.");
        Heartbeat("WAITING_FOR_START");
        Heartbeat("WAITING_FOR_ALL_LANES");
        Verify(!guard.CanUpdate, "Observed armed state was forgotten on restaging.");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { UiTests(update!); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Firmware notice UI tests failed.", failure);
        Console.WriteLine("Firmware availability comparison, package, guard and UI tests passed.");
    }

    private static void UiTests(AvailableFirmwareUpdate update)
    {
        using var form = new Form { ClientSize = new Size(900, 180), StartPosition = FormStartPosition.Manual, Location = new Point(-2500, 0) };
        using var notice = new FirmwareUpdateNotice { Dock = DockStyle.Top };
        form.Controls.Add(notice);
        var button = (Button)typeof(FirmwareUpdateNotice).GetField("update", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(notice)!;
        var label = (Label)typeof(FirmwareUpdateNotice).GetField("message", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(notice)!;
        var clicks = 0;
        notice.UpdateRequested += (_, _) => ++clicks;
        form.Show();
        foreach (var width in new[] { 900, 1100 })
        {
            form.ClientSize = new Size(width, 180);
            notice.SetAvailability(update, true, "");
            Application.DoEvents();
            Verify(notice.Visible && button.Enabled && label.Text.Contains("0.6.7 -> 0.6.8"), "Update notice not presented.");
            Verify(label.Right <= button.Left && button.Right <= button.Parent!.ClientSize.Width && button.Bottom <= notice.Height,
                "Notice controls overlap or clip.");
        }
        button.PerformClick();
        Verify(clicks == 1, "Update action not raised exactly once.");
        notice.SetAvailability(update, false, "Finish the pass first");
        button.PerformClick();
        Verify(notice.Visible && !button.Enabled && clicks == 1, "Unsafe update action remained available.");
        var screenshot = Environment.GetEnvironmentVariable("DRAG_FIRMWARE_NOTICE_SCREENSHOT");
        if (!string.IsNullOrEmpty(screenshot))
        {
            notice.SetAvailability(update, true, "");
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(screenshot);
        }
        notice.SetAvailability(null, false, "Disconnected");
        Verify(!notice.Visible, "Disconnected/equal/reconnected notice was retained.");
        form.Close();

        var root = Path.Combine(Path.GetTempPath(), "drag-notice-ui-" + Guid.NewGuid());
        try
        {
            var repository = new RaceRepository(Path.Combine(root, "test.db"), Path.Combine(root, "backups"));
            foreach (var demo in new[] { true, false })
            {
                using var main = new MainForm(demo, repository, new AppSettings { VoiceAnnouncementsEnabled = false });
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var banner = (FirmwareUpdateNotice)typeof(MainForm).GetField("firmwareNotice", flags)!.GetValue(main)!;
                banner.SetAvailability(update, true, "");
                typeof(MainForm).GetMethod("UpdateFirmwareNotice", flags)!.Invoke(main, null);
                Verify(!banner.Visible, "Demo/disconnected main window retained an update notice.");
                var menu = (ToolStripMenuItem)typeof(MainForm).GetField("updateControllerFirmwareMenuItem", flags)!.GetValue(main)!;
                if (demo) Verify(!menu.Enabled, "Demo enabled firmware updates.");
                ((System.Windows.Forms.Timer)typeof(MainForm).GetField("heartbeatTimer", flags)!.GetValue(main)!).Dispose();
                // Only show demo mode; normal startup intentionally auto-connects.
                if (demo)
                {
                    main.StartPosition = FormStartPosition.Manual;
                    main.Location = new Point(-2500, 0);
                    main.Show();
                    foreach (var size in new[] { new Size(900, 430), new Size(1100, 500) })
                    {
                        main.Size = size;
                        banner.SetAvailability(update, true, "");
                        Application.DoEvents();
                        foreach (var field in new[] { "startPracticeButton", "runTournamentButton" })
                        {
                            var action = (Control)typeof(MainForm).GetField(field, flags)!.GetValue(main)!;
                            var bounds = new Rectangle(main.PointToClient(action.PointToScreen(Point.Empty)), action.Size);
                            Verify(main.ClientRectangle.Contains(bounds), "Firmware notice clipped main-window action: " + field);
                        }
                        if (!string.IsNullOrEmpty(screenshot))
                        {
                            using var bitmap = new Bitmap(main.Width, main.Height);
                            main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size));
                            bitmap.Save(Path.ChangeExtension(screenshot, $"main-{size.Width}.png"));
                        }
                    }
                }
                ((DragSerialClient)typeof(MainForm).GetField("client", flags)!.GetValue(main)!).Dispose();
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Verify(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
