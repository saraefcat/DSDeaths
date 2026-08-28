using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;


namespace DSDeaths {
    class Game {
        public readonly string name;
        public readonly int[] offsets32;
        public readonly int[] offsets64;

        public Game(in string name, in int[] offsets32, in int[] offsets64) {
            this.name = name;
            this.offsets32 = offsets32;
            this.offsets64 = offsets64;
        }
    }

    class Program {
        const int PROCESS_WM_READ = 0x0010;
        const int PROCESS_QUERY_INFORMATION = 0x0400;
        static bool consoleInputUnavailable;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool IsWow64Process(IntPtr hProcess, ref bool Wow64Process);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(
            IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

        static readonly Game[] games =
        {
            new Game("DARKSOULS", new int[] {0xF78700, 0x5C}, null),
            new Game("DarkSoulsII", new int[] {0x1150414, 0x74, 0xB8, 0x34, 0x4, 0x28C, 0x100}, new int[] {0x16148F0, 0xD0, 0x490, 0x104}),
            new Game("DarkSoulsIII", null, new int[] {0x47572B8, 0x98}),
            new Game("DarkSoulsRemastered", null, new int[] {0x1C8A530, 0x98}),
            new Game("Sekiro", null, new int[] {0x3D5AAC0, 0x90}),
            new Game("eldenring", null, new int[] {0x3D61F98, 0x94})
        };

        static bool Write(int value) {
            try {
                File.WriteAllText("DSDeaths.txt", value.ToString());
            } catch (IOException) {
                Console.WriteLine("Could not write to DSDeaths.txt.");
                return false;
            }
            return true;
        }

        static bool IsEldenRing(Game game) {
            return game != null && game.name.Equals("eldenring", StringComparison.OrdinalIgnoreCase);
        }

        static void PrintEldenRingOffsetControls(EldenRingOffsetSettings settings, int rawValue, bool hasRawValue) {
            Console.WriteLine();
            Console.WriteLine("Elden Ring offset controls");
            Console.WriteLine("  Z = set the current raw count as zero and enable the offset");
            Console.WriteLine("  E = enter an exact zero-baseline value and enable the offset");
            Console.WriteLine("  O = toggle the offset ON/OFF");
            Console.WriteLine("  H = show controls and current status");
            Console.WriteLine();
            PrintEldenRingOffsetStatus(settings, rawValue, hasRawValue);
        }

        static void PrintEldenRingOffsetStatus(EldenRingOffsetSettings settings, int rawValue, bool hasRawValue) {
            string state = settings.Enabled ? "ON" : "OFF";
            Console.WriteLine("Offset: " + state + " | zero baseline: " + settings.Offset.ToString(CultureInfo.InvariantCulture));

            if (hasRawValue) {
                Console.WriteLine(
                    "Raw deaths: " + rawValue.ToString(CultureInfo.InvariantCulture) +
                    " | output: " + settings.Apply(rawValue).ToString(CultureInfo.InvariantCulture));
            } else {
                Console.WriteLine("Raw deaths: not available yet");
            }
        }

        static bool HandleEldenRingOffsetInput(
            EldenRingOffsetSettings settings,
            string settingsPath,
            int rawValue,
            bool hasRawValue) {
            if (consoleInputUnavailable) {
                return false;
            }

            bool changed = false;

            while (true) {
                ConsoleKeyInfo key;
                try {
                    if (!Console.KeyAvailable) {
                        return changed;
                    }
                    key = Console.ReadKey(true);
                } catch (InvalidOperationException) {
                    consoleInputUnavailable = true;
                    Console.WriteLine("Interactive offset controls are unavailable because console input is redirected.");
                    return changed;
                } catch (IOException) {
                    consoleInputUnavailable = true;
                    Console.WriteLine("Interactive offset controls are unavailable because console input could not be read.");
                    return changed;
                }

                switch (key.Key) {
                    case ConsoleKey.O:
                        settings.Enabled = !settings.Enabled;
                        SaveEldenRingOffsetSettings(settings, settingsPath);
                        Console.WriteLine();
                        Console.WriteLine("Elden Ring offset turned " + (settings.Enabled ? "ON." : "OFF."));
                        PrintEldenRingOffsetStatus(settings, rawValue, hasRawValue);
                        changed = true;
                        break;

                    case ConsoleKey.Z:
                        if (!hasRawValue || rawValue < 0) {
                            Console.WriteLine("A valid current raw death count is required before setting the zero baseline.");
                            break;
                        }

                        settings.Offset = rawValue;
                        settings.Enabled = true;
                        SaveEldenRingOffsetSettings(settings, settingsPath);
                        Console.WriteLine();
                        Console.WriteLine("Zero baseline set from the current raw death count.");
                        PrintEldenRingOffsetStatus(settings, rawValue, true);
                        changed = true;
                        break;

                    case ConsoleKey.E:
                        Console.WriteLine();
                        Console.Write("New non-negative zero-baseline value: ");
                        string input = Console.ReadLine();
                        int newOffset;
                        if (input == null ||
                            !int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out newOffset) ||
                            newOffset < 0) {
                            Console.WriteLine("Offset was not changed. Enter a non-negative decimal Int32 value.");
                            break;
                        }

                        settings.Offset = newOffset;
                        settings.Enabled = true;
                        SaveEldenRingOffsetSettings(settings, settingsPath);
                        Console.WriteLine("Zero baseline updated and offset turned ON.");
                        PrintEldenRingOffsetStatus(settings, rawValue, hasRawValue);
                        changed = true;
                        break;

                    case ConsoleKey.H:
                        PrintEldenRingOffsetControls(settings, rawValue, hasRawValue);
                        break;
                }
            }
        }

