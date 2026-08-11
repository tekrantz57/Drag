using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DragWin;

public enum SpeechBackendMode
{
    Automatic,
    WindowsSapi,
    LinuxHelper,
    None,
    Piper
}

internal interface ISpeechBackend : IDisposable
{
    IReadOnlyList<string> GetVoices();
    void WarmUp(string voiceName);
    void Speak(string phrase, string voiceName);
}

internal static class SpeechBackendFactory
{
    public static ISpeechBackend? Create(SpeechBackendMode mode) => mode switch
    {
        SpeechBackendMode.Automatic => CreateAutomatic(),
        SpeechBackendMode.WindowsSapi => SapiSpeechBackend.TryCreate(),
        SpeechBackendMode.Piper => PiperSpeechBackend.TryCreate(),
        SpeechBackendMode.LinuxHelper => LinuxSpeechBackend.TryCreate(),
        _ => null
    };

    private static ISpeechBackend? CreateAutomatic()
    {
        ISpeechBackend? sapi = SapiSpeechBackend.TryCreate();
        if (sapi is not null)
        {
            try
            {
                if (sapi.GetVoices().Count > 0)
                {
                    return sapi;
                }
            }
            catch
            {
            }

            sapi.Dispose();
        }

        ISpeechBackend? piper = PiperSpeechBackend.TryCreate();
        return piper ?? LinuxSpeechBackend.TryCreate();
    }
}

internal sealed class PiperSpeechBackend : ISpeechBackend
{
    private readonly SpeechHelperClient client = new(PiperHelperLauncher.Port);

    private PiperSpeechBackend()
    {
    }

    public static PiperSpeechBackend? TryCreate()
    {
        var backend = new PiperSpeechBackend();
        try
        {
            PiperHelperLauncher.EnsureAvailable(backend.client);
            return backend.GetVoices().Count > 0 ? backend : null;
        }
        catch
        {
            backend.Dispose();
            return null;
        }
    }

    public IReadOnlyList<string> GetVoices() => client.GetVoices();

    public void WarmUp(string voiceName) => client.WarmUp(voiceName);

    public void Speak(string phrase, string voiceName) => client.Speak(phrase, voiceName);

    public void Dispose()
    {
    }
}

internal static class PiperHelperLauncher
{
    public const int Port = 38593;
    private static readonly object SyncRoot = new();
    private static Process? process;

