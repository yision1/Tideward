using System.Diagnostics;

namespace Tideward.Core.Games;

public static class WutheringWavesRuntime
{
    public const string LauncherExecutableName = "Wuthering Waves.exe";

    public static IReadOnlyList<string> ProcessNames { get; } = Array.AsReadOnly(new[]
    {
        "Client-Win64-Shipping", "Client-Win64-ShippingBase"
    });

    public const string ScreenshotFolderName = "Wuthering Waves";

    public static bool IsGameProcessName(string name)
        => ProcessNames.Contains(Path.GetFileNameWithoutExtension(name), StringComparer.OrdinalIgnoreCase);

    public static bool IsRunning()
        => ProcessNames.Prepend(Path.GetFileNameWithoutExtension(LauncherExecutableName)).Any(IsProcessRunning);

    public static bool IsProcessRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        bool running = processes.Length > 0;
        foreach (var process in processes) process.Dispose();
        return running;
    }
}
