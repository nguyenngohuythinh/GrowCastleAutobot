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
if (args.Length > 0 && args[0] == "--long-replay")
{
    Environment.ExitCode = LongReplay.Run(args);
    return;
}
Directory.CreateDirectory("artifacts/run");
Directory.SetCurrentDirectory(Path.GetFullPath("artifacts/run"));

if (args.Length > 0 && args[0] == "--benchmark")
{
    int iterations = args.Length >= 2 && int.TryParse(args[1], out int n) && n > 0 ? n : 100;
    using var raw1600 = new Bitmap(1600, 900, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(raw1600))
    {
        g.Clear(Color.FromArgb(255, 80, 140, 220));
        for (int i = 0; i < 16; i++)
            g.FillRectangle(i % 2 == 0 ? Brushes.OrangeRed : Brushes.ForestGreen, i * 95 + 10, (i * 47) % 700 + 20, 80, 120);
    }
    using var norm = GameCoordinates.Normalize(raw1600, new Rectangle(Point.Empty, raw1600.Size));
    using var blankFrame = new Bitmap(1520, 865, PixelFormat.Format32bppArgb);
    var rawAdb = new byte[16 + 1600 * 900 * 4];
    BitConverter.GetBytes(1600).CopyTo(rawAdb, 0);
    BitConverter.GetBytes(900).CopyTo(rawAdb, 4);
    BitConverter.GetBytes(1).CopyTo(rawAdb, 8);
    for (int i = 16; i < rawAdb.Length; i += 4) { rawAdb[i] = 120; rawAdb[i + 1] = 160; rawAdb[i + 2] = 200; rawAdb[i + 3] = 255; }

    void Measure(string name, int count, Action body)
    {
        body(); // warmup
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++) body();
        sw.Stop();
        long allocAfter = GC.GetTotalAllocatedBytes(precise: true);
        double avgMs = sw.Elapsed.TotalMilliseconds / count;
        double kbPerOp = (allocAfter - allocBefore) / 1024.0 / count;
        Console.WriteLine($"BENCH {name,-32} | n={count,3} | avg={avgMs,7:F3} ms | total={sw.Elapsed.TotalMilliseconds,8:F1} ms | alloc={kbPerOp,8:F1} KB/op | GC={GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2}");
    }

    Measure("Utils.BmpHash(1520x865)", iterations, () => { _ = Utils.BmpHash(norm); });
    Measure("FrameHealth.IsBlank(game)", iterations * 2, () => { _ = FrameHealth.IsBlank(norm); });
    Measure("FrameHealth.IsBlank(blank)", iterations * 2, () => { _ = FrameHealth.IsBlank(blankFrame); });
    Measure("GameCoordinates.Normalize", iterations, () => { using var b = GameCoordinates.Normalize(raw1600, new Rectangle(Point.Empty, raw1600.Size)); });
    Measure("ScreenshotCache.CompressJpeg", Math.Max(10, iterations / 2), () => { _ = ScreenshotCache.CompressToJpeg(norm, 10); });
    Measure("AdbCapture.DecodeRawScreencap", Math.Max(10, iterations / 2), () => { using var b = AdbCapture.DecodeRawScreencap(rawAdb); });
    return;
}

if (args.Length == 2 && args[0] == "--live-adb-capture")
{
    var dir = AdbTarget.FindLdPlayerDirectory(WinAPI.FindWindow(null!, args[1]))
        ?? throw new Exception("LDPlayer directory not found.");
    string ldconsole = Path.Combine(dir, "ldconsole.exe");
    string adb = Path.Combine(dir, "adb.exe");
    var psi = new ProcessStartInfo(ldconsole, "list2") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
    using var proc = Process.Start(psi)!;
    string list2 = proc.StandardOutput.ReadToEnd();
    proc.WaitForExit();
    var match = list2.Split('\n').Select(l => l.Trim().Split(',')).FirstOrDefault(p => p.Length == 10 && p[1] == args[1])
        ?? throw new Exception($"Instance '{args[1]}' not found in ldconsole list2.");
    int idx = int.Parse(match[0]);
    string serial = AdbTarget.ResolveSerial(adb, idx);
    var target = new AdbTarget(adb, serial, idx);
    var capture = new AdbCapture(target);
    Directory.CreateDirectory("artifacts");
    var elapsed = new List<double>();
    byte[]? prev = null;
    int changed = 0;
    for (int i = 0; i < 10; i++)
    {
        var sw = Stopwatch.StartNew();
        using var frame = capture.Capture(CancellationToken.None);
        elapsed.Add(sw.Elapsed.TotalMilliseconds);
        var hash = Utils.BmpHash(frame);
        if (prev != null && !prev.SequenceEqual(hash)) changed++;
        prev = hash;
        if (i == 0 || i == 9) frame.Save($"artifacts/adb-live-{i}.png", ImageFormat.Png);
        if (i == 0)
        {
            var bot = new Autobot();
            typeof(Autobot).GetField("currentScreen", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bot, frame);
            Console.WriteLine($"ADB frame0 {frame.Width}x{frame.Height} (raw {target.ScreenSize.Width}x{target.ScreenSize.Height}) serial={serial}; sky={bot.CheckSky(false)}; menu={bot.CheckGCMenu(false)}");
        }
        Thread.Sleep(100);
    }
    Console.WriteLine($"ADB captured 10 frames; changed={changed}; average={elapsed.Average():F1}ms max={elapsed.Max():F1}ms.");
    return;
}

