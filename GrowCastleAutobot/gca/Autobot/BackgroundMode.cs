using gca.Classes;
using gca.Classes.Automation;
using System.Diagnostics;

namespace gca;

public partial class Autobot
{
    public bool UsesBackgroundMode => backgroundMode;
    private IGameCapture? gameCapture;
    private IGameInput? gameInput;
    private WindowTarget? backgroundTarget;
    private CancellationTokenSource ioCancellation = new();
    private readonly FrameHealth frameHealth = new();
    private readonly Stopwatch frameClock = Stopwatch.StartNew();
    private DateTime lastValidCapture;
    private int ldInstanceIndex;

    private void ResetAutomationSession()
    {
        ioCancellation.Dispose();
        ioCancellation = new CancellationTokenSource();
        frameHealth.Reset();
        currentScreen?.Dispose();
        currentScreen = null!;
        screenShiftX = screenShiftY = 0;
        gameCapture = null;
        gameInput = null;
        backgroundTarget = null;
    }

    private void InitializeBackground(bool captureInner)
    {
        if (!backgroundMode) return;
        backgroundTarget = new WindowTarget(hwnd, renderHwnd);
        gameCapture = new WindowCapture(backgroundTarget, captureInner);
        gameInput = new BackgroundInput(backgroundTarget, C);
        Log.I($"Background Win32: root={hwnd}, render={renderHwnd}, preferInner={captureInner}; viewport={GameCoordinates.Viewport}");
    }

    private Bitmap CaptureBackground()
    {
        for (int attempt = 1; ; attempt++)
        {
            ioCancellation.Token.ThrowIfCancellationRequested();
            if (IsActive) C();
            try
            {
                var frame = gameCapture!.Capture(ioCancellation.Token);
                try { frameHealth.Observe(frame, frameClock.Elapsed); }
                catch { frame.Dispose(); throw; }
                lastValidCapture = DateTime.UtcNow;
                return frame;
            }
            catch (AutomationException e) when (attempt < 3)
            {
                Log.W($"{e.Message} Capture recovery {attempt}/2.");
                if (ioCancellation.Token.WaitHandle.WaitOne(200)) ioCancellation.Token.ThrowIfCancellationRequested();
            }
        }
    }

    private void BackgroundAction(Action<IGameInput> action)
    {
        C();
        if (gameInput == null) throw new AutomationException("Input failed: background session is not initialized.");
        if (DateTime.UtcNow - lastValidCapture > TimeSpan.FromMilliseconds(500)) G();
        action(gameInput);
        frameHealth.InputSent(frameClock.Elapsed);
    }

    private void PauseAutomation(AutomationException error)
    {
        ioCancellation.Token.ThrowIfCancellationRequested();
        restartRequested = false;
        Log.E(error.Message);
        SetPausedState();
        IsPaused = true;
        SetPausedUI();
        OnFailed?.Invoke(error.Message + "\nBot paused. Correct the problem, then resume; Stop remains available.");
        C();
        frameHealth.Reset();
    }

    private void RequireForegroundInput()
    {
        if (backgroundMode) throw new AutomationException("Input failed: global input is disabled in Background mode.");
    }

    private string RunLdConsole(params string[] arguments)
    {
        if (IsActive) C();
        if (backgroundMode && backgroundTarget != null) backgroundTarget.Validate();
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
                Task.WhenAll(process.WaitForExitAsync(ioCancellation.Token), output, errors)
                    .WaitAsync(TimeSpan.FromSeconds(5), ioCancellation.Token).GetAwaiter().GetResult();
            }
            catch
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }
            var stderr = errors.GetAwaiter().GetResult();
            var stdout = output.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(stderr))
                throw new AutomationException($"Input failed: ldconsole exit={process.ExitCode}: {stderr} {stdout}");
            return stdout;
        }
        catch (OperationCanceledException) { throw; }
        catch (AutomationException) { throw; }
        catch (Exception e) { throw new AutomationException($"Input failed: ldconsole: {e.Message}"); }
    }
}
