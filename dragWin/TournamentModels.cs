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
    bool IsBye);

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
    bool IsBye);
