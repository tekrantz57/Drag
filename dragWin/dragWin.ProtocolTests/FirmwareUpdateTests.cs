using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DragWin;

internal static class FirmwareUpdateTests
{
    public static async Task Run(string megaPath)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"drag-updater-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var mega = ControllerFirmwarePackage.Load(megaPath);
            var duePath = Path.Combine(Path.GetDirectoryName(megaPath)!, "DragMC-due-0.1.2.dragfw");
            var due = ControllerFirmwarePackage.Load(duePath);
            var bundled = ControllerFirmwarePackage.LoadBundledPackages();
            Check(FirmwareUpdatePolicy.SelectPackage(bundled, ControllerFirmwarePackage.MegaBoardProfile).Manifest.FirmwareVersion == mega.Manifest.FirmwareVersion &&
                FirmwareUpdatePolicy.SelectPackage(bundled, ControllerFirmwarePackage.DueBoardProfile).Manifest.FirmwareVersion == due.Manifest.FirmwareVersion,
                "The app output does not contain exactly one matching package per board.");
            var dueIdentity = new ControllerIdentity("DRAG_MC_DUE", due.Manifest.FirmwareVersion, 11, "SAM3X8E");
            var megaIdentity = new ControllerIdentity("DRAG_MC", mega.Manifest.FirmwareVersion, 11, "MEGA2560");
            var light = new ControllerIdentity("DRAG_MC_DUE_LIGHT_TEST", "0.1.0", 8, "SAM3X8E");
            Check(due.IsDue && !mega.IsDue, "Package profile detection failed.");
            Check(FirmwareUpdatePolicy.IdentifyBoard(light) == ControllerFirmwarePackage.DueBoardProfile, "Light-only Due cannot migrate to race firmware.");
            Check(FirmwareUpdatePolicy.IdentifyBoard(null) is null, "Unknown board defaulted to Mega.");
            Check(FirmwareUpdatePolicy.SelectPackage([mega, due], ControllerFirmwarePackage.DueBoardProfile) == due,
                "Multiple board packages were ambiguous.");
            Fail<InvalidDataException>(() => FirmwareUpdatePolicy.SelectPackage([due, due], ControllerFirmwarePackage.DueBoardProfile));
            Fail<FileNotFoundException>(() => FirmwareUpdatePolicy.SelectPackage([mega], ControllerFirmwarePackage.DueBoardProfile));
            Fail<InvalidOperationException>(() => FirmwareUpdatePolicy.CheckTarget(due, megaIdentity));
            Fail<InvalidOperationException>(() => FirmwareUpdatePolicy.CheckTarget(mega, dueIdentity));
            Fail<InvalidOperationException>(() => FirmwareUpdatePolicy.IdentifyBoard(dueIdentity with { Product = "OTHER" }));
            FirmwareUpdatePolicy.CheckTarget(due, light);
            Check(FirmwareUpdatePolicy.VerifyIdentity(due, dueIdentity) && !FirmwareUpdatePolicy.VerifyIdentity(due, light) &&
                !FirmwareUpdatePolicy.VerifyIdentity(due, megaIdentity) &&
                !FirmwareUpdatePolicy.VerifyIdentity(due, dueIdentity with { FirmwareVersion = "old" }), "Post-upload identity verification is too permissive.");

            void Bad(ControllerFirmwareManifest manifest, byte[]? bytes = null)
            {
                var data = bytes ?? due.ImageBytes;
                var path = Path.Combine(directory, Guid.NewGuid() + ".dragfw");
                using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    using (var output = zip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(output, manifest);
                    using var image = zip.CreateEntry(manifest.ImageFile).Open();
                    image.Write(data);
                }
                Fail<InvalidDataException>(() => ControllerFirmwarePackage.Load(path));
            }
            Bad(due.Manifest with { Product = "DRAG_MC" });
            Bad(due.Manifest with { BoardProfile = ControllerFirmwarePackage.MegaBoardProfile });
            Bad(due.Manifest with { ArduinoFqbn = "arduino:sam:arduino_due_x" });
            Bad(due.Manifest with { UploadBaud = 115200 });
            Bad(due.Manifest with { UploaderBackend = "avrdude" });
            Bad(due.Manifest with { ImageFile = "application.hex" });
            Bad(due.Manifest with { ImageFile = "../application.bin" });
            Bad(due.Manifest with { Sha256 = new string('0', 64) });
            var zero = new byte[due.ImageBytes.Length];
            Bad(due.Manifest with { Sha256 = Convert.ToHexString(SHA256.HashData(zero)) }, zero);
            Fail<InvalidDataException>(() => ControllerFirmwarePackage.ValidateDueBinary(mega.ImageBytes));
            Fail<InvalidDataException>(() => ControllerFirmwarePackage.ValidateDueBinary(new byte[512 * 1024 + 1]));
            Fail<InvalidDataException>(() => BossacProvider.ValidateOfficialArchive(new byte[(int)BossacProvider.OfficialArchiveSize]));

