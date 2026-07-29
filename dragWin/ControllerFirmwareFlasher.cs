using System.Diagnostics;

namespace DragWin;

public sealed record AvrDudeTool(
    string ExecutablePath,
    string ConfigurationPath,
    string Version,
    string Source);

public interface IControllerFirmwareFlasher
{
    Task FlashAsync(
        ControllerFirmwarePackage package,
        string portName,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

internal sealed record FirmwareToolResult(IReadOnlyList<string> OutputLines);

internal static class FirmwareToolRunner
{
    public static async Task<FirmwareToolResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new List<string>();
        var outputGate = new object();
        void Capture(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }
            lock (outputGate)
            {
                output.Add(line);
            }
            progress?.Report(line);
        }

        process.OutputDataReceived += (_, args) => Capture(args.Data);
        process.ErrorDataReceived += (_, args) => Capture(args.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException($"{Path.GetFileName(executablePath)} did not start.");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            process.WaitForExit();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"{Path.GetFileName(executablePath)} did not finish within {timeout.TotalMinutes:0} minutes.");
        }
        catch
        {
            TryKill(process);
            throw;
        }

        lock (outputGate)
        {
            if (process.ExitCode != 0)
            {
                var detail = string.Join(Environment.NewLine, output.TakeLast(14));
                throw new InvalidOperationException(
                    $"{Path.GetFileName(executablePath)} failed with exit code {process.ExitCode}." +
                    (detail.Length == 0 ? "" : Environment.NewLine + detail));
            }
            return new FirmwareToolResult(output.ToArray());
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
