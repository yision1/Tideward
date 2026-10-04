using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Tideward.RPC.GameInstall;

internal sealed class GameInstallHelper(ILogger<GameInstallHelper> logger)
{
    private readonly object gate = new();
    private int limit;
    private long next;

    public int SetRateLimiter(int bytesPerSecond)
    {
        lock (gate) { limit = Math.Max(0, bytesPerSecond); next = 0; }
        logger.LogInformation("Set downloading rate limiter: {bytesPerSecond} bytes/s", limit);
        return limit;
    }

    public async Task ThrottleAsync(int bytes, CancellationToken token)
    {
        long due;
        lock (gate)
        {
            if (limit == 0) return;
            next = Math.Max(next, Stopwatch.GetTimestamp()) + (long)((double)bytes / limit * Stopwatch.Frequency);
            due = next;
        }
        double seconds = (double)(due - Stopwatch.GetTimestamp()) / Stopwatch.Frequency;
        if (seconds > 0) await Task.Delay(TimeSpan.FromSeconds(seconds), token);
    }
}
