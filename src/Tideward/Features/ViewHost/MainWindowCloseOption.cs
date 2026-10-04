using Tideward.Helpers.Enumeration;

namespace Tideward.Features.ViewHost;

public enum MainWindowCloseOption
{

        [LocalizationKey(nameof(Lang.ExperienceSettingPage_MinimizeToSystemTray))]
    Hide = 1,

        [LocalizationKey(nameof(Lang.ExperienceSettingPage_ExitCompletely))]
    Exit = 2,

}