using Tideward.Core.Gacha;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tideward.Core.Games;

public sealed record KuroGachaLink(GameBiz Region, long PlayerId, string ServerId, string RecordId, string ResourcesId, string Language)
{
    public static KuroGachaLink Parse(string text, GameBiz region)
    {
        _ = KuroDistribution.AppId(region);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo != ""
            || !(uri.Host is "aki-gm-resources.aki-game.com" or "aki-gm-resources.aki-game.net"
                or "aki-gm-resources-oversea.aki-game.com" or "aki-gm-resources-oversea.aki-game.net"))
            throw new FormatException("请输入游戏内唤取记录页面的完整链接。");
        int index = text.IndexOf('?');
        if (index < 0) throw new FormatException("唤取链接缺少参数。");
        var values = new Dictionary<string, string>();
        foreach (var field in text[(index + 1)..].Split('&'))
        {
            var pair = field.Split('=', 2);
            if (pair.Length != 2 || !values.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1])))
                throw new FormatException("唤取链接参数无效。");
        }
        string Need(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new FormatException("唤取链接参数不完整。");
        if (!long.TryParse(Need("player_id"), out long uid) || uid <= 0) throw new FormatException("玩家 ID 无效。");
        var area = Need("svr_area");
        bool mainland = region.IsChinaServer() || region.IsBilibili();
        if ((mainland && area != "cn") || (region.IsGlobalServer() && area != "global" && area != "os"))
            throw new FormatException("唤取链接区服与当前选择不一致。");
        if (uri.Host.EndsWith(".com") != mainland) throw new FormatException("唤取链接域名与区服不一致。");
        return new(region, uid, Need("svr_id"), Need("record_id"), Need("resources_id"), Need("lang"));
    }
}

public sealed record KuroGachaType(int Value, string Name) : IGachaType
{
    public string ToLocalization() => Name;
}

public sealed class KuroGachaClient(HttpClient? client = null)
{
    private readonly HttpClient http = client ?? new(new HttpClientHandler { AllowAutoRedirect = false });
    public static IReadOnlyCollection<IGachaType> Types { get; } = new IGachaType[]
    {
        new KuroGachaType(1, "角色活动唤取"), new KuroGachaType(2, "武器活动唤取"),
        new KuroGachaType(3, "角色常驻唤取"), new KuroGachaType(4, "武器常驻唤取"),
        new KuroGachaType(5, "新手唤取"), new KuroGachaType(6, "新手自选唤取"),
        new KuroGachaType(7, "感恩定向唤取"), new KuroGachaType(8, "角色新旅唤取"),
        new KuroGachaType(9, "武器新旅唤取"), new KuroGachaType(10, "角色联动唤取"),
        new KuroGachaType(11, "武器联动唤取"), new KuroGachaType(12, "角色忆旅唤取"), new KuroGachaType(13, "武器忆旅唤取")
    };

    public async Task<List<GachaLogItem>> FetchAsync(KuroGachaLink link, IProgress<string>? progress = null, CancellationToken token = default)
    {
        string endpoint = link.Region.IsGlobalServer() ? "https://gmserver-api.aki-game2.net/gacha/record/query" : "https://gmserver-api.aki-game2.com/gacha/record/query";
        var result = new List<GachaLogItem>();
        foreach (var type in Types)
        {
            progress?.Report(type.ToLocalization());
            using var response = await http.PostAsJsonAsync(endpoint, new { cardPoolId = link.ResourcesId, cardPoolType = type.Value,
                languageCode = link.Language, playerId = link.PlayerId.ToString(CultureInfo.InvariantCulture), recordId = link.RecordId, serverId = link.ServerId }, token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"唤取请求失败：HTTP {(int)response.StatusCode}。");
            var body = await response.Content.ReadFromJsonAsync<Response>(KuroDistribution.JsonOptions, token)
                ?? throw new InvalidDataException("唤取响应为空。");
            if (body.Code != 0) throw new InvalidOperationException($"唤取接口返回错误 {body.Code}，请在游戏中重新打开唤取记录页面，再点击“更新记录”。");
            if (body.Data is null) throw new InvalidDataException("唤取响应缺少记录列表。");
            foreach (var item in body.Data.AsEnumerable().Reverse())
            {
                if (item.ResourceId <= 0 || item.QualityLevel is < 3 or > 5 || item.Count <= 0
                    || !DateTime.TryParseExact(item.Time, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    throw new InvalidDataException("唤取记录格式无效。");
                result.Add(new GachaLogItem { Uid = link.PlayerId, GachaType = type.Value, ItemId = item.ResourceId, Name = item.Name,
                    RankType = item.QualityLevel, ItemType = item.ResourceType, Count = item.Count, Time = time, Lang = link.Language });
            }
        }
        return result;
    }

    public static List<GachaLogItem> Merge(IEnumerable<GachaLogItem> saved, IEnumerable<GachaLogItem> fetched)
    {
        var result = saved.Select(x => x.Clone()).ToList();
        long id = result.Count == 0 ? 0 : result.Max(x => x.Id);
        static string Key(GachaLogItem x) => $"{x.Uid}/{x.GachaType}/{x.Time:O}/{x.ItemId}/{x.Count}/{x.RankType}";
        var counts = result.GroupBy(Key).ToDictionary(x => x.Key, x => x.Count());
        var seen = new Dictionary<string, int>();
        foreach (var item in fetched)
        {
            string key = Key(item); seen.TryGetValue(key, out int n); seen[key] = ++n;
            if (n <= counts.GetValueOrDefault(key)) continue;
            var copy = item.Clone(); copy.Id = ++id; result.Add(copy);
        }
        return result.OrderBy(x => x.Time).ThenBy(x => x.Id).ToList();
    }

    public sealed class Response
    {
        public int Code { get; set; } = int.MinValue;
        public List<Item>? Data { get; set; }
    }
    public sealed class Item
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int ResourceId { get; set; }
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int QualityLevel { get; set; }
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Count { get; set; }
        public string ResourceType { get; set; } = "";
        public string Name { get; set; } = "";
        public string Time { get; set; } = "";
    }
}
