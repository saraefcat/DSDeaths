using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DSDeaths.Live {
    internal static class DiagnosticLogger {
        private const long MaximumLogBytes = 1024 * 1024;
        private static readonly object Sync = new object();
        private static string lastSnapshotKey;

        internal static string LogPath {
            get {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "DSDeaths.Live.log");
            }
        }

        internal static void Write(string message) {
            lock (Sync) {
                try {
                    RotateIfNeeded();
                    string line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) +
                                  " | " + (message ?? string.Empty) + Environment.NewLine;
                    File.AppendAllText(LogPath, line, new UTF8Encoding(false));
                } catch (IOException) {
                } catch (UnauthorizedAccessException) {
                }
            }
        }

        internal static void WriteException(string source, Exception exception) {
            Write(source + Environment.NewLine + exception);
        }

        internal static void WriteSnapshot(MonitorSnapshot snapshot) {
            if (snapshot == null) {
                return;
            }

            string game = snapshot.Game == null ? "none" : snapshot.Game.DisplayName;
            string key = string.Join(
                "|",
                game,
                snapshot.State.ToString(),
                snapshot.Is64Bit.ToString(CultureInfo.InvariantCulture),
                snapshot.OutputWriteSucceeded.ToString(CultureInfo.InvariantCulture),
                snapshot.OutputError ?? string.Empty,
                snapshot.MessageCode.ToString(),
                snapshot.Message ?? string.Empty);

            lock (Sync) {
                if (string.Equals(lastSnapshotKey, key, StringComparison.Ordinal)) {
                    return;
                }
                lastSnapshotKey = key;
            }

            Write(
                "Monitor state=" + snapshot.State +
                ", game=" + game +
                ", architecture=" + (snapshot.Is64Bit ? "64-bit" : "unknown/32-bit") +
                ", output=" + (snapshot.OutputWriteSucceeded ? "ready" : "error") +
                (string.IsNullOrEmpty(snapshot.Message) ? string.Empty :
                    ", detail=" + snapshot.Message) +
                (string.IsNullOrEmpty(snapshot.OutputError) ? string.Empty :
                    ", outputDetail=" + snapshot.OutputError));
        }

        private static void RotateIfNeeded() {
            if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaximumLogBytes) {
                return;
            }

            string previousPath = LogPath + ".previous";
            if (File.Exists(previousPath)) {
                File.Delete(previousPath);
            }
            File.Move(LogPath, previousPath);
        }
    }
}
