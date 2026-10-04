using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Tideward.Core.Games;

public sealed record KuroDeviceIdentity(string DevCode, string Did = "")
{
    public const string UserAgent = "Tideward (Windows; WinUI)";
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string CreateDevCode() => RandomNumberGenerator.GetString(Alphabet, 32);
    public static bool IsValidDevCode(string? value) => value is { Length: 32 } && value.All(char.IsAsciiLetterOrDigit);
    public static bool IsValidDid(string value) => value.Length <= 512 && value.All(c => c is >= '!' and <= '~');

    public void AddCommunityHeaders(HttpRequestHeaders headers)
    {
        if (!IsValidDevCode(DevCode)) throw new ArgumentException("Invalid Kuro device code.");
        headers.Add("devCode", DevCode);
        headers.Add("distinct_id", DevCode);
        headers.TryAddWithoutValidation("User-Agent", UserAgent);
        AddDid(headers);
    }

    public void AddDid(HttpRequestHeaders headers)
    {
        // did is supplied by Kuro's App bridge; it is not the website's dc value.
        if (!IsValidDid(Did)) throw new ArgumentException("Invalid Kuro App device identifier.");
        headers.TryAddWithoutValidation("did", Did);
    }

    public override string ToString() => "Kuro device identity";
}
