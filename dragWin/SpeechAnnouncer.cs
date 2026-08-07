using System.Collections.Concurrent;

namespace DragWin;

public static class SpeechAnnouncer
{
    private static readonly object SyncRoot = new();
    private static BlockingCollection<SpeechRequest>? requests;

    public static IReadOnlyList<string> GetInstalledVoices(SpeechBackendMode backendMode)
    {
        try
        {
            using var backend = SpeechBackendFactory.Create(backendMode);
            return backend?.GetVoices() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void WarmUpAsync(string voiceName, SpeechBackendMode backendMode) =>
        Queue("", voiceName, backendMode);

    public static void SpeakAsync(
        string phrase,
        string voiceName,
        SpeechBackendMode backendMode)
    {
        if (!string.IsNullOrWhiteSpace(phrase) && backendMode != SpeechBackendMode.None)
        {
            Queue(phrase, voiceName, backendMode);
        }
    }

    private static void Queue(
        string phrase,
        string voiceName,
        SpeechBackendMode backendMode)
    {
        if (backendMode == SpeechBackendMode.None)
        {
            return;
        }

        EnsureStarted();
        requests?.TryAdd(new SpeechRequest(phrase, voiceName, backendMode));
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
        ISpeechBackend? backend = null;
        SpeechBackendMode? activeBackendMode = null;
        var activeVoiceName = "";
        foreach (var request in speechRequests.GetConsumingEnumerable())
        {
            try
            {
                var resetToDefaultVoice =
                    !string.IsNullOrWhiteSpace(activeVoiceName) &&
                    string.IsNullOrWhiteSpace(request.VoiceName);
                if (backend is null ||
                    activeBackendMode != request.BackendMode ||
                    resetToDefaultVoice)
                {
                    backend?.Dispose();
                    backend = SpeechBackendFactory.Create(request.BackendMode);
                    activeBackendMode = request.BackendMode;
                }

                activeVoiceName = request.VoiceName;

                if (backend is not null && !string.IsNullOrWhiteSpace(request.Phrase))
                {
                    backend.Speak(request.Phrase, request.VoiceName);
                }
            }
            catch
            {
                backend?.Dispose();
                backend = null;
                activeVoiceName = "";
            }
        }

        backend?.Dispose();
    }

    private sealed record SpeechRequest(
        string Phrase,
        string VoiceName,
        SpeechBackendMode BackendMode);
}
