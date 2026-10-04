using Tideward.Core.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tideward.Core;
using Tideward.Core.Games.Models;
using Tideward.Features.Background;
using Tideward.Features.GameInstall;
using Tideward.Features.GameSelector;
using Tideward.Features.Games;
using Tideward.Helpers;
using Tideward.RPC.GameInstall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

#pragma warning disable MVVMTK0034 // Direct field reference to [ObservableProperty] backing field
#pragma warning disable MVVMTK0045 // Using [ObservableProperty] on fields is not AOT compatible for WinRT

namespace Tideward.Features.GameLauncher;

[INotifyPropertyChanged]
public sealed partial class GameLauncherSettingDialog : ContentDialog
{

    private readonly ILogger<GameLauncherSettingDialog> _logger = AppConfig.GetLogger<GameLauncherSettingDialog>();

    private readonly GameCatalogService _gameCatalogService = AppConfig.GetService<GameCatalogService>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();

    private readonly GamePackageService _gamePackageService = AppConfig.GetService<GamePackageService>();

    private readonly GameInstallService _gameInstallService = AppConfig.GetService<GameInstallService>();

    private readonly BackgroundService _backgroundService = AppConfig.GetService<BackgroundService>();

    public GameLauncherSettingDialog()
    {
        this.InitializeComponent();
        this.Loaded += GameLauncherSettingDialog_Loaded;
        this.Unloaded += GameLauncherSettingDialog_Unloaded;
    }

    public GameId CurrentGameId { get; set; }

    public GameBiz CurrentGameBiz { get; set; }

    private void FlipView_Settings_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var grid = VisualTreeHelper.GetChild(FlipView_Settings, 0);
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
                        else if (child is ScrollViewer scrollViewer)
                        {
                            scrollViewer.PointerWheelChanged += (_, e) => e.Handled = true;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        try
        {
            if (args.InvokedItemContainer?.Tag is string index && int.TryParse(index, out int target))
            {
                int steps = target - FlipView_Settings.SelectedIndex;
                if (steps > 0)
                {
                    for (int i = 0; i < steps; i++)
                    {
                        FlipView_Settings.SelectedIndex++;
                    }
                }
                else
                {
                    for (int i = 0; i < -steps; i++)
                    {
                        FlipView_Settings.SelectedIndex--;
                    }
                }
            }
        }
        catch { }
    }

    private async void GameLauncherSettingDialog_Loaded(object sender, RoutedEventArgs e)
    {
        CurrentGameBiz = CurrentGameId?.GameBiz ?? GameBiz.None;
        CheckCanRepairGame();
        await InitializeBasicInfoAsync();
        InitializeStartArgument();
        InitializeCustomBg();
        await InitializeGamePackagesAsync();
    }

