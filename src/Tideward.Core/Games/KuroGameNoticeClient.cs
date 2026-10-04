using System.Net;
using System.Text.Json;

namespace Tideward.Core.Games;

public sealed record KuroGameNotice(string Id, bool Alert)
{
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public string Category { get; init; } = "game";
}

public sealed class KuroGameNoticeClient(HttpClient? client = null)
{
    public const string PublicServer = "e7e8965f8a6ff61b8d10b7dcc742afd5";
    private readonly HttpClient http = client ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });

    public static string Language(GameBiz region, string culture) => region == GameBiz.wuwa_cn ? "zh-Hans" : culture switch
    {
        "zh-CN" => "zh-Hans", "zh-TW" or "zh-HK" => "zh-Hant", "ja-JP" => "ja", "ko-KR" => "ko",
        "de-DE" => "de", "fr-FR" => "fr", "es-ES" => "es", "ru-RU" => "ru", _ => "en",
    };

    public static Uri PageUri(GameBiz region, string culture, string? roleId = null)
    {
        _ = KuroDistribution.AppId(region);
        if (region != GameBiz.wuwa_cn) throw new NotSupportedException("国际服游戏公告入口尚未验证，请暂用官网公告。");
        string root = region == GameBiz.wuwa_cn ? "https://aki-gm-resources.aki-game.com" : "https://aki-gm-resources-oversea.aki-game.net";
        string game = region == GameBiz.wuwa_cn ? "G152" : "G153";
        string role = string.IsNullOrEmpty(roleId) ? "" : roleId.All(char.IsAsciiDigit) ? roleId : throw new ArgumentException("Invalid notice role.");
        return new($"{root}/aki/announcement/index.html?game_id={game}&server_id={PublicServer}&lang={Language(region, culture)}&platform=PC&channel=0&user_id=before_login&role_id={role}");
    }

    public async Task<List<KuroGameNotice>> GetAsync(GameBiz region, string culture, string? roleId, CancellationToken token = default, bool publicView = false)
    {
        _ = PageUri(region, culture, roleId);
        string host = region == GameBiz.wuwa_cn ? "aki-gm-resources-back.aki-game.com" : "aki-gm-resources-back.aki-game.net";
        string game = region == GameBiz.wuwa_cn ? "G152" : "G153";
        string json = await http.GetStringAsync($"https://{host}/gamenotice/{game}/{PublicServer}/{Language(region, culture)}.json", token);
        return Parse(json, roleId, DateTimeOffset.UtcNow, publicView);
    }

    public static List<KuroGameNotice> Parse(string json, string? roleId, DateTimeOffset now, bool publicView = false)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<KuroGameNotice>();
        foreach (string category in new[] { "game", "activity", "recommend" })
        {
            if (!doc.RootElement.TryGetProperty(category, out var items) || items.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in items.EnumerateArray())
            {
                long time = now.ToUnixTimeMilliseconds();
                if (Number(item, "startTimeMs") >= time || Number(item, "endTimeMs") <= time) continue;
                bool permanent = Number(item, "permanent") == 1;
                var whitelist = item.TryGetProperty("whiteList", out var list) ? list.EnumerateArray().Select(x => x.ToString()).ToArray() : [];
                if (whitelist.Length > 0 ? !whitelist.Contains(roleId) : !publicView && !permanent && string.IsNullOrEmpty(roleId)) continue;
                if (!item.TryGetProperty("platform", out var platform) || !platform.EnumerateArray().Any(x => x.ToString() == "1")) continue;
                if (item.TryGetProperty("channel", out var channels) && channels.GetArrayLength() > 0 && !channels.EnumerateArray().Any(x => x.ToString() == "0")) continue;
                string id = item.GetProperty("id").ToString();
                if (id.Length > 0) result.Add(new(id, Number(item, "red") == 1 && !permanent && !string.IsNullOrEmpty(roleId))
                {
                    Title = item.TryGetProperty("tabTitle", out var title) ? title.GetString() ?? "" : "",
                    Content = item.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "",
                    Category = category,
                });
            }
        }
        return result.DistinctBy(x => x.Id).ToList();
    }

    public static string[] Unread(IEnumerable<KuroGameNotice> notices, IEnumerable<string> readIds)
    {
        var read = readIds.ToHashSet(StringComparer.Ordinal);
        return notices.Where(x => x.Alert && !read.Contains(x.Id)).Select(x => x.Id).ToArray();
    }

    private static long Number(JsonElement item, string key) => item.TryGetProperty(key, out var value) && long.TryParse(value.ToString(), out long number) ? number : 0;
}
