using Tideward.Core.JsonConverter;
using System.Text.Json.Serialization;

namespace Tideward.Core.Games;

public class GameId : IEquatable<GameId>
{

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("biz")]
    [JsonConverter(typeof(GameBizJsonConverter))]
    public GameBiz GameBiz { get; set; }

    public static GameId? FromGameBiz(GameBiz gameBiz)
    {
        return WutheringWavesCatalog.Find(gameBiz) is { } game
            ? new GameId { Id = game.Id, GameBiz = game.GameBiz } : null;
    }

    public bool Equals(GameId? other)
    {
        return this.Id == other?.Id;
    }

    public override bool Equals(object? obj)
    {
        return this.Equals(obj as GameId);
    }

    public override int GetHashCode()
    {
        return this.Id.GetHashCode();
    }

    public static bool operator ==(GameId? left, GameId? right)
    {
        return left?.Id == right?.Id;
    }

    public static bool operator !=(GameId? left, GameId? right) => !(left == right);

}
