using Tideward.Core.Games;
using Tideward.Core;
using Tideward.Features.Gacha;
using Tideward.Features.GameLauncher;
using Tideward.Features.KuroGameRecord;

using Tideward.Features.Screenshot;
using System.Collections.Generic;

namespace Tideward.Features;

internal partial class GameFeatureConfig
{

    private GameFeatureConfig()
    {

    }

        public List<string> SupportedPages { get; init; } = [];

        public bool SupportHardLink { get; init; }

        public bool SupportGameAccountSwitcher { get; init; }

    public static GameFeatureConfig FromGameId(GameId? gameId)
    {
        return gameId?.GameBiz.Value switch
        {
            GameBiz.wuwa_bilibili => WutheringWavesBilibili,
            GameBiz.wuwa_cn or GameBiz.wuwa_global => WutheringWaves,
            _ => None,
        };
    }

    private static readonly GameFeatureConfig WutheringWaves = new()
    {
        SupportedPages = [nameof(GameLauncherPage), nameof(ScreenshotPage), nameof(GachaLogPage), nameof(KuroGameRecordPage)],
        SupportGameAccountSwitcher = true,
    };

    private static readonly GameFeatureConfig WutheringWavesBilibili = new()
    {
        SupportedPages = [nameof(GameLauncherPage), nameof(ScreenshotPage), nameof(GachaLogPage)],
    };

    private static readonly GameFeatureConfig None = new();

}
