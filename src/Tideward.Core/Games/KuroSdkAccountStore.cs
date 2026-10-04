using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tideward.Core.Games;

public sealed record KuroSdkAccountSnapshot(string Region, string ProductId, Dictionary<string, byte[]?> Files)
{
    [JsonIgnore]
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(this, KuroAccountJsonContext.Default.KuroSdkAccountSnapshot)));
}

// KRSDK owns the login cache in Roaming AppData, separately from game settings.
public sealed class KuroSdkAccountStore
{
    public const string UserCacheFile = "KRSDKUserCache.json";
    private static readonly string[] AccountFiles = [UserCacheFile, "KRSDKUserLauncherCache.json"];
    private readonly GameBiz region;
    private readonly string productId;
    private readonly string cacheDirectory;
    private readonly Func<bool> isRunning;

    public KuroSdkAccountStore(string gameRoot, GameBiz region, Func<bool> isRunning, string? roamingDirectory = null)
    {
        this.region = region;
        this.isRunning = isRunning;
        string projectId = region.Value switch
        {
            GameBiz.wuwa_cn => "G152",
            GameBiz.wuwa_global => "G153",
            _ => throw new ArgumentException("未知鸣潮区服。", nameof(region)),
        };
        if (KuroDistribution.ReadLocalVersion(gameRoot, region) is null)
            throw new InvalidOperationException("请先设置当前区服的鸣潮游戏目录。");
        string thirdParty = GameFilePath.Resolve(gameRoot, "Client/Binaries/Win64/ThirdParty");
        var configs = Directory.Exists(thirdParty) ? Directory.EnumerateDirectories(thirdParty, "KrPcSdk*")
            .Select(x => GameFilePath.Resolve(gameRoot, $"Client/Binaries/Win64/ThirdParty/{Path.GetFileName(x)}/KRSDKRes/KRSDKConfig.json"))
            .Where(File.Exists).ToArray() : [];
        if (configs.Length != 1) throw new InvalidOperationException("未找到唯一的鸣潮官方 SDK 配置，当前客户端暂不支持账号切换。");
        string configPath = configs[0];
        using var config = JsonDocument.Parse(File.ReadAllBytes(configPath));
        if (config.RootElement.GetProperty("KR_ProjectId").GetString() != projectId)
            throw new InvalidDataException("鸣潮 SDK 与当前区服不匹配。");
        productId = config.RootElement.GetProperty("KR_ProductId").GetString() ?? "";
        if (productId.Length < 2 || productId[0] != 'A' || !productId[1..].All(char.IsAsciiDigit))
            throw new InvalidDataException("鸣潮 SDK 产品标识无效。");
        cacheDirectory = GameFilePath.Resolve(roamingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), $"KR_{projectId}/{productId}");
    }

    public KuroSdkAccountSnapshot? ReadCurrent()
    {
        EnsureStopped();
        var files = new Dictionary<string, byte[]?>();
        foreach (string file in AccountFiles) files.Add(file, ReadFile(file));
        EnsureStopped();
        if (files[UserCacheFile] is not { Length: > 0 }) return null;
        return new(region.Value, productId, files);
    }

    public void Restore(KuroSdkAccountSnapshot snapshot)
    {
        EnsureStopped();
        if (snapshot.Region != region.Value || snapshot.ProductId != productId)
            throw new InvalidDataException("账号缓存与当前区服或 SDK 产品不匹配。");
        if (snapshot.Files.Count != AccountFiles.Length || AccountFiles.Any(x => !snapshot.Files.ContainsKey(x))
            || snapshot.Files[UserCacheFile] is not { Length: > 0 }
            || snapshot.Files.Values.Any(x => x?.Length > 32 * 1024 * 1024))
            throw new InvalidDataException("鸣潮账号缓存无效。");
        var originals = AccountFiles.ToDictionary(x => x, ReadFile);
        var written = new List<string>();
        Directory.CreateDirectory(cacheDirectory);
        try
        {
            foreach (string file in AccountFiles)
            {
                EnsureStopped();
                WriteFile(file, snapshot.Files[file]);
                written.Add(file);
            }
        }
        catch
        {
            foreach (string file in written.AsEnumerable().Reverse()) WriteFile(file, originals[file]);
            throw;
        }
    }

    private byte[]? ReadFile(string file)
    {
        string path = GameFilePath.Resolve(cacheDirectory, file);
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("鸣潮账号缓存过大。");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void WriteFile(string file, byte[]? bytes)
    {
        string path = GameFilePath.Resolve(cacheDirectory, file);
        if (bytes is null) { File.Delete(path); return; }
        string temp = path + ".tideward-new";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { File.Delete(temp); }
    }

    private void EnsureStopped()
    {
        if (isRunning()) throw new InvalidOperationException("请先退出鸣潮和官方启动器，再保存或切换账号。");
    }
}

[JsonSerializable(typeof(KuroSdkAccountSnapshot))]
[JsonSerializable(typeof(KuroAccountSnapshot))]
public partial class KuroAccountJsonContext : JsonSerializerContext { }