        static void SaveEldenRingOffsetSettings(EldenRingOffsetSettings settings, string settingsPath) {
            string error;
            if (!settings.TrySave(settingsPath, out error)) {
                Console.WriteLine("Could not save " + EldenRingOffsetSettings.FileName + ": " + error);
            }
        }

        static bool PeekMemory(in IntPtr handle, in IntPtr baseAddress, bool isX64, in int[] offsets, ref int value) {
            long address = baseAddress.ToInt64();
            byte[] buffer = new byte[8];
            int discard = 0;

            foreach (int offset in offsets) {
                if (address == 0) {
                    return false;
                }

                address += offset;

                if (!ReadProcessMemory(handle, (IntPtr)address, buffer, 8, ref discard)) {
                    Console.WriteLine("Could not read game memory.");
                    return false;
                }

                address = isX64 ? BitConverter.ToInt64(buffer, 0) : BitConverter.ToInt32(buffer, 0);
            }

            value = (int)address;
            return true;
        }



        static bool ScanProcesses(ref Process proc, ref Game game) {
            foreach (Game g in games) {
                Process[] process = Process.GetProcessesByName(g.name);
                if (process.Length != 0) {
                    Console.WriteLine("Found: " + g.name);
                    proc = process[0];
                    game = g;
                    return true;
                }
            }
            return false;
        }

        static void Main() {
            Console.CancelKeyPress += delegate {
                Write(0);
            };

            // put DSDeaths.txt in the same directory as the exe
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, EldenRingOffsetSettings.FileName);
            string settingsWarning;
            EldenRingOffsetSettings eldenRingOffset = EldenRingOffsetSettings.Load(settingsPath, out settingsWarning);

            Console.WriteLine("-----------------------------------WARNING-----------------------------------");
            Console.WriteLine(" Does NOT work with Elden Ring if Easy Anti-Cheat (EAC) is running.");
            Console.WriteLine(" Possible risk of BANS by trying to use with EAC enabled.");
            Console.WriteLine(" USE AT YOUR OWN RISK.");
            Console.WriteLine("-----------------------------------WARNING-----------------------------------");
            Console.WriteLine();

            if (!string.IsNullOrEmpty(settingsWarning)) {
                Console.WriteLine(settingsWarning);
                Console.WriteLine();
            }

            PrintEldenRingOffsetStatus(eldenRingOffset, 0, false);
            Console.WriteLine();

            while (true) {
                Write(0);
                Console.WriteLine("Looking for Dark Souls process...");

                Process proc = null;
                Game game = null;

                while (!ScanProcesses(ref proc, ref game)) {
                    Thread.Sleep(500);
                }

                IntPtr handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_WM_READ, false, proc.Id);
                IntPtr baseAddress = proc.MainModule.BaseAddress;
                int oldValue = int.MinValue, oldRawValue = int.MinValue, value = 0;

                bool isWow64 = false;
                if (IsWow64Process(handle, ref isWow64)) {
                    Console.WriteLine("Found " + (isWow64 ? "32" : "64") + " bit variant.");
                    int[] offsets = isWow64 ? game.offsets32 : game.offsets64;
                    bool isEldenRing = IsEldenRing(game);

                    if (isEldenRing) {
                        PrintEldenRingOffsetControls(eldenRingOffset, value, false);
                    }

                    while (!proc.HasExited) {
                        bool hasRawValue = PeekMemory(handle, baseAddress, !isWow64, offsets, ref value);

                        if (isEldenRing &&
                            HandleEldenRingOffsetInput(eldenRingOffset, settingsPath, value, hasRawValue)) {
                            oldValue = int.MinValue;
                        }

                        if (hasRawValue) {
                            int outputValue = isEldenRing ? eldenRingOffset.Apply(value) : value;

                            if (isEldenRing && eldenRingOffset.Enabled && value < eldenRingOffset.Offset &&
                                value != oldRawValue) {
                                Console.WriteLine(
                                    "Raw deaths are below the zero baseline. " +
                                    "The output is clamped to 0; no character may be loaded, or a different character may be active.");
                            }

                            oldRawValue = value;

                            if (outputValue != oldValue) {
                                oldValue = outputValue;
                                Write(outputValue);

                                if (isEldenRing && eldenRingOffset.Enabled) {
                                    Console.WriteLine(
                                        "Deaths: " + outputValue.ToString(CultureInfo.InvariantCulture) +
                                        " (raw: " + value.ToString(CultureInfo.InvariantCulture) +
                                        ", zero baseline: " + eldenRingOffset.Offset.ToString(CultureInfo.InvariantCulture) + ")");
                                } else {
                                    Console.WriteLine("Deaths: " + outputValue.ToString(CultureInfo.InvariantCulture));
                                }
                            }
                        }
                        Thread.Sleep(500);
                    }
                }

                Console.WriteLine("Process has exited.");
                Thread.Sleep(2000);
            }
        }
    }
}
