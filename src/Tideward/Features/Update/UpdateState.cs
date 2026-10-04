namespace Tideward.Features.Update;

public enum UpdateState
{
        Stop = 0,

        Pending = 1,

        Downloading = 2,

        Finish = 3,

        Error = 4,

        NotSupport = 5,
}