            var args = ArduinoDueFirmwareFlasher.BuildArguments("COM21", @"C:\folder with spaces\application.bin");
            Check(args.SequenceEqual(new[] { "--port=COM21", "-U", "false", "-e", "-w", "-v", "-b",
                @"C:\folder with spaces\application.bin", "-R" }), "Due upload flags do not match the Programming Port recipe.");
            Fail<ArgumentException>(() => ArduinoDueFirmwareFlasher.BuildArguments("COM21 --help", "image.bin"));
            var tool = new BossacTool("test-bossac.exe", "test", "test");
            var calls = new List<string>();
            string? temporaryImage = null;
            var flasher = new ArduinoDueFirmwareFlasher(tool)
            {
                ResetPortAsync = (port, token) => { calls.Add("reset " + port); return Task.CompletedTask; },
                RunUploaderAsync = (exe, arguments, progress, token) =>
                {
                    calls.Add("write");
                    temporaryImage = arguments[^2];
                    Check(File.ReadAllBytes(temporaryImage).SequenceEqual(due.ImageBytes), "Uploader did not receive validated BIN bytes.");
                    return Task.CompletedTask;
                }
            };
            await flasher.FlashAsync(due, "COM21");
            Check(calls.SequenceEqual(["reset COM21", "write"]) && !File.Exists(temporaryImage), "Upload order or cleanup failed.");
            calls.Clear();
            await FailAsync<InvalidOperationException>(() => flasher.FlashAsync(mega, "COM26"));
            Check(calls.Count == 0, "Due uploader touched a Mega before rejecting it.");
            await FailAsync<InvalidOperationException>(() => new ArduinoMegaFirmwareFlasher(new AvrDudeTool("none", "none", "test", "test"))
                .FlashAsync(due, "COM21"));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await FailAsync<OperationCanceledException>(() => flasher.FlashAsync(due, "COM21", cancellationToken: cancelled.Token));
            Check(calls.Count == 0, "A pre-cancelled update reset the board.");
            using var afterReset = new CancellationTokenSource();
            var interrupted = new ArduinoDueFirmwareFlasher(tool)
            {
                ResetPortAsync = (port, token) => { afterReset.Cancel(); return Task.CompletedTask; },
                RunUploaderAsync = (_, _, _, _) => throw new Exception("Uploader ran after reset cancellation.")
            };
            await FailAsync<OperationCanceledException>(() => interrupted.FlashAsync(due, "COM21", cancellationToken: afterReset.Token));
            var failing = new ArduinoDueFirmwareFlasher(tool)
            {
                ResetPortAsync = (_, _) => Task.CompletedTask,
                RunUploaderAsync = (_, arguments, _, _) =>
                {
                    temporaryImage = arguments[^2];
                    throw new IOException("Simulated write failure");
                }
            };
            await FailAsync<IOException>(() => failing.FlashAsync(due, "COM21"));
            Check(!File.Exists(temporaryImage), "Failed update retained a temporary image.");
            var archive = Environment.GetEnvironmentVariable("DRAG_TEST_BOSSAC_ARCHIVE");
            if (!string.IsNullOrEmpty(archive))
            {
                var provider = new BossacProvider(Path.Combine(directory, "bossac-cache"));
                var installed = provider.InstallOfficialArchive(File.ReadAllBytes(archive));
                Check(File.Exists(installed.ExecutablePath), "Verified Arduino archive did not install bossac.");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("Due firmware packaging, safety, upload orchestration and recovery tests passed.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Fail<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static async Task FailAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
