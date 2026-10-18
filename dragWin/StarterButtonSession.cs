namespace DragWin;

internal sealed class StarterButtonSession
{
    public const int RequiredProtocolVersion = 9;
    private bool armed;
    private bool manual;
    private bool pending;
    private ushort? token;

    public static bool Supported(ControllerIdentity? identity) =>
        identity is { Product: "DRAG_MC", Mcu: "MEGA2560", ProtocolVersion: >= RequiredProtocolVersion };

    public void Arm(bool manualStart)
    {
        Cancel();
        armed = true;
        manual = manualStart;
    }

    public void Cancel()
    {
        armed = false;
        token = null;
        pending = false;
    }

    public bool CanStart(bool connected, int? protocol) =>
        armed && manual && token.HasValue && !pending && connected && protocol >= RequiredProtocolVersion;

    public string[]? TakeCommand(bool connected, int? protocol)
    {
        if (!CanStart(connected, protocol)) return null;
        pending = true;
        return ["START_TREE", token!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)];
    }

    public void SendFailed() => pending = false;

    public void Observe(ProtocolMessage message)
    {
        if (message.Type == "HELLO") { Cancel(); return; }
        if (armed && message.Type == "HEARTBEAT")
        {
            var fields = message.Parts.ToList();
            var stateAt = fields.IndexOf("STATE");
            var tokenAt = fields.IndexOf("START_TOKEN");
            if (stateAt >= 0 && stateAt + 1 < fields.Count)
            {
                var state = fields[stateAt + 1];
                if (state == "WAITING_FOR_START" && tokenAt >= 0 && tokenAt + 1 < fields.Count)
                    Observe(ProtocolMessage.Create("EVENT", "TREE", "WAITING_FOR_START", fields[tokenAt + 1]));
                else if (state is "WAITING_FOR_ALL_LANES" or "RACE_COMPLETE")
                    Observe(ProtocolMessage.Create("EVENT", "TREE", state));
                else if (state is "RACE_ACTIVE" or "WAITING_FOR_CLEAR") Cancel();
                else if (state == "START_DELAY") pending = true;
            }
        }
        if (!armed || message.Type != "EVENT" || message.Parts.Count < 3 || message.Parts[1] != "TREE") return;
        switch (message.Parts[2])
        {
            case "WAITING_FOR_START" when message.Parts.Count == 4 && ushort.TryParse(message.Parts[3], out var next):
                if (token != next) pending = false;
                token = next;
                break;
            case "START_AUTHORIZED":
                pending = true;
                break;
            case "WAITING_FOR_ALL_LANES":
            case "STAGING_ABORTED":
                token = null;
                pending = false;
                break;
            case "HEADS_UP_START":
            case "BRACKET_START":
            case "RACE_COMPLETE":
                Cancel();
                break;
        }
    }
}
