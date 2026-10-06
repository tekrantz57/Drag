namespace DragWin;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(demoMode: args.Contains("--demo", StringComparer.OrdinalIgnoreCase)));
    }
}
