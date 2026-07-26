using System.Text.Json;

namespace DragWin;

public sealed class AppSettings
{
    public string RaceMode { get; set; } = "BRACKET";
    public int LaneCount { get; set; } = 4;
    public string TreeMode { get; set; } = "FULL";
    public string StagingMode { get; set; } = "BOTH_BLOCKED";
    public decimal StagedDelaySeconds { get; set; } = 0.500M;
    public decimal TrackLengthInches { get; set; } = 660.000M;
    public decimal SpeedTrapLengthInches { get; set; } = 12.000M;
    public decimal[] DialSeconds { get; set; } = [10.000M, 10.000M, 10.000M, 10.000M];
    public int[] PracticeLanes { get; set; } = [1, 2, 3, 4];
    public int[] IntervalTimerLanes { get; set; } = [];
    public bool ExportTournamentJson { get; set; } = true;
    public bool ExportTournamentCsv { get; set; } = true;
}

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DragWin",
        "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(Normalize(settings), JsonOptions));
    }

    private static AppSettings Normalize(AppSettings? settings)
    {
        settings ??= new AppSettings();
        settings.RaceMode = settings.RaceMode is "HEADS_UP" or "BRACKET"
            ? settings.RaceMode
            : "BRACKET";
        settings.LaneCount = settings.LaneCount is 2 or 4 ? settings.LaneCount : 4;
        settings.TreeMode = settings.TreeMode is "FULL" or "PRO" ? settings.TreeMode : "FULL";
        settings.StagingMode = settings.StagingMode == "IN_ORDER" ? "IN_ORDER" : "BOTH_BLOCKED";
        settings.StagedDelaySeconds = Math.Clamp(settings.StagedDelaySeconds, 0M, 5.000M);
        settings.TrackLengthInches = Math.Clamp(settings.TrackLengthInches, 1.000M, 10000.000M);
        settings.SpeedTrapLengthInches = Math.Clamp(
            settings.SpeedTrapLengthInches,
            0.100M,
            settings.TrackLengthInches - 0.001M);

        var savedDials = settings.DialSeconds ?? [];
        settings.DialSeconds = Enumerable.Range(0, 4)
            .Select(index => Math.Clamp(
                index < savedDials.Length ? savedDials[index] : 10.000M,
                0.100M,
                60.000M))
            .ToArray();
        settings.PracticeLanes = (settings.PracticeLanes ?? [])
            .Where(lane => lane is >= 1 and <= 4)
            .Distinct()
            .Order()
            .ToArray();
        if (settings.PracticeLanes.Length == 0)
        {
            settings.PracticeLanes = [1];
        }
        settings.IntervalTimerLanes = (settings.IntervalTimerLanes ?? [])
            .Where(lane => lane is >= 1 and <= 4)
            .Distinct()
            .Order()
            .ToArray();
        return settings;
    }
}