    private void GameLauncherSettingDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        LatestPackageGroups = null!;
        PreInstallPackageGroups = null!;
        FlipView_Settings.Items.Clear();
    }

    [RelayCommand]
    private void Close()
    {
        this.Hide();
    }

    #region 基本信息

    private bool? _hasAudioPackages;

    public bool CanRepairGame { get; set => SetProperty(ref field, value); } = true;

    public GameBizIcon CurrentGameBizIcon { get; set => SetProperty(ref field, value); }

        public string? InstallPath { get; set => SetProperty(ref field, value); }

        public string? GameSize { get; set => SetProperty(ref field, value); }

        public bool UninstallAndRepairEnabled { get; set => SetProperty(ref field, value); }

        public bool EnableBannerAndPost
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableBannerAndPost = value;
                WeakReferenceMessenger.Default.Send(new GameAnnouncementSettingChangedMessage());
            }
        }
    } = AppConfig.EnableBannerAndPost;

        public bool DisableGameNoticeRedHot
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.DisableGameNoticeRedHot = value;
                WeakReferenceMessenger.Default.Send(new GameAnnouncementSettingChangedMessage());
            }
        }
    } = AppConfig.DisableGameNoticeRedHot;

        public bool StartGameWithCMD
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.StartGameWithCMD = value;
            }
        }
    } = AppConfig.StartGameWithCMD;

    private async Task InitializeBasicInfoAsync()
    {
        try
        {
            if (CurrentGameId.GameBiz.IsKnown())
            {
                CurrentGameBizIcon = new GameBizIcon(CurrentGameId.GameBiz);
            }
            else
            {
                var info = await _gameCatalogService.GetGameInfoAsync(CurrentGameId);
                CurrentGameBizIcon = new GameBizIcon(info);
            }
            InstallPath = GameLauncherService.GetGameInstallPath(CurrentGameId, out bool storageRemoved);
            GameSize = GetSize(InstallPath);
            if (await _gameLauncherService.GetGameProcessAsync(CurrentGameId) is null)
            {
                UninstallAndRepairEnabled = InstallPath != null && !storageRemoved;
            }
            else
            {
                UninstallAndRepairEnabled = false;
            }
            await InitializeAudioLanguageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InitializeBasicInfoAsync ({biz})", CurrentGameBiz);
        }
    }

    private static string? GetSize(string? path)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }
        var size = new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        var gb = (double)size / (1 << 30);
        return $"{gb:F2}GB";
    }

    private async Task InitializeAudioLanguageAsync()
    {
        try
        {
            GameConfig? config = await _gameCatalogService.GetGameConfigAsync(CurrentGameId);
            if (config is not null)
            {
                if (!string.IsNullOrWhiteSpace(config.AudioPackageScanDir))
                {
                    _hasAudioPackages = true;
                    Segmented_SelectLanguage.SelectedItems.Clear();
                    AudioLanguage audioLanguage = await _gamePackageService.GetAudioLanguageAsync(CurrentGameId, InstallPath);
                    if (audioLanguage.HasFlag(AudioLanguage.Chinese))
                    {
                        Segmented_SelectLanguage.SelectedItems.Add(SegmentedItem_Chinese);
                    }
                    if (audioLanguage.HasFlag(AudioLanguage.English))
                    {
                        Segmented_SelectLanguage.SelectedItems.Add(SegmentedItem_English);
                    }
                    if (audioLanguage.HasFlag(AudioLanguage.Japanese))
                    {
                        Segmented_SelectLanguage.SelectedItems.Add(SegmentedItem_Japanese);
                    }
                    if (audioLanguage.HasFlag(AudioLanguage.Korean))
                    {
                        Segmented_SelectLanguage.SelectedItems.Add(SegmentedItem_Korean);
                    }
                }
                else
                {
                    _hasAudioPackages = false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InitializeAudioLanguageAsync ({biz})", CurrentGameBiz);
        }
    }

        [RelayCommand]
    private async Task OpenInstalGameFolderAsync()
    {
        try
        {
            if (Directory.Exists(InstallPath))
            {
                await Launcher.LaunchUriAsync(new Uri(InstallPath));
            }
        }
        catch { }
    }

        [RelayCommand]
    private async Task DeleteGameInstllPathAsync()
    {
        try
        {
            GameLauncherService.ChangeGameInstallPath(CurrentGameId, null);
            WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
            await InitializeBasicInfoAsync();
            await TryStopGameInstallTaskAsync();
        }
        catch { }
    }

        [RelayCommand]
    private async Task LocateGameAsync()
    {
        try
        {
            string? previousInstallPath = InstallPath;
            string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                if (!await _gameLauncherService.IsGameExeExistsAsync(CurrentGameId, folder))
                {
                    InAppToast.MainWindow?.Warning(null, "所选文件夹中没有 Wuthering Waves.exe，请选择鸣潮游戏目录。", 0);
                    return;
                }
                if (DriveHelper.GetDriveType(folder) is DriveType.Network && !new Uri(folder).IsUnc)
                {
                    TextBlock_NetworkDriveWarning.Visibility = Visibility.Visible;
                }
                else
                {
                    TextBlock_NetworkDriveWarning.Visibility = Visibility.Collapsed;
                    GameLauncherService.ChangeGameInstallPath(CurrentGameId, folder);
                    await InitializeBasicInfoAsync();
                    WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
                    if (previousInstallPath != folder)
                    {
                        await TryStopGameInstallTaskAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Locate game failed {GameBiz}", CurrentGameBiz);
            InAppToast.MainWindow?.Error(ex);
        }
    }

        private void CheckCanRepairGame()
    {
        var task = _gameInstallService.GetGameInstallTask(CurrentGameId);
        Button_RepairGame.IsEnabled = task is null || task.State is GameInstallState.Stop or GameInstallState.Finish or GameInstallState.Error;
    }

        [RelayCommand]
    private async Task RepairGameAsync()
    {
        if (_hasAudioPackages is null)
        {
            return;
        }
        if (_hasAudioPackages.Value && Button_StartRepairing.Visibility is Visibility.Collapsed)
        {
            Segmented_SelectLanguage.Visibility = Visibility.Visible;
            Button_StartRepairing.Visibility = Visibility.Visible;
            Button_RepairGame.Visibility = Visibility.Collapsed;
        }
        else
        {
            await RepairGameInternalAsync();
        }
    }

    [RelayCommand]
    private async Task RepairGameInternalAsync()
    {
        try
        {
            if (!Directory.Exists(InstallPath))
            {
                return;
            }
            AudioLanguage audio = AudioLanguage.None;
            foreach (SegmentedItem item in Segmented_SelectLanguage.SelectedItems.Cast<SegmentedItem>())
            {
                audio |= item.Tag switch
                {
                    "zh-cn" => AudioLanguage.Chinese,
                    "en-us" => AudioLanguage.English,
                    "ja-jp" => AudioLanguage.Japanese,
                    "ko-kr" => AudioLanguage.Korean,
                    _ => AudioLanguage.None,
                };
            }
            GameInstallContext? task = await _gameInstallService.StartRepairAsync(CurrentGameId, InstallPath, audio);
            if (task is not null && task.State is not GameInstallState.Stop and not GameInstallState.Error)
            {
                WeakReferenceMessenger.Default.Send(new GameInstallTaskStartedMessage(task));
                Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repair game internal {GameBiz}", CurrentGameBiz);
            InAppToast.MainWindow?.Error(ex);
        }
    }

    public string? UninstallError { get; set => SetProperty(ref field, value); }

    [RelayCommand]
    private void ShowUninstallGameWarning()
    {
        try
        {
            if (Directory.Exists(InstallPath))
            {
                string installPath = Path.GetFullPath(InstallPath);
                if (Path.GetPathRoot(InstallPath) == InstallPath)
                {
                    // 不能删除驱动器根目录
                    UninstallError = Lang.GameLauncherSettingDialog_CannotDeleteTheDriveRootDirectory;
                    return;
                }
                if (Directory.Exists(AppConfig.UserDataFolder))
                {
                    string userDataFolder = Path.GetFullPath(AppConfig.UserDataFolder);
                    if (userDataFolder.StartsWith(installPath))
                    {

                        UninstallError = Lang.GameLauncherSettingDialog_UninstallGameUserDataFolderWarning;
                        return;
                    }
                }
                string baseFolder = AppContext.BaseDirectory.TrimEnd('/', '\\');
                if (baseFolder.StartsWith(installPath))
                {

                    UninstallError = Lang.GameLauncherSettingDialog_UninstallGameTidewardProgramFolderWarning;
                    return;
                }
                Grid_UninstallWarning.Visibility = Visibility.Visible;
            }
            else
            {
                _ = InitializeBasicInfoAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show uninstall game warning {GameBiz}", CurrentGameBiz);
        }
    }

    [RelayCommand]
    private async Task UninstallGameAsync()
    {
        try
        {
            UninstallError = null;
            if (await _gameLauncherService.GetGameProcessAsync(CurrentGameId) is not null)
            {
                UninstallError = Lang.LauncherPage_GameIsRunning;
                await InitializeBasicInfoAsync();
                return;
            }
            if (Directory.Exists(InstallPath))
            {
                if (await _gameInstallService.StartUninstallAsync(CurrentGameId, InstallPath))
                {
                    _logger.LogInformation("""
                        Uninstall game finished:
                        GameId: {gameId} {gameBiz}
                        InstallPath: {installPath}
                        """, CurrentGameId.Id, CurrentGameId.GameBiz, InstallPath);
                    Grid_UninstallWarning.Visibility = Visibility.Collapsed;
                    WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
                    CheckCanRepairGame();
                    await InitializeBasicInfoAsync();
                }
            }
            else
            {
                await InitializeBasicInfoAsync();
            }
        }
        catch (Exception ex)
        {
            UninstallError = ex.Message;
            _logger.LogError(ex, "Uninstall game failed {GameBiz}", CurrentGameBiz);
        }
    }

    private void Segmented_SelectLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is Segmented segmented)
        {
            CanRepairGame = segmented.SelectedItems.Count > 0;
        }
    }

    private async Task TryStopGameInstallTaskAsync()
    {
        try
        {
            if (_gameInstallService.GetGameInstallTask(CurrentGameId) is GameInstallContext task)
            {
                if (task.State is not GameInstallState.Stop and not GameInstallState.Finish)
                {
                    await _gameInstallService.StopTaskAsync(task);
                    await Task.Delay(1000);
                    CheckCanRepairGame();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Try stop game install task {GameBiz}", CurrentGameBiz);
        }
    }

    #endregion

    #region 启动参数

        [ObservableProperty]
    public string? _StartGameArgument;
    partial void OnStartGameArgumentChanged(string? value)
    {
        AppConfig.SetStartArgument(CurrentGameBiz, value);
    }

        public int StartGameAction
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.StartGameAction = (StartGameAction)value;
            }
        }
    } = Math.Clamp((int)AppConfig.StartGameAction, 0, 2);

        [ObservableProperty]
    public bool _EnableThirdPartyTool;
    partial void OnEnableThirdPartyToolChanged(bool value)
    {
        AppConfig.SetEnableThirdPartyTool(CurrentGameBiz, value);
    }

        [ObservableProperty]
    public string? _ThirdPartyToolPath;
    partial void OnThirdPartyToolPathChanged(string? value)
    {
        try
        {
            GameLauncherService.SetThirdPartyToolPath(CurrentGameId, value);
        }
        catch { }
    }

    private void InitializeStartArgument()
    {
        _StartGameArgument = AppConfig.GetStartArgument(CurrentGameBiz);
        _EnableThirdPartyTool = AppConfig.GetEnableThirdPartyTool(CurrentGameBiz);
        _ThirdPartyToolPath = GameLauncherService.GetThirdPartyToolPath(CurrentGameId);
        OnPropertyChanged(nameof(StartGameArgument));
        OnPropertyChanged(nameof(EnableThirdPartyTool));
        OnPropertyChanged(nameof(ThirdPartyToolPath));
    }

        [RelayCommand]
    private async Task ChangeThirdPartyPathAsync()
    {
        try
        {
            var file = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot);
            if (File.Exists(file))
            {
                ThirdPartyToolPath = file;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change third party tool path ({biz})", CurrentGameBiz);
        }
    }

        [RelayCommand]
    private async Task OpenThirdPartyToolFolderAsync()
    {
        try
        {
            if (File.Exists(ThirdPartyToolPath))
            {
                var folder = Path.GetDirectoryName(ThirdPartyToolPath);
                var file = await StorageFile.GetFileFromPathAsync(ThirdPartyToolPath);
                var option = new FolderLauncherOptions();
                option.ItemsToSelect.Add(file);
                await Launcher.LaunchFolderPathAsync(folder, option);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open third party tool folder {folder}", ThirdPartyToolPath);
        }
    }

        [RelayCommand]
    private void DeleteThirdPartyToolPath()
    {
        ThirdPartyToolPath = null;
    }

    #endregion

    #region 自定义背景

        [ObservableProperty]
    public bool _EnableCustomBg;
    partial void OnEnableCustomBgChanged(bool value)
    {
        AppConfig.SetEnableCustomBg(CurrentGameBiz, value);
        WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
    }

        public string? CustomBg { get; set => SetProperty(ref field, value); }

        public string? ChangeBgError { get; set => SetProperty(ref field, value); }

    private void InitializeCustomBg()
    {
        _EnableCustomBg = AppConfig.GetEnableCustomBg(CurrentGameBiz);
        CustomBg = AppConfig.GetCustomBg(CurrentGameBiz);
        OnPropertyChanged(nameof(EnableCustomBg));
    }

        [RelayCommand]
    private async Task ChangeCustomBgAsync()
    {
        try
        {
            ChangeBgError = null;
            string? name = await _backgroundService.ChangeCustomBackgroundFileAsync(this.XamlRoot);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }
            CustomBg = name;
            AppConfig.SetCustomBg(CurrentGameBiz, name);
            WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
        }
        catch (COMException ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_CannotDecodeFile;
            _logger.LogError(ex, "Change custom background failed");
        }
        catch (Exception ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_AnUnknownErrorOccurredPleaseCheckTheLogs;
            _logger.LogError(ex, "Change custom background failed");
        }
    }

        [RelayCommand]
    private async Task OpenCustomBgAsync()
    {
        try
        {
            string? path = BackgroundService.GetBgFilePath(CustomBg);
            if (File.Exists(path))
            {
                await Launcher.LaunchUriAsync(new Uri(path));
            }
        }
        catch { }
    }

        [RelayCommand]
    private void DeleteCustomBg()
    {
        CustomBg = null;
        AppConfig.SetCustomBg(CurrentGameBiz, null);
        WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
    }

        public int VideoBgVolume
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(VideoBgVolumeButtonIcon));
                WeakReferenceMessenger.Default.Send(new VideoBgVolumeChangedMessage(value));
                AppConfig.VideoBgVolume = value;
            }
        }
    } = AppConfig.VideoBgVolume;

        public string VideoBgVolumeButtonIcon => VideoBgVolume switch
    {
        > 66 => "\uE995",
        > 33 => "\uE994",
        > 1 => "\uE993",
        _ => "\uE992",
    };

    private int notMuteVolume = 100;

        [RelayCommand]
    private void Mute()
    {
        if (VideoBgVolume > 0)
        {
            notMuteVolume = VideoBgVolume;
            VideoBgVolume = 0;
        }
        else
        {
            VideoBgVolume = notMuteVolume;
        }
    }

        private void Grid_BackgroundDragIn_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

        private async void Grid_BackgroundDragIn_Drop(object sender, DragEventArgs e)
    {
        ChangeBgError = null;
        var defer = e.GetDeferral();
        try
        {
            if ((await e.DataView.GetStorageItemsAsync()).FirstOrDefault() is StorageFile file)
            {
                string? name = await BackgroundService.ChangeCustomBackgroundFileAsync(file);
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }
                CustomBg = name;
                AppConfig.SetCustomBg(CurrentGameBiz, name);
                if (EnableCustomBg)
                {
                    WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
                }
                else
                {
                    EnableCustomBg = true;
                }
            }
        }
        catch (COMException ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_CannotDecodeFile;
            _logger.LogError(ex, "Change custom background failed");
        }
        catch (Exception ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_AnUnknownErrorOccurredPleaseCheckTheLogs;
            _logger.LogError(ex, "Change custom background failed");
        }
        defer.Complete();
    }

    #endregion

    #region 游戏包体

        public string LatestVersion { get; set => SetProperty(ref field, value); }

        public List<PackageGroup> LatestPackageGroups { get; set => SetProperty(ref field, value); }

        public string PreInstallVersion { get; set => SetProperty(ref field, value); }

        public List<PackageGroup> PreInstallPackageGroups { get; set => SetProperty(ref field, value); }

    public string PackageStatus { get; set => SetProperty(ref field, value); }

    private async Task InitializeGamePackagesAsync()
    {
        bool chinese = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
        PackageStatus = chinese ? "正在读取官方资源列表…" : "Reading the official resource list…";

        try
        {
            var localVersion = await _gameLauncherService.GetLocalGameVersionAsync(CurrentGameId);
            var groups = await _gameCatalogService.GetGamePackageFileGroupsAsync(CurrentGameId, sourceVersion: localVersion?.ToString());
            LatestVersion = groups.First(group => group.IsFull).Version;
            LatestPackageGroups = groups.Select(group => new PackageGroup
            {
                Name = group.IsFull ? Lang.GameResourcePage_FullPackages : $"{Lang.GameResourcePage_DiffPackages}  {group.Version}",
                Source = group,
            }).ToList();
            PackageStatus = "";
        }
        catch (Exception ex)
        {
            PackageStatus = chinese ? "读取包体信息失败，请稍后重新打开游戏设置。" : "Could not read package information. Reopen game settings to retry.";
            _logger.LogError(ex, "Get game resource failed, gameBiz: {gameBiz}", CurrentGameBiz);
        }
    }

    private async void PackageGroup_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (sender.DataContext is PackageGroup group)
            await LoadPackageGroupAsync(group);
    }

    private async Task<bool> LoadPackageGroupAsync(PackageGroup group)
    {
        try
        {
            await group.LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get package group failed: {group}", group.Name);
            return false;
        }
    }

    private async void Button_CopyUrl_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            if (sender is Button button)
            {
                if (button.DataContext is PackageGroup group)
                {
                    if (!await LoadPackageGroupAsync(group)) return;
                    if (group.Items is not null)
                    {
                        var sb = new StringBuilder();
                        foreach (var item in group.Items)
                        {
                            if (!string.IsNullOrEmpty(item.Url))
                            {
                                sb.AppendLine(item.Url);
                            }
                        }
                        string url = sb.ToString().TrimEnd();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            ClipboardHelper.SetText(url);
                            await CopySuccessAsync(button);
                        }
                    }
                }
                if (button.DataContext is PackageItem package)
                {
                    if (!string.IsNullOrEmpty(package.Url))
                    {
                        ClipboardHelper.SetText(package.Url);
                        await CopySuccessAsync(button);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy url failed");
        }
    }

    private async Task CopySuccessAsync(Button button)
    {
        try
        {
            button.IsEnabled = false;
            if (button.Content is FontIcon icon)
            {

                icon.Glyph = "\uF78C";
                await Task.Delay(1000);
            }
        }
        finally
        {
            button.IsEnabled = true;
            if (button.Content is FontIcon icon)
            {

                icon.Glyph = "\uE71B";
            }
        }
    }

    public class PackageGroup : ObservableObject
    {
        public string Name { get; set; }
        public KuroPackageFileGroup Source { get; set; }
        public string PackageName => Source.IsFull ? $"鸣潮 {Source.TargetVersion}" : $"{Source.Version} → {Source.TargetVersion}";
        public string PackageSizeString => PackageItem.GetSizeString(Source.Size);
        public List<PackageItem>? Items { get; private set => SetProperty(ref field, value); }
        public string Status { get; private set => SetProperty(ref field, value); } = "";
        private Task? loading;

        public async Task LoadAsync()
        {
            if (Items is not null) return;
            loading ??= ReadFilesAsync();
            try { await loading; }
            finally { loading = null; }
        }

        private async Task ReadFilesAsync()
        {
            bool chinese = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
            Status = chinese ? "正在读取官方资源列表…" : "Reading the official resource list…";
            try
            {
                Items = (await Source.GetFilesAsync()).Select(item => new PackageItem
                {
                    FileName = Path.GetFileName(item.Url), Url = item.Url, Md5 = item.MD5,
                    PackageSize = item.Size, DecompressSize = item.DecompressedSize,
                }).ToList();
                Status = "";
            }
            catch
            {
                Status = chinese ? "读取包体信息失败，请重新展开以重试。" : "Could not read package information. Expand again to retry.";
                throw;
            }
        }
    }

    public class PackageItem
    {
        public string FileName { get; set; }

        public string Url { get; set; }

        public string Md5 { get; set; }

        public long PackageSize { get; set; }

        public long DecompressSize { get; set; }

        public string PackageSizeString => GetSizeString(PackageSize);

        public string DecompressSizeString => GetSizeString(DecompressSize);

        public static string GetSizeString(long size)
        {
            const double KB = 1 << 10;
            const double MB = 1 << 20;
            const double GB = 1 << 30;
            if (size >= GB)
            {
                return $"{size / GB:F2} GB";
            }
            else if (size >= MB)
            {
                return $"{size / MB:F2} MB";
            }
            else
            {
                return $"{size / KB:F2} KB";
            }
        }
    }

    #endregion

    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }

}
