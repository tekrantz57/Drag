using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using DragWin;

internal static class PracticeUiTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Test(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("Practice UI test failed", failure);
        Console.WriteLine("Practice dial and interval display tests passed.");
    }

    private static void Test()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var root = Path.Combine(Path.GetTempPath(), "drag-practice-ui-" + Guid.NewGuid());
        try
        {
            var repository = new RaceRepository(Path.Combine(root, "test.db"), Path.Combine(root, "backups"));
            var settings = new AppSettings { RaceMode = "BRACKET", PracticeLanes = [1],
                DialSeconds = [1.234M, 2M, 3M, 4M], VoiceAnnouncementsEnabled = false };
            using var main = new MainForm(true, repository, settings);
            main.StartPosition = FormStartPosition.Manual;
            main.Location = new Point(-2500, 0);
            main.Show();
            var defaults = (NumericUpDown[])typeof(MainForm).GetField("dialInputs", flags)!.GetValue(main)!;
            var overrides = (NumericUpDown[])typeof(MainForm).GetField("practiceDialInputs", flags)!.GetValue(main)!;
            Check(overrides[0].Value == 1.234M, "Practice default not initialized.");
            var modes = (ComboBox)typeof(MainForm).GetField("modeSelector", flags)!.GetValue(main)!;
            modes.SelectedItem = "HEADS_UP";
            Check(!overrides[0].Enabled, "Heads-up enabled practice dial editing.");
            modes.SelectedItem = "BRACKET";
            Check(overrides[0].Enabled && !overrides[1].Enabled, "Dial controls did not follow selected lanes.");
            overrides[0].Value = 1.111M;
            var snapshot = (Dictionary<int, int>)typeof(MainForm).GetMethod("PracticeDials", flags)!
                .Invoke(main, [new[] { 1 }])!;
            object?[] args = [null, true, snapshot];
            Check((bool)typeof(MainForm).GetMethod("TryBuildRaceSettingsCommands", flags)!.Invoke(main, args)!, "Could not build commands.");
            var commands = (List<string[]>)args[0]!;
            Check(commands.Any(c => c.SequenceEqual(new[] { "SET", "DIAL", "1", "1111" })), "Override was not sent.");
            var lanes = (ComboBox)typeof(MainForm).GetField("laneCountSelector", flags)!.GetValue(main)!;
            lanes.SelectedItem = "2";
            overrides[3].Value = 4.321M;
            var twoLaneDials = (Dictionary<int, int>)typeof(MainForm).GetMethod("PracticeDials", flags)!
                .Invoke(main, [new[] { 1, 4 }])!;
            object?[] twoLaneArgs = [null, true, twoLaneDials];
            Check((bool)typeof(MainForm).GetMethod("TryBuildRaceSettingsCommands", flags)!.Invoke(main, twoLaneArgs)!, "Two-lane commands failed.");
            var twoCommands = (List<string[]>)twoLaneArgs[0]!;
            Check(twoCommands.Any(c => c.SequenceEqual(new[] { "SET", "DIAL", "4", "4321" })) &&
                !twoCommands.Any(c => c.Length > 2 && c[0] == "SET" && c[1] == "DIAL" && c[2] == "2"),
                "Two-lane dial assignment used an inactive lane.");
            lanes.SelectedItem = "4";
            Check(defaults[0].Value == 1.234M, "Override changed saved default.");
            typeof(MainForm).GetMethod("HandleMessage", flags)!.Invoke(main,
                [ProtocolMessage.Create("STATUS", "LANE", "1", "DIAL_MS", "1111")]);
            Check(defaults[0].Value == 1.234M, "Controller status overwrote default.");
            typeof(MainForm).GetMethod("ResetPracticeDials", flags)!.Invoke(main, null);
            Check(overrides[0].Value == 1.234M && snapshot[1] == 1111, "Next pass default or prior snapshot was changed.");
            overrides[0].Value = 1.222M;
            typeof(MainForm).GetMethod("DemoPracticeRun", flags)!.Invoke(main, null);
            var activeResults = (PassResultsForm)typeof(MainForm).GetField("passResultsForm", flags)!.GetValue(main)!;
            var actualDials = (IReadOnlyDictionary<int, int>)typeof(PassResultsForm).GetField("passDials", flags)!.GetValue(activeResults)!;
            Check(actualDials[1] == 1222 && overrides[0].Value == 1.234M && defaults[0].Value == 1.234M,
                "Demo did not retain pass dial and restore next-pass default.");
            activeResults.Close();
            main.Size = new Size(900, 430);
            Application.DoEvents();
            var arm = (Button)typeof(MainForm).GetField("startPracticeButton", flags)!.GetValue(main)!;
            var armBounds = new Rectangle(main.PointToClient(arm.PointToScreen(Point.Empty)), arm.Size);
            Check(main.ClientRectangle.Contains(armBounds), "Practice controls clipped at compact window size.");
            var screenshot = Environment.GetEnvironmentVariable("DRAG_PRACTICE_SCREENSHOT");
            if (!string.IsNullOrEmpty(screenshot))
            {
                using var bitmap = new Bitmap(main.Width, main.Height);
                main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size));
                bitmap.Save(screenshot);
            }

            using var client = new DragSerialClient();
            using var form = new PassResultsForm(client, false, "", SpeechBackendMode.Automatic);
            var grid = (DataGridView)typeof(PassResultsForm).GetField("resultsGrid", flags)!.GetValue(form)!;
            form.BeginPass([1], [], false, snapshot);
            Check(!grid.Columns["Split1"]!.Visible && !grid.Columns["Split2"]!.Visible, "Uninstalled intervals visible.");
            Check(!grid.Columns.Contains("Split1To2") && !grid.Columns.Contains("Split2ToTrap"), "Extra segment columns remain.");
            form.BeginPass([1, 2], [1], false, snapshot);
            Check(grid.Columns["Split1"]!.Visible && grid.Columns["Split2"]!.Visible, "Installed intervals hidden.");
            Check(grid.Rows[2].Cells["Split1"].Value?.ToString() == "N/A", "Uninstalled lane not marked unavailable.");
            typeof(PassResultsForm).GetMethod("ClearResults", flags)!.Invoke(form, null);
            Check(!grid.Columns["Split1"]!.Visible, "Clear retained empty interval columns.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
