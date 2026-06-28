namespace DragWin;

public sealed class TournamentPlanner
{
    private const int CandidateCount = 2000;

    public RoundPlan CreateRound(
        IReadOnlyList<Car> cars,
        int laneCount,
        int roundNumber,
        int? randomSeed = null,
        IReadOnlyDictionary<long, long?>? priorReactionMicroseconds = null)
    {
        ArgumentNullException.ThrowIfNull(cars);
        if (laneCount is not (2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(laneCount), "Lane count must be 2 or 4.");
        }
        if (cars.Count == 0)
        {
            throw new ArgumentException("At least one car is required.", nameof(cars));
        }
        if (cars.Select(car => car.Id).Distinct().Count() != cars.Count)
        {
            throw new ArgumentException("A car can appear only once in a round.", nameof(cars));
        }

        var seed = randomSeed ?? Random.Shared.Next();
        var random = new Random(seed);
        var heatCount = (cars.Count + laneCount - 1) / laneCount;
        List<List<Car>>? bestCandidate = null;
        long bestScore = long.MaxValue;

        for (var attempt = 0; attempt < CandidateCount; attempt++)
        {
            var shuffled = cars.OrderBy(_ => random.Next()).ToArray();
            var candidate = new List<List<Car>>(heatCount);
            for (var heat = 0; heat < heatCount; heat++)
            {
                candidate.Add([]);
            }

            for (var index = 0; index < shuffled.Length; index++)
            {
                candidate[index / laneCount].Add(shuffled[index]);
            }

            var score = ScoreCandidate(candidate, laneCount);
            if (score < bestScore)
            {
                bestScore = score;
                bestCandidate = candidate;
                if (score == 0)
                {
                    break;
                }
            }
        }

        var advanceCount = laneCount / 2;
        var physicalLanes = laneCount == 2 ? new[] { 1, 4 } : new[] { 1, 2, 3, 4 };
        var heats = bestCandidate!
            .Select((heatCars, heatIndex) =>
            {
                var isByeHeat = heatCars.Count <= advanceCount;
                var laneOrder = physicalLanes.OrderBy(_ => random.Next()).ToArray();
                var choiceOrder = OrderForLaneChoice(
                    heatCars,
                    roundNumber,
                    random,
                    priorReactionMicroseconds);

                var entries = choiceOrder
                    .Select((car, index) => new RoundEntry(
                        car,
                        laneOrder[index],
                        index + 1,
                        isByeHeat))
                    .ToArray();

                return new HeatPlan(heatIndex + 1, advanceCount, entries);
            })
            .ToArray();

        return new RoundPlan(roundNumber, seed, heats);
    }

    public IReadOnlyList<RunResult> SelectAdvancers(
        HeatPlan heat,
        IReadOnlyList<RunResult> results)
    {
        ArgumentNullException.ThrowIfNull(heat);
        ArgumentNullException.ThrowIfNull(results);

        var resultByCar = results.ToDictionary(result => result.CarId);
        var ordered = heat.Entries
            .Select(entry => resultByCar.TryGetValue(entry.Car.Id, out var result)
                ? result with { IsBye = entry.IsBye }
                : new RunResult(
                    entry.Car.Id,
                    RunLegality.DidNotFinish,
                    int.MaxValue,
                    null,
                    null,
                    entry.IsBye))
            .OrderBy(result => result.IsBye ? -1 : LegalityRank(result.Legality))
            .ThenBy(result => result.IsBye ? 0 : LegalityDetail(result))
            .ThenBy(result => result.FinishOrder)
            .Take(Math.Min(heat.AdvanceCount, heat.Entries.Count))
            .ToArray();

        return ordered;
    }

    private static long ScoreCandidate(
        IReadOnlyList<List<Car>> candidate,
        int laneCount)
    {
        long ownerCollisionScore = 0;
        long byeFairnessScore = 0;
        var advanceCount = laneCount / 2;

        foreach (var heat in candidate)
        {
            ownerCollisionScore += heat
                .GroupBy(car => car.RacerId)
                .Sum(group => (long)group.Count() * (group.Count() - 1) / 2);

            if (heat.Count <= advanceCount)
            {
                byeFairnessScore += heat.Sum(car => car.ByeCount);
            }
        }

        return ownerCollisionScore * 1_000_000L + byeFairnessScore;
    }

    private static IReadOnlyList<Car> OrderForLaneChoice(
        IReadOnlyList<Car> cars,
        int roundNumber,
        Random random,
        IReadOnlyDictionary<long, long?>? priorReactions)
    {
        if (roundNumber <= 1 || priorReactions is null)
        {
            return cars.OrderBy(_ => random.Next()).ToArray();
        }

        return cars
            .Select(car => new
            {
                Car = car,
                Reaction = priorReactions.TryGetValue(car.Id, out var reaction) &&
                           reaction >= 0
                    ? reaction
                    : null,
                TieBreaker = random.Next()
            })
            .OrderBy(item => item.Reaction.HasValue ? 0 : 1)
            .ThenBy(item => item.Reaction ?? long.MaxValue)
            .ThenBy(item => item.TieBreaker)
            .Select(item => item.Car)
            .ToArray();
    }

    private static int LegalityRank(RunLegality legality) => legality switch
    {
        RunLegality.Legal => 0,
        RunLegality.Breakout => 1,
        RunLegality.RedLight => 2,
        RunLegality.DidNotFinish => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(legality))
    };

    private static long LegalityDetail(RunResult result) => result.Legality switch
    {
        RunLegality.Breakout => result.BreakoutMicroseconds ?? long.MaxValue,
        RunLegality.RedLight => result.ReactionMicroseconds.HasValue
            ? Math.Abs(result.ReactionMicroseconds.Value)
            : long.MaxValue,
        _ => 0
    };
}
