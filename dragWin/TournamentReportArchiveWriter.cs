using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DragWin;

public sealed record TournamentReportArchive(
    int SchemaVersion,
    string ApplicationVersion,
    DateTimeOffset ExportedAt,
    TournamentReport TournamentReport);

public sealed record TournamentReportExportPaths(
    string Html,
    string? Json,
    string? Csv);

public sealed record TournamentReportExportOptions(
    bool ExportJson = true,
    bool ExportCsv = true);

public static class TournamentReportArchiveWriter
{
    public const int CurrentSchemaVersion = 2;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static TournamentReportExportPaths Write(
        TournamentReport report,
        string? outputDirectory = null,
        TournamentReportExportOptions? exportOptions = null)
    {
        var options = exportOptions ?? new TournamentReportExportOptions();
        var directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? TournamentReportWriter.GetReportDirectory()
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);

        var baseName = $"{TournamentReportWriter.SafeFileName(report.Tournament.Name)}-" +
            $"{DateTime.Now:yyyyMMdd-HHmmss}";
        var paths = new TournamentReportExportPaths(
            Path.Combine(directory, $"{baseName}.html"),
            options.ExportJson ? Path.Combine(directory, $"{baseName}.json") : null,
            options.ExportCsv ? Path.Combine(directory, $"{baseName}.csv") : null);

        TournamentReportWriter.WriteFile(report, paths.Html);
        if (paths.Json is not null)
        {
            WriteJson(report, paths.Json);
        }
        if (paths.Csv is not null)
        {
            File.WriteAllText(paths.Csv, BuildCsv(report), Utf8NoBom);
        }
        return paths;
    }

    private static void WriteJson(TournamentReport report, string path)
    {
        var archive = new TournamentReportArchive(
            CurrentSchemaVersion,
            BuildIdentity.Current,
            DateTimeOffset.Now,
            report);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(path, JsonSerializer.Serialize(archive, options), Utf8NoBom);
    }

    private static string BuildCsv(TournamentReport report)
    {
        var csv = new StringBuilder();
        AppendRow(csv,
            "ApplicationVersion", "TournamentId", "TournamentName", "Status", "TournamentCreatedUtc", "LaneCount",
            "Round", "Heat", "Lane", "LaneChoiceOrder", "Racer", "Car", "DialMilliseconds",
            "IsBye", "Legality", "FinishOrder", "ReactionMicroseconds", "BreakoutMicroseconds",
            "ElapsedMicroseconds", "SpeedMphX100", "IntervalTimersEnabled",
            "Interval1Microseconds", "Interval2Microseconds", "SpeedTrapMicroseconds",
            "Interval1ToInterval2Microseconds", "Interval2ToSpeedTrapMicroseconds",
            "SpeedTrapToFinishMicroseconds", "Advanced", "ConfirmedUtc");
        foreach (var row in report.Rows.OrderBy(row => row.RoundNumber)
                     .ThenBy(row => row.HeatNumber)
                     .ThenBy(row => row.LaneNumber))
        {
            AppendRow(csv,
                BuildIdentity.Current,
                report.Tournament.Id,
                report.Tournament.Name,
                report.Status,
                report.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                report.Tournament.LaneCount,
                row.RoundNumber,
                row.HeatNumber,
                row.LaneNumber,
                row.LaneChoiceOrder,
                row.RacerName,
                row.CarName,
                row.DialMilliseconds,
                row.IsBye,
                row.Legality?.ToString(),
                row.FinishOrder == int.MaxValue ? null : row.FinishOrder,
                row.ReactionMicroseconds,
                row.BreakoutMicroseconds,
                row.ElapsedMicroseconds,
                row.SpeedMphX100,
                row.IntervalTimersEnabled,
                row.Interval1Microseconds,
                row.Interval2Microseconds,
                row.SpeedTrapMicroseconds,
                row.Interval1ToInterval2Microseconds,
                row.Interval2ToSpeedTrapMicroseconds,
                row.SpeedTrapToFinishMicroseconds,
                row.Advanced,
                row.ConfirmedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }
        return csv.ToString();
    }

    private static void AppendRow(StringBuilder csv, params object?[] values)
    {
        csv.AppendLine(string.Join(",", values.Select(FormatCsvValue)));
    }

    private static string FormatCsvValue(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable =>
                formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }
}
