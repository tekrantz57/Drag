namespace DragWin;

public sealed record Racer(long Id, string Name);

public sealed record Car(
    long Id,
    long RacerId,
    string RacerName,
    string Name,
    int DefaultDialMilliseconds,
    int ByeCount = 0)
{
    public string DisplayName => $"{RacerName} — {Name}";
}

public sealed record Tournament(long Id, string Name, int LaneCount);

public sealed record RoundEntry(
    Car Car,
    int LaneNumber,
    int LaneChoiceOrder,
    bool IsBye,
    int DialMilliseconds);

public sealed record HeatPlan(
    int HeatNumber,
    int AdvanceCount,
    IReadOnlyList<RoundEntry> Entries);

public sealed record RoundPlan(
    int RoundNumber,
    int RandomSeed,
    IReadOnlyList<HeatPlan> Heats);

public enum RunLegality
{
    Legal,
    Breakout,
    RedLight,
    DidNotFinish
}

public sealed record RunResult(
    long CarId,
    RunLegality Legality,
    int FinishOrder,
    long? ReactionMicroseconds,
    long? BreakoutMicroseconds,
    bool IsBye,
    long? ElapsedMicroseconds = null,
    long? SpeedMphX100 = null,
    bool IntervalTimersEnabled = false,
    long? Interval1Microseconds = null,
    long? Interval2Microseconds = null,
    long? SpeedTrapMicroseconds = null);

public sealed record TournamentReport(
    Tournament Tournament,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TournamentReportRow> Rows)
{
    public TournamentReportRow? Winner => Rows
        .Where(row => row.Advanced)
        .OrderByDescending(row => row.RoundNumber)
        .ThenBy(row => row.FinishOrder)
        .FirstOrDefault();
}

public sealed record TournamentReportRow(
    int RoundNumber,
    int HeatNumber,
    int LaneNumber,
    int LaneChoiceOrder,
    string RacerName,
    string CarName,
    int DialMilliseconds,
    bool IsBye,
    RunLegality? Legality,
    int? FinishOrder,
    long? ReactionMicroseconds,
    long? BreakoutMicroseconds,
    bool Advanced,
    DateTimeOffset? ConfirmedAt,
    long? ElapsedMicroseconds = null,
    long? SpeedMphX100 = null,
    bool IntervalTimersEnabled = false,
    long? Interval1Microseconds = null,
    long? Interval2Microseconds = null,
    long? SpeedTrapMicroseconds = null)
{
    public long? Interval1ToInterval2Microseconds =>
        Segment(Interval1Microseconds, Interval2Microseconds);

    public long? Interval2ToSpeedTrapMicroseconds =>
        Segment(Interval2Microseconds, SpeedTrapMicroseconds);

    public long? SpeedTrapToFinishMicroseconds =>
        Segment(SpeedTrapMicroseconds, ElapsedMicroseconds);

    private static long? Segment(long? start, long? end) =>
        start.HasValue && end.HasValue && end >= start ? end - start : null;
}
