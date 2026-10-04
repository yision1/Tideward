using System.Text.Json;

namespace Tideward.Core.Games;

public static class KuroRecordWebProtocol
{
    public const string Origin = "https://web-static.kurobbs.com";
    public const string UserAgent = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36 KuroGameBox/2.2.0 Tideward";

    public static Uri PageUrl(KuroDailyNoteRole role) => new($"{Origin}/mcbox/index.html#/mc-role-box?roleId={Uri.EscapeDataString(role.RoleId)}&serverId={Uri.EscapeDataString(role.ServerId)}");

    public static string CreateInitializationScript(string token, string userId, KuroDailyNoteRole role, string did)
    {
        // Initialize the storage keys and bridge used by the official page.
        // Credentials stay out of the URL and are only installed in the mcbox document.
        return $$"""
            (() => {
                if (location.origin !== '{{Origin}}' || !location.pathname.startsWith('/mcbox/')) return;
                const user = { token: {{JsonString(token)}}, userId: {{JsonString(userId)}}, did: {{JsonString(did)}} };
                const role = { roleId: {{JsonString(role.RoleId)}}, serverId: {{JsonString(role.ServerId)}}, roleName: {{JsonString(role.Name)}}, userId: user.userId, gameId: '3' };
                localStorage.setItem('token', user.token);
                localStorage.setItem('userId', user.userId);
                localStorage.setItem('initUserInfo', JSON.stringify(user));
                localStorage.setItem('mc_userInfo', JSON.stringify(role));
                localStorage.setItem('mc_roleId', role.roleId);
                localStorage.setItem('mc_serverId', role.serverId);
                localStorage.setItem('mc_roleName', role.roleName);
                sessionStorage.removeItem('mc-base-params');
                window.WebViewJavascriptBridge = {
                    init() {},
                    registerHandler() {},
                    callHandler(name, data, callback) {
                        if (name === 'finishPage') { window.chrome.webview.postMessage('close'); return; }
                        let result = '';
                        if (name === 'getUserInfo' || name === 'refreshToken' || name === 'refreshTokenV2') result = JSON.stringify(user);
                        else if (name === 'selectRole') result = JSON.stringify(role);
                        if (typeof callback === 'function') callback(result);
                    }
                };
            })();
            """;
    }

    private static string JsonString(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";
}
