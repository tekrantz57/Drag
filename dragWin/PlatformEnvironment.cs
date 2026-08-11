using System.Runtime.InteropServices;

namespace DragWin;

internal static class PlatformEnvironment
{
    private static readonly Lazy<bool> RunningUnderWine = new(DetectWine);

    public static bool IsWine => RunningUnderWine.Value;

    private static bool DetectWine()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WINEPREFIX")))
        {
            return true;
        }

        nint module = 0;
        try
        {
            if (!NativeLibrary.TryLoad("ntdll.dll", out module))
            {
                return false;
            }

            return NativeLibrary.TryGetExport(module, "wine_get_version", out _);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (module != 0)
            {
                NativeLibrary.Free(module);
            }
        }
    }
}
