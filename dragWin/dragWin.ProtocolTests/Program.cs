using DragWin;
using System.Text.Json;

AssertEqual("PING:10", ProtocolMessage.Create("PING").Encode());
AssertEqual("ACK:PING:63", ProtocolMessage.Create("ACK", "PING").Encode());
AssertEqual(
    "SET:MODE:BRACKET:09",
    ProtocolMessage.Create("SET", "MODE", "BRACKET").Encode());
var laneCountCommand = ProtocolMessage.Create("SET", "LANES", "2").Encode();
Assert(
    ProtocolMessage.TryParse(laneCountCommand, out var laneCountMessage, out _),
    "A lane-count command should round-trip.");
AssertEqual("2", laneCountMessage!.Parts[2]);
var distancesCommand = ProtocolMessage.Create(
    "SET", "DISTANCES", "660000", "12000").Encode();
Assert(
    ProtocolMessage.TryParse(distancesCommand, out var distancesMessage, out _),
    "A distance command should round-trip.");
AssertEqual("660000", distancesMessage!.Parts[2]);
AssertEqual("12000", distancesMessage.Parts[3]);
var treeCommand = ProtocolMessage.Create("SET", "TREE", "PRO").Encode();
Assert(
    ProtocolMessage.TryParse(treeCommand, out var treeMessage, out _),
    "A Tree-mode command should round-trip.");
AssertEqual("PRO", treeMessage!.Parts[2]);
var stagedDelayCommand = ProtocolMessage.Create(
    "SET", "STAGED_DELAY", "750").Encode();
Assert(
    ProtocolMessage.TryParse(stagedDelayCommand, out var stagedDelayMessage, out _),
    "A staged-delay command should round-trip.");
AssertEqual("750", stagedDelayMessage!.Parts[2]);
var stagingModeCommand = ProtocolMessage.Create(
    "SET", "STAGING_MODE", "IN_ORDER").Encode();
Assert(
    ProtocolMessage.TryParse(stagingModeCommand, out var stagingModeMessage, out _),
    "A staging-mode command should round-trip.");
AssertEqual("IN_ORDER", stagingModeMessage!.Parts[2]);

var settingsPath = Path.Combine(
    Path.GetTempPath(), $"dragWin-settings-{Guid.NewGuid():N}.json");
try
{
    AppSettingsStore.Save(new AppSettings
    {
        RaceMode = "HEADS_UP",
        LaneCount = 2,
        TreeMode = "PRO",
        StagingMode = "IN_ORDER",
        StagedDelaySeconds = 0.750M,
        TrackLengthInches = 1320M,
        SpeedTrapLengthInches = 66M,
        DialSeconds = [7.1M, 7.2M, 7.3M, 7.4M],
        PracticeLanes = [1, 4],
        IntervalTimerLanes = [1, 4],
        VoiceAnnouncementsEnabled = true,
        SpeechVoiceName = "Test Voice",
        ExportTournamentJson = false,
        ExportTournamentCsv = true
    }, settingsPath);
    var loadedSettings = AppSettingsStore.Load(settingsPath);
    AssertEqual("HEADS_UP", loadedSettings.RaceMode);
    AssertEqual(2, loadedSettings.LaneCount);
    AssertEqual("IN_ORDER", loadedSettings.StagingMode);
    AssertEqual(0.750M, loadedSettings.StagedDelaySeconds);
    AssertEqual(7.4M, loadedSettings.DialSeconds[3]);
    AssertEqual(4, loadedSettings.PracticeLanes[1]);
    AssertEqual(4, loadedSettings.IntervalTimerLanes[1]);
    AssertEqual(true, loadedSettings.VoiceAnnouncementsEnabled);
    AssertEqual("Test Voice", loadedSettings.SpeechVoiceName);
    AssertEqual(false, loadedSettings.ExportTournamentJson);
    AssertEqual(true, loadedSettings.ExportTournamentCsv);
}
finally
{
    File.Delete(settingsPath);
}
var eventWithMetadata = ProtocolMessage.Create(
    "EVENT", "LANE", "1", "GREEN", "SEQ", "42", "MS", "123456").Encode();
