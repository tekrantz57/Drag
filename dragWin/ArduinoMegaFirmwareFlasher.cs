namespace DragWin;

public sealed class ArduinoMegaFirmwareFlasher(AvrDudeTool tool) : IControllerFirmwareFlasher
{
    public static IReadOnlyList<string> BuildArguments(
        AvrDudeTool tool,
        string portName,
        string imagePath) =>
    [
        "-C", tool.ConfigurationPath,
        "-v",
        "-patmega2560",
        "-cwiring",
        $"-P{portName}",
        "-b115200",
        "-D",
        $"-Uflash:w:{imagePath}:i"
    ];

    public async Task FlashAsync(
        ControllerFirmwarePackage package,
        string portName,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        if (!string.Equals(
                package.Manifest.BoardProfile,
                ControllerFirmwarePackage.MegaBoardProfile,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The firmware package is not for an Arduino Mega 2560.");
        }

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"dragWin-firmware-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var imagePath = Path.Combine(temporaryDirectory, package.Manifest.ImageFile);
            await File.WriteAllBytesAsync(imagePath, package.ImageBytes, cancellationToken);
            progress?.Report($"Uploader: avrdude {tool.Version} ({tool.Source})");
            progress?.Report($"Writing DragMC {package.Manifest.FirmwareVersion} to {portName}...");
            await FirmwareToolRunner.RunAsync(
                tool.ExecutablePath,
                BuildArguments(tool, portName, imagePath),
                TimeSpan.FromMinutes(2),
                progress,
                cancellationToken);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }
}
