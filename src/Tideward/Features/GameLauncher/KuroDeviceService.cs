using Tideward.Core.Games;

namespace Tideward.Features.GameLauncher;

internal static class KuroDeviceService
{
    private static string? deviceCode;
    private static readonly object deviceCodeLock = new();

    private static string GetDeviceCode()
    {
        lock (deviceCodeLock)
        {
            string? code = deviceCode ?? AppConfig.KuroDeviceCode;
            if (!KuroDeviceIdentity.IsValidDevCode(code))
            {
                code = KuroDeviceIdentity.CreateDevCode();
                AppConfig.KuroDeviceCode = code;
            }
            return deviceCode = code!;
        }
    }

    internal static void UpdateDeviceCode()
    {
        lock (deviceCodeLock)
        {
            string code = KuroDeviceIdentity.CreateDevCode();
            AppConfig.KuroDeviceCode = code;
            deviceCode = code;
        }
    }

    internal static KuroDeviceIdentity GetIdentity(KuroDailyNoteCredentials? credentials = null)
        => new(GetDeviceCode(), credentials?.Did ?? "");
}
