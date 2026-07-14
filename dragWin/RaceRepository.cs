using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DragWin;

public sealed class RaceRepository
{
    private readonly string connectionString;

    public RaceRepository(string? databasePath = null)
    {
        DatabasePath = databasePath ?? GetDefaultDatabasePath();
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        Initialize();
    }

    public string DatabasePath { get; }

    public IReadOnlyList<Racer> GetRacers()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name FROM racers ORDER BY name COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        var racers = new List<Racer>();
        while (reader.Read())
        {
            racers.Add(new Racer(reader.GetInt64(0), reader.GetString(1)));
        }
        return racers;
    }

    public IReadOnlyList<Car> GetCars()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.id, c.racer_id, r.name, c.name, c.default_dial_ms,
                   COALESCE(SUM(te.bye_count), 0)
            FROM cars c
            JOIN racers r ON r.id = c.racer_id
            LEFT JOIN tournament_entries te ON te.car_id = c.id
            WHERE c.active = 1
            GROUP BY c.id, c.racer_id, r.name, c.name, c.default_dial_ms
            ORDER BY r.name COLLATE NOCASE, c.name COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        var cars = new List<Car>();
        while (reader.Read())
        {
            cars.Add(new Car(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }
        return cars;
    }

    public IReadOnlyList<Tournament> GetTournaments()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, lane_count
            FROM tournaments
            WHERE status IN ('ACTIVE', 'DRAFT')
            ORDER BY created_utc DESC;
            """;
        using var reader = command.ExecuteReader();
        var tournaments = new List<Tournament>();
        while (reader.Read())
        {
            tournaments.Add(new Tournament(
                reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2)));
        }
        return tournaments;
    }

    public RoundPlan GetLatestRound(long tournamentId)
    {
        using var connection = OpenConnection();
        using var roundCommand = connection.CreateCommand();
        roundCommand.CommandText =
            """
            SELECT id, round_number, random_seed
            FROM rounds
            WHERE tournament_id = $tournament
            ORDER BY round_number DESC LIMIT 1;
            """;
        roundCommand.Parameters.AddWithValue("$tournament", tournamentId);
        using var roundReader = roundCommand.ExecuteReader();
        if (!roundReader.Read())
        {
            throw new InvalidOperationException("The tournament has no rounds.");
        }
        var roundId = roundReader.GetInt64(0);
        var roundNumber = roundReader.GetInt32(1);
        var seed = roundReader.GetInt32(2);
        roundReader.Close();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT h.id, h.heat_number, h.advance_count,
                   c.id, c.racer_id, r.name, c.name, c.default_dial_ms,
                   te.bye_count, he.lane_number, he.lane_choice_order, he.is_bye,
                   he.dial_ms
            FROM heats h
            JOIN heat_entries he ON he.heat_id = h.id
            JOIN tournament_entries te ON te.id = he.tournament_entry_id
            JOIN cars c ON c.id = te.car_id
            JOIN racers r ON r.id = c.racer_id
            WHERE h.round_id = $round
            ORDER BY h.heat_number, he.lane_number;
            """;
        command.Parameters.AddWithValue("$round", roundId);
        using var reader = command.ExecuteReader();
        var heatRows = new List<(long HeatId, int Heat, int Advance, RoundEntry Entry)>();
        while (reader.Read())
        {
            heatRows.Add((
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                new RoundEntry(
                    new Car(
                        reader.GetInt64(3), reader.GetInt64(4),
                        reader.GetString(5), reader.GetString(6),
                        reader.GetInt32(7), reader.GetInt32(8)),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11) != 0,
                    reader.GetInt32(12))));
        }
        return new RoundPlan(
            roundNumber,
            seed,
            heatRows.GroupBy(row => new { row.HeatId, row.Heat, row.Advance })
                .Select(group => new HeatPlan(
                    group.Key.Heat,
                    group.Key.Advance,
                    group.Select(row => row.Entry).ToArray()))
                .ToArray());
    }

    public void UpdateHeatLanes(
        long tournamentId,
        int roundNumber,
        int heatNumber,
        IReadOnlyDictionary<long, int> laneByCarId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var clearCommand = connection.CreateCommand())
        {
            clearCommand.Transaction = transaction;
            clearCommand.CommandText =
                """
                UPDATE heat_entries
                SET lane_number = -id
                WHERE heat_id = (
                    SELECT h.id FROM heats h
                    JOIN rounds ro ON ro.id = h.round_id
                    WHERE ro.tournament_id = $tournament
                      AND ro.round_number = $round
                      AND h.heat_number = $heat
                );
                """;
            clearCommand.Parameters.AddWithValue("$tournament", tournamentId);
            clearCommand.Parameters.AddWithValue("$round", roundNumber);
            clearCommand.Parameters.AddWithValue("$heat", heatNumber);
            clearCommand.ExecuteNonQuery();
        }
        foreach (var assignment in laneByCarId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE heat_entries
                SET lane_number = $lane
                WHERE id = (
                    SELECT he.id FROM heat_entries he
                    JOIN heats h ON h.id = he.heat_id
                    JOIN rounds ro ON ro.id = h.round_id
                    JOIN tournament_entries te ON te.id = he.tournament_entry_id
                    WHERE ro.tournament_id = $tournament
                      AND ro.round_number = $round
                      AND h.heat_number = $heat
                      AND te.car_id = $car
                );
                """;
            command.Parameters.AddWithValue("$lane", assignment.Value);
            command.Parameters.AddWithValue("$tournament", tournamentId);
            command.Parameters.AddWithValue("$round", roundNumber);
            command.Parameters.AddWithValue("$heat", heatNumber);
            command.Parameters.AddWithValue("$car", assignment.Key);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void UpdateHeatDialOverrides(
        long tournamentId,
        int roundNumber,
        int heatNumber,
        IReadOnlyDictionary<long, int> dialMillisecondsByCarId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var overrideDial in dialMillisecondsByCarId)
        {
            if (overrideDial.Value is < 100 or > 60000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dialMillisecondsByCarId),
                    "Dial-in must be between 0.100 and 60.000 seconds.");
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE heat_entries
                SET dial_ms = $dial
                WHERE id = (
                    SELECT he.id FROM heat_entries he
                    JOIN heats h ON h.id = he.heat_id
                    JOIN rounds ro ON ro.id = h.round_id
                    JOIN tournament_entries te ON te.id = he.tournament_entry_id
                    WHERE ro.tournament_id = $tournament
                      AND ro.round_number = $round
                      AND h.heat_number = $heat
                      AND te.car_id = $car
                );
                """;
            command.Parameters.AddWithValue("$dial", overrideDial.Value);
            command.Parameters.AddWithValue("$tournament", tournamentId);
            command.Parameters.AddWithValue("$round", roundNumber);
            command.Parameters.AddWithValue("$heat", heatNumber);
            command.Parameters.AddWithValue("$car", overrideDial.Key);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void SaveHeatResults(
        long tournamentId,
        int roundNumber,
        int heatNumber,
        IReadOnlyList<RunResult> results,
        IReadOnlySet<long> advancingCarIds)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var result in results)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT OR REPLACE INTO heat_results(
                    heat_entry_id, legality, finish_order, reaction_us,
                    breakout_us, advanced)
                SELECT he.id, $legality, $finish, $reaction, $breakout, $advanced
                FROM heat_entries he
                JOIN heats h ON h.id = he.heat_id
                JOIN rounds ro ON ro.id = h.round_id
                JOIN tournament_entries te ON te.id = he.tournament_entry_id
                WHERE ro.tournament_id = $tournament
                  AND ro.round_number = $round
                  AND h.heat_number = $heat
                  AND te.car_id = $car;
                """;
            command.Parameters.AddWithValue("$legality", result.Legality.ToString());
            command.Parameters.AddWithValue("$finish", result.FinishOrder);
            command.Parameters.AddWithValue(
                "$reaction", (object?)result.ReactionMicroseconds ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$breakout", (object?)result.BreakoutMicroseconds ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$advanced", advancingCarIds.Contains(result.CarId) ? 1 : 0);
            command.Parameters.AddWithValue("$tournament", tournamentId);
            command.Parameters.AddWithValue("$round", roundNumber);
            command.Parameters.AddWithValue("$heat", heatNumber);
            command.Parameters.AddWithValue("$car", result.CarId);
            command.ExecuteNonQuery();
        }
        using var confirmation = connection.CreateCommand();
        confirmation.Transaction = transaction;
        confirmation.CommandText =
            """
            INSERT OR REPLACE INTO heat_confirmations(heat_id, confirmed_utc)
            SELECT h.id, $confirmed
            FROM heats h JOIN rounds ro ON ro.id = h.round_id
            WHERE ro.tournament_id = $tournament
              AND ro.round_number = $round AND h.heat_number = $heat;
            """;
        confirmation.Parameters.AddWithValue("$confirmed", DateTimeOffset.UtcNow.ToString("O"));
        confirmation.Parameters.AddWithValue("$tournament", tournamentId);
        confirmation.Parameters.AddWithValue("$round", roundNumber);
        confirmation.Parameters.AddWithValue("$heat", heatNumber);
        confirmation.ExecuteNonQuery();
        transaction.Commit();
    }

    public bool IsRoundConfirmed(long tournamentId, int roundNumber)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) = (
                SELECT COUNT(*) FROM heats h2
                JOIN rounds r2 ON r2.id = h2.round_id
                WHERE r2.tournament_id = $tournament AND r2.round_number = $round
            )
            FROM heat_confirmations hc
            JOIN heats h ON h.id = hc.heat_id
            JOIN rounds r ON r.id = h.round_id
            WHERE r.tournament_id = $tournament AND r.round_number = $round;
            """;
        command.Parameters.AddWithValue("$tournament", tournamentId);
        command.Parameters.AddWithValue("$round", roundNumber);
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }

    public IReadOnlySet<int> GetConfirmedHeatNumbers(
        long tournamentId,
        int roundNumber)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT h.heat_number
            FROM heat_confirmations hc
            JOIN heats h ON h.id = hc.heat_id
            JOIN rounds r ON r.id = h.round_id
            WHERE r.tournament_id = $tournament AND r.round_number = $round;
            """;
        command.Parameters.AddWithValue("$tournament", tournamentId);
        command.Parameters.AddWithValue("$round", roundNumber);
        using var reader = command.ExecuteReader();
        var numbers = new HashSet<int>();
        while (reader.Read()) numbers.Add(reader.GetInt32(0));
        return numbers;
    }

    public void CompleteTournament(long tournamentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE tournaments SET status = 'COMPLETE' WHERE id = $id;";
        command.Parameters.AddWithValue("$id", tournamentId);
        command.ExecuteNonQuery();
    }

    public (IReadOnlyList<Car> Cars, IReadOnlyDictionary<long, long?> Reactions)
        GetRoundAdvancers(long tournamentId, int roundNumber)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.id, c.racer_id, r.name, c.name, c.default_dial_ms,
                   te.bye_count, hr.reaction_us
            FROM heat_results hr
            JOIN heat_entries he ON he.id = hr.heat_entry_id
            JOIN tournament_entries te ON te.id = he.tournament_entry_id
            JOIN cars c ON c.id = te.car_id
            JOIN racers r ON r.id = c.racer_id
            JOIN heats h ON h.id = he.heat_id
            JOIN rounds ro ON ro.id = h.round_id
            WHERE ro.tournament_id = $tournament
              AND ro.round_number = $round AND hr.advanced = 1;
            """;
        command.Parameters.AddWithValue("$tournament", tournamentId);
        command.Parameters.AddWithValue("$round", roundNumber);
        using var reader = command.ExecuteReader();
        var cars = new List<Car>();
        var reactions = new Dictionary<long, long?>();
        while (reader.Read())
        {
            var car = new Car(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5));
            cars.Add(car);
            reactions[car.Id] = reader.IsDBNull(6) ? null : reader.GetInt64(6);
        }
        return (cars, reactions);
    }

    public TournamentReport GetTournamentReport(long tournamentId)
    {
        using var connection = OpenConnection();
        using var tournamentCommand = connection.CreateCommand();
        tournamentCommand.CommandText =
            """
            SELECT id, name, lane_count, status, created_utc
            FROM tournaments
            WHERE id = $tournament;
            """;
        tournamentCommand.Parameters.AddWithValue("$tournament", tournamentId);
        using var tournamentReader = tournamentCommand.ExecuteReader();
        if (!tournamentReader.Read())
        {
            throw new InvalidOperationException("Tournament not found.");
        }

        var tournament = new Tournament(
            tournamentReader.GetInt64(0),
            tournamentReader.GetString(1),
            tournamentReader.GetInt32(2));
        var status = tournamentReader.GetString(3);
        var createdAt = DateTimeOffset.Parse(
            tournamentReader.GetString(4),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        tournamentReader.Close();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ro.round_number, h.heat_number, he.lane_number,
                   he.lane_choice_order, r.name, c.name, he.dial_ms,
                   he.is_bye, hr.legality, hr.finish_order, hr.reaction_us,
                   hr.breakout_us, hr.advanced, hc.confirmed_utc
            FROM rounds ro
            JOIN heats h ON h.round_id = ro.id
            JOIN heat_entries he ON he.heat_id = h.id
            JOIN tournament_entries te ON te.id = he.tournament_entry_id
            JOIN cars c ON c.id = te.car_id
            JOIN racers r ON r.id = c.racer_id
            LEFT JOIN heat_results hr ON hr.heat_entry_id = he.id
            LEFT JOIN heat_confirmations hc ON hc.heat_id = h.id
            WHERE ro.tournament_id = $tournament
            ORDER BY ro.round_number, h.heat_number, he.lane_number;
            """;
        command.Parameters.AddWithValue("$tournament", tournamentId);
        using var reader = command.ExecuteReader();
        var rows = new List<TournamentReportRow>();
        while (reader.Read())
        {
            RunLegality? legality = null;
            if (!reader.IsDBNull(8) &&
                Enum.TryParse<RunLegality>(reader.GetString(8), out var parsedLegality))
            {
                legality = parsedLegality;
            }

            DateTimeOffset? confirmedAt = null;
            if (!reader.IsDBNull(13))
            {
                confirmedAt = DateTimeOffset.Parse(
                    reader.GetString(13),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
            }

            rows.Add(new TournamentReportRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7) != 0,
                legality,
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt64(10),
                reader.IsDBNull(11) ? null : reader.GetInt64(11),
                !reader.IsDBNull(12) && reader.GetInt32(12) != 0,
                confirmedAt));
        }

        return new TournamentReport(tournament, status, createdAt, rows);
    }

    public Racer AddRacer(string name)
    {
        name = RequiredName(name, nameof(name));
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO racers(name, created_utc) VALUES ($name, $created); " +
            "SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        var id = (long)command.ExecuteScalar()!;
        return new Racer(id, name);
    }

    public Car AddCar(long racerId, string name, int defaultDialMilliseconds)
    {
        name = RequiredName(name, nameof(name));
        if (defaultDialMilliseconds is < 100 or > 60000)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultDialMilliseconds));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO cars(racer_id, name, default_dial_ms, active, created_utc)
            VALUES ($racer, $name, $dial, 1, $created);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$racer", racerId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$dial", defaultDialMilliseconds);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        var id = (long)command.ExecuteScalar()!;
        var racer = GetRacers().Single(item => item.Id == racerId);
        return new Car(id, racerId, racer.Name, name, defaultDialMilliseconds);
    }

    public Car UpdateCar(
        long carId,
        long racerId,
        string name,
        int defaultDialMilliseconds)
    {
        name = RequiredName(name, nameof(name));
        if (defaultDialMilliseconds is < 100 or > 60000)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultDialMilliseconds));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE cars
            SET racer_id = $racer, name = $name, default_dial_ms = $dial
            WHERE id = $car AND active = 1;
            """;
        command.Parameters.AddWithValue("$racer", racerId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$dial", defaultDialMilliseconds);
        command.Parameters.AddWithValue("$car", carId);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The selected car could not be updated.");
        }

        var racer = GetRacers().Single(item => item.Id == racerId);
        return new Car(carId, racerId, racer.Name, name, defaultDialMilliseconds);
    }

    public void RetireCar(long carId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE cars
            SET active = 0
            WHERE id = $car AND active = 1;
            """;
        command.Parameters.AddWithValue("$car", carId);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The selected car could not be retired.");
        }
    }

    public Tournament CreateTournament(
        string name,
        int laneCount,
        IReadOnlyCollection<long> carIds)
    {
        name = RequiredName(name, nameof(name));
        if (laneCount is not (2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(laneCount));
        }
        if (carIds.Count == 0 || carIds.Distinct().Count() != carIds.Count)
        {
            throw new ArgumentException("Select one or more unique cars.", nameof(carIds));
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var tournamentCommand = connection.CreateCommand();
        tournamentCommand.Transaction = transaction;
        tournamentCommand.CommandText =
            """
            INSERT INTO tournaments(name, lane_count, status, created_utc)
            VALUES ($name, $lanes, 'ACTIVE', $created);
            SELECT last_insert_rowid();
            """;
        tournamentCommand.Parameters.AddWithValue("$name", name);
        tournamentCommand.Parameters.AddWithValue("$lanes", laneCount);
        tournamentCommand.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        var tournamentId = (long)tournamentCommand.ExecuteScalar()!;

        foreach (var carId in carIds)
        {
            using var entryCommand = connection.CreateCommand();
            entryCommand.Transaction = transaction;
            entryCommand.CommandText =
                """
                INSERT INTO tournament_entries(tournament_id, car_id, bye_count, active)
                VALUES ($tournament, $car, 0, 1);
                """;
            entryCommand.Parameters.AddWithValue("$tournament", tournamentId);
            entryCommand.Parameters.AddWithValue("$car", carId);
            entryCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return new Tournament(tournamentId, name, laneCount);
    }

    public void SaveRound(long tournamentId, RoundPlan round)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var roundCommand = connection.CreateCommand();
        roundCommand.Transaction = transaction;
        roundCommand.CommandText =
            """
            INSERT INTO rounds(tournament_id, round_number, random_seed, created_utc)
            VALUES ($tournament, $number, $seed, $created);
            SELECT last_insert_rowid();
            """;
        roundCommand.Parameters.AddWithValue("$tournament", tournamentId);
        roundCommand.Parameters.AddWithValue("$number", round.RoundNumber);
        roundCommand.Parameters.AddWithValue("$seed", round.RandomSeed);
        roundCommand.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        var roundId = (long)roundCommand.ExecuteScalar()!;

        foreach (var heat in round.Heats)
        {
            using var heatCommand = connection.CreateCommand();
            heatCommand.Transaction = transaction;
            heatCommand.CommandText =
                """
                INSERT INTO heats(round_id, heat_number, advance_count)
                VALUES ($round, $number, $advance);
                SELECT last_insert_rowid();
                """;
            heatCommand.Parameters.AddWithValue("$round", roundId);
            heatCommand.Parameters.AddWithValue("$number", heat.HeatNumber);
            heatCommand.Parameters.AddWithValue("$advance", heat.AdvanceCount);
            var heatId = (long)heatCommand.ExecuteScalar()!;

            foreach (var entry in heat.Entries)
            {
                using var entryCommand = connection.CreateCommand();
                entryCommand.Transaction = transaction;
                entryCommand.CommandText =
                    """
                    INSERT INTO heat_entries(
                        heat_id, tournament_entry_id, lane_number,
                        lane_choice_order, is_bye, dial_ms)
                    SELECT $heat, id, $lane, $choice, $bye, $dial
                    FROM tournament_entries
                    WHERE tournament_id = $tournament AND car_id = $car;

                    UPDATE tournament_entries
                    SET bye_count = bye_count + $bye
                    WHERE tournament_id = $tournament AND car_id = $car;
                    """;
                entryCommand.Parameters.AddWithValue("$heat", heatId);
                entryCommand.Parameters.AddWithValue("$lane", entry.LaneNumber);
                entryCommand.Parameters.AddWithValue("$choice", entry.LaneChoiceOrder);
                entryCommand.Parameters.AddWithValue("$bye", entry.IsBye ? 1 : 0);
                entryCommand.Parameters.AddWithValue("$dial", entry.DialMilliseconds);
                entryCommand.Parameters.AddWithValue("$tournament", tournamentId);
                entryCommand.Parameters.AddWithValue("$car", entry.Car.Id);
                entryCommand.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS racers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                created_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS cars (
                id INTEGER PRIMARY KEY,
                racer_id INTEGER NOT NULL REFERENCES racers(id),
                name TEXT NOT NULL COLLATE NOCASE,
                default_dial_ms INTEGER NOT NULL CHECK(default_dial_ms BETWEEN 100 AND 60000),
                active INTEGER NOT NULL CHECK(active IN (0, 1)),
                created_utc TEXT NOT NULL,
                UNIQUE(racer_id, name)
            );
            CREATE TABLE IF NOT EXISTS tournaments (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                lane_count INTEGER NOT NULL CHECK(lane_count IN (2, 4)),
                status TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS tournament_entries (
                id INTEGER PRIMARY KEY,
                tournament_id INTEGER NOT NULL REFERENCES tournaments(id),
                car_id INTEGER NOT NULL REFERENCES cars(id),
                bye_count INTEGER NOT NULL DEFAULT 0,
                active INTEGER NOT NULL CHECK(active IN (0, 1)),
                UNIQUE(tournament_id, car_id)
            );
            CREATE TABLE IF NOT EXISTS rounds (
                id INTEGER PRIMARY KEY,
                tournament_id INTEGER NOT NULL REFERENCES tournaments(id),
                round_number INTEGER NOT NULL,
                random_seed INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                UNIQUE(tournament_id, round_number)
            );
            CREATE TABLE IF NOT EXISTS heats (
                id INTEGER PRIMARY KEY,
                round_id INTEGER NOT NULL REFERENCES rounds(id),
                heat_number INTEGER NOT NULL,
                advance_count INTEGER NOT NULL,
                UNIQUE(round_id, heat_number)
            );
            CREATE TABLE IF NOT EXISTS heat_entries (
                id INTEGER PRIMARY KEY,
                heat_id INTEGER NOT NULL REFERENCES heats(id),
                tournament_entry_id INTEGER NOT NULL REFERENCES tournament_entries(id),
                lane_number INTEGER NOT NULL,
                lane_choice_order INTEGER NOT NULL,
                is_bye INTEGER NOT NULL CHECK(is_bye IN (0, 1)),
                dial_ms INTEGER NOT NULL DEFAULT 10000 CHECK(dial_ms BETWEEN 100 AND 60000),
                UNIQUE(heat_id, tournament_entry_id),
                UNIQUE(heat_id, lane_number)
            );
            CREATE TABLE IF NOT EXISTS heat_results (
                heat_entry_id INTEGER PRIMARY KEY REFERENCES heat_entries(id),
                legality TEXT NOT NULL,
                finish_order INTEGER NOT NULL,
                reaction_us INTEGER,
                breakout_us INTEGER,
                advanced INTEGER NOT NULL CHECK(advanced IN (0, 1))
            );
            CREATE TABLE IF NOT EXISTS heat_confirmations (
                heat_id INTEGER PRIMARY KEY REFERENCES heats(id),
                confirmed_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureHeatEntryDialColumn(connection);
        SetUserVersion(connection, 3);
    }

    private static void EnsureHeatEntryDialColumn(SqliteConnection connection)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = "PRAGMA table_info(heat_entries);";
        using (var reader = checkCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "dial_ms", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText =
            """
            ALTER TABLE heat_entries
            ADD COLUMN dial_ms INTEGER NOT NULL DEFAULT 10000
            CHECK(dial_ms BETWEEN 100 AND 60000);
            """;
        alterCommand.ExecuteNonQuery();

        using var backfillCommand = connection.CreateCommand();
        backfillCommand.CommandText =
            """
            UPDATE heat_entries
            SET dial_ms = (
                SELECT c.default_dial_ms
                FROM tournament_entries te
                JOIN cars c ON c.id = te.car_id
                WHERE te.id = heat_entries.tournament_entry_id
            );
            """;
        backfillCommand.ExecuteNonQuery();
    }

    private static void SetUserVersion(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string RequiredName(string value, string parameterName)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            throw new ArgumentException("A name is required.", parameterName);
        }
        return value;
    }

    private static string GetDefaultDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dragWin");
        return Path.Combine(directory, "dragWin.db");
    }
}