if (args.Length == 2 && args[0] == "--live-adb-replay")
{
    var dir = AdbTarget.FindLdPlayerDirectory(WinAPI.FindWindow(null!, args[1]))
        ?? throw new Exception("LDPlayer directory not found.");
    string ldconsole = Path.Combine(dir, "ldconsole.exe");
    string adb = Path.Combine(dir, "adb.exe");
    var psi = new ProcessStartInfo(ldconsole, "list2") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
    using var proc = Process.Start(psi)!;
    string list2 = proc.StandardOutput.ReadToEnd();
    proc.WaitForExit();
    var match = list2.Split('\n').Select(l => l.Trim().Split(',')).FirstOrDefault(p => p.Length == 10 && p[1] == args[1])
        ?? throw new Exception($"Instance '{args[1]}' not found in ldconsole list2.");
    int idx = int.Parse(match[0]);
    string serial = AdbTarget.ResolveSerial(adb, idx);
    var target = new AdbTarget(adb, serial, idx);
    using var adbInput = new AdbInput(target, () => { });
    var bot = new Autobot();
    void Set(string name, object value) => typeof(Autobot).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(bot, value);
    Set("backgroundMode", true); Set("useAdb", true); Set("adbTarget", target);
    Set("gameCapture", new AdbCapture(target)); Set("gameInput", adbInput);
    Set("stopRequested", false); Set("thisChronoSlot", -1); Set("screenshotPopups", false);
    ((ManualResetEvent)typeof(Autobot).GetField("stopWaitHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bot)!).Reset();
    if (!bot.CheckGCMenu()) throw new Exception("Expected Grow Castle menu before ADB replay test.");
    var fgBefore = WinAPI.GetForegroundWindow();
    WinAPI.GetCursorPos(out var curBefore);
    bot.PerformReplay();
    WinAPI.GetCursorPos(out var curAfter);
    if (WinAPI.GetForegroundWindow() != fgBefore) throw new Exception("Foreground changed during ADB replay.");
    if (curBefore.X != curAfter.X || curBefore.Y != curAfter.Y) throw new Exception("Cursor moved during ADB replay.");
    if (bot.CheckGCMenu()) throw new Exception("Game did not leave menu after ADB replay tap.");
    Console.WriteLine($"PASS ADB replay input on {serial}: menu changed, foreground and cursor unchanged.");
    return;
}

if (args.Length >= 2 && args[0] == "--live-adb-cycles")
{
    int targetCycles = args.Length >= 3 && int.TryParse(args[2], out int c) && c > 0 ? c : 2;
    var dir = AdbTarget.FindLdPlayerDirectory(WinAPI.FindWindow(null!, args[1]))
        ?? throw new Exception("LDPlayer directory not found.");
    string ldconsole = Path.Combine(dir, "ldconsole.exe");
    string adb = Path.Combine(dir, "adb.exe");
    var psi = new ProcessStartInfo(ldconsole, "list2") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
    using var proc = Process.Start(psi)!;
    string list2 = proc.StandardOutput.ReadToEnd();
    proc.WaitForExit();
    var match = list2.Split('\n').Select(l => l.Trim().Split(',')).FirstOrDefault(p => p.Length == 10 && p[1] == args[1])
        ?? throw new Exception($"Instance '{args[1]}' not found in ldconsole list2.");
    int idx = int.Parse(match[0]);
    string serial = AdbTarget.ResolveSerial(adb, idx);
    var target = new AdbTarget(adb, serial, idx);
    var bot = new Autobot();
    void Set(string name, object value) => typeof(Autobot).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(bot, value);
    using var adbInput = new AdbInput(target, () => { });
    Set("backgroundMode", true); Set("useAdb", true); Set("adbTarget", target);
    Set("gameCapture", new AdbCapture(target)); Set("gameInput", adbInput);
    Set("stopRequested", false); Set("thisChronoSlot", -1); Set("screenshotPopups", false);
    ((ManualResetEvent)typeof(Autobot).GetField("stopWaitHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bot)!).Reset();

    var timer = Stopwatch.StartNew();
    var battleTimer = new Stopwatch();
    int completed = 0;
    int battleFrames = 0;
    bool started = false;
    var fgInitial = WinAPI.GetForegroundWindow();
    Console.WriteLine($"Starting live ADB replay cycles test: instance='{args[1]}', serial={serial}, targetCycles={targetCycles}.");
    while (timer.Elapsed < TimeSpan.FromMinutes(6) && completed < targetCycles)
    {
        if (bot.CaptchaOnScreen()) { Console.WriteLine("CAPTCHA appeared: stopping live ADB test for manual handling."); return; }
        if (!bot.CheckSky(false) && !bot.WaitUntil(() => bot.CheckSky(), () => { }, 4000, 100))
            throw new Exception("Unknown game state after 4s; stopping safely without recovery input.");
        if (bot.CheckGCMenu(false))
        {
            if (started)
            {
                if (battleFrames < 3 || battleTimer.Elapsed < TimeSpan.FromSeconds(5))
                    throw new Exception($"Replay returned to menu too quickly ({battleTimer.Elapsed.TotalSeconds:F1}s, battleFrames={battleFrames}); battle may not have started.");
                completed++;
                Console.WriteLine($"ADB Replay cycle {completed}/{targetCycles} completed at {timer.Elapsed.TotalSeconds:F1}s (battle={battleTimer.Elapsed.TotalSeconds:F1}s, battleFrames={battleFrames}, retries={bot.CaptureRetryTracker.TotalRetries}).");
                started = false;
                battleFrames = 0;
                battleTimer.Reset();
                if (completed >= targetCycles) break;
            }
            var fgBefore = WinAPI.GetForegroundWindow();
            WinAPI.GetCursorPos(out var curBefore);
            bot.PerformReplay();
            WinAPI.GetCursorPos(out var curAfter);
            if (WinAPI.GetForegroundWindow() != fgBefore) throw new Exception("Foreground changed during ADB replay.");
            if (curBefore.X != curAfter.X || curBefore.Y != curAfter.Y) throw new Exception("Cursor moved during ADB replay.");
            if (bot.CheckGCMenu()) throw new Exception("Game did not leave menu after ADB replay tap.");
            started = true;
            battleFrames = 0;
            battleTimer.Restart();
            Console.WriteLine($"ADB Replay cycle {completed + 1}/{targetCycles} started at {timer.Elapsed.TotalSeconds:F1}s.");
        }
        else if (started)
        {
            battleFrames++;
        }
        Thread.Sleep(250);
    }
    if (completed < targetCycles) throw new Exception($"Only {completed}/{targetCycles} ADB replay cycles completed before timeout.");
    Console.WriteLine($"PASS {completed} consecutive ADB replay cycles on {serial} in {timer.Elapsed.TotalSeconds:F1}s; fgInitial={fgInitial}, totalCaptureAttempts={bot.CaptureRetryTracker.TotalAttempts}, retries={bot.CaptureRetryTracker.TotalRetries}.");
    return;
}

if (args.Length == 2 && args[0] == "--recognize-frame")
{
    using var frame = new Bitmap(args[1]);
    var bot = new Autobot();
    typeof(Autobot).GetField("currentScreen", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(bot, frame);
    Console.WriteLine($"Frame {frame.Width}x{frame.Height}; blank={FrameHealth.IsBlank(frame)}; " +
        $"sky={bot.CheckSky(false)}; menu={bot.CheckGCMenu(false)}");
    if (FrameHealth.IsBlank(frame) || !bot.CheckSky(false) || !bot.CheckGCMenu(false))
        throw new Exception("Captured Grow Castle menu was not recognized.");
    return;
}

if (args.Length == 2 && args[0] == "--live-input")
{
    using var dpi = new PhysicalPixels();
    var root = WinAPI.FindWindow(null!, args[1]);
    var render = WinAPI.FindChildWindowByClass(root, "RenderWindow");
    if (root == IntPtr.Zero || WinAPI.GetForegroundWindow() == root)
        throw new Exception("Open and cover the selected LDPlayer before testing input.");
    var bot = new Autobot();
    void Set(string name, object value) => typeof(Autobot)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(bot, value);
    Set("hwnd", root); Set("renderHwnd", render); Set("backgroundMode", true);
    typeof(Autobot).GetMethod("InitializeBackground", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(bot, new object[] { true });
    Set("stopRequested", false); Set("thisChronoSlot", -1); Set("screenshotPopups", false);
    ((ManualResetEvent)typeof(Autobot).GetField("stopWaitHandle", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(bot)!).Reset();
    if (!bot.CheckGCMenu()) throw new Exception("Expected Grow Castle menu before replay input.");
    var foreground = WinAPI.GetForegroundWindow();
    WinAPI.GetCursorPos(out var before);
    bot.PerformReplay();
    WinAPI.GetCursorPos(out var after);
    if (WinAPI.GetForegroundWindow() != foreground || WinAPI.GetForegroundWindow() == root)
        throw new Exception("Foreground changed during background replay input.");
    if (before.X != after.X || before.Y != after.Y)
        throw new Exception("Cursor moved during background replay input; repeat without user movement.");
    if (bot.CheckGCMenu()) throw new Exception("Game did not leave the menu after replay input.");
    Console.WriteLine("PASS covered replay input: menu changed, foreground and cursor unchanged.");
    return;
}

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
var settings = JsonSerializer.Deserialize<ClickerSettings>(JsonSerializer.Serialize(new ClickerSettings { BackgroundMode=true, CaptureInnerWindow=true, UseAdb=true }));
Check(settings!.BackgroundMode && settings.CaptureInnerWindow && settings.UseAdb,"JSON round trip retains background and ADB options");
var defaultSettings = JsonSerializer.Deserialize<ClickerSettings>("{}")!;
Check(defaultSettings.BackgroundMode && !defaultSettings.UseAdb,"old JSON defaults remain compatible");

// ADB transport deterministic checks (serial resolution, raw screencap decoding, key mapping).
Check(AdbTarget.ExpectedSerials(0) == ("emulator-5554", "127.0.0.1:5555"), "ADB index 0 maps to expected LDPlayer ports");
Check(AdbTarget.ExpectedSerials(1) == ("emulator-5556", "127.0.0.1:5557"), "ADB index 1 maps to expected LDPlayer ports");
var parsedDevices = AdbTarget.ParseDevices("* daemon started successfully\nList of devices attached\nemulator-5554\tdevice\n127.0.0.1:5557\toffline\n");
Check(parsedDevices.Count == 2 && parsedDevices["emulator-5554"] == "device" && parsedDevices["127.0.0.1:5557"] == "offline", "ADB devices output parsed accurately");
{
    // 16-byte Android 8+ header with RGBA_8888 (format=1), 1600x900
    int w = 1600, h = 900;
    var raw16 = new byte[16 + w * h * 4];
    BitConverter.GetBytes(w).CopyTo(raw16, 0);
    BitConverter.GetBytes(h).CopyTo(raw16, 4);
    BitConverter.GetBytes(1).CopyTo(raw16, 8);
    // Fill every pixel with RGBA(10, 20, 30, 128) -> expect opaque RGB(10, 20, 30)
    for (int i = 16; i < raw16.Length; i += 4) { raw16[i] = 10; raw16[i + 1] = 20; raw16[i + 2] = 30; raw16[i + 3] = 128; }
    // Put distinct pixel at (0,0) -> RGBA(200, 100, 50, 255)
    raw16[16] = 200; raw16[17] = 100; raw16[18] = 50; raw16[19] = 255;
    using var decoded16 = AdbCapture.DecodeRawScreencap(raw16);
    Check(decoded16.Size == new Size(1600, 900) && decoded16.GetPixel(0, 0).ToArgb() == Color.FromArgb(255, 200, 100, 50).ToArgb(),
        "ADB 16-byte RGBA raw screencap decodes with R/B swap and opaque alpha");
    using var normAdb = GameCoordinates.Normalize(decoded16, new Rectangle(Point.Empty, decoded16.Size));
    Check(normAdb.Size == new Size(1520, 865) && normAdb.GetPixel(3, 34).ToArgb() == Color.FromArgb(255, 200, 100, 50).ToArgb(),
        "ADB normalized frame aligns with canonical viewport origin");

    // 12-byte Android <=7 header with BGRA_8888 (format=5), 16x9
    var raw12 = new byte[12 + 16 * 9 * 4];
    BitConverter.GetBytes(16).CopyTo(raw12, 0);
    BitConverter.GetBytes(9).CopyTo(raw12, 4);
    BitConverter.GetBytes(5).CopyTo(raw12, 8);
    raw12[12] = 40; raw12[13] = 50; raw12[14] = 60; raw12[15] = 255; // B=40, G=50, R=60
    using var decoded12 = AdbCapture.DecodeRawScreencap(raw12);
    Check(decoded12.GetPixel(0, 0).ToArgb() == Color.FromArgb(255, 60, 50, 40).ToArgb(),
        "ADB 12-byte BGRA raw screencap decodes without channel swap");

    Fails(() => AdbCapture.DecodeRawScreencap(new byte[8]), "Capture failed");
    Fails(() => AdbCapture.DecodeRawScreencap(new byte[16 + 10]), "Capture failed");
}
Check(AdbInput.MapVirtualKeyToAndroidKeyCode(0x1B) == 4 && AdbInput.MapVirtualKeyToAndroidKeyCode(0x0D) == 66 &&
      AdbInput.MapVirtualKeyToAndroidKeyCode(0x30) == 7 && AdbInput.MapVirtualKeyToAndroidKeyCode(0x41) == 29,
      "ADB virtual key mapping converts Windows VK to Android keycodes");
Fails(() => AdbInput.MapVirtualKeyToAndroidKeyCode(0xFF), "Input failed");
Fails(() => new WindowTarget(IntPtr.Zero,IntPtr.Zero),"Window invalid");

// Run the actual worker with a failed capture backend. It must pause before any
// recovery input, then Stop must wake the paused worker promptly.
var faultBot = new Autobot();
void SetField(Autobot bot, string name, object value) => typeof(Autobot)
    .GetField(name,BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bot,value);
SetField(faultBot,"backgroundMode",true);
SetField(faultBot,"stopRequested",false);
var failedCapture = new FailedCapture();
SetField(faultBot,"gameCapture",failedCapture);
SetField(faultBot,"waitBetweenBattlesRuntimes",new List<WaitBetweenBattlesRuntime>());
typeof(Autobot).GetProperty(nameof(Autobot.IsActive))!.SetValue(faultBot,true);
typeof(Autobot).GetProperty(nameof(Autobot.IsRunning))!.SetValue(faultBot,true);
using var paused = new ManualResetEventSlim();
string? failureMessage = null;
int failureEvents = 0;
faultBot.OnFailed += message => { failureMessage = message; failureEvents++; paused.Set(); };
var worker = new Thread(() => typeof(Autobot).GetMethod("WorkerLoop",BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(faultBot,null));
worker.Start();
Check(paused.Wait(3000),"capture recovery exhausted then worker pauses");
Check(failedCapture.Attempts == 3,"capture makes exactly two retries before pause");
Check(failureEvents == 1 && failureMessage!.StartsWith("Capture failed:") && failureMessage.Contains("Bot paused."),
    "single categorized capture error gives a pause instruction");
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

    // 1. Capture timeout: single-flight guard, timeout classification, and late bitmap disposal.
    var timeoutCapture = new WindowCapture(new WindowTarget(receiver.Root, receiver.Handle), true, captureTimeoutMs: 120);
    using var gate = new ManualResetEventSlim(false);
    using var lateBitmapCreated = new ManualResetEventSlim(false);
    Bitmap? lateBitmap = null;
    try
    {
        timeoutCapture.ExecuteSingleFlightCapture(() =>
        {
            lateBitmap = new Bitmap(16, 16);
            lateBitmapCreated.Set();
            gate.Wait(3000);
            return lateBitmap;
        }, 120, CancellationToken.None, "TestPrintWindow");
        throw new Exception("Expected capture timeout");
    }
    catch (AutomationException ex)
    {
        Check(ex.Kind == AutomationErrorKind.CaptureTimeout && ex.IsRetryable && ex.Message.StartsWith("Capture failed:"),
            "capture timeout is classified as retryable CaptureTimeout");
    }
    Check(timeoutCapture.HasPendingCapture, "timed-out capture keeps single-flight guard active");
    try
    {
        timeoutCapture.ExecuteSingleFlightCapture(() => new Bitmap(16, 16), 120, CancellationToken.None, "TestPrintWindow");
        throw new Exception("Expected single-flight rejection");
    }
    catch (AutomationException ex)
    {
        Check(ex.Kind == AutomationErrorKind.CaptureTimeout && ex.Message.Contains("still running"),
            "second capture while hung is rejected without accumulating worker tasks");
    }
    gate.Set();
    var clearWait = Stopwatch.StartNew();
    while (timeoutCapture.HasPendingCapture && clearWait.ElapsedMilliseconds < 2000) Thread.Sleep(15);
    Check(!timeoutCapture.HasPendingCapture, "late capture clears single-flight guard after finishing");
    bool lateDisposed = false;
    try { _ = lateBitmap!.Width; } catch (ArgumentException) { lateDisposed = true; }
    Check(lateDisposed, "late timed-out bitmap is safely disposed");
}

// 2. Retry policy & non-retryable error control.
{
    var policy = new RetryPolicy(maxRetries: 2, baseDelayMs: 200, maxDelayMs: 500, backoffMultiplier: 2.0);
    Check(policy.GetDelayMs(1) == 200 && policy.GetDelayMs(2) == 400 && policy.GetDelayMs(3) == 500,
        "retry policy applies capped exponential backoff");
    Check(policy.ShouldRetry(new AutomationException(AutomationErrorKind.CaptureTimeout, "Capture failed: timed out"), 1),
        "capture timeout is retryable within max retries");
    Check(!policy.ShouldRetry(new AutomationException(AutomationErrorKind.CaptureTimeout, "Capture failed: timed out"), 3),
        "retry policy stops once max retries are exhausted");
    Check(!policy.ShouldRetry(new AutomationException(AutomationErrorKind.WindowInvalid, "Window invalid: minimized"), 1),
        "non-retryable WindowInvalid skips useless retries");
    Check(!policy.ShouldRetry(new AutomationException(AutomationErrorKind.StaleFrame, "Stale frame: frozen"), 1),
        "non-retryable StaleFrame skips useless retries");

    // Non-retryable WindowInvalid pauses immediately on attempt 1 (no wasted retries).
    var nonRetryBot = new Autobot();
    SetField(nonRetryBot, "backgroundMode", true);
    SetField(nonRetryBot, "stopRequested", false);
    var invalidCapture = new SequenceCapture(
        () => throw new AutomationException(AutomationErrorKind.WindowInvalid, "Window invalid: LDPlayer is minimized."));
    SetField(nonRetryBot, "gameCapture", invalidCapture);
    SetField(nonRetryBot, "waitBetweenBattlesRuntimes", new List<WaitBetweenBattlesRuntime>());
    typeof(Autobot).GetProperty(nameof(Autobot.IsActive))!.SetValue(nonRetryBot, true);
    typeof(Autobot).GetProperty(nameof(Autobot.IsRunning))!.SetValue(nonRetryBot, true);
    using var nrPaused = new ManualResetEventSlim();
    nonRetryBot.OnFailed += _ => nrPaused.Set();
    var nrWorker = new Thread(() => typeof(Autobot).GetMethod("WorkerLoop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(nonRetryBot, null));
    nrWorker.Start();
    Check(nrPaused.Wait(2000), "non-retryable error pauses worker promptly");
    Check(invalidCapture.Attempts == 1, "non-retryable WindowInvalid makes exactly 1 attempt with 0 retries");
    nonRetryBot.Stop();
    Check(nrWorker.Join(1500), "Stop cleanly exits worker after non-retryable pause");

    // Transient capture timeout recovers on attempt 2 and resets consecutive failure counters.
    var recoverBot = new Autobot();
    SetField(recoverBot, "backgroundMode", true);
    SetField(recoverBot, "stopRequested", false);
    var recoverCapture = new SequenceCapture(
        () => throw new AutomationException(AutomationErrorKind.CaptureTimeout, "Capture failed: transient timeout"),
        () => (Bitmap)normalized.Clone());
    SetField(recoverBot, "gameCapture", recoverCapture);
    using var recovered = (Bitmap)typeof(Autobot).GetMethod("CaptureBackground", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(recoverBot, null)!;
    Check(recoverCapture.Attempts == 2 && recoverBot.CaptureRetryTracker.ConsecutiveFailures == 0 && recoverBot.CaptureRetryTracker.TotalRetries == 1,
        "transient capture timeout recovers on retry and resets consecutive failures");
}

// 3. Infinite restart loop prevention (RestartGuard: re-entrancy, consecutive limit, sliding window, thread restarts).
{
    DateTime fakeNow = new(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);
    var guard = new RestartGuard(
        maxRestartsInWindow: 3,
        restartWindow: TimeSpan.FromMinutes(10),
        maxConsecutiveRestarts: 2,
        maxThreadRestartsInWindow: 2,
        threadRestartWindow: TimeSpan.FromMinutes(10),
        clock: () => fakeNow);

    Check(guard.TryBeginRestart("test-1", out _), "first restart allowed");
    Check(!guard.TryBeginRestart("nested", out var reentrantReason) && reentrantReason!.Contains("re-entrancy"),
        "nested recursive restart (Restart -> Reset -> EnterGC -> Restart) is blocked");
    guard.EndRestart();

    Check(guard.TryBeginRestart("test-2", out _), "second consecutive restart allowed");
    guard.EndRestart();
    Check(!guard.TryBeginRestart("test-3", out var consecReason) && consecReason!.Contains("consecutive"),
        "consecutive restart limit blocks infinite restart loop");

    guard.RecordHealthyProgress();
    Check(guard.TryBeginRestart("test-3-after-progress", out _), "healthy progress resets consecutive counter");
    guard.EndRestart();
    guard.RecordHealthyProgress();
    Check(!guard.TryBeginRestart("test-4-window", out var winReason) && winReason!.Contains("loop prevented"),
        "sliding time window blocks excessive restarts even after brief progress");

    fakeNow = fakeNow.AddMinutes(11);
    Check(guard.TryBeginRestart("test-after-window", out _), "restarts allowed again after sliding window expires");
    guard.EndRestart();

    Check(guard.TryConsumeThreadRestart("freeze-1", out _), "thread restart 1 allowed");
    Check(guard.TryConsumeThreadRestart("freeze-2", out _), "thread restart 2 allowed");
    Check(!guard.TryConsumeThreadRestart("freeze-3", out var threadReason) && threadReason!.Contains("Thread restart loop prevented"),
        "thread restart budget prevents infinite WorkerLoop -> RestartThread loop");
}

// 4. Thread-safe logging, enriched context, and size-based rotation.
{
    string logDir = Path.Combine(Path.GetFullPath("artifacts"), "log-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(logDir);
    string testLogPath = Path.Combine(logDir, "test.log");
    try
    {
        Log.Configure(path: testLogPath, maxBytes: 4096, backups: 2);
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var logThreads = Enumerable.Range(0, 8).Select(tid => new Thread(() =>
        {
            try
            {
                for (int i = 0; i < 40; i++)
                    Log.I($"concurrent-log thread={tid} iter={i} payload=0123456789abcdef");
            }
            catch (Exception ex) { exceptions.Add(ex); }
        })).ToList();
        logThreads.ForEach(t => t.Start());
        logThreads.ForEach(t => t.Join());

        Check(exceptions.IsEmpty, "concurrent multi-thread logging throws zero IOExceptions");
        Check(File.Exists(testLogPath) && File.Exists(testLogPath + ".1"), "size-based log rotation creates backup files");
        Check(!File.Exists(testLogPath + ".3"), "log rotation enforces max backup cap");
        string sampleLine = File.ReadLines(testLogPath).First();
        Check(sampleLine.Contains(" T") && sampleLine.Contains("Program:") && sampleLine.Contains("[I]"),
            "log format includes thread ID, caller source, and level");
    }
    finally
    {
        Log.ResetConfiguration();
        try { Directory.Delete(logDir, recursive: true); } catch { }
    }
}

// 5. Safe Stop and Pause: immediate Pause during Wait, immediate Stop during Wait & Pause, no green-UI flash, idempotent Stop.
{
    var ctrlBot = new Autobot();
    SetField(ctrlBot, "stopRequested", false);
    SetField(ctrlBot, "userStopRequested", false);
    typeof(Autobot).GetProperty(nameof(Autobot.IsActive))!.SetValue(ctrlBot, true);
    typeof(Autobot).GetProperty(nameof(Autobot.IsRunning))!.SetValue(ctrlBot, true);
    typeof(Autobot).GetProperty(nameof(Autobot.State))!.SetValue(ctrlBot, BotState.Running);
    SetField(ctrlBot, "waitBetweenBattlesRuntimes", new List<WaitBetweenBattlesRuntime>());
    ((ManualResetEvent)typeof(Autobot).GetField("stopWaitHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ctrlBot)!).Reset();
    ((ManualResetEventSlim)typeof(Autobot).GetField("workerExitedEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ctrlBot)!).Reset();

    using var enteredWait = new ManualResetEventSlim();
    using var pausedReached = new ManualResetEventSlim();
    int onStartedWhileStopping = 0;
    bool stopping = false;
    ctrlBot.OnPaused += () => pausedReached.Set();
    ctrlBot.OnStarted += _ => { if (stopping) Interlocked.Increment(ref onStartedWhileStopping); };

    var waitMethod = typeof(Autobot).GetMethod("Wait", BindingFlags.Instance | BindingFlags.NonPublic)!;
    bool canceledCleanly = false;
    var ctrlThread = new Thread(() =>
    {
        try
        {
            enteredWait.Set();
            waitMethod.Invoke(ctrlBot, new object[] { 10_000 });
        }
        catch (TargetInvocationException tie) when (tie.InnerException is OperationCanceledException)
        {
            canceledCleanly = true;
        }
        finally
        {
            ((ManualResetEventSlim)typeof(Autobot).GetField("workerExitedEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ctrlBot)!).Set();
        }
    });
    ctrlThread.Start();
    Check(enteredWait.Wait(1000), "worker enters long 10s Wait");
    Thread.Sleep(50);

    var pauseSw = Stopwatch.StartNew();
    ctrlBot.Pause();
    Check(pausedReached.Wait(1000) && pauseSw.ElapsedMilliseconds < 1000,
        "Pause interrupts long Wait immediately without blocking for 10s");
    Check(ctrlBot.IsPaused && ctrlBot.State == BotState.Paused && !ctrlBot.IsRunning,
        "Pause transitions cleanly to Paused state");

    // Verify Stop while Paused wakes immediately and does NOT flash Running UI.
    stopping = true;
    var stopSw = Stopwatch.StartNew();
    Check(ctrlBot.StopAndWait(TimeSpan.FromMilliseconds(1500)) && stopSw.ElapsedMilliseconds < 1500,
        "StopAndWait terminates paused worker immediately");
    Check(canceledCleanly, "paused worker exits via OperationCanceledException on Stop");
    Check(onStartedWhileStopping == 0, "Stop while paused never flashes Running UI");

    // Verify Stop is idempotent and clears restartRequested.
    SetField(ctrlBot, "restartRequested", true);
    ctrlBot.Stop();
    ctrlBot.Stop();
    bool restartFlagAfterStop = (bool)typeof(Autobot).GetField("restartRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ctrlBot)!;
    Check(!restartFlagAfterStop, "repeated Stop calls are idempotent and clear pending restartRequested");

    // Verify PauseAutomation never swallows a concurrent Stop.
    var raceBot = new Autobot();
    raceBot.Stop();
    bool pauseSwallowedStop = false;
    try
    {
        typeof(Autobot).GetMethod("PauseAutomation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(raceBot, new object[] { new AutomationException("Capture failed: late fault") });
        pauseSwallowedStop = true;
    }
    catch (TargetInvocationException tie) when (tie.InnerException is OperationCanceledException)
    {
        pauseSwallowedStop = false;
    }
    Check(!pauseSwallowedStop, "PauseAutomation respects Stop and never swallows cancellation");
}

// 6. Phase 5 performance optimizations: exact equivalence for BmpHash, Normalize, IsBlank, AdbCapture buffer decode, and JPEG cache.
{
    // 6a. Utils.BmpHash matches reference MD5 over (y += 3, x += 3, skip x<75 && y<75) bit-for-bit.
    using var hashBmp = new Bitmap(160, 120, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(hashBmp))
    {
        g.Clear(Color.FromArgb(255, 25, 50, 75));
        g.FillRectangle(Brushes.Crimson, 10, 10, 50, 50); // inside ignored top-left 75x75
        g.FillRectangle(Brushes.Gold, 75, 75, 40, 30);    // outside ignored region
    }
    byte[] ComputeReferenceHash(Bitmap bmp)
    {
        var bytes = new List<byte>();
        for (int y = 0; y < bmp.Height; y += 3)
            for (int x = 0; x < bmp.Width; x += 3)
            {
                if (x < 75 && y < 75) continue;
                var c = bmp.GetPixel(x, y);
                bytes.Add(c.B); bytes.Add(c.G); bytes.Add(c.R); bytes.Add(c.A);
            }
        return System.Security.Cryptography.MD5.HashData(bytes.ToArray());
    }
    var fastHash = Utils.BmpHash(hashBmp);
    var refHash = ComputeReferenceHash(hashBmp);
    Check(fastHash.SequenceEqual(refHash), "optimized Utils.BmpHash matches reference MD5 bit-for-bit");
    hashBmp.SetPixel(12, 12, Color.Lime); // inside ignored 75x75
    Check(Utils.BmpHash(hashBmp).SequenceEqual(fastHash), "Utils.BmpHash ignores top-left 75x75 region");
    hashBmp.SetPixel(75, 75, Color.Cyan); // outside ignored 75x75
    Check(!Utils.BmpHash(hashBmp).SequenceEqual(fastHash), "Utils.BmpHash detects pixel change at (75,75)");

    // 6b. GameCoordinates.Normalize matches GDI+ NearestNeighbor + PixelOffsetMode.Half pixel-for-pixel (1:1 and scaled 1600x900).
    Bitmap NormalizeGdiReference(Bitmap src, Rectangle srcArea)
    {
        var res = new Bitmap(1520, 865, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(res);
        g.Clear(Color.Black);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(src, GameCoordinates.Viewport, srcArea, GraphicsUnit.Pixel);
        return res;
    }

    using var src1to1 = new Bitmap(1500, 880, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(src1to1))
    {
        g.Clear(Color.FromArgb(255, 40, 80, 120));
        g.FillRectangle(Brushes.Magenta, 15, 25, 300, 200);
        g.FillRectangle(Brushes.Yellow, 900, 500, 250, 250);
    }
    var cropRect = new Rectangle(10, 20, 1475, 830);
    using var fast1to1 = GameCoordinates.Normalize(src1to1, cropRect);
    using var gdi1to1 = NormalizeGdiReference(src1to1, cropRect);
    Check(Utils.BitmapsEqual(fast1to1, gdi1to1), "optimized Normalize 1:1 crop matches GDI+ pixel-for-pixel");

    using var srcScaled = new Bitmap(1600, 900, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(srcScaled))
    {
        g.Clear(Color.FromArgb(255, 11, 22, 33));
        for (int i = 0; i < 16; i++)
            g.FillRectangle(i % 2 == 0 ? Brushes.OrangeRed : Brushes.CornflowerBlue, i * 100, (i * 53) % 780, 90, 110);
    }
    using var fastScaled = GameCoordinates.Normalize(srcScaled, new Rectangle(Point.Empty, srcScaled.Size));
    using var gdiScaled = NormalizeGdiReference(srcScaled, new Rectangle(Point.Empty, srcScaled.Size));
    Check(Utils.BitmapsEqual(fastScaled, gdiScaled), "optimized Normalize 1600x900 scaling matches GDI+ NearestNeighbor pixel-for-pixel");

    // 6c. FrameHealth.IsBlank (LockBits) detects uniform non-black and single sample change.
    using var uniformGray = new Bitmap(1520, 865, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(uniformGray)) g.Clear(Color.FromArgb(255, 64, 64, 64));
    Check(FrameHealth.IsBlank(uniformGray), "LockBits IsBlank rejects uniform non-black frame");
    uniformGray.SetPixel(42 + 23 * 10, 53 + 23 * 10, Color.FromArgb(255, 80, 64, 64));
    Check(!FrameHealth.IsBlank(uniformGray), "LockBits IsBlank accepts frame with pixel delta > 3");

    // 6d. AdbCapture.DecodeRawScreencap(buffer, length) ignores trailing capacity bytes from reusable MemoryStream.
    var oversized = new byte[16 + 16 * 9 * 4 + 4096];
    Array.Fill(oversized, (byte)0xEE);
    BitConverter.GetBytes(16).CopyTo(oversized, 0);
    BitConverter.GetBytes(9).CopyTo(oversized, 4);
    BitConverter.GetBytes(1).CopyTo(oversized, 8);
    oversized[16] = 210; oversized[17] = 110; oversized[18] = 60; oversized[19] = 10;
    using var decOversized = AdbCapture.DecodeRawScreencap(oversized, 16 + 16 * 9 * 4);
    Check(decOversized.Size == new Size(16, 9) && decOversized.GetPixel(0, 0).ToArgb() == Color.FromArgb(255, 210, 110, 60).ToArgb(),
        "AdbCapture.DecodeRawScreencap(buffer, length) works with reusable oversized buffers");

    // 6e. ScreenshotCache.CompressToJpeg returns valid JPEG header and disposes GDI encoder objects cleanly.
    var jpegBytes = ScreenshotCache.CompressToJpeg(fastScaled, 15);
    Check(jpegBytes.Length > 100 && jpegBytes[0] == 0xFF && jpegBytes[1] == 0xD8,
        "ScreenshotCache.CompressToJpeg produces valid JPEG bytes with cached encoder");
}

// 7. Phase 6 UX runtime status snapshot & formatting checks.
{
    var adbSnap = new RuntimeStatusSnapshot("Background ADB", "emulator-5556", 12, 340, 1, 0);
    Check(adbSnap.FormatShortBadge() == "ADB", "ADB snapshot produces ADB badge");
    Check(adbSnap.FormatSummary() == "Mode: Background ADB (emulator-5556) | Battles: 12 | Capture: 340 ms | Retries: 1",
        "ADB runtime status summary formats mode, target, battles, latency, and retries");

    var winSnap = new RuntimeStatusSnapshot("Background Win32", "HWND 0x1A2B", 0, 0, 0, 0);
    Check(winSnap.FormatShortBadge() == "Win32" && winSnap.FormatSummary().Contains("Capture: -"),
        "Win32 snapshot formats placeholder latency before first capture");

    var statusBot = new Autobot();
    SetField(statusBot, "backgroundMode", true);
    SetField(statusBot, "useAdb", true);
    SetField(statusBot, "ldInstanceIndex", 1);
    RuntimeStatusSnapshot? observedSnap = null;
    statusBot.OnRuntimeStatusUpdate += s => observedSnap = s;
    statusBot.RecordCompletedBattle();
    Check(statusBot.CompletedBattles == 1 && observedSnap.HasValue && observedSnap.Value.CompletedBattles == 1 && observedSnap.Value.ModeName == "Background ADB",
        "RecordCompletedBattle increments battle counter and emits OnRuntimeStatusUpdate");
}

Console.WriteLine($"{passed} assertions passed.");

sealed class SequenceCapture(params Func<Bitmap>[] steps) : IGameCapture
{
    public int Attempts { get; private set; }
    public Bitmap Capture(CancellationToken cancellationToken)
    {
        int idx = Math.Min(Attempts, steps.Length - 1);
        Attempts++;
        return steps[idx]();
    }
}

sealed class FailedCapture : IGameCapture
{
    public int Attempts { get; private set; }
    public Bitmap Capture(CancellationToken cancellationToken)
    {
        Attempts++;
        throw new AutomationException("Capture failed: injected test failure.");
    }
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
