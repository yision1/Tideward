using Tideward.Core.Games;
using System.Text.Json.Serialization;

namespace Tideward.Core.Games.Models;

public class GameContent
{

    [JsonPropertyName("game")]
    public GameId GameId { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; }

        [JsonPropertyName("banners")]
    public List<GameBanner> Banners { get; set; }

        [JsonPropertyName("posts")]
    public List<GamePost> Posts { get; set; }

        [JsonPropertyName("social_media_list")]
    public List<GameSocialMedia> SocialMediaList { get; set; }

}

public class GameBanner
{

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("image")]
    public GameImage Image { get; set; }

}

public class GamePost
{

    [JsonPropertyName("id")]
    public string Id { get; set; }

        [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

        [JsonPropertyName("link")]
    public string Link { get; set; }

        [JsonPropertyName("date")]
    public string Date { get; set; }

}

public abstract class GamePostType
{
    public const string POST_TYPE_ACTIVITY = "POST_TYPE_ACTIVITY";

    public const string POST_TYPE_ANNOUNCE = "POST_TYPE_ANNOUNCE";

    public const string POST_TYPE_INFO = "POST_TYPE_INFO";
}

public class GameSocialMedia
{

    [JsonPropertyName("id")]
    public string Id { get; set; }

        [JsonPropertyName("icon")]
    public GameImage Icon { get; set; }

        [JsonPropertyName("qr_image")]
    public GameImage QrImage { get; set; }

    [JsonPropertyName("qr_desc")]
    public string QrDesc { get; set; }

        [JsonPropertyName("links")]
    public List<GameSocialMediaLink> Links { get; set; }

    [JsonPropertyName("enable_red_dot")]
    public bool EnableRedDot { get; set; }

    [JsonPropertyName("red_dot_content")]
    public string RedDotContent { get; set; }

}

public class GameSocialMediaLink
{

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("link")]
    public string Link { get; set; }

}