Assert(
    ProtocolMessage.TryParse(eventWithMetadata, out var eventMessage, out _),
    "An event with sequence and controller timestamp metadata should parse.");
AssertEqual("GREEN", eventMessage!.Parts[3]);
AssertEqual("42", eventMessage.Parts[5]);

var sensorDiagnostic = ProtocolMessage.Create(
    "SENSOR", "1", "SPEED_TRAP", "RAW", "0", "EDGES", "7",
    "PULSE_US", "1340").Encode();
Assert(
    ProtocolMessage.TryParse(sensorDiagnostic, out var sensorDiagnosticMessage, out _),
    "A sensor diagnostic should round-trip.");
AssertEqual("SPEED_TRAP", sensorDiagnosticMessage!.Parts[2]);
AssertEqual("7", sensorDiagnosticMessage.Parts[6]);
var intervalMessage = ProtocolMessage.Create(
    "RESULT", "LANE", "1", "INTERVAL_1_US", "3450000").Encode();
Assert(ProtocolMessage.TryParse(intervalMessage, out var parsedInterval, out _),
    "An interval-timer result should round-trip.");
AssertEqual("INTERVAL_1_US", parsedInterval!.Parts[3]);
AssertEqual(
    "RESET_SENSOR_DIAGNOSTICS:17",
    ProtocolMessage.Create("RESET_SENSOR_DIAGNOSTICS").Encode());

Assert(
    ProtocolMessage.TryParse("STATUS:TREE:GREEN:49", out var message, out _),
    "A valid status message should parse.");
AssertEqual("STATUS", message!.Type);
AssertEqual("GREEN", message.Parts[2]);

Assert(
    !ProtocolMessage.TryParse("STATUS:TREE:GREEN:00", out _, out _),
    "A corrupt checksum should be rejected.");
Assert(
    !ProtocolMessage.TryParse("PING", out _, out _),
    "A missing checksum should be rejected.");
Assert(
    !ProtocolMessage.TryParse("\u00FFPING:10", out _, out _),
    "Non-ASCII input should be rejected.");

var planner = new TournamentPlanner();
var cars = new[]
{
    new Car(1, 1, "Owner A", "A1", 8000),
    new Car(2, 1, "Owner A", "A2", 8100),
    new Car(3, 2, "Owner B", "B1", 8200),
    new Car(4, 3, "Owner C", "C1", 8300),
    new Car(5, 4, "Owner D", "D1", 8400),
    new Car(6, 5, "Owner E", "E1", 8500)
};
var plannedRound = planner.CreateRound(cars, 4, 1, randomSeed: 12345);
Assert(
    plannedRound.Heats.All(heat =>
        heat.Entries.GroupBy(entry => entry.Car.RacerId).All(group => group.Count() == 1)),
    "Cars from the same owner should be separated when possible.");
Assert(
    plannedRound.Heats.Single(heat => heat.Entries.Count == 2)
        .Entries.All(entry => entry.IsBye),
    "A heat with no eliminations should consist of BYE passes.");
var finalRound = planner.CreateRound(cars.Take(2).ToArray(), 4, 2, randomSeed: 12345);
AssertEqual(1, finalRound.Heats.Single().AdvanceCount);
Assert(
    finalRound.Heats.Single().Entries.All(entry => !entry.IsBye),
    "A two-car final in a four-lane tournament should be raced, not treated as a BYE heat.");

var competitiveHeat = new HeatPlan(
    1,
    2,
    cars.Take(4)
        .Select((car, index) => new RoundEntry(
            car, index + 1, index + 1, false, car.DefaultDialMilliseconds))
        .ToArray());
var lineupAnnouncement = RaceAnnouncementText.HeatLineup(2, competitiveHeat);
Assert(lineupAnnouncement.Contains("Round 2, heat 1. 2 cars advance.", StringComparison.Ordinal),
    "The lineup announcement should identify the round, heat, and advancement count.");
Assert(lineupAnnouncement.Contains("Lane 1, Owner A, driving A1", StringComparison.Ordinal),
    "The lineup announcement should identify the lane, racer, and car.");
