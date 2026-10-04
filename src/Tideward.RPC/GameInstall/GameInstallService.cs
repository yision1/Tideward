using Tideward.Core.Games;
using Microsoft.Extensions.Logging;
using Tideward.RPC.Env;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tideward.RPC.GameInstall;

internal class GameInstallService
{

    private readonly ILogger<GameInstallService> _logger;

    private readonly GameInstallHelper _gameInstallHelper;

    private readonly ConcurrentDictionary<GameId, GameInstallContext> _tasks = new();

    public event EventHandler<GameInstallContext>? TaskStateChanged;

    public GameInstallContext? CurrentTask { get; private set; }

    public GameInstallService(ILogger<GameInstallService> logger, GameInstallHelper gameInstallHelper)
    {
        _logger = logger;
        _gameInstallHelper = gameInstallHelper;
        LifecycleManager.ParentProcessExited += LifecycleManager_ParentProcessExited;
    }

    private void LifecycleManager_ParentProcessExited(object? sender, Process e)
    {
        try
        {
            _logger.LogInformation("Parent process {name} ({pid}) exited, stop all game install tasks.", e.ProcessName, e.Id);
            foreach (var item in _tasks)
            {
                item.Value.Cancel(GameInstallState.Stop);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cancel all install task");
        }
    }

    public bool TryGetTask(GameId gameId, [NotNullWhen(true)] out GameInstallContext? context)
    {
        return _tasks.TryGetValue(gameId, out context);
    }

    public GameInstallContextDTO StartOrContinueTask(GameInstallRequest request)
    {
        WutheringWavesCatalog.Require(request.GetGameId());

        if (_tasks.TryGetValue(request.GetGameId(), out GameInstallContext? context))
        {
            if (context.Operation != (GameInstallOperation)request.Operation)
            {

                _logger.LogInformation("The new task operation is different from the previous task, cancel the previous task, GameBiz: {game_biz}, Operation: {operation}", context.GameId.GameBiz, context.Operation);
                context.Cancel(GameInstallState.Stop);
                _tasks.TryRemove(context.GameId, out _);
                context = request.ToTask();
                _tasks.TryAdd(context.GameId, context);
            }
        }
        else
        {
            context = request.ToTask();
            _tasks.TryAdd(context.GameId, context);
        }
        return StartOrContinueTask(context);
    }

    private GameInstallContextDTO StartOrContinueTask(GameInstallContext context)
    {
        if (context.State is GameInstallState.Stop && CurrentTask != null && CurrentTask != context)
        {

            _logger.LogInformation("Queueing GameInstallTask, GameBiz: {game_biz}, Operation: {operation}", context.GameId.GameBiz, context.Operation);
            context.State = GameInstallState.Queueing;
            return GameInstallContextDTO.FromTask(context);
        }
        if (CurrentTask != null && CurrentTask != context)
        {
            CurrentTask.Cancel(GameInstallState.Queueing);
            TaskStateChanged?.Invoke(this, CurrentTask);
        }
        CurrentTask = context;
        if (context.State is GameInstallState.Finish)
        {
            ChangeToAnotherTask(context);
            return GameInstallContextDTO.FromTask(context);
        }
        else if (context.State is GameInstallState.Waiting or GameInstallState.Downloading or GameInstallState.Decompressing or GameInstallState.Merging or GameInstallState.Verifying)
        {
            return GameInstallContextDTO.FromTask(context);
        }
        context.State = GameInstallState.Waiting;
        context.ErrorMessage = null;
        _ = PrepareGameInstallTaskAsync(context, context.CancellationToken);
        return GameInstallContextDTO.FromTask(context);
    }

    public GameInstallContextDTO PauseTask(GameInstallRequest request)
    {
        if (_tasks.TryGetValue(request.GetGameId(), out GameInstallContext? context))
        {
            context.Cancel(GameInstallState.Paused);
        }
        else
        {
            context = request.ToTask();
            context.State = GameInstallState.Stop;
        }
        return GameInstallContextDTO.FromTask(context);
    }

    public GameInstallContextDTO StopTask(GameInstallRequest request)
    {
        if (_tasks.TryRemove(request.GetGameId(), out GameInstallContext? context))
        {
            context.Cancel(GameInstallState.Stop);
            context.State = GameInstallState.Stop;
        }
        else
        {
            context = request.ToTask();
            context.State = GameInstallState.Stop;
        }
        return GameInstallContextDTO.FromTask(context);
    }

    public async Task PrepareGameInstallTaskAsync(GameInstallContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("""
                Start game install task: 
                Operation: {operation}
                GameId: {gameId} {gameBiz}
                InstallPath: {installPath}
                AudioLanguage: {audioLanguage}
                HardLinkPath: {hardLinkPath}
                """, context.Operation, context.GameId.Id, context.GameId.GameBiz, context.InstallPath, context.AudioLanguage, context.HardLinkPath);
            await ExecuteKuroTaskAsync(context, cancellationToken);
            context.State = GameInstallState.Finish;
            _logger.LogInformation("GameInstallTask Finished, GameBiz: {game_biz}, Operation: {operation}", context.GameId.GameBiz, context.Operation);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "PrepareGameInstallTaskAsync");
            context.State = GameInstallState.Error;
            context.ErrorMessage = ex.InnerException.Message;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("GameInstallTask canceled, GameBiz: {game_biz}, Operation: {operation}, CancelState: {state}", context.GameId.GameBiz, context.Operation, context.CancelState);
            context.State = context.CancelState;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PrepareGameInstallTaskAsync");
            context.State = GameInstallState.Error;
            context.ErrorMessage = ex.Message;
        }
        ChangeToAnotherTask(context);
    }

    private void ChangeToAnotherTask(GameInstallContext context)
    {
        if (context.State is GameInstallState.Stop or GameInstallState.Finish)
        {
            _tasks.TryRemove(context.GameId, out _);
        }
        TaskStateChanged?.Invoke(this, context);
        if (_tasks.Values.FirstOrDefault(x => x.State is not GameInstallState.Paused and not GameInstallState.Error && x != context) is GameInstallContext anotherTask)
        {
            CurrentTask = anotherTask;
            StartOrContinueTask(anotherTask);
        }
        else
        {
            CurrentTask = null;
        }
    }

    private async Task ExecuteKuroTaskAsync(GameInstallContext context, CancellationToken token)
    {
        if (WutheringWavesRuntime.IsRunning())
            throw new IOException("请先退出鸣潮再安装、更新或修复。");
        using var http = new System.Net.Http.HttpClient(KuroDistribution.CreateHttpHandler());
        var distribution = new KuroDistribution(http);
        var release = await distribution.GetReleaseAsync(context.GameId.GameBiz, token);
        var full = await distribution.GetManifestAsync(release, release.Config, token);
        var local = KuroDistribution.ReadLocalVersion(context.InstallPath, context.GameId.GameBiz);
        var patchPackage = context.Operation == GameInstallOperation.Update
            ? release.Config.PatchConfig.FirstOrDefault(x => x.Version == local?.Version) : null;
        var patch = patchPackage is null ? null : await distribution.GetManifestAsync(release, patchPackage, token);
        var progress = new InlineProgress(value =>
        {
            context.State = value.Phase switch { "Checking" => GameInstallState.Verifying,
                "Patching" or "Committing" => GameInstallState.Merging, _ => GameInstallState.Downloading };
            context.Progress_Percent = value.Total == 0 ? 0 : (double)value.Finished / value.Total * 100;
            if (value.Phase == "Downloading")
            {
                context.Progress_DownloadTotalBytes = value.Total;
                context.Progress_DownloadFinishBytes = value.Finished;

            }
        });
        await new KuroInstaller(http, KuroPatch.ApplyAsync, _gameInstallHelper.ThrottleAsync, count => Interlocked.Add(ref context.networkDownloadBytes, count)).InstallAsync(context.InstallPath, context.GameId.GameBiz,
            release, full, patchPackage, patch, context.Operation == GameInstallOperation.Predownload, progress, token);
    }

    private sealed class InlineProgress(Action<KuroInstallProgress> report) : IProgress<KuroInstallProgress>
    {
        public void Report(KuroInstallProgress value) => report(value);
    }
}
