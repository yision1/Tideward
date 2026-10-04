using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tideward.Features.RPC;
using Tideward.Frameworks;
using Tideward.Helpers;
using Tideward.RPC.GameInstall;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.System;

namespace Tideward.Features.Setting;

public sealed partial class DownloadSetting : PageBase
{

    private readonly ILogger<DownloadSetting> _logger = AppConfig.GetLogger<DownloadSetting>();

    public DownloadSetting()
    {
        this.InitializeComponent();
        InitializeDefaultInstallPath();
    }

    #region 默认安装文件夹

        public string? DefaultInstallPath { get; set => SetProperty(ref field, value); }

        private void InitializeDefaultInstallPath()
    {
        try
        {
            string? path = AppConfig.DefaultGameInstallationPath;
            if (Directory.Exists(path))
            {
                DefaultInstallPath = path;
            }
            else
            {
                AppConfig.DefaultGameInstallationPath = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get default intall path");
        }
    }

        [RelayCommand]
    private async Task ChangeDefaultInstallPathAsync()
    {
        try
        {
            var path = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (Directory.Exists(path))
            {
                DefaultInstallPath = path;
                AppConfig.DefaultGameInstallationPath = path;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change default install path");
        }
    }

        [RelayCommand]
    private async Task OpenDefaultInstallPathAsync()
    {
        try
        {
            if (Directory.Exists(DefaultInstallPath))
            {
                await Launcher.LaunchUriAsync(new Uri(DefaultInstallPath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open default install path");
        }
    }

        [RelayCommand]
    private void DeleteDefaultInstallPath()
    {
        DefaultInstallPath = null;
        AppConfig.DefaultGameInstallationPath = null;
    }

    #endregion

    #region 硬链接

    public bool EnableHardLink
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableHardLink = value;
            }
        }
    } = AppConfig.EnableHardLink;

    #endregion

    #region 下载限速

        public int SpeedLimit
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.SpeedLimitKBPerSecond = value;
                _ = SetRateLimiterAsync(value);
            }
        }
    } = AppConfig.SpeedLimitKBPerSecond;

    private async Task SetRateLimiterAsync(int value)
    {
        try
        {
            if (RpcService.CheckRpcServerRunning())
            {
                var client = RpcService.CreateRpcClient<GameInstaller.GameInstallerClient>();
                int limit = Math.Clamp(value * 1024, 0, int.MaxValue);
                await client.SetRateLimiterAsync(new RateLimiterMessage { BytesPerSecond = limit }, deadline: DateTime.UtcNow.AddSeconds(3));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Set rate limiter");
        }
    }

    #endregion

}
