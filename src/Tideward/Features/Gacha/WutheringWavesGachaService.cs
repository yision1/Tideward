using Dapper;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;
using Tideward.Core;
using Tideward.Core.Gacha;
using Tideward.Core.Games;
using Tideward.Features.Database;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tideward.Features.Gacha;

internal sealed class WutheringWavesGachaService : GachaLogService
{
    private readonly KuroGachaClient client = new();
    private static readonly object mergeGate = new();
    private static readonly KuroGachaIcons icons = new();
    private static readonly Lazy<Task> iconRefresh = new(RefreshIconsAsync);

    public static Task LoadIconsAsync() => iconRefresh.Value;

    public static void UpdateIcons(IEnumerable<GachaLogItemEx> items)
    {
        foreach (var item in items)
        {
            if (item.ItemId > 0) item.Icon = icons.GetIcon(item.ItemId) ?? "ms-appx:///Assets/Image/Transparent.png";
        }
    }

    private static async Task RefreshIconsAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        await Task.WhenAll(LoadCatalogAsync(true), LoadCatalogAsync(false));

        async Task LoadCatalogAsync(bool characters)
        {
            try
            {
                string kind = characters ? "character" : "weapon";
                string json = await http.GetStringAsync($"https://api.encore.moe/zh-Hans/{kind}");
                icons.Update(json, characters);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
            {
                AppConfig.GetLogger<WutheringWavesGachaService>().LogWarning("Unable to refresh public gacha icon catalog; using bundled index ({ErrorType}).", ex.GetType().Name);
            }
        }
    }
    protected override GameBiz CurrentGameBiz { get; }
    protected override string GachaTableName { get; }
    public override IReadOnlyCollection<IGachaType> QueryGachaTypes => KuroGachaClient.Types;

    public WutheringWavesGachaService(GameBiz region) : base(AppConfig.GetLogger<WutheringWavesGachaService>())
    {
        _ = KuroDistribution.AppId(region);
        CurrentGameBiz = region;
        GachaTableName = region.Value switch
        {
            GameBiz.wuwa_cn => "WuwaCnGachaItem",
            GameBiz.wuwa_bilibili => "WuwaBilibiliGachaItem",
            _ => "WuwaGlobalGachaItem",
        };
        using var db = DatabaseService.CreateConnection();
        db.Execute($"""
            CREATE TABLE IF NOT EXISTS {GachaTableName} (
                Uid INTEGER NOT NULL, Id INTEGER NOT NULL, GachaType INTEGER NOT NULL, Name TEXT,
                ItemType TEXT, RankType INTEGER, Time TEXT, ItemId INTEGER, Count INTEGER, Lang TEXT,
                PRIMARY KEY (Uid, Id));
            """);
    }

    protected override List<GachaLogItemEx> GetGachaLogItemsByQueryType(IEnumerable<GachaLogItemEx> items, IGachaType type)
        => items.Where(x => x.GachaType == type.Value).ToList();

    protected override int GetPityLimit(IGachaType type) => type.Value == 5 ? 50 : 80;

    public override List<GachaLogItemEx> GetGachaLogItemEx(long uid)
    {
        var items = base.GetGachaLogItemEx(uid);
        foreach (var item in items) item.Icon = icons.GetIcon(item.ItemId) ?? "ms-appx:///Assets/Image/Transparent.png";
        return items;
    }

    protected override int InsertGachaLogItems(List<GachaLogItem> items)
    {
        using var db = DatabaseService.CreateConnection(); using var transaction = db.BeginTransaction();
        int count = db.Execute($"""
            INSERT OR REPLACE INTO {GachaTableName} (Uid,Id,GachaType,Name,ItemType,RankType,Time,ItemId,Count,Lang)
            VALUES (@Uid,@Id,@GachaType,@Name,@ItemType,@RankType,@Time,@ItemId,@Count,@Lang)
            """, items, transaction);
        transaction.Commit(); return count;
    }

    public override Task<long> GetUidFromGachaLogUrl(string url)
        => Task.FromResult(KuroGachaLink.Parse(url, CurrentGameBiz).PlayerId);

    public override string? GetGachaLogUrlFromWebCache(GameBiz gameBiz, string path)
        => KuroGachaLogReader.FindLatest(gameBiz, path);

