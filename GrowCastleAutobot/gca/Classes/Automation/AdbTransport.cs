using gca.Script;
using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace gca.Classes.Automation;

public sealed class AdbTarget
{
    public string AdbPath { get; }
    public string Serial { get; }
    public int InstanceIndex { get; }
    public Size ScreenSize { get; set; }

    public AdbTarget(string adbPath, string serial, int instanceIndex, Size? initialScreenSize = null)
    {
        AdbPath = adbPath;
        Serial = serial;
        InstanceIndex = instanceIndex;
        ScreenSize = initialScreenSize ?? new Size(1600, 900);
        Validate();
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AdbPath) || !File.Exists(AdbPath))
            throw new AutomationException($"Window invalid: adb.exe was not found at '{AdbPath}'.");
        if (string.IsNullOrWhiteSpace(Serial))
            throw new AutomationException("Window invalid: ADB serial is empty.");
        if (ScreenSize.Width < 100 || ScreenSize.Height < 100 ||
            Math.Abs((double)ScreenSize.Width / ScreenSize.Height - 16.0 / 9) > 0.025)
            throw new AutomationException($"Window invalid: ADB screen size {ScreenSize.Width}x{ScreenSize.Height} is not landscape 16:9.");
    }

    public static (string EmulatorSerial, string TcpSerial) ExpectedSerials(int instanceIndex)
    {
        if (instanceIndex < 0)
            throw new AutomationException($"Window invalid: invalid LDPlayer instance index {instanceIndex}.");
        int consolePort = 5554 + instanceIndex * 2;
        int adbPort = 5555 + instanceIndex * 2;
        return ($"emulator-{consolePort}", $"127.0.0.1:{adbPort}");
    }

    public static Dictionary<string, string> ParseDevices(string devicesOutput)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(devicesOutput)) return result;
        foreach (var rawLine in devicesOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("*", StringComparison.Ordinal))
                continue;
            var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                result[parts[0]] = parts[1];
        }
        return result;
    }

    public static string ResolveSerial(string adbPath, int instanceIndex, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(adbPath) || !File.Exists(adbPath))
            throw new AutomationException($"Window invalid: adb.exe was not found at '{adbPath}'.");

        var (emulatorSerial, tcpSerial) = ExpectedSerials(instanceIndex);
        var devices = ParseDevices(RunAdb(adbPath, new[] { "devices" }, 5000, cancellationToken).Stdout);
        if (TryPickOnline(devices, emulatorSerial, tcpSerial, out var online))
            return online;

        // Try connecting via local TCP port if LDPlayer didn't auto-register emulator-XXXX.
        _ = RunAdb(adbPath, new[] { "connect", tcpSerial }, 5000, cancellationToken, allowNonZero: true);
        devices = ParseDevices(RunAdb(adbPath, new[] { "devices" }, 5000, cancellationToken).Stdout);
        if (TryPickOnline(devices, emulatorSerial, tcpSerial, out online))
            return online;

        if (devices.TryGetValue(emulatorSerial, out var emuState) || devices.TryGetValue(tcpSerial, out emuState))
        {
            throw new AutomationException(
                $"Window invalid: ADB device for LDPlayer index {instanceIndex} ({emulatorSerial} / {tcpSerial}) is '{emuState}'. " +
                "Wait for LDPlayer to finish booting or restart ADB debugging.");
        }

        throw new AutomationException(
            $"Window invalid: ADB device for LDPlayer index {instanceIndex} ({emulatorSerial} / {tcpSerial}) is not available. " +
            "Enable 'ADB debugging (Local connection)' in LDPlayer Settings -> Other settings, then restart LDPlayer.");
    }

    private static bool TryPickOnline(Dictionary<string, string> devices, string emulatorSerial, string tcpSerial, out string serial)
    {
        if (devices.TryGetValue(emulatorSerial, out var state) && state.Equals("device", StringComparison.OrdinalIgnoreCase))
        {
            serial = emulatorSerial;
            return true;
        }
        if (devices.TryGetValue(tcpSerial, out state) && state.Equals("device", StringComparison.OrdinalIgnoreCase))
        {
            serial = tcpSerial;
            return true;
        }
        serial = "";
        return false;
    }

    public static string? FindLdPlayerDirectory(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            try
            {
                WinAPI.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                {
                    using var proc = Process.GetProcessById((int)pid);
                    var dir = Path.GetDirectoryName(proc.MainModule?.FileName);
                    if (HasLdConsole(dir)) return dir;
                }
            }
            catch { }
        }

        try
        {
            foreach (var proc in Process.GetProcessesByName("dnplayer"))
            {
                try
                {
                    var dir = Path.GetDirectoryName(proc.MainModule?.FileName);
                    if (HasLdConsole(dir)) return dir;
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }

        string?[] registryLocations =
        {
            ReadInstallDir(Registry.CurrentUser, @"Software\XuanZhi\LDPlayer9"),
            ReadInstallDir(Registry.CurrentUser, @"Software\leidian\ldplayer9"),
            ReadInstallDir(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\leidian\ldplayer9"),
            ReadInstallDir(Registry.LocalMachine, @"SOFTWARE\XuanZhi\LDPlayer9")
        };

        foreach (var dir in registryLocations)
        {
            if (HasLdConsole(dir)) return dir;
        }

        return null;
    }

    private static string? ReadInstallDir(RegistryKey root, string subKey)
    {
        try
        {
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue("InstallDir") as string;
        }
        catch { return null; }
    }

    private static bool HasLdConsole(string? dir) =>
        !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "ldconsole.exe"));

    internal static (int ExitCode, string Stdout, string Stderr) RunAdb(
        string adbPath, string[] arguments, int timeoutMs, CancellationToken cancellationToken, bool allowNonZero = false)
    {
        var info = new ProcessStartInfo(adbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                Task.WhenAll(process.WaitForExitAsync(cancellationToken), stdoutTask, stderrTask)
                    .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken)
                    .GetAwaiter().GetResult();
            }
            catch
            {
                TryKill(process);
                DrainTasksSafely(stdoutTask, stderrTask);
                throw;
            }
            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();
            if (!allowNonZero && process.ExitCode != 0)
                throw new AutomationException(AutomationErrorKind.InputFailed, $"Input failed: adb exited with {process.ExitCode}: {stderr.Trim()} {stdout.Trim()}".Trim());
            return (process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException) { throw new AutomationException(AutomationErrorKind.CaptureTimeout, $"Capture failed: adb command timed out after {timeoutMs} ms."); }
        catch (AutomationException) { throw; }
        catch (Exception e) { throw new AutomationException(AutomationErrorKind.InputFailed, $"Input failed: adb error: {e.Message}"); }
    }

    internal static void DrainTasksSafely(params Task[] tasks)
    {
        try
        {
            Task.WhenAll(tasks).Wait(250);
        }
        catch
        {
            // Ignore; process was terminated.
        }
        foreach (var t in tasks)
        {
            _ = t.ContinueWith(done => { _ = done.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    internal static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
    }
}

public sealed class AdbCapture : IGameCapture, IDisposable
{
    public const int DefaultCaptureTimeoutMs = 4000;
    public const int SlowCaptureWarningMs = 1500;
    private const int InitialRawBufferCapacity = 1600 * 900 * 4 + 32;

    private readonly AdbTarget target;
    private readonly int captureTimeoutMs;
    private MemoryStream? reusableRawStream;

    public AdbCapture(AdbTarget target, int captureTimeoutMs = DefaultCaptureTimeoutMs)
    {
        if (captureTimeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(captureTimeoutMs));
        this.target = target;
        this.captureTimeoutMs = captureTimeoutMs;
    }

    public void Dispose()
    {
        reusableRawStream?.Dispose();
        reusableRawStream = null;
    }

    public Bitmap Capture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target.Validate();

        var sw = Stopwatch.StartNew();
        using var rawBitmap = CaptureAndDecodeRaw(cancellationToken, sw);

        if (rawBitmap.Width < 100 || rawBitmap.Height < 100 ||
            Math.Abs((double)rawBitmap.Width / rawBitmap.Height - 16.0 / 9) > 0.025)
        {
            throw new AutomationException(
                AutomationErrorKind.WindowInvalid,
                $"Window invalid: ADB screen is {rawBitmap.Width}x{rawBitmap.Height}, expected landscape 16:9 (1600x900).");
        }

        target.ScreenSize = rawBitmap.Size;
        var normalized = GameCoordinates.Normalize(rawBitmap, new Rectangle(Point.Empty, rawBitmap.Size));
        if (FrameHealth.IsBlank(normalized))
        {
            normalized.Dispose();
            throw new AutomationException(AutomationErrorKind.CaptureFailed, "Capture failed: ADB screencap frame is blank/black.");
        }
        if (sw.ElapsedMilliseconds >= SlowCaptureWarningMs)
            Log.W($"Slow capture: ADB screencap took {sw.ElapsedMilliseconds} ms (limit={captureTimeoutMs} ms).");
        return normalized;
    }

    private Bitmap CaptureAndDecodeRaw(CancellationToken cancellationToken, Stopwatch sw)
    {
        var info = new ProcessStartInfo(target.AdbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-s");
        info.ArgumentList.Add(target.Serial);
        info.ArgumentList.Add("exec-out");
        info.ArgumentList.Add("screencap");

        // Detach reusable stream before async I/O so an abandoned/timed-out read can never race with the next capture.
        var memory = reusableRawStream;
        reusableRawStream = null;
        if (memory == null)
        {
            memory = new MemoryStream(InitialRawBufferCapacity);
        }
        else
        {
            memory.SetLength(0);
            memory.Position = 0;
        }

        bool canReuseMemory = false;
        using var process = new Process { StartInfo = info };
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            process.Start();
            var copyTask = process.StandardOutput.BaseStream.CopyToAsync(memory, readCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                Task.WhenAll(process.WaitForExitAsync(cancellationToken), copyTask, stderrTask)
                    .WaitAsync(TimeSpan.FromMilliseconds(captureTimeoutMs), cancellationToken)
                    .GetAwaiter().GetResult();
                canReuseMemory = true;
            }
            catch (TimeoutException)
            {
                readCts.Cancel();
                AdbTarget.TryKill(process);
                AdbTarget.DrainTasksSafely(copyTask, stderrTask);
                throw new AutomationException(
                    AutomationErrorKind.CaptureTimeout,
                    $"Capture failed: ADB screencap exceeded {captureTimeoutMs} ms (elapsed={sw.ElapsedMilliseconds} ms).");
            }
            catch
            {
                readCts.Cancel();
                AdbTarget.TryKill(process);
                AdbTarget.DrainTasksSafely(copyTask, stderrTask);
                throw;
            }

            string stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new AutomationException(
                    AutomationErrorKind.CaptureFailed,
                    $"Capture failed: ADB screencap exit={process.ExitCode}: {stderr.Trim()}");
            }

            return DecodeRawScreencap(memory.GetBuffer(), (int)memory.Length);
        }
        catch (OperationCanceledException) { throw; }
        catch (AutomationException) { throw; }
        catch (Exception e) { throw new AutomationException(AutomationErrorKind.CaptureFailed, $"Capture failed: ADB screencap error: {e.Message}"); }
        finally
        {
            if (canReuseMemory)
            {
                memory.SetLength(0);
                memory.Position = 0;
                reusableRawStream = memory;
            }
            else
            {
                memory.Dispose();
            }
        }
    }

    public static Bitmap DecodeRawScreencap(byte[] data) =>
        DecodeRawScreencap(data, data?.Length ?? 0);

    public static unsafe Bitmap DecodeRawScreencap(byte[] data, int length)
    {
        if (data == null || length < 16 || length > data.Length)
            throw new AutomationException("Capture failed: ADB screencap returned empty or truncated header.");

        int width = BitConverter.ToInt32(data, 0);
        int height = BitConverter.ToInt32(data, 4);
        int format = BitConverter.ToInt32(data, 8);

        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new AutomationException($"Capture failed: invalid ADB screencap dimensions {width}x{height}.");

        long pixelBytes = (long)width * height * 4;
        int headerSize;
        if (length == 16 + pixelBytes) headerSize = 16; // Android 8+ (width, height, format, colorSpace)
        else if (length == 12 + pixelBytes) headerSize = 12; // Android <= 7 (width, height, format)
        else
            throw new AutomationException(
                $"Capture failed: unexpected ADB screencap length {length} for {width}x{height} (format={format}).");

        // Android PixelFormat: 1 = RGBA_8888, 2 = RGBX_8888, 5 = BGRA_8888
        bool swapRedBlue = format switch
        {
            1 or 2 => true,
            5 => false,
            _ => throw new AutomationException($"Capture failed: unsupported ADB screencap pixel format {format}.")
        };

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                fixed (byte* srcBase = &data[headerSize])
                {
                    byte* dstBase = (byte*)bmpData.Scan0;
                    int srcRowBytes = width * 4;
                    for (int y = 0; y < height; y++)
                    {
                        uint* src = (uint*)(srcBase + (long)y * srcRowBytes);
                        uint* dst = (uint*)(dstBase + (long)y * bmpData.Stride);
                        if (swapRedBlue)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                uint p = src[x];
                                dst[x] = 0xFF000000u | ((p & 0x000000FFu) << 16) | (p & 0x0000FF00u) | ((p & 0x00FF0000u) >> 16);
                            }
                        }
                        else
                        {
                            for (int x = 0; x < width; x++)
                            {
                                dst[x] = src[x] | 0xFF000000u;
                            }
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}

public sealed class AdbInput(AdbTarget target, Action checkpoint, CancellationToken cancellationToken = default) : IGameInput, IDisposable
{
    private const int CommandTimeoutMs = 3000;
    private const string DoneMarker = "__GCA_ADB_DONE__:";
    private readonly object shellLock = new();
    private Process? shellProcess;
    private bool disposed;

    public void Click(int x, int y, bool right = false)
    {
        checkpoint();
        var point = Map(x, y);
        ExecuteShell($"input tap {point.X} {point.Y}");
    }

    public void Move(int x, int y)
    {
        checkpoint();
        // Validate coordinates even though Android touchscreens do not have hover cursor movement.
        _ = Map(x, y);
    }

    public void Wheel(int x, int y, int delta)
    {
        checkpoint();
        var point = Map(x, y);
        int sizeH = target.ScreenSize.Height;
        int dist = Math.Clamp(Math.Abs(delta) * 2, 120, Math.Max(120, sizeH / 2));
        // Positive wheel delta scrolls up (finger swipes down); negative scrolls down (finger swipes up).
        int endY = Math.Clamp(delta > 0 ? point.Y + dist : point.Y - dist, 20, sizeH - 20);
        int startY = Math.Clamp(point.Y, 20, sizeH - 20);
        ExecuteShell($"input swipe {point.X} {startY} {point.X} {endY} 200");
    }

    public void Key(uint key)
    {
        checkpoint();
        target.Validate();
        int androidKey = MapVirtualKeyToAndroidKeyCode(key);
        ExecuteShell($"input keyevent {androidKey}");
    }

    public void PressAndroidButton(string buttonName)
    {
        checkpoint();
        target.Validate();
        int androidKey = buttonName.ToLowerInvariant() switch
        {
            "home" => 3,  // KEYCODE_HOME
            "back" => 4,  // KEYCODE_BACK
            _ => throw new AutomationException($"Input failed: unsupported ADB button '{buttonName}'.")
        };
        ExecuteShell($"input keyevent {androidKey}");
    }

    private Point Map(int x, int y)
    {
        target.Validate();
        return GameCoordinates.ToClient(x, y, target.ScreenSize);
    }

    public static int MapVirtualKeyToAndroidKeyCode(uint vk)
    {
        return vk switch
        {
            0x08 => 67,  // Backspace -> KEYCODE_DEL
            0x09 => 61,  // Tab -> KEYCODE_TAB
            0x0D => 66,  // Enter -> KEYCODE_ENTER
            0x1B => 4,   // Escape -> KEYCODE_BACK (LDPlayer maps Esc to Back in games)
            0x20 => 62,  // Space -> KEYCODE_SPACE
            0x24 => 3,   // Home -> KEYCODE_HOME
            0x25 => 21,  // Left -> KEYCODE_DPAD_LEFT
            0x26 => 19,  // Up -> KEYCODE_DPAD_UP
            0x27 => 22,  // Right -> KEYCODE_DPAD_RIGHT
            0x28 => 20,  // Down -> KEYCODE_DPAD_DOWN
            >= 0x30 and <= 0x39 => (int)(vk - 0x30 + 7),  // '0'-'9' -> KEYCODE_0..KEYCODE_9 (7..16)
            >= 0x41 and <= 0x5A => (int)(vk - 0x41 + 29), // 'A'-'Z' -> KEYCODE_A..KEYCODE_Z (29..54)
            >= 0x60 and <= 0x69 => (int)(vk - 0x60 + 144), // NumPad 0-9 -> KEYCODE_NUMPAD_0..9
            _ => throw new AutomationException($"Input failed: unmapped virtual key 0x{vk:X} for ADB input.")
        };
    }

    private void ExecuteShell(string command)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (shellLock)
        {
            if (disposed)
                throw new AutomationException("Input failed: ADB input session is disposed.");

            var process = EnsureShellStarted();
            try
            {
                process.StandardInput.Write($"{command}; echo {DoneMarker}$?\n");
                process.StandardInput.Flush();

                var deadline = Stopwatch.StartNew();
                while (true)
                {
                    int remainingMs = CommandTimeoutMs - (int)deadline.ElapsedMilliseconds;
                    if (remainingMs <= 0)
                        throw new TimeoutException();

                    string? line = process.StandardOutput.ReadLineAsync()
                        .WaitAsync(TimeSpan.FromMilliseconds(remainingMs), cancellationToken)
                        .GetAwaiter().GetResult();

                    if (line == null)
                        throw new AutomationException("Input failed: ADB shell closed unexpectedly. Input was not retried.");

                    if (line.StartsWith(DoneMarker, StringComparison.Ordinal))
                    {
                        string codeText = line[DoneMarker.Length..].Trim();
                        if (int.TryParse(codeText, out int exitCode) && exitCode == 0)
                            return;
                        throw new AutomationException($"Input failed: ADB shell command '{command}' exited with {codeText}.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                ResetShellLocked();
                throw;
            }
            catch (TimeoutException)
            {
                ResetShellLocked();
                throw new AutomationException($"Input failed: ADB command '{command}' timed out after {CommandTimeoutMs} ms. Input was not retried.");
            }
            catch (AutomationException)
            {
                ResetShellLocked();
                throw;
            }
            catch (Exception e)
            {
                ResetShellLocked();
                throw new AutomationException($"Input failed: ADB shell error: {e.Message}");
            }
        }
    }

    private Process EnsureShellStarted()
    {
        if (shellProcess != null && !shellProcess.HasExited)
            return shellProcess;

        ResetShellLocked();
        var info = new ProcessStartInfo(target.AdbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add("-s");
        info.ArgumentList.Add(target.Serial);
        info.ArgumentList.Add("shell");

        var process = new Process { StartInfo = info };
        process.Start();
        // Drain stderr asynchronously so the pipe buffer never blocks the shell.
        _ = Task.Run(async () =>
        {
            try { await process.StandardError.ReadToEndAsync(); } catch { }
        });
        shellProcess = process;
        return process;
    }

    private void ResetShellLocked()
    {
        if (shellProcess == null) return;
        try { AdbTarget.TryKill(shellProcess); } catch { }
        try { shellProcess.Dispose(); } catch { }
        shellProcess = null;
    }

    public void Dispose()
    {
        lock (shellLock)
        {
            disposed = true;
            ResetShellLocked();
        }
    }
}
