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

    public enum MonitorMessageCode {
        None,
        UnexpectedFailure,
        OpenProcessFailed,
        InspectModuleFailed,
        ArchitectureDetectionFailed,
        EldenRingRequires64Bit,
        SignatureResolved,
        SignatureResolutionFailed,
        UnsupportedVariant,
        NullPointer,
        ReadMemoryFailed
    }

    public enum MonitorOperationErrorCode {
        None,
        EldenRingNotConnected,
        ValidEldenRingCountRequired,
        InvalidZeroBaseline,
        SettingsSaveFailed
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
            string message,
            MonitorMessageCode messageCode,
            string messageArgument0,
            string messageArgument1) {
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
            MessageCode = messageCode;
            MessageArgument0 = messageArgument0;
            MessageArgument1 = messageArgument1;
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
        public MonitorMessageCode MessageCode { get; private set; }
        public string MessageArgument0 { get; private set; }
        public string MessageArgument1 { get; private set; }

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
                   string.Equals(Message, other.Message, StringComparison.Ordinal) &&
                   MessageCode == other.MessageCode &&
                   string.Equals(MessageArgument0, other.MessageArgument0, StringComparison.Ordinal) &&
                   string.Equals(MessageArgument1, other.MessageArgument1, StringComparison.Ordinal);
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
            UIntPtr size,
            out UIntPtr bytesRead);

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
            OffsetSettingsWarning[] warningDetails;
            offsetSettings = EldenRingOffsetSettings.Load(settingsPath, out warning, out warningDetails);
            SettingsWarning = warning;
            SettingsWarnings = warningDetails;
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

        public OffsetSettingsWarning[] SettingsWarnings { get; private set; }

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
            MonitorOperationErrorCode ignored;
            return TrySetOffsetEnabled(enabled, out error, out ignored);
        }

        public bool TrySetOffsetEnabled(
            bool enabled,
            out string error,
            out MonitorOperationErrorCode errorCode) {
            if (!IsEldenRingConnected()) {
                error = "Elden Ring is not currently connected.";
                errorCode = MonitorOperationErrorCode.EldenRingNotConnected;
                return false;
            }

            lock (sync) {
                bool previous = offsetSettings.Enabled;
                offsetSettings.Enabled = enabled;
                if (!offsetSettings.TrySave(settingsPath, out error)) {
                    offsetSettings.Enabled = previous;
                    errorCode = MonitorOperationErrorCode.SettingsSaveFailed;
                    return false;
                }
                forceRefresh = true;
            }

            errorCode = MonitorOperationErrorCode.None;
            return true;
        }

        public bool TryToggleOffset(out string error) {
            bool enabled;
            int ignored;
            GetOffsetConfiguration(out enabled, out ignored);
            return TrySetOffsetEnabled(!enabled, out error);
        }

        public bool TrySetCurrentAsZero(out string error) {
            MonitorOperationErrorCode ignored;
            return TrySetCurrentAsZero(out error, out ignored);
        }

        public bool TrySetCurrentAsZero(
            out string error,
            out MonitorOperationErrorCode errorCode) {
            MonitorSnapshot snapshot = LatestSnapshot;
            if (snapshot.Game == null || !snapshot.Game.IsEldenRing || !snapshot.HasDeathCount) {
                error = "A valid Elden Ring death count is required before setting the zero baseline.";
                errorCode = MonitorOperationErrorCode.ValidEldenRingCountRequired;
                return false;
            }

            return TrySetOffset(snapshot.RawDeathCount, out error, out errorCode);
        }

        public bool TrySetOffset(int offset, out string error) {
            MonitorOperationErrorCode ignored;
            return TrySetOffset(offset, out error, out ignored);
        }

        public bool TrySetOffset(
            int offset,
            out string error,
            out MonitorOperationErrorCode errorCode) {
            if (offset < 0) {
                error = "The zero baseline must be a non-negative Int32 value.";
                errorCode = MonitorOperationErrorCode.InvalidZeroBaseline;
                return false;
            }
            if (!IsEldenRingConnected()) {
                error = "Elden Ring is not currently connected.";
                errorCode = MonitorOperationErrorCode.EldenRingNotConnected;
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
                    errorCode = MonitorOperationErrorCode.SettingsSaveFailed;
                    return false;
                }
                forceRefresh = true;
            }

            errorCode = MonitorOperationErrorCode.None;
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
                    "The monitor stopped unexpectedly: " + exception.Message,
                    MonitorMessageCode.UnexpectedFailure,
                    exception.Message));
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
                string errorDetail = DescribeLastWin32Error();
                Publish(CreateSnapshot(
                    MonitorState.Error,
                    game,
                    false,
                    false,
                    0,
                    0,
                    "Could not open the game process for read-only access. " + errorDetail,
                    MonitorMessageCode.OpenProcessFailed,
                    errorDetail));
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
                        "Could not inspect the game module: " + exception.Message,
                        MonitorMessageCode.InspectModuleFailed,
                        exception.Message));
                    return;
                }

                bool isWow64 = false;
                if (!IsWow64Process(handle, ref isWow64)) {
                    string errorDetail = DescribeLastWin32Error();
                    Publish(CreateSnapshot(
                        MonitorState.Error,
                        game,
                        false,
                        false,
                        0,
                        0,
                        "Could not determine the game process architecture. " + errorDetail,
                        MonitorMessageCode.ArchitectureDetectionFailed,
                        errorDetail));
                    return;
                }

                bool is64Bit = !isWow64;
                int[] offsets = is64Bit ? game.Offsets64 : game.Offsets32;
                string resolutionMessage = null;
                MonitorMessageCode resolutionMessageCode = MonitorMessageCode.None;
                string resolutionMessageArgument0 = null;
                string resolutionMessageArgument1 = null;

                if (game.IsEldenRing) {
                    if (!is64Bit) {
                        offsets = null;
                        resolutionMessage = "Elden Ring must be a 64-bit process.";
                        resolutionMessageCode = MonitorMessageCode.EldenRingRequires64Bit;
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
                            resolutionMessageArgument0 = signatureRva.ToString("X8", CultureInfo.InvariantCulture);
                            resolutionMessageArgument1 = resolvedRva.ToString("X8", CultureInfo.InvariantCulture);
                            resolutionMessage =
                                "Signature resolved uniquely (getter RVA 0x" +
                                resolutionMessageArgument0 +
                                ", pointer RVA 0x" +
                                resolutionMessageArgument1 + ").";
                            resolutionMessageCode = MonitorMessageCode.SignatureResolved;
                        } else {
                            offsets = null;
                            resolutionMessage = resolutionError;
                            resolutionMessageCode = MonitorMessageCode.SignatureResolutionFailed;
                        }
                    }
                }

                if (offsets == null) {
                    string unsupportedMessage =
                        resolutionMessage ?? "This process variant cannot be monitored safely.";
                    MonitorMessageCode unsupportedMessageCode = resolutionMessageCode == MonitorMessageCode.None
                        ? MonitorMessageCode.UnsupportedVariant
                        : resolutionMessageCode;
                    Publish(CreateSnapshot(
                        MonitorState.Unsupported,
                        game,
                        is64Bit,
                        false,
                        0,
                        0,
                        unsupportedMessage,
                        unsupportedMessageCode,
                        resolutionMessageArgument0,
                        resolutionMessageArgument1));
                    WaitForProcessExit(process);
                    return;
                }

                MonitorDeathCount(
                    process,
                    handle,
                    module.BaseAddress,
                    game,
                    is64Bit,
                    offsets,
                    resolutionMessage,
                    resolutionMessageCode,
                    resolutionMessageArgument0,
                    resolutionMessageArgument1);
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
            string initialMessage,
            MonitorMessageCode initialMessageCode,
            string initialMessageArgument0,
            string initialMessageArgument1) {
            int rawValue = 0;
            bool firstRead = true;

            while (!stopSignal.WaitOne(0) && !HasExited(process)) {
                string readError;
                MonitorMessageCode readMessageCode;
                string readMessageArgument0;
                if (!TryReadDeathCount(
                        handle,
                        moduleBase,
                        is64Bit,
                        offsets,
                        out rawValue,
                        out readError,
                        out readMessageCode,
                        out readMessageArgument0)) {
                    Publish(CreateSnapshot(
                        MonitorState.Error,
                        game,
                        is64Bit,
                        false,
                        0,
                        0,
                        readError,
                        readMessageCode,
                        readMessageArgument0));
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
                        firstRead ? initialMessage : null,
                        firstRead ? initialMessageCode : MonitorMessageCode.None,
                        firstRead ? initialMessageArgument0 : null,
                        firstRead ? initialMessageArgument1 : null));
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
            out string error,
            out MonitorMessageCode messageCode,
            out string messageArgument0) {
            long address = moduleBase.ToInt64();
            byte[] buffer = new byte[8];

            foreach (int offset in offsets) {
                if (address == 0) {
                    value = 0;
                    error = "The death-count pointer chain contained a null pointer.";
                    messageCode = MonitorMessageCode.NullPointer;
                    messageArgument0 = null;
                    return false;
                }

                address += offset;
                int requestedSize = is64Bit ? 8 : 4;
                UIntPtr bytesRead;
                if (!ReadProcessMemory(
                        handle,
                        new IntPtr(address),
                        buffer,
                        new UIntPtr((uint)requestedSize),
                        out bytesRead) ||
                    bytesRead.ToUInt64() != (ulong)requestedSize) {
                    string errorDetail = DescribeLastWin32Error();
                    value = 0;
                    error = "Could not read game memory. " + errorDetail;
                    messageCode = MonitorMessageCode.ReadMemoryFailed;
                    messageArgument0 = errorDetail;
                    return false;
                }

                address = is64Bit ? BitConverter.ToInt64(buffer, 0) : BitConverter.ToInt32(buffer, 0);
            }

            value = unchecked((int)address);
            error = null;
            messageCode = MonitorMessageCode.None;
            messageArgument0 = null;
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
            string message,
            MonitorMessageCode messageCode = MonitorMessageCode.None,
            string messageArgument0 = null,
            string messageArgument1 = null) {
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
                    message,
                    messageCode,
                    messageArgument0,
                    messageArgument1);
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
