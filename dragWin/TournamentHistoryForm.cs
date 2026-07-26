using System.Globalization;

namespace DragWin;

public sealed class TournamentHistoryForm : Form
{
    private readonly TournamentReportExportOptions reportExportOptions;

    public TournamentHistoryForm(
        TournamentReport report,
        TournamentReportExportOptions reportExportOptions)
    {
        this.reportExportOptions = reportExportOptions;
        Text = $"Race History - {report.Tournament.Name}";
        MinimumSize = new Size(900, 480);
        Size = new Size(1050, 600);
        StartPosition = FormStartPosition.CenterParent;

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        grid.Columns.Add("Round", "Round");
        grid.Columns.Add("Heat", "Heat");
        grid.Columns.Add("Lane", "Lane");
        grid.Columns.Add("Entrant", "Racer / Car");
        grid.Columns.Add("Dial", "Dial");
        grid.Columns.Add("Reaction", "RT");
        grid.Columns.Add("Outcome", "Outcome");
        grid.Columns.Add("Advanced", "Advanced");
        grid.Columns["Round"]!.FillWeight = 35;
        grid.Columns["Heat"]!.FillWeight = 35;
        grid.Columns["Lane"]!.FillWeight = 35;
        grid.Columns["Entrant"]!.FillWeight = 125;
        grid.Columns["Dial"]!.FillWeight = 45;
        grid.Columns["Reaction"]!.FillWeight = 45;
        grid.Columns["Outcome"]!.FillWeight = 70;
        grid.Columns["Advanced"]!.FillWeight = 50;

        var confirmedRows = report.Rows.Where(row => row.ConfirmedAt.HasValue).ToArray();
        foreach (var row in confirmedRows)
        {
            var rowIndex = grid.Rows.Add(
                row.RoundNumber,
                row.HeatNumber,
                row.LaneNumber,
                $"{row.RacerName} / {row.CarName}" + (row.IsBye ? " (BYE)" : string.Empty),
                (row.DialMilliseconds / 1000M).ToString("0.000", CultureInfo.CurrentCulture),
                row.ReactionMicroseconds.HasValue
                    ? (row.ReactionMicroseconds.Value / 1_000_000.0).ToString("0.000", CultureInfo.CurrentCulture)
                    : "",
                FormatOutcome(row),
                row.Advanced ? "Yes" : "No");
            if (row.Advanced)
            {
                grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(218, 242, 225);
            }
        }

        var status = new Label
        {
            AutoSize = true,
            Text = grid.Rows.Count == 0
                ? "No heats have been confirmed yet."
                : $"{confirmedRows.Select(row => row.RoundNumber).Distinct().Count()} round(s), " +
                  $"{confirmedRows.Select(row => (row.RoundNumber, row.HeatNumber)).Distinct().Count()} confirmed heat(s)",
            ForeColor = SystemColors.GrayText
        };
        var reportButton = new Button { Text = "View / Export Report", AutoSize = true };
        reportButton.Click += (_, _) => ShowReport(report);
        var closeButton = new Button { Text = "Close", AutoSize = true };
        closeButton.Click += (_, _) => Close();
        CancelButton = closeButton;
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(status, 0, 0);
        footer.Controls.Add(reportButton, 1, 0);
        footer.Controls.Add(closeButton, 2, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(grid, 0, 0);
        layout.Controls.Add(footer, 0, 1);
        Controls.Add(layout);
    }

    private static string FormatOutcome(TournamentReportRow row) => row.Legality switch
    {
        RunLegality.Legal => row.FinishOrder.HasValue ? $"Place {row.FinishOrder}" : "Legal",
        RunLegality.Breakout => "Breakout",
        RunLegality.RedLight => "Red light",
        RunLegality.DidNotFinish => "DNF",
        _ => ""
    };

    private void ShowReport(TournamentReport report)
    {
        try
        {
            var paths = TournamentReportArchiveWriter.Write(
                report,
                exportOptions: reportExportOptions);
            using var form = new TournamentReportForm(report, paths);
            form.ShowDialog(this);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                NotSupportedException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                $"Could not create the report exports: {exception.Message}",
                "Tournament Report",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
