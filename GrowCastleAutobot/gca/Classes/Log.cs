using gca.Script;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace gca.Classes
{
    public static class Log
    {
        public const long DefaultMaxLogBytes = 5 * 1024 * 1024; // 5 MB
        public const int DefaultMaxBackups = 3;

        private static readonly object writeLock = new();
        private static string logFilePath = Cst.LOG_FILE_PATH;
        private static long maxLogBytes = DefaultMaxLogBytes;
        private static int maxBackups = DefaultMaxBackups;

        private static string DTN => DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff");

        public static void Configure(string? path = null, long? maxBytes = null, int? backups = null)
        {
            lock (writeLock)
            {
                if (!string.IsNullOrWhiteSpace(path))
                    logFilePath = path;
                if (maxBytes.HasValue && maxBytes.Value > 0)
                    maxLogBytes = maxBytes.Value;
                if (backups.HasValue && backups.Value >= 0)
                    maxBackups = backups.Value;
            }
        }

        public static void ResetConfiguration()
        {
            lock (writeLock)
            {
                logFilePath = Cst.LOG_FILE_PATH;
                maxLogBytes = DefaultMaxLogBytes;
                maxBackups = DefaultMaxBackups;
            }
        }

        private static void SaveLog(string message, string logLevel, int line, string filePath)
        {
            string source = string.IsNullOrEmpty(filePath)
                ? "Unknown"
                : Path.GetFileNameWithoutExtension(filePath);
            int threadId = Environment.CurrentManagedThreadId;
            string entry = $"[{DTN} T{threadId:D2} {source}:{line,-4}] [{logLevel}] {message}\n";
            WriteRaw(entry);
        }

        private static void WriteRaw(string entry)
        {
            Debug.Write(entry);
            lock (writeLock)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        string fullPath = Path.GetFullPath(logFilePath);
                        string? dir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        RotateIfNeededLocked(fullPath);
                        File.AppendAllText(fullPath, entry, Encoding.UTF8);
                        return;
                    }
                    catch (IOException) when (attempt == 0)
                    {
                        Thread.Sleep(10);
                    }
                    catch (Exception ex)
                    {
                        Debug.Write($"[Log write failed] {ex.Message}\n");
                        return;
                    }
                }
            }
        }

        private static void RotateIfNeededLocked(string fullPath)
        {
            if (maxLogBytes <= 0 || !File.Exists(fullPath))
                return;

            var info = new FileInfo(fullPath);
            if (info.Length < maxLogBytes)
                return;

            if (maxBackups <= 0)
            {
                File.WriteAllText(fullPath, string.Empty, Encoding.UTF8);
                return;
            }

            string oldest = $"{fullPath}.{maxBackups}";
            if (File.Exists(oldest))
                File.Delete(oldest);

            for (int i = maxBackups - 1; i >= 1; i--)
            {
                string src = $"{fullPath}.{i}";
                string dst = $"{fullPath}.{i + 1}";
                if (File.Exists(src))
                    File.Move(src, dst, overwrite: true);
            }

            File.Move(fullPath, $"{fullPath}.1", overwrite: true);
        }

        [Conditional("DEBUG")]
        public static void D(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "D", line, file);
        }

        [Conditional("DEBUG")]
        public static void T(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "T", line, file);
        }

        public static void U(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "U", line, file);
        }

        public static void UC(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "-", line, file);
        }
        public static void O(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "O", line, file);
        }
        public static void N(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "N", line, file);
        }
        public static void K(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "K", line, file);
        }
        public static void R(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "R", line, file);
        }
        public static void X(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "X", line, file);
        }
        public static void M(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "M", line, file);
        }
        public static void S(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "S", line, file);
        }
        public static void H(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "H", line, file);
        }
        public static void P(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "P", line, file);
        }
        public static void I(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "I", line, file);
        }
        public static void C(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "C", line, file);
        }

        public static void W(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "W", line, file);
        }

        public static void E(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "E", line, file);
        }

        public static void Q(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "Q", line, file);
        }
        public static void L(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "L", line, file);
        }

        public static void F(string message, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            SaveLog(message, "F", line, file);
        }

        public static void ST(
            [CallerLineNumber] int line = 0,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "")
        {
            // skip 2 because 1-st is current method, and for second we need line number, and name is known
            var st = new StackTrace(2, true).ToString();
            string source = string.IsNullOrEmpty(file) ? "Unknown" : Path.GetFileNameWithoutExtension(file);
            var callerInfo = $"   at {source}.{member}():line {line}";
            string log = $"[{DTN} T{Environment.CurrentManagedThreadId:D2}] [ST] Stack trace:\n{callerInfo}\n{st}\n";
            WriteRaw(log);
        }
    }
}
