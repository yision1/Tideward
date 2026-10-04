using Tideward.Core.Games.Models;

namespace Tideward.Core.Games;

public static class GameBackgroundPreference
{
    public static GameBackground? Select(IReadOnlyList<GameBackground> backgrounds, string? cachedFile, string? previousIds, bool videoPaused, string? selectedId = null)
    {
        var background = backgrounds.FirstOrDefault();
        if (background is null) return null;
        if (!string.IsNullOrWhiteSpace(cachedFile) && previousIds?.Split(',')[0] == background.Id)
            background = backgrounds.FirstOrDefault(x => x.Id == selectedId)
                ?? backgrounds.FirstOrDefault(x => x.Video is not null && Path.GetFileName(x.Video.Url) == cachedFile)
                ?? backgrounds.FirstOrDefault(x => Path.GetFileName(x.Background.Url) == cachedFile
                    && (videoPaused ? x.Type == GameBackground.BACKGROUND_TYPE_VIDEO : x.Type == GameBackground.BACKGROUND_TYPE_POSTER))
                ?? backgrounds.FirstOrDefault(x => Path.GetFileName(x.Background.Url) == cachedFile) ?? background;
        // A cached first frame can be a temporary download fallback. Only an explicit
        // pause action should persist static playback across application restarts.
        background.StopVideo = background.Type == GameBackground.BACKGROUND_TYPE_VIDEO && videoPaused;
        return background;
    }
}
