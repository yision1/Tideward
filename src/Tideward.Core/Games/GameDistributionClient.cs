using Tideward.Core.Games.Models;

namespace Tideward.Core.Games;

public sealed class GameDistributionClient(HttpClient? client = null)
{
    private readonly KuroDistribution distribution = new(client);
    public Task<GameConfig?> GetGameConfigAsync(GameId game, CancellationToken cancellationToken = default)
    {
        WutheringWavesCatalog.Require(game);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<GameConfig?>(new GameConfig
        {
            GameId = game, ExeFileName = WutheringWavesRuntime.LauncherExecutableName, InstallationDir = "Wuthering Waves Game",
            GameScreenshotDir = "Client/Saved/ScreenShot",
            GameLogGenDir = "Client/Saved/Logs", RelatedProcesses = WutheringWavesRuntime.ProcessNames.Select(name => name + ".exe").ToList(),
        });
    }
    public async Task<GamePackage> GetGamePackageAsync(GameId game, CancellationToken cancellationToken = default)
    {
        WutheringWavesCatalog.Require(game);
        var release = await distribution.GetReleaseAsync(game.GameBiz, cancellationToken);
        var package = release.Config;
        return new GamePackage { GameId = game, PreDownload = new() { Patches = [] },
            Main = new() { Patches = [], Major = new() { Version = package.Version, AudioPackages = [],
                ResListUrl = KuroDistribution.Resolve(release, package.IndexFile).AbsoluteUri,
                GamePackages = [new() { Url = "", MD5 = "", Size = package.Size, DecompressedSize = package.UnCompressSize }] } } };
    }
    /// <summary>Read-only package groups. Each resource list is loaded only when requested by the viewer.</summary>
    public async Task<IReadOnlyList<KuroPackageFileGroup>> GetGamePackageFileGroupsAsync(GameId game, CancellationToken cancellationToken = default, string? sourceVersion = null)
    {
        WutheringWavesCatalog.Require(game);
        var release = await distribution.GetReleaseAsync(game.GameBiz, cancellationToken);
        var groups = new List<KuroPackageFileGroup> { new(distribution, release, release.Config, true) };

        var patches = release.Config.PatchConfig.OrderByDescending(package => Version.Parse(package.Version)).ToList();
        var patch = patches.FirstOrDefault(package => package.Version == sourceVersion) ?? patches.FirstOrDefault();
        if (patch is not null) groups.Add(new(distribution, release, patch, false));
        return groups;
    }
}

public sealed class KuroPackageFileGroup
{
    private readonly KuroDistribution distribution;
    private readonly KuroRelease release;
    private readonly KuroPackage package;
    private readonly SemaphoreSlim readLock = new(1, 1);
    private IReadOnlyList<GamePackageFile>? files;

    internal KuroPackageFileGroup(KuroDistribution distribution, KuroRelease release, KuroPackage package, bool isFull)
    {
        this.distribution = distribution;
        this.release = release;
        this.package = package;
        IsFull = isFull;
    }

    public string Version => package.Version;
    public string TargetVersion => release.Config.Version;
    public long Size => package.Size;
    public long DecompressedSize => package.UnCompressSize;
    public bool IsFull { get; }

    public async Task<IReadOnlyList<GamePackageFile>> GetFilesAsync(CancellationToken cancellationToken = default)
    {
        await readLock.WaitAsync(cancellationToken);
        try
        {
            if (files is not null) return files;
            var manifest = await distribution.GetManifestAsync(release, package, cancellationToken);
            files = manifest.Resource.Select(file => new GamePackageFile
            {
                Url = KuroDistribution.Resolve(release, (file.FromFolder ?? package.BaseUrl).TrimEnd('/') + "/" + file.Dest).AbsoluteUri,
                MD5 = file.Md5, Size = file.Size, DecompressedSize = file.Size,
            }).ToList();
            return files;
        }
        finally { readLock.Release(); }
    }
}

