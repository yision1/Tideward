using Tideward.Core.Gacha;

namespace Tideward.Features.Gacha;

public class GachaBanner
{

    public IGachaType GachaType { get; private set; }

    public int Value => GachaType.Value;

    public string ToLocalization() => GachaType.ToLocalization();

    public GachaBanner(IGachaType gachaType)
    {
        GachaType = gachaType;
    }

}
