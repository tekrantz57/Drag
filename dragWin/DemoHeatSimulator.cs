namespace DragWin;

public static class DemoHeatSimulator
{
    private const int AmberSequenceMicroseconds = 1_500_000;

    public static IReadOnlyList<ProtocolMessage> CreateBracketHeatMessages(
        HeatPlan heat,
        int? randomSeed = null)
    {
        ArgumentNullException.ThrowIfNull(heat);

        var random = randomSeed.HasValue ? new Random(randomSeed.Value) : Random.Shared;
        var slowestDialMilliseconds = heat.Entries.Max(entry => entry.DialMilliseconds);
        var laneResults = heat.Entries
            .Select(entry => CreateLaneResult(entry, slowestDialMilliseconds, random))
            .ToArray();

        var messages = new List<ProtocolMessage>
        {
            ProtocolMessage.Create("EVENT", "TREE", "BRACKET_START")
        };

        foreach (var result in laneResults.OrderBy(result => result.GreenOffsetUs))
        {
            messages.Add(ProtocolMessage.Create("EVENT", "LANE", result.Entry.LaneNumber.ToString(), "GREEN"));
        }

        foreach (var result in laneResults.OrderBy(result => result.LaunchOffsetUs))
        {
            if (result.Fouled)
            {
                messages.Add(ProtocolMessage.Create("EVENT", "LANE", result.Entry.LaneNumber.ToString(), "FOUL"));
                continue;
            }

            messages.Add(ProtocolMessage.Create(
                "EVENT",
                "LANE",
                result.Entry.LaneNumber.ToString(),
                "REACTION_US",
                result.ReactionUs.ToString()));
        }

        foreach (var result in laneResults
                     .Where(result => !result.Fouled)
                     .OrderBy(result => result.FinishOffsetUs))
        {
            messages.Add(ProtocolMessage.Create(
                "RESULT",
                "LANE",
                result.Entry.LaneNumber.ToString(),
                "ELAPSED_US",
                result.ElapsedUs.ToString()));
            messages.Add(result.BreakoutUs.HasValue
                ? ProtocolMessage.Create(
                    "RESULT",
                    "LANE",
                    result.Entry.LaneNumber.ToString(),
                    "BREAKOUT_US",
                    result.BreakoutUs.Value.ToString())
                : ProtocolMessage.Create(
                    "RESULT",
                    "LANE",
                    result.Entry.LaneNumber.ToString(),
                    "VALID"));
            messages.Add(ProtocolMessage.Create(
                "RESULT",
                "LANE",
                result.Entry.LaneNumber.ToString(),
                "SPEED_MPH_X100",
                result.SpeedMphX100.ToString()));
        }

        var winner = SelectBracketWinner(laneResults);
        messages.Add(winner is null
            ? ProtocolMessage.Create("RESULT", "NO_WINNER")
            : ProtocolMessage.Create("RESULT", "WINNER", "LANE", winner.Entry.LaneNumber.ToString()));
        messages.Add(ProtocolMessage.Create("EVENT", "TREE", "RACE_COMPLETE"));
        return messages;
    }

