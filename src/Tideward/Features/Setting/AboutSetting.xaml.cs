using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Tideward.Features.Update;
using Tideward.Frameworks;
using System;
using System.Threading.Tasks;

namespace Tideward.Features.Setting;

public sealed partial class AboutSetting : PageBase
{

    private readonly ILogger<AboutSetting> _logger = AppConfig.GetLogger<AboutSetting>();

    public AboutSetting()
    {
        this.InitializeComponent();
    }

        public bool EnablePreviewRelease
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnablePreviewRelease = value;
            }
        }
    } = AppConfig.EnablePreviewRelease;

        public string? LatestVersion { get; set => SetProperty(ref field, value); }

        public string? UpdateErrorText { get; set => SetProperty(ref field, value); }

        [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        try
        {
            LatestVersion = null;
            UpdateErrorText = null;
            var release = await AppConfig.GetService<UpdateService>().GetLatestVersionAsync();
            _ = NuGetVersion.TryParse(AppConfig.AppVersion, out var currentVersion);
            _ = NuGetVersion.TryParse(release.Version, out var newVersion);
            if (newVersion! > currentVersion!)
            {
                new UpdateWindow { NewVersion = release }.Activate();
            }
            else
            {
                LatestVersion = release.Version;
            }
        }
        catch (Exception ex)
        {
            UpdateErrorText = ex.Message;
            _logger.LogError(ex, "Check update");
        }
    }

}
