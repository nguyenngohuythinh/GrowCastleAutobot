using gca;
using gca.Classes;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using gca.Classes.Automation;
using gca.Classes.SettingsScripts;
using System.Diagnostics;
using System.Text.Json;
using System.Reflection;

// Keep test output away from the user's settings and logs.
Directory.CreateDirectory("artifacts/run");
Directory.SetCurrentDirectory(Path.GetFullPath("artifacts/run"));

if (args.Length >= 2 && args[0] == "--live-replay")
{
    using var dpi = new PhysicalPixels();
    var root = WinAPI.FindWindow(null!, args[1]);
    var render = WinAPI.FindChildWindowByClass(root, "RenderWindow");
    if (WinAPI.GetForegroundWindow() == root) throw new Exception("Cover LDPlayer before running this test.");
    var bot = new Autobot();
    void Set(string name, object value) => typeof(Autobot).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(bot,value);
    Set("hwnd",root); Set("renderHwnd",render); Set("backgroundMode",true);
    typeof(Autobot).GetMethod("InitializeBackground",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bot,new object[]{true});
    Set("stopRequested",false);
    Set("thisChronoSlot",-1);
    Set("screenshotPopups",false);
    ((ManualResetEvent)typeof(Autobot).GetField("stopWaitHandle",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bot)!).Reset();
    var timer = Stopwatch.StartNew();
    int completed = 0;
    bool started = false;
    while (timer.Elapsed < TimeSpan.FromMinutes(4) && completed < 2)
    {
        if (WinAPI.GetForegroundWindow() == root) throw new Exception("LDPlayer became foreground; stop live test.");
        if (bot.CaptchaOnScreen()) { Console.WriteLine("CAPTCHA appeared: stopping live test for manual handling."); return; }
        if (!bot.CheckSky() && !bot.WaitUntil(() => bot.CheckSky(), () => { }, 4000, 100))
            throw new Exception("Unknown game state after 4s; no recovery input sent.");
        if (bot.CheckGCMenu(false))
        {
            if (started) { completed++; Console.WriteLine($"Replay completed {completed}, elapsed={timer.Elapsed.TotalSeconds:F1}s"); }
            if (completed == 2) break;
            Console.WriteLine("Starting production PerformReplay while LDPlayer is covered.");
            WinAPI.GetCursorPos(out var before);
            bot.PerformReplay();
            WinAPI.GetCursorPos(out var after);
            Console.WriteLine($"Cursor sample before={before.X},{before.Y} after={after.X},{after.Y} (user may move it).");
            if (bot.CheckGCMenu()) throw new Exception("Replay input was not processed.");
            started = true;
        }
        Thread.Sleep(250);
    }
    if (completed != 2) throw new Exception($"Only {completed} replay cycles completed before timeout.");
    Console.WriteLine("PASS two covered production replay cycles; LDPlayer never observed foreground.");
    return;
}

if (args.Length >= 2 && args[0] == "--live-capture")
{
    using var dpi = new PhysicalPixels();
    var root = WinAPI.FindWindow(null!, args[1]);
    var render = WinAPI.FindChildWindowByClass(root, "RenderWindow");
    var capture = new WindowCapture(new WindowTarget(root, render), false);
    var foreground = WinAPI.GetForegroundWindow();
    Directory.CreateDirectory("artifacts");
    byte[]? previous = null;
    int changed = 0;
    var elapsed = new List<double>();
    for (int i = 0; i < 20; i++)
    {
        var timer = Stopwatch.StartNew();
        using var frame = capture.Capture(CancellationToken.None);
        elapsed.Add(timer.Elapsed.TotalMilliseconds);
        var hash = Utils.BmpHash(frame);
        if (previous != null && !previous.SequenceEqual(hash)) changed++;
        previous = hash;
        if (i == 0 || i == 19) frame.Save($"artifacts/live-{i}.png", ImageFormat.Png);
        if (foreground != root && WinAPI.GetForegroundWindow() == root) throw new Exception("LDPlayer became foreground during capture");
        Thread.Sleep(100);
    }
    Console.WriteLine($"Captured 20 frames; changed={changed}; average={elapsed.Average():F1}ms max={elapsed.Max():F1}ms; LDPlayer not activated.");
    if (changed == 0) throw new Exception("No changing frames observed");
    return;
}

