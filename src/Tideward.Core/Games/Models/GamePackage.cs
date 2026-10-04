using Tideward.Core.Games;
using System.Text.Json.Serialization;

namespace Tideward.Core.Games.Models;

public class GamePackage
{

    [JsonPropertyName("game")]
    public GameId GameId { get; set; }

        [JsonPropertyName("main")]
    public GamePackageVersion Main { get; set; }

        [JsonPropertyName("pre_download")]
    public GamePackageVersion PreDownload { get; set; }

}

public class GamePackageVersion
{
        [JsonPropertyName("major")]
    public GamePackageResource? Major { get; set; }

        [JsonPropertyName("patches")]
    public List<GamePackageResource> Patches { get; set; }

}

public class GamePackageResource
{

        [JsonPropertyName("version")]
    public string Version { get; set; }

        [JsonPropertyName("game_pkgs")]
    public List<GamePackageFile> GamePackages { get; set; }

        [JsonPropertyName("audio_pkgs")]
    public List<GamePackageFile> AudioPackages { get; set; }

        [JsonPropertyName("res_list_url")]
    public string ResListUrl { get; set; }

}

public class GamePackageFile
{

        [JsonPropertyName("language")]
    public string? Language { get; set; }

        [JsonPropertyName("url")]
    public string Url { get; set; }

        [JsonPropertyName("md5")]
    public string MD5 { get; set; }

        [JsonPropertyName("size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Size { get; set; }

        [JsonPropertyName("decompressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long DecompressedSize { get; set; }

}