    public static IReadOnlyList<ProtocolMessage> CreatePracticeMessages(
        IReadOnlyDictionary<int, int> dialMillisecondsByLane,
        bool bracketMode,
        int? randomSeed = null)
    {
        ArgumentNullException.ThrowIfNull(dialMillisecondsByLane);
        if (dialMillisecondsByLane.Count == 0)
        {
            throw new ArgumentException("At least one lane is required.", nameof(dialMillisecondsByLane));
        }

        var random = randomSeed.HasValue ? new Random(randomSeed.Value) : Random.Shared;
        var entries = dialMillisecondsByLane
            .OrderBy(item => item.Key)
            .Select(item => new RoundEntry(
                new Car(item.Key, item.Key, $"Lane {item.Key}", $"Practice {item.Key}", item.Value),
                item.Key,
                item.Key,
                false,
                item.Value))
            .ToArray();
        var slowestDialMilliseconds = bracketMode
            ? entries.Max(entry => entry.DialMilliseconds)
            : entries.Min(entry => entry.DialMilliseconds);
        var laneResults = entries
            .Select(entry => CreateLaneResult(
                entry,
                slowestDialMilliseconds,
                random,
                allowFoul: true,
                allowBreakout: bracketMode,
                useBracketDelay: bracketMode))
            .ToArray();

        var messages = new List<ProtocolMessage>
        {
            ProtocolMessage.Create("EVENT", "TREE", bracketMode ? "BRACKET_START" : "HEADS_UP_START")
        };

        foreach (var result in laneResults.OrderBy(result => result.GreenOffsetUs))
        {
            messages.Add(ProtocolMessage.Create("EVENT", "LANE", result.Entry.LaneNumber.ToString(), "GREEN"));
        }

        foreach (var result in laneResults.OrderBy(result => result.LaunchOffsetUs))
        {
            if (result.Fouled)
            {
                messages.Add(ProtocolMessage.Create("EVENT", "LANE", result.Entry.LaneNumber.ToString(), "FOUL"));
                continue;
            }

            messages.Add(ProtocolMessage.Create(
                "EVENT",
                "LANE",
                result.Entry.LaneNumber.ToString(),
                "REACTION_US",
                result.ReactionUs.ToString()));
        }

        var finishers = laneResults
            .Where(result => !result.Fouled)
            .OrderBy(result => result.FinishOffsetUs)
            .ToArray();
        foreach (var result in finishers)
        {
            messages.Add(ProtocolMessage.Create(
                "RESULT",
                "LANE",
                result.Entry.LaneNumber.ToString(),
                "ELAPSED_US",
                result.ElapsedUs.ToString()));
            messages.Add(result.BreakoutUs.HasValue
                ? ProtocolMessage.Create(
                    "RESULT",
                    "LANE",
                    result.Entry.LaneNumber.ToString(),
                    "BREAKOUT_US",
                    result.BreakoutUs.Value.ToString())
                : ProtocolMessage.Create(
                    "RESULT",
                    "LANE",
                    result.Entry.LaneNumber.ToString(),
                    "VALID"));
            messages.Add(ProtocolMessage.Create(
                "RESULT",
                "LANE",
                result.Entry.LaneNumber.ToString(),
                "SPEED_MPH_X100",
                result.SpeedMphX100.ToString()));
        }

        if (bracketMode)
        {
            var winner = SelectBracketWinner(laneResults);
            messages.Add(winner is null
                ? ProtocolMessage.Create("RESULT", "NO_WINNER")
                : ProtocolMessage.Create("RESULT", "WINNER", "LANE", winner.Entry.LaneNumber.ToString()));
        }
        else
        {
            var place = 1;
            foreach (var result in finishers)
            {
                messages.Add(ProtocolMessage.Create(
                    "RESULT",
                    "PLACE",
                    place.ToString(),
                    "LANE",
                    result.Entry.LaneNumber.ToString()));
                place++;
            }
            if (place == 1)
            {
                messages.Add(ProtocolMessage.Create("RESULT", "NO_WINNER"));
            }
        }

        messages.Add(ProtocolMessage.Create("EVENT", "TREE", "RACE_COMPLETE"));
        return messages;
    }

    private static DemoLaneResult CreateLaneResult(
        RoundEntry entry,
        int slowestDialMilliseconds,
        Random random,
        bool allowFoul = true,
        bool allowBreakout = true,
        bool useBracketDelay = true)
    {
        var laneDelayUs = useBracketDelay
            ? (slowestDialMilliseconds - entry.DialMilliseconds) * 1000L
            : 0L;
        var greenOffsetUs = laneDelayUs + AmberSequenceMicroseconds;
        var fouled = allowFoul && !entry.IsBye && random.NextDouble() < 0.08;
        var reactionUs = fouled
            ? -random.Next(1_000, 35_000)
            : random.Next(40_000, 280_000);

        var elapsedAdjustmentUs = !allowBreakout
            ? random.Next(30_000, 280_000)
            : entry.IsBye
            ? random.Next(30_000, 180_000)
            : random.Next(-90_000, 260_000);
        var elapsedUs = Math.Max(100_000L, entry.DialMilliseconds * 1000L + elapsedAdjustmentUs);
        var breakoutUs = allowBreakout && elapsedUs < entry.DialMilliseconds * 1000L
            ? entry.DialMilliseconds * 1000L - elapsedUs
            : (long?)null;
        var launchOffsetUs = greenOffsetUs + reactionUs;
        var finishOffsetUs = launchOffsetUs + elapsedUs;
        var speedMphX100 = random.Next(1_250, 2_850);

        return new DemoLaneResult(
            entry,
            fouled,
            reactionUs,
            elapsedUs,
            breakoutUs,
            speedMphX100,
            greenOffsetUs,
            launchOffsetUs,
            finishOffsetUs);
    }

    private static DemoLaneResult? SelectBracketWinner(IReadOnlyList<DemoLaneResult> results)
    {
        var legalWinner = results
            .Where(result => !result.Fouled && !result.BreakoutUs.HasValue)
            .OrderBy(result => result.FinishOffsetUs)
            .FirstOrDefault();
        if (legalWinner is not null)
        {
            return legalWinner;
        }

        return results
            .Where(result => !result.Fouled && result.BreakoutUs.HasValue)
            .OrderBy(result => result.BreakoutUs)
            .FirstOrDefault();
    }

    private sealed record DemoLaneResult(
        RoundEntry Entry,
        bool Fouled,
        long ReactionUs,
        long ElapsedUs,
        long? BreakoutUs,
        int SpeedMphX100,
        long GreenOffsetUs,
        long LaunchOffsetUs,
        long FinishOffsetUs);
}
