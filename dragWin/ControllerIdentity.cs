namespace DragWin;

public sealed record ControllerIdentity(
    string Product,
    string FirmwareVersion,
    int ProtocolVersion,
    string Mcu)
{
    public bool IsExpectedDragMc(string firmwareVersion) =>
        string.Equals(Product, ControllerFirmwarePackage.ProductName, StringComparison.Ordinal) &&
        string.Equals(FirmwareVersion, firmwareVersion, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Mcu, "MEGA2560", StringComparison.Ordinal);

    public static bool TryParse(ProtocolMessage message, out ControllerIdentity? identity)
    {
        identity = null;
        if (message.Type != "HELLO" || message.Parts.Count < 7)
        {
            return false;
        }

        var protocolIndex = IndexOf(message.Parts, "PROTO", 2);
        var mcuIndex = IndexOf(message.Parts, "MCU", protocolIndex + 2);
        if (protocolIndex < 0 || protocolIndex + 1 >= message.Parts.Count ||
            mcuIndex < 0 || mcuIndex + 1 >= message.Parts.Count ||
            !int.TryParse(message.Parts[protocolIndex + 1], out var protocolVersion))
        {
            return false;
        }

        identity = new ControllerIdentity(
            message.Parts[1],
            message.Parts[2],
            protocolVersion,
            message.Parts[mcuIndex + 1]);
        return true;
    }

    private static int IndexOf(IReadOnlyList<string> parts, string value, int startIndex)
    {
        for (var index = Math.Max(0, startIndex); index < parts.Count; index++)
        {
            if (string.Equals(parts[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return -1;
    }
}
