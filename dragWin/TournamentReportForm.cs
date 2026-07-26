using System.Diagnostics;

namespace DragWin;

public sealed class TournamentReportForm : Form
{
    private readonly TournamentReportExportPaths paths;

    public TournamentReportForm(
        TournamentReport report,
        TournamentReportExportPaths paths)
    {
        this.paths = paths;
        Text = $"Tournament Report - {report.Tournament.Name}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 800);
        MinimumSize = new Size(750, 520);

        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Dock = DockStyle.Top
        };
        toolbar.Items.Add(CreateButton(
            "Open in Browser",
            "Open this report in the default web browser",
            () => Open(paths.Html)));
        toolbar.Items.Add(CreateButton(
            "Open Report Folder",
            "Show the report export files in File Explorer",
            OpenReportFolder));
        var jsonPath = paths.Json;
        var csvPath = paths.Csv;
        if (jsonPath is not null || csvPath is not null)
        {
            toolbar.Items.Add(new ToolStripSeparator());
        }
        if (jsonPath is not null)
        {
            toolbar.Items.Add(CreateButton(
                "Open JSON",
                "Open the versioned tournament data archive",
                () => Open(jsonPath)));
        }
        if (csvPath is not null)
        {
            toolbar.Items.Add(CreateButton(
                "Open CSV",
                "Open the flat tournament results export",
                () => Open(csvPath)));
        }

        var browser = new WebBrowser
        {
            Dock = DockStyle.Fill,
            ScriptErrorsSuppressed = true,
            AllowWebBrowserDrop = false,
            WebBrowserShortcutsEnabled = true,
            Url = new Uri(Path.GetFullPath(paths.Html))
        };

        Controls.Add(browser);
        Controls.Add(toolbar);
    }

    private ToolStripButton CreateButton(
        string text,
        string toolTip,
        Action action)
    {
        var button = new ToolStripButton(text) { ToolTipText = toolTip };
        button.Click += (_, _) => RunAction(action);
        return button;
    }

    private void RunAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Report File Could Not Be Opened",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void Open(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenReportFolder()
    {
        Process.Start(new ProcessStartInfo(
            "explorer.exe",
            $"/select,\"{Path.GetFullPath(paths.Html)}\"")
        {
            UseShellExecute = true
        });
    }
}