AssertEqual(
    "Owner A, driving A1 has selected lane 3.",
    RaceAnnouncementText.LaneChoiceConfirmed(cars[0], 3));
AssertEqual(
    "Heat complete. Advancing: Owner A, driving A1 and Owner B, driving B1.",
    RaceAnnouncementText.HeatComplete([cars[0], cars[2]]));
AssertEqual(
    "Tournament complete. Champion, Owner A, driving A1. Runner-up, Owner A, driving A2.",
    RaceAnnouncementText.TournamentComplete(cars[0], cars[1]));
AssertEqual(
    "Practice pass complete. Lane 1, elapsed time 7.432 seconds, 21.50 miles per hour. Lane 4, red light.",
    RaceAnnouncementText.PracticeComplete([
        new PracticeAnnouncementResult(1, 7_432_000, 2_150, false, false, false),
        new PracticeAnnouncementResult(4, null, null, true, false, false)
    ]));
var advancers = planner.SelectAdvancers(
    competitiveHeat,
    [
        new RunResult(1, RunLegality.Breakout, 3, 10000, 5000, false),
        new RunResult(2, RunLegality.Legal, 1, 20000, null, false),
        new RunResult(3, RunLegality.Breakout, 2, 15000, 2000, false),
        new RunResult(4, RunLegality.RedLight, 4, -1000, null, false)
    ]);
AssertEqual(2L, advancers[0].CarId);
AssertEqual(3L, advancers[1].CarId);

var legacyAdvancers = planner.SelectAdvancers(
    competitiveHeat,
    [
        new RunResult(1, RunLegality.Breakout, int.MaxValue, 10000, 5000, false),
        new RunResult(2, RunLegality.Legal, int.MaxValue, 20000, null, false),
        new RunResult(3, RunLegality.Breakout, int.MaxValue, 15000, 2000, false),
        new RunResult(4, RunLegality.RedLight, int.MaxValue, -1000, null, false)
    ]);
AssertEqual(2L, legacyAdvancers[0].CarId);
AssertEqual(3L, legacyAdvancers[1].CarId);

var byeHeat = new HeatPlan(
    1,
    1,
    [new RoundEntry(cars[0], 1, 1, true, cars[0].DefaultDialMilliseconds)]);
var byeAdvancer = planner.SelectAdvancers(
    byeHeat,
    [new RunResult(1, RunLegality.RedLight, 1, -5000, null, true)]);
AssertEqual(1L, byeAdvancer.Single().CarId);
var demoMessages = DemoHeatSimulator.CreateBracketHeatMessages(
    competitiveHeat, randomSeed: 7, splitSensorLanes: [1]);
Assert(
    demoMessages.Any(item => item.Type == "EVENT" && item.Parts[1] == "TREE" && item.Parts[2] == "RACE_COMPLETE"),
    "A demo heat should complete the tree.");
Assert(
    demoMessages.Any(item => item.Type == "RESULT" && item.Parts[1] == "WINNER"),
    "A demo heat should report a winner when at least one car makes a legal or breakout pass.");
AssertEqual(
    competitiveHeat.Entries.Count,
    demoMessages.Count(item => item.Type == "RESULT" && item.Parts[1] == "PLACE"));
Assert(
    demoMessages.Any(item => item.Type == "RESULT" && item.Parts.Count > 3 && item.Parts[3] == "SPEED_MPH_X100"),
    "A demo heat should include speed-trap output.");
Assert(
    demoMessages.Any(item => item.Type == "RESULT" && item.Parts.Count > 3 && item.Parts[3] == "INTERVAL_1_US"),
    "A demo heat should include enabled interval timers.");
var demoByeMessages = DemoHeatSimulator.CreateBracketHeatMessages(byeHeat, randomSeed: 7);
Assert(
    demoByeMessages.Any(item => item.Type == "EVENT" && item.Parts.Count > 3 && item.Parts[3] == "REACTION_US"),
    "A demo BYE pass should still produce a reaction time.");
Assert(
    demoByeMessages.All(item => !(item.Type == "EVENT" && item.Parts.Count > 3 && item.Parts[3] == "FOUL")),
    "A demo BYE pass should not red-light.");

