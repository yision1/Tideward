using Microsoft.Extensions.Caching.Memory;
using Tideward.Core;
using Tideward.Core.Games;
using Tideward.Core.Games.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tideward.Features.Games;

public sealed class GameCatalogService
{
    private readonly IMemoryCache memoryCache;
    private readonly KuroPresentationClient presentation;
    private readonly GameDistributionClient distribution;

    public GameCatalogService(IMemoryCache memoryCache, KuroPresentationClient presentation, GameDistributionClient distribution)
    {
        this.memoryCache = memoryCache;
        this.presentation = presentation;
        this.distribution = distribution;
    }

    public static List<GameInfo> GetLocalGames() => WutheringWavesCatalog.All.Select(CreateInfo).ToList();

    private static GameInfo CreateInfo(WutheringWavesProfile game)
    {
        GameImage image = new() { Url = KuroPresentationClient.GameIcon, Link = game.OfficialWebsite.AbsoluteUri };
        return new GameInfo
        {
            Id = game.Id, GameBiz = game.GameBiz, DisplayStatus = GameInfoDisplayStatus.LAUNCHER_GAME_DISPLAY_STATUS_AVAILABLE,
            Display = new GameInfoDisplay
            {
                Name = game.GameBiz.ToGameName(), Title = game.GameBiz.ToGameName(), Subtitle = game.GameBiz.ToGameServerName(),
                Language = CultureInfo.CurrentUICulture.Name, Icon = image, Logo = new(), Thumbnail = new() { Url = KuroPresentationClient.GameCard }, Shortcut = image,
                Background = new GameImage { Url = "" }, Introduction = "Wuthering Waves · Tideward"
            }
        };
    }

    public Task<List<GameInfo>> UpdateGameInfoListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var game in WutheringWavesCatalog.All)
        {
            memoryCache.Remove($"{nameof(GameBackgroundInfo)}_{game.Id}");
            memoryCache.Remove($"{nameof(GameContent)}_{game.Id}");
            memoryCache.Remove($"{nameof(GamePackage)}_{game.Id}");
        }
        return Task.FromResult(GetLocalGames());
    }

    public Task<GameInfo> GetGameInfoAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateInfo(WutheringWavesCatalog.Require(gameId)));
    }

    public Task<GameBackgroundInfo> GetGameBackgroundAsync(GameId gameId, CancellationToken cancellationToken = default)
        => GetCachedAsync(gameId, token => presentation.GetBackgroundAsync(gameId, token), cancellationToken);

    public Task<GameContent> GetGameContentAsync(GameId gameId, CancellationToken cancellationToken = default)
        => GetCachedAsync(gameId, token => presentation.GetContentAsync(gameId, token), cancellationToken);

    public Task<GameConfig?> GetGameConfigAsync(GameId gameId, CancellationToken cancellationToken = default)
        => distribution.GetGameConfigAsync(gameId, cancellationToken);

    public Task<GamePackage> GetGamePackageAsync(GameId gameId, CancellationToken cancellationToken = default)
        => GetCachedAsync(gameId, token => distribution.GetGamePackageAsync(gameId, token), cancellationToken);

    private async Task<T> GetCachedAsync<T>(GameId gameId, Func<CancellationToken, Task<T>> fetch, CancellationToken cancellationToken) where T : class
    {
        WutheringWavesCatalog.Require(gameId);
        cancellationToken.ThrowIfCancellationRequested();
        return (await memoryCache.GetOrCreateAsync($"{typeof(T).Name}_{gameId.Id}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return await fetch(cancellationToken);
        }))!;
    }

    public Task<IReadOnlyList<KuroPackageFileGroup>> GetGamePackageFileGroupsAsync(GameId gameId, CancellationToken cancellationToken = default, string? sourceVersion = null)
        => distribution.GetGamePackageFileGroupsAsync(gameId, cancellationToken, sourceVersion);
}
