using gca.Script;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace gca.Classes.Automation;

public sealed class AutomationException : Exception
{
    public AutomationErrorKind Kind { get; }
    public bool IsRetryable { get; }

    public AutomationException(string message)
        : this(Classify(message), message)
    {
    }

    public AutomationException(AutomationErrorKind kind, string message, bool? isRetryable = null)
        : base(message)
    {
        Kind = kind;
        IsRetryable = isRetryable ?? DefaultRetryable(kind);
    }

    public static AutomationErrorKind Classify(string message)
    {
        if (string.IsNullOrEmpty(message)) return AutomationErrorKind.Unknown;
        if (message.StartsWith("Capture failed:", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Contains("exceeded", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("still running", StringComparison.OrdinalIgnoreCase))
                return AutomationErrorKind.CaptureTimeout;
            return AutomationErrorKind.CaptureFailed;
        }
        if (message.StartsWith("Stale frame:", StringComparison.OrdinalIgnoreCase))
            return AutomationErrorKind.StaleFrame;
        if (message.StartsWith("Input failed:", StringComparison.OrdinalIgnoreCase))
            return AutomationErrorKind.InputFailed;
        if (message.StartsWith("Window invalid:", StringComparison.OrdinalIgnoreCase))
            return AutomationErrorKind.WindowInvalid;
        if (message.StartsWith("Game state unknown:", StringComparison.OrdinalIgnoreCase))
            return AutomationErrorKind.GameStateUnknown;
        if (message.StartsWith("Restart limit", StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("Restart loop", StringComparison.OrdinalIgnoreCase))
            return AutomationErrorKind.RestartLimitReached;
        return AutomationErrorKind.Unknown;
    }

    public static bool DefaultRetryable(AutomationErrorKind kind) =>
        kind is AutomationErrorKind.CaptureTimeout or AutomationErrorKind.CaptureFailed;
}

public interface IGameCapture
{
    Bitmap Capture(CancellationToken cancellationToken);
}

public interface IGameInput
{
    void Click(int x, int y, bool right = false);
    void Move(int x, int y);
    void Wheel(int x, int y, int delta);
    void Key(uint key);
}

// Recognition coordinates were authored against a 1520x865 LDPlayer window.
// Every capture is placed in this canvas, including all unsafe pixel scanners.
public static class GameCoordinates
{
    public static readonly Rectangle Viewport = new(3, 34, 1475, 830);

    public static Point ToClient(int x, int y, Size client)
    {
        if (!Viewport.Contains(x, y) || client.Width <= 0 || client.Height <= 0)
            throw new AutomationException($"Input failed: point ({x},{y}) is outside the game viewport.");
        return new Point((int)((long)(x - Viewport.X) * client.Width / Viewport.Width),
            (int)((long)(y - Viewport.Y) * client.Height / Viewport.Height));
    }

    public static unsafe Bitmap Normalize(Bitmap source, Rectangle sourceArea)
    {
        if (sourceArea.Width <= 0 || sourceArea.Height <= 0 ||
            !new Rectangle(Point.Empty, source.Size).Contains(sourceArea))
            throw new AutomationException("Capture failed: render bounds are outside the captured image.");

        var result = new Bitmap(Cst.WINDOW_WIDTH, Cst.WINDOW_HEIGHT, PixelFormat.Format32bppArgb);
        try
        {
            var srcData = source.LockBits(new Rectangle(Point.Empty, source.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var dstData = result.LockBits(new Rectangle(Point.Empty, result.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte* srcBase = (byte*)srcData.Scan0;
                    byte* dstBase = (byte*)dstData.Scan0;
                    int srcStride = srcData.Stride;
                    int dstStride = dstData.Stride;

                    int dstW = Cst.WINDOW_WIDTH;
                    int dstH = Cst.WINDOW_HEIGHT;
                    int vpX = Viewport.X;
                    int vpY = Viewport.Y;
                    int vpW = Viewport.Width;
                    int vpH = Viewport.Height;
                    int vpRight = vpX + vpW;
                    int vpBottom = vpY + vpH;

                    const uint opaqueBlack = 0xFF000000u;

                    // Fill top & bottom borders with opaque black.
                    for (int y = 0; y < vpY; y++)
                    {
                        new Span<uint>(dstBase + (long)y * dstStride, dstW).Fill(opaqueBlack);
                    }
                    for (int y = vpBottom; y < dstH; y++)
                    {
                        new Span<uint>(dstBase + (long)y * dstStride, dstW).Fill(opaqueBlack);
                    }

                    int srcX0 = sourceArea.X;
                    int srcY0 = sourceArea.Y;
                    int srcW = sourceArea.Width;
                    int srcH = sourceArea.Height;
                    int rightBorderW = dstW - vpRight;

                    if (srcW == vpW && srcH == vpH)
                    {
                        long rowCopyBytes = (long)vpW * 4;
                        for (int dy = 0; dy < vpH; dy++)
                        {
                            byte* dstRow = dstBase + (long)(vpY + dy) * dstStride;
                            uint* dstRow32 = (uint*)dstRow;
                            for (int bx = 0; bx < vpX; bx++) dstRow32[bx] = opaqueBlack;
                            for (int bx = vpRight; bx < dstW; bx++) dstRow32[bx] = opaqueBlack;

                            byte* srcRow = srcBase + (long)(srcY0 + dy) * srcStride + (long)srcX0 * 4;
                            Buffer.MemoryCopy(srcRow, dstRow + (long)vpX * 4, rowCopyBytes, rowCopyBytes);
                        }
                    }
                    else
                    {
                        // Matches GDI+ InterpolationMode.NearestNeighbor + PixelOffsetMode.Half:
                        // center (dx + 0.5) maps to floor((dx + 0.5) * srcW / vpW).
                        int* xMap = stackalloc int[vpW];
                        long denomX = 2L * vpW;
                        for (int dx = 0; dx < vpW; dx++)
                        {
                            int sx = (int)(((2L * dx + 1) * srcW) / denomX);
                            if (sx >= srcW) sx = srcW - 1;
                            xMap[dx] = srcX0 + sx;
                        }

                        long denomY = 2L * vpH;
                        for (int dy = 0; dy < vpH; dy++)
                        {
                            int sy = (int)(((2L * dy + 1) * srcH) / denomY);
                            if (sy >= srcH) sy = srcH - 1;

                            uint* srcRow = (uint*)(srcBase + (long)(srcY0 + sy) * srcStride);
                            uint* dstRow = (uint*)(dstBase + (long)(vpY + dy) * dstStride);
                            for (int bx = 0; bx < vpX; bx++) dstRow[bx] = opaqueBlack;

                            uint* dstVp = dstRow + vpX;
                            for (int dx = 0; dx < vpW; dx++)
                            {
                                dstVp[dx] = srcRow[xMap[dx]];
                            }

                            for (int bx = 0; bx < rightBorderW; bx++) dstRow[vpRight + bx] = opaqueBlack;
                        }
                    }
                }
                finally
                {
                    result.UnlockBits(dstData);
                }
            }
            finally
            {
                source.UnlockBits(srcData);
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}

public sealed class PhysicalPixels : IDisposable
{
    private readonly IntPtr previous = WinAPI.SetThreadDpiAwarenessContext(new IntPtr(-4));
    public void Dispose() { if (previous != IntPtr.Zero) WinAPI.SetThreadDpiAwarenessContext(previous); }
}

public sealed class WindowTarget
{
    public IntPtr Root { get; }
    public IntPtr Render { get; }
    private readonly uint processId;

    public WindowTarget(IntPtr root, IntPtr render)
    {
        Root = root;
        Render = render;
        WinAPI.GetWindowThreadProcessId(root, out processId);
        Validate();
    }

    public void Validate()
    {
        WinAPI.GetWindowThreadProcessId(Root, out var rootPid);
        WinAPI.GetWindowThreadProcessId(Render, out var renderPid);
        if (processId == 0 || rootPid != processId || renderPid != processId ||
            !WinAPI.WindowExists(Root) || !WinAPI.WindowExists(Render) || !WinAPI.IsChild(Root, Render))
            throw new AutomationException("Window invalid: selected LDPlayer instance or render window was closed. Stop and start again.");
        if (WinAPI.IsIconic(Root))
            throw new AutomationException("Window invalid: LDPlayer is minimized. Restore it manually, then resume.");
        if (!WinAPI.GetClientRect(Render, out var rect) || rect.Width < 100 || rect.Height < 100 ||
            Math.Abs((double)rect.Width / rect.Height - 16.0 / 9) > 0.025)
            throw new AutomationException("Window invalid: render area is empty or is not landscape 16:9.");
    }
}

public sealed class WindowCapture : IGameCapture
{
    public const int DefaultCaptureTimeoutMs = 2000;
    public const int SlowCaptureWarningMs = 1000;

    private readonly WindowTarget target;
    private readonly int captureTimeoutMs;
    private bool useInner;
    private Task<Bitmap>? pending;
    private readonly object pendingLock = new();

    public bool HasPendingCapture
    {
        get { lock (pendingLock) return pending != null; }
    }

    public WindowCapture(WindowTarget target, bool preferInner, int captureTimeoutMs = DefaultCaptureTimeoutMs)
    {
        if (captureTimeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(captureTimeoutMs));
        this.target = target;
        this.useInner = preferInner;
        this.captureTimeoutMs = captureTimeoutMs;
    }

    public Bitmap Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dpi = new PhysicalPixels();
        target.Validate();
        return ExecuteSingleFlightCapture(CaptureCore, captureTimeoutMs, cancellationToken, "PrintWindow");
    }

    internal Bitmap ExecuteSingleFlightCapture(Func<Bitmap> captureFunc, int timeoutMs, CancellationToken cancellationToken, string operationName)
    {
        // PrintWindow is synchronous. Never accumulate workers if a driver stops replying.
        lock (pendingLock)
        {
            if (pending != null)
            {
                throw new AutomationException(
                    AutomationErrorKind.CaptureTimeout,
                    $"Capture failed: previous {operationName} is still running. Wait for LDPlayer to respond, then resume.");
            }
            pending = Task.Run(captureFunc);
        }

        Task<Bitmap> task;
        lock (pendingLock) task = pending!;

        var sw = Stopwatch.StartNew();
        try
        {
            var result = task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken).GetAwaiter().GetResult();
            lock (pendingLock)
            {
                if (ReferenceEquals(pending, task)) pending = null;
            }
            if (sw.ElapsedMilliseconds >= SlowCaptureWarningMs)
                Log.W($"Slow capture: {operationName} took {sw.ElapsedMilliseconds} ms (limit={timeoutMs} ms).");
            return result;
        }
        catch (TimeoutException)
        {
            DisposeLateResult(task, operationName, sw);
            throw new AutomationException(
                AutomationErrorKind.CaptureTimeout,
                $"Capture failed: {operationName} exceeded {timeoutMs} ms (elapsed={sw.ElapsedMilliseconds} ms).");
        }
        catch (OperationCanceledException)
        {
            DisposeLateResult(task, operationName, sw);
            throw;
        }
        catch
        {
            lock (pendingLock)
            {
                if (ReferenceEquals(pending, task)) pending = null;
            }
            throw;
        }
    }

    private void DisposeLateResult(Task<Bitmap> task, string operationName, Stopwatch sw) => _ = task.ContinueWith(t =>
    {
        if (t.Status == TaskStatus.RanToCompletion)
        {
            try { t.Result.Dispose(); } catch { }
            Log.W($"Late {operationName} finished after {sw.ElapsedMilliseconds} ms; late bitmap disposed.");
        }
        else
        {
            _ = t.Exception;
            Log.W($"Late {operationName} ended with status {t.Status} after {sw.ElapsedMilliseconds} ms.");
        }
        lock (pendingLock)
            if (ReferenceEquals(pending, task)) pending = null;
    }, TaskScheduler.Default);

    private Bitmap CaptureCore()
    {
        using var dpi = new PhysicalPixels();
        target.Validate();
        if (!useInner)
        {
            try
            {
                using var outer = CaptureRaw(target.Root, 2);
                WinAPI.GetWindowRect(target.Root, out var root);
                WinAPI.GetClientRect(target.Render, out var client);
                var origin = new WinAPI.Point();
                if (!WinAPI.ClientToScreen(target.Render, ref origin))
                    throw new AutomationException("Capture failed: cannot locate render origin.");
                var normalized = GameCoordinates.Normalize(outer,
                    new Rectangle(origin.X - root.Left, origin.Y - root.Top, client.Width, client.Height));
                if (!FrameHealth.IsBlank(normalized)) return normalized;
                normalized.Dispose();
            }
            catch (AutomationException e) { Log.W(e.Message); }
            useInner = true;
            Log.W("Capture failed: outer capture unavailable/blank; switching to inner render capture.");
        }
        using var inner = CaptureRaw(target.Render, 3); // PW_CLIENTONLY | PW_RENDERFULLCONTENT
        var result = GameCoordinates.Normalize(inner, new Rectangle(Point.Empty, inner.Size));
        if (!FrameHealth.IsBlank(result)) return result;
        result.Dispose();
        throw new AutomationException("Capture failed: inner render frame is blank/black. Check LDPlayer renderer.");
    }

    public static Bitmap CaptureRaw(IntPtr hwnd, int flags)
    {
        using var dpi = new PhysicalPixels();
        if (!WinAPI.WindowExists(hwnd)) throw new AutomationException("Window invalid: capture HWND no longer exists.");
        WinAPI.RECT rect;
        bool found = (flags & 1) != 0 ? WinAPI.GetClientRect(hwnd, out rect) : WinAPI.GetWindowRect(hwnd, out rect);
        if (!found || rect.Width <= 0 || rect.Height <= 0 || rect.Width > 8192 || rect.Height > 8192)
            throw new AutomationException("Capture failed: invalid image dimensions.");
        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black); // Never reuse yesterday's pixels on a partial render.
            var hdc = graphics.GetHdc();
            try
            {
                if (!WinAPI.PrintWindow(hwnd, hdc, flags))
                    throw new AutomationException("Capture failed: PrintWindow returned false.");
            }
            finally { graphics.ReleaseHdc(hdc); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}

public sealed class BackgroundInput(WindowTarget target, Action checkpoint) : IGameInput
{
    public void Click(int x, int y, bool right = false)
    {
        checkpoint();
        using var dpi = new PhysicalPixels();
        var point = Map(x, y);
        try { Send(right ? WinAPI.WM_RBUTTONDOWN : WinAPI.WM_LBUTTONDOWN, right ? 2 : 1, Pack(point.X, point.Y)); }
        // Attempt release even after ambiguous delivery; never replay a down.
        finally { Send(right ? WinAPI.WM_RBUTTONUP : WinAPI.WM_LBUTTONUP, 0, Pack(point.X, point.Y)); }
    }

    public void Move(int x, int y)
    {
        checkpoint();
        using var dpi = new PhysicalPixels();
        var point = Map(x, y);
        Send(WinAPI.WM_MOUSEMOVE, 0, Pack(point.X, point.Y));
    }

    public void Wheel(int x, int y, int delta)
    {
        checkpoint();
        using var dpi = new PhysicalPixels();
        var point = Map(x, y);
        var screen = new WinAPI.Point { X = point.X, Y = point.Y };
        if (!WinAPI.ClientToScreen(target.Render, ref screen))
            throw new AutomationException("Input failed: cannot map wheel coordinates to screen.");
        Send(WinAPI.WM_MOUSEWHEEL, Pack(0, delta), Pack(screen.X, screen.Y));
    }

    public void Key(uint key)
    {
        checkpoint();
        target.Validate();
        long scan = WinAPI.MapVirtualKey(key, 0);
        var down = (IntPtr)(1L | scan << 16);
        try { Send(WinAPI.WM_KEYDOWN, (IntPtr)key, down); }
        finally { Send(WinAPI.WM_KEYUP, (IntPtr)key, (IntPtr)(down.ToInt64() | 0xC0000000L)); }
    }

    private Point Map(int x, int y)
    {
        target.Validate();
        WinAPI.GetClientRect(target.Render, out var client);
        return GameCoordinates.ToClient(x, y, new Size(client.Width, client.Height));
    }

    public static IntPtr Pack(int x, int y) => (IntPtr)unchecked((int)(((uint)y & 0xffff) << 16 | ((uint)x & 0xffff)));

    private void Send(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (WinAPI.SendMessageTimeout(target.Render, message, wParam, lParam, 0x22, 500, out _) == IntPtr.Zero)
            throw new AutomationException($"Input failed: message 0x{message:X} timed out or window closed. Input was not retried.");
    }
}

public sealed class FrameHealth
{
    private byte[]? hash;
    private bool inputSinceChange;
    private TimeSpan firstInputAt;

    public void Reset() { hash = null; inputSinceChange = false; }
    public void InputSent(TimeSpan now)
    {
        if (!inputSinceChange) firstInputAt = now;
        inputSinceChange = true;
    }

    public void Observe(Bitmap frame, TimeSpan now)
    {
        if (frame.Size != new Size(Cst.WINDOW_WIDTH, Cst.WINDOW_HEIGHT) || IsBlank(frame))
            throw new AutomationException("Capture failed: blank frame or unexpected normalized dimensions.");
        var next = Utils.BmpHash(frame);
        if (hash == null || !hash.AsSpan().SequenceEqual(next))
        {
            hash = next;
            inputSinceChange = false;
        }
        else if (inputSinceChange && now - firstInputAt >= TimeSpan.FromSeconds(15))
            throw new AutomationException("Stale frame: unchanged for 15s despite input; capture or game may be frozen.");
    }

    public static unsafe bool IsBlank(Bitmap bitmap)
    {
        if (!new Rectangle(Point.Empty, bitmap.Size).Contains(GameCoordinates.Viewport)) return true;
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte* scan0 = (byte*)data.Scan0;
            int stride = data.Stride;
            byte* first = scan0 + 53L * stride + 42 * 4;
            int firstB = first[0];
            int firstG = first[1];
            int firstR = first[2];

            for (int y = 53; y < 820; y += 23)
            {
                byte* row = scan0 + (long)y * stride;
                for (int x = 42; x < 1449; x += 23)
                {
                    byte* px = row + x * 4;
                    if (Math.Abs(px[2] - firstR) > 3 || Math.Abs(px[1] - firstG) > 3 || Math.Abs(px[0] - firstB) > 3)
                        return false;
                }
            }
            return true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
