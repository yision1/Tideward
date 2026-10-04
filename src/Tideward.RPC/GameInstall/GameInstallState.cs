namespace Tideward.RPC.GameInstall;

public enum GameInstallState
{

        Stop = 0,

        Waiting = 1,

        Downloading = 2,

        Decompressing = 3,

        Merging = 4,

        Verifying = 5,

        Paused = 6,

        Finish = 7,

        Error = 8,

        Queueing = 9,

}
