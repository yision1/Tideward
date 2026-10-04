using System.Text.Json.Serialization;

namespace Tideward.Core.Games.Models;

public class GameConfig
{
    [JsonPropertyName("game")]
    public GameId GameId { get; set; } = null!;
    [JsonPropertyName("exe_file_name")]
    public string ExeFileName { get; set; } = "";
    [JsonPropertyName("installation_dir")]
    public string InstallationDir { get; set; } = "";
    [JsonPropertyName("audio_pkg_scan_dir")]
    public string AudioPackageScanDir { get; set; } = "";
    [JsonPropertyName("audio_pkg_res_dir")]
    public string AudioPackageResDir { get; set; } = "";
    [JsonPropertyName("audio_pkg_cache_dir")]
    public string AudioPackageCacheDir { get; set; } = "";
    [JsonPropertyName("game_cached_res_dir")]
    public string GameCachedResDir { get; set; } = "";
    [JsonPropertyName("game_screenshot_dir")]
    public string GameScreenshotDir { get; set; } = "";
    [JsonPropertyName("game_log_gen_dir")]
    public string GameLogGenDir { get; set; } = "";
    [JsonPropertyName("related_processes")]
    public List<string> RelatedProcesses { get; set; } = [];
}
