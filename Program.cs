using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace DSDeaths {
    internal static class Program {
        private static readonly ManualResetEvent Shutdown = new ManualResetEvent(false);
        private static bool consoleInputUnavailable;
        private static MonitorState lastPrintedState = MonitorState.Stopped;
        private static int lastPrintedRawValue = int.MinValue;
        private static int lastPrintedOutputValue = int.MinValue;
        private static string lastPrintedMessage;

        private static void Main() {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            SingleInstanceGuard instanceGuard;
            if (!SingleInstanceGuard.TryAcquire(out instanceGuard)) {
                instanceGuard.Dispose();
                Console.WriteLine("Another DSDeaths counter is already running.");
                Console.WriteLine("Close DSDeaths or DSDeaths Live before starting this instance.");
                PauseWhenInteractive();
                return;
            }

            using (instanceGuard)
            using (var monitor = new DSDeathsMonitor(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DSDeathsMonitor.OutputFileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, EldenRingOffsetSettings.FileName))) {
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs arguments) {
                    arguments.Cancel = true;
                    Shutdown.Set();
                };

                PrintSafetyWarning();
                if (!string.IsNullOrEmpty(monitor.SettingsWarning)) {
                    Console.WriteLine(monitor.SettingsWarning);
                    Console.WriteLine();
                }

                PrintOffsetStatus(monitor, monitor.LatestSnapshot);
                Console.WriteLine();

                monitor.SnapshotChanged += OnSnapshotChanged;
                monitor.Start();

                while (!Shutdown.WaitOne(100)) {
                    HandleOffsetInput(monitor);
                }
            }
        }

        private static void PrintSafetyWarning() {
            Console.WriteLine("-----------------------------------WARNING-----------------------------------");
            Console.WriteLine(" Does NOT work with Elden Ring if Easy Anti-Cheat (EAC) is running.");
            Console.WriteLine(" Possible risk of BANS by trying to use with EAC enabled.");
            Console.WriteLine(" USE AT YOUR OWN RISK.");
            Console.WriteLine("-----------------------------------WARNING-----------------------------------");
            Console.WriteLine();
        }

        private static void OnSnapshotChanged(object sender, MonitorSnapshotEventArgs arguments) {
            MonitorSnapshot snapshot = arguments.Snapshot;

            if (snapshot.State != lastPrintedState) {
                switch (snapshot.State) {
                    case MonitorState.Searching:
                        Console.WriteLine("Looking for a supported game process...");
                        break;
                    case MonitorState.Connecting:
                        Console.WriteLine("Found: " + snapshot.Game.DisplayName);
                        break;
                    case MonitorState.Monitoring:
                        Console.WriteLine("Monitoring " + snapshot.Game.DisplayName +
                                          " (" + (snapshot.Is64Bit ? "64" : "32") + " bit).");
                        if (snapshot.Game.IsEldenRing) {
                            PrintOffsetControls((DSDeathsMonitor)sender, snapshot);
                        }
                        break;
                    case MonitorState.Unsupported:
                        Console.WriteLine("This process variant cannot be monitored safely.");
                        break;
                    case MonitorState.Error:
                        Console.WriteLine("Monitoring error.");
                        break;
                    case MonitorState.Stopped:
                        Console.WriteLine("Monitoring stopped.");
                        break;
                }
                lastPrintedState = snapshot.State;
            }

            if (!string.IsNullOrEmpty(snapshot.Message) &&
                !string.Equals(lastPrintedMessage, snapshot.Message, StringComparison.Ordinal)) {
                Console.WriteLine(snapshot.Message);
                lastPrintedMessage = snapshot.Message;
            }

            if (!snapshot.OutputWriteSucceeded && !string.IsNullOrEmpty(snapshot.OutputError)) {
                Console.WriteLine("Could not write to " + DSDeathsMonitor.OutputFileName + ": " +
                                  snapshot.OutputError);
            }

            if (snapshot.State == MonitorState.Monitoring && snapshot.HasDeathCount &&
                (snapshot.RawDeathCount != lastPrintedRawValue ||
                 snapshot.DeathCount != lastPrintedOutputValue)) {
                if (snapshot.Game.IsEldenRing && snapshot.OffsetEnabled) {
                    if (snapshot.RawDeathCount < snapshot.Offset) {
                        Console.WriteLine(
                            "Raw deaths are below the zero baseline. The output is clamped to 0; " +
                            "no character may be loaded, or a different character may be active.");
                    }
                    Console.WriteLine(
                        "Deaths: " + snapshot.DeathCount.ToString(CultureInfo.InvariantCulture) +
                        " (raw: " + snapshot.RawDeathCount.ToString(CultureInfo.InvariantCulture) +
                        ", zero baseline: " + snapshot.Offset.ToString(CultureInfo.InvariantCulture) + ")");
                } else {
                    Console.WriteLine("Deaths: " +
                                      snapshot.DeathCount.ToString(CultureInfo.InvariantCulture));
                }

                lastPrintedRawValue = snapshot.RawDeathCount;
                lastPrintedOutputValue = snapshot.DeathCount;
            }
        }

        private static void HandleOffsetInput(DSDeathsMonitor monitor) {
            if (consoleInputUnavailable) {
                return;
            }

            ConsoleKeyInfo key;
            try {
                if (!Console.KeyAvailable) {
                    return;
                }
                key = Console.ReadKey(true);
            } catch (InvalidOperationException) {
                consoleInputUnavailable = true;
                return;
            } catch (IOException) {
                consoleInputUnavailable = true;
                return;
            }

            MonitorSnapshot snapshot = monitor.LatestSnapshot;
            if (snapshot.Game == null || !snapshot.Game.IsEldenRing) {
                return;
            }

            string error;
            switch (key.Key) {
                case ConsoleKey.O:
                    if (!monitor.TryToggleOffset(out error)) {
                        Console.WriteLine(error);
                    } else {
                        PrintOffsetStatus(monitor, monitor.LatestSnapshot);
                    }
                    break;

                case ConsoleKey.Z:
                    if (!monitor.TrySetCurrentAsZero(out error)) {
                        Console.WriteLine(error);
                    } else {
                        Console.WriteLine("Zero baseline set from the current raw death count.");
                        PrintOffsetStatus(monitor, monitor.LatestSnapshot);
                    }
                    break;

                case ConsoleKey.E:
                    Console.WriteLine();
                    Console.Write("New non-negative zero-baseline value: ");
                    string input = Console.ReadLine();
                    int offset;
                    if (input == null ||
                        !int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out offset) ||
                        offset < 0) {
                        Console.WriteLine("Offset was not changed. Enter a non-negative decimal Int32 value.");
                    } else if (!monitor.TrySetOffset(offset, out error)) {
                        Console.WriteLine(error);
                    } else {
                        Console.WriteLine("Zero baseline updated and offset turned ON.");
                        PrintOffsetStatus(monitor, monitor.LatestSnapshot);
                    }
                    break;

                case ConsoleKey.H:
                    PrintOffsetControls(monitor, snapshot);
                    break;
            }
        }

        private static void PrintOffsetControls(DSDeathsMonitor monitor, MonitorSnapshot snapshot) {
            Console.WriteLine();
            Console.WriteLine("Elden Ring offset controls");
            Console.WriteLine("  Z = set the current raw count as zero and enable the offset");
            Console.WriteLine("  E = enter an exact zero-baseline value and enable the offset");
            Console.WriteLine("  O = toggle the offset ON/OFF");
            Console.WriteLine("  H = show controls and current status");
            Console.WriteLine();
            PrintOffsetStatus(monitor, snapshot);
        }

        private static void PrintOffsetStatus(DSDeathsMonitor monitor, MonitorSnapshot snapshot) {
            bool enabled;
            int offset;
            monitor.GetOffsetConfiguration(out enabled, out offset);
            Console.WriteLine("Offset: " + (enabled ? "ON" : "OFF") +
                              " | zero baseline: " + offset.ToString(CultureInfo.InvariantCulture));

            if (snapshot != null && snapshot.HasDeathCount &&
                snapshot.Game != null && snapshot.Game.IsEldenRing) {
                int output = enabled ? Math.Max(0, snapshot.RawDeathCount - offset) : snapshot.RawDeathCount;
                Console.WriteLine(
                    "Raw deaths: " + snapshot.RawDeathCount.ToString(CultureInfo.InvariantCulture) +
                    " | output: " + output.ToString(CultureInfo.InvariantCulture));
            } else {
                Console.WriteLine("Raw deaths: not available yet");
            }
        }

        private static void PauseWhenInteractive() {
            try {
                if (!Console.IsInputRedirected) {
                    Console.WriteLine("Press any key to close.");
                    Console.ReadKey(true);
                }
            } catch (IOException) {
            } catch (InvalidOperationException) {
            }
        }
    }
}
