using gca.Classes;
using gca.Classes.Automation;
using gca.Enums;
using gca.Script;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace gca
{
    public enum BotState
    {
        Stopped = 0,
        Starting,
        Running,
        PauseRequested,
        Paused,
        StopRequested
    }

    public partial class Autobot
    {
        private readonly object stateLock = new();

        private Thread? clickerThread = null;
        private Thread? timeRunningThread = null;

        public BotState State { get; private set; } = BotState.Stopped;
        public bool IsActive { get; private set; } = false;
        public bool IsRunning { get; private set; } = false;
        public bool IsPaused { get; private set; } = false;

        private volatile bool stopRequested = true;
        private volatile bool userStopRequested = false;

        private readonly ManualResetEventSlim pauseEvent = new ManualResetEventSlim(true);
        private readonly ManualResetEvent pauseRequestedEvent = new ManualResetEvent(false);
        private readonly ManualResetEvent stopWaitHandle = new ManualResetEvent(true);
        private readonly ManualResetEventSlim workerExitedEvent = new ManualResetEventSlim(true);

        private TestMode testMode;

        private string windowName = null!;
        private IEnumerable<WaitBetweenBattlesUserControl> waitBetweenBattlesUserControls = null!;
        private BuildUserControl build = null!;

        private void StartThread(TestMode testMode = TestMode.None, bool isAutoRestart = false)
        {
            try
            {
                bool shouldTogglePause = false;
                bool shouldResume = false;
                bool needWaitForExit = false;

                lock (stateLock)
                {
                    if (IsActive)
                    {
                        if (clickerThread is null)
                        {
                            Log.F($"Thread is null while {nameof(IsActive)} is true (state={State}).");
                            OnFailed?.Invoke($"Clicker thread is null and {nameof(IsActive)} is true.\nCannot run clicker.\nIf you keep seeing this error - restart app");
                            return;
                        }
                        if (IsRunning)
                            shouldTogglePause = true;
                        else
                            shouldResume = true;
                    }
                    else if (State == BotState.Starting)
                    {
                        Log.W("Start ignored: bot is already initializing.");
                        return;
                    }
                    else if (clickerThread is not null)
                    {
                        needWaitForExit = true;
                    }
                    else
                    {
                        State = BotState.Starting;
                        userStopRequested = false;
                        stopRequested = false;
                    }
                }

                if (shouldTogglePause)
                {
                    RequestPause("Start/Pause toggle");
                    return;
                }
                if (shouldResume)
                {
                    RequestResume("Start/Resume toggle");
                    return;
                }

                if (needWaitForExit)
                {
                    Log.W("Previous clicker thread is still exiting; waiting up to 1000 ms.");
                    workerExitedEvent.Wait(1000);
                    lock (stateLock)
                    {
                        if (clickerThread is not null)
                        {
                            Log.F("Previous clicker thread did not exit in time.");
                            OnFailed?.Invoke("Previous clicker thread was not finished.\nWait a moment or restart app.");
                            return;
                        }
                        if (IsActive || State == BotState.Starting)
                            return;

                        State = BotState.Starting;
                        userStopRequested = false;
                        stopRequested = false;
                    }
                }

                if (!isAutoRestart)
                {
                    restartGuard.ResetSession();
                }

                Log.I($"Initialize parameters (autoRestart={isAutoRestart})");
                if (!InitParameters(out string message))
                {
                    Log.F($"Init failed with message: {message}");
                    ResetAfterFailedStart();
                    OnFailed?.Invoke(message);
                    return;
                }

                if (userStopRequested)
                {
                    Log.U("Start aborted: Stop requested during initialization.");
                    ResetAfterFailedStart();
                    return;
                }

                bool plainScreen;
                try
                {
                    plainScreen = IsPlainScreen();
                }
                catch (AutomationException e)
                {
                    Log.E($"Startup screen check failed [{e.Kind}]: {e.Message}");
                    ResetAfterFailedStart();
                    OnFailed?.Invoke(e.Message);
                    return;
                }
                catch (OperationCanceledException)
                {
                    Log.U("Start aborted during startup screen check.");
                    ResetAfterFailedStart();
                    return;
                }

                if (plainScreen)
                {
                    string msg = "The script sees plain screen. Check if you have correct Window name.";
                    if (backgroundMode)
                    {
                        if (useAdb)
                        {
                            msg += "\nADB capture returned a blank screen. Ensure LDPlayer has finished booting and Grow Castle is running.";
                        }
                        else if (captureHwnd == hwnd)
                        {
                            msg += "\nTry 'Capture inner window', below 'Background mode'.";
                        }
                        else
                        {
                            msg += "\nCapture failed for the LDPlayer render surface. Check the renderer and retry. The bot will not switch to global input.";
                        }
                    }
                    else
                    {
                        msg += "\nIf you have grow castle opened - the script can't see it for some reason. Message me with the problem or open an issue on Github page of the bot.";
                    }

                    ResetAfterFailedStart();
                    OnFailed?.Invoke(msg);
                    return;
                }

                if (userStopRequested)
                {
                    Log.U("Start aborted: Stop requested before launching worker thread.");
                    ResetAfterFailedStart();
                    return;
                }

                this.testMode = testMode;

                if (saveScreenshotsOnError)
                {
                    screenshotCache = new ScreenshotCache(cacheDurationSec, cacheImageQuality, cacheIntervalMs);
                }
                else
                {
                    screenshotCache = new ScreenshotCache(120, 10, 300);
                }

                UpdateRestartTime();
                UpdateCleanupTime();

                Log.I("Finished initialization");
                Log.U("Starting clicker thread");

                Thread worker;
                Thread timer;
                lock (stateLock)
                {
                    if (userStopRequested)
                    {
                        ResetAfterFailedStartLocked();
                        return;
                    }

                    worker = new Thread(WorkerLoop)
                    {
                        IsBackground = true,
                        Name = "GCA-Worker"
                    };

                    timer = new Thread(TimeRunningUpdater)
                    {
                        IsBackground = true,
                        Name = "GCA-TimeUpdater"
                    };

                    clickerThread = worker;
                    timeRunningThread = timer;
                    workerExitedEvent.Reset();

                    SetRunningStateLocked();
                    lastRunTime = DateTime.Now;
                    clickerStopwatch = Stopwatch.StartNew();
                }

                Log.U("Run");
                _ = GetLatestVersionAsync();
                SetRunningUI();
                _ = LogRun();
                worker.Start();
                timer.Start();
            }
            catch (AutomationException e)
            {
                Log.E($"Start failed [{e.Kind}]: {e.Message}");
                ResetAfterFailedStart();
                OnFailed?.Invoke(e.Message);
            }
            catch (Exception e)
            {
                Log.F($"Unhandled exception in {nameof(StartThread)}:\n{e.Message}\n\nInner message: {e.InnerException?.Message}\n\nCall stack:\n{e.StackTrace}");
                ResetAfterFailedStart();
                OnFailed?.Invoke($"Error happened inside of {nameof(StartThread)}:\n{e.Message}\n\nInner message: {e.InnerException?.Message}\n\nCall stack:\n{e.StackTrace}");
            }
        }

        private void ResetAfterFailedStart()
        {
            lock (stateLock)
            {
                ResetAfterFailedStartLocked();
            }
            DisposeAutomationResources();
        }

        private void ResetAfterFailedStartLocked()
        {
            if (clickerThread is null)
            {
                SetDefaultThreadStateLocked();
            }
        }

        private bool RunRebootProcess(string arguments, string label, int timeoutMs = 15_000)
        {
            C();
            var token = GetIoToken();
            var psi = new ProcessStartInfo
            {
                FileName = ldConsolePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process proc = new Process { StartInfo = psi };
            Log.I($"Start {label} process");
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            try
            {
                Task.WhenAll(proc.WaitForExitAsync(token), stdoutTask, stderrTask)
                    .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), token)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                AdbTarget.TryKill(proc);
                AdbTarget.DrainTasksSafely(stdoutTask, stderrTask);
                throw;
            }
            catch (TimeoutException)
            {
                AdbTarget.TryKill(proc);
                AdbTarget.DrainTasksSafely(stdoutTask, stderrTask);
                Log.E($"{label} process timed out after {timeoutMs} ms.");
                return false;
            }

            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();
            string output = stdout;
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                output += Environment.NewLine + stderr;
            }

            if (output.Length > 0)
            {
                Log.E($"{label} failed with message: {output}");
                return false;
            }
            return true;
        }

        public bool LDConsoleReboot()
        {
            if (backgroundMode)
                throw new AutomationException(AutomationErrorKind.WindowInvalid, "Window invalid: automatic LDPlayer reboot is disabled in Background mode.");

            if (!RunRebootProcess($"quit --name \"{windowName}\"", "Quit"))
                return false;

            if (!WaitUntil(() => !WinAPI.WindowExists(hwnd), delegate { }, 30_000, 100))
            {
                Log.E("Cannot finish current LDPlayer instance");
                return false;
            }

            Log.I("Finished LDPlayer instance");

            if (!RunRebootProcess($"launch --name \"{windowName}\"", "Launch"))
                return false;

            Log.I("Wait for LDPlayer window to appear");
            if (WaitUntil(() => GetLDPlayerWindow() != IntPtr.Zero, delegate { }, 30_000, 1_000))
            {
                hwnd = GetLDPlayerWindow();
                GetRenderHwnd(hwnd);
                Log.I($"LDPlayer appeared: HWND: {hwnd}, Render: {renderHwnd}");

                if (hwnd != IntPtr.Zero && renderHwnd != IntPtr.Zero)
                {
                    if (WaitUntil(() => WinAPI.IsWindowVisible(renderHwnd), delegate { }, 300_000, 1_000))
                    {
                        Log.I("LDPlayer loaded");
                        Wait(3_000);
                        SetPos(hwnd);
                        Wait(3_000);
                        return true;
                    }
                    else
                    {
                        Log.E("LDPlayer didn't load");
                    }
                }
                else
                {
                    Log.E("Couldn't get render hwnd");
                }
            }
            else
            {
                Log.E("LDPlayer window did not appear");
            }
            return false;
        }

        private void SetPos(IntPtr hwnd)
        {
            if (backgroundMode) return;
            Utils.SetDefaultWindowState(hwnd, DisplayInfo.X, DisplayInfo.Y);
            WinAPI.RestoreWindow(hwnd);
            WinAPI.SetWindowPos(hwnd, hwnd, 0, 0, Cst.WINDOW_WIDTH + 1, Cst.WINDOW_HEIGHT + 1, WinAPI.SWP_NOZORDER);
            Utils.SetDefaultWindowState(hwnd, DisplayInfo.X, DisplayInfo.Y);
        }

        public void LDConsoleCloseGC()
        {
            RunLdConsole("killapp", backgroundMode ? "--index" : "--name",
                backgroundMode ? ldInstanceIndex.ToString() : windowName, "--packagename", "com.raongames.growcastle");
        }

        public void LDConsoleOpenGC()
        {
            RunLdConsole("runapp", backgroundMode ? "--index" : "--name",
                backgroundMode ? ldInstanceIndex.ToString() : windowName, "--packagename", "com.raongames.growcastle");
        }

        /// <summary>
        /// Call only from inside of clicker thread
        /// </summary>
        /// <exception cref="OperationCanceledException"></exception>
        private void Halt(bool allowAutoRestart = false)
        {
            Log.I($"Stop by halt (allowAutoRestart={allowAutoRestart})");
            lock (stateLock)
            {
                restartRequested = allowAutoRestart && !userStopRequested;
            }
            RequestStop(userInitiated: false, reason: allowAutoRestart ? "Halt for restart" : "Halt");
            throw new OperationCanceledException();
        }

        /// <summary>
        /// Don't call from inside of clicker thread
        /// </summary>
        private void RestartThread()
        {
            if (userStopRequested)
            {
                Log.I("Skipping RestartThread because user requested Stop.");
                return;
            }
            Log.I("Starting new thread (auto-restart)");
            restartRequested = false;

            StartThread(TestMode.None, isAutoRestart: true);
        }

        private void RequestStop(bool userInitiated, string reason)
        {
            bool notifyStopRequested = false;
            List<WaitBetweenBattlesRuntime>? runtimesToDispose = null;

            lock (stateLock)
            {
                if (userInitiated)
                {
                    userStopRequested = true;
                    restartRequested = false;
                }

                stopRequested = true;
                CancelIo();
                try { (gameInput as IDisposable)?.Dispose(); } catch { }

                if (IsActive || State == BotState.Starting)
                {
                    State = BotState.StopRequested;
                    IsRunning = false;
                    IsActive = false;
                    IsPaused = false;

                    pauseRequestedEvent.Reset();
                    pauseEvent.Set();
                    stopWaitHandle.Set();
                    clickerStopwatch.Stop();

                    if (waitBetweenBattlesRuntimes != null && waitBetweenBattlesRuntimes.Count > 0)
                    {
                        runtimesToDispose = new List<WaitBetweenBattlesRuntime>(waitBetweenBattlesRuntimes);
                    }
                    notifyStopRequested = true;
                }
                else
                {
                    pauseRequestedEvent.Reset();
                    pauseEvent.Set();
                    stopWaitHandle.Set();
                }
            }

            Log.U($"Stop requested ({reason}, userInitiated={userInitiated}).");

            if (runtimesToDispose != null)
            {
                foreach (var rt in runtimesToDispose)
                {
                    try { rt?.Dispose(); } catch { }
                }
            }

            if (notifyStopRequested)
            {
                OnStopRequested?.Invoke();
                _ = GetLatestVersionAsync();
            }
        }

        private void RequestPause(string reason)
        {
            lock (stateLock)
            {
                if (!IsActive || stopRequested || userStopRequested)
                {
                    Log.I($"Pause ignored ({reason}): bot is not active or is stopping (state={State}).");
                    return;
                }
                if (!IsRunning)
                {
                    Log.I($"Pause ignored ({reason}): bot is already paused/pausing (state={State}).");
                    return;
                }

                State = BotState.PauseRequested;
                IsRunning = false;
                pauseEvent.Reset();
                pauseRequestedEvent.Set();
                clickerStopwatch.Stop();
                SuspendWaitTimersLocked();
            }

            Log.U($"Pause requested ({reason}).");
            OnPauseRequested?.Invoke();
        }

        private void RequestResume(string reason)
        {
            lock (stateLock)
            {
                if (!IsActive || stopRequested || userStopRequested)
                {
                    Log.I($"Resume ignored ({reason}): bot is not active or is stopping (state={State}).");
                    return;
                }
                if (IsRunning)
                {
                    Log.I($"Resume ignored ({reason}): bot is already running.");
                    return;
                }

                State = BotState.Running;
                IsRunning = true;
                IsPaused = false;
                pauseRequestedEvent.Reset();
                pauseEvent.Set();
                clickerStopwatch.Start();
                ResumeWaitTimersLocked();
            }

            Log.U($"Resumed ({reason}).");
            _ = GetLatestVersionAsync();
            SetRunningUI();
        }

        private void SuspendWaitTimersLocked()
        {
            if (waitBetweenBattlesRuntimes == null) return;
            foreach (var rt in waitBetweenBattlesRuntimes)
            {
                try { rt?.Suspend(); } catch { }
            }
        }

        private void ResumeWaitTimersLocked()
        {
            if (waitBetweenBattlesRuntimes == null) return;
            foreach (var rt in waitBetweenBattlesRuntimes)
            {
                try
                {
                    if (rt != null && rt.IsActive && !rt.IsElapsed)
                        rt.Resume();
                }
                catch { }
            }
        }

        private void SetStoppedState() => RequestStop(userInitiated: true, reason: "SetStoppedState");

        private void SetPausedState() => RequestPause("SetPausedState");

        private void SetPausedUI()
        {
            OnPaused?.Invoke();
        }

        private void SetRunningStateLocked()
        {
            State = BotState.Running;
            IsActive = true;
            IsRunning = true;
            IsPaused = false;
            stopRequested = false;

            pauseRequestedEvent.Reset();
            pauseEvent.Set();
            stopWaitHandle.Reset();
        }

        private void SetRunningUI()
        {
            OnStarted?.Invoke(notificationOnlyMode);
        }

        private void OnResumed() => RequestResume("OnResumed");

        private void TimeRunningUpdater()
        {
            try
            {
                while (IsActive && !stopRequested)
                {
                    TimeSpan runningTime = clickerStopwatch.Elapsed;
                    try { OnRunningTimeUpdate?.Invoke(runningTime); }
                    catch { }

                    if (stopWaitHandle.WaitOne(137))
                        break;
                }
            }
            catch { }
        }

        public void Start(TestMode testMode = TestMode.None)
        {
            StartThread(testMode);
        }

        public void Pause()
        {
            RequestPause("Pause API");
        }

        public void Resume()
        {
            RequestResume("Resume API");
        }

        public void Stop()
        {
            RequestStop(userInitiated: true, reason: "Stop API");
        }

        public bool StopAndWait(TimeSpan timeout)
        {
            RequestStop(userInitiated: true, reason: "StopAndWait");
            return workerExitedEvent.Wait(timeout);
        }

        public void OnStopHotkey()
        {
            Log.U("Stop hotkey");
            RequestStop(userInitiated: true, reason: "Stop hotkey");
        }

        /// <summary>
        /// Call only when clicker is off
        /// </summary>
        private void SetDefaultThreadState()
        {
            lock (stateLock)
            {
                SetDefaultThreadStateLocked();
            }
        }

        private void SetDefaultThreadStateLocked()
        {
            State = BotState.Stopped;
            IsRunning = false;
            stopRequested = true;
            IsActive = false;
            IsPaused = false;

            pauseRequestedEvent.Reset();
            pauseEvent.Set();
            stopWaitHandle.Set();
            clickerStopwatch.Stop();
        }

        private void Wait(int milliseconds)
        {
            C();
            if (milliseconds <= 0) return;

            var token = GetIoToken();
            int remainingMs = milliseconds;
            var handles = new WaitHandle[] { token.WaitHandle, pauseRequestedEvent };

            while (remainingMs > 0)
            {
                C();
                var sw = Stopwatch.StartNew();
                int signaled;
                try
                {
                    signaled = WaitHandle.WaitAny(handles, remainingMs);
                }
                catch (ObjectDisposedException)
                {
                    throw new OperationCanceledException();
                }
                sw.Stop();

                if (signaled == 0 || stopRequested || token.IsCancellationRequested)
                    throw new OperationCanceledException(token);

                if (signaled == 1)
                {
                    // Woke immediately because Pause was requested; enter pause and preserve remaining wait time.
                    remainingMs -= (int)sw.ElapsedMilliseconds;
                    C();
                    continue;
                }

                break;
            }

            C();
        }

        private void C()
        {
            var token = GetIoToken();
            if (stopRequested || token.IsCancellationRequested)
                throw new OperationCanceledException(token);

            if (!pauseEvent.IsSet)
            {
                bool enteredPause = false;
                lock (stateLock)
                {
                    if (stopRequested || token.IsCancellationRequested)
                        throw new OperationCanceledException(token);

                    if (!pauseEvent.IsSet && !IsPaused)
                    {
                        IsPaused = true;
                        State = BotState.Paused;
                        enteredPause = true;
                    }
                }

                if (enteredPause)
                {
                    Log.U("Worker paused safely at checkpoint.");
                    SetPausedUI();
                }

                try
                {
                    pauseEvent.Wait(token);
                }
                catch (ObjectDisposedException)
                {
                    throw new OperationCanceledException();
                }
            }

            if (stopRequested || token.IsCancellationRequested)
                throw new OperationCanceledException(token);

            bool resumedFromPause = false;
            lock (stateLock)
            {
                if (stopRequested || token.IsCancellationRequested)
                    throw new OperationCanceledException(token);

                if (IsPaused)
                {
                    IsPaused = false;
                    State = BotState.Running;
                    resumedFromPause = true;
                }
            }

            if (resumedFromPause)
            {
                SetRunningUI();
            }
        }
    }
}
