using System.Text.RegularExpressions;

namespace Tideward.Core.Games;

public static class KuroLaunchArguments
{
    public static string Build(string? customArguments, bool enableDx11 = false)
    {
        string arguments = customArguments?.Trim() ?? "";

        if (enableDx11 && !Regex.IsMatch(arguments, "(?:^|\\s)\"?-(?:dx|d3d)(?:11|12)\"?(?=\\s|$)", RegexOptions.IgnoreCase))
            arguments = arguments.Length == 0 ? "-dx11" : $"{arguments} -dx11";

        if (Regex.IsMatch(arguments, "(?:^|\\s)\"?-krqlv=(?:\"[^\"]+\"|[^\\s\"]+)\"?(?=\\s|$)", RegexOptions.IgnoreCase))
        {
            return arguments;
        }
        return arguments.Length == 0 ? "-krqlv=hd" : $"-krqlv=hd {arguments}";
    }
}