if (args.Length == 2 && args[0] == "--capture")
{
    WinAPI.SetThreadDpiAwarenessContext(new IntPtr(-4));
    var root = WinAPI.FindWindow(null!, args[1]);
    var render = WinAPI.FindChildWindowByClass(root, "RenderWindow");
    Console.WriteLine($"root={root} render={render}");
    Directory.CreateDirectory("artifacts");
    foreach (var target in new[] { ("outer", root), ("inner", render) })
    {
        WinAPI.GetWindowRect(target.Item2, out var bounds);
        Console.WriteLine($"{target.Item1}: {bounds.Left},{bounds.Top} {bounds.Width}x{bounds.Height}");
        foreach (int flag in new[] { 0, 2 })
        {
            var bot = new Autobot { CaptureFlags = flag };
            using var frame = bot.CaptureWindow(target.Item2);
            frame.Save($"artifacts/{target.Item1}-{flag}.png", ImageFormat.Png);
            Console.WriteLine($"flag={flag} hash={Utils.BmpHash(frame)}");
        }
    }
    return;
}
int passed = 0;
void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
    Console.WriteLine("PASS " + message);
    passed++;
}
void Fails(Action action, string category)
{
    try { action(); }
    catch (AutomationException e) { Check(e.Message.StartsWith(category), category); return; }
    throw new Exception("Expected " + category);
}

Check(GameCoordinates.ToClient(3, 34, new Size(1600, 900)) == Point.Empty, "viewport origin maps to Android/render origin");
Check(GameCoordinates.ToClient(1477, 863, new Size(1475, 830)) == new Point(1474,829), "last pixel maps inside render");
Check(GameCoordinates.ToClient(740, 449, new Size(2950, 1660)) == new Point(1474,830), "scaled render input");
Fails(() => GameCoordinates.ToClient(2, 34, new Size(1600,900)), "Input failed");
Fails(() => GameCoordinates.ToClient(1478, 863, new Size(1600,900)), "Input failed");
Check(BackgroundInput.Pack(-100, -200).ToInt32() == unchecked((int)0xff38ff9c), "signed multi-monitor coordinates");
using var source = new Bitmap(1475,830,PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(source)) { g.Clear(Color.CornflowerBlue); g.FillRectangle(Brushes.Red, 200,200,200,200); }
using var normalized = GameCoordinates.Normalize(source, new Rectangle(Point.Empty,source.Size));
Check(normalized.Size == new Size(1520,865), "all recognition paths share canonical dimensions");
Check(normalized.GetPixel(203,234).ToArgb() == Color.Red.ToArgb(), "pixel and region scanner offsets agree");
Check(normalized.GetPixel(3,34).ToArgb() == Color.CornflowerBlue.ToArgb(), "normalization preserves exact colors");
Check(!FrameHealth.IsBlank(normalized), "game frame accepted");
using var blank = new Bitmap(1520,865);
Check(FrameHealth.IsBlank(blank), "black capture rejected");
using var tiny = new Bitmap(10,10);
Check(FrameHealth.IsBlank(tiny), "undersized frame rejected before pixel access");
var health = new FrameHealth();
health.Observe(normalized,TimeSpan.Zero);
health.Observe(normalized,TimeSpan.FromSeconds(30));
Check(true,"static idle menu alone is not proof of a stale frame");
health.InputSent(TimeSpan.FromSeconds(30));
health.Observe(normalized,TimeSpan.FromSeconds(31));
Check(true,"first input after idle gets a full response interval");
Fails(() => health.Observe(normalized,TimeSpan.FromSeconds(46)), "Stale frame");
health.Reset();
health.Observe(normalized,TimeSpan.FromSeconds(32));
health.InputSent(TimeSpan.FromSeconds(32));
using (var g = Graphics.FromImage(normalized)) g.FillRectangle(Brushes.Lime,480,480,40,40);
health.Observe(normalized,TimeSpan.FromSeconds(50));
Check(true,"changing frame clears stale history");
Fails(() => health.Observe(blank,TimeSpan.FromSeconds(51)), "Capture failed");
var settings = JsonSerializer.Deserialize<ClickerSettings>(JsonSerializer.Serialize(new ClickerSettings { BackgroundMode=true, CaptureInnerWindow=true }));
Check(settings!.BackgroundMode && settings.CaptureInnerWindow,"JSON round trip retains background options");
Check(JsonSerializer.Deserialize<ClickerSettings>("{}")!.BackgroundMode,"old JSON defaults remain compatible");
Fails(() => new WindowTarget(IntPtr.Zero,IntPtr.Zero),"Window invalid");

// Run the actual worker with a failed capture backend. It must pause before any
// recovery input, then Stop must wake the paused worker promptly.
var faultBot = new Autobot();
void SetField(Autobot bot, string name, object value) => typeof(Autobot)
    .GetField(name,BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bot,value);
