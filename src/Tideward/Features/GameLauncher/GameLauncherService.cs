using Tideward.Core.Games;
using Microsoft.Extensions.Logging;
using Tideward.Core;
using Tideward.Core.Games.Models;

using Tideward.Features.Games;
using Tideward.Features.PlayTime;
using Tideward.Helpers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Tideward.Features.GameLauncher;

internal partial class GameLauncherService
{

    private readonly ILogger<GameLauncherService> _logger;

    private readonly GameCatalogService _gameCatalogService;

    private readonly PlayTimeRecordService _playTimeRecorderService;

    public GameLauncherService(ILogger<GameLauncherService> logger, GameCatalogService gameCatalogService, PlayTimeRecordService playTimeRecorderService)
    {
        _logger = logger;
        _gameCatalogService = gameCatalogService;
        _playTimeRecorderService = playTimeRecorderService;
    }

    public static string? GetGameInstallPath(GameId gameId)
    {
        return GetGameInstallPath(gameId.GameBiz);
    }

    public static string? GetGameInstallPath(GameBiz gameBiz)
        => GetGameInstallPath(gameBiz, out _);

    public static string? GetGameInstallPath(GameId gameId, out bool storageRemoved)
        => GetGameInstallPath(gameId.GameBiz, out storageRemoved);

    private static string? GetGameInstallPath(GameBiz gameBiz, out bool storageRemoved)
    {
        storageRemoved = false;
        var path = AppConfig.GetGameInstallPath(gameBiz);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        path = GetFullPathIfRelativePath(path);
        if (Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }
        else if (AppConfig.GetGameInstallPathRemovable(gameBiz))
        {
            storageRemoved = true;
            return path;
        }
        else
        {
            ChangeGameInstallPath(gameBiz, null);
            return null;
        }
    }

    public Task<Version?> GetLocalGameVersionAsync(GameId gameId, string? installPath = null)
        => GetLocalGameVersionAsync(gameId.GameBiz, installPath);

    public Task<Version?> GetLocalGameVersionAsync(GameBiz gameBiz, string? installPath = null)
    {
        _ = WutheringWavesCatalog.Find(gameBiz) ?? throw new ArgumentException("Unknown Wuthering Waves region.", nameof(gameBiz));
        installPath ??= GetGameInstallPath(gameBiz);
        var version = string.IsNullOrWhiteSpace(installPath) ? null : KuroDistribution.ReadLocalVersion(installPath, gameBiz)?.Version;
        return Task.FromResult(Version.TryParse(version, out var parsed) ? parsed : null);
    }

    public async Task<(Version? Latest, Version? Predownload)> GetLatestGameVersionAsync(GameId gameId)
    {
        var package = await _gameCatalogService.GetGamePackageAsync(gameId);
        Version.TryParse(package.Main.Major?.Version, out var latest);
        Version.TryParse(package.PreDownload.Major?.Version, out var predownload);
        return (latest, predownload);
    }

    public async Task<string> GetGameExeNameAsync(GameId gameId)
    {
        string? name = GetGameExeName(gameId.GameBiz);
        if (string.IsNullOrWhiteSpace(name))
        {
            var config = await _gameCatalogService.GetGameConfigAsync(gameId);
            name = config?.ExeFileName;
        }
        return name ?? throw new ArgumentOutOfRangeException($"Unknown game ({gameId.Id}, {gameId.GameBiz}).");
    }

    public static string? GetGameExeName(GameBiz gameBiz)
    {
        string? name = gameBiz.Value switch
        {
            GameBiz.wuwa_cn or GameBiz.wuwa_global or GameBiz.wuwa_bilibili => WutheringWavesRuntime.LauncherExecutableName,
            _ => null,
        };
        return name;
    }

