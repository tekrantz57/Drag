using System.IO.Ports;
using System.Text.RegularExpressions;

namespace DragWin;

public sealed class ArduinoDueFirmwareFlasher(BossacTool tool) : IControllerFirmwareFlasher
{
    internal Func<string, CancellationToken, Task> ResetPortAsync { get; init; } = TouchProgrammingPortAsync;
    internal Func<string, IReadOnlyList<string>, IProgress<string>?, CancellationToken, Task> RunUploaderAsync { get; init; } =
        async (executable, arguments, progress, token) =>
            await FirmwareToolRunner.RunAsync(executable, arguments, TimeSpan.FromMinutes(2), progress, token);

    public static IReadOnlyList<string> BuildArguments(string portName, string imagePath)
    {
        ValidatePort(portName);
        return [$"--port={portName}", "-U", "false", "-e", "-w", "-v", "-b", imagePath, "-R"];
    }

    public async Task FlashAsync(ControllerFirmwarePackage package, string portName, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!package.IsDue) throw new InvalidOperationException("This uploader accepts only Due Programming Port firmware.");
        ValidatePort(portName);
        ControllerFirmwarePackage.ValidateDueBinary(package.ImageBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(Path.GetTempPath(), $"dragWin-due-firmware-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var image = Path.Combine(directory, package.Manifest.ImageFile);
            await File.WriteAllBytesAsync(image, package.ImageBytes, cancellationToken);
            progress?.Report($"Uploader: bossac {tool.Version} ({tool.Source})");
            progress?.Report($"Entering the Due Programming-port bootloader on {portName}. This erases the current sketch.");
            await ResetPortAsync(portName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Writing and verifying Due firmware...");
            await RunUploaderAsync(tool.ExecutablePath, BuildArguments(portName, image), progress, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void ValidatePort(string portName)
    {
        if (!Regex.IsMatch(portName ?? "", @"\ACOM[1-9][0-9]*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Select the Due Programming port's Windows COM name.", nameof(portName));
    }

    private static async Task TouchProgrammingPortAsync(string portName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using (var port = new SerialPort(portName, 1200) { DtrEnable = true, ReadTimeout = 1000, WriteTimeout = 1000 })
        {
            port.Open();
            await Task.Delay(100, token);
            port.DtrEnable = false;
            port.Close();
        }
        await Task.Delay(1000, token);
    }
}
