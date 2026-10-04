using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Tideward.Frameworks;
using Tideward.Core.Games;
using System;
using System.IO;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace Tideward.Features.GameLauncher;

public sealed partial class KuroLoginWindow : WindowEx
{
    private readonly TaskCompletionSource<KuroAppLoginResult?> completion = new();
    private readonly string profile = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Tideward-KuroLogin-" + Guid.NewGuid().ToString("N")));

    public KuroLoginWindow()
    {
        InitializeComponent(); Title = "Tideward - 库街区 App 登录"; SetIcon();
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsMaximizable = false;
        Closed += async (_, _) =>
        {
            completion.TrySetResult(null);
            webview.Close();
            // Release the temporary official-browser session after its browser process exits.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(profile) && Path.GetDirectoryName(profile) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                        && (new DirectoryInfo(profile).Attributes & FileAttributes.ReparsePoint) == 0)
                        Directory.Delete(profile, true);
                    break;
                }
                catch (IOException) { await Task.Delay(1000); }
                catch (UnauthorizedAccessException) { break; }
            }
        };
    }

    public static Task<KuroAppLoginResult?> LoginAsync(XamlRoot root)
    {
        var window = new KuroLoginWindow();
        User32.SetWindowLong(window.WindowHandle, User32.WindowLongFlags.GWL_HWNDPARENT, (nint)root.ContentIslandEnvironment.AppWindowId.Value);
        window.CenterInScreen(1000, 760); window.Activate();
        return window.completion.Task;
    }

    private async void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profile, null);
            await webview.EnsureCoreWebView2Async(environment);
            var device = KuroDeviceService.GetIdentity();
            await webview.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(KuroAppLoginProtocol.CreateDeviceInitializationScript(device));
            webview.CoreWebView2.AddWebResourceRequestedFilter("https://api.kurobbs.com/user/sdkLogin*", CoreWebView2WebResourceContext.All);
            webview.CoreWebView2.WebResourceRequested += (_, args) =>
            {
                if (!KuroAppLoginProtocol.IsLoginRequest(args.Request.Uri, args.Request.Method)) return;
                foreach (var header in KuroAppLoginProtocol.CreateRequestHeaders(device))
                    args.Request.Headers.SetHeader(header.Key, header.Value);
            };
            webview.CoreWebView2.WebResourceResponseReceived += LoginResponseReceived;
            webview.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                StatusText.Text = args.IsSuccess ? "点击官网右上角登录，验证成功后将自动返回。" : "官网加载失败，请检查网络。";
            };
            webview.Source = new Uri("https://www.kurobbs.com/mc/home/9");
        }
        catch { StatusText.Text = "无法打开登录网页，请检查 WebView2 运行环境。"; }
    }

    private async void LoginResponseReceived(CoreWebView2 sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (completion.Task.IsCompleted || !KuroAppLoginProtocol.IsLoginRequest(args.Request.Uri, args.Request.Method)) return;
        try
        {
            if (args.Request.Headers.GetHeader("source") != "android") return;
            if (args.Response.StatusCode != 200) { StatusText.Text = "App 登录请求失败，请检查网络后重试。"; return; }
            using var stream = await args.Response.GetContentAsync();
            using var reader = new StreamReader(stream.AsStreamForRead());
            var result = KuroAppLoginProtocol.ParseResponse(await reader.ReadToEndAsync());
            if (completion.TrySetResult(result)) Close();
        }
        catch (InvalidDataException ex) { if (!completion.Task.IsCompleted) StatusText.Text = ex.Message; }
        catch { if (!completion.Task.IsCompleted) StatusText.Text = "未能读取 App 登录结果，请在官网重新登录。"; }
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => webview.CoreWebView2?.Reload();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