IReadOnlyList<ProtocolMessage>? demoWithFoul = null;
for (var seed = 0; seed < 1000 && demoWithFoul is null; seed++)
{
    var candidate = DemoHeatSimulator.CreateBracketHeatMessages(competitiveHeat, seed);
    if (candidate.Any(item =>
            item.Type == "EVENT" && item.Parts.Count > 3 && item.Parts[3] == "FOUL"))
    {
        demoWithFoul = candidate;
    }
}
Assert(demoWithFoul is not null, "The demo search should find a deterministic foul.");
var foulMessages = demoWithFoul!;
var foulIndex = foulMessages.ToList().FindIndex(item =>
    item.Type == "EVENT" && item.Parts.Count > 3 && item.Parts[3] == "FOUL");
Assert(foulIndex >= 0, "The selected demo should contain a foul event.");
var foulLane = foulMessages[foulIndex].Parts[2];
Assert(
    foulMessages.Take(foulIndex).Any(item =>
        item.Type == "EVENT" && item.Parts.Count > 4 &&
        item.Parts[2] == foulLane && item.Parts[3] == "REACTION_US" &&
        long.TryParse(item.Parts[4], out var reaction) && reaction < 0),
    "A foul should be preceded by that lane's negative reaction time.");

var laneChoiceEntries = cars.Take(4)
    .Select((car, index) => new RoundEntry(
        car, index + 1, index + 1, false, car.DefaultDialMilliseconds))
    .ToArray();
var laneChoices = new LaneChoiceSession(laneChoiceEntries, [1, 2, 3, 4]);
laneChoices.Choose(1, 2);
AssertEqual(2, laneChoices.GetLane(1));
AssertEqual(1, laneChoices.GetLane(2));
Assert(laneChoices.LockedLanes.Contains(2), "The first chosen lane should lock.");
var protectedLaneRejected = false;
try
{
    laneChoices.Choose(2, 2);
}
catch (InvalidOperationException)
{
    protectedLaneRejected = true;
}
Assert(protectedLaneRejected, "A later chooser must not take an earlier locked lane.");
laneChoices.Choose(2, 3);
AssertEqual(3, laneChoices.GetLane(2));
AssertEqual(1, laneChoices.GetLane(3));

var databasePath = Path.Combine(
    Path.GetTempPath(),
    $"dragWin-tests-{Guid.NewGuid():N}.db");
