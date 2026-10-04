using System.Text.Json.Serialization;

namespace Tideward.Core.Games.Models;

public class GameImage
{

        [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("hover_url")]
    public string HoverUrl { get; set; }

        [JsonPropertyName("link")]
    public string Link { get; set; }

    [JsonPropertyName("md5")]
    public string MD5 { get; set; }

    [JsonPropertyName("size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Size { get; set; }

}
