using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace DragWin;

public sealed record BossacTool(string ExecutablePath, string Version, string Source);

public sealed class BossacProvider
{
    // Arduino SAM Boards 1.6.12's declared uploader dependency, from package_index.json.
    public const string ToolVersion = "1.6.1-arduino";
    public const string OfficialDownloadUrl = "https://downloads.arduino.cc/bossac-1.6.1-arduino-mingw32.tar.gz";
    public const long OfficialArchiveSize = 222918;
    public const string OfficialArchiveSha256 = "D59F43E2E83A337D04C4AE88B195A4EE175B8D87FFF4C43144D23412A4A9513B";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string cacheDirectory;

    public BossacProvider(string? cacheDirectory = null) => this.cacheDirectory = Path.GetFullPath(cacheDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dragWin", "Tools", "bossac", ToolVersion));

    public BossacTool? FindExisting()
    {
        var overridePath = Environment.GetEnvironmentVariable("DRAGWIN_BOSSAC_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return FindAt(overridePath, "DRAGWIN_BOSSAC_PATH override") ??
                throw new FileNotFoundException("DRAGWIN_BOSSAC_PATH must identify a compatible bossac.exe.", overridePath);
        var cached = FindAt(cacheDirectory, "dragWin tool cache");
        if (cached is not null) return cached;
        foreach (var folder in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
        {
            var installed = FindAt(Path.Combine(Environment.GetFolderPath(folder), "Arduino15", "packages", "arduino",
                "tools", "bossac", ToolVersion), "Arduino SAM tools");
            if (installed is not null) return installed;
        }
        return null;
    }

    public async Task<BossacTool> DownloadOfficialAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var existing = FindAt(cacheDirectory, "dragWin tool cache");
        if (existing is not null) return existing;
        progress?.Report("Downloading official Arduino SAM bossac uploader...");
        using var response = await Client.GetAsync(OfficialDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != OfficialArchiveSize)
            throw new InvalidDataException("The bossac download size does not match the pinned Arduino archive.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return InstallOfficialArchive(bytes);
    }

    public static void ValidateOfficialArchive(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.LongLength != OfficialArchiveSize || Convert.ToHexString(SHA256.HashData(bytes)) != OfficialArchiveSha256)
            throw new InvalidDataException("The bossac download does not match the pinned Arduino size and SHA-256.");
    }

    public BossacTool InstallOfficialArchive(byte[] bytes)
    {
        ValidateOfficialArchive(bytes);
        var parent = Directory.GetParent(cacheDirectory)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".bossac-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(staging);
        try
        {
            using var compressed = new MemoryStream(bytes, writable: false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            var found = false;
            TarEntry? entry;
            while ((entry = tar.GetNextEntry()) is not null)
            {
                if (entry.EntryType != TarEntryType.RegularFile || entry.DataStream is null ||
                    entry.Name.Replace('\\', '/').Split('/').Last() != "bossac.exe") continue;
                if (found) throw new InvalidDataException("The bossac archive contains duplicate executables.");
                found = true;
                using var file = File.Create(Path.Combine(staging, "bossac.exe"));
                entry.DataStream.CopyTo(file);
            }
            if (!found) throw new InvalidDataException("The bossac archive contains no bossac.exe.");
            if (!Directory.Exists(cacheDirectory)) Directory.Move(staging, cacheDirectory);
            return FindAt(cacheDirectory, "dragWin tool cache") ?? throw new InvalidDataException("Cannot locate the installed bossac uploader.");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static BossacTool? FindAt(string path, string source)
    {
        var full = Path.GetFullPath(path);
        var executable = Directory.Exists(full) ? Path.Combine(full, "bossac.exe") : full;
        return File.Exists(executable) && Path.GetFileName(executable).Equals("bossac.exe", StringComparison.OrdinalIgnoreCase)
            ? new BossacTool(executable, ToolVersion, source) : null;
    }
}
