using System;

namespace Tideward.Features.ViewHost;

internal class MainWindowStateChangedMessage
{

    public bool Activate { get; set; }

    public bool Hide { get; set; }

    public bool SessionLock { get; set; }

    public DateTimeOffset CurrentTime { get; set; }

    public DateTimeOffset LastActivatedTime { get; set; }

    public bool IsCrossingHour => CrossingHour(LastActivatedTime.LocalDateTime, CurrentTime.LocalDateTime);

    public bool ElapsedOver(TimeSpan timeSpan) => CurrentTime - LastActivatedTime > timeSpan;

    private static bool CrossingHour(DateTime lastTime, DateTime currentTime)
    {

        DateTime lastHourStart = new DateTime(lastTime.Year, lastTime.Month, lastTime.Day, lastTime.Hour, 0, 0);
        DateTime currentHourStart = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, currentTime.Hour, 0, 0);

        return currentHourStart > lastHourStart;
    }

}
