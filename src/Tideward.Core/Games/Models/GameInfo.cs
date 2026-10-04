using Tideward.Core.Games;
using System.Text.Json.Serialization;

namespace Tideward.Core.Games.Models;

public class GameInfo : GameId
{

        [JsonPropertyName("display")]
    public GameInfoDisplay Display { get; set; }

        [JsonPropertyName("display_status")]
    public string DisplayStatus { get; set; }

}

public class GameInfoDisplay
{

    [JsonPropertyName("language")]
    public string Language { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

        [JsonPropertyName("icon")]
    public GameImage Icon { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; }

        [JsonPropertyName("background")]
    public GameImage Background { get; set; }

        [JsonPropertyName("logo")]
    public GameImage Logo { get; set; }

        [JsonPropertyName("thumbnail")]
    public GameImage Thumbnail { get; set; }

        [JsonPropertyName("shortcut")]
    public GameImage Shortcut { get; set; }

        [JsonPropertyName("top_left_logo")]
    public GameImage? TopLeftLogo { get; set; }

        [JsonPropertyName("introduction")]
    public string Introduction { get; set; }

}

public abstract class GameInfoDisplayStatus
{
    public const string LAUNCHER_GAME_DISPLAY_STATUS_AVAILABLE = "LAUNCHER_GAME_DISPLAY_STATUS_AVAILABLE";

    public const string LAUNCHER_GAME_DISPLAY_STATUS_COMING_SOON = "LAUNCHER_GAME_DISPLAY_STATUS_COMING_SOON";

    public const string LAUNCHER_GAME_DISPLAY_STATUS_RESERVATION_ENABLED = "LAUNCHER_GAME_DISPLAY_STATUS_RESERVATION_ENABLED";
}
