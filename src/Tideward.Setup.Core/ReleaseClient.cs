using Tideward.Setup.Core.Github;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tideward.Setup.Core;

public class ReleaseClient
{
    public static string? GithubRepository { get; set; } = "yision1/Tideward";

    private static string RequireGithubRepository() => GithubRepository
        ?? throw new InvalidOperationException("Tideward GitHub release repository is not configured.");

    private readonly HttpClient _httpClient;

    public ReleaseClient(HttpClient httpClient)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                EnableMultipleHttp2Connections = true,
                EnableMultipleHttp3Connections = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"{Path.GetFileNameWithoutExtension(Environment.ProcessPath)}/*");
        }
        else
        {
            _httpClient = httpClient;
        }
    }

    public async Task<ReleaseInfo> GetLatestReleaseInfoAsync(bool isPrerelease, string currentVersion, CancellationToken cancellationToken = default)
    {
        var release = isPrerelease
            ? await GetGithubLatestReleaseAsync(cancellationToken)
            : await _httpClient.GetFromJsonAsync($"https://api.github.com/repos/{RequireGithubRepository()}/releases/latest", ReleaseJsonContext.Default.GithubRelease, cancellationToken);
        return CreateReleaseInfo(release);
    }

    public async Task<ReleaseInfoDetail> GetLatestReleaseInfoDetailAsync(bool isPrerelease, string currentVersion, Architecture arch, InstallType type, CancellationToken cancellationToken = default)
    {
        var info = await GetLatestReleaseInfoAsync(isPrerelease, currentVersion, cancellationToken);
        string key = $"{arch}-{type}".ToLower();
        if (info.Releases?.TryGetValue(key, out var value) ?? false)
        {
            return value;
        }
        else
        {
            throw new PlatformNotSupportedException($"Platform ({arch}, {type}) is not supported.");
        }
    }

    public async Task<ReleaseInfo> GetReleaseInfoAsync(string version, CancellationToken cancellationToken = default)
    {
        return CreateReleaseInfo(await GetGithubReleaseAsync(version, cancellationToken));
    }

    private static ReleaseInfo CreateReleaseInfo(GithubRelease? release)
    {
        if (release is null || release.Draft)
        {
            throw new InvalidOperationException("No published Tideward release was found.");
        }
        var info = new ReleaseInfo { Version = release.TagName, Releases = new() };
        foreach (var arch in new[] { Architecture.X64, Architecture.Arm64 })
        {
            foreach (var type in new[] { InstallType.Setup, InstallType.Portable })
            {
                string extension = type is InstallType.Setup ? "exe" : "7z";
                string name = $"Tideward_{type}_{release.TagName}_{arch.ToString().ToLowerInvariant()}.{extension}";
                var asset = release.Assets?.FirstOrDefault(x => x.Name == name);
                if (asset is null) { continue; }
                info.Releases.Add($"{arch}-{type}".ToLowerInvariant(), new ReleaseInfoDetail
                {
                    Version = release.TagName,
                    Architecture = arch,
                    InstallType = type,
                    BuildTime = release.PublishedAt,
                    DisableAutoUpdate = true,
                    PackageUrl = asset.BrowserDownloadUrl,
                    PackageSize = asset.Size,
                    PackageHash = string.Empty,
                    ManifestUrl = string.Empty,
                    Diffs = new(),
                });
            }
        }
        return info;
    }

    public async Task<ReleaseManifest> GetReleaseManifestAsync(string url, CancellationToken cancellationToken = default)
    {
        var manifest = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ReleaseManifest, cancellationToken);
        return manifest ?? throw new NullReferenceException($"Cannot get json content from '{url}'.");
    }

    #region Github

    public async Task<GithubRelease?> GetGithubLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        string url = $"https://api.github.com/repos/{RequireGithubRepository()}/releases?page=1&per_page=1";
        var list = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ListGithubRelease, cancellationToken);
        return list?.FirstOrDefault();
    }

    public async Task<List<GithubRelease>> GetGithubReleaseAsync(int page, int perPage, CancellationToken cancellationToken = default)
    {
        string url = $"https://api.github.com/repos/{RequireGithubRepository()}/releases?page={page}&per_page={perPage}";
        var list = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ListGithubRelease, cancellationToken);
        return list ?? new List<GithubRelease>();
    }

    public async Task<GithubRelease?> GetGithubReleaseAsync(string tag, CancellationToken cancellationToken = default)
    {
        string url = $"https://api.github.com/repos/{RequireGithubRepository()}/releases/tags/{Uri.EscapeDataString(tag)}";
        return await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.GithubRelease, cancellationToken);
    }

    public async Task<string> RenderGithubMarkdownAsync(string markdown, CancellationToken cancellationToken = default)
    {
        const string url = "https://api.github.com/markdown";
        var request = new GithubMarkdownRequest
        {
            Text = markdown,
            Mode = "gfm",
            Context = RequireGithubRepository(),
        };
        var content = new StringContent(JsonSerializer.Serialize(request, ReleaseJsonContext.Default.GithubMarkdownRequest), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    #endregion

}
