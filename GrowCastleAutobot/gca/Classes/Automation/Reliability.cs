using System.Diagnostics;

namespace gca.Classes.Automation;

public enum AutomationErrorKind
{
    Unknown = 0,
    CaptureTimeout,
    CaptureFailed,
    StaleFrame,
    InputFailed,
    WindowInvalid,
    GameStateUnknown,
    RestartLimitReached
}

public sealed class RetryPolicy
{
    public int MaxRetries { get; }
    public int BaseDelayMs { get; }
    public int MaxDelayMs { get; }
    public double BackoffMultiplier { get; }

    public RetryPolicy(int maxRetries = 2, int baseDelayMs = 200, int maxDelayMs = 2000, double backoffMultiplier = 2.0)
    {
        if (maxRetries < 0) throw new ArgumentOutOfRangeException(nameof(maxRetries));
        if (baseDelayMs < 0) throw new ArgumentOutOfRangeException(nameof(baseDelayMs));
        if (maxDelayMs < baseDelayMs) throw new ArgumentOutOfRangeException(nameof(maxDelayMs));
        if (backoffMultiplier < 1.0) throw new ArgumentOutOfRangeException(nameof(backoffMultiplier));

        MaxRetries = maxRetries;
        BaseDelayMs = baseDelayMs;
        MaxDelayMs = maxDelayMs;
        BackoffMultiplier = backoffMultiplier;
    }

    public bool ShouldRetry(AutomationException error, int attemptNumber)
    {
        if (attemptNumber < 1) return false;
        return error.IsRetryable && attemptNumber <= MaxRetries;
    }

    public int GetDelayMs(int attemptNumber)
    {
        if (attemptNumber < 1 || BaseDelayMs == 0) return 0;
        double scaled = BaseDelayMs * Math.Pow(BackoffMultiplier, attemptNumber - 1);
        if (double.IsNaN(scaled) || double.IsInfinity(scaled) || scaled >= MaxDelayMs)
            return MaxDelayMs;
        return Math.Max(0, (int)Math.Round(scaled));
    }
}

public sealed class RetryTracker
{
    private readonly object gate = new();

    public int TotalAttempts { get; private set; }
    public int TotalRetries { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public int ConsecutiveTimeouts { get; private set; }
    public AutomationErrorKind LastErrorKind { get; private set; } = AutomationErrorKind.Unknown;
    public string? LastErrorMessage { get; private set; }

    public void Reset()
    {
        lock (gate)
        {
            TotalAttempts = 0;
            TotalRetries = 0;
            ConsecutiveFailures = 0;
            ConsecutiveTimeouts = 0;
            LastErrorKind = AutomationErrorKind.Unknown;
            LastErrorMessage = null;
        }
    }

    public void RecordAttempt()
    {
        lock (gate)
        {
            TotalAttempts++;
        }
    }

    public void RecordSuccess()
    {
        lock (gate)
        {
            ConsecutiveFailures = 0;
            ConsecutiveTimeouts = 0;
            LastErrorKind = AutomationErrorKind.Unknown;
            LastErrorMessage = null;
        }
    }

    public void RecordFailure(AutomationException error, bool willRetry)
    {
        lock (gate)
        {
            ConsecutiveFailures++;
            if (error.Kind == AutomationErrorKind.CaptureTimeout)
                ConsecutiveTimeouts++;
            else
                ConsecutiveTimeouts = 0;

            if (willRetry)
                TotalRetries++;

            LastErrorKind = error.Kind;
            LastErrorMessage = error.Message;
        }
    }
}

public sealed class RestartGuard
{
    private readonly object gate = new();
    private readonly Queue<DateTime> restartTimestamps = new();
    private readonly Queue<DateTime> threadRestartTimestamps = new();
    private readonly Func<DateTime> clock;
    private int activeRestartDepth;

    public int MaxRestartsInWindow { get; private set; }
    public TimeSpan RestartWindow { get; private set; }
    public int MaxConsecutiveRestarts { get; private set; }
    public int MaxThreadRestartsInWindow { get; private set; }
    public TimeSpan ThreadRestartWindow { get; private set; }

    public int ConsecutiveRestarts { get; private set; }
    public bool IsRestartInProgress { get { lock (gate) return activeRestartDepth > 0; } }

