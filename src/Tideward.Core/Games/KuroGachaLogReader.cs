using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Tideward.Core.Games;

public static class KuroGachaLogReader
{
    private static readonly Regex UrlPattern = new("https://aki-gm-resources(?:-oversea)?\\.aki-game\\.(?:com|net)/aki/gacha/index\\.html#/record\\?[^\\s\"'<>\\\\\\x00-\\x1f]+", RegexOptions.CultureInvariant);
    private static readonly Regex UnicodeEscape = new(@"\\u([0-9a-fA-F]{4})", RegexOptions.CultureInvariant);

    public static string? FindWebCacheFolder(GameBiz region, string gameDirectory)
    {
        _ = KuroDistribution.AppId(region);
        string sdk = region == GameBiz.wuwa_cn ? "KrPcSdk_Mainland" : "KrPcSdk_Global";
        foreach (string relative in new[]
        {
            $"Client/Binaries/Win64/ThirdParty/{sdk}/KRSDKRes/KRSDKWebView/Cache",
            "Client/Saved/webCaches"
        })
        {
            string path = GameFilePath.Resolve(gameDirectory, relative);
            if (Directory.Exists(path)) return path;
        }
        return null;
    }

    public static string? FindLatest(GameBiz region, string gameDirectory)
    {
        _ = KuroDistribution.AppId(region);
        string sdk = region == GameBiz.wuwa_cn ? "KrPcSdk_Mainland" : "KrPcSdk_Global";
        string[] paths =
        [
            Path.Combine(gameDirectory, "Client/Saved/Logs/Client.log"),
            Path.Combine(gameDirectory, $"Client/Binaries/Win64/ThirdParty/{sdk}/KRSDKRes/KRSDKWebView/debug.log"),
        ];
        foreach (var file in paths.Select(x => new FileInfo(x)).Where(x => x.Exists).OrderByDescending(x => x.LastWriteTimeUtc))
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                // Scan backwards in bounded windows, retaining overlap for a URL across a boundary.
                const int blockSize = 1024 * 1024, overlap = 16 * 1024;
                long length = stream.Length;
                for (long end = length; end > 0;)
                {
                    long start = Math.Max(0, end - blockSize);
                    byte[] bytes = new byte[(int)(Math.Min(length, end + overlap) - start)];
                    stream.Position = start;
                    stream.ReadExactly(bytes);
                    string? url = Extract(bytes, region);
                    if (url is not null) return url;
                    end = start;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    private static string? Extract(byte[] bytes, GameBiz region)
    {
        string? url = ExtractText(Encoding.UTF8.GetString(bytes), region)
            ?? ExtractText(Encoding.Unicode.GetString(bytes), region);
        if (url is not null) return url;

        // New Client.log files encode each byte; decode only our in-memory copy.
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] ^= (byte)((bytes[i] & 1) == 1 ? 0xA5 : 0xEF);
        return ExtractText(Encoding.UTF8.GetString(bytes), region)
            ?? ExtractText(Encoding.Unicode.GetString(bytes), region);
    }

    private static string? ExtractText(string text, GameBiz region)
    {
        text = UnicodeEscape.Replace(text, match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
        text = WebUtility.HtmlDecode(text.Replace("\\/", "/"));
        var matches = UrlPattern.Matches(text);
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            string url = matches[i].Value;
            try
            {
                _ = KuroGachaLink.Parse(url, region);
                return url;
            }
            catch (FormatException) { }
        }
        return null;
    }
}
