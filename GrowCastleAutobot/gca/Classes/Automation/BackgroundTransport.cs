using gca.Script;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace gca.Classes.Automation;

public sealed class AutomationException(string message) : Exception(message);

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

    public static Bitmap Normalize(Bitmap source, Rectangle sourceArea)
    {
        if (sourceArea.Width <= 0 || sourceArea.Height <= 0 ||
            !new Rectangle(Point.Empty, source.Size).Contains(sourceArea))
            throw new AutomationException("Capture failed: render bounds are outside the captured image.");
        var result = new Bitmap(Cst.WINDOW_WIDTH, Cst.WINDOW_HEIGHT, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(source, Viewport, sourceArea, GraphicsUnit.Pixel);
        return result;
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

public sealed class WindowCapture(WindowTarget target, bool preferInner) : IGameCapture
{
    private bool useInner = preferInner;
    private Task<Bitmap>? pending;
    private readonly object pendingLock = new();

    public Bitmap Capture(CancellationToken cancellationToken)
    {
        using var dpi = new PhysicalPixels();
        target.Validate();
        // PrintWindow is synchronous. Never accumulate workers if a driver stops replying.
        lock (pendingLock)
        {
            if (pending != null)
                throw new AutomationException("Capture failed: previous PrintWindow is still running. Wait for LDPlayer to respond, then resume.");
        }
        var task = Task.Run(() => CaptureCore());
        lock (pendingLock) pending = task;
        try
        {
            var result = task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).GetAwaiter().GetResult();
            lock (pendingLock) pending = null;
            return result;
        }
        catch (TimeoutException)
        {
            DisposeLateResult(task);
            throw new AutomationException("Capture failed: PrintWindow exceeded 2000 ms.");
        }
        catch (OperationCanceledException) { DisposeLateResult(task); throw; }
        catch { lock (pendingLock) pending = null; throw; }
    }

    private void DisposeLateResult(Task<Bitmap> task) => _ = task.ContinueWith(t =>
    {
        if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose();
        else _ = t.Exception;
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

    public static bool IsBlank(Bitmap bitmap)
    {
        if (!new Rectangle(Point.Empty, bitmap.Size).Contains(GameCoordinates.Viewport)) return true;
        var first = bitmap.GetPixel(42, 53);
        for (int y = 53; y < 820; y += 23)
            for (int x = 42; x < 1449; x += 23)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (Math.Abs(pixel.R - first.R) > 3 || Math.Abs(pixel.G - first.G) > 3 || Math.Abs(pixel.B - first.B) > 3)
                    return false;
            }
        return true;
    }
}
