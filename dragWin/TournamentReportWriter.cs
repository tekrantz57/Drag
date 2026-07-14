using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;

namespace DragWin;

public static class TournamentReportWriter
{
    public static string WriteAndOpen(TournamentReport report)
    {
        var path = Write(report);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return path;
    }

    public static string Write(TournamentReport report)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dragWin",
            "Reports");
        Directory.CreateDirectory(directory);

        var fileName = $"{SafeFileName(report.Tournament.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.html";
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, BuildHtml(report), Encoding.UTF8);
        return path;
    }

    private static string BuildHtml(TournamentReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.Append("<title>");
        builder.Append(Html(report.Tournament.Name));
        builder.AppendLine(" results</title>");
        builder.AppendLine(
            """
            <style>
            :root {
              color-scheme: light dark;
              --accent: #d7a21f;
              --border: #c9c9c9;
              --muted: #666;
              --surface: #f7f7f7;
            }
            body {
              font-family: Segoe UI, Arial, sans-serif;
              margin: 2rem;
              line-height: 1.35;
            }
            h1, h2 { margin-bottom: 0.25rem; }
            .summary {
              display: flex;
              flex-wrap: wrap;
              gap: 1rem;
              margin: 1rem 0 1.5rem;
            }
            .card {
              border: 1px solid var(--border);
              border-radius: 0.6rem;
              padding: 0.8rem 1rem;
              min-width: 12rem;
              background: var(--surface);
            }
            .label {
              color: var(--muted);
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
              border: 1px solid var(--border);
              padding: 0.4rem 0.5rem;
              text-align: left;
              vertical-align: top;
            }
            th {
              background: var(--accent);
              color: #111;
            }
            tr.advanced td {
              font-weight: 650;
            }
            .muted { color: var(--muted); }
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
            ? $"{winner.RacerName} — {winner.CarName}"
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
                    "<thead><tr><th>Lane</th><th>Racer</th><th>Car</th><th>Dial</th><th>Legality</th><th>Finish</th><th>Reaction</th><th>Breakout</th><th>Advanced</th></tr></thead>");
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

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        safe = safe.Trim(' ', '.', '-');
        return safe.Length == 0 ? "tournament" : safe;
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}
