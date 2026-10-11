using System.Reflection;
using System.Windows.Forms;
using DragWin;

internal static class RegressionTests
{
    public static void Run()
    {
        foreach (var text in new[] { "999999999", decimal.MaxValue.ToString(), "-1", "0.099", "60.001", "bad" })
            Check(!DialInput.TryParseMilliseconds(text, out _), $"Invalid dial accepted: {text}");
        Check(DialInput.TryParseMilliseconds("0.100", out var minimum) && minimum == 100, "Minimum dial rejected.");
        Check(DialInput.TryParseMilliseconds("60.000", out var maximum) && maximum == 60000, "Maximum dial rejected.");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunUiAndRecoveryTests(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Regression tests failed.", failure);
    }

    private static void RunUiAndRecoveryTests()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"drag-regressions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            RunDemoReleaseTests(directory);
            var repository = new RaceRepository(Path.Combine(directory, "test.db"), Path.Combine(directory, "backups"));
            var racer = repository.AddRacer("Regression racer");
            var cars = Enumerable.Range(1, 4).Select(index =>
                repository.AddCar(racer.Id, $"Car {index}", 10000)).ToArray();
            var planner = new TournamentPlanner();
            var tournament = repository.CreateTournament("Recovery", 4, cars.Select(car => car.Id).ToArray());
            var round = planner.CreateRound(cars, 4, 1, randomSeed: 42);
            repository.SaveRound(tournament.Id, round);
            var heat = round.Heats.Single();

            using (var client = new DragSerialClient())
            using (var runner = new TournamentRunnerForm(tournament, repository, client, 500, "BOTH_BLOCKED",
                       [], false, "", SpeechBackendMode.None, new TournamentReportExportOptions(false, false)))
            {
                Invoke(runner, "ProcessMessage", ProtocolMessage.Create("EVENT", "TREE", "HEADS_UP_START"));
                Check(Field(runner, "phase").ToString() == "ReadyToStage", "An unarmed heat accepted a race event.");
                Invoke(runner, "DemoHeat");
                Check(Field(runner, "phase").ToString() == "ResultsReady", "Demo heat did not finish.");
                Invoke(runner, "ProcessMessage", ProtocolMessage.Create("EVENT", "TREE", "WAITING_FOR_ALL_LANES"));
                Invoke(runner, "ProcessMessage", ProtocolMessage.Create("EVENT", "TREE", "ALL_LANES_STAGED"));
                Invoke(runner, "ProcessMessage", ProtocolMessage.Create("EVENT", "TREE", "BRACKET_START"));
                Check(Field(runner, "phase").ToString() == "ResultsReady", "Post-race events replaced unconfirmed results.");
                Check(((Button)Field(runner, "confirmButton")).Enabled, "Result confirmation became disabled.");
            }

            var results = heat.Entries.Select((entry, index) => new RunResult(
                entry.Car.Id, RunLegality.Legal, index + 1, 100000, null, entry.IsBye)).ToArray();
            repository.SaveHeatResults(tournament.Id, 1, heat.HeatNumber, results,
                results.Take(2).Select(result => result.CarId).ToHashSet());
            // Simulate stopping after confirmation was committed but before the next round existed.
            var reopened = new RaceRepository(repository.DatabasePath, Path.Combine(directory, "backups"));
            ExecuteSql(repository.DatabasePath,
                "CREATE TRIGGER fail_next_round BEFORE INSERT ON rounds WHEN NEW.round_number = 2 BEGIN SELECT RAISE(ABORT, 'test save failure'); END;");
            try
            {
                reopened.RecoverTournamentProgress(tournament, planner);
                throw new InvalidOperationException("Injected round-save failure was ignored.");
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                Check(reopened.IsRoundConfirmed(tournament.Id, 1), "Failed advancement lost confirmed results.");
                Check(reopened.GetLatestRound(tournament.Id).RoundNumber == 1, "Failed round save left partial progress.");
            }
            ExecuteSql(repository.DatabasePath, "DROP TRIGGER fail_next_round;");
            var recovered = reopened.RecoverTournamentProgress(tournament, planner);
            Check(recovered.RoundNumber == 2, "A confirmed round was reopened instead of advanced.");
            Check(reopened.RecoverTournamentProgress(tournament, planner).RandomSeed == recovered.RandomSeed,
                "Repeated recovery created a different round.");
            var finalHeat = recovered.Heats.Single();
            var finalResults = finalHeat.Entries.Select((entry, index) => new RunResult(
                entry.Car.Id, RunLegality.Legal, index + 1, 100000, null, entry.IsBye)).ToArray();
            reopened.SaveHeatResults(tournament.Id, 2, finalHeat.HeatNumber, finalResults,
                new HashSet<long> { finalResults[0].CarId });
            reopened.RecoverTournamentProgress(tournament, planner);
            Check(reopened.GetTournamentReport(tournament.Id).Status == "COMPLETE", "Confirmed final did not recover completion.");

            using var settings = new RaceSettingsForm("BRACKET", 4, "FULL", "BOTH_BLOCKED", 0.5M,
                [10M, 10M, 10M, 10M], 660M, 12M, [], false, "saved-voice", SpeechBackendMode.Piper,
                false, false, false);
            var calls = 0;
            var completion = new TaskCompletionSource<IReadOnlyList<string>>();
            settings.VoiceDiscovery = _ => { ++calls; return completion.Task; };
            _ = settings.Handle;
            Invoke(settings, "OnShown", EventArgs.Empty);
            Check(calls == 0, "Disabled announcements triggered voice discovery.");
            ((CheckBox)Field(settings, "voiceAnnouncementsCheck")).Checked = true;
            Check(calls == 1, "Enabled announcements did not start discovery.");
            Check(settings.SpeechVoiceName == "saved-voice", "Pending discovery lost the saved voice.");
            Check(!((Button)Field(settings, "testVoiceButton")).Enabled, "Voice test enabled during discovery.");
            ((CheckBox)Field(settings, "voiceAnnouncementsCheck")).Checked = false;
            completion.SetResult(["another-voice"]);
            Application.DoEvents();
            Check(settings.SpeechVoiceName == "saved-voice", "Stale discovery changed disabled speech settings.");

            var nextCompletion = new TaskCompletionSource<IReadOnlyList<string>>();
            settings.VoiceDiscovery = _ => nextCompletion.Task;
            ((CheckBox)Field(settings, "voiceAnnouncementsCheck")).Checked = true;
            nextCompletion.SetResult(["saved-voice", "another-voice"]);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!((Button)Field(settings, "testVoiceButton")).Enabled && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(((Button)Field(settings, "testVoiceButton")).Enabled, "Completed discovery did not enable voice controls.");
            Check(settings.SpeechVoiceName == "saved-voice", "Completed discovery lost the selected voice.");

            var closingCompletion = new TaskCompletionSource<IReadOnlyList<string>>();
            settings.VoiceDiscovery = _ => closingCompletion.Task;
            ((ComboBox)Field(settings, "speechBackendSelector")).SelectedIndex = 3;
            settings.Dispose();
            closingCompletion.SetResult(["late-voice"]);
            Application.DoEvents();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void RunDemoReleaseTests(string directory)
    {
        var repository = new RaceRepository(Path.Combine(directory, "demo.db"), Path.Combine(directory, "demo-backups"));
        Check(repository.GetCars().Count == 0, "Demo test did not start with an empty database.");
        using (var main = new MainForm(demoMode: true, repository: repository, settings: new AppSettings()))
        {
            _ = main.Handle;
            Invoke(main, "TryAutoConnect");
            Invoke(main, "ToggleConnection");
            Check(!((DragSerialClient)Field(main, "client")).IsConnected, "Demo startup opened a serial port.");
            Check(!((Button)Field(main, "connectButton")).Enabled, "Demo allowed a manual connection.");
            Check(!((ToolStripMenuItem)Field(main, "updateControllerFirmwareMenuItem")).Enabled,
                "Demo allowed a firmware update.");
            Invoke(main, "DemoPracticeRun");
            Check(Field(main, "passResultsForm") is PassResultsForm, "Demo practice did not open pass results.");
            var practiceGrid = (DataGridView)Field(Field(main, "passResultsForm"), "resultsGrid");
            Check(practiceGrid.Rows.Cast<DataGridViewRow>().All(row =>
                row.Cells["Source"].Value?.ToString() == "Simulated"), "Practice demo omitted source labels.");
            ((Form)Field(main, "passResultsForm")).Close();
            main.Close();
        }

        var tournament = DemoTournamentFactory.Create(repository);
        Check(repository.GetRacers().Count == 4 && repository.GetCars().Count == 8,
            "Sample tournament did not supply four racers and eight cars.");
        DemoTournamentFactory.Create(repository);
        Check(repository.GetRacers().Count == 4 && repository.GetCars().Count == 8,
            "Repeated sample creation duplicated racers or cars.");
        using var client = new DragSerialClient();
        using var runner = new TournamentRunnerForm(tournament, repository, client, 500, "BOTH_BLOCKED",
            [1, 2, 3, 4], false, "", SpeechBackendMode.None, new TournamentReportExportOptions(), demoMode: true);
        Check(((Button)Field(runner, "sendHeatButton")).Text == "Run Demo Heat", "Demo retained the hardware arm button.");
        // Test report generation separately without a modal window blocking the test runner.
        runner.GetType().GetField("completionReportShown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(runner, true);
        var heats = 0;
        while (repository.GetTournamentReport(tournament.Id).Status != "COMPLETE" && heats < 20)
        {
            for (var choice = 0; Field(runner, "phase").ToString() == "ChoosingLanes" && choice < 4; ++choice)
                Invoke(runner, "ConfirmLaneChoice");
            Check(Field(runner, "phase").ToString() == "ReadyToStage", "Demo could not complete ordered lane choices.");
            // Raise the same Click event as the demo's primary button, without needing a visible form.
            typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Field(runner, "sendHeatButton"), [EventArgs.Empty]);
            Check(Field(runner, "phase").ToString() == "ResultsReady", "Demo heat did not produce confirmable results.");
            Invoke(runner, "ConfirmHeat");
            runner.GetType().GetField("lastRunnerButtonActionAt", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(runner, default(DateTimeOffset));
            ++heats;
        }
        var report = repository.GetTournamentReport(tournament.Id);
        Check(report.Status == "COMPLETE" && heats > 1, "Multi-round sample tournament did not complete.");
        Check(!client.IsConnected, "Demo tournament opened a controller connection.");
        var paths = TournamentReportArchiveWriter.Write(report, Path.Combine(directory, "reports"));
        Check(File.Exists(paths.Html) && File.Exists(paths.Json) && File.Exists(paths.Csv),
            "Completed demo did not export HTML, JSON and CSV reports.");
        var reopened = new RaceRepository(repository.DatabasePath, Path.Combine(directory, "demo-backups"));
        var savedReport = reopened.GetTournamentReport(tournament.Id);
        Check(savedReport.Rows.All(row => row.Source == ResultSource.Simulated),
            "Reopening a demo tournament lost its simulated source.");
        Check(savedReport.HasSimulatedResults, "Demo report did not identify simulated results.");
        Check(File.ReadAllText(paths.Html).Contains("SIMULATED RESULTS INCLUDED"),
            "Demo HTML omitted its simulation warning.");
        Check(File.ReadAllText(paths.Csv!).Contains("ResultSource") &&
              File.ReadAllText(paths.Csv!).Contains(",Simulated"), "Demo CSV omitted result sources.");
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(paths.Json!));
        Check(json.RootElement.GetProperty("tournamentReport").GetProperty("rows")
            .EnumerateArray().All(row => row.GetProperty("source").GetString() == "Simulated"),
            "Demo JSON omitted result sources.");

        var firstRound = reopened.GetLatestRound(tournament.Id);
        var firstHeat = firstRound.Heats[0];
        var controllerResults = firstHeat.Entries.Select((entry, index) => new RunResult(
            entry.Car.Id, RunLegality.Legal, index + 1, 100000, null, entry.IsBye,
            Source: ResultSource.Controller)).ToArray();
        reopened.SaveHeatResults(tournament.Id, firstRound.RoundNumber, firstHeat.HeatNumber,
            controllerResults, controllerResults.Take(1).Select(result => result.CarId).ToHashSet());
        var mixed = reopened.GetTournamentReport(tournament.Id);
        Check(mixed.HasSimulatedResults && mixed.Rows.Any(row => row.Source == ResultSource.Controller),
            "Mixed tournament lost per-result provenance.");
        var mixedPaths = TournamentReportArchiveWriter.Write(mixed, Path.Combine(directory, "mixed"));
        Check(File.ReadAllText(mixedPaths.Html).Contains("SIMULATED RESULTS INCLUDED") &&
              File.ReadAllText(mixedPaths.Csv!).Contains(",Controller"), "Mixed report omitted source labels.");
    }

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static object? Invoke(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);

    private static void ExecuteSql(string databasePath, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
