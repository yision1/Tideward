using System.Text.Json;

namespace Tideward.Core.Games;

public sealed record KuroGameNoticeReadState(string[] ReadIds, string[] UnreadIds)
{

    public static string EncodeUnreadIds(IEnumerable<string> ids) => JsonSerializer.Serialize(ids);

    public static KuroGameNoticeReadState Apply(IEnumerable<string> readIds, IEnumerable<string> unreadIds, JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.String)
        {
            using var decoded = JsonDocument.Parse(payload.GetString()!);
            return Apply(readIds, unreadIds, decoded.RootElement);
        }
        if (payload.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid notice read-state payload.");
        var remaining = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in payload.EnumerateArray())
        {
            string id = item.ValueKind switch
            {
                JsonValueKind.Number => item.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonValueKind.String => item.GetString()!,
                _ => throw new InvalidDataException("Invalid notice ID."),
            };
            if (id.Length == 0 || !id.All(char.IsAsciiDigit)) throw new InvalidDataException("Invalid notice ID.");
            remaining.Add(id);
        }
        var previousUnread = unreadIds.Distinct(StringComparer.Ordinal).ToArray();
        return new(readIds.Concat(previousUnread.Where(id => !remaining.Contains(id))).Distinct(StringComparer.Ordinal).ToArray(),
            previousUnread.Where(remaining.Contains).ToArray());
    }
}
