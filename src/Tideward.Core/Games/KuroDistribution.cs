using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tideward.Core.Games;

public sealed class KuroIndex
{
    [JsonPropertyName("default")] public KuroRelease Release { get; set; } = new();
}

public sealed class KuroRelease
{
    public List<KuroCdn> CdnList { get; set; } = [];
    public KuroPackage Config { get; set; } = new();
}

public sealed class KuroCdn
{
    [JsonPropertyName("P")] public int Weight { get; set; }
    public string Url { get; set; } = "";
}

public sealed class KuroPackage
{
    public string Version { get; set; } = "";
    public string IndexFile { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public long Size { get; set; }
    public long UnCompressSize { get; set; }
    public List<KuroPackage> PatchConfig { get; set; } = [];
}

public sealed class KuroManifest
{
    public List<KuroFile> Resource { get; set; } = [];
    public List<string> DeleteFiles { get; set; } = [];
    public List<KuroPatchGroup> GroupInfos { get; set; } = [];
}

public sealed class KuroFile
{
    public string Dest { get; set; } = "";
    public string Md5 { get; set; } = "";
    public long Size { get; set; }
    public string? FromFolder { get; set; }
}

public sealed class KuroPatchGroup
{
    public string Dest { get; set; } = "";
    public List<KuroFile> SrcFiles { get; set; } = [];
    public List<KuroFile> DstFiles { get; set; } = [];
}

public sealed record KuroLocalVersion(string Version, string AppId);

public sealed class KuroDistribution
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient client;

    public KuroDistribution(HttpClient? client = null) => this.client = client ?? new HttpClient(CreateHttpHandler());

    public static HttpClientHandler CreateHttpHandler() => new()
    { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = false };

    public static string AppId(GameBiz region) => region.Value switch
    {
        GameBiz.wuwa_cn => "10003", GameBiz.wuwa_global => "50004",
        _ => throw new ArgumentException("Unknown Wuthering Waves region.")
    };

    public static Uri IndexUri(GameBiz region) => region.Value switch
    {
        GameBiz.wuwa_cn => new("https://prod-cn-alicdn-gamestarter.kurogame.com/launcher/game/G152/10003_Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5/index.json"),
        GameBiz.wuwa_global => new("https://prod-alicdn-gamestarter.kurogame.com/launcher/game/G153/50004_obOHXFrFanqsaIEOmuKroCcbZkQRBC7c/index.json"),
        _ => throw new ArgumentException("Unknown Wuthering Waves region.")
    };

    public async Task<KuroRelease> GetReleaseAsync(GameBiz region, CancellationToken token = default)
    {
        var index = await client.GetFromJsonAsync<KuroIndex>(IndexUri(region), JsonOptions, token)
            ?? throw new InvalidDataException("Empty Kuro index.");
        if (!Version.TryParse(index.Release.Config.Version, out _) || index.Release.CdnList.All(x => x.Weight <= 0))
            throw new InvalidDataException("Incomplete Kuro index.");
        return index.Release;
    }

    public async Task<KuroManifest> GetManifestAsync(KuroRelease release, KuroPackage package, CancellationToken token = default)
    {
        var manifest = await client.GetFromJsonAsync<KuroManifest>(Resolve(release, package.IndexFile), JsonOptions, token)
            ?? throw new InvalidDataException("Empty Kuro manifest.");
        if (manifest.Resource.Count == 0) throw new InvalidDataException("Kuro manifest contains no files.");
        return manifest;
    }

    public static Uri Resolve(KuroRelease release, string relative)
    {
        var cdn = release.CdnList.Where(x => x.Weight > 0).OrderByDescending(x => x.Weight).First();
        var root = new Uri(cdn.Url);
        if (root.Scheme != "https" || !(root.Host.EndsWith(".aki-game.com", StringComparison.OrdinalIgnoreCase)
            || root.Host.EndsWith(".aki-game.net", StringComparison.OrdinalIgnoreCase)
            || root.Host.EndsWith(".kurogame.com", StringComparison.OrdinalIgnoreCase)
            || root.Host.EndsWith(".kurogame.net", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Unrecognized Kuro CDN.");
        if (Uri.TryCreate(relative, UriKind.Absolute, out _) || relative.Contains("..") || relative.Contains('\\'))
            throw new InvalidDataException("Invalid Kuro resource URL.");
        return new Uri(root, relative.TrimStart('/'));
    }

    public static KuroLocalVersion? ReadLocalVersion(string root, GameBiz region)
    {
        string path = Path.Combine(root, "launcherDownloadConfig.json");
        if (!File.Exists(path)) return null;
        var version = JsonSerializer.Deserialize<KuroLocalVersion>(File.ReadAllText(path), JsonOptions);
        if (version?.AppId != AppId(region)) throw new InvalidDataException("Installation belongs to a different region.");
        if (!Version.TryParse(version.Version, out _)) throw new InvalidDataException("Invalid installed version.");
        return version;
    }
}
