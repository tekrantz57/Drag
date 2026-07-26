using System.Globalization;
using System.Net;
using System.Text;

namespace DragWin;

public static class TournamentReportWriter
{
    public static string GetReportDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dragWin",
            "Reports");
    }

    public static string Write(TournamentReport report, string? outputDirectory = null)
    {
        var directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? GetReportDirectory()
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);

        var fileName = $"{SafeFileName(report.Tournament.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.html";
        var path = Path.Combine(directory, fileName);
        WriteFile(report, path);
        return path;
    }

    public static void WriteFile(TournamentReport report, string path)
    {
        File.WriteAllText(path, BuildHtml(report), new UTF8Encoding(false));
    }

    private static string BuildHtml(TournamentReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
        builder.Append("<title>");
        builder.Append(Html(report.Tournament.Name));
        builder.AppendLine(" results</title>");
        builder.AppendLine(
            """
            <style>
            body {
              font-family: Segoe UI, Arial, sans-serif;
              margin: 2rem;
              line-height: 1.35;
              color: #202830;
              background: #fff;
            }
            h1, h2 { margin-bottom: 0.25rem; }
            .summary {
              display: flex;
              flex-wrap: wrap;
              gap: 1rem;
              margin: 1rem 0 1.5rem;
            }
            .card {
              border: 1px solid #c9c9c9;
              border-radius: 0.6rem;
              padding: 0.8rem 1rem;
              min-width: 12rem;
              background: #f7f7f7;
            }
            .label {
              color: #666;
              font-size: 0.85rem;
              text-transform: uppercase;
              letter-spacing: 0.05em;
            }
            .value {
              font-size: 1.2rem;
              font-weight: 650;
              margin-top: 0.15rem;
            }
            table {
              border-collapse: collapse;
              width: 100%;
              margin: 0.7rem 0 1.4rem;
            }
            th, td {
              border: 1px solid #c9c9c9;
              padding: 0.4rem 0.5rem;
              text-align: left;
              vertical-align: top;
            }
            th {
              background: #d7a21f;
              color: #111;
            }
            tr.advanced td {
              font-weight: 650;
            }
            .muted { color: #666; }
            .number { text-align: right; font-variant-numeric: tabular-nums; }
            @media print {
              body { margin: 0.5in; }
              .card { break-inside: avoid; }
              table { break-inside: auto; }
              tr { break-inside: avoid; }
            }
            </style>
            """);
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.Append("<h1>");
        builder.Append(Html(report.Tournament.Name));
        builder.AppendLine("</h1>");
        builder.AppendLine("<p class=\"muted\">Drag strip tournament results</p>");

        builder.AppendLine("<section class=\"summary\">");
        AddCard(builder, "Winner", report.Winner is { } winner
            ? $"{winner.RacerName} - {winner.CarName}"
            : "No winner recorded");
        AddCard(builder, "Lanes", report.Tournament.LaneCount.ToString(CultureInfo.InvariantCulture));
        AddCard(builder, "Status", report.Status);
        AddCard(builder, "Created", report.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        AddCard(builder, "Generated", DateTime.Now.ToString("g", CultureInfo.CurrentCulture));
        builder.AppendLine("</section>");

        foreach (var roundGroup in report.Rows.GroupBy(row => row.RoundNumber))
        {
            builder.Append("<h2>Round ");
            builder.Append(roundGroup.Key.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("</h2>");

            foreach (var heatGroup in roundGroup.GroupBy(row => row.HeatNumber))
            {
                var confirmed = heatGroup.Select(row => row.ConfirmedAt).FirstOrDefault(value => value.HasValue);
                builder.Append("<h3>Heat ");
                builder.Append(heatGroup.Key.ToString(CultureInfo.InvariantCulture));
                if (confirmed.HasValue)
                {
                    builder.Append(" <span class=\"muted\">confirmed ");
                    builder.Append(Html(confirmed.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
                    builder.Append("</span>");
                }
                builder.AppendLine("</h3>");

                builder.AppendLine("<table>");
                builder.AppendLine(
                    "<thead><tr><th>Lane</th><th>Racer</th><th>Car</th><th>Dial</th><th>Legality</th><th>Finish</th><th>Reaction</th><th>ET</th><th>MPH</th><th>Interval 1</th><th>Interval 2</th><th>I1-I2</th><th>I2-Trap</th><th>Trap-Finish</th><th>Breakout</th><th>Advanced</th></tr></thead>");
                builder.AppendLine("<tbody>");
                foreach (var row in heatGroup.OrderBy(row => row.LaneNumber))
                {
                    builder.Append(row.Advanced ? "<tr class=\"advanced\">" : "<tr>");
                    AddCell(builder, row.LaneNumber.ToString(CultureInfo.InvariantCulture), "number");
                    AddCell(builder, row.RacerName);
                    AddCell(builder, row.IsBye ? $"{row.CarName} (BYE)" : row.CarName);
                    AddCell(builder, FormatMilliseconds(row.DialMilliseconds), "number");
                    AddCell(builder, row.Legality?.ToString() ?? "Pending");
                    AddCell(builder, FormatFinish(row.FinishOrder), "number");
                    AddCell(builder, FormatMicroseconds(row.ReactionMicroseconds), "number");
                    AddCell(builder, FormatMicroseconds(row.ElapsedMicroseconds), "number");
                    AddCell(builder, FormatSpeed(row.SpeedMphX100), "number");
                    AddCell(builder, FormatInterval(row.Interval1Microseconds, row.IntervalTimersEnabled), "number");
                    AddCell(builder, FormatInterval(row.Interval2Microseconds, row.IntervalTimersEnabled), "number");
                    AddCell(builder, FormatSegment(row.Interval1Microseconds, row.Interval2Microseconds, row.IntervalTimersEnabled), "number");
                    AddCell(builder, FormatSegment(row.Interval2Microseconds, row.SpeedTrapMicroseconds, row.IntervalTimersEnabled), "number");
                    AddCell(builder, FormatSegment(row.SpeedTrapMicroseconds, row.ElapsedMicroseconds, row.IntervalTimersEnabled), "number");
                    AddCell(builder, FormatMicroseconds(row.BreakoutMicroseconds), "number");
                    AddCell(builder, row.Advanced ? "Yes" : "");
                    builder.AppendLine("</tr>");
                }
                builder.AppendLine("</tbody>");
                builder.AppendLine("</table>");
            }
        }

        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AddCard(StringBuilder builder, string label, string value)
    {
        builder.AppendLine("<div class=\"card\">");
        builder.Append("<div class=\"label\">");
        builder.Append(Html(label));
        builder.AppendLine("</div>");
        builder.Append("<div class=\"value\">");
        builder.Append(Html(value));
        builder.AppendLine("</div>");
        builder.AppendLine("</div>");
    }

    private static void AddCell(StringBuilder builder, string value, string? cssClass = null)
    {
        builder.Append("<td");
        if (!string.IsNullOrWhiteSpace(cssClass))
        {
            builder.Append(" class=\"");
            builder.Append(Html(cssClass));
            builder.Append('"');
        }
        builder.Append('>');
        builder.Append(Html(value));
        builder.Append("</td>");
    }

    private static string FormatMilliseconds(int milliseconds) =>
        (milliseconds / 1000.0).ToString("0.000", CultureInfo.CurrentCulture);

    private static string FormatMicroseconds(long? microseconds) =>
        microseconds.HasValue
            ? (microseconds.Value / 1_000_000.0).ToString("0.000", CultureInfo.CurrentCulture)
            : "";

    private static string FormatFinish(int? finishOrder) =>
        finishOrder.HasValue && finishOrder.Value != int.MaxValue
            ? finishOrder.Value.ToString(CultureInfo.InvariantCulture)
            : "";

    private static string FormatSpeed(long? speedMphX100) => speedMphX100.HasValue
        ? (speedMphX100.Value / 100.0).ToString("0.00", CultureInfo.CurrentCulture)
        : "";

    private static string FormatInterval(long? value, bool enabled) =>
        value.HasValue ? FormatMicroseconds(value) : enabled ? "Missed" : "N/A";

    private static string FormatSegment(long? start, long? end, bool enabled) =>
        start.HasValue && end.HasValue && end >= start
            ? FormatMicroseconds(end - start)
            : enabled ? "" : "N/A";

    public static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        safe = safe.Trim(' ', '.', '-');
        return safe.Length == 0 ? "tournament" : safe;
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
