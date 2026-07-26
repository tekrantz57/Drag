using System.Collections.Concurrent;

namespace DragWin;

public static class SpeechAnnouncer
{
    private static readonly object SyncRoot = new();
    private static BlockingCollection<SpeechRequest>? requests;

    public static IReadOnlyList<string> GetInstalledVoices()
    {
        var voices = new List<string>();
        try
        {
            var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
            dynamic? voice = voiceType is null ? null : Activator.CreateInstance(voiceType);
            if (voice is null)
            {
                return voices;
            }

            dynamic installedVoices = voice.GetVoices();
            for (var index = 0; index < installedVoices.Count; index++)
            {
                string? description = installedVoices.Item(index).GetDescription();
                if (!string.IsNullOrWhiteSpace(description))
                {
                    voices.Add(description);
                }
            }
        }
        catch
        {
            // Speech is optional and unavailable SAPI components must not affect racing.
        }

        return voices;
    }

    public static void WarmUpAsync(string voiceName) => Queue("", voiceName);

    public static void SpeakAsync(string phrase, string voiceName)
    {
        if (!string.IsNullOrWhiteSpace(phrase))
        {
            Queue(phrase, voiceName);
        }
    }

    private static void Queue(string phrase, string voiceName)
    {
        EnsureStarted();
        requests?.TryAdd(new SpeechRequest(phrase, voiceName));
    }

    private static void EnsureStarted()
    {
        lock (SyncRoot)
        {
            if (requests is not null)
            {
                return;
            }

            requests = new BlockingCollection<SpeechRequest>(32);
            var worker = new Thread(() => RunWorker(requests))
            {
                IsBackground = true,
                Name = "Drag race speech announcer"
            };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }
    }

    private static void RunWorker(BlockingCollection<SpeechRequest> speechRequests)
    {
        dynamic? voice = null;
        var activeVoiceName = "";
        foreach (var request in speechRequests.GetConsumingEnumerable())
        {
            try
            {
                voice ??= CreateVoice();
                if (voice is null)
                {
                    continue;
                }

                if (!string.Equals(
                        activeVoiceName,
                        request.VoiceName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(request.VoiceName))
                    {
                        voice = CreateVoice() ?? voice;
                    }
                    else
                    {
                        ApplyVoice(voice, request.VoiceName);
                    }
                    activeVoiceName = request.VoiceName;
                }

                if (!string.IsNullOrWhiteSpace(request.Phrase))
                {
                    voice.Speak(request.Phrase);
                }
            }
            catch
            {
                // Announcements must never interrupt race control or the operator UI.
            }
        }
    }

    private static dynamic? CreateVoice()
    {
        var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
        return voiceType is null ? null : Activator.CreateInstance(voiceType);
    }

    private static void ApplyVoice(dynamic voice, string voiceName)
    {
        dynamic installedVoices = voice.GetVoices();
        for (var index = 0; index < installedVoices.Count; index++)
        {
            dynamic candidate = installedVoices.Item(index);
            string? description = candidate.GetDescription();
            if (string.Equals(description, voiceName, StringComparison.OrdinalIgnoreCase))
            {
                voice.Voice = candidate;
                return;
            }
        }
    }

    private sealed record SpeechRequest(string Phrase, string VoiceName);
}
