using System.Globalization;
using System.Text;

namespace DragWin;

public sealed record ProtocolMessage(IReadOnlyList<string> Parts)
{
    public string Type => Parts.Count == 0 ? string.Empty : Parts[0];

    public static ProtocolMessage Create(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        if (parts.Length == 0 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A message needs one or more nonempty parts.", nameof(parts));
        }

        if (parts.Any(part => part.Contains(':') || part.Contains('\r') || part.Contains('\n')))
        {
            throw new ArgumentException("Message parts cannot cotain colons or line endings.", nameof(parts));
        }

        if (parts.Any(part => part.Any(character => character is < ' ' or > '~')))
        {
            throw new ArgumentException("Message parts must contain printable ASCII only.", nameof(parts));
        }

        return new ProtocolMessage(parts);
    }

    public string Encode()
    {
        var payload = string.Join(':', Parts);
        return $"{payload}:{CalculateChecksum(payload):X2}";
    }

    public static bool TryParse(
        string line,
        out ProtocolMessage? message,
        out string error)
    {
        message = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "The line is empty.";
            return false;
        }

        line = line.TrimEnd('\r', '\n');
        if (line.Any(character => character is < ' ' or > '~'))
        {
            error = "The line contains non-printable or non-ASCII characters.";
            return false;
        }

        var checksumSeparator = line.LastIndexOf(':');
        if (checksumSeparator <= 0 || checksumSeparator == line.Length - 1)
        {
            error = "The checksum field is missing.";
            return false;
        }

        var payload = line[..checksumSeparator];
        var checksumText = line[(checksumSeparator + 1)..];
        if (checksumText.Length != 2 ||
            !byte.TryParse(
                checksumText,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var receivedChecksum))
        {
            error = "The checksum must be two hexadecimal digits.";
            return false;
        }

        var expectedChecksum = CalculateChecksum(payload);
        if (receivedChecksum != expectedChecksum)
        {
            error = $"Checksum mismatch: received {receivedChecksum:X2}, expected {expectedChecksum:X2}.";
            return false;
        }

        var parts = payload.Split(':');
        if (parts.Any(string.IsNullOrWhiteSpace))
        {
            error = "Message parts cannot be empty.";
            return false;
        }

        message = new ProtocolMessage(parts);
        return true;
    }

    public static byte CalculateChecksum(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        byte checksum = 0;
        foreach (var value in Encoding.ASCII.GetBytes(payload))
        {
            checksum ^= value;
        }

        return checksum;
    }
}
