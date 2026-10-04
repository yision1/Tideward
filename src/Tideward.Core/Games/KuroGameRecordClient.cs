using System.Security.Cryptography;
using System.Text.Json;
using System.Net;

namespace Tideward.Core.Games;

public sealed record KuroRecordAvatar(string Name, string Level, string Icon, int SkillBranch = -1)
{
    public string LevelLabel => Level.Length > 0 ? $"Lv.{Level}" : "";
}
public sealed record KuroRecordBuff(string Name, string Description, string Icon, int Quality = 0);
public sealed record KuroRecordPanel(string Title, string Detail, IReadOnlyList<KuroRecordAvatar> Roles, string Group = "", string Image = "", IReadOnlyList<KuroDailyNoteStat>? Metrics = null,
    string GroupDetail = "", string GroupImage = "", string Rank = "", int Stars = -1, IReadOnlyList<KuroRecordBuff>? Buffs = null, string TeamIcon = "");
public sealed record KuroRecordGroup(string Title, string Detail, IReadOnlyList<KuroDailyNoteStat> Stats, IReadOnlyList<KuroRecordPanel> Panels, string Id = "");
public sealed record KuroRecordPeriod(string Id, string Title);
public sealed record KuroRecordData(IReadOnlyList<KuroDailyNoteStat> Stats, IReadOnlyList<KuroRecordPanel> Panels, IReadOnlyList<KuroRecordGroup>? Groups = null, string Period = "", string EmptyMessage = "暂无挑战记录");

