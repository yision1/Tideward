using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Tideward.Core;
using Tideward.Core.Games;
using Tideward.Frameworks;
using Tideward.Helpers;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.Graphics;
using Windows.System;

namespace Tideward.Features.GameLauncher;

public sealed partial class GameNoticeWindow : WindowEx
{
    public GameBiz CurrentGameBiz { get; set; }
    public nint ParentWindowHandle { get; set; }
    private string[] unread = [];
    private Uri? noticeUri;
    private bool publicReader;

    public GameNoticeWindow()
    {
        InitializeComponent();
        Title = "Tideward";
        SystemBackdrop = new TransparentBackdrop();
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.IconShowOptions = IconShowOptions.ShowIconAndSystemMenu;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.SetDragRectangles([new RectInt32(0, 0, 0, 0)]);
        AdaptTitleBarButtonColorToActuallTheme();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }
        User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_STYLE,
            User32.GetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_STYLE) & ~(nint)User32.WindowStyles.WS_DLGFRAME);
        Closed += (_, _) => { webview.Close(); WeakReferenceMessenger.Default.Send(new GameNoticeWindowClosedMessage()); };
    }

    public new void Activate()
    {
        var parent = AppWindow.GetFromWindowId(new Microsoft.UI.WindowId((ulong)ParentWindowHandle));
        if (parent is null) { Close(); return; }
        int edge = (int)(8 * UIScale);
        AppWindow.MoveAndResize(new RectInt32(parent.Position.X + edge, parent.Position.Y, parent.Size.Width - 2 * edge, parent.Size.Height - edge));
        User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_HWNDPARENT, ParentWindowHandle);
        base.Activate();
    }

    protected override nint InputSiteSubclassProc(HWND hwnd, uint message, nint wParam, nint lParam, nuint id, nint data)
    {
        if (message == (uint)User32.WindowMessage.WM_KEYDOWN && (VirtualKey)wParam == VirtualKey.Escape)
        { Close(); return 0; }
        return base.InputSiteSubclassProc(hwnd, message, wParam, lParam, id, data);
    }

    private async void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            KuroDailyNoteCredentials? credentials = null;
            try { credentials = await KuroDailyNoteService.ReadCredentialsAsync(); }
            catch { /* Public notices remain available if a saved community connection cannot be read. */ }
            string? role = CurrentGameBiz == GameBiz.wuwa_cn ? credentials?.RoleId : null;
            string culture = CultureInfo.CurrentUICulture.Name;
            noticeUri = KuroGameNoticeClient.PageUri(CurrentGameBiz, culture, role);
            try
            {
                var notices = await new KuroGameNoticeClient().GetAsync(CurrentGameBiz, culture, role);
                unread = KuroGameNoticeClient.Unread(notices, (AppConfig.GetReadGameNoticeIds(CurrentGameBiz) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
            }
            catch {  }
            await webview.EnsureCoreWebView2Async();
            webview.CoreWebView2.WebMessageReceived += OnWebMessage;
            webview.CoreWebView2.DOMContentLoaded += (_, _) =>
            {
                webview.Visibility = Visibility.Visible;
                LoadingRing.IsActive = false;
            };
            if (string.IsNullOrEmpty(role))
            {
                var publicNotices = await new KuroGameNoticeClient().GetAsync(CurrentGameBiz, culture, null, publicView: true);
                publicReader = true;
                webview.NavigateToString(KuroGameNoticeHtml.Create(publicNotices));
                return;
            }
            webview.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                LoadingRing.IsActive = false;
                if (!args.IsSuccess)
                {
                    ErrorText.Text = "无法加载官方游戏公告，请检查网络后重试。";
                    ErrorPanel.Visibility = Visibility.Visible;
                }
            };
            await webview.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                (() => {
                  // Kuro's PC bridge calls functions on window and registers its reply
                  // handlers there. Let the official SDK own window.jsBridge.
                  window.__tidewardNoticeReply = (name, data) => window[name]?.(data);
                  window.__tidewardNoticeReady = data => {
                    if (window.__tidewardNoticeDelivered) return;
                    window.__tidewardNoticeDelivered = true;
                    if (typeof window.redPoint === 'function') window.redPoint(data);
                    else Object.defineProperty(window, 'redPoint', {
                      configurable: true,
                      set(fn) {
                        Object.defineProperty(window, 'redPoint', { value: fn, writable: true, configurable: true });
                        fn(data);
                      }
                    });
                  };
                  ['close_webview', 'new_openPage', 'loading_success', 'get_redPoint', 'save_redPoint'].forEach(name => {
                    window[name] = data => chrome.webview.postMessage({ name, data });
                  });
                  window.addEventListener('keydown', e => { if (e.key === 'Escape') window.close_webview(); });
                })();
                """);
            webview.Source = noticeUri;
        }
        catch (Exception ex)
        {
            LoadingRing.IsActive = false;
            ErrorText.Text = ex is NotSupportedException ? ex.Message : "无法初始化游戏公告窗口，请检查 WebView2 运行环境。";
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private async void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            if (noticeUri is null || !Uri.TryCreate(args.Source, UriKind.Absolute, out var origin)
                || (publicReader ? args.Source != "about:blank" : origin.Host != noticeUri.Host)) return;
            using var doc = JsonDocument.Parse(args.WebMessageAsJson);
            var root = doc.RootElement;
            string? name = root.GetProperty("name").GetString();
            if (name == "close_webview") Close();

            else if (name == "loading_success")
                await webview.CoreWebView2.ExecuteScriptAsync($"window.__tidewardNoticeReady({JsonSerializer.Serialize(KuroGameNoticeReadState.EncodeUnreadIds(unread))});");
            else if (name == "save_redPoint" && root.TryGetProperty("data", out var data))
            {
                var state = KuroGameNoticeReadState.Apply(
                    (AppConfig.GetReadGameNoticeIds(CurrentGameBiz) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries), unread, data);
                AppConfig.SetReadGameNoticeIds(CurrentGameBiz, string.Join(',', state.ReadIds));
                unread = state.UnreadIds;
            }
            else if (name == "new_openPage" && root.TryGetProperty("data", out var linkData))
            {
                if (Uri.TryCreate(linkData.GetProperty("url").GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                    await Launcher.LaunchUriAsync(uri);
            }
        }
        catch {  }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private new void Close()
    {
        AppWindow.Hide();
        base.Close();
    }
}
