using Tideward.Core.Games.Models;

namespace Tideward.Features.Background;

internal class BackgroundChangedMessage
{

    public GameBackground? GameBackground { get; set; }

    public BackgroundChangedMessage(GameBackground? gameBackground = null)
    {
        GameBackground = gameBackground;
    }

}
