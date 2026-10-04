using Tideward.Core.Games;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Tideward.Core.Games.Models;
using Tideward.Features.Games;
using Tideward.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Storage;

namespace Tideward.Features.Background;

public class BackgroundService
{

    private readonly ILogger<BackgroundService> _logger;

    private readonly GameCatalogService _gameCatalogService;

    private readonly HttpClient _httpClient;

    private static readonly SemaphoreSlim _downloadLock = new(1, 1);

    public BackgroundService(ILogger<BackgroundService> logger, GameCatalogService gameCatalogService, HttpClient httpClient)
    {
        _logger = logger;
        _gameCatalogService = gameCatalogService;
        _httpClient = httpClient;
    }

    public static string BackgroundFolder => Path.Join(AppConfig.IsPortable ? AppConfig.UserDataFolder : AppConfig.CacheFolder, "bg");

    [return: NotNullIfNotNull(nameof(name))]
    public static string? GetBgFilePath(string? name)
    {
        return Path.Join(BackgroundFolder, name);
    }

        public static bool TryGetCustomBgFilePath(GameId gameId, [NotNullWhen(true)] out string? path)
    {
        path = null;
        if (gameId is null)
        {
            return false;
        }
        if (AppConfig.GetEnableCustomBg(gameId.GameBiz))
        {
            path = GetBgFilePath(AppConfig.GetCustomBg(gameId.GameBiz));
            if (File.Exists(path))
            {
                return true;
            }
        }
        return false;
    }

        public static bool FileIsSupportedVideo(string? name)
    {
        return Path.GetExtension(name) switch
        {
            ".mp4" or ".mkv" or ".webm" => true,
            _ => false,
        };
    }

        public static string? GetCachedBackgroundFile(GameId gameId)
    {
        if (gameId is null)
        {
            return null;
        }
        if (TryGetCustomBgFilePath(gameId, out string? path))
        {
            return path;
        }
        path = GetBgFilePath(AppConfig.GetBg(gameId.GameBiz));
        return File.Exists(path) ? path : null;
    }

        public async Task<List<GameBackground>> GetGameBackgroundsAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        GameBackgroundInfo backgroundInfo = await _gameCatalogService.GetGameBackgroundAsync(gameId, cancellationToken);
        List<GameBackground> backgrounds = backgroundInfo?.Backgrounds?.ToList() ?? [];
        if (TryGetCustomBgFilePath(gameId, out string? path))
        {
            backgrounds.Add(GameBackground.FromCustomFile(path));
        }
        return backgrounds;
    }

    public async Task<GameBackground?> GetSuggestedGameBackgroundAsync(GameId gameId, CancellationToken cancellationToken = default)
    {
        if (TryGetCustomBgFilePath(gameId, out string? file))
        {
            return GameBackground.FromCustomFile(file);
        }
        List<GameBackground> backgrounds = await GetGameBackgroundsAsync(gameId, cancellationToken);
        return GameBackgroundPreference.Select(backgrounds, AppConfig.GetBg(gameId.GameBiz),
            AppConfig.GetGameBackgroundIds(gameId.GameBiz), AppConfig.GetVideoBackgroundPaused(gameId.GameBiz),
            AppConfig.GetSelectedBackgroundId(gameId.GameBiz));
    }

    public async Task<string> GetBackgroundFileAsync(string url, CancellationToken cancellationToken = default)
    {
        string name = Path.GetFileName(new Uri(url).AbsolutePath);
        string key = $"background_file_{name}";
        await _downloadLock.WaitAsync(cancellationToken);
        try
        {
            string file = GetBgFilePath(AppConfig.GetValue<string>(null, key) ?? name);
            if (File.Exists(file)) return file;

            var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);
            Directory.CreateDirectory(BackgroundFolder);
            byte[] hash = MD5.HashData(bytes);
            foreach (string cached in Directory.EnumerateFiles(BackgroundFolder))
            {
                if (!Path.GetExtension(cached).Equals(Path.GetExtension(name), StringComparison.OrdinalIgnoreCase)
                    || new FileInfo(cached).Length != bytes.Length) continue;
                using var stream = File.Open(cached, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                byte[] cachedHash = await MD5.HashDataAsync(stream, cancellationToken);
                if (hash.SequenceEqual(cachedHash))
                {
                    AppConfig.SetValue(Path.GetFileName(cached), key);
                    return cached;
                }
            }

            file = GetBgFilePath(name);
            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                File.Move(temporary, file, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            AppConfig.SetValue(name, key);
            return file;
        }
        finally { _downloadLock.Release(); }
    }

        public static string? GetFallbackBackgroundImage(GameId gameId)
    {
        string? bg = GetBgFilePath(AppConfig.GetBg(gameId.GameBiz));
        if (!File.Exists(bg)) return null;
        return bg;
    }

        public async Task<string?> ChangeCustomBackgroundFileAsync(XamlRoot xamlRoot)
    {
        string? file = await PickBackgroundFileAsync(xamlRoot);
        if (file is null)
        {
            return null;
        }
        await CheckBackgroundFileAvailableAsync(file);
        string bg = BackgroundFolder;
        Directory.CreateDirectory(bg);
        string name = Path.GetFileName(file);
        string path = Path.Combine(bg, name);
        if (path != file)
        {
            File.Copy(file, path, true);
        }
        return name;
    }

        public static async Task<string?> ChangeCustomBackgroundFileAsync(StorageFile file)
    {
        string bg = BackgroundFolder;
        if (Path.GetDirectoryName(file.Path) != bg)
        {
            if (FileIsSupportedVideo(file.Name))
            {
                using var source = MediaSource.CreateFromStorageFile(file);
                await source.OpenAsync();
            }
            else
            {
                using var fs = await file.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(fs);
            }
            {
                Directory.CreateDirectory(bg);
                string path = Path.Combine(bg, file.Name);
                using var dest = File.OpenWrite(path);
                using var stream = await file.OpenReadAsync();
                await stream.AsStream().CopyToAsync(dest);
            }
        }
        return file.Name;
    }

        private async Task<string?> PickBackgroundFileAsync(XamlRoot xamlRoot)
    {
        var filter = new (string, string)[]
            {
                ("Image", ".bmp"),
                ("Image", ".jpg"),
                ("Image", ".png"),
                ("Image", ".webp"),
                ("Image", ".avif"),
                ("Image", ".jxl"),
                ("Video", ".mp4"),
                ("Video", ".mkv"),
                ("Video", ".webm"),
            };
        return await FileDialogHelper.PickSingleFileAsync(xamlRoot, filter);
    }

        private static async Task CheckBackgroundFileAvailableAsync(string file)
    {
        if (FileIsSupportedVideo(file))
        {

            using var source = MediaSource.CreateFromUri(new Uri(file));
            await source.OpenAsync();
        }
        else
        {

            using var fs = File.OpenRead(file);
            var decoder = await BitmapDecoder.CreateAsync(fs.AsRandomAccessStream());
        }
    }

}
