using Tideward.Core.Games;
using Tideward.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Tideward.Core.Games.Models;
using Tideward.Features.Games;
using Tideward.Features.ViewHost;
using Tideward.Helpers;
using Tideward.Language;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.System;

namespace Tideward.Features.GameLauncher;

[INotifyPropertyChanged]
public sealed partial class GameBannerAndPost : UserControl
{

    private Microsoft.UI.Dispatching.DispatcherQueueTimer _bannerTimer;

    private readonly ILogger<GameBannerAndPost> _logger = AppConfig.GetLogger<GameBannerAndPost>();

    private readonly GameCatalogService _gameCatalogService = AppConfig.GetService<GameCatalogService>();

    public GameId CurrentGameId { get; set; }

    public GameBannerAndPost()
    {
        this.InitializeComponent();
        this.Loaded += GameBannerAndPost_Loaded;
        this.Unloaded += GameBannerAndPost_Unloaded;
        _bannerTimer = DispatcherQueue.CreateTimer();
        _bannerTimer.Interval = TimeSpan.FromSeconds(5);
        _bannerTimer.IsRepeating = true;
        _bannerTimer.Tick += _bannerTimer_Tick;
    }

    private async void GameBannerAndPost_Loaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(this, OnMainWindowStateChanged);
        WeakReferenceMessenger.Default.Register<GameAnnouncementSettingChangedMessage>(this, OnGameAnnouncementSettingChanged);
        WeakReferenceMessenger.Default.Register<KuroDailyNoteConnectionChangedMessage>(this, OnConnectionChanged);
        WeakReferenceMessenger.Default.Register<GameNoticeWindowClosedMessage>(this, OnGameNoticeWindowClosed);
        await UpdateGameContentAsync();
        await UpdateGameNoticeAlertAsync();
    }

    private void GameBannerAndPost_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _bannerTimer.Stop();
        _bannerTimer.Tick -= _bannerTimer_Tick;
        Banners = null;
        PostGroups = null;
    }

    private void OnMainWindowStateChanged(object _, MainWindowStateChangedMessage message)
    {
        try
        {
            if (message.Activate)
            {
                _bannerTimer.Start();
            }
            else if (message.Hide || message.SessionLock)
            {
                _bannerTimer.Stop();
            }
        }
        catch { }
    }

    private async void OnGameAnnouncementSettingChanged(object _, GameAnnouncementSettingChangedMessage message)
    {
        if (AppConfig.EnableBannerAndPost)
        {
            ShowBannerAndPost = true;
            if (Banners is null && PostGroups is null)
                await UpdateGameContentAsync();
        }
        else
        {
            ShowBannerAndPost = false;
        }
        await UpdateGameNoticeAlertAsync();
    }

    private async void OnGameNoticeWindowClosed(object _, GameNoticeWindowClosedMessage message)
    {
        if (XamlRoot is not null) User32.SetForegroundWindow(XamlRoot.GetWindowHandle());
        await UpdateGameNoticeAlertAsync();
    }

    private async void OnConnectionChanged(object _, KuroDailyNoteConnectionChangedMessage message) => await UpdateGameNoticeAlertAsync();

    public bool IsGameNoticesAlert { get; set => SetProperty(ref field, value); }

    private async Task UpdateGameNoticeAlertAsync()
    {
        if (AppConfig.DisableGameNoticeRedHot || CurrentGameId is null) { IsGameNoticesAlert = false; return; }
        var biz = CurrentGameId.GameBiz;
        try
        {
            var credentials = await KuroDailyNoteService.ReadCredentialsAsync();
            var notices = await new KuroGameNoticeClient().GetAsync(biz, CultureInfo.CurrentUICulture.Name,
                biz == GameBiz.wuwa_cn ? credentials?.RoleId : null);
            var readIds = (AppConfig.GetReadGameNoticeIds(biz) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (CurrentGameId.GameBiz == biz)
                IsGameNoticesAlert = !AppConfig.DisableGameNoticeRedHot && KuroGameNoticeClient.Unread(notices, readIds).Length > 0;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Get public Kuro notice alert"); IsGameNoticesAlert = false; }
    }

    public List<GameBanner>? Banners { get; set => SetProperty(ref field, value); }

    public List<GamePostGroup>? PostGroups { get; set => SetProperty(ref field, value); }

    public bool ShowBannerAndPost
    {
        get => this.Opacity == 1;
        set
        {
            if (value && (Banners?.Count > 0 || PostGroups?.Count > 0))
            {
                _bannerTimer.Start();
                this.Opacity = 1;
                this.IsHitTestVisible = true;
            }
            else
            {
                _bannerTimer.Stop();
                this.Opacity = 0;
                this.IsHitTestVisible = false;
            }
        }
    }

    private async Task UpdateGameContentAsync()
    {
        try
        {
            var content = await _gameCatalogService.GetGameContentAsync(CurrentGameId);
            if (content is null || !AppConfig.EnableBannerAndPost)
            {
                ShowBannerAndPost = false;
                return;
            }
            Banners = content.Banners;
            PostGroups = GamePostGroup.FromGameContent(content);
            ShowBannerAndPost = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get game launcher content ({CurrentGameId})", CurrentGameId);
        }
    }

    private void _bannerTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        try
        {
            if (Banners?.Count > 0)
            {
                FlipView_Banner.SelectedIndex = (FlipView_Banner.SelectedIndex + 1) % Banners.Count;
            }
        }
        catch { }
    }

    private async void Image_Banner_Tapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is GameBanner banner)
            {
                await Launcher.LaunchUriAsync(new Uri(banner.Image.Link));
            }
        }
        catch { }
    }

        private void FlipView_Banner_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var grid = VisualTreeHelper.GetChild(FlipView_Banner, 0);
            if (grid != null)
            {
                var count = VisualTreeHelper.GetChildrenCount(grid);
                if (count > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var child = VisualTreeHelper.GetChild(grid, i);
                        if (child is Button button)
                        {

                            button.IsHitTestVisible = false;
                            button.Opacity = 0;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void Grid_BannerContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _bannerTimer.Stop();
        Border_PipsPager.Visibility = Visibility.Visible;
    }

    private void Grid_BannerContainer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _bannerTimer.Start();
        Border_PipsPager.Visibility = Visibility.Collapsed;
    }

    [RelayCommand]
    private void OpenGameNotices()
    {
        try
        {
            new GameNoticeWindow
            {
                CurrentGameBiz = CurrentGameId.GameBiz,
                ParentWindowHandle = (nint)XamlRoot.ContentIslandEnvironment.AppWindowId.Value,
            }.Activate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open Kuro game notices");
        }
    }

    public static string AddOne(int number)
    {
        return (number + 1).ToString();
    }

}
