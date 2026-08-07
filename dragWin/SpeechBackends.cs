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
    None
}

internal interface ISpeechBackend : IDisposable
{
    IReadOnlyList<string> GetVoices();
    void Speak(string phrase, string voiceName);
}

internal static class SpeechBackendFactory
{
    public static ISpeechBackend? Create(SpeechBackendMode mode) => mode switch
    {
        SpeechBackendMode.Automatic => CreateAutomatic(),
        SpeechBackendMode.WindowsSapi => SapiSpeechBackend.TryCreate(),
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

        return LinuxSpeechBackend.TryCreate();
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
    private readonly LinuxSpeechHelperClient client = new();

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

    public void Speak(string phrase, string voiceName) => client.Speak(phrase, voiceName);

    public void Dispose()
    {
    }
}

internal sealed class LinuxSpeechHelperClient
{
    public const int Port = 38592;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);
    private const int VoiceResponseTimeoutMilliseconds = 1500;
    private const int SpeechResponseTimeoutMilliseconds = 30000;

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

    private static JsonDocument Send(object request, int responseTimeoutMilliseconds)
    {
        using var client = new TcpClient();
        client.ConnectAsync(IPAddress.Loopback, Port)
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
            ?? throw new IOException("The Linux speech helper closed the connection without responding.");
        return JsonDocument.Parse(response);
    }

    private static void EnsureSuccess(JsonElement response)
    {
        if (response.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
        {
            return;
        }

        var message = response.TryGetProperty("error", out var error)
            ? error.GetString() ?? "Linux speech helper failed."
            : "Linux speech helper returned an invalid response.";
        throw new IOException(message);
    }
}
