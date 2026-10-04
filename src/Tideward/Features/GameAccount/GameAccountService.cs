using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Tideward.Core;
using Tideward.Core.Games;
using Tideward.Features.Database;
using Tideward.Features.GameLauncher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tideward.Features.GameAccount;

public class GameAccountService(ILogger<GameAccountService> logger)
{
    public List<GameAccount> GetGameAccounts(GameBiz biz)
    {
        using var db = DatabaseService.CreateConnection();
        var accounts = db.Query<GameAccount>("SELECT * FROM GameAccount WHERE GameBiz=@biz", new { biz }).ToList();
        foreach (var item in accounts) item.IsSaved = true;
        try
        {
            if (!IsRunning() && GetCurrentAccount(biz) is { } current)
            {
                if (accounts.FirstOrDefault(x => x.SHA256 == current.SHA256) is { } saved)
                {
                    accounts.Remove(saved);
                    current = saved;
                }
                accounts.Insert(0, current);
            }
        }
        catch (Exception)
        {
            logger.LogWarning("Cannot read current SDK account for {region}", biz);
        }
        return accounts;
    }

    private static KuroSdkAccountStore CreateStore(GameBiz biz) => new(RequireRoot(biz), biz, IsRunning);

    private static GameAccount? GetCurrentAccount(GameBiz biz)
    {
        var snapshot = CreateStore(biz).ReadCurrent();
        if (snapshot is null) return null;
        return new GameAccount
        {
            GameBiz = biz, SHA256 = snapshot.Fingerprint, Name = "-", Uid = GetRecentUid(biz),
            Value = Encrypt(snapshot),
        };
    }

    public IEnumerable<long> GetSuggestionUids(GameBiz biz)
    {
        using var db = DatabaseService.CreateConnection();
        var uids = db.Query<long>("SELECT DISTINCT Uid FROM GameAccount WHERE GameBiz=@biz AND Uid>0", new { biz }).ToList();
        return uids.Distinct().Order();
    }

    public void SaveGameAccount(GameAccount account, GameAccount? replace = null)
    {
        if (!account.IsSaved)
        {
            var current = GetCurrentAccount(account.GameBiz) ?? throw new InvalidOperationException("尚无当前账号缓存，请先登录游戏并退出。");
            if (current.SHA256 != account.SHA256) throw new InvalidOperationException("当前登录账号已变化，请重新打开账号列表后保存。");
            account.Value = current.Value;
        }
        if (account.Value.Length == 0) throw new InvalidOperationException("账号缓存为空。");
        account.Name ??= "";
        using var db = DatabaseService.CreateConnection();
        using var transaction = db.BeginTransaction();
        db.Execute("INSERT OR REPLACE INTO GameAccount (SHA256,GameBiz,Uid,Name,Value,Time) VALUES (@SHA256,@GameBiz,@Uid,@Name,@Value,@Time)", account, transaction);
        if (replace is not null && replace.SHA256 != account.SHA256 && replace.GameBiz == account.GameBiz)
            db.Execute("DELETE FROM GameAccount WHERE SHA256=@SHA256 AND GameBiz=@GameBiz", replace, transaction);
        transaction.Commit();
        account.IsSaved = true;
    }

    public void DeleteGameAccount(GameAccount account)
    {
        using var db = DatabaseService.CreateConnection();
        db.Execute("DELETE FROM GameAccount WHERE SHA256=@SHA256 AND GameBiz=@GameBiz", account);
    }

    public void ChangeGameAccount(GameAccount account)
    {
        byte[] value = ProtectedData.Unprotect(account.Value, null, DataProtectionScope.CurrentUser);
        using var json = JsonDocument.Parse(value);
        if (json.RootElement.TryGetProperty("ProductId", out _))
        {
            var snapshot = JsonSerializer.Deserialize(value, KuroAccountJsonContext.Default.KuroSdkAccountSnapshot)
                ?? throw new InvalidDataException("账号缓存无效。");
            var store = CreateStore(account.GameBiz);
            store.Restore(snapshot);
        }
        else
        {
            // Retain compatibility with snapshots saved by earlier Tideward versions.
            var snapshot = JsonSerializer.Deserialize(value, KuroAccountJsonContext.Default.KuroAccountSnapshot)
                ?? throw new InvalidDataException("账号快照无效。");
            string root = RequireRoot(account.GameBiz);
            snapshot.Restore(root, account.GameBiz, IsRunning);
        }
        logger.LogInformation("Restored local game account for {region}", account.GameBiz);
    }

    private static byte[] Encrypt(KuroSdkAccountSnapshot snapshot) => ProtectedData.Protect(
        JsonSerializer.SerializeToUtf8Bytes(snapshot, KuroAccountJsonContext.Default.KuroSdkAccountSnapshot), null, DataProtectionScope.CurrentUser);

    private static long GetRecentUid(GameBiz biz)
    {
        string path = Path.Combine(RequireRoot(biz), "Client", "Saved", "LocalStorage", "LocalStorage.db");
        if (!File.Exists(path)) return 0;
        try
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            db.Open();
            return long.TryParse(db.QueryFirstOrDefault<string>("SELECT value FROM LocalStorage WHERE key='RecentlyLoginUID'"), out long uid) ? uid : 0;
        }
        catch { return 0; }
    }

    private static string RequireRoot(GameBiz region) => GameLauncherService.GetGameInstallPath(region)
        ?? throw new InvalidOperationException("请先设置游戏安装目录。");

    public static bool IsRunning()
        => WutheringWavesRuntime.IsRunning() || WutheringWavesRuntime.IsProcessRunning("KRSDKExternal");
}
