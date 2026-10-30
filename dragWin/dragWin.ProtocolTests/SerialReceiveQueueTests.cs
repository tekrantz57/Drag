using System.Collections.Concurrent;
using DragWin;

static class SerialReceiveQueueTests
{
    public static void Run()
    {
        var delivered = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        using var complete = new ManualResetEventSlim();
        using var consumerGate = new ManualResetEventSlim();
        using var queue = new SerialReceiveQueue(line =>
        {
            consumerGate.Wait();
            delivered.Enqueue(line);
            if (delivered.Count == 2000) complete.Set();
        }, errors.Enqueue, capacity: 4096);
        for (var i = 0; i < 2000; i++)
        {
            var frame = ProtocolMessage.Create("SENSOR", i.ToString()).Encode() + "\r\n";
            queue.Feed(frame[..3]);
            queue.Feed(frame[3..]);
        }
        consumerGate.Set();
        Check(complete.Wait(TimeSpan.FromSeconds(15)), "Timed out waiting for ordered frames.");
        Check(errors.IsEmpty, "Unexpected receive error.");
        var frames = delivered.ToArray();
        for (var i = 0; i < frames.Length; i++)
        {
            Check(ProtocolMessage.TryParse(frames[i], out var message, out _), "Invalid frame.");
            Check(message!.Parts[1] == i.ToString(), "Frame order changed.");
        }

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var overflow = new ManualResetEventSlim();
        using var bounded = new SerialReceiveQueue(_ => { entered.Set(); release.Wait(); },
            error => { if (error.Contains("overflow")) overflow.Set(); }, capacity: 2);
        bounded.Feed("first\n");
        Check(entered.Wait(TimeSpan.FromSeconds(5)), "Worker did not start.");
        bounded.Feed("second\nthird\nfourth\n");
        release.Set();
        Check(overflow.Wait(TimeSpan.FromSeconds(5)), "Overflow was not reported.");

        using var oversized = new ManualResetEventSlim();
        using var after = new ManualResetEventSlim();
        using var limited = new SerialReceiveQueue(line => { if (line == "ok") after.Set(); },
            error => { if (error.Contains("exceeded")) oversized.Set(); }, maximumLineLength: 4);
        limited.Feed("12345\nok\n");
        Check(oversized.Wait(TimeSpan.FromSeconds(5)) && after.Wait(TimeSpan.FromSeconds(5)),
            "Oversized frame did not recover at the next newline.");

        var staleDelivered = 0;
        using var staleStarted = new ManualResetEventSlim();
        using var staleRelease = new ManualResetEventSlim();
        using var closing = new SerialReceiveQueue(_ =>
        {
            Interlocked.Increment(ref staleDelivered);
            staleStarted.Set();
            staleRelease.Wait();
        }, _ => { });
        closing.Feed("old1\n");
        Check(staleStarted.Wait(TimeSpan.FromSeconds(5)), "Closing worker did not start.");
        closing.Feed("old2\n");
        closing.Dispose();
        staleRelease.Set();
        Check(closing.Completion.Wait(TimeSpan.FromSeconds(5)), "Closing worker did not stop.");
        Check(staleDelivered == 1, "Queued messages survived disconnect.");
        Console.WriteLine("Serial receive queue stress tests passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
