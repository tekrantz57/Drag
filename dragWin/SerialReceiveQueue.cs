using System.Text;
using System.Threading.Channels;

namespace DragWin;

// One reader assembles and queues complete lines; one worker delivers them in order.
public sealed class SerialReceiveQueue : IDisposable
{
    private readonly object gate = new();
    private readonly StringBuilder buffer = new();
    private readonly Channel<string> lines;
    private readonly Action<string> deliver;
    private readonly Action<string> reportError;
    private readonly int maximumLineLength;
    private readonly CancellationTokenSource cancellation = new();
    private bool discarding;
    private int dropped;
    private bool stopped;
    public Task Completion { get; }

    public SerialReceiveQueue(Action<string> deliver, Action<string> reportError,
        int capacity = 4096, int maximumLineLength = 512)
    {
        this.deliver = deliver;
        this.reportError = reportError;
        this.maximumLineLength = maximumLineLength;
        lines = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        Completion = Task.Run(ProcessAsync);
    }

    public void Feed(string text)
    {
        lock (gate)
        {
            if (stopped) return;
            foreach (var character in text)
            {
                if (character == '\n')
                {
                    if (discarding)
                        EnqueueError($"Received line exceeded {maximumLineLength} characters and was discarded.");
                    else if (!lines.Writer.TryWrite(buffer.ToString().TrimEnd('\r')))
                        Interlocked.Increment(ref dropped);
                    buffer.Clear();
                    discarding = false;
                }
                else if (!discarding)
                {
                    if (buffer.Length >= maximumLineLength)
                    {
                        buffer.Clear();
                        discarding = true;
                    }
                    else buffer.Append(character);
                }
            }
        }
    }

    // Errors use a sentinel that cannot occur in an ASCII protocol frame.
    private void EnqueueError(string error)
    {
        if (!lines.Writer.TryWrite("\0" + error)) Interlocked.Increment(ref dropped);
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var line in lines.Reader.ReadAllAsync(cancellation.Token))
            {
                if (cancellation.IsCancellationRequested) break;
                var lost = Interlocked.Exchange(ref dropped, 0);
                if (lost > 0) reportError($"Serial receive queue overflow: {lost} lines discarded; results may be incomplete.");
                if (line.StartsWith('\0')) reportError(line[1..]);
                else deliver(line);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            reportError($"Serial receive processing stopped: {exception.Message}");
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (stopped) return;
            stopped = true;
            cancellation.Cancel();
            lines.Writer.TryComplete();
        }
    }
}