var backupPath = databasePath + ".backup.db";
var legacyBackupPath = databasePath + ".v3-backup.db";
var safetyBackupPath = databasePath + ".before-restore.db";
var automaticBackupDirectory = databasePath + ".automatic";
var reportExportDirectory = databasePath + ".reports";
try
{
    var repository = new RaceRepository(databasePath, automaticBackupDirectory);
    var racer = repository.AddRacer("Test Racer");
    var car = repository.AddCar(racer.Id, "Test Car", 7500);
    car = repository.UpdateCar(car.Id, racer.Id, "Test Car Updated", 7600);
    AssertEqual("Test Car Updated", car.Name);
    AssertEqual(7600, car.DefaultDialMilliseconds);
    AssertEqual(1, repository.GetRacers().Count);
    AssertEqual(1, repository.GetCars().Count);
    var tournament = repository.CreateTournament(
        "Test Tournament",
        2,
        [car.Id]);
    var firstRound = planner.CreateRound([car], 2, 1, randomSeed: 44);
    repository.SaveRound(tournament.Id, firstRound);
    repository.UpdateHeatDialOverrides(tournament.Id, 1, 1, new Dictionary<long, int>
    {
        [car.Id] = 8100
    });
    var loadedRound = repository.GetLatestRound(tournament.Id);
    AssertEqual(1, loadedRound.Heats.Count);
    var persistedHeat = loadedRound.Heats.Single();
    AssertEqual(8100, persistedHeat.Entries.Single().DialMilliseconds);
    AssertEqual(7600, persistedHeat.Entries.Single().Car.DefaultDialMilliseconds);
    var persistedResult = new RunResult(
        car.Id, RunLegality.RedLight, 1, -1000, null, true,
        ElapsedMicroseconds: 7_600_000,
        SpeedMphX100: 2_150,
        IntervalTimersEnabled: true,
        Interval1Microseconds: 2_500_000,
        Interval2Microseconds: 5_000_000,
        SpeedTrapMicroseconds: 7_000_000);
    repository.SaveHeatResults(
        tournament.Id,
        1,
        persistedHeat.HeatNumber,
        [persistedResult],
        new HashSet<long> { car.Id });
    Assert(repository.IsRoundConfirmed(tournament.Id, 1), "The saved heat should confirm the round.");
    var persistedAdvancers = repository.GetRoundAdvancers(tournament.Id, 1);
    AssertEqual(car.Id, persistedAdvancers.Cars.Single().Id);
    repository.CompleteTournament(tournament.Id);
    var report = repository.GetTournamentReport(tournament.Id);
    AssertEqual("Test Tournament", report.Tournament.Name);
    AssertEqual("COMPLETE", report.Status);
    AssertEqual("Test Racer", report.Winner?.RacerName);
    AssertEqual("Test Car Updated", report.Winner?.CarName);
    AssertEqual(1, report.Rows.Count);
    AssertEqual("RedLight", report.Rows.Single().Legality?.ToString());
    Assert(report.Rows.Single().Advanced, "The report should mark the advancing car.");
    AssertEqual(2_500_000L, report.Rows.Single().Interval1Microseconds);
    AssertEqual(5_000_000L, report.Rows.Single().Interval2Microseconds);
    AssertEqual(7_000_000L, report.Rows.Single().SpeedTrapMicroseconds);
    AssertEqual(2_500_000L, report.Rows.Single().Interval1ToInterval2Microseconds);
    AssertEqual(2_000_000L, report.Rows.Single().Interval2ToSpeedTrapMicroseconds);
    AssertEqual(600_000L, report.Rows.Single().SpeedTrapToFinishMicroseconds);
    Assert(report.Rows.Single().IntervalTimersEnabled,
        "The report should retain the interval-timer configuration used for the run.");

    var reportExports = TournamentReportArchiveWriter.Write(report, reportExportDirectory);
    Assert(File.Exists(reportExports.Html), "The HTML tournament report should be exported.");
    Assert(File.Exists(reportExports.Json), "The JSON tournament archive should be exported.");
    Assert(File.Exists(reportExports.Csv), "The CSV tournament results should be exported.");
    using (var archive = JsonDocument.Parse(File.ReadAllText(reportExports.Json!)))
    {
        AssertEqual(
            TournamentReportArchiveWriter.CurrentSchemaVersion,
            archive.RootElement.GetProperty("schemaVersion").GetInt32());
        AssertEqual(
            "Test Tournament",
            archive.RootElement.GetProperty("tournamentReport")
                .GetProperty("tournament")
                .GetProperty("name")
                .GetString());
        AssertEqual(
            2_500_000L,
            archive.RootElement.GetProperty("tournamentReport")
                .GetProperty("rows")[0]
                .GetProperty("interval1ToInterval2Microseconds")
                .GetInt64());
    }
    var csvText = File.ReadAllText(reportExports.Csv!);
    Assert(csvText.Contains("ReactionMicroseconds", StringComparison.Ordinal),
        "The CSV should include stable result headers.");
    Assert(csvText.Contains("Interval1Microseconds", StringComparison.Ordinal),
        "The CSV should include interval-timer fields.");
    Assert(csvText.Contains("Test Racer", StringComparison.Ordinal),
        "The CSV should include tournament entrants.");

    var htmlOnlyDirectory = Path.Combine(reportExportDirectory, "html-only");
    var htmlOnly = TournamentReportArchiveWriter.Write(
        report,
        htmlOnlyDirectory,
        new TournamentReportExportOptions(ExportJson: false, ExportCsv: false));
    AssertEqual<string?>(null, htmlOnly.Json);
    AssertEqual<string?>(null, htmlOnly.Csv);
    AssertEqual(1, Directory.GetFiles(htmlOnlyDirectory).Length);

    var backup = repository.CreateBackup(backupPath);
    AssertEqual(backupPath, backup.Path);
    AssertEqual(1, backup.RacerCount);
    AssertEqual(1, backup.CarCount);
    AssertEqual(1, backup.TournamentCount);
    var backupRepository = new RaceRepository(backupPath);
    AssertEqual("Test Racer", backupRepository.GetRacers().Single().Name);
    AssertEqual("Test Car Updated", backupRepository.GetCars().Single().Name);
    AssertEqual("COMPLETE", backupRepository.GetTournamentReport(tournament.Id).Status);

    repository.RetireCar(car.Id);
    AssertEqual(0, repository.GetCars().Count);
    var restore = repository.RestoreBackup(backupPath, safetyBackupPath);
    AssertEqual(backupPath, restore.RestoredFromPath);
    AssertEqual(safetyBackupPath, restore.SafetyBackupPath);
    AssertEqual(1, restore.RacerCount);
    AssertEqual(1, restore.CarCount);
    AssertEqual(1, restore.TournamentCount);
    AssertEqual("Test Car Updated", repository.GetCars().Single().Name);
    var safetyRepository = new RaceRepository(safetyBackupPath);
    AssertEqual(0, safetyRepository.GetCars().Count);

    File.Copy(backupPath, legacyBackupPath);
    using (var versionConnection = new Microsoft.Data.Sqlite.SqliteConnection(
               $"Data Source={legacyBackupPath};Pooling=False"))
    {
        versionConnection.Open();
        using var versionCommand = versionConnection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version = 3;";
        versionCommand.ExecuteNonQuery();
    }
    repository.RetireCar(car.Id);
    var legacyRestore = repository.RestoreBackup(legacyBackupPath, safetyBackupPath);
    AssertEqual(legacyBackupPath, legacyRestore.RestoredFromPath);
    AssertEqual("Test Car Updated", repository.GetCars().Single().Name);

    Directory.CreateDirectory(automaticBackupDirectory);
    File.Copy(backupPath, Path.Combine(automaticBackupDirectory, "dragWin-auto-20000101.db"));
    File.Copy(backupPath, Path.Combine(automaticBackupDirectory, "dragWin-auto-20000102.db"));
    var automaticBackup = repository.CreateAutomaticBackup(2);
    Assert(automaticBackup is not null, "The first daily automatic backup should be created.");
    Assert(File.Exists(automaticBackup!.Path), "The automatic backup file should exist.");
    AssertEqual(1, automaticBackup.CarCount);
    AssertEqual(2, Directory.EnumerateFiles(
        automaticBackupDirectory,
        "dragWin-auto-*.db").Count());
    AssertEqual<DatabaseBackupResult?>(null, repository.CreateAutomaticBackup(2));

    using (var versionConnection = new Microsoft.Data.Sqlite.SqliteConnection(
               $"Data Source={databasePath};Pooling=False"))
    {
        versionConnection.Open();
        using var versionCommand = versionConnection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version = 3;";
        versionCommand.ExecuteNonQuery();
    }
    _ = new RaceRepository(databasePath, automaticBackupDirectory);
    Assert(Directory.EnumerateFiles(
            automaticBackupDirectory,
            "dragWin-before-schema-v3-to-v4-*.db").Any(),
        "A pre-migration safety backup should be created.");
}
finally
{
    File.Delete(databasePath);
    File.Delete(databasePath + "-shm");
    File.Delete(databasePath + "-wal");
    File.Delete(backupPath);
    File.Delete(backupPath + "-shm");
    File.Delete(backupPath + "-wal");
    File.Delete(legacyBackupPath);
    File.Delete(legacyBackupPath + "-shm");
    File.Delete(legacyBackupPath + "-wal");
    File.Delete(safetyBackupPath);
    File.Delete(safetyBackupPath + "-shm");
    File.Delete(safetyBackupPath + "-wal");
    if (Directory.Exists(automaticBackupDirectory))
    {
        Directory.Delete(automaticBackupDirectory, true);
    }
    if (Directory.Exists(reportExportDirectory))
    {
        Directory.Delete(reportExportDirectory, true);
    }
}

Console.WriteLine("Protocol tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }
}
