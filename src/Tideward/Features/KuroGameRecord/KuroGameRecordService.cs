using Tideward.Core.Games;
using Tideward.Features.Database;
using Tideward.Features.GameLauncher;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Tideward.Features.KuroGameRecord;

internal static class KuroGameRecordService
{
    internal static bool SameRecord(KuroRecordData? left, KuroRecordData right)
        => left is not null && JsonSerializer.Serialize(left, KuroGameRecordJsonContext.Default.KuroRecordData)
            == JsonSerializer.Serialize(right, KuroGameRecordJsonContext.Default.KuroRecordData);

    internal static KuroRecordData? GetRecord(KuroDailyNoteCredentials credentials, string section, string month = "")
        => Get(credentials, section, KuroGameRecordJsonContext.Default.KuroRecordData, month);

    internal static List<KuroRecordPeriod>? GetPeriods(KuroDailyNoteCredentials credentials)
        => Get(credentials, "calendar-periods", KuroGameRecordJsonContext.Default.ListKuroRecordPeriod);

    internal static void SaveRecord(KuroDailyNoteCredentials credentials, string section, KuroRecordData data, string month = "")
        => Save(credentials, section, data, KuroGameRecordJsonContext.Default.KuroRecordData, month);

    internal static void SavePeriods(KuroDailyNoteCredentials credentials, IReadOnlyList<KuroRecordPeriod> periods)
        => Save(credentials, "calendar-periods", new List<KuroRecordPeriod>(periods), KuroGameRecordJsonContext.Default.ListKuroRecordPeriod);

    // Like Starward's game records, keep successful responses in the local database.
    // Login tokens are never part of the persisted key or value.
    private static string? CacheKey(KuroDailyNoteCredentials credentials, string section, string month)
    {
        string userId = credentials.UserId.Length > 0 ? credentials.UserId : credentials.Role?.UserId ?? "";
        if (userId.Length == 0 || credentials.RoleId.Length == 0) return null;
        return $"KuroGameRecord_{credentials.Source}_{userId}_{credentials.RoleId}_{credentials.ServerId}_{section}_{month}";
    }

    private static T? Get<T>(KuroDailyNoteCredentials credentials, string section, JsonTypeInfo<T> typeInfo, string month = "") where T : class
    {
        if (CacheKey(credentials, section, month) is not { } key) return null;
        string? json = DatabaseService.GetValue<string>(key, out _);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize(json, typeInfo); }
        catch (JsonException) { return null; }
    }

    private static void Save<T>(KuroDailyNoteCredentials credentials, string section, T data, JsonTypeInfo<T> typeInfo, string month = "")
    {
        if (CacheKey(credentials, section, month) is { } key)
            DatabaseService.SetValue(key, JsonSerializer.Serialize(data, typeInfo));
    }
}

[JsonSerializable(typeof(KuroRecordData))]
[JsonSerializable(typeof(List<KuroRecordPeriod>))]
internal partial class KuroGameRecordJsonContext : JsonSerializerContext;