    public override string? GetGachaLogUrlByUid(long uid)
    {
        using var db = DatabaseService.CreateConnection();
        string? protectedUrl = db.QueryFirstOrDefault<string>("SELECT Url FROM GachaLogUrl WHERE GameBiz=@biz AND Uid=@uid", new { biz = CurrentGameBiz, uid });
        if (protectedUrl is null) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedUrl), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }

    public override async Task<long> GetGachaLogAsync(string url, bool all, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var link = KuroGachaLink.Parse(url, CurrentGameBiz);
        if (!string.IsNullOrWhiteSpace(lang)) link = link with { Language = lang };
        var fetched = await client.FetchAsync(link, progress, cancellationToken);
        lock (mergeGate)
        {
            var saved = GetGachaLogItemEx(link.PlayerId).Cast<GachaLogItem>().ToList();
            var merged = KuroGachaClient.Merge(saved, fetched);
            cancellationToken.ThrowIfCancellationRequested();
            InsertGachaLogItems(merged);
            string encrypted = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(url), null, DataProtectionScope.CurrentUser));
            using var db = DatabaseService.CreateConnection();
            db.Execute("INSERT OR REPLACE INTO GachaLogUrl (GameBiz,Uid,Url,Time) VALUES (@biz,@uid,@url,@time)",
                new { biz = CurrentGameBiz, uid = link.PlayerId, url = encrypted, time = DateTime.Now });
            progress?.Report($"获取 {fetched.Count} 条记录，新增 {merged.Count - saved.Count} 条记录");
        }
        return link.PlayerId;
    }

    public override async Task ExportGachaLogAsync(long uid, string file, string format)
    {
        var list = GetGachaLogItemEx(uid);
        if (format == "excel")
            await MiniExcel.SaveAsAsync(file, list.Select(x => new { x.Uid, x.Time, x.Name, x.ItemType, x.RankType, x.GachaType, x.Pity, x.Count }));
        else
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new Archive("Tideward.Wuwa", 1, CurrentGameBiz.Value, uid, list.Cast<GachaLogItem>().ToList()), new JsonSerializerOptions { WriteIndented = true }));
    }

    public override long ImportGachaLog(string file)
    {
        var archive = JsonSerializer.Deserialize<Archive>(File.ReadAllText(file)) ?? throw new InvalidDataException("记录文件为空。");
        if (archive.Format != "Tideward.Wuwa" || archive.Version != 1 || archive.Region != CurrentGameBiz.Value || archive.Uid <= 0
            || archive.Items is null || archive.Items.Any(x => x.Uid != archive.Uid || !QueryGachaTypes.Any(t => t.Value == x.GachaType)
                || x.RankType is < 3 or > 5 || x.ItemId <= 0 || x.Count <= 0 || x.Time == default))
            throw new InvalidDataException("记录格式、区服或玩家 ID 不匹配。");
        lock (mergeGate) InsertGachaLogItems(KuroGachaClient.Merge(GetGachaLogItemEx(archive.Uid), archive.Items));
        return archive.Uid;
    }

    public sealed record Archive(string Format, int Version, string Region, long Uid, List<GachaLogItem> Items);

    public override async Task<(string Language, int Count)> ChangeGachaItemNameAsync(string language)
    {
        string locale = language.Trim().ToLowerInvariant() switch
        {
            "zh-cn" or "zh-hans" or "zh" => "zh-Hans",
            "zh-tw" or "zh-hk" or "zh-hant" => "zh-Hant",
            "en" or "en-us" or "en-gb" => "en",
            "ja" or "ja-jp" => "ja", "ko" or "ko-kr" => "ko",
            "de" or "de-de" => "de", "es" or "es-es" => "es", "fr" or "fr-fr" => "fr",
            _ => throw new NotSupportedException("请输入鸣潮支持的语言代码：zh-CN、zh-TW、en、ja、ko、de、es 或 fr。"),
        };
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var catalogs = await Task.WhenAll(http.GetStringAsync($"https://api.encore.moe/{locale}/character"),
            http.GetStringAsync($"https://api.encore.moe/{locale}/weapon"));
        Dictionary<int, string> names = [];
        for (int i = 0; i < catalogs.Length; i++)
        {
            using var document = JsonDocument.Parse(catalogs[i]);
            foreach (var item in document.RootElement.GetProperty(i == 0 ? "roleList" : "weapons").EnumerateArray())
                if (item.GetProperty("Id").TryGetInt32(out int id) && id > 0
                    && item.GetProperty("Name").GetString() is { Length: > 0 } name) names[id] = name;
        }
        lock (mergeGate)
        {
            using var db = DatabaseService.CreateConnection();
            using var transaction = db.BeginTransaction();
            int count = 0;
            foreach (var (id, name) in names)
                count += db.Execute($"UPDATE {GachaTableName} SET Name=@name, Lang=@locale WHERE ItemId=@id AND (Name IS NULL OR Name<>@name OR Lang IS NULL OR Lang<>@locale)", new { id, name, locale }, transaction);
            transaction.Commit();
            return (locale, count);
        }
    }
}
