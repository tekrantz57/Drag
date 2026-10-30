namespace DragWin;

public sealed record AvailableFirmwareUpdate(string BoardName, string InstalledVersion, string AvailableVersion);

public static class FirmwareAvailability
{
    public static AvailableFirmwareUpdate? Check(
        ControllerIdentity? identity,
        Func<IReadOnlyList<ControllerFirmwarePackage>> loadPackages,
        out string? error)
    {
        error = null;
        if (identity is null || !TryVersion(identity.FirmwareVersion, out var installed)) return null;
        string? board;
        try { board = FirmwareUpdatePolicy.IdentifyBoard(identity); }
        catch (InvalidOperationException) { return null; }
        try
        {
            var matches = loadPackages().Where(package => package.Manifest.BoardProfile == board).ToArray();
            if (matches.Length != 1) return null;
            var manifest = matches[0].Manifest;
            // The light-test sketch has a different version series, despite sharing a Due board.
            if (manifest.Product != identity.Product || !TryVersion(manifest.FirmwareVersion, out var available) ||
                available <= installed) return null;
            return new(manifest.BoardDisplayName, identity.FirmwareVersion, manifest.FirmwareVersion);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or System.Security.SecurityException)
        {
            error = exception.Message;
            return null;
        }
    }

    public static bool TryVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var parts = text.Split('.');
        if (parts.Length is < 2 or > 4 || parts.Any(part => part.Length == 0 ||
            part.Any(character => character is < '0' or > '9')) || !Version.TryParse(text, out var parsed)) return false;
        version = new(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }
}

public sealed class FirmwareUpdateGuard
{
    private bool armed;
    private string? state;
    public bool CanUpdate => !armed && state is "WAITING_FOR_ALL_LANES" or "RACE_COMPLETE" or
        "WAITING_FOR_CLEAR" or "LIGHT_TEST_ONLY";

    public void Reset() { armed = false; state = null; }

    public void CommandSent(IReadOnlyList<string> parts)
    {
        if (parts.Count > 0 && parts[0] is "ARM" or "RESET") armed = true;
    }

    public void Observe(ProtocolMessage message)
    {
        if (message.Type == "ACK" && message.Parts.Count > 1 && message.Parts[1] is "ARM" or "RESET") armed = true;
        if (message.Type == "HEARTBEAT")
        {
            for (var i = 1; i + 1 < message.Parts.Count; i++)
                if (message.Parts[i] == "STATE") { SetState(message.Parts[i + 1]); break; }
        }
        else if (message.Type == "EVENT" && message.Parts.Count > 2 && message.Parts[1] == "TREE")
        {
            SetState(message.Parts[2] switch
            {
                "START_AUTHORIZED" => "START_DELAY",
                "HEADS_UP_START" or "BRACKET_START" => "RACE_ACTIVE",
                "ALL_LANES_STAGED" => "STAGING_HOLD",
                "STAGING_ABORTED" => "WAITING_FOR_ALL_LANES",
                var value => value
            });
        }
    }

    private void SetState(string value)
    {
        state = value;
        if (value is "STAGING_HOLD" or "WAITING_FOR_START" or "START_DELAY" or "RACE_ACTIVE") armed = true;
        if (value is "RACE_COMPLETE" or "WAITING_FOR_CLEAR") armed = false;
    }
}
