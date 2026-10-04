using Tideward.Core.Games;
using Tideward.Core;
using Tideward.Core.Games.Models;
using Tideward.Features.GameLauncher;
using Tideward.Features.Games;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Tideward.Features.GameInstall;

internal partial class GamePackageService
{

    private readonly GameCatalogService _gameCatalogService;

    private readonly GameLauncherService _gameLauncherService;

    public GamePackageService(GameCatalogService gameCatalogService, GameLauncherService gameLauncherService)
    {
        _gameCatalogService = gameCatalogService;
        _gameLauncherService = gameLauncherService;
    }

    public static string? GetGameInstallPath(GameId gameId)
    {
        return GameLauncherService.GetGameInstallPath(gameId);
    }

    public Task<Version?> GetLocalGameVersionAsync(GameId gameId, string? installPath = null)
        => _gameLauncherService.GetLocalGameVersionAsync(gameId, installPath);

    public Task<bool> CheckPreDownloadFinishedAsync(GameId gameId, string? installPath = null)
    {
        WutheringWavesCatalog.Require(gameId);
        return Task.FromResult(false);
    }

    public async Task<AudioLanguage> GetAudioLanguageAsync(GameId gameId, string? installPath = null)
    {
        GameConfig? config = await _gameCatalogService.GetGameConfigAsync(gameId);
        if (string.IsNullOrWhiteSpace(config?.AudioPackageScanDir))
        {
            return AudioLanguage.None;
        }
        installPath ??= GameLauncherService.GetGameInstallPath(gameId);
        AudioLanguage flag = AudioLanguage.None;
        string file = Path.Join(installPath, config.AudioPackageScanDir);
        if (File.Exists(file))
        {
            var lines = await File.ReadAllLinesAsync(file);
            if (lines.Any(x => x.Contains("Chinese"))) { flag |= AudioLanguage.Chinese; }
            if (lines.Any(x => x.Contains("English(US)"))) { flag |= AudioLanguage.English; }
            if (lines.Any(x => x.Contains("Japanese"))) { flag |= AudioLanguage.Japanese; }
            if (lines.Any(x => x.Contains("Korean"))) { flag |= AudioLanguage.Korean; }
        }
        return flag;
    }

}
