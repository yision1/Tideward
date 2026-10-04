using Tideward.Core;
using Tideward.Core.Games;
using Tideward.Features.GamepadControl;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Timers;
using Vanara.PInvoke;

namespace Tideward.Features.Overlay;

internal static class RunningGameService
{

    private static readonly Lock _runningGameLock = new();

    private static readonly List<RunningGame> _runningGames = new();

    private static readonly System.Timers.Timer _timer = new(1000);

    private static RunningGame? _latestActiveGame;

    private static readonly User32.WinEventProc hookProc;

    static RunningGameService()
    {
        _timer.Elapsed += CheckGameExit;
        hookProc = new User32.WinEventProc(WinEventProc);
    }

    public static void AddRuninngGame(GameBiz gameBiz, Process process)
    {
        try
        {
            lock (_runningGameLock)
            {
                if (_runningGames.FirstOrDefault(x => x.Pid == process.Id) is not RunningGame runningGame)
                {
                    runningGame = new RunningGame(gameBiz, process);
                    runningGame.WinEventHook = User32.SetWinEventHook(User32.EventConstant.EVENT_SYSTEM_FOREGROUND, User32.EventConstant.EVENT_SYSTEM_FOREGROUND, HINSTANCE.NULL, hookProc, (uint)process.Id, 0, User32.WINEVENT.WINEVENT_OUTOFCONTEXT);
                    _runningGames.Add(runningGame);
                    _latestActiveGame = runningGame;
                    Debug.WriteLine($"Added running game: {runningGame.Name} ({runningGame.Pid})");
                }
                GamepadController.DisableGamepadGuideButtonForGameBarBecauseOfGameStart();
                _timer.Start();
            }
        }
        catch { }
    }

    public static RunningGame? GetLatestActiveGame()
    {
        nint foreground = (nint)User32.GetForegroundWindow();
        User32.GetWindowThreadProcessId(foreground, out uint pid);
        lock (_runningGameLock)
        {
            var active = _runningGames.FirstOrDefault(x => x.Pid == pid);
            if (active is null && pid != 0)
            {
                Process? process = null;
                try
                {
                    process = Process.GetProcessById((int)pid);
                    if (process.SessionId == Process.GetCurrentProcess().SessionId && WutheringWavesRuntime.IsGameProcessName(process.ProcessName))
                    {
                        AddRuninngGame(AppConfig.CurrentGameBiz, process);
                        active = _runningGames.FirstOrDefault(x => x.Pid == pid);
                    }
                    else process.Dispose();
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    process?.Dispose();
                }
            }
            if (active is not null) active.WindowHandle = foreground;
            _latestActiveGame = active ?? _latestActiveGame;
            if (_latestActiveGame is { } latest && CanCapture(latest)) return latest;
            return _runningGames.FirstOrDefault(CanCapture);
        }
    }

    private static bool CanCapture(RunningGame game)
    {
        try { return !game.Process.HasExited && game.WindowHandle != 0; }
        catch (InvalidOperationException) { return false; }
    }

    public static int GetRunningGameCount()
    {
        lock (_runningGameLock) return _runningGames.Count;
    }

    private static void CheckGameExit(object? sender, ElapsedEventArgs e)
    {
        lock (_runningGameLock)
        {
            for (int i = 0; i < _runningGames.Count; i++)
            {
                RunningGame runningGame = _runningGames[i];
                if (runningGame.Process.HasExited)
                {
                    User32.UnhookWinEvent(runningGame.WinEventHook);
                    _runningGames.RemoveAt(i);
                    if (_latestActiveGame?.Pid == runningGame.Pid)
                    {
                        _latestActiveGame = null;
                    }
                    i--;
                    Debug.WriteLine($"Running game exited: {runningGame.Name} ({runningGame.Pid})");
                }
            }
            if (_runningGames.Count == 0)
            {
                GamepadController.RestoreGamepadGuideButtonForGameBarBecauseOfGameExit();
                _timer.Stop();
            }
        }
    }


    public static bool OpenOverlayWindow() => false;

    private static void WinEventProc(User32.HWINEVENTHOOK hWinEventHook, User32.EventConstant winEvent, HWND hwnd, User32.ObjectIdentifier idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        lock (_runningGameLock)
        {
            if (_runningGames.FirstOrDefault(x => x.WinEventHook == hWinEventHook) is RunningGame runningGame)
            {
                _latestActiveGame = runningGame;
                runningGame.WindowHandle = (nint)hwnd;
                Debug.WriteLine($"Set to foreground: {runningGame.Name}");
            }
        }
    }

}
