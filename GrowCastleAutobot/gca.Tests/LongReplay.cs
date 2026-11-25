using gca;
using gca.Classes;
using gca.Classes.Automation;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;

// Diagnostic only: calls production replay/capture/input, never loads saved settings.
internal static class LongReplay
{
    public static int Run(string[] args)
    {
        if (args.Length != 5 || !int.TryParse(args[3], out int cycles) || cycles < 0 ||
            !int.TryParse(args[4], out int minutes) || minutes < 1 || minutes > 120)
        {
            Console.Error.WriteLine("Usage: --long-replay <title> <cover-label> <cycles; 0=soak> <minutes; 1..120>");
            return 2;
        }
        var directory = Path.GetFullPath(Path.Combine("artifacts", "task08", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(Path.Combine(directory, "work"));
        Directory.SetCurrentDirectory(Path.Combine(directory, "work"));
        using var events = new StreamWriter(Path.Combine(directory, "events.jsonl")) { AutoFlush = true };
        var timer = Stopwatch.StartNew();
        void Record(string kind, object data)
        {
            string line = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, elapsedSeconds = timer.Elapsed.TotalSeconds, kind, data });
            events.WriteLine(line);
            Console.WriteLine(line);
        }
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(minutes));
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += onCancel;
        var stats = new Counters();
        int completed = 0, stops = 0, foregroundChanges = 0, cursorChanges = 0;
        bool started = false;
        string outcome = "STOPPED", reason = "";
        Record("start", new { title = args[1], cover = args[2], cycles, minutes, directory, note = "Cover is user-confirmed; keyboard content is never recorded. This is a replay integration harness, not the full WPF worker." });
        try
        {
            using var dpi = new PhysicalPixels();
            var root = WinAPI.FindWindow(null!, args[1]);
            var render = WinAPI.FindChildWindowByClass(root, "RenderWindow");
            var target = new WindowTarget(root, render);
            var bot = new Autobot();
            void Set(string name, object value) => typeof(Autobot).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(bot, value);
            var previousForeground = WinAPI.GetForegroundWindow();
            WinAPI.GetCursorPos(out var previousCursor);
            void Guard()
            {
                cancel.Token.ThrowIfCancellationRequested();
                target.Validate();
                var foreground = WinAPI.GetForegroundWindow();
                if (foreground == root || WinAPI.IsChild(root, foreground))
                    throw new AutomationException("Focus observed: LDPlayer foreground. Attribution requires user confirmation.");
                if (foreground != previousForeground)
                {
                    foregroundChanges++;
                    Record("foreground_change", new { hwnd = foreground.ToInt64() });
                    previousForeground = foreground;
                }
                WinAPI.GetCursorPos(out var cursor);
                if (cursor.X != previousCursor.X || cursor.Y != previousCursor.Y) cursorChanges++;
                previousCursor = cursor;
            }
            Guard();
            Set("hwnd", root); Set("renderHwnd", render); Set("backgroundMode", true);
            Set("backgroundTarget", target); Set("ioCancellation", cancel);
            Set("gameCapture", new CountCapture(new WindowCapture(target, true), stats, Guard));
            Set("gameInput", new CountInput(new BackgroundInput(target, Guard), stats));
            Set("stopRequested", false); Set("thisChronoSlot", -1); Set("screenshotPopups", false);
            ((ManualResetEvent)typeof(Autobot).GetField("stopWaitHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bot)!).Reset();
            TimeSpan heartbeat = TimeSpan.Zero;
            while (true)
            {
                Guard();
                if (bot.CaptchaOnScreen()) throw new AutomationException("CAPTCHA observed: manual handling required.");
                if (!bot.CheckSky() && !bot.WaitUntil(() => bot.CheckSky(), () => Guard(), 4000, 100))
                    throw new AutomationException("Game state unknown: no recovery input sent.");
                if (bot.CheckGCMenu(false))
                {
                    if (started) { completed++; started = false; Record("replay_completed", new { completed }); }
                    if (cycles > 0 && completed >= cycles) { outcome = "PASS"; reason = "Requested cycles completed"; break; }
                    Guard();
                    bot.PerformReplay();
                    Guard();
                    if (bot.CheckGCMenu()) throw new AutomationException("Input failed: Replay did not leave menu.");
                    started = true;
                    Record("replay_started", new { next = completed + 1 });
                }
                if (timer.Elapsed >= heartbeat)
                {
                    Record("heartbeat", new { completed, stats.Captures, stats.CaptureErrors, stats.Inputs, stats.InputErrors, foregroundChanges, cursorChanges });
                    heartbeat = timer.Elapsed + TimeSpan.FromSeconds(30);
                }
                if (cancel.Token.WaitHandle.WaitOne(250)) cancel.Token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            bool durationReached = timer.Elapsed >= TimeSpan.FromMinutes(minutes);
            outcome = durationReached && cycles == 0 && completed > 0 ? "DURATION_COMPLETED" : "STOPPED";
            reason = durationReached ? "Time limit reached" : "Operator canceled";
            stops++;
        }
        catch (Exception e) { reason = e.Message; stops++; Record("stop", new { type = e.GetType().Name, reason }); }
        finally { Console.CancelKeyPress -= onCancel; }
        var summary = new { outcome, reason, cover = args[2], elapsedSeconds = timer.Elapsed.TotalSeconds, completed, inFlightReplay = started, stats.Captures, stats.CaptureErrors, stats.Inputs, stats.InputErrors, stops, foregroundChanges, cursorChanges,
            limitations = "No keyboard logging. Cursor changes may be user input. Focus sampled at capture/input/loop boundaries, not continuously. Cover and interference require operator confirmation. No automatic restart/resume." };
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Record("summary", summary);
        return outcome is "PASS" or "DURATION_COMPLETED" ? 0 : 1;
    }

    private sealed class Counters { public int Captures, CaptureErrors, Inputs, InputErrors; }
    private sealed class CountCapture(IGameCapture inner, Counters stats, Action guard) : IGameCapture
    {
        public Bitmap Capture(CancellationToken token)
        {
            guard(); stats.Captures++;
            try { return inner.Capture(token); }
            catch (OperationCanceledException) { throw; }
            catch { stats.CaptureErrors++; throw; }
        }
    }
    private sealed class CountInput(IGameInput inner, Counters stats) : IGameInput
    {
        private void Send(Action action) { stats.Inputs++; try { action(); } catch (OperationCanceledException) { throw; } catch { stats.InputErrors++; throw; } }
        public void Click(int x, int y, bool right = false) => Send(() => inner.Click(x, y, right));
        public void Move(int x, int y) => Send(() => inner.Move(x, y));
        public void Wheel(int x, int y, int delta) => Send(() => inner.Wheel(x, y, delta));
        public void Key(uint key) => Send(() => inner.Key(key));
    }
}
