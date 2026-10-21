using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;

namespace DragWin;

public sealed record ControllerFirmwareManifest(
    int FormatVersion,
    string Product,
    string FirmwareVersion,
    string BoardProfile,
    string BoardDisplayName,
    string Mcu,
    string UploaderBackend,
    string ArduinoFqbn,
    string ArduinoCoreVersion,
    string UploadProtocol,
    int UploadBaud,
    string ImageFile,
    long ImageSizeBytes,
    string Sha256);

public sealed class ControllerFirmwarePackage
{
    public const int CurrentFormatVersion = 1;
    public const string PackageExtension = ".dragfw";
    public const string ProductName = "DRAG_MC";
    public const string MegaBoardProfile = "ARDUINO_MEGA_2560";
    public const string DueBoardProfile = "ARDUINO_DUE_PROGRAMMING";
    public bool IsDue => Manifest.BoardProfile == DueBoardProfile;
    private const int MaximumManifestBytes = 64 * 1024;
    private const int MaximumImageBytes = 2 * 1024 * 1024;

    private ControllerFirmwarePackage(
        string packagePath,
        ControllerFirmwareManifest manifest,
        byte[] imageBytes)
    {
        PackagePath = packagePath;
        Manifest = manifest;
        ImageBytes = imageBytes;
    }

    public string PackagePath { get; }
    public ControllerFirmwareManifest Manifest { get; }
    public byte[] ImageBytes { get; }

    public static ControllerFirmwarePackage Load(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var fullPath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The controller firmware package was not found.", fullPath);
        }

        using var archive = ZipFile.OpenRead(fullPath);
        if (archive.Entries.Count != 2 ||
            archive.Entries.Any(entry => string.IsNullOrEmpty(entry.Name)))
        {
            throw new InvalidDataException(
                "A DragMC firmware package must contain only manifest.json and one firmware image.");
        }

