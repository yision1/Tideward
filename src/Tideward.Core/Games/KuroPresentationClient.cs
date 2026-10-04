using Tideward.Core.Games.Models;
using System.Net;
using System.Text.Json;

namespace Tideward.Core.Games;

/// <summary>Public Kuro launcher content, independent of game distribution and account credentials.</summary>
public sealed class KuroPresentationClient(HttpClient? client = null)
{
    public const string GameIcon = "ms-appx:///Assets/Game/wuwa.jpg";
    public const string GameCard = "ms-appx:///Assets/Game/wuwa-card.webp";
    private readonly HttpClient http = client ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });

    private static (string Host, string Slug, string Language) Region(GameBiz region) => region.Value switch
    {
        GameBiz.wuwa_cn => ("https://prod-cn-alicdn-gamestarter.kurogame.com", "10003_Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5/G152", "zh-Hans"),
        GameBiz.wuwa_global => ("https://prod-alicdn-gamestarter.kurogame.com", "50004_obOHXFrFanqsaIEOmuKroCcbZkQRBC7c/G153", "en"),
        _ => throw new ArgumentException("Unknown Wuthering Waves region.")
    };

    public async Task<GameContent> GetContentAsync(GameId game, CancellationToken token = default)
    {
        WutheringWavesCatalog.Require(game);
        var (host, slug, language) = Region(game.GameBiz);
        string json = await http.GetStringAsync($"{host}/launcher/{slug}/information/{language}.json", token);
        return ParseContent(game, language, json);
    }

    public async Task<GameBackgroundInfo> GetBackgroundAsync(GameId game, CancellationToken token = default)
    {
        WutheringWavesCatalog.Require(game);
        var (host, slug, language) = Region(game.GameBiz);
        using var index = JsonDocument.Parse(await http.GetStringAsync($"{host}/launcher/launcher/{slug}/index.json", token));
        string code = index.RootElement.GetProperty("functionCode").GetProperty("background").GetString() ?? "";
        if (code.Length == 0 || !code.All(char.IsAsciiLetterOrDigit)) throw new InvalidDataException("Invalid Kuro background code.");
        string json = await http.GetStringAsync($"{host}/launcher/{slug}/background/{code}/{language}.json", token);
        return new GameBackgroundInfo { GameId = game, Backgrounds = ParseBackgrounds(code, json) };
    }

    public static GameContent ParseContent(GameId game, string language, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new GameContent { GameId = game, Language = language, Banners = [], Posts = [], SocialMediaList = [] };
        if (doc.RootElement.TryGetProperty("slideshow", out var slideshow))
            foreach (var item in slideshow.EnumerateArray())
            {
                string image = Https(item.GetProperty("url").GetString());
                string link = Https(item.GetProperty("jumpUrl").GetString());
                result.Banners.Add(new GameBanner { Id = image, Image = new GameImage { Url = image, Link = link } });
            }
        if (doc.RootElement.TryGetProperty("guidance", out var guidance))
            foreach (var (key, type) in new[] { ("activity", GamePostType.POST_TYPE_ACTIVITY), ("notice", GamePostType.POST_TYPE_ANNOUNCE), ("news", GamePostType.POST_TYPE_INFO) })
            {
                if (!guidance.TryGetProperty(key, out var section) || !section.TryGetProperty("contents", out var contents)) continue;
                if (section.TryGetProperty("functionSwitch", out var enabled) && enabled.GetInt32() == 0) continue;
                foreach (var item in contents.EnumerateArray())
                {
                    string link = Https(item.GetProperty("jumpUrl").GetString());
                    result.Posts.Add(new GamePost { Id = link, Type = type, Link = link,
                        Title = item.GetProperty("content").GetString() ?? "", Date = item.GetProperty("time").GetString() ?? "" });
                }
            }
        return result;
    }

    public static GameBackground ParseBackground(string code, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("functionSwitch").GetInt32() != 1) throw new InvalidDataException("Kuro background is unavailable.");
        string poster = Https(root.GetProperty("firstFrameImage").GetString());
        bool video = root.GetProperty("backgroundFileType").GetInt32() == 2;
        return new GameBackground { Id = video ? $"{code}:video" : code,
            Type = video ? GameBackground.BACKGROUND_TYPE_VIDEO : GameBackground.BACKGROUND_TYPE_POSTER,
            Background = new GameImage { Url = poster }, Icon = new GameImage { Url = poster },
            Video = video ? new GameImage { Url = Https(root.GetProperty("backgroundFile").GetString()) } : null!,
            Theme = new GameImage { Url = Https(root.GetProperty("slogan").GetString()) } };
    }

    public static List<GameBackground> ParseBackgrounds(string code, string json)
    {
        var background = ParseBackground(code, json);
        if (background.Type != GameBackground.BACKGROUND_TYPE_VIDEO) return [background];
        return [background, new GameBackground
        {
            Id = $"{code}:poster", Type = GameBackground.BACKGROUND_TYPE_POSTER,
            Background = background.Background, Icon = background.Icon, Theme = background.Theme
        }];
    }

    private static string Https(string? text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Invalid Kuro presentation URL.");
        return uri.AbsoluteUri;
    }
}
