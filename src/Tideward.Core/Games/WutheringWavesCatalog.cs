namespace Tideward.Core.Games;

public sealed record WutheringWavesProfile(string Id, GameBiz GameBiz, Uri OfficialWebsite);

public static class WutheringWavesCatalog
{
    public static IReadOnlyList<WutheringWavesProfile> All { get; } = Array.AsReadOnly(new[]
    {
        new WutheringWavesProfile("wuwa-cn", GameBiz.wuwa_cn, new Uri("https://mc.kurogames.com/")),
        new WutheringWavesProfile("wuwa-global", GameBiz.wuwa_global, new Uri("https://wutheringwaves.kurogames.com/")),
        new WutheringWavesProfile("wuwa-bilibili", GameBiz.wuwa_bilibili, new Uri("https://www.biligame.com/detail/?id=108820")),
    });

    public static WutheringWavesProfile? Find(GameBiz gameBiz) => All.FirstOrDefault(x => x.GameBiz == gameBiz);

    public static WutheringWavesProfile Require(GameId gameId)
    {
        var profile = Find(gameId.GameBiz);
        return profile is not null && profile.Id == gameId.Id ? profile
            : throw new ArgumentException("Unknown or mismatched Wuthering Waves region.", nameof(gameId));
    }

    public static void RequireVerifiedDistribution()
        => throw new NotSupportedException("鸣潮安装、增量更新与修复协议尚未完成验证，请暂用所选区服的官方启动器。");
}