    public RestartGuard(
        int maxRestartsInWindow = 5,
        TimeSpan? restartWindow = null,
        int maxConsecutiveRestarts = 4,
        int maxThreadRestartsInWindow = 3,
        TimeSpan? threadRestartWindow = null,
        Func<DateTime>? clock = null)
    {
        MaxRestartsInWindow = Math.Max(1, maxRestartsInWindow);
        RestartWindow = restartWindow ?? TimeSpan.FromMinutes(15);
        MaxConsecutiveRestarts = Math.Max(1, maxConsecutiveRestarts);
        MaxThreadRestartsInWindow = Math.Max(1, maxThreadRestartsInWindow);
        ThreadRestartWindow = threadRestartWindow ?? TimeSpan.FromMinutes(15);
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    public void Configure(int? maxConsecutiveRestarts = null, int? maxRestartsInWindow = null, int? maxThreadRestartsInWindow = null)
    {
        lock (gate)
        {
            if (maxConsecutiveRestarts.HasValue)
                MaxConsecutiveRestarts = Math.Max(1, maxConsecutiveRestarts.Value);
            if (maxRestartsInWindow.HasValue)
                MaxRestartsInWindow = Math.Max(1, maxRestartsInWindow.Value);
            if (maxThreadRestartsInWindow.HasValue)
                MaxThreadRestartsInWindow = Math.Max(1, maxThreadRestartsInWindow.Value);
        }
    }

    public void ResetSession()
    {
        lock (gate)
        {
            restartTimestamps.Clear();
            threadRestartTimestamps.Clear();
            ConsecutiveRestarts = 0;
            activeRestartDepth = 0;
        }
    }

    public void RecordHealthyProgress()
    {
        lock (gate)
        {
            ConsecutiveRestarts = 0;
        }
    }

    public bool TryBeginRestart(string reason, out string? blockReason)
    {
        lock (gate)
        {
            if (activeRestartDepth > 0)
            {
                blockReason = $"Restart re-entrancy blocked ({reason}): a restart sequence is already active (depth={activeRestartDepth}).";
                return false;
            }

            DateTime now = clock();
            Prune(restartTimestamps, now, RestartWindow);

            if (ConsecutiveRestarts >= MaxConsecutiveRestarts)
            {
                blockReason = $"Restart limit reached ({reason}): {ConsecutiveRestarts}/{MaxConsecutiveRestarts} consecutive restarts without healthy progress.";
                return false;
            }

            if (restartTimestamps.Count >= MaxRestartsInWindow)
            {
                blockReason = $"Restart loop prevented ({reason}): {restartTimestamps.Count}/{MaxRestartsInWindow} restarts within the last {RestartWindow.TotalMinutes:F0} min.";
                return false;
            }

            restartTimestamps.Enqueue(now);
            ConsecutiveRestarts++;
            activeRestartDepth++;
            blockReason = null;
            return true;
        }
    }

    public void EndRestart()
    {
        lock (gate)
        {
            if (activeRestartDepth > 0)
                activeRestartDepth--;
        }
    }

    public bool TryConsumeThreadRestart(string reason, out string? blockReason)
    {
        lock (gate)
        {
            DateTime now = clock();
            Prune(threadRestartTimestamps, now, ThreadRestartWindow);

            if (threadRestartTimestamps.Count >= MaxThreadRestartsInWindow)
            {
                blockReason = $"Thread restart loop prevented ({reason}): {threadRestartTimestamps.Count}/{MaxThreadRestartsInWindow} automatic thread restarts within the last {ThreadRestartWindow.TotalMinutes:F0} min.";
                return false;
            }

            threadRestartTimestamps.Enqueue(now);
            blockReason = null;
            return true;
        }
    }

    public (int RestartsInWindow, int Consecutive, int ThreadRestartsInWindow) GetSnapshot()
    {
        lock (gate)
        {
            DateTime now = clock();
            Prune(restartTimestamps, now, RestartWindow);
            Prune(threadRestartTimestamps, now, ThreadRestartWindow);
            return (restartTimestamps.Count, ConsecutiveRestarts, threadRestartTimestamps.Count);
        }
    }

    private static void Prune(Queue<DateTime> queue, DateTime now, TimeSpan window)
    {
        while (queue.Count > 0 && now - queue.Peek() > window)
            queue.Dequeue();
    }
}

public readonly record struct RuntimeStatusSnapshot(
    string ModeName,
    string TargetInfo,
    int CompletedBattles,
    long LastCaptureMs,
    int TotalCaptureRetries,
    int ConsecutiveCaptureFailures)
{
    public string FormatShortBadge() => ModeName switch
    {
        "Background ADB" => "ADB",
        "Background Win32" => "Win32",
        _ => "FG"
    };

    public string FormatSummary()
    {
        string targetPart = string.IsNullOrWhiteSpace(TargetInfo) ? "" : $" ({TargetInfo})";
        string capturePart = LastCaptureMs > 0 ? $"{LastCaptureMs} ms" : "-";
        return $"Mode: {ModeName}{targetPart} | Battles: {CompletedBattles} | Capture: {capturePart} | Retries: {TotalCaptureRetries}";
    }
}
