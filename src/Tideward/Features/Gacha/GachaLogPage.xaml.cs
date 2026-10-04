using Tideward.Core.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Tideward.Core;
using Tideward.Core.Gacha;
using Tideward.Features.GameLauncher;
using Tideward.Features.ViewHost;
using Tideward.Frameworks;
using Tideward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.Storage;
using Windows.System;

namespace Tideward.Features.Gacha;

public sealed partial class GachaLogPage : PageBase
{

    private readonly ILogger<GachaLogPage> _logger = AppConfig.GetLogger<GachaLogPage>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();

    private GachaLogService _gachaLogService;

    public GachaLogPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        GachaTypeText = GachaLogService.GetGachaLogText(CurrentGameBiz);
        LegacyFormatName = "JSON";
        LegacyImportText = string.Format(Lang.GachaLogPage_ImportFrom0, LegacyFormatName);
        _gachaLogService = new WutheringWavesGachaService(CurrentGameBiz);
    }

    public string GachaTypeText { get; set => SetProperty(ref field, value); }

        public string LegacyFormatName { get; set => SetProperty(ref field, value); } = "JSON";

        public string LegacyImportText { get; set => SetProperty(ref field, value); } = "";

    public ObservableCollection<long> UidList { get; set => SetProperty(ref field, value); }

    [ObservableProperty]
    public partial long? SelectUid { get; set; }
    partial void OnSelectUidChanged(long? value)
    {
        AppConfig.SetLastUidInGachaLogPage(CurrentGameBiz.Value, value ?? 0);
        UpdateGachaTypeStats(value);
    }

    protected override async void OnLoaded()
    {
        WeakReferenceMessenger.Default.Register<UpdateGachaLogMessage>(this, (s, m) =>
        {
            if (m.GameBiz == CurrentGameBiz)
            {
                User32.SetForegroundWindow((nint)this.XamlRoot.ContentIslandEnvironment.AppWindowId.Value);
                _ = UpdateGachaLogInternalAsync(m.Url);
            }
        });
        Grid_GachaStats.PointerWheelChanged += Grid_GachaStats_PointerWheelChanged;
        Initialize();
        var service = _gachaLogService;
        await WutheringWavesGachaService.LoadIconsAsync();
        if (IsLoaded && ReferenceEquals(service, _gachaLogService))
        {
            if (gachaTypeStats is not null)
                WutheringWavesGachaService.UpdateIcons(gachaTypeStats.SelectMany(x => x.List_5.Concat(x.List_4)));
            if (GachaItemStats is not null) WutheringWavesGachaService.UpdateIcons(GachaItemStats);
        }
    }

    protected override void OnUnloaded()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        Grid_GachaStats.PointerWheelChanged -= Grid_GachaStats_PointerWheelChanged;
        if (DisplayGachaTypeStatsCollection is not null)
        {
            DisplayGachaTypeStatsCollection.Clear();
            DisplayGachaTypeStatsCollection = null!;
        }
        if (GachaItemStats is not null)
        {
            GachaItemStats.Clear();
            GachaItemStats = null;
        }
        ListView_GachaBanners.SelectionChanged -= ListView_GachaBanners_SelectionChanged;
        if (GachaBanners is not null)
        {
            GachaBanners.Clear();
            GachaBanners = null!;
        }
    }

    private void Initialize()
    {
        try
        {
            InitializeGachaBanners();
            SelectUid = null;
            UidList = new(_gachaLogService.GetUids());
            var lastUid = AppConfig.GetLastUidInGachaLogPage(CurrentGameBiz.Value);
            if (UidList.Contains(lastUid))
            {
                SelectUid = lastUid;
            }
            else
            {
                SelectUid = UidList.FirstOrDefault();
            }
            if (UidList.Count == 0)
            {
                StackPanel_Emoji.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize");
        }
    }

    private void Grid_GachaStats_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(Grid_GachaStats).Properties;
        if (properties.IsHorizontalMouseWheel || InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            var delta = properties.MouseWheelDelta;
            ScrollViewer_GachaStats.ChangeView(ScrollViewer_GachaStats.HorizontalOffset - delta, null, null);
            e.Handled = true;
        }
    }

    [RelayCommand]
    private void OpenItemStatsPane()
    {
        SplitView_Content.IsPaneOpen = true;
    }

    #region Gacha Stats

    public List<GachaBanner> GachaBanners { get; set => SetProperty(ref field, value); }

    public ObservableCollection<GachaTypeStats> DisplayGachaTypeStatsCollection { get; set => SetProperty(ref field, value); }

    public List<GachaLogItemEx>? GachaItemStats { get; set => SetProperty(ref field, value); }

    private List<GachaTypeStats>? gachaTypeStats;

    private void InitializeGachaBanners()
    {
        GachaBanners = _gachaLogService.QueryGachaTypes.Select(x => new GachaBanner(x)).ToList();
        string? banner = AppConfig.GetDisplayGachaBanners(CurrentGameBiz.Value);
        if (!string.IsNullOrWhiteSpace(banner))
        {
            foreach (var item in banner.Split(','))
            {
                if (int.TryParse(item, out int type))
                {
                    if (GachaBanners.FirstOrDefault(x => x.Value == type) is GachaBanner gachaType)
                    {
                        ListView_GachaBanners.SelectedItems.Add(gachaType);
                    }
                }
            }
        }
        if (ListView_GachaBanners.SelectedItems.Count == 0)
        {
            foreach (var item in GachaBanners)
            {
                ListView_GachaBanners.SelectedItems.Add(item);
            }
        }
        ListView_GachaBanners.SelectionChanged -= ListView_GachaBanners_SelectionChanged;
        ListView_GachaBanners.SelectionChanged += ListView_GachaBanners_SelectionChanged;
    }

    private void ListView_GachaBanners_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            string value = string.Join(',', ListView_GachaBanners.SelectedItems.Cast<GachaBanner>().Select(x => x.Value));
            AppConfig.SetDisplayGachaBanners(CurrentGameBiz.Value, value);
            UpdateDisplayGachaTypeStats();
        }
        catch { }
    }

    private void UpdateGachaTypeStats(long? uid)
    {
        try
        {
            if (uid is null or 0)
            {
                gachaTypeStats = null;
                DisplayGachaTypeStatsCollection = [];
                GachaItemStats = null;
                StackPanel_Emoji.Visibility = Visibility.Visible;
            }
            else
            {
                (gachaTypeStats, GachaItemStats) = _gachaLogService.GetGachaTypeStats(uid.Value);
                UpdateDisplayGachaTypeStats();
                StackPanel_Emoji.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateGachaTypeStats");
        }
    }

    private void UpdateDisplayGachaTypeStats()
    {
        if (gachaTypeStats is null)
        {
            return;
        }
        DisplayGachaTypeStatsCollection ??= [];
        DisplayGachaTypeStatsCollection.Clear();
        var list = ListView_GachaBanners.SelectedItems.Cast<GachaBanner>().ToList();
        if (list.Count == 0)
        {
            list = GachaBanners;
        }
        foreach (var item in list)
        {
            if (gachaTypeStats.FirstOrDefault(x => x.GachaType == item.Value) is GachaTypeStats stats)
            {
                DisplayGachaTypeStatsCollection.Add(stats);
            }
        }
    }

    private void UpdateGachaStatsCardLayout()
    {
        try
        {
            if (ItemsControl_GachaStats != null)
            {
                int count = ItemsControl_GachaStats.Items.Count;
                if (count > 0)
                {
                    double width = (ScrollViewer_GachaStats.ActualWidth - 40 - (count - 1) * 12) / count;
                    width = Math.Clamp(width, 262, double.MaxValue);
                    for (int i = 0; i < count; i++)
                    {
                        if (ItemsControl_GachaStats.ContainerFromIndex(i) is ContentPresenter presenter)
                        {
                            presenter.Width = width;
                        }
                    }
                }
            }

        }
        catch { }
    }

    private void GachaStatsCard_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateGachaStatsCardLayout();
    }

    private void GachaStatsCard_Unloaded(object sender, RoutedEventArgs e)
    {
        UpdateGachaStatsCardLayout();
    }

    #endregion

    #region Get Gacha

    [RelayCommand]
    private async Task UpdateGachaLogAsync(string? param = null)
    {
        try
        {
            string? url = null;
            if (param is "cache")
            {
                if (SelectUid is null or 0)
                {
                    return;
                }
                url = _gachaLogService.GetGachaLogUrlByUid(SelectUid.Value);
                if (string.IsNullOrWhiteSpace(url))
                {

                    InAppToast.MainWindow?.Warning(null, string.Format(Lang.GachaLogPage_CannotFindSavedURLOfUid, SelectUid));
                    return;
                }
            }
            else
            {
                var gameId = CurrentGameId;
                var gameBiz = CurrentGameBiz;
                var service = _gachaLogService;
                var path = GameLauncherService.GetGameInstallPath(gameId);
                if (!Directory.Exists(path))
                {
                    InAppToast.MainWindow?.Warning(null, "请先在首页定位鸣潮游戏文件夹。");
                    return;
                }
                url = await Task.Run(() => service.GetGachaLogUrlFromWebCache(gameBiz, path));
                if (CurrentGameId != gameId) return;
                if (string.IsNullOrWhiteSpace(url))
                {
                    InAppToast.MainWindow?.Warning(null, "未找到唤取记录链接。请在游戏内打开“唤取 → 记录”，等待记录显示后，再点击“更新记录”。");
                    return;
                }
            }
            await UpdateGachaLogInternalAsync(url, param is "all");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update gacha log");
            InAppToast.MainWindow?.Error(ex);
        }
    }

    [RelayCommand]
    private async Task InputUrlAsync()
    {
        try
        {
            var textbox = new TextBox();
            var dialog = new ContentDialog
            {

                Title = Lang.GachaLogPage_InputURL,
                Content = textbox,

                PrimaryButtonText = Lang.Common_Confirm,

                SecondaryButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var url = textbox.Text;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    await UpdateGachaLogInternalAsync(url);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Input url");
            InAppToast.MainWindow?.Error(ex);
        }
    }

    private async Task UpdateGachaLogInternalAsync(string url, bool all = false)
    {
        using var cancelSource = new CancellationTokenSource();
        InfoBar? infoBar = null;
        try
        {
            var uid = await _gachaLogService.GetUidFromGachaLogUrl(url);
            var button = new Button
            {

                Content = Lang.Common_Cancel,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            infoBar = new InfoBar
            {
                Severity = InfoBarSeverity.Informational,
                Background = Application.Current.Resources["CustomAcrylicBrush"] as Brush,
                ActionButton = button,
            };
            button.Click += (_, _) =>
            {
                cancelSource.Cancel();

                infoBar.Message = Lang.GachaLogPage_OperationCanceled;
                infoBar.ActionButton = null;
            };
            InAppToast.MainWindow?.Show(infoBar);
            var progress = new Progress<string>((str) => infoBar.Message = str);
            var newUid = await _gachaLogService.GetGachaLogAsync(url, all, GachaLanguage, progress, cancelSource.Token);
            infoBar.Title = $"Uid {newUid}";
            infoBar.Severity = InfoBarSeverity.Success;
            infoBar.ActionButton = null;
            if (SelectUid == uid)
            {
                UpdateGachaTypeStats(uid);
            }
            else
            {
                if (!UidList.Contains(uid))
                {
                    UidList.Add(uid);
                }
                SelectUid = uid;
            }
        }
        catch (TaskCanceledException) when (!cancelSource.IsCancellationRequested)
        {
            InAppToast.MainWindow?.ShowWithButton(InfoBarSeverity.Warning,
                Lang.GachaLogPage_RequestTimedOut,
                Lang.GachaLogPage_RestartGameAfterDeletingTheCacheFolder,
                Lang.GachaLogPage_DeleteCacheFolder,
                () => _ = DeleteGachaCacheFolderAsync());
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Get gacha log canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Input url");
            InAppToast.MainWindow?.Error(ex);
        }
        finally
        {
            if (infoBar is not null) infoBar.ActionButton = null;
        }
    }

    #endregion

    #region Gacha Setting Panel

    [RelayCommand]
    private async Task DeleteGachaCacheFolderAsync()
    {
        try
        {
            string? installPath = GameLauncherService.GetGameInstallPath(CurrentGameId);
            string? cachePath = string.IsNullOrWhiteSpace(installPath) ? null
                : KuroGachaLogReader.FindWebCacheFolder(CurrentGameBiz, installPath);
            if (cachePath is null)
            {
                InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_CacheFolderNotFound);
                return;
            }
            var folder = await StorageFolder.GetFolderFromPathAsync(cachePath);
            var options = new FolderLauncherOptions();
            options.ItemsToSelect.Add(folder);
            await Launcher.LaunchFolderAsync(await folder.GetParentAsync(), options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open gacha web cache folder");
            InAppToast.MainWindow?.Error(ex);
        }
    }

    public string? GachaLanguage
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.GachaLanguage = value;
            }
        }
    } = AppConfig.GachaLanguage;

    [RelayCommand]
    private async Task ChangeGachaItemNameAsync()
    {
        try
        {
            string language = string.IsNullOrWhiteSpace(GachaLanguage) ? System.Globalization.CultureInfo.CurrentUICulture.Name : GachaLanguage;
            var result = await _gachaLogService.ChangeGachaItemNameAsync(language);
            InAppToast.MainWindow?.Success(null, string.Format(Lang.GachaLogPage_0GachaItemsHaveBeenChangedToLanguage1, result.Count, result.Language), 5000);
            UpdateGachaTypeStats(SelectUid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change gacha item name");
            InAppToast.MainWindow?.Error(ex);
        }
    }

    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        try
        {
            if (SelectUid is null or 0)
            {
                return;
            }
            var url = _gachaLogService.GetGachaLogUrlByUid(SelectUid.Value);
            if (!string.IsNullOrWhiteSpace(url))
            {
                ClipboardHelper.SetText(url);
                FontIcon_CopyUrl.Glyph = "\uE8FB";
                await Task.Delay(1000);
                FontIcon_CopyUrl.Glyph = "\uE8C8";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy url");
        }
    }

    [RelayCommand]
    private async Task DeleteUidAsync()
    {
        try
        {
            var uid = SelectUid;
            if (uid is null or 0)
            {
                return;
            }
            var dialog = new ContentDialog
            {

                Title = Lang.Common_Warning,

                Content = string.Format(Lang.GachaLogPage_DeleteGachaRecordsWarning, uid),

                PrimaryButtonText = Lang.Common_Delete,

                SecondaryButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var count = _gachaLogService.DeleteUid(uid.Value);

                InAppToast.MainWindow?.Success(null, string.Format(Lang.GachaLogPage_DeletedGachaRecordsOfUid, count, uid));
                Initialize();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete uid");
            InAppToast.MainWindow?.Error(ex);
        }
    }

    [RelayCommand]
    private async Task DeleteUidByTimeAsync()
    {
        try
        {
            var dialog = new DeleteGachaLogDialog
            {
                CurrentGameBiz = this.CurrentGameBiz,
                DefaultUid = this.SelectUid,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (dialog.Deleted)
            {
                UpdateGachaTypeStats(dialog.SelectUid);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete uid");
        }
    }

    #endregion

    #region Import & Export

    [RelayCommand]
    private async Task ExportGachaLogAsync(string format)
    {
        try
        {
            if (SelectUid is null or 0)
            {
                return;
            }
            long uid = SelectUid.Value;

            var ext = format switch
            {
                "excel" => "xlsx",
                "json" => "json",
                _ => "json"
            };
            var suggestName = $"Tideward_Export_{CurrentGameBiz.Game}_{uid}_{DateTime.Now:yyyyMMddHHmmss}.{ext}";
            var file = await FileDialogHelper.OpenSaveFileDialogAsync(this.XamlRoot, suggestName, (format is "excel" ? "Excel" : "JSON", $".{ext}"));
            if (file is not null)
            {
                await _gachaLogService.ExportGachaLogAsync(uid, file, format);
                var storageFile = await StorageFile.GetFileFromPathAsync(file);
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(storageFile);
                await Launcher.LaunchFolderAsync(await storageFile.GetParentAsync(), options);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export gacha log");
            InAppToast.MainWindow?.Error(ex);
        }
    }

    [RelayCommand]
    private async Task ImportGachaLogAsync()
    {
        try
        {
            var file = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot, ("JSON", ".json"));
            if (File.Exists(file))
            {
                var uid = _gachaLogService.ImportGachaLog(file);
                if (uid == SelectUid)
                {
                    UpdateGachaTypeStats(uid);
                }
                else if (UidList.Contains(uid))
                {
                    SelectUid = uid;
                }
                else
                {
                    UidList.Add(uid);
                    SelectUid = uid;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import gacha log");
            InAppToast.MainWindow?.Error(ex);
        }
    }

    #endregion

}

