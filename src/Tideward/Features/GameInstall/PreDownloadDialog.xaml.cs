using Tideward.Core.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tideward.Core;
using Tideward.Core.Games.Models;
using Tideward.Features.Games;
using Tideward.Helpers;
using Tideward.RPC.GameInstall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Tideward.Features.GameInstall;

[INotifyPropertyChanged]
public sealed partial class PreDownloadDialog : ContentDialog
{

    private const double GB = 1 << 30;

    private readonly ILogger<PreDownloadDialog> _logger = AppConfig.GetLogger<PreDownloadDialog>();

    private readonly GameCatalogService _gameCatalogService = AppConfig.GetService<GameCatalogService>();

    private readonly GamePackageService _gamePackageService = AppConfig.GetService<GamePackageService>();

    private readonly GameInstallService _gameInstallService = AppConfig.GetService<GameInstallService>();

    public PreDownloadDialog()
    {
        this.InitializeComponent();
        this.Loaded += PreDownloadDialog_Loaded;
    }

    public GameId CurrentGameId { get; set; }

    private void PreDownloadDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (CurrentGameId is null)
        {
            _logger.LogWarning("CurrentGameId is null.");
            this.Hide();
            return;
        }
        _ = GetGamePackageAsync();
    }

    private string _installationPath;

    private string _localGameVersion;

    private GamePackage? _gamePackage;

    private AudioLanguage _audioLanguage;

    private async Task GetGamePackageAsync()
    {
        try
        {
            var installPath = GamePackageService.GetGameInstallPath(CurrentGameId);
            var version = await _gamePackageService.GetLocalGameVersionAsync(CurrentGameId, installPath);
            if (installPath is null || version is null)
            {
                TextBlock_PredownloadUnavailable.Visibility = Visibility.Visible;
                return;
            }
            _installationPath = installPath;
            _localGameVersion = version.ToString();
            var package = await _gameCatalogService.GetGamePackageAsync(CurrentGameId);
            if (package.PreDownload.Major is null)
            {
                TextBlock_PredownloadUnavailable.Visibility = Visibility.Visible;
                return;
            }
            _gamePackage = package;
            _audioLanguage = await _gamePackageService.GetAudioLanguageAsync(CurrentGameId);
            await ComputePackageSizeAsync();
            CheckCanPreDownload();
        }
        catch (Exception ex) { _logger.LogError(ex, "Get game package."); }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackageSizeText))]
    public partial long PackageSizeBytes { get; set; }

    public string PackageSizeText => PackageSizeBytes == 0 ? "..." : $"{PackageSizeBytes / GB:F2} GB";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnzipSpaceText))]
    public partial long UnzipSpaceBytes { get; set; }

    public string UnzipSpaceText => UnzipSpaceBytes == 0 ? "..." : $"{UnzipSpaceBytes / GB:F2} GB";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableSpaceText))]
    public partial long AvailableSpaceBytes { get; set; }

    public string AvailableSpaceText => AvailableSpaceBytes == 0 ? "..." : $"{AvailableSpaceBytes / GB:F2} GB";

    private async Task ComputePackageSizeAsync()
    {
        try
        {
            AvailableSpaceBytes = DriveHelper.GetDriveAvailableSpace(_installationPath);
            long size = 0, unzipSize = 0;
            if (_gamePackage is not null)
            {
                if (_gamePackage.PreDownload.Patches.FirstOrDefault(x => x.Version == _localGameVersion) is GamePackageResource patch)
                {
                    size += patch.GamePackages.Sum(x => x.Size);
                    unzipSize += patch.GamePackages.Sum(x => x.DecompressedSize);

                    foreach (var lang in Enum.GetValues<AudioLanguage>())
                    {
                        if (_audioLanguage.HasFlag(lang))
                        {
                            if (patch.AudioPackages.FirstOrDefault(x => x.Language == lang.ToDescription()) is GamePackageFile packageFile)
                            {
                                size += packageFile.Size;
                                unzipSize += packageFile.DecompressedSize;
                            }
                        }
                    }
                }
                else if (_gamePackage.PreDownload.Major is not null)
                {
                    size += _gamePackage.PreDownload.Major.GamePackages.Sum(x => x.Size);
                    unzipSize += _gamePackage.PreDownload.Major.GamePackages.Sum(x => x.DecompressedSize);

                    foreach (var lang in Enum.GetValues<AudioLanguage>())
                    {
                        if (_audioLanguage.HasFlag(lang))
                        {
                            if (_gamePackage.PreDownload.Major.AudioPackages.FirstOrDefault(x => x.Language == lang.ToDescription()) is GamePackageFile packageFile)
                            {
                                size += packageFile.Size;
                                unzipSize += packageFile.DecompressedSize;
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("PreDownloadMajor of ({GameBiz}) is null.", CurrentGameId.GameBiz);
                    TextBlock_PredownloadUnavailable.Visibility = Visibility.Visible;
                }
            }
            PackageSizeBytes = size;
            UnzipSpaceBytes = unzipSize;
            if (AvailableSpaceBytes > 0 && UnzipSpaceBytes > AvailableSpaceBytes)
            {
                TextBlock_AvailableSpace.Foreground = App.Current.Resources["SystemFillColorCautionBrush"] as Brush;
            }
            else
            {
                TextBlock_AvailableSpace.Foreground = App.Current.Resources["TextFillColorSecondaryBrush"] as Brush;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compute package size.");
        }
    }

    private void CheckCanPreDownload()
    {
        try
        {
            if (_gamePackage is not null)
            {
                if (Path.IsPathFullyQualified(_installationPath) && !string.IsNullOrWhiteSpace(_localGameVersion))
                {
                    if (PackageSizeBytes > 0 && UnzipSpaceBytes > 0)
                    {
                        Button_StartPredownload.IsEnabled = true;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check can start predownload.");
        }
        Button_StartPredownload.IsEnabled = false;
    }

    [RelayCommand]
    private async Task StartPredownloadAsync()
    {
        try
        {
            GameInstallContext? task = await _gameInstallService.StartPredownloadAsync(CurrentGameId, _installationPath, _audioLanguage);
            if (task is not null && task.State is not GameInstallState.Stop and not GameInstallState.Error)
            {
                WeakReferenceMessenger.Default.Send(new GameInstallTaskStartedMessage(task));
                Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start predownload.");
        }
    }

    [RelayCommand]
    private void Close()
    {
        this.Hide();
    }

}