        var manifestEntry = archive.GetEntry("manifest.json") ??
            throw new InvalidDataException("The firmware package does not contain manifest.json.");
        if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException("The firmware manifest has an invalid size.");
        }

        ControllerFirmwareManifest manifest;
        try
        {
            using var manifestStream = manifestEntry.Open();
            manifest = JsonSerializer.Deserialize<ControllerFirmwareManifest>(
                manifestStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidDataException("The firmware manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The firmware manifest is not valid JSON.", exception);
        }
        ValidateManifest(manifest);

        var imageEntry = archive.GetEntry(manifest.ImageFile) ??
            throw new InvalidDataException(
                $"The firmware package does not contain {manifest.ImageFile}.");
        if (imageEntry.Length != manifest.ImageSizeBytes ||
            imageEntry.Length <= 0 || imageEntry.Length > MaximumImageBytes)
        {
            throw new InvalidDataException("The firmware image size does not match its manifest.");
        }

        byte[] imageBytes;
        using (var imageStream = imageEntry.Open())
        using (var buffer = new MemoryStream((int)imageEntry.Length))
        {
            imageStream.CopyTo(buffer);
            imageBytes = buffer.ToArray();
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(imageBytes));
        if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The firmware image SHA-256 does not match its manifest.");
        }
        if (manifest.BoardProfile == DueBoardProfile) ValidateDueBinary(imageBytes);
        else ValidateIntelHex(imageBytes);
        return new ControllerFirmwarePackage(fullPath, manifest, imageBytes);
    }

    public static IReadOnlyList<ControllerFirmwarePackage> LoadBundledPackages(
        string? baseDirectory = null)
    {
        var firmwareDirectory = Path.Combine(
            baseDirectory ?? AppContext.BaseDirectory,
            "Firmware");
        if (!Directory.Exists(firmwareDirectory))
        {
            return [];
        }

        return Directory.GetFiles(firmwareDirectory, $"*{PackageExtension}")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(Load)
            .ToArray();
    }

    public static void ValidateIntelHex(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(imageBytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The firmware image is not valid text Intel HEX.", exception);
        }

        var sawData = false;
        var sawEnd = false;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (sawEnd)
            {
                throw new InvalidDataException("The Intel HEX image contains data after its EOF record.");
            }
            if (line.Length < 11 || line[0] != ':' || (line.Length - 1) % 2 != 0)
            {
                throw new InvalidDataException("The firmware image contains a malformed Intel HEX record.");
            }

            byte[] record;
            try
            {
                record = Convert.FromHexString(line[1..]);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("The firmware image contains invalid HEX digits.", exception);
            }
            if (record.Length != record[0] + 5 || record.Sum(value => value) % 256 != 0)
            {
                throw new InvalidDataException("The firmware image contains an invalid Intel HEX record.");
            }

            var recordType = record[3];
            if (recordType == 0)
            {
                sawData |= record[0] > 0;
            }
            else if (recordType == 1)
            {
                if (record[0] != 0 || record[1] != 0 || record[2] != 0)
                {
                    throw new InvalidDataException("The Intel HEX EOF record is malformed.");
                }
                sawEnd = true;
            }
            else if (recordType is not (2 or 3 or 4 or 5))
            {
                throw new InvalidDataException(
                    $"The Intel HEX image uses unsupported record type {recordType}.");
            }
        }

        if (!sawData || !sawEnd)
        {
            throw new InvalidDataException("The Intel HEX image is empty or missing its EOF record.");
        }
    }

    public static void ValidateDueBinary(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length < 8 || imageBytes.Length > 512 * 1024)
            throw new InvalidDataException("Due firmware must be a nonempty application BIN image within 512 KB.");
        var stack = BinaryPrimitives.ReadUInt32LittleEndian(imageBytes.AsSpan(0, 4));
        var reset = BinaryPrimitives.ReadUInt32LittleEndian(imageBytes.AsSpan(4, 4));
        if (stack < 0x20070000 || stack > 0x20088000 || stack % 8 != 0 ||
            (reset & 1) == 0 || (reset & ~1U) < 0x80000 || (reset & ~1U) >= 0x80000 + imageBytes.Length)
            throw new InvalidDataException("The Due image has an invalid SAM3X8E stack/reset vector.");
    }

    private static void ValidateManifest(ControllerFirmwareManifest manifest)
    {
        if (manifest.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported firmware package format {manifest.FormatVersion}.");
        }
        var mega = manifest.Product == ProductName && manifest.BoardProfile == MegaBoardProfile &&
            manifest.BoardDisplayName == "Arduino Mega 2560" && manifest.Mcu == "atmega2560" &&
            manifest.UploaderBackend == "avrdude" && manifest.ArduinoFqbn == "arduino:avr:mega:cpu=atmega2560" &&
            manifest.UploadProtocol == "wiring" && manifest.UploadBaud == 115200;
        var due = manifest.Product == "DRAG_MC_DUE" && manifest.BoardProfile == DueBoardProfile &&
            manifest.BoardDisplayName == "Arduino Due (Programming Port)" && manifest.Mcu == "sam3x8e" &&
            manifest.UploaderBackend == "bossac" && manifest.ArduinoFqbn == "arduino:sam:arduino_due_x_dbg" &&
            manifest.UploadProtocol == "sam-ba" && manifest.UploadBaud == 1200;
        if (!mega && !due)
        {
            throw new InvalidDataException(
                "The firmware package does not match a supported Mega or Due Programming Port profile.");
        }
        if (string.IsNullOrWhiteSpace(manifest.FirmwareVersion) ||
            string.IsNullOrWhiteSpace(manifest.BoardDisplayName) ||
            string.IsNullOrWhiteSpace(manifest.ArduinoCoreVersion))
        {
            throw new InvalidDataException("The firmware manifest is missing required version information.");
        }
        if (string.IsNullOrWhiteSpace(manifest.ImageFile) ||
            Path.GetFileName(manifest.ImageFile) != manifest.ImageFile ||
            manifest.ImageFile.Contains('\\') || manifest.ImageFile.Contains('/') || manifest.ImageFile.Contains(':') ||
            !manifest.ImageFile.EndsWith(due ? ".bin" : ".hex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The firmware manifest contains an invalid image filename.");
        }
        if (manifest.ImageSizeBytes <= 0 || manifest.ImageSizeBytes > MaximumImageBytes)
        {
            throw new InvalidDataException("The firmware manifest contains an invalid image size.");
        }
        if (manifest.Sha256?.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The firmware manifest contains an invalid SHA-256 value.");
        }
    }
}
