namespace DragWin;

// A quiet indication never proves that the controller remains connected.
public sealed class HeartbeatQuietTracker
{
    private DateTimeOffset? since;
    public void Reset() => since = null;
    public void Observe(ProtocolMessage message, DateTimeOffset now)
    {
        if (message.Type == "HEARTBEAT") Reset();
        else if (message.Type == "EVENT" && message.Parts.Count > 2 &&
                 message.Parts[1] == "TREE" && message.Parts[2] == "HEARTBEAT_QUIET")
            since = now;
    }
    public bool IsExpected(DateTimeOffset now) =>
        since is { } at && now >= at && now - at < TimeSpan.FromMinutes(2);
}
