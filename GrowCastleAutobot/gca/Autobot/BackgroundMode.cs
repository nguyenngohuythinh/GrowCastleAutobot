using gca.Classes;
using gca.Classes.Automation;
using System.Diagnostics;

namespace gca;

public partial class Autobot
{
    public bool UsesBackgroundMode => backgroundMode;
    public bool UsesAdb => backgroundMode && useAdb;
    private bool useAdb;
    private IGameCapture? gameCapture;
    private IGameInput? gameInput;
    private WindowTarget? backgroundTarget;
    private AdbTarget? adbTarget;
    private readonly object ioLock = new();
    private CancellationTokenSource ioCancellation = new();
    private readonly FrameHealth frameHealth = new();
    private readonly Stopwatch frameClock = Stopwatch.StartNew();
    private readonly RetryPolicy captureRetryPolicy = new(maxRetries: 2, baseDelayMs: 200, maxDelayMs: 2000, backoffMultiplier: 2.0);
    private readonly RetryTracker captureRetryTracker = new();
    private readonly RestartGuard restartGuard = new();
    private DateTime lastValidCapture;
    private int ldInstanceIndex;
    private int completedBattles;
    private long lastCaptureElapsedMs;
    private long lastStatusPublishTicks;

    public RetryTracker CaptureRetryTracker => captureRetryTracker;
    public RestartGuard RestartGuard => restartGuard;
    public int CompletedBattles => Volatile.Read(ref completedBattles);

    public RuntimeStatusSnapshot GetRuntimeStatusSnapshot()
    {
        string modeName = backgroundMode
            ? (useAdb ? "Background ADB" : "Background Win32")
            : "Foreground";
        string targetInfo = backgroundMode
            ? (useAdb ? (adbTarget?.Serial ?? $"LD#{ldInstanceIndex}") : (backgroundTarget != null ? $"HWND 0x{backgroundTarget.Root.ToInt64():X}" : ""))
            : "";

        return new RuntimeStatusSnapshot(
            modeName,
            targetInfo,
            Volatile.Read(ref completedBattles),
            Volatile.Read(ref lastCaptureElapsedMs),
            captureRetryTracker.TotalRetries,
            captureRetryTracker.ConsecutiveFailures);
    }

    internal void RecordCompletedBattle()
    {
        Interlocked.Increment(ref completedBattles);
        PublishRuntimeStatus(force: true);
    }

    private void PublishRuntimeStatus(bool force)
    {
        var handler = OnRuntimeStatusUpdate;
        if (handler == null) return;

        long nowTicks = Stopwatch.GetTimestamp();
        if (!force)
        {
            long prevTicks = Volatile.Read(ref lastStatusPublishTicks);
            if (prevTicks != 0 && (nowTicks - prevTicks) * 1000 / Stopwatch.Frequency < 500)
                return;
        }
        Volatile.Write(ref lastStatusPublishTicks, nowTicks);
        try { handler.Invoke(GetRuntimeStatusSnapshot()); }
        catch { }
    }

    private CancellationToken GetIoToken()
    {
        lock (ioLock)
        {
            try { return ioCancellation.Token; }
            catch (ObjectDisposedException) { return new CancellationToken(canceled: true); }
        }
    }

