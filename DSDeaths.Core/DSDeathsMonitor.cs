using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DSDeaths {
    public enum MonitorState {
        Stopped,
        Searching,
        Connecting,
        Monitoring,
        Unsupported,
        Error
    }

    public sealed class MonitorSnapshot {
        internal MonitorSnapshot(
            MonitorState state,
            GameDefinition game,
            bool is64Bit,
            bool hasDeathCount,
            int rawDeathCount,
            int deathCount,
            bool offsetEnabled,
            int offset,
            bool outputWriteSucceeded,
            string outputError,
            string message) {
            State = state;
            Game = game;
            Is64Bit = is64Bit;
            HasDeathCount = hasDeathCount;
            RawDeathCount = rawDeathCount;
            DeathCount = deathCount;
            OffsetEnabled = offsetEnabled;
            Offset = offset;
            OutputWriteSucceeded = outputWriteSucceeded;
            OutputError = outputError;
            Message = message;
        }

        public MonitorState State { get; private set; }
        public GameDefinition Game { get; private set; }
        public bool Is64Bit { get; private set; }
        public bool HasDeathCount { get; private set; }
        public int RawDeathCount { get; private set; }
        public int DeathCount { get; private set; }
        public bool OffsetEnabled { get; private set; }
        public int Offset { get; private set; }
        public bool OutputWriteSucceeded { get; private set; }
        public string OutputError { get; private set; }
        public string Message { get; private set; }

        internal bool IsEquivalentTo(MonitorSnapshot other) {
            return other != null &&
                   State == other.State &&
                   ReferenceEquals(Game, other.Game) &&
                   Is64Bit == other.Is64Bit &&
                   HasDeathCount == other.HasDeathCount &&
                   RawDeathCount == other.RawDeathCount &&
                   DeathCount == other.DeathCount &&
                   OffsetEnabled == other.OffsetEnabled &&
                   Offset == other.Offset &&
                   OutputWriteSucceeded == other.OutputWriteSucceeded &&
                   string.Equals(OutputError, other.OutputError, StringComparison.Ordinal) &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }
    }

    public sealed class MonitorSnapshotEventArgs : EventArgs {
        internal MonitorSnapshotEventArgs(MonitorSnapshot snapshot) {
            Snapshot = snapshot;
        }

        public MonitorSnapshot Snapshot { get; private set; }
    }

    public sealed class DSDeathsMonitor : IDisposable {
        public const string OutputFileName = "DSDeaths.txt";

        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessQueryInformation = 0x0400;

        private readonly object sync = new object();
        private readonly ManualResetEvent stopSignal = new ManualResetEvent(false);
        private readonly string outputPath;
        private readonly string settingsPath;
        private readonly EldenRingOffsetSettings offsetSettings;

        private Thread worker;
        private MonitorSnapshot latestSnapshot;
        private bool disposed;
        private bool outputWriteSucceeded = true;
        private int lastWrittenValue = int.MinValue;
        private string outputError;
        private volatile bool forceRefresh;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr process, ref bool wow64Process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr process,
            IntPtr baseAddress,
            byte[] buffer,
            int size,
            ref int bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public DSDeathsMonitor(string outputPath, string settingsPath) {
            if (string.IsNullOrWhiteSpace(outputPath)) {
                throw new ArgumentException("An output path is required.", "outputPath");
            }
            if (string.IsNullOrWhiteSpace(settingsPath)) {
                throw new ArgumentException("A settings path is required.", "settingsPath");
            }

            this.outputPath = outputPath;
            this.settingsPath = settingsPath;

            string warning;
            offsetSettings = EldenRingOffsetSettings.Load(settingsPath, out warning);
            SettingsWarning = warning;
            latestSnapshot = CreateSnapshot(MonitorState.Stopped, null, false, false, 0, 0, null);
        }

        public event EventHandler<MonitorSnapshotEventArgs> SnapshotChanged;

        public string OutputPath {
            get { return outputPath; }
        }

        public string SettingsPath {
            get { return settingsPath; }
        }

        public string SettingsWarning { get; private set; }

        public MonitorSnapshot LatestSnapshot {
            get {
                lock (sync) {
                    return latestSnapshot;
                }
            }
        }

        public void Start() {
            lock (sync) {
                ThrowIfDisposed();
                if (worker != null) {
                    return;
                }

                stopSignal.Reset();
                worker = new Thread(Run) {
                    IsBackground = true,
                    Name = "DSDeaths game monitor"
                };
                worker.Start();
            }
        }

        public void Stop() {
            Thread thread;
            lock (sync) {
                thread = worker;
                if (thread != null) {
                    stopSignal.Set();
                }
            }

            if (thread != null && thread != Thread.CurrentThread) {
                thread.Join(10000);
            }

            lock (sync) {
                if (thread != null && ReferenceEquals(worker, thread) && !thread.IsAlive) {
                    worker = null;
                }
            }

            TryWriteOutput(0, true);
            Publish(CreateSnapshot(MonitorState.Stopped, null, false, false, 0, 0, null));
        }

        public void GetOffsetConfiguration(out bool enabled, out int offset) {
            lock (sync) {
                enabled = offsetSettings.Enabled;
                offset = offsetSettings.Offset;
            }
        }

        public bool TrySetOffsetEnabled(bool enabled, out string error) {
            if (!IsEldenRingConnected()) {
                error = "Elden Ring is not currently connected.";
                return false;
            }

            lock (sync) {
                bool previous = offsetSettings.Enabled;
                offsetSettings.Enabled = enabled;
                if (!offsetSettings.TrySave(settingsPath, out error)) {
                    offsetSettings.Enabled = previous;
                    return false;
                }
                forceRefresh = true;
            }

            return true;
        }

        public bool TryToggleOffset(out string error) {
            bool enabled;
            int ignored;
            GetOffsetConfiguration(out enabled, out ignored);
            return TrySetOffsetEnabled(!enabled, out error);
        }

        public bool TrySetCurrentAsZero(out string error) {
            MonitorSnapshot snapshot = LatestSnapshot;
            if (snapshot.Game == null || !snapshot.Game.IsEldenRing || !snapshot.HasDeathCount) {
                error = "A valid Elden Ring death count is required before setting the zero baseline.";
                return false;
            }

            return TrySetOffset(snapshot.RawDeathCount, out error);
        }

        public bool TrySetOffset(int offset, out string error) {
            if (offset < 0) {
                error = "The zero baseline must be a non-negative Int32 value.";
                return false;
            }
            if (!IsEldenRingConnected()) {
                error = "Elden Ring is not currently connected.";
                return false;
            }

            lock (sync) {
                int previousOffset = offsetSettings.Offset;
                bool previousEnabled = offsetSettings.Enabled;
                offsetSettings.Offset = offset;
                offsetSettings.Enabled = true;

                if (!offsetSettings.TrySave(settingsPath, out error)) {
                    offsetSettings.Offset = previousOffset;
                    offsetSettings.Enabled = previousEnabled;
                    return false;
                }
                forceRefresh = true;
            }

            return true;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            Stop();
            disposed = true;
            stopSignal.Dispose();
        }

        private void Run() {
            try {
                while (!stopSignal.WaitOne(0)) {
                    TryWriteOutput(0, false);
                    Publish(CreateSnapshot(MonitorState.Searching, null, false, false, 0, 0, null));

                    GameDefinition game;
                    Process process = WaitForSupportedProcess(out game);
                    if (process == null) {
                        break;
                    }

                    try {
                        MonitorProcess(process, game);
                    } finally {
                        process.Dispose();
                    }

                    if (!stopSignal.WaitOne(2000)) {
                        continue;
                    }
                }
            } catch (Exception exception) {
                TryWriteOutput(0, true);
                Publish(CreateSnapshot(
                    MonitorState.Error,
                    null,
                    false,
                    false,
                    0,
                    0,
                    "The monitor stopped unexpectedly: " + exception.Message));
            } finally {
                lock (sync) {
                    if (ReferenceEquals(worker, Thread.CurrentThread)) {
                        worker = null;
                    }
                }
            }
        }

        private Process WaitForSupportedProcess(out GameDefinition game) {
            game = null;
            while (!stopSignal.WaitOne(0)) {
                foreach (GameDefinition candidate in GameCatalog.InternalGames) {
                    Process[] processes = Process.GetProcessesByName(candidate.ProcessName);
                    if (processes.Length == 0) {
                        continue;
                    }

                    Process selected = processes[0];
                    for (int index = 1; index < processes.Length; index++) {
                        processes[index].Dispose();
                    }

                    game = candidate;
                    Publish(CreateSnapshot(MonitorState.Connecting, game, false, false, 0, 0, null));
                    return selected;
                }

                stopSignal.WaitOne(500);
            }

            return null;
        }

        private void MonitorProcess(Process process, GameDefinition game) {
            IntPtr handle = OpenProcess(
                ProcessQueryInformation | ProcessVmRead,
                false,
                process.Id);
            if (handle == IntPtr.Zero) {
                Publish(CreateSnapshot(
                    MonitorState.Error,
                    game,
                    false,
                    false,
                    0,
                    0,
                    "Could not open the game process for read-only access. " + DescribeLastWin32Error()));
                return;
            }

            try {
                ProcessModule module;
                try {
                    module = process.MainModule;
                } catch (Exception exception) {
                    Publish(CreateSnapshot(
                        MonitorState.Error,
                        game,
                        false,
                        false,
                        0,
                        0,
                        "Could not inspect the game module: " + exception.Message));
                    return;
                }

                bool isWow64 = false;
                if (!IsWow64Process(handle, ref isWow64)) {
                    Publish(CreateSnapshot(
                        MonitorState.Error,
                        game,
                        false,
                        false,
                        0,
                        0,
                        "Could not determine the game process architecture. " + DescribeLastWin32Error()));
                    return;
                }

                bool is64Bit = !isWow64;
                int[] offsets = is64Bit ? game.Offsets64 : game.Offsets32;
                string resolutionMessage = null;

                if (game.IsEldenRing) {
                    if (!is64Bit) {
                        offsets = null;
                        resolutionMessage = "Elden Ring must be a 64-bit process.";
                    } else {
                        int resolvedRva;
                        long signatureAddress;
                        string resolutionError;
                        if (EldenRingSignatureResolver.TryResolve(
                                handle,
                                module.BaseAddress,
                                module.ModuleMemorySize,
                                out resolvedRva,
                                out signatureAddress,
                                out resolutionError)) {
                            offsets = new[] {resolvedRva, EldenRingSignature.FieldOffset};
                            long signatureRva = signatureAddress - module.BaseAddress.ToInt64();
                            resolutionMessage =
                                "Signature resolved uniquely (getter RVA 0x" +
                                signatureRva.ToString("X8", CultureInfo.InvariantCulture) +
                                ", pointer RVA 0x" +
                                resolvedRva.ToString("X8", CultureInfo.InvariantCulture) + ").";
                        } else {
                            offsets = null;
                            resolutionMessage = resolutionError;
                        }
                    }
                }

                if (offsets == null) {
                    Publish(CreateSnapshot(
                        MonitorState.Unsupported,
                        game,
                        is64Bit,
                        false,
                        0,
                        0,
                        resolutionMessage ?? "This process variant cannot be monitored safely."));
                    WaitForProcessExit(process);
                    return;
                }

                MonitorDeathCount(process, handle, module.BaseAddress, game, is64Bit, offsets, resolutionMessage);
            } finally {
                CloseHandle(handle);
            }
        }

        private void MonitorDeathCount(
            Process process,
            IntPtr handle,
            IntPtr moduleBase,
            GameDefinition game,
            bool is64Bit,
            int[] offsets,
            string initialMessage) {
            int rawValue = 0;
            bool firstRead = true;

            while (!stopSignal.WaitOne(0) && !HasExited(process)) {
                string readError;
                if (!TryReadDeathCount(handle, moduleBase, is64Bit, offsets, out rawValue, out readError)) {
                    Publish(CreateSnapshot(
                        MonitorState.Error,
                        game,
                        is64Bit,
                        false,
                        0,
                        0,
                        readError));
                } else {
                    int outputValue = ApplyOffset(game, rawValue);
                    TryWriteOutput(outputValue, forceRefresh);
                    forceRefresh = false;
                    Publish(CreateSnapshot(
                        MonitorState.Monitoring,
                        game,
                        is64Bit,
                        true,
                        rawValue,
                        outputValue,
                        firstRead ? initialMessage : null));
                    firstRead = false;
                }

                stopSignal.WaitOne(500);
            }
        }

        private void WaitForProcessExit(Process process) {
            while (!stopSignal.WaitOne(500) && !HasExited(process)) {
            }
        }

        private static bool HasExited(Process process) {
            try {
                return process.HasExited;
            } catch (InvalidOperationException) {
                return true;
            } catch (Win32Exception) {
                return true;
            }
        }

        private static bool TryReadDeathCount(
            IntPtr handle,
            IntPtr moduleBase,
            bool is64Bit,
            int[] offsets,
            out int value,
            out string error) {
            long address = moduleBase.ToInt64();
            byte[] buffer = new byte[8];

            foreach (int offset in offsets) {
                if (address == 0) {
                    value = 0;
                    error = "The death-count pointer chain contained a null pointer.";
                    return false;
                }

                address += offset;
                int bytesRead = 0;
                int requestedSize = is64Bit ? 8 : 4;
                if (!ReadProcessMemory(
                        handle,
                        new IntPtr(address),
                        buffer,
                        requestedSize,
                        ref bytesRead) ||
                    bytesRead != requestedSize) {
                    value = 0;
                    error = "Could not read game memory. " + DescribeLastWin32Error();
                    return false;
                }

                address = is64Bit ? BitConverter.ToInt64(buffer, 0) : BitConverter.ToInt32(buffer, 0);
            }

            value = unchecked((int)address);
            error = null;
            return true;
        }

        private int ApplyOffset(GameDefinition game, int rawValue) {
            lock (sync) {
                return game.IsEldenRing ? offsetSettings.Apply(rawValue) : rawValue;
            }
        }

        private bool IsEldenRingConnected() {
            MonitorSnapshot snapshot = LatestSnapshot;
            return snapshot.Game != null &&
                   snapshot.Game.IsEldenRing &&
                   (snapshot.State == MonitorState.Monitoring || snapshot.State == MonitorState.Connecting);
        }

        private bool TryWriteOutput(int value, bool force) {
            lock (sync) {
                if (!force && outputWriteSucceeded && lastWrittenValue == value) {
                    return true;
                }

                try {
                    File.WriteAllText(outputPath, value.ToString(CultureInfo.InvariantCulture));
                    outputWriteSucceeded = true;
                    outputError = null;
                    lastWrittenValue = value;
                    return true;
                } catch (IOException exception) {
                    outputWriteSucceeded = false;
                    outputError = exception.Message;
                    return false;
                } catch (UnauthorizedAccessException exception) {
                    outputWriteSucceeded = false;
                    outputError = exception.Message;
                    return false;
                }
            }
        }

        private MonitorSnapshot CreateSnapshot(
            MonitorState state,
            GameDefinition game,
            bool is64Bit,
            bool hasDeathCount,
            int rawValue,
            int outputValue,
            string message) {
            lock (sync) {
                return new MonitorSnapshot(
                    state,
                    game,
                    is64Bit,
                    hasDeathCount,
                    rawValue,
                    outputValue,
                    offsetSettings.Enabled,
                    offsetSettings.Offset,
                    outputWriteSucceeded,
                    outputError,
                    message);
            }
        }

        private void Publish(MonitorSnapshot snapshot) {
            EventHandler<MonitorSnapshotEventArgs> handler;
            lock (sync) {
                if (snapshot.IsEquivalentTo(latestSnapshot)) {
                    return;
                }
                latestSnapshot = snapshot;
                handler = SnapshotChanged;
            }

            if (handler == null) {
                return;
            }

            var arguments = new MonitorSnapshotEventArgs(snapshot);
            foreach (EventHandler<MonitorSnapshotEventArgs> subscriber in handler.GetInvocationList()) {
                try {
                    subscriber(this, arguments);
                } catch (Exception) {
                }
            }
        }

        private static string DescribeLastWin32Error() {
            int errorCode = Marshal.GetLastWin32Error();
            if (errorCode == 0) {
                return "Unknown Windows error.";
            }
            return new Win32Exception(errorCode).Message + " (code " +
                   errorCode.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private void ThrowIfDisposed() {
            if (disposed) {
                throw new ObjectDisposedException("DSDeathsMonitor");
            }
        }
    }
}
