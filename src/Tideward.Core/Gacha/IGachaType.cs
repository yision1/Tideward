namespace Tideward.Core.Gacha;

public interface IGachaType
{

    public int Value { get; init; }

    public string ToLocalization();

}
