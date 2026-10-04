using Microsoft.Win32;

namespace Tideward.Setup.Services;

public static class RegistryHelper
{

    public static void WriteUninstallInfo(string folder, string version, long size)
    {
        string exe = Path.Combine(folder, "Tideward.exe");
        string setupExe = Path.Combine(folder, "Tideward.Setup.exe");
        using var subkey = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tideward");
        subkey.SetValue("Publisher", "Tideward", RegistryValueKind.String);
        subkey.SetValue("DisplayName", "Tideward", RegistryValueKind.String);
        subkey.SetValue("DisplayIcon", exe, RegistryValueKind.String);
        subkey.SetValue("DisplayVersion", version, RegistryValueKind.String);
        subkey.SetValue("InstallLocation", folder, RegistryValueKind.String);
        subkey.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
        subkey.SetValue("InstallDate", $"{DateTime.Now:yyyyMMdd}", RegistryValueKind.String);
        subkey.SetValue("UninstallString", $"\"{setupExe}\" uninstall", RegistryValueKind.String);
        subkey.SetValue("QuietUninstallString", $"\"{setupExe}\" uninstall /S", RegistryValueKind.String);
    }

    public static void DeleteUninstallInfo()
    {
        Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tideward", false);
    }

    public static void WriteUrlProtocol(string folder)
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Tideward", false);
        Registry.SetValue(@"HKEY_LOCAL_MACHINE\Software\Classes\Tideward", "", "URL:Tideward Protocol");
        Registry.SetValue(@"HKEY_LOCAL_MACHINE\Software\Classes\Tideward", "URL Protocol", "");
        Registry.SetValue(@"HKEY_LOCAL_MACHINE\Software\Classes\Tideward\DefaultIcon", "", "Tideward.exe,1");
        Registry.SetValue(@"HKEY_LOCAL_MACHINE\Software\Classes\Tideward\Shell\Open\Command", "", $"""
            "{Path.Combine(folder, "Tideward.exe")}" "%1"
            """);
    }

    public static void DeleteUrlProtocol()
    {
        Registry.LocalMachine.DeleteSubKeyTree(@"Software\Classes\Tideward", false);
    }

    public static string? GetInstallLocation()
    {
        using var subkey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tideward");
        return subkey?.GetValue("InstallLocation") as string;
    }

    public static void DeleteRegistrySetting()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Tideward", false);
    }

}

