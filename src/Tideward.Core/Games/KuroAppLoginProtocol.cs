using System.Text.Json;

namespace Tideward.Core.Games;

public sealed record KuroAppLoginResult(string Token, string UserId, string UserName = "", string Did = "")
{
    public string Source => "android";
    public override string ToString() => "Kuro App login result";
}

public static class KuroAppLoginProtocol
{
    public static bool IsLoginRequest(string url, string method)
        => method == "POST" && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == "https" && uri.Host == "api.kurobbs.com" && uri.IsDefaultPort
            && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/user/sdkLogin";

    public static IReadOnlyDictionary<string, string> RequestHeaders { get; } =
        new Dictionary<string, string> { ["source"] = "android", ["osversion"] = "Android" };

    public static IReadOnlyDictionary<string, string> CreateRequestHeaders(KuroDeviceIdentity device)
    {
        if (!KuroDeviceIdentity.IsValidDevCode(device.DevCode)) throw new ArgumentException("Invalid Kuro device code.");
        return new Dictionary<string, string>(RequestHeaders) { ["devCode"] = device.DevCode };
    }

    public static string CreateDeviceInitializationScript(KuroDeviceIdentity device)
    {
        if (!KuroDeviceIdentity.IsValidDevCode(device.DevCode)) throw new ArgumentException("Invalid Kuro device code.");
        return $"if (location.origin === 'https://www.kurobbs.com') localStorage.setItem('dc', {JsonSerializer.Serialize(device.DevCode)});";
    }

    public static KuroAppLoginResult ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string code = Text(root, "code");
        if (code != "200") throw new InvalidDataException(code == "-130"
            ? "验证码错误或已过期，请重新获取验证码。"
            : "库街区未完成 App 登录，请检查验证码和官网提示。");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("库街区没有返回 App 登录凭据，请重试。");
        if (data.TryGetProperty("unRegistering", out var unregistering) && unregistering.ValueKind == JsonValueKind.True)
            throw new InvalidDataException("此账号正在注销确认期，请先在库街区 App 中处理账号状态。");
        string token = Text(data, "token"), userId = Text(data, "userId");
        if (token.Length == 0 || token.Any(char.IsWhiteSpace) || userId.Length == 0 || !userId.All(char.IsAsciiDigit))
            throw new InvalidDataException("库街区没有返回有效的 App 登录凭据，请重试。");
        string did = Text(data, "did");
        if (!KuroDeviceIdentity.IsValidDid(did)) throw new InvalidDataException("库街区返回的设备标识无效，请重试。");
        return new(token, userId, Text(data, "userName"), did);
    }

    private static string Text(JsonElement item, string key)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value)
            && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : "";
}
