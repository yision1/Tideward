using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Tideward.Core.Games;
using Tideward.Features.GameLauncher;
using Tideward.Frameworks;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;

namespace Tideward.Features.KuroGameRecord;

public sealed partial class KuroBattleChronicleWindow : WindowEx
{
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private bool closed;
    private string? initializationScript;

    internal KuroDailyNoteCredentials? CurrentConnection
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            if (RootGrid.IsLoaded) _ = LoadPageAsync();
        }
    }

    public KuroBattleChronicleWindow()
    {
        InitializeComponent();
        Title = "库街区战绩";
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        RootGrid.RequestedTheme = ShouldAppsUseDarkMode() ? ElementTheme.Dark : ElementTheme.Light;
        AdaptTitleBarButtonColorToActuallTheme();
        SetIcon();
        var workArea = DisplayArea.GetFromWindowId(MainWindowId, DisplayAreaFallback.Nearest).WorkArea;
        int height = (int)(workArea.Height * 0.95);
        int width = (int)(height / 16.0 * 9.0);
        if (width > workArea.Width) { width = (int)(workArea.Width * 0.95); height = (int)(width * 16.0 / 9.0); }
        AppWindow.MoveAndResize(new RectInt32(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height));
        Closed += (_, _) => { closed = true; webview.Close(); };
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        await loadLock.WaitAsync();
        try
        {
            if (closed || CurrentConnection is not { } connection || KuroDailyNoteService.GetSavedRole(connection) is not { } role) return;
            if (webview.CoreWebView2 is null)
            {
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(AppConfig.CacheFolder, "WebView2"), null);
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.IsInPrivateModeEnabled = true;
                await webview.EnsureCoreWebView2Async(environment, options);
                if (closed) return;
                var core = webview.CoreWebView2!;
                core.Settings.UserAgent = KuroRecordWebProtocol.UserAgent;
                core.DocumentTitleChanged += (_, _) => PageTitle.Text = Title = core.DocumentTitle;
                core.NavigationCompleted += (_, args) =>
                {
                    StatusText.Text = "库街区网页加载失败，请检查网络后重新打开战绩。";
                    StatusText.Visibility = args.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
                };
                core.WebMessageReceived += (_, args) =>
                {
                    if (Uri.TryCreate(args.Source, UriKind.Absolute, out var source) && source.GetLeftPart(UriPartial.Authority) == KuroRecordWebProtocol.Origin
                        && source.AbsolutePath.StartsWith("/mcbox/", StringComparison.Ordinal) && args.WebMessageAsJson == "\"close\"") Close();
                };
                core.AddWebResourceRequestedFilter("https://api.kurobbs.com/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, args) =>
                {
                    if (CurrentConnection is { } active) args.Request.Headers.SetHeader("source", active.Source);
                };
            }
            if (closed || connection != CurrentConnection) return;
            var activeCore = webview.CoreWebView2!;
            if (initializationScript is not null) activeCore.RemoveScriptToExecuteOnDocumentCreated(initializationScript);
            string userId = connection.UserId.Length > 0 ? connection.UserId : role.UserId;
            initializationScript = await activeCore.AddScriptToExecuteOnDocumentCreatedAsync(
                KuroRecordWebProtocol.CreateInitializationScript(connection.Token, userId, role, connection.Did));
            if (closed || connection != CurrentConnection) return;
            StatusText.Visibility = Visibility.Collapsed;
            webview.Source = KuroRecordWebProtocol.PageUrl(role);
        }
        catch
        {
            if (!closed)
            {
                StatusText.Text = "无法打开库街区战绩，请检查 WebView2 运行环境。";
                StatusText.Visibility = Visibility.Visible;
            }
        }
        finally { loadLock.Release(); }
    }
}
