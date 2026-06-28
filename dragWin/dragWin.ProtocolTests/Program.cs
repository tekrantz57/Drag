using DragWin;

AssertEqual("PING:10", ProtocolMessage.Create("PING").Encode());
AssertEqual("ACK:PING:63", ProtocolMessage.Create("ACK", "PING").Encode());
AssertEqual(
    "SET:MODE:BRACKET:09",
    ProtocolMessage.Create("SET", "MODE", "BRACKET").Encode());
var laneCountCommand = ProtocolMessage.Create("SET", "LANES", "2").Encode();
Assert(
    ProtocolMessage.TryParse(laneCountCommand, out var laneCountMessage, out _),
    "A lane-count command should round-trip.");
AssertEqual("2", laneCountMessage!.Parts[2]);
var distancesCommand = ProtocolMessage.Create(
    "SET", "DISTANCES", "660000", "12000").Encode();
Assert(
    ProtocolMessage.TryParse(distancesCommand, out var distancesMessage, out _),
    "A distance command should round-trip.");
AssertEqual("660000", distancesMessage!.Parts[2]);
AssertEqual("12000", distancesMessage.Parts[3]);

Assert(
    ProtocolMessage.TryParse("STATUS:TREE:GREEN:49", out var message, out _),
    "A valid status message should parse.");
AssertEqual("STATUS", message!.Type);
AssertEqual("GREEN", message.Parts[2]);

Assert(
    !ProtocolMessage.TryParse("STATUS:TREE:GREEN:00", out _, out _),
    "A corrupt checksum should be rejected.");
Assert(
    !ProtocolMessage.TryParse("PING", out _, out _),
    "A missing checksum should be rejected.");
Assert(
    !ProtocolMessage.TryParse("\u00FFPING:10", out _, out _),
    "Non-ASCII input should be rejected.");

Console.WriteLine("Protocol tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }
}