SetField(faultBot,"backgroundMode",true);
SetField(faultBot,"stopRequested",false);
SetField(faultBot,"gameCapture",new FailedCapture());
SetField(faultBot,"waitBetweenBattlesRuntimes",new List<WaitBetweenBattlesRuntime>());
typeof(Autobot).GetProperty(nameof(Autobot.IsActive))!.SetValue(faultBot,true);
typeof(Autobot).GetProperty(nameof(Autobot.IsRunning))!.SetValue(faultBot,true);
using var paused = new ManualResetEventSlim();
faultBot.OnFailed += _ => paused.Set();
var worker = new Thread(() => typeof(Autobot).GetMethod("WorkerLoop",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(faultBot,null));
worker.Start();
Check(paused.Wait(3000),"capture recovery exhausted then worker pauses");
Check(faultBot.IsPaused && faultBot.IsActive && !faultBot.IsRunning,"fault pause keeps Stop/Resume available");
var stopTime = Stopwatch.StartNew();
faultBot.Stop();
Check(worker.Join(1500),"Stop wakes paused worker");
Check(!faultBot.IsActive && !faultBot.IsRunning && stopTime.ElapsedMilliseconds < 1500,"Stop completes without restart");
typeof(Autobot).GetMethod("ResetAutomationSession",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(faultBot,null);
var freshIo = (CancellationTokenSource)typeof(Autobot).GetField("ioCancellation",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(faultBot)!;
Check(!freshIo.IsCancellationRequested,"a stopped bot gets a fresh I/O session before restart");
Fails(() => faultBot.LeftClick(100,100),"Input failed");
Fails(() => faultBot.RightClick(100,100),"Input failed");
Fails(() => faultBot.ScrollWheel(120),"Input failed");
Fails(() => faultBot.EscClickStart(),"Game state unknown");
Fails(() => faultBot.Restart(),"Game state unknown");
Fails(() => faultBot.Reset(),"Game state unknown");

// A hidden, owned Win32 receiver verifies actual message payloads without sending
// user input to any application or moving the physical cursor.
using (var dpi = new PhysicalPixels())
using (var receiver = new InputReceiver())
{
    var foreground = WinAPI.GetForegroundWindow();
    WinAPI.GetCursorPos(out var cursor);
    int checkpoints = 0;
    var input = new BackgroundInput(new WindowTarget(receiver.Root,receiver.Handle),() => checkpoints++);
    input.Click(203,234);
    Check(receiver.Messages.Select(m=>m.Msg).SequenceEqual(new[]{0x201,0x202}),"left down/up delivered only to selected receiver");
    Check(receiver.Messages[0].LParam == BackgroundInput.Pack(200,200),"click is mapped to render client coordinates");
    receiver.Messages.Clear();
    input.Click(203,234,true);
    Check(receiver.Messages.Select(m=>m.Msg).SequenceEqual(new[]{0x204,0x205}),"right down/up delivered");
    receiver.Messages.Clear();
    input.Move(203,234);
    input.Wheel(203,234,-120);
    var screen = new WinAPI.Point { X=200,Y=200 };
    WinAPI.ClientToScreen(receiver.Handle,ref screen);
    Check(receiver.Messages[1].LParam == BackgroundInput.Pack(screen.X,screen.Y),"wheel uses screen coordinates");
    Check(receiver.Messages[1].WParam == BackgroundInput.Pack(0,-120),"wheel preserves signed delta");
    receiver.Messages.Clear();
    input.Key(0x41);
    Check(receiver.Messages.Select(m=>m.Msg).SequenceEqual(new[]{0x100,0x101}),"key down/up delivered without focus");
    Check((receiver.Messages[1].LParam.ToInt64() & 0xC0000000L) == 0xC0000000L,"key-up transition bits are set");
    WinAPI.GetCursorPos(out var after);
    Check(foreground == WinAPI.GetForegroundWindow() && cursor.X==after.X && cursor.Y==after.Y,"transport leaves foreground and cursor unchanged");
    Check(checkpoints == 5,"pause/stop checkpoint precedes every input action");
    var canceled = new BackgroundInput(new WindowTarget(receiver.Root,receiver.Handle),() => throw new OperationCanceledException());
    receiver.Messages.Clear();
    try { canceled.Click(203,234); } catch (OperationCanceledException) { }
    Check(receiver.Messages.Count==0,"canceled input sends no messages");
}

Console.WriteLine($"{passed} assertions passed.");

sealed class FailedCapture : IGameCapture
{
    public Bitmap Capture(CancellationToken cancellationToken) => throw new AutomationException("Capture failed: injected test failure.");
}

sealed class InputReceiver : System.Windows.Forms.NativeWindow, IDisposable
{
    private readonly System.Windows.Forms.NativeWindow root = new();
    public IntPtr Root => root.Handle;
    public List<(int Msg, IntPtr WParam, IntPtr LParam)> Messages { get; } = new();
    public InputReceiver()
    {
        root.CreateHandle(new System.Windows.Forms.CreateParams { Caption="GCA hidden test",X=50,Y=70,Width=1520,Height=865 });
        CreateHandle(new System.Windows.Forms.CreateParams { Parent=root.Handle,Style=0x40000000,Width=1475,Height=830 });
    }
    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        if (m.Msg is 0x100 or 0x101 or 0x200 or 0x201 or 0x202 or 0x204 or 0x205 or 0x20a)
        {
            Messages.Add((m.Msg,m.WParam,m.LParam));
            m.Result=IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }
    public void Dispose() { DestroyHandle(); root.DestroyHandle(); }
}