    private void CancelIo()
    {
        lock (ioLock)
        {
            try { ioCancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    private void ResetAutomationSession()
    {
        CancellationTokenSource oldCts;
        lock (ioLock)
        {
            oldCts = ioCancellation;
            ioCancellation = new CancellationTokenSource();
        }
        try { oldCts.Dispose(); } catch { }

        frameHealth.Reset();
        captureRetryTracker.Reset();
        Interlocked.Exchange(ref completedBattles, 0);
        Interlocked.Exchange(ref lastCaptureElapsedMs, 0);
        Volatile.Write(ref lastStatusPublishTicks, 0);
        DisposeAutomationResources();
        screenShiftX = screenShiftY = 0;
    }

    private void DisposeAutomationResources()
    {
        try { currentScreen?.Dispose(); } catch { }
        currentScreen = null!;
        try { (gameCapture as IDisposable)?.Dispose(); } catch { }
        try { (gameInput as IDisposable)?.Dispose(); } catch { }
        gameCapture = null;
        gameInput = null;
        backgroundTarget = null;
        adbTarget = null;
    }

    private void InitializeBackground(bool captureInner)
    {
        if (!backgroundMode) return;
        backgroundTarget = new WindowTarget(hwnd, renderHwnd);
        gameCapture = new WindowCapture(backgroundTarget, captureInner);
        gameInput = new BackgroundInput(backgroundTarget, C);
        Log.I($"Background Win32: root={hwnd}, render={renderHwnd}, preferInner={captureInner}; viewport={GameCoordinates.Viewport}");
    }

    private void InitializeAdb(string adbPath, int instanceIndex, int width, int height)
    {
        if (!backgroundMode) return;
        var token = GetIoToken();
        string serial = AdbTarget.ResolveSerial(adbPath, instanceIndex, token);
        adbTarget = new AdbTarget(adbPath, serial, instanceIndex, new Size(width, height));
        gameCapture = new AdbCapture(adbTarget);
        gameInput = new AdbInput(adbTarget, C, token);
        Log.I($"Background ADB: index={instanceIndex}, serial={serial}, screen={width}x{height}; viewport={GameCoordinates.Viewport}");
    }

    private Bitmap CaptureBackground()
    {
        for (int attempt = 1; ; attempt++)
        {
            var token = GetIoToken();
            token.ThrowIfCancellationRequested();
            if (IsActive) C();

            captureRetryTracker.RecordAttempt();
            var sw = Stopwatch.StartNew();
            try
            {
                if (gameCapture == null)
                    throw new AutomationException(AutomationErrorKind.CaptureFailed, "Capture failed: background capture session is not initialized.", isRetryable: false);

                var frame = gameCapture.Capture(token);
                try
                {
                    frameHealth.Observe(frame, frameClock.Elapsed);
                }
                catch
                {
                    frame.Dispose();
                    throw;
                }

                sw.Stop();
                if (attempt > 1)
                {
                    Log.I($"Capture recovered on attempt {attempt} ({sw.ElapsedMilliseconds} ms) after {attempt - 1} retry(s).");
                }
                captureRetryTracker.RecordSuccess();
                Interlocked.Exchange(ref lastCaptureElapsedMs, sw.ElapsedMilliseconds);
                lastValidCapture = DateTime.UtcNow;
                PublishRuntimeStatus(force: attempt > 1);
                return frame;
            }
            catch (AutomationException e)
            {
                sw.Stop();
                bool willRetry = captureRetryPolicy.ShouldRetry(e, attempt);
                captureRetryTracker.RecordFailure(e, willRetry);

                if (!willRetry)
                {
                    if (!e.IsRetryable)
                    {
                        Log.W($"{e.Message} Non-retryable error ({e.Kind}); skipping capture retries.");
                    }
                    else
                    {
                        Log.E($"{e.Message} Capture retries exhausted after {attempt} attempt(s) (kind={e.Kind}, consecutive={captureRetryTracker.ConsecutiveFailures}, timeouts={captureRetryTracker.ConsecutiveTimeouts}).");
                    }
                    throw;
                }

                int delayMs = captureRetryPolicy.GetDelayMs(attempt);
                Log.W($"{e.Message} Capture recovery {attempt}/{captureRetryPolicy.MaxRetries} (kind={e.Kind}, delay={delayMs}ms, elapsed={sw.ElapsedMilliseconds}ms, consecutive={captureRetryTracker.ConsecutiveFailures}).");
                WaitRetryDelay(delayMs, token);
            }
        }
    }

    private void WaitRetryDelay(int delayMs, CancellationToken token)
    {
        if (delayMs <= 0)
        {
            if (stopRequested || token.IsCancellationRequested)
                throw new OperationCanceledException(token);
            if (IsActive) C();
            return;
        }

        int remainingMs = delayMs;
        var handles = new WaitHandle[] { token.WaitHandle, pauseRequestedEvent };

        while (remainingMs > 0)
        {
            if (stopRequested || token.IsCancellationRequested)
                throw new OperationCanceledException(token);
            if (IsActive) C();

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
                remainingMs -= (int)sw.ElapsedMilliseconds;
                if (IsActive) C();
                continue;
            }

            break;
        }
    }

    private void BackgroundAction(Action<IGameInput> action)
    {
        C();
        if (gameInput == null) throw new AutomationException(AutomationErrorKind.InputFailed, "Input failed: background session is not initialized.");
        if (DateTime.UtcNow - lastValidCapture > TimeSpan.FromMilliseconds(500)) G();
        action(gameInput);
        frameHealth.InputSent(frameClock.Elapsed);
    }

    private void PauseAutomation(AutomationException error)
    {
        var token = GetIoToken();
        lock (stateLock)
        {
            if (stopRequested || userStopRequested || token.IsCancellationRequested)
                throw new OperationCanceledException(token);

            restartRequested = false;
            State = BotState.Paused;
            IsActive = true;
            IsRunning = false;
            IsPaused = true;
            pauseEvent.Reset();
            pauseRequestedEvent.Set();
            clickerStopwatch.Stop();
            SuspendWaitTimersLocked();
        }

        Log.E($"[{error.Kind}] {error.Message}");
        Log.U("Paused due to automation error");
        OnPauseRequested?.Invoke();
        SetPausedUI();
        OnFailed?.Invoke(error.Message + "\nBot paused. Correct the problem, then resume; Stop remains available.");

        C();
        frameHealth.Reset();
        captureRetryTracker.RecordSuccess();
    }

    private void RequireForegroundInput()
    {
        if (backgroundMode) throw new AutomationException(AutomationErrorKind.InputFailed, "Input failed: global input is disabled in Background mode.");
    }

    private string RunLdConsole(params string[] arguments)
    {
        if (IsActive) C();
        if (backgroundMode && backgroundTarget != null) backgroundTarget.Validate();
        var token = GetIoToken();
        var info = new ProcessStartInfo(ldConsolePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try
            {
                Task.WhenAll(process.WaitForExitAsync(token), output, errors)
                    .WaitAsync(TimeSpan.FromSeconds(5), token).GetAwaiter().GetResult();
            }
            catch
            {
                AdbTarget.TryKill(process);
                AdbTarget.DrainTasksSafely(output, errors);
                throw;
            }
            var stderr = errors.GetAwaiter().GetResult();
            var stdout = output.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(stderr))
                throw new AutomationException(AutomationErrorKind.InputFailed, $"Input failed: ldconsole exit={process.ExitCode}: {stderr} {stdout}");
            return stdout;
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException) { throw new AutomationException(AutomationErrorKind.InputFailed, "Input failed: ldconsole timed out after 5000 ms."); }
        catch (AutomationException) { throw; }
        catch (Exception e) { throw new AutomationException(AutomationErrorKind.InputFailed, $"Input failed: ldconsole: {e.Message}"); }
    }
}
