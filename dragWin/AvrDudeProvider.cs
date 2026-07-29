using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace DragWin;

public sealed class AvrDudeProvider
{
    public const string ToolVersion = "8.0.0-arduino1";
    public const string OfficialDownloadUrl =
        "https://downloads.arduino.cc/tools/avrdude_8.0-arduino.1_Windows_32bit.tar.gz";
    public const long OfficialArchiveSize = 1_890_359;
    public const string OfficialArchiveSha256 =
        "833AA1A66A8E70CD597FCFDBD7E559A91A00ECA1D7AA3BE2CE9BCADF7CCB987C";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    public AvrDudeTool? FindExisting()
    {
        var overridePath = Environment.GetEnvironmentVariable("DRAGWIN_AVRDUDE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var tool = FindToolAt(overridePath, "DRAGWIN_AVRDUDE_PATH override");
            if (tool is null)
            {
                throw new FileNotFoundException(
                    "DRAGWIN_AVRDUDE_PATH does not identify avrdude.exe with a matching avrdude.conf.",
                    overridePath);
            }
            return tool;
        }

        var cached = FindToolAt(CacheDirectory, "dragWin tool cache");
        if (cached is not null)
        {
            return cached;
        }

        foreach (var root in ArduinoPackageRoots())
        {
            var toolRoot = Path.Combine(root, "packages", "arduino", "tools", "avrdude");
            if (!Directory.Exists(toolRoot))
            {
                continue;
            }
            try
            {
                foreach (var versionDirectory in Directory.GetDirectories(toolRoot)
                             .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var installed = FindToolAt(versionDirectory, "Arduino AVR tools");
                    if (installed is not null)
                    {
                        return installed;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return null;
    }

    public async Task<AvrDudeTool> DownloadOfficialAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var existing = FindToolAt(CacheDirectory, "dragWin tool cache");
        if (existing is not null)
        {
            return existing;
        }

        progress?.Report("Downloading the official Arduino avrdude uploader...");
        using var response = await HttpClient.GetAsync(
            OfficialDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var archiveBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        ValidateOfficialArchive(archiveBytes);
        progress?.Report("Download verified; installing uploader in the dragWin tool cache...");
        InstallArchive(archiveBytes);
        return FindToolAt(CacheDirectory, "dragWin tool cache") ??
            throw new InvalidDataException("The verified avrdude archive did not contain the expected files.");
    }

    public static void ValidateOfficialArchive(byte[] archiveBytes)
    {
        ArgumentNullException.ThrowIfNull(archiveBytes);
        if (archiveBytes.LongLength != OfficialArchiveSize)
        {
            throw new InvalidDataException("The avrdude download size does not match the pinned Arduino archive.");
        }
        var actualHash = Convert.ToHexString(SHA256.HashData(archiveBytes));
        if (!string.Equals(actualHash, OfficialArchiveSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The avrdude download SHA-256 does not match the pinned Arduino archive.");
        }
    }

    private static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "dragWin", "Tools", "avrdude", ToolVersion);

    private static IEnumerable<string> ArduinoPackageRoots()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Arduino15");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Arduino15");
    }

    private static AvrDudeTool? FindToolAt(string path, string source)
    {
        var fullPath = Path.GetFullPath(path);
        var executablePath = File.Exists(fullPath)
            ? fullPath
            : Path.Combine(fullPath, "bin", "avrdude.exe");
        if (!File.Exists(executablePath))
        {
            executablePath = Path.Combine(fullPath, "avrdude.exe");
        }
        if (!File.Exists(executablePath))
        {
            return null;
        }

        var executableDirectory = Path.GetDirectoryName(executablePath)!;
        var configOverride = Environment.GetEnvironmentVariable("DRAGWIN_AVRDUDE_CONFIG");
        var configCandidates = new[]
        {
            configOverride,
            Path.Combine(executableDirectory, "avrdude.conf"),
            Path.Combine(executableDirectory, "..", "etc", "avrdude.conf"),
            Path.Combine(fullPath, "etc", "avrdude.conf")
        };
        var configurationPath = configCandidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault(File.Exists);
        if (configurationPath is null)
        {
            return null;
        }

        var versionDirectory = Directory.GetParent(executableDirectory)?.Name;
        return new AvrDudeTool(
            Path.GetFullPath(executablePath),
            configurationPath,
            string.IsNullOrWhiteSpace(versionDirectory) ? "installed" : versionDirectory,
            source);
    }

    private static void InstallArchive(byte[] archiveBytes)
    {
        var cacheParent = Directory.GetParent(CacheDirectory)!.FullName;
        Directory.CreateDirectory(cacheParent);
        var temporaryDirectory = Path.Combine(
            cacheParent,
            $".{ToolVersion}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "bin"));
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "etc"));
        try
        {
            using var compressed = new MemoryStream(archiveBytes, writable: false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.DataStream is null || entry.EntryType is not TarEntryType.RegularFile)
                {
                    continue;
                }
                var normalized = entry.Name.Replace('\\', '/');
                string? destination = normalized.EndsWith("/bin/avrdude.exe", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(normalized, "bin/avrdude.exe", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(temporaryDirectory, "bin", "avrdude.exe")
                    : normalized.EndsWith("/etc/avrdude.conf", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(normalized, "etc/avrdude.conf", StringComparison.OrdinalIgnoreCase)
                        ? Path.Combine(temporaryDirectory, "etc", "avrdude.conf")
                        : null;
                if (destination is not null)
                {
                    using var output = File.Create(destination);
                    entry.DataStream.CopyTo(output);
                }
            }

            if (FindToolAt(temporaryDirectory, "temporary install") is null)
            {
                throw new InvalidDataException("The official avrdude archive is missing required files.");
            }
            if (Directory.Exists(CacheDirectory))
            {
                Directory.Delete(CacheDirectory, recursive: true);
            }
            Directory.Move(temporaryDirectory, CacheDirectory);
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
