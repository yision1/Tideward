using Tideward.Core.Games;
using Dapper;
using Tideward.Features.Database;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tideward.Features.GameLauncher;

internal sealed record KuroDailyNoteCredentials(string Token, string RoleId, string Source = "android", string ServerId = KuroDailyNoteClient.ChinaServer, string UserId = "", string UserName = "", KuroDailyNoteRole? Role = null, string Did = "");

internal static class KuroDailyNoteService
{
    internal static readonly KuroDailyNoteClient Client = new();
    private static KuroDailyNote? cached;
    private static DateTime refreshed;
    private const string CredentialsKey = nameof(KuroDailyNoteCredentials);

    internal static Task<KuroDailyNoteCredentials?> ReadCredentialsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var con = DatabaseService.CreateConnection();
        string? value = con.QueryFirstOrDefault<string>("SELECT Value FROM KVT WHERE Key = @Key;", new { Key = CredentialsKey });
        if (value is null) return Task.FromResult<KuroDailyNoteCredentials?>(null);
        byte[] encrypted = Convert.FromBase64String(value);
        byte[] bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return Task.FromResult<KuroDailyNoteCredentials?>(JsonSerializer.Deserialize<KuroDailyNoteCredentials>(bytes) ?? throw new InvalidDataException("库街区连接信息无效，请重新登录。")); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal static async Task<KuroDailyNote?> GetAsync(bool force, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var credentials = await ReadCredentialsAsync(cancellationToken);
        if (credentials is null || credentials.RoleId.Length == 0) return null;
        if (!force && cached?.RoleId == credentials.RoleId && cached.ServerId == credentials.ServerId && DateTime.UtcNow - refreshed < TimeSpan.FromMinutes(5)) return cached;
        var note = await ReadNoteAsync(credentials, cancellationToken);
        cached = note; refreshed = DateTime.UtcNow;
        return note;
    }

    internal static async Task<KuroDailyNote> ConnectAsync(KuroDailyNoteCredentials credentials)
    {
        var note = await ReadNoteAsync(credentials);
        var role = GetSavedRole(credentials)! with { Name = note.Name, HeadIcon = note.HeadIcon, Level = note.Level, Server = note.Server };
        credentials = credentials with { Role = role };
        await SaveCredentialsAsync(credentials);
        cached = note; refreshed = DateTime.UtcNow;
        return note;
    }

    internal static KuroDailyNoteRole? GetSavedRole(KuroDailyNoteCredentials? credentials)
    {
        if (credentials is null || credentials.RoleId.Length == 0) return null;
        if (credentials.Role is { } role && role.RoleId == credentials.RoleId && role.ServerId == credentials.ServerId) return role;

        return new(credentials.RoleId, "鸣潮角色", "国服", true, credentials.ServerId, UserId: credentials.UserId);
    }

    internal static async Task<KuroDailyNoteCredentials> SaveRoleAsync(KuroDailyNoteCredentials credentials, KuroDailyNoteRole role)
    {
        if (role.RoleId != credentials.RoleId || role.ServerId != credentials.ServerId) throw new ArgumentException("角色与本地连接不一致。");
        credentials = credentials with { Role = role };
        await SaveCredentialsAsync(credentials);
        return credentials;
    }

    internal static async Task<KuroDailyNoteCredentials> SaveLoginAsync(KuroAppLoginResult login)
    {
        var credentials = new KuroDailyNoteCredentials(login.Token, "", login.Source, UserId: login.UserId, UserName: login.UserName, Did: login.Did);
        await SaveCredentialsAsync(credentials);
        cached = null; refreshed = default;
        return credentials;
    }

    private static Task SaveCredentialsAsync(KuroDailyNoteCredentials credentials)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try
        {
            string encrypted = Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
            using var con = DatabaseService.CreateConnection();
            con.Execute("INSERT OR REPLACE INTO KVT (Key, Value, Time) VALUES (@Key, @Value, @Time);", new { Key = CredentialsKey, Value = encrypted, Time = DateTime.Now });
            return Task.CompletedTask;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static async Task<KuroDailyNote> ReadNoteAsync(KuroDailyNoteCredentials credentials, CancellationToken cancellationToken = default)
    {
        var note = await Client.GetAsync(credentials.Token, credentials.RoleId, cancellationToken, source: credentials.Source, serverId: credentials.ServerId, device: KuroDeviceService.GetIdentity(credentials), refresh: true);
        if (note.HeadIcon.Length > 0 && note.Level.Length > 0) return note;
        try
        {
            var roles = await Client.GetGameRolesAsync(credentials.Token, credentials.Source, cancellationToken, KuroDeviceService.GetIdentity(credentials));
            var role = roles.Find(role => role.RoleId == note.RoleId && role.ServerId == note.ServerId);
            return note with { HeadIcon = role?.HeadIcon is { Length: > 0 } ? role.HeadIcon : note.HeadIcon, Level = role?.Level is { Length: > 0 } ? role.Level : note.Level };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException or TaskCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A missing profile must not hide otherwise valid daily-note data.
            return note;
        }
    }

    internal static void Disconnect()
    {
        using var con = DatabaseService.CreateConnection();
        con.Execute("DELETE FROM KVT WHERE Key = @Key;", new { Key = CredentialsKey });
        cached = null; refreshed = default;
    }
}
