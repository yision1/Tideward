using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Tideward.Core.Games;

public sealed record KuroDailyNoteStat(string Name, string Amount, string Detail, string Icon = "", string Key = "", long RecoveryTime = 0);
public sealed record KuroDailyNote(string RoleId, string Name, string Server, IReadOnlyList<KuroDailyNoteStat> Stats, string ServerId = KuroDailyNoteClient.ChinaServer, string HeadIcon = "", string Level = "");
public sealed record KuroDailyNoteRole(string RoleId, string Name, string Server, bool IsDefault, string ServerId = KuroDailyNoteClient.ChinaServer, string HeadIcon = "", string UserId = "", string Level = "", IReadOnlyList<KuroDailyNoteStat>? SummaryStats = null)
{
    public string DisplayName => $"{Name} · {Server} · UID {RoleId}";
    public string RoleInfo => Level.Length > 0 ? $"{Server}  Lv.{Level}" : Server;
}

public sealed class KuroDailyNoteClient(HttpClient? client = null)
{
    public const string ChinaServer = "76402e5b20be2c39f095a152090afddc";
    private readonly HttpClient http = client ?? new(new HttpClientHandler
    {
        AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All
    }) { Timeout = TimeSpan.FromSeconds(30) };

    public Task<KuroDailyNote> GetAsync(string token, string roleId, CancellationToken cancellationToken = default, string source = "android", string serverId = ChinaServer, KuroDeviceIdentity? device = null, bool refresh = false)
        => GetNoteAsync(token, roleId, serverId, source, cancellationToken, device, refresh);

    public Task<KuroDailyNote> GetDefaultAsync(string token, string source = "h5", CancellationToken cancellationToken = default, KuroDeviceIdentity? device = null)
        => GetNoteAsync(token, null, null, source, cancellationToken, device, false);