    public async Task<bool> IsGameExeExistsAsync(GameId gameId, string? installPath = null)
    {
        installPath ??= GetGameInstallPath(gameId);
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            var exe = Path.Join(installPath, await GetGameExeNameAsync(gameId));
            return File.Exists(exe);
        }
        return false;
    }

    public async Task<Process?> GetGameProcessAsync(GameId gameId)
    {
        int currentSessionId = Process.GetCurrentProcess().SessionId;
        var name = (await GetGameExeNameAsync(gameId)).Replace(".exe", "");
        var names = gameId.GameBiz.Game == GameBiz.wuwa
            ? WutheringWavesRuntime.ProcessNames : new[] { name };
        var processes = names.SelectMany(Process.GetProcessesByName).ToArray();
        Process? selected = null;
        foreach (var process in processes)
        {
            try
            {
                if (process.SessionId != currentSessionId || process.HasExited || (process.MainWindowHandle == 0 && IsProcessPending(process))) continue;
                if (selected is null || (selected.MainWindowHandle == 0 && process.MainWindowHandle != 0)) selected = process;
            }
            catch (InvalidOperationException) { }
        }
        foreach (var process in processes) if (process != selected) process.Dispose();
        return selected;
    }

    public static bool IsProcessPending(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return false;
            }
            foreach (ProcessThread thread in process.Threads)
            {
                if (thread.ThreadState is not ThreadState.Wait)
                {
                    return false;
                }
                else if (thread.WaitReason is not ThreadWaitReason.Suspended)
                {
                    return false;
                }
            }
            return true;
        }
        catch { }
        return true;
    }

    public async Task<Process?> StartGameAsync(GameId gameId, string? installPath = null)
    {
        const int ERROR_CANCELLED = 0x000004C7;
        try
        {
            if (await GetGameProcessAsync(gameId) is Process existingProcess)
            {
                throw new Exception($"Game is running: {existingProcess.ProcessName}.exe ({existingProcess.Id}).");
            }
            string? exe = null, arg = null, verb = null;
            if (Directory.Exists(installPath))
            {
                var e = Path.Join(installPath, await GetGameExeNameAsync(gameId));
                if (File.Exists(e))
                {
                    exe = e;
                }
            }
            bool thirdPartyTool = false;
            if (string.IsNullOrWhiteSpace(exe) && AppConfig.GetEnableThirdPartyTool(gameId.GameBiz))
            {
                exe = GetThirdPartyToolPath(gameId);
                if (File.Exists(exe))
                {
                    thirdPartyTool = true;
                    verb = Path.GetExtension(exe) is ".exe" or ".bat" ? "runas" : "";
                }
                else
                {
                    exe = null;
                    SetThirdPartyToolPath(gameId, null);
                    _logger.LogWarning("Third party tool not found: {path}", exe);
                }
            }
            if (string.IsNullOrWhiteSpace(exe))
            {
                var folder = GetGameInstallPath(gameId);
                var name = await GetGameExeNameAsync(gameId);
                exe = Path.Join(folder, name);
                verb = "runas";
                if (!File.Exists(exe))
                {
                    _logger.LogWarning("Game exe not found: {path}", exe);
                    throw new FileNotFoundException("Game exe not found", name);
                }
            }
            arg = AppConfig.GetStartArgument(gameId.GameBiz)?.Trim();

            if (!thirdPartyTool && gameId.GameBiz.IsKnown())
            {
                arg = KuroLaunchArguments.Build(arg, AppConfig.GetEnableDX11(gameId.GameBiz));
            }

            if (!thirdPartyTool && AppConfig.StartGameWithCMD)
            {
                arg = $"""/c start "" /d "{Path.GetDirectoryName(exe)}" "{exe}" {arg}""";
                exe = "cmd.exe";
            }
            _logger.LogInformation("Start game ({biz})\r\npath: {exe}\r\narg: {arg}", gameId, exe, arg);
            var info = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arg,
                UseShellExecute = true,
                Verb = verb,
                WorkingDirectory = Path.GetDirectoryName(exe),
            };
            Process? process = Process.Start(info);
            if (process != null)
            {
                if (gameId.GameBiz.Game == GameBiz.wuwa || thirdPartyTool || AppConfig.StartGameWithCMD)
                {
                    return await _playTimeRecorderService.StartProcessToLogAsync(gameId);
                }
                else
                {
                    await _playTimeRecorderService.StartProcessToLogAsync(gameId, process.Id);
                    return process;
                }
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {

            _logger.LogInformation("Start game operation canceled.");
        }
        return null;
    }

    public static string? ChangeGameInstallPath(GameId gameId, string? path)
    {
        return ChangeGameInstallPath(gameId.GameBiz, path);
    }

    public static string? ChangeGameInstallPath(GameBiz gameBiz, string? path)
    {
        if (Directory.Exists(path))
        {
            path = Path.GetFullPath(path);
            string relativePath = GetRelativePathIfInRemovableStorage(path, out bool removable);
            AppConfig.SetGameInstallPath(gameBiz, relativePath);
            AppConfig.SetGameInstallPathRemovable(gameBiz, removable);
        }
        else
        {
            path = null;
            AppConfig.SetGameInstallPath(gameBiz, null);
            AppConfig.SetGameInstallPathRemovable(gameBiz, false);
        }
        return path;
    }

    /// 如果安装在可移动存储设备中，获取相对路径

    public static string GetRelativePathIfInRemovableStorage(string path, out bool removableStorage)
    {
        removableStorage = DriveHelper.IsDeviceRemovableOrOnUSB(path);
        if (removableStorage && Path.GetPathRoot(AppConfig.TidewardExecutePath) == Path.GetPathRoot(path))
        {
            path = Path.GetRelativePath(Path.GetDirectoryName(AppConfig.ConfigPath)!, path);
        }
        return path;
    }

    public static string GetFullPathIfRelativePath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }
        else
        {
            return Path.GetFullPath(path, Path.GetDirectoryName(AppConfig.ConfigPath)!);
        }
    }

    public static string? GetThirdPartyToolPath(GameId gameId)
    {
        string? path = AppConfig.GetThirdPartyToolPath(gameId.GameBiz);
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = GetFullPathIfRelativePath(path);
        }
        if (File.Exists(path))
        {
            return path;
        }
        else
        {
            AppConfig.SetThirdPartyToolPath(gameId.GameBiz, null);
            return null;
        }
    }

    public static string? SetThirdPartyToolPath(GameId gameId, string? path)
    {
        if (File.Exists(path))
        {
            path = Path.GetFullPath(path);
            string relativePath = GetRelativePathIfInRemovableStorage(path, out bool removable);
            AppConfig.SetThirdPartyToolPath(gameId.GameBiz, relativePath);
        }
        else
        {
            path = null;
            AppConfig.SetThirdPartyToolPath(gameId.GameBiz, null);
        }
        return path;
    }

}

