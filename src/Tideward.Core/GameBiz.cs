using System.Collections.ObjectModel;

namespace Tideward.Core;

public record struct GameBiz
{
    private readonly string _value;
    public string Value => _value ?? "";
    public string Game => Value.Contains('_') ? Value[..Value.IndexOf('_')] : Value;
    public string Server => Value.Contains('_') ? Value[(Value.IndexOf('_') + 1)..] : "";

    public GameBiz(string? value) => _value = value ?? "";

    public const string wuwa = "wuwa";
    public const string wuwa_cn = "wuwa_cn";
    public const string wuwa_global = "wuwa_global";
    public const string wuwa_bilibili = "wuwa_bilibili";
    public const string None = "";

    public static ReadOnlyCollection<GameBiz> AllGameBizs { get; } = new List<GameBiz> { wuwa_cn, wuwa_global, wuwa_bilibili }.AsReadOnly();

    public static bool TryParse(string? value, out GameBiz gameBiz)
    {
        gameBiz = new(value);
        return gameBiz.IsKnown();
    }

    public override string ToString() => Value;
    public static implicit operator GameBiz(string? value) => new(value);
    public static implicit operator string(GameBiz value) => value.Value;
    public bool IsKnown() => Value is wuwa_cn or wuwa_global or wuwa_bilibili;
    public bool IsChinaServer() => Server == "cn";
    public bool IsGlobalServer() => Server == "global";
    public bool IsBilibili() => Server == "bilibili";
    public GameBiz ToGame() => Game;
    public string ToGameName() => Game == wuwa
        ? (LanguageUtil.FilterLanguage(System.Globalization.CultureInfo.CurrentUICulture.Name) == "zh-cn" ? "鸣潮" : "Wuthering Waves") : "";
    public string ToGameServerName() => Server switch
    {
        "cn" => CoreLang.GameServer_ChinaServer,
        "global" => CoreLang.GameServer_GlobalServer,
        "bilibili" => CoreLang.GameServer_Bilibili,
        _ => "",
    };
}