    static PiperHelperLauncher()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
    }

    public static void EnsureAvailable(SpeechHelperClient client)
    {
        if (client.Ping())
        {
            return;
        }

        if (PlatformEnvironment.IsWine)
        {
            throw new IOException("The native Linux Piper helper is not running.");
        }

        lock (SyncRoot)
        {
            if (client.Ping())
            {
                return;
            }

            if (process is not { HasExited: false })
            {
                process?.Dispose();
                process = Start();
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (client.Ping())
            {
                return;
            }

            if (process is { HasExited: true })
            {
                throw new IOException("The Piper speech helper exited during startup.");
            }

            Thread.Sleep(100);
        }

        throw new IOException("The Piper speech helper did not start.");
    }

    private static Process Start()
    {
        var helperPath = Path.Combine(
            AppContext.BaseDirectory,
            "Linux",
            "drag-speech-helper.py");
        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException(
                "The packaged Piper speech helper was not found.",
                helperPath);
        }

        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var voiceDirectory = Environment.GetEnvironmentVariable("DRAG_PIPER_VOICE_DIR")
            ?? Path.Combine(localApplicationData, "Drag", "PiperVoices");
        Directory.CreateDirectory(voiceDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DRAG_PYTHON") ?? "python",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = voiceDirectory
        };
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add("--engine");
        startInfo.ArgumentList.Add("piper");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(Port.ToString());
        startInfo.ArgumentList.Add("--data-dir");
        startInfo.ArgumentList.Add(voiceDirectory);

        return Process.Start(startInfo)
            ?? throw new IOException("Python did not start the Piper speech helper.");
    }

    private static void Stop()
    {
        lock (SyncRoot)
        {
            var currentProcess = Interlocked.Exchange(ref process, null);
            if (currentProcess is null)
            {
                return;
            }

            try
            {
                if (!currentProcess.HasExited)
                {
                    currentProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
            finally
            {
                currentProcess.Dispose();
            }
        }
    }
}

internal sealed class SapiSpeechBackend : ISpeechBackend
{
    private object? voice;
    private string activeVoiceName = "";

    private SapiSpeechBackend(object voice)
    {
        this.voice = voice;
    }

    public static SapiSpeechBackend? TryCreate()
    {
        try
        {
            var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
            var voice = voiceType is null ? null : Activator.CreateInstance(voiceType);
            return voice is null ? null : new SapiSpeechBackend(voice);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetVoices()
    {
        var voices = new List<string>();
        if (voice is null)
        {
            return voices;
        }

        dynamic sapiVoice = voice;
        dynamic installedVoices = sapiVoice.GetVoices();
        for (var index = 0; index < installedVoices.Count; index++)
        {
            string? description = installedVoices.Item(index).GetDescription();
            if (!string.IsNullOrWhiteSpace(description))
            {
                voices.Add(description);
            }
        }

        return voices;
    }

    public void WarmUp(string voiceName)
    {
        if (voice is not null)
        {
            ApplyVoice((dynamic)voice, voiceName);
        }
    }

    public void Speak(string phrase, string voiceName)
    {
        if (voice is null || string.IsNullOrWhiteSpace(phrase))
        {
            return;
        }

        dynamic sapiVoice = voice;
        ApplyVoice(sapiVoice, voiceName);
        sapiVoice.Speak(phrase);
    }

    private void ApplyVoice(dynamic sapiVoice, string voiceName)
    {
        if (string.IsNullOrWhiteSpace(voiceName) ||
            string.Equals(activeVoiceName, voiceName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        dynamic installedVoices = sapiVoice.GetVoices();
        for (var index = 0; index < installedVoices.Count; index++)
        {
            dynamic candidate = installedVoices.Item(index);
            string? description = candidate.GetDescription();
            if (string.Equals(description, voiceName, StringComparison.OrdinalIgnoreCase))
            {
                sapiVoice.Voice = candidate;
                activeVoiceName = voiceName;
                return;
            }
        }
    }

    public void Dispose()
    {
        var currentVoice = Interlocked.Exchange(ref voice, null);
        if (currentVoice is not null && Marshal.IsComObject(currentVoice))
        {
            try
            {
                Marshal.FinalReleaseComObject(currentVoice);
            }
            catch
            {
            }
        }
    }
}

internal sealed class LinuxSpeechBackend : ISpeechBackend
{
    private readonly SpeechHelperClient client = new(SpeechHelperClient.EspeakPort);

    private LinuxSpeechBackend()
    {
    }

    public static LinuxSpeechBackend? TryCreate()
    {
        var backend = new LinuxSpeechBackend();
        try
        {
            return backend.GetVoices().Count > 0 ? backend : null;
        }
        catch
        {
            backend.Dispose();
            return null;
        }
    }

    public IReadOnlyList<string> GetVoices() => client.GetVoices();

    public void WarmUp(string voiceName)
    {
    }

    public void Speak(string phrase, string voiceName) => client.Speak(phrase, voiceName);

    public void Dispose()
    {
    }
}

internal sealed class SpeechHelperClient
{
    public const int EspeakPort = 38594;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);
    private const int VoiceResponseTimeoutMilliseconds = 1500;
    private const int SpeechResponseTimeoutMilliseconds = 30000;
    private readonly int port;

    public SpeechHelperClient(int port)
    {
        this.port = port;
    }

    public IReadOnlyList<string> GetVoices()
    {
        using var response = Send(
            new { protocol = 1, command = "voices" },
            VoiceResponseTimeoutMilliseconds);
        var root = response.RootElement;
        EnsureSuccess(root);
        if (!root.TryGetProperty("voices", out var voicesElement) ||
            voicesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return voicesElement
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? "")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool Ping()
    {
        try
        {
            using var response = Send(
                new { protocol = 1, command = "ping" },
                VoiceResponseTimeoutMilliseconds);
            EnsureSuccess(response.RootElement);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void WarmUp(string voiceName)
    {
        using var response = Send(
            new { protocol = 1, command = "warmup", voice = voiceName },
            SpeechResponseTimeoutMilliseconds);
        EnsureSuccess(response.RootElement);
    }

    public void Speak(string phrase, string voiceName)
    {
        using var response = Send(
            new
            {
                protocol = 1,
                command = "speak",
                text = phrase,
                voice = voiceName
            },
            SpeechResponseTimeoutMilliseconds);
        EnsureSuccess(response.RootElement);
    }

    private JsonDocument Send(object request, int responseTimeoutMilliseconds)
    {
        using var client = new TcpClient();
        client.ConnectAsync(IPAddress.Loopback, port)
            .WaitAsync(ConnectTimeout)
            .GetAwaiter()
            .GetResult();
        client.ReceiveTimeout = responseTimeoutMilliseconds;
        client.SendTimeout = VoiceResponseTimeoutMilliseconds;

        using var stream = client.GetStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        writer.WriteLine(JsonSerializer.Serialize(request));
        var response = reader.ReadLine()
            ?? throw new IOException("The speech helper closed the connection without responding.");
        return JsonDocument.Parse(response);
    }

    private static void EnsureSuccess(JsonElement response)
    {
        if (response.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
        {
            return;
        }

        var message = response.TryGetProperty("error", out var error)
            ? error.GetString() ?? "The speech helper failed."
            : "The speech helper returned an invalid response.";
        throw new IOException(message);
    }
}
