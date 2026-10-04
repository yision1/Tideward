using Tideward.RPC.GameInstall;

namespace Tideward.Features.GameInstall;

class GameInstallTaskStartedMessage
{
    public GameInstallContext InstallTask { get; init; }

    public GameInstallTaskStartedMessage(GameInstallContext installTask)
    {
        InstallTask = installTask;
    }
}
