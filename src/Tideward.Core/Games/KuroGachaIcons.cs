using System.Collections.Concurrent;
using System.Text.Json;

namespace Tideward.Core.Games;

public sealed class KuroGachaIcons
{
    private readonly ConcurrentDictionary<int, string> icons = new();

    public KuroGachaIcons()
    {
        using var stream = typeof(KuroGachaIcons).Assembly.GetManifestResourceStream("Tideward.Core.Games.Data.KuroGachaIcons.json")!;
        var saved = JsonSerializer.Deserialize<Dictionary<int, string>>(stream)!;
        foreach (var item in saved) icons[item.Key] = item.Value;
    }

    public string? GetIcon(int itemId) => icons.GetValueOrDefault(itemId);

    public void Update(string json, bool characters)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.GetProperty(characters ? "roleList" : "weapons").EnumerateArray())
        {
            if (!item.TryGetProperty("Id", out var id) || !id.TryGetInt32(out int value) || value <= 0
                || !item.TryGetProperty(characters ? "RoleHeadIcon" : "Icon", out var icon)
                || icon.ValueKind != JsonValueKind.String
                || !Uri.TryCreate(icon.GetString(), UriKind.Absolute, out var uri)
                || uri.Scheme != "https" || uri.Host != "api.encore.moe" || uri.Port != 443 || uri.UserInfo.Length != 0
                || !uri.AbsolutePath.StartsWith("/resource/Data/", StringComparison.Ordinal)) continue;
            icons[value] = uri.AbsoluteUri;
        }
    }
}
