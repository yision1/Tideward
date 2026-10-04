using Tideward.Core.Games.Models;
using System.Collections.Generic;

namespace Tideward.Features.GameSelector;

public class GameBizDisplay
{

    public GameInfo GameInfo { get; set; }

    public List<GameBizIcon> Servers { get; set; } = new();

}
