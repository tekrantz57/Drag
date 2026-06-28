using System.Diagnostics;

namespace DragWin;

public sealed class SerialLog
{
    private readonly object gate = new();

    public SerialLog()
    {
        var logDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dragWin",
            "logs");
        Directory.CreateDirectory(logDirectory);
        Path = System.IO.Path.Combine(
            logDirectory,
            $"serial-{DateTime.Now:yyyyMMdd}.log");
    }

    public string Path { get; }

    public void Info(string message) => Write("INFO", message);

    public void Raw(string line) => Write("RAW", line);

    public void Warn(string message) => Write("WARN", message);

    public void Error(Exception exception, string message) =>
        Write("ERROR", $"{message}: {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        var line =
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
        Trace.WriteLine(line);

        try
        {
            lock (gate)
            {
                File.AppendAllText(Path, line + Environment.NewLine);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine(
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} " +
                $"[ERROR] serial log write failed: {exception.Message}");
        }
    }
}
