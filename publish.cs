#:property BuiltInComInteropSupport = true
#:package Microsoft.Extensions.Configuration.Binder@10.0.8
#:package Microsoft.Extensions.Configuration.CommandLine@10.0.8
#:package SharpSevenZip@2.0.47
#:project src/Tideward.Setup.Core/Tideward.Setup.Core.csproj

using Microsoft.Extensions.Configuration;
using SharpSevenZip;
using Tideward.Setup.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

string command = "full";
string[] optionArgs = args;
if (args.Length > 0 && !args[0].StartsWith('-'))
{
    command = args[0].ToLowerInvariant();
    optionArgs = args[1..];
}
if (command is not ("res" or "compile" or "pack" or "full"))
{
    Console.Error.WriteLine("Supported subcommands: res, compile, pack, full.");
    return 1;
}
var config = new ConfigurationBuilder().AddCommandLine(optionArgs).Build();
string? version = config["version"];
if (version is null || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$"))
{
    Console.Error.WriteLine("Use --version with a numeric version, such as 0.18.3 or 0.18.3-preview.1 (without a v prefix).");
    return 1;
}
string? archOption = config["arch"]?.ToLowerInvariant();
if (archOption is not (null or "x64" or "arm64"))
{
    Console.Error.WriteLine("Use --arch x64 or arm64.");
    return 1;
}
List<Architecture> targetArchitectures = archOption switch
{
    "x64" => [Architecture.X64],
    "arm64" => [Architecture.Arm64],
    _ => [Architecture.X64, Architecture.Arm64],
};

if (command == "res")
{
    File.Delete("src/Tideward.Setup/Assets/Tideward.7z");
    foreach (var arch in targetArchitectures)
    {
        string archName = arch.ToLower();
        await Process.Start("dotnet", $"publish src/Tideward.Setup -c Release -o publish/pub_res/ -r win-{archName} -p:Version={version}").EnsureExitSuccessAsync();
        File.Move("publish/pub_res/Tideward.Setup.exe", $"publish/pub_res/Tideward.Setup_{archName}_{version}.exe", true);
        await Process.Start("msbuild", $"src/Tideward.Launcher -property:Configuration=Release;Platform={archName};Version={version};OutDir={Path.GetFullPath("publish/pub_res/")}").EnsureExitSuccessAsync();
        File.Move("publish/pub_res/Tideward.exe", $"publish/pub_res/Tideward_{archName}_{version}.exe", true);
    }
    return 0;
}

bool doCompile = command is "compile" or "full";
bool doPackage = command is "pack" or "full";

if (doCompile)
{
    foreach (var arch in targetArchitectures)
    {
        string archName = arch.ToLower();
        string archPath = $"publish/{archName}";
        if (Directory.Exists(archPath))
        {
            Directory.Delete(archPath, true);
        }

        Console.WriteLine($"Building {archName} release...");
        await Process.Start("dotnet", $"publish src/Tideward -c Release -r win-{archName} --self-contained true -o {archPath}/Tideward/app-{version} -p:Platform={archName} -p:Version={version} -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:CsWinRTAotOptimizerEnabled=false").EnsureExitSuccessAsync();
        await File.WriteAllTextAsync($"{archPath}/Tideward/version.ini", $"version={version}");
    }
}


if (doPackage)
{
    foreach (var arch in targetArchitectures)
    {
        await CreatePackageAsync(version, arch, InstallType.Setup, doPackage);
        await CreatePackageAsync(version, arch, InstallType.Portable, doPackage);
    }
}
return 0;

async Task CreatePackageAsync(string version, Architecture arch, InstallType type, bool doPackage)
{
    Console.WriteLine($"Creating package for ({version}, {arch}, {type})...");

    string rootPath = type is InstallType.Setup ? $"publish/{arch.ToLower()}/Tideward/app-{version}/" : $"publish/{arch.ToLower()}/Tideward/";

    // compress
    if (doPackage)
    {
        Directory.CreateDirectory("publish/release/package/");
        Directory.CreateDirectory("src/Tideward.Setup/Assets/");
        var compressor = new SharpSevenZipCompressor { CompressionLevel = SharpSevenZip.CompressionLevel.Ultra };
        if (type is InstallType.Setup)
        {
            Console.WriteLine("Compressing setup package...");
            File.Copy($"publish/pub_res/Tideward.Setup_{arch.ToLower()}_{version}.exe", Path.Join(rootPath, "Tideward.Setup.exe"), true);
            try
            {
                compressor.CompressDirectory(rootPath, "src/Tideward.Setup/Assets/Tideward.7z");
                Console.WriteLine("Creating setup executable...");
                var p = Process.Start("dotnet", $"""
                    publish src/Tideward.Setup -c Release -o publish/{arch.ToLower()}-setup/ -r win-{arch.ToLower()} -p:Version={version}
                    """);
                await p.WaitForExitAsync();
                if (p.ExitCode != 0)
                {
                    throw new Exception($"Publish setup exited with code {p.ExitCode}");
                }
                File.Move($"publish/{arch.ToLower()}-setup/Tideward.Setup.exe", $"publish/release/package/Tideward_Setup_{version}_{arch.ToLower()}.exe", true);
            }
            finally
            {
                File.Delete("src/Tideward.Setup/Assets/Tideward.7z");
                File.Delete(Path.Join(rootPath, "Tideward.Setup.exe"));
            }
        }
        else
        {
            Console.WriteLine("Compressing portable package...");
            File.Copy($"publish/pub_res/Tideward_{arch.ToLower()}_{version}.exe", Path.Join(rootPath, "Tideward.exe"), true);
            compressor.CompressDirectory(Path.GetDirectoryName(rootPath.TrimEnd('/', '\\'))!, $"publish/release/package/Tideward_Portable_{version}_{arch.ToLower()}.7z");
        }
        Console.WriteLine("Compression completed.");
        Console.WriteLine("--------------------");
    }

}

public static class Extension
{
    public static string ToLower(this Enum @enum) => @enum.ToString().ToLower();


    public static async Task EnsureExitSuccessAsync(this Process process)
    {
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new Exception($"Process exited with code {process.ExitCode}");
        }
    }
}