public sealed class KuroGameRecordClient
{
    private readonly HttpClient http;
    private const string UserAgent = KuroDeviceIdentity.UserAgent;
    private string? requestIp;
    public KuroGameRecordClient(HttpClient? http = null) => this.http = http ?? new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<KuroRecordData> GetAsync(string token, KuroDailyNoteRole role, string source, string section, CancellationToken cancellationToken = default, KuroDeviceIdentity? device = null, bool refresh = false)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace) || source is not ("h5" or "android")) throw new ArgumentException("请先登录库街区。");
        if (role.RoleId.Length == 0 || !role.RoleId.All(char.IsAsciiDigit) || role.ServerId.Length == 0 || !role.ServerId.All(char.IsAsciiLetterOrDigit)
            || role.UserId.Length == 0 || !role.UserId.All(char.IsAsciiDigit)) throw new InvalidDataException("角色资料不完整，请刷新角色信息。");
        string endpoint = section switch { "overview" => "baseData", "roles" => "roleData", "challenge" => "towerDataDetail", "slash" => "slashDetail", "matrix" => "newTowerDetail", _ => throw new ArgumentException("不支持的战绩栏目。") };
        var form = new Dictionary<string, string> { ["roleId"] = role.RoleId, ["serverId"] = role.ServerId, ["userId"] = role.UserId };
        await InitializeRequestIpAsync(cancellationToken);
        var session = await PostAsync("aki/roleBox/requestToken", form, token, source, "", cancellationToken, device);
        string accessToken = Text(session, "accessToken");
        if (accessToken.Length == 0) throw new InvalidDataException("库街区未返回战绩授权。");

        bool tokenRequired = !session.TryGetProperty("tokenRequire", out var required) || required.ValueKind != JsonValueKind.False;
        if (refresh)
            await PostAsync("aki/roleBox/akiBox/refreshData", new() { ["gameId"] = "3", ["roleId"] = role.RoleId, ["serverId"] = role.ServerId }, tokenRequired ? token : "", source, accessToken, cancellationToken, device, allowStatus: true);
        if (section != "slash") { form.Remove("userId"); if (section != "matrix") form["gameId"] = "3"; }
        var data = await PostAsync("aki/roleBox/akiBox/" + endpoint, form, tokenRequired ? token : "", source, accessToken, cancellationToken, device);
        if (section == "overview" && Text(data, "id") is { Length: > 0 } id && id != role.RoleId)
            throw new InvalidDataException("库街区返回了其他角色的战绩，已停止显示。");
        return ParseSection(data, section);
    }

    public async Task<IReadOnlyList<KuroRecordPeriod>> GetResourcePeriodsAsync(string token, string source, CancellationToken cancellationToken = default)
    {
        var data = await ResourceRequestAsync("period/list", token, source, null, cancellationToken);
        return Array(data, "months").Where(x => Text(x, "index").Length > 0)
            .OrderByDescending(x => Number(x, "index")).Select(x => new KuroRecordPeriod(Text(x, "index"), Text(x, "title"))).ToArray();
    }

    public async Task<KuroRecordData> GetResourceMonthAsync(string token, KuroDailyNoteRole role, string source, string period, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(period, out _)) throw new ArgumentException("请选择资源简报月份。");
        var form = new Dictionary<string, string> { ["roleId"] = role.RoleId, ["serverId"] = role.ServerId, ["period"] = period };
        return ParseSection(await ResourceRequestAsync("month", token, source, form, cancellationToken), "calendar");
    }

    private async Task<JsonElement> ResourceRequestAsync(string endpoint, string token, string source, Dictionary<string, string>? form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace) || source is not ("h5" or "android")) throw new ArgumentException("请先登录库街区。");
        using var request = new HttpRequestMessage(form is null ? HttpMethod.Get : HttpMethod.Post, "https://api.kurobbs.com/aki/resource/" + endpoint);
        request.Headers.Add("token", token);
        request.Headers.Add("source", source);
        request.Headers.Add("Origin", "https://web-static.kurobbs.com");
        request.Headers.Referrer = new Uri("https://web-static.kurobbs.com/resource-briefing/index.html");
        if (form is not null) request.Content = new FormUrlEncodedContent(form);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var data = ParseEnvelope(await response.Content.ReadAsStringAsync(cancellationToken));
        if (data.TryGetProperty("geeTest", out var verification) && verification.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))
            throw new InvalidDataException("资源简报需要库街区安全验证，请先在库街区完成验证后刷新。");
        return data;
    }

    private async Task<JsonElement> PostAsync(string endpoint, Dictionary<string, string> form, string token, string source, string accessToken, CancellationToken cancellationToken, KuroDeviceIdentity? device, bool allowStatus = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.kurobbs.com/" + endpoint);
        if (token.Length > 0) request.Headers.Add("token", token);
        request.Headers.Add("source", source);
        request.Headers.Add("Origin", "https://web-static.kurobbs.com");
        request.Headers.Referrer = new Uri("https://web-static.kurobbs.com/mcbox/index.html");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Add("devCode", requestIp + ", " + UserAgent);
        if (device is not null) device.AddDid(request.Headers);
        else request.Headers.TryAddWithoutValidation("did", "");
        request.Headers.TryAddWithoutValidation("b-at", accessToken);
        request.Content = new FormUrlEncodedContent(form);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return ParseEnvelope(await response.Content.ReadAsStringAsync(cancellationToken), allowStatus);
    }

    private async Task InitializeRequestIpAsync(CancellationToken cancellationToken)
    {
        if (requestIp is not null) return;
        // Match mcbox's public client metadata lookup. No credentials are sent.
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://event.kurobbs.com/event/ip");
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            string text = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (text.StartsWith('"')) text = JsonSerializer.Deserialize<string>(text) ?? "";
            requestIp = IPAddress.TryParse(text, out var address) ? address.ToString() : "127.127.127.127";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requestIp = "127.127.127.127"; // Official frontend's network-error fallback.
        }
    }

    public static JsonElement ParseEnvelope(string json, bool allowStatus = false)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string code = Text(root, "code");
        if (code is "220" or "230" or "10902") throw new InvalidDataException("库街区战绩登录已失效，请重新登录。");
        if (code == "10901") throw new InvalidDataException("库街区拒绝此次战绩授权（10901：禁止访问）。角色、便笺与战绩的读取权限不同；当前便笺连接仍可使用。");
        if (code == "10000") throw new InvalidDataException("库街区战绩请求参数错误（10000），请刷新角色信息后重试。");
        if (code != "200") throw new InvalidDataException($"库街区战绩接口读取失败（代码 {code}），请刷新后重试。");
        if (!root.TryGetProperty("data", out var data)) throw new InvalidDataException("库街区没有返回战绩数据。");
        if (allowStatus && data.ValueKind == JsonValueKind.True) return data.Clone();
        if (allowStatus && data.ValueKind == JsonValueKind.False) throw new InvalidDataException("库街区尚未完成数据更新，请稍后刷新。");
        if (data.ValueKind == JsonValueKind.String)
        {
            string text = data.GetString()!;
            try { using var decoded = JsonDocument.Parse(text); return decoded.RootElement.Clone(); }
            catch (JsonException)
            {
                // Official mcbox response encoding; never a saved account credential.
                try
                {
                    using var aes = Aes.Create(); aes.Key = Convert.FromBase64String("MumyIMISem+2coWbBuJT3w==");
                    byte[] bytes = aes.DecryptEcb(Convert.FromBase64String(text), PaddingMode.PKCS7);
                    try { using var decoded = JsonDocument.Parse(bytes); return decoded.RootElement.Clone(); }
                    finally { CryptographicOperations.ZeroMemory(bytes); }
                }
                catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
                { throw new InvalidDataException("无法解析库街区战绩数据。"); }
            }
        }
        if (data.ValueKind != JsonValueKind.Object) throw new InvalidDataException("库街区没有返回战绩数据。");
        return data.Clone();
    }

    public static KuroRecordData ParseSection(JsonElement data, string section)
    {
        List<KuroDailyNoteStat> stats = [];
        List<KuroRecordPanel> panels = [];
        List<KuroRecordGroup> groups = [];
        if (section == "overview")
        {
            foreach (var (key, label) in new[] { ("level", "联觉等级"), ("worldLevel", "索拉等级"), ("activeDays", "活跃天数"), ("roleNum", "共鸣者"), ("achievementCount", "成就"), ("achievementStar", "成就星数"), ("smallCount", "小型信标"), ("bigCount", "中枢信标"), ("soundBox", "声匣") })
            {
                string value = Text(data, key);
                if (value.Length > 0) stats.Add(new(label, value, ""));
            }
            foreach (var (key, title, labels) in new[] { ("treasureBoxList", "奇藏箱", new[] { "朴素", "基准", "精密", "辉光" }), ("phantomBoxList", "潮汐之遗", new[] { "绿", "紫", "金" }) })
            {
                var metrics = Array(data, key).Where(x => Number(x, "id") >= 1 && Number(x, "id") <= labels.Length)
                    .Select(x => new KuroDailyNoteStat(labels[(int)Number(x, "id") - 1], Text(x, "num"), "")).Where(x => x.Amount.Length > 0).ToArray();
                if (metrics.Length > 0) panels.Add(new(title, "探索收集", [], Metrics: metrics));
            }
        }
        else if (section == "roles")
        {
            var roles = Array(data, "roleList").Select(x => new KuroRecordAvatar(Text(x, "roleName"), Text(x, "level"), Icon(Text(x, "roleIconUrl")))).ToArray();
            if (roles.Length > 0) panels.Add(new("共鸣者", $"已拥有 {roles.Length} 位", roles));
        }
        else if (section == "challenge")
        {
            foreach (var difficulty in Array(data, "difficultyList"))
            {
                List<KuroRecordPanel> groupPanels = [];
                List<KuroDailyNoteStat> groupStats = [];
                foreach (var area in Array(difficulty, "towerAreaList"))
                {
                    if (Text(area, "star").Length > 0) groupStats.Add(new(Text(area, "areaName"), Ratio(area, "star", "maxStar"), "印记"));
                    var floors = Array(area, "floorList").ToArray();
                    foreach (var floor in floors)
                    {
                        var roles = Avatars(floor);
                        string stars = Text(floor, "star");
                        int count = int.TryParse(stars, out var parsedStars) ? Math.Clamp(parsedStars, 0, 3) : -1;
                        string detail = roles.Count == 0 ? "暂无挑战记录" : count >= 0 ? new string('★', count) + new string('☆', 3 - count) : stars;
                        groupPanels.Add(new($"第 {Text(floor, "floor")} 层", detail, roles, Text(area, "areaName"), Icon(Text(floor, "picUrl")),
                            GroupDetail: Ratio(area, "star", "maxStar"), Stars: count));
                    }
                    if (floors.Length == 0) groupPanels.Add(new(Text(area, "areaName"), "暂无挑战记录", [], Text(area, "areaName"), GroupDetail: Ratio(area, "star", "maxStar")));
                }
                panels.AddRange(groupPanels);
                if (Text(difficulty, "star").Length > 0) groupStats = [new("总印记", Ratio(difficulty, "star", "maxStar"), "")];
                groups.Add(new(Text(difficulty, "difficultyName"), "", groupStats, groupPanels, Text(difficulty, "difficulty")));
            }
            groups.Reverse();
        }
        else if (section == "slash")
        {
            foreach (var difficulty in Array(data, "difficultyList"))
            {
                List<KuroRecordPanel> groupPanels = [];
                List<KuroDailyNoteStat> groupStats = [];
                foreach (var challenge in Array(difficulty, "challengeList"))
                {
                    string challengeName = Text(challenge, "challengeName");
                    int halfIndex = 0;
                    foreach (var half in Array(challenge, "halfList"))
                    {
                        var roles = Avatars(half);
                        string score = Text(half, "score");
                        groupPanels.Add(new($"队伍 {++halfIndex}", roles.Count == 0 && score.Length == 0 ? "暂无挑战记录" : $"积分 {score}", roles,
                            Group: (Text(challenge, "challengeId").Length > 0 ? Text(challenge, "challengeId") + " · " : "") + challengeName,
                            GroupDetail: Text(challenge, "score").Length > 0 ? $"积分 {Text(challenge, "score")}" : "暂无挑战记录",
                            GroupImage: Icon(Text(difficulty, "detailPageBG")), Rank: Text(challenge, "rank"), Buffs: Buffs(half), TeamIcon: Icon(Text(difficulty, "teamIcon"))));
                    }
                    if (halfIndex == 0) groupPanels.Add(new(challengeName, "暂无挑战记录", []));
                }
                panels.AddRange(groupPanels);
                if (Text(difficulty, "allScore").Length > 0) groupStats.Add(new(Text(difficulty, "difficulty") == "2" ? "湍渊总积分" : "海隙总积分", Ratio(difficulty, "allScore", "maxScore"), ""));
                groups.Add(new(Text(difficulty, "difficultyName"), "", groupStats, groupPanels, Text(difficulty, "difficulty")));
            }

            var main = groups.FindIndex(x => x.Id == "1");
            var extra = groups.FindIndex(x => x.Id == "2");
            if (main >= 0 && extra >= 0)
            {
                groups[main] = groups[main] with { Stats = groups[main].Stats.Concat(groups[extra].Stats).ToArray(), Panels = groups[extra].Panels.Concat(groups[main].Panels).ToArray() };
                groups.RemoveAt(extra);
            }
            for (int i = 0; i < groups.Count; i++)
                if (groups[i].Title == "再生海域-海隙") groups[i] = groups[i] with { Title = "再生海域" };
            int regeneration = groups.FindIndex(x => x.Title == "再生海域");
            int forbidden = groups.FindIndex(x => x.Title == "禁忌海域");
            if (regeneration > forbidden && forbidden >= 0)
            {
                var group = groups[regeneration];
                groups.RemoveAt(regeneration);
                groups.Insert(forbidden, group);
            }
        }
        else if (section == "matrix")
        {
            foreach (var mode in Array(data, "modeDetails"))
            {
                string name = Text(mode, "modeId") == "0" ? "稳态协议" : "奇点扩张";
                List<KuroDailyNoteStat> modeStats = [];
                if (Text(mode, "score").Length > 0) modeStats.Add(new("累计积分", Text(mode, "score"), ""));
                if (Text(mode, "passBoss").Length > 0) modeStats.Add(new("挑战进度", Ratio(mode, "passBoss", "bossCount"), ""));
                if (Text(mode, "round").Length > 0) modeStats.Add(new("当前轮次", Text(mode, "round"), ""));
                var teams = Array(mode, "teams").Select((team, index) => new KuroRecordPanel($"队伍 {index + 1}",
                    $"积分 {Text(team, "score")} · 挑战进度 {Ratio(team, "passBoss", "bossCount")}",
                    Avatars(team), Text(team, "round").Length > 0 ? $"第 {Text(team, "round")} 轮" : "出战队伍", Buffs: Buffs(team))).ToArray();
                panels.AddRange(teams);
                groups.Add(new(name, Flag(mode, "isUnlock", false) ? "" : "暂未解锁", modeStats, teams));
            }
        }
        else if (section == "calendar")
        {
            foreach (var item in Array(data, "itemList"))
            {
                string name = Text(item, "type") switch { "1" => "贝币", "2" => "星声", "3" => "唤声涡纹", "4" => "浮金波纹 & 铸潮波纹", _ => "" };
                if (name.Length == 0) continue;
                if (Text(item, "total").Length > 0) stats.Add(new(name, Text(item, "total"), "本月获取"));
                var detail = Array(item, "detail").Select(x => new KuroDailyNoteStat(Text(x, "type"), Text(x, "num"), "")).Where(x => x.Amount.Length > 0).ToArray();
                panels.Add(new(name, "收入来源", [], Metrics: detail));
            }
        }
        string period = Text(data, "sessionId").Length > 0 && Text(data, "stageId").Length > 0 ? $"S{Text(data, "sessionId")}-{Text(data, "stageId")}" : "";
        double end = Number(data, "seasonEndTime");
        // The official challenge pages format seasonEndTime / 1000 as a remaining duration.
        if (end > 0 && end < TimeSpan.MaxValue.TotalMilliseconds)
        {
            var remaining = TimeSpan.FromMilliseconds(end);
            period = remaining.Days > 0 ? $"距离周期重置 {remaining.Days}天{remaining.Hours}小时"
                : remaining.Hours > 0 ? $"距离周期重置 {remaining.Hours}小时{remaining.Minutes}分钟"
                : $"距离周期重置 {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}分钟";
        }
        return new(stats, panels, groups, period, Flag(data, "isUnlock", true) ? "暂无挑战记录" : "暂未解锁");
    }

    private static IReadOnlyList<KuroRecordAvatar> Avatars(JsonElement item) => Array(item, "roleList").Select(x => new KuroRecordAvatar(Text(x, "roleName"), Text(x, "level"), Icon(Text(x, "iconUrl")),
        Text(x, "skillBranchIndex") is "0" or "1" ? (int)Number(x, "skillBranchIndex") : -1)).ToArray();
    private static IReadOnlyList<KuroRecordBuff> Buffs(JsonElement item)
    {
        var buffs = Array(item, "buffs").Select(Buff).ToList();
        if (Text(item, "buffIcon").Length > 0 || Text(item, "buffName").Length > 0) buffs.Add(Buff(item));
        return buffs;
    }
    private static KuroRecordBuff Buff(JsonElement item) => new(Text(item, "buffName"), Text(item, "buffDescription"), Icon(Text(item, "buffIcon")), (int)Number(item, "buffQuality"));
    private static double Number(JsonElement item, string key) => double.TryParse(Text(item, key), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static bool Flag(JsonElement item, string key, bool fallback) => item.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static string Ratio(JsonElement item, string current, string total) => Text(item, total).Length > 0 ? $"{Text(item, current)}/{Text(item, total)}" : Text(item, current);

    private static IEnumerable<JsonElement> Array(JsonElement item, string key)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];
    private static string Text(JsonElement item, string key)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : "";
    private static string Icon(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0
            && (uri.Host == "kurobbs.com" || uri.Host.EndsWith(".kurobbs.com", StringComparison.OrdinalIgnoreCase)) ? uri.AbsoluteUri : "";
}
