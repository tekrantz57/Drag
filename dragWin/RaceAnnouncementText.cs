using System.Globalization;

namespace DragWin;

public sealed record PracticeAnnouncementResult(
    int Lane,
    long? ElapsedMicroseconds,
    long? SpeedMphX100,
    bool RedLight,
    bool Breakout,
    bool DidNotFinish);

public static class RaceAnnouncementText
{
    public static string HeatLineup(int roundNumber, HeatPlan heat)
    {
        var advanceText = heat.AdvanceCount == 1
            ? "One car advances"
            : $"{heat.AdvanceCount} cars advance";
        var entries = heat.Entries
            .OrderBy(entry => entry.LaneNumber)
            .Select(LineupEntry);
        return $"Round {roundNumber}, heat {heat.HeatNumber}. {advanceText}. " +
               $"{string.Join(". ", entries)}.";
    }

    public static string LaneChoicePrompt(Car car) =>
        $"{Entrant(car)}, please choose a lane.";

    public static string LaneChoiceConfirmed(Car car, int lane) =>
        $"{Entrant(car)} has selected lane {lane}.";

    public static string HeatArmed(IEnumerable<int> lanes)
    {
        var ordered = lanes.Distinct().Order().ToArray();
        var laneText = ordered.Length == 1
            ? $"lane {ordered[0]}"
            : $"lanes {Join(ordered.Select(lane => lane.ToString(CultureInfo.InvariantCulture)))}";
        return $"Heat armed. Please stage {laneText}.";
    }

    public static string HeatComplete(IEnumerable<Car> advancingCars)
    {
        var entrants = advancingCars.Select(Entrant).ToArray();
        return entrants.Length == 0
            ? "Heat complete. No cars advance."
            : $"Heat complete. Advancing: {Join(entrants)}.";
    }

    public static string TournamentComplete(Car? champion, Car? runnerUp)
    {
        if (champion is null)
        {
            return "Tournament complete with no winner.";
        }

        var result = $"Tournament complete. Champion, {Entrant(champion)}.";
        return runnerUp is null
            ? result
            : $"{result} Runner-up, {Entrant(runnerUp)}.";
    }

    public static string PracticeArmed(IEnumerable<int> lanes)
    {
        var ordered = lanes.Distinct().Order().ToArray();
        return ordered.Length == 1
            ? $"Practice pass armed for lane {ordered[0]}."
            : $"Practice pass armed for lanes {Join(ordered.Select(lane => lane.ToString(CultureInfo.InvariantCulture)))}.";
    }

    public static string PracticeComplete(IEnumerable<PracticeAnnouncementResult> results)
    {
        var laneResults = results.OrderBy(result => result.Lane).Select(PracticeLane).ToArray();
        return laneResults.Length == 0
            ? "Practice pass complete."
            : $"Practice pass complete. {string.Join(". ", laneResults)}.";
    }

    private static string LineupEntry(RoundEntry entry)
    {
        var text = $"Lane {entry.LaneNumber}, {Entrant(entry.Car)}";
        return entry.IsBye ? $"{text}, bye pass, guaranteed to advance" : text;
    }

    private static string PracticeLane(PracticeAnnouncementResult result)
    {
        var parts = new List<string> { $"Lane {result.Lane}" };
        if (result.RedLight)
        {
            parts.Add("red light");
        }
        else if (result.DidNotFinish)
        {
            parts.Add("did not finish");
        }
        if (result.ElapsedMicroseconds.HasValue)
        {
            parts.Add($"elapsed time {Seconds(result.ElapsedMicroseconds.Value)} seconds");
        }
        if (result.SpeedMphX100.HasValue)
        {
            parts.Add($"{(result.SpeedMphX100.Value / 100.0).ToString("0.00", CultureInfo.InvariantCulture)} miles per hour");
        }
        if (result.Breakout)
        {
            parts.Add("breakout");
        }
        return string.Join(", ", parts);
    }

    private static string Entrant(Car car) => $"{car.RacerName}, driving {car.Name}";

    private static string Seconds(long microseconds) =>
        (microseconds / 1_000_000.0).ToString("0.000", CultureInfo.InvariantCulture);

    private static string Join(IEnumerable<string> values)
    {
        var items = values.ToArray();
        return items.Length switch
        {
            0 => "",
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items[..^1])}, and {items[^1]}"
        };
    }
}
