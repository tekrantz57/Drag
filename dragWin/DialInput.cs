using System.Globalization;

namespace DragWin;

internal static class DialInput
{
    public static bool TryParseMilliseconds(string? text, out int milliseconds)
    {
        milliseconds = 0;
        if ((!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var seconds) &&
             !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out seconds)) ||
            seconds is < 0.100M or > 60.000M)
        {
            return false;
        }

        milliseconds = decimal.ToInt32(seconds * 1000M);
        return true;
    }
}