    private async Task<KuroDailyNote> GetNoteAsync(string token, string? roleId, string? serverId, string source, CancellationToken cancellationToken, KuroDeviceIdentity? device, bool refresh)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
            throw new ArgumentException("请填写有效的库街区 Token。");
        if (roleId is not null && (roleId.Length == 0 || !roleId.All(char.IsAsciiDigit)))
            throw new ArgumentException("请填写鸣潮角色 UID。");
        if (serverId is not null && (serverId.Length == 0 || !serverId.All(char.IsAsciiLetterOrDigit)))
            throw new ArgumentException("库街区区服信息无效，请重新选择角色。");
        string endpoint = refresh ? "refresh" : "getData";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.kurobbs.com/gamer/widget/game3/" + endpoint);
        request.Headers.Add("token", token);
        request.Headers.Add("source", source is "android" or "h5" ? source : throw new ArgumentException("Invalid Kuro login source."));
        request.Headers.Add("Origin", "https://web-static.kurobbs.com");
        device?.AddCommunityHeaders(request.Headers);
        var form = new Dictionary<string, string> { ["gameId"] = "3", ["type"] = "2", ["sizeType"] = "1" };
        if (roleId is not null) { form["roleId"] = roleId; form["serverId"] = serverId!; }
        request.Content = new FormUrlEncodedContent(form);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"库街区请求失败（HTTP {(int)response.StatusCode}）。");
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken), roleId, serverId);
    }

    public Task<List<KuroDailyNoteRole>> GetRolesAsync(string token, string source = "h5", CancellationToken cancellationToken = default, KuroDeviceIdentity? device = null)
        => ReadRolesAsync("user/role/findRoleList", token, source, cancellationToken, device);

    public Task<List<KuroDailyNoteRole>> GetGameRolesAsync(string token, string source = "h5", CancellationToken cancellationToken = default, KuroDeviceIdentity? device = null)
        => ReadRolesAsync("gamer/role/list", token, source, cancellationToken, device);

    private async Task<List<KuroDailyNoteRole>> ReadRolesAsync(string endpoint, string token, string source, CancellationToken cancellationToken, KuroDeviceIdentity? device)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace)) throw new ArgumentException("请先登录库街区。");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.kurobbs.com/" + endpoint);
        request.Headers.Add("token", token);
        request.Headers.Add("source", source is "android" or "h5" ? source : throw new ArgumentException("Invalid Kuro login source."));
        request.Headers.Add("Origin", "https://www.kurobbs.com");
        device?.AddCommunityHeaders(request.Headers);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["gameId"] = "3" });
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return ParseRoles(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public static List<KuroDailyNoteRole> ParseRoles(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        long? code = Number(root, "code");
        if (code is 220 or 221 or 222 or 230) throw new InvalidDataException($"库街区会话已失效（{code}），请重新登录。");
        if (code != 200) throw new InvalidDataException($"库街区角色接口读取失败（代码 {code}），登录凭据已保留。请检查库街区角色绑定和数据终端权限。");
        if (!root.TryGetProperty("data", out var roles) || roles.ValueKind != JsonValueKind.Array) throw new InvalidDataException("库街区未返回角色列表。");
        var result = new List<KuroDailyNoteRole>();
        foreach (var role in roles.EnumerateArray())
        {
            string id = Text(role, "roleId");

            string server = Text(role, "serverId");
            if ((Number(role, "gameId") is long game && game != 3) || Number(role, "bindStatus") == 0
                || id.Length == 0 || !id.All(char.IsAsciiDigit)) continue;
            if (server.Length == 0 || !server.All(char.IsAsciiLetterOrDigit)) continue;
            bool isDefault = role.TryGetProperty("isDefault", out var selected) && (selected.ValueKind == JsonValueKind.True || selected.ToString() == "1");
            List<KuroDailyNoteStat> summary = [];
            foreach (var (key, label) in new[] { ("gameLevel", "联觉等级"), ("activeDay", "活跃天数"), ("achievementCount", "成就"), ("roleNum", "共鸣者") })
            {
                string value = Text(role, key);
                if (value.Length > 0) summary.Add(new(label, value, ""));
            }
            result.Add(new(id, Text(role, "roleName"), Text(role, "serverName"), isDefault, server, GameHeadIcon(Text(role, "headPhotoUrl")), Text(role, "userId"), Text(role, "gameLevel"), summary));
        }
        return result.DistinctBy(x => (x.ServerId, x.RoleId)).ToList();
    }

    public static KuroDailyNote Parse(string json, string? expectedRoleId, string? expectedServerId = ChinaServer)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        long? code = Number(root, "code");
        if (code != 200)
            throw new InvalidDataException(code == 220 ? "库街区登录已过期，请重新连接。" : $"库街区暂未返回便笺数据（代码 {code}），请确认角色已绑定且数据终端已开启。");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("库街区尚无此角色的数据。");
        string roleId = Text(data, "roleId");
        string serverId = Text(data, "serverId");
        if (roleId.Length == 0 || !roleId.All(char.IsAsciiDigit) || serverId.Length == 0 || !serverId.All(char.IsAsciiLetterOrDigit)
            || (expectedRoleId is not null && roleId != expectedRoleId) || (expectedServerId is not null && serverId != expectedServerId)
            || (Number(data, "gameId") is long game && game != 3))
            throw new InvalidDataException("库街区返回的角色或区服不匹配。");
        var stats = new List<KuroDailyNoteStat>();
        long? serverTime = Number(data, "serverTime");
        foreach (string key in new[] { "energyData", "storeEnergyData", "livenessData", "weeklyData", "towerData", "slashTowerData", "weeklyFrameData", "weeklyRougeData" })
            if (data.TryGetProperty(key, out var value)) AddStat(value, key);
        if (data.TryGetProperty("battlePassData", out var pass) && pass.ValueKind == JsonValueKind.Array)
            foreach (var value in pass.EnumerateArray()) AddStat(value, "battlePassData");
        if (stats.Count == 0) throw new InvalidDataException("库街区尚无便笺数据。");
        return new(roleId, Text(data, "roleName"), Text(data, "serverName"), stats, serverId, GameHeadIcon(Text(data, "headPhotoUrl")));

        void AddStat(JsonElement item, string key)
        {
            if (item.ValueKind != JsonValueKind.Object) return;
            string name = Text(item, "name");
            if (name.Length == 0 && key == "weeklyFrameData") name = "周度游历";
            if (name.Length == 0) return;
            long? current = Number(item, "cur"), total = Number(item, "total");
            string amount = Text(item, "value");
            if (amount.Length == 0) amount = current.HasValue && total > 0 ? $"{current}/{total}" : "暂无数据";
            string detail = "";
            long? recovery = Number(item, "refreshTimeStamp");
            if (key == "energyData" && current.HasValue && total > 0)
            {
                if (current >= total) detail = "已回满";
                else if (recovery > serverTime && serverTime > 0)
                {
                    long minutes = (recovery.Value - serverTime.Value + 59) / 60;
                    detail = $"回满剩余 {minutes / 60}h {minutes % 60}m";
                }
            }
            string icon = Text(item, "img");
            if (!Uri.TryCreate(icon, UriKind.Absolute, out var iconUri) || iconUri.Scheme != "https" || iconUri.Host != "web-static.kurobbs.com" || iconUri.UserInfo.Length != 0)
                icon = key is "energyData" or "storeEnergyData" ? $"https://web-static.kurobbs.com/gamerdata/widget/game3/{(key == "energyData" ? "energy" : "storeEnergy")}.png" : "";
            stats.Add(new(name, amount, detail, icon, key, key == "energyData" && recovery > serverTime && current < total ? recovery.Value : 0));
        }
    }

    private static string GameHeadIcon(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0
            && uri.Host is "prod-alicdn-community.kurobbs.com" or "web-static.kurobbs.com" ? uri.AbsoluteUri : "";

    private static string Text(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : "";
    private static long? Number(JsonElement item, string name)
        => long.TryParse(Text(item, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
