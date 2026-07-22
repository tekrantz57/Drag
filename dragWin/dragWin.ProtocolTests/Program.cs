using DragWin;

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
var advancers = planner.SelectAdvancers(
    competitiveHeat,
    [
        new RunResult(1, RunLegality.Breakout, 1, 10000, 5000, false),
        new RunResult(2, RunLegality.Legal, 3, 20000, null, false),
        new RunResult(3, RunLegality.Breakout, 2, 15000, 2000, false),
        new RunResult(4, RunLegality.RedLight, 4, -1000, null, false)
    ]);
AssertEqual(2L, advancers[0].CarId);
AssertEqual(3L, advancers[1].CarId);

var byeHeat = new HeatPlan(
    1,
    1,
    [new RoundEntry(cars[0], 1, 1, true, cars[0].DefaultDialMilliseconds)]);
var byeAdvancer = planner.SelectAdvancers(
    byeHeat,
    [new RunResult(1, RunLegality.RedLight, 1, -5000, null, true)]);
AssertEqual(1L, byeAdvancer.Single().CarId);
var demoMessages = DemoHeatSimulator.CreateBracketHeatMessages(competitiveHeat, randomSeed: 7);
Assert(
    demoMessages.Any(item => item.Type == "EVENT" && item.Parts[1] == "TREE" && item.Parts[2] == "RACE_COMPLETE"),
    "A demo heat should complete the tree.");
Assert(
    demoMessages.Any(item => item.Type == "RESULT" && item.Parts[1] == "WINNER"),
    "A demo heat should report a winner when at least one car makes a legal or breakout pass.");
Assert(
    demoMessages.Any(item => item.Type == "RESULT" && item.Parts.Count > 3 && item.Parts[3] == "SPEED_MPH_X100"),
    "A demo heat should include speed-trap output.");
var demoByeMessages = DemoHeatSimulator.CreateBracketHeatMessages(byeHeat, randomSeed: 7);
Assert(
    demoByeMessages.Any(item => item.Type == "EVENT" && item.Parts.Count > 3 && item.Parts[3] == "REACTION_US"),
    "A demo BYE pass should still produce a reaction time.");
Assert(
    demoByeMessages.All(item => !(item.Type == "EVENT" && item.Parts.Count > 3 && item.Parts[3] == "FOUL")),
    "A demo BYE pass should not red-light.");

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
try
{
    var repository = new RaceRepository(databasePath);
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
        car.Id, RunLegality.RedLight, 1, -1000, null, true);
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
    repository.RetireCar(car.Id);
    AssertEqual(0, repository.GetCars().Count);
}
finally
{
    File.Delete(databasePath);
    File.Delete(databasePath + "-shm");
    File.Delete(databasePath + "-wal");
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
