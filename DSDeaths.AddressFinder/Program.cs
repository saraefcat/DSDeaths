using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DSDeaths.AddressFinder;

internal static class Program
{
    private const int LegacyFieldOffset = 0x94;
    private const int MaximumDisplayedCandidates = 100;

    private static volatile bool _cancellationRequested;

    private static int Main(string[] args)
    {
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _cancellationRequested = true;
            Console.WriteLine();
            Console.WriteLine("Cancellation requested. Finishing the current read...");
        };

        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Argument error: {exception.Message}");
            Console.Error.WriteLine("Use --help to see supported options.");
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        PrintHeader();

        if (!ConfirmOfflineMode(options.OfflineConfirmed))
        {
            return 2;
        }

        Process? selectedProcess = SelectProcess(options.ProcessId);
        if (selectedProcess is null)
        {
            return 2;
        }

        using Process process = selectedProcess;

        ProcessModule? mainModule;
        try
        {
            mainModule = process.MainModule;
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine($"Could not inspect eldenring.exe: {exception.Message}");
            return 2;
        }

        if (mainModule is null)
        {
            Console.Error.WriteLine("Could not locate the eldenring.exe main module.");
            return 2;
        }

        ulong moduleBase = ToUInt64(mainModule.BaseAddress);
        ulong moduleSize = checked((ulong)mainModule.ModuleMemorySize);

        using ProcessSafeHandle handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmRead,
            inheritHandle: false,
            process.Id);

        if (handle.IsInvalid)
        {
            int errorCode = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"OpenProcess failed: {DescribeWin32Error(errorCode)}");
            return 2;
        }

        if (!NativeMethods.IsWow64Process(handle, out bool isWow64))
        {
            int errorCode = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"Could not determine process architecture: {DescribeWin32Error(errorCode)}");
            return 2;
        }

        if (isWow64)
        {
            Console.Error.WriteLine("The selected process is 32-bit. Elden Ring and this tool must both be 64-bit.");
            return 2;
        }

        Console.WriteLine("Process");
        Console.WriteLine("=======");
        Console.WriteLine($"Name        : {process.ProcessName}.exe");
        Console.WriteLine($"PID         : {process.Id}");
        Console.WriteLine($"Module Base : {FormatAddress(moduleBase)}");
        Console.WriteLine($"Module Size : 0x{moduleSize:X} ({FormatBytes(moduleSize)})");
        Console.WriteLine();

        var reader = new ProcessMemoryReader(process, handle);

        try
        {
            if (options.AnalysisRva.HasValue)
            {
                return RunSignatureResearch(reader, moduleBase, moduleSize, options);
            }

            return options.ValidationRva.HasValue
                ? RunRestartValidation(reader, moduleBase, moduleSize, options)
                : RunDiscovery(reader, moduleBase, moduleSize, options);
        }
        catch (OverflowException exception)
        {
            Console.Error.WriteLine($"Address calculation overflow: {exception.Message}");
            return 2;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int RunDiscovery(
        ProcessMemoryReader reader,
        ulong moduleBase,
        ulong moduleSize,
        Options options)
    {
        int knownDeathCount = options.KnownDeathCount ?? PromptForNonNegativeInt32("Current known death count");
        if (_cancellationRequested)
        {
            return 130;
        }

        Console.WriteLine();
        Console.WriteLine($"Known death count: {knownDeathCount}");
        Console.WriteLine("Enumerating readable committed memory...");

        List<MemoryRegion> readableRegions = reader.EnumerateReadableCommittedRegions(
            IsCancellationRequested,
            out int failedQueries);

        if (_cancellationRequested)
        {
            Console.WriteLine("Cancelled before scanning.");
            return 130;
        }

        ulong readableBytes = ProcessMemoryReader.SumRegionLengths(readableRegions);
        Console.WriteLine($"Readable regions : {readableRegions.Count}");
        Console.WriteLine($"Readable bytes   : {FormatBytes(readableBytes)}");
        if (failedQueries > 0)
        {
            Console.WriteLine($"VirtualQueryEx pages skipped: {failedQueries}");
        }

        Console.WriteLine();
        Console.WriteLine("Initial Scan (Int32, little endian, exact value)");
        Console.WriteLine("================================================");

        var initialProgress = new ScanProgressPrinter("Scanning");
        ScanResult initialScan = reader.ScanExact(
            readableRegions,
            BitConverter.GetBytes(knownDeathCount),
            initialProgress.Report,
            IsCancellationRequested);
        initialProgress.Complete(initialScan);
        PrintReadErrors(initialScan);

        if (initialScan.Cancelled)
        {
            Console.WriteLine("Scan cancelled or eldenring.exe exited.");
            return 130;
        }

        Console.WriteLine($"Initial candidates: {initialScan.Addresses.Count}");

        if (initialScan.Addresses.Count == 0)
        {
            Console.Error.WriteLine("No candidates were found. Confirm that the correct character is loaded and the known count is exact.");
            return 3;
        }

        var candidates = initialScan.Addresses;
        int expectedDeathCount = knownDeathCount;

        if (candidates.Count <= 20)
        {
            PrintCandidates(reader, candidates);
        }

        while (candidates.Count > 1)
        {
            if (_cancellationRequested)
            {
                return 130;
            }

            Console.WriteLine();
            Console.WriteLine("Die once in Elden Ring, then enter the new cumulative death count.");
            Console.WriteLine("Commands: L = list candidates, USE <index> = manually choose a proven candidate, Q = quit");
            Console.Write("New death count: ");

            string? rawInput = Console.ReadLine();
            if (rawInput is null)
            {
                Console.WriteLine();
                Console.WriteLine("Console input closed. Exiting without selecting an address.");
                return 0;
            }

            string input = rawInput.Trim();
            if (input.Equals("Q", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (input.Equals("L", StringComparison.OrdinalIgnoreCase))
            {
                PrintCandidates(reader, candidates);
                continue;
            }

            if (TryParseManualCandidate(input, candidates.Count, out int candidateIndex))
            {
                ulong selectedAddress = candidates[candidateIndex];
                Console.WriteLine($"Manually selected {FormatAddress(selectedAddress)}.");
                Console.WriteLine("Manual selection is not proof; pointer-chain and restart validation are still required.");
                candidates = new List<ulong> { selectedAddress };
                break;
            }

            if (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int newDeathCount) ||
                newDeathCount < 0)
            {
                Console.WriteLine("Enter a non-negative decimal Int32 value, L, USE <index>, or Q.");
                continue;
            }

            if (newDeathCount <= expectedDeathCount)
            {
                Console.WriteLine($"The next cumulative count must be greater than {expectedDeathCount}.");
                continue;
            }

            int previousCount = candidates.Count;
            FilterResult filtered = reader.FilterInt32(candidates, newDeathCount);
            candidates = filtered.Addresses;
            expectedDeathCount = newDeathCount;

            Console.WriteLine($"Remaining: {previousCount} -> {candidates.Count}");
            if (filtered.UnreadableCandidates > 0)
            {
                Console.WriteLine($"Unreadable candidates removed: {filtered.UnreadableCandidates}");
            }

            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("All candidates were removed. Confirm the entered count and that the same character/process remained loaded.");
                return 3;
            }

            if (candidates.Count <= 20)
            {
                PrintCandidates(reader, candidates);
            }
        }

        ulong deathAddress = candidates[0];
        if (!reader.TryReadInt32(deathAddress, out int currentValue, out int deathReadError))
        {
            Console.Error.WriteLine($"The final death address became unreadable: {DescribeWin32Error(deathReadError)}");
            return 3;
        }

        if (currentValue != expectedDeathCount)
        {
            Console.Error.WriteLine($"The final candidate changed unexpectedly (expected {expectedDeathCount}, read {currentValue}).");
            return 3;
        }

        Console.WriteLine();
        Console.WriteLine("Death Address");
        Console.WriteLine("=============");
        Console.WriteLine($"Address : {FormatAddress(deathAddress)}");
        Console.WriteLine($"Value   : {currentValue}");

        int fieldOffset = options.FieldOffset;
        if (deathAddress < (ulong)fieldOffset)
        {
            Console.Error.WriteLine($"The death address is smaller than field offset 0x{fieldOffset:X}.");
            return 3;
        }

        ulong structureBase = checked(deathAddress - (ulong)fieldOffset);

        Console.WriteLine();
        Console.WriteLine($"Testing field offset 0x{fieldOffset:X}...");
        Console.WriteLine($"Structure Base : {FormatAddress(structureBase)}");

        List<MemoryRegion> moduleRegions = ProcessMemoryReader.ClipRegions(
            readableRegions,
            moduleBase,
            moduleSize);

        Console.WriteLine();
        Console.WriteLine("Searching eldenring.exe for an UInt64 pointer to Structure Base...");

        var pointerProgress = new ScanProgressPrinter("Module scan");
        ScanResult pointerScan = reader.ScanExact(
            moduleRegions,
            BitConverter.GetBytes(structureBase),
            pointerProgress.Report,
            IsCancellationRequested);
        pointerProgress.Complete(pointerScan);
        PrintReadErrors(pointerScan);

        if (pointerScan.Cancelled)
        {
            Console.WriteLine("Module scan cancelled or eldenring.exe exited.");
            return 130;
        }

        Console.WriteLine($"Pointer storage candidates: {pointerScan.Addresses.Count}");
        if (pointerScan.Addresses.Count == 0)
        {
            Console.Error.WriteLine($"No module pointer was found for DeathAddress - 0x{fieldOffset:X}.");
            Console.Error.WriteLine("Do not guess another offset. The surrounding structure must be investigated separately.");
            return 3;
        }

        var validatedRvas = new List<ulong>();

        Console.WriteLine();
        Console.WriteLine("DSDeaths-style Validation");
        Console.WriteLine("=========================");

        for (int index = 0; index < pointerScan.Addresses.Count; index++)
        {
            ulong pointerStorage = pointerScan.Addresses[index];
            ulong rva = checked(pointerStorage - moduleBase);

            Console.WriteLine();
            Console.WriteLine($"Candidate [{index}]");
            Console.WriteLine($"Pointer Storage : {FormatAddress(pointerStorage)}");
            Console.WriteLine($"RVA             : 0x{rva:X8}");

            if (!TryReadDsDeathsChain(
                    reader,
                    moduleBase,
                    rva,
                    fieldOffset,
                    out ulong pointer,
                    out ulong fieldAddress,
                    out int readValue,
                    out int errorCode))
            {
                Console.WriteLine($"Read error      : {DescribeWin32Error(errorCode)}");
                Console.WriteLine("RESULT          : FAILED");
                continue;
            }

            bool matches = pointer == structureBase &&
                           fieldAddress == deathAddress &&
                           readValue == expectedDeathCount;

            Console.WriteLine($"Pointer         : {FormatAddress(pointer)}");
            Console.WriteLine($"Field Address   : {FormatAddress(fieldAddress)}");
            Console.WriteLine($"Offset          : 0x{fieldOffset:X}");
            Console.WriteLine($"Expected        : {expectedDeathCount}");
            Console.WriteLine($"Read            : {readValue}");
            Console.WriteLine($"RESULT          : {(matches ? "MATCH" : "MISMATCH")}");

            if (matches)
            {
                validatedRvas.Add(rva);
            }
        }

        Console.WriteLine();
        if (validatedRvas.Count == 0)
        {
            Console.Error.WriteLine("No pointer candidate passed DSDeaths-style validation. Do not update Program.cs.");
            return 3;
        }

        Console.WriteLine(validatedRvas.Count == 1
            ? "VALIDATION PASSED"
            : $"VALIDATION PASSED FOR {validatedRvas.Count} RVA CANDIDATES");
        Console.WriteLine();
        Console.WriteLine("Completely exit Elden Ring, start it again, load the same character, then run:");

        foreach (ulong rva in validatedRvas)
        {
            Console.WriteLine(
                $"DSDeaths.AddressFinder.exe --offline --validate-rva 0x{rva:X} --offset 0x{fieldOffset:X} --expected {expectedDeathCount}");
        }

        Console.WriteLine();
        Console.WriteLine("Do not update DSDeaths until one RVA passes after a complete game restart.");
        return 0;
    }

    private static int RunSignatureResearch(
        ProcessMemoryReader reader,
        ulong moduleBase,
        ulong moduleSize,
        Options options)
    {
        ulong rva = options.AnalysisRva!.Value;
        int expected = options.ExpectedDeathCount ??
                       options.KnownDeathCount ??
                       PromptForNonNegativeInt32("Expected death count");
        int fieldOffset = options.FieldOffset;

        if (rva >= moduleSize)
        {
            Console.Error.WriteLine($"RVA 0x{rva:X} is outside the eldenring.exe module (size 0x{moduleSize:X}).");
            return 3;
        }

        if (!TryReadDsDeathsChain(
                reader,
                moduleBase,
                rva,
                fieldOffset,
                out ulong pointer,
                out ulong fieldAddress,
                out int readValue,
                out int errorCode))
        {
            Console.Error.WriteLine($"Could not validate the supplied RVA: {DescribeWin32Error(errorCode)}");
            return 3;
        }

        if (readValue != expected)
        {
            Console.Error.WriteLine($"The supplied RVA read {readValue}, not the expected count {expected}. Research aborted.");
            return 3;
        }

        ulong pointerStorage = checked(moduleBase + rva);
        Console.WriteLine("Signature Research");
        Console.WriteLine("==================");
        Console.WriteLine($"Known RVA       : 0x{rva:X8}");
        Console.WriteLine($"Pointer Storage : {FormatAddress(pointerStorage)}");
        Console.WriteLine($"Pointer         : {FormatAddress(pointer)}");
        Console.WriteLine($"Field Offset    : 0x{fieldOffset:X}");
        Console.WriteLine($"Death Address   : {FormatAddress(fieldAddress)}");
        Console.WriteLine($"Expected / Read : {expected} / {readValue}");
        Console.WriteLine();
        Console.WriteLine("Enumerating executable eldenring.exe memory regions...");

        List<MemoryRegion> readableRegions = reader.EnumerateReadableCommittedRegions(
            IsCancellationRequested,
            out int failedQueries);
        List<MemoryRegion> moduleRegions = ProcessMemoryReader.ClipRegions(
            readableRegions,
            moduleBase,
            moduleSize);

        var progress = new ScanProgressPrinter("Executable scan");
        SignatureResearchResult result = SignatureResearchScanner.Scan(
            reader,
            moduleRegions,
            pointerStorage,
            progress.Report,
            IsCancellationRequested);
        progress.Complete(result.ProcessedBytes, result.ExecutableBytes, result.Candidates.Count);

        if (result.Cancelled)
        {
            Console.WriteLine("Signature research cancelled or eldenring.exe exited.");
            return 130;
        }

        string reportPath = Path.GetFullPath(options.ReportPath ?? "DSDeaths.SignatureResearch.txt");
        var report = new List<string>
        {
            "DSDeaths Elden Ring Signature Research",
            "======================================",
            string.Empty,
            $"Module Base       : {FormatAddress(moduleBase)}",
            $"Module Size       : 0x{moduleSize:X}",
            $"Known RVA         : 0x{rva:X8}",
            $"Pointer Storage   : {FormatAddress(pointerStorage)}",
            $"Pointer           : {FormatAddress(pointer)}",
            $"Field Offset      : 0x{fieldOffset:X}",
            $"Death Address     : {FormatAddress(fieldAddress)}",
            $"Expected / Read   : {expected} / {readValue}",
            $"Executable Regions: {result.ExecutableRegionCount}",
            $"Executable Bytes  : {FormatBytes(result.ExecutableBytes)}",
            $"Query Failures    : {failedQueries}",
            $"Skipped Chunks    : {result.SkippedChunks}",
            $"References Found  : {result.Candidates.Count}",
            string.Empty
        };

        if (result.ReadErrors.Count > 0)
        {
            var errors = new List<KeyValuePair<int, int>>(result.ReadErrors);
            errors.Sort((left, right) => left.Key.CompareTo(right.Key));
            foreach (KeyValuePair<int, int> pair in errors)
            {
                report.Add($"Read Error {pair.Key}: {pair.Value} chunk(s) - {DescribeWin32Error(pair.Key)}");
            }
            report.Add(string.Empty);
        }

        for (int index = 0; index < result.Candidates.Count; index++)
        {
            SignatureResearchCandidate candidate = result.Candidates[index];
            ulong instructionRva = checked(candidate.Reference.InstructionAddress - moduleBase);
            ulong signatureRva = checked(candidate.Window.StartAddress - moduleBase);

            report.Add($"Candidate [{index}]");
            report.Add($"Instruction RVA  : 0x{instructionRva:X8}");
            report.Add($"Instruction Addr : {FormatAddress(candidate.Reference.InstructionAddress)}");
            report.Add($"Kind             : {candidate.Reference.Kind}");
            report.Add($"Instruction      : {candidate.InstructionBytes}");
            report.Add($"Signature RVA    : 0x{signatureRva:X8}");
            report.Add($"Exact Context    : {candidate.Window.ExactBytes}");
            report.Add($"Pattern Candidate: {candidate.Window.Pattern}");
            report.Add(string.Empty);
        }

        report.Add("RESEARCH ONLY: compare candidates across game versions before using any pattern in DSDeaths.");

        Console.WriteLine($"Executable regions : {result.ExecutableRegionCount}");
        Console.WriteLine($"Executable bytes   : {FormatBytes(result.ExecutableBytes)}");
        Console.WriteLine($"References found   : {result.Candidates.Count}");
        Console.WriteLine($"Skipped chunks     : {result.SkippedChunks}");

        try
        {
            File.WriteAllLines(reportPath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Could not write signature report: {exception.Message}");
            return 2;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Could not write signature report: {exception.Message}");
            return 2;
        }

        Console.WriteLine($"Report written     : {reportPath}");
        Console.WriteLine();
        Console.WriteLine("These are research candidates, not yet production signatures.");
        Console.WriteLine("Collect reports from both Elden Ring 1.16 and 1.17 before implementing automatic resolution.");

        return result.Candidates.Count > 0 ? 0 : 3;
    }

    private static int RunRestartValidation(
        ProcessMemoryReader reader,
        ulong moduleBase,
        ulong moduleSize,
        Options options)
    {
        ulong rva = options.ValidationRva!.Value;
        int expected = options.ExpectedDeathCount ??
                       options.KnownDeathCount ??
                       PromptForNonNegativeInt32("Expected death count");
        int fieldOffset = options.FieldOffset;

        if (rva >= moduleSize)
        {
            Console.Error.WriteLine($"RVA 0x{rva:X} is outside the eldenring.exe module (size 0x{moduleSize:X}).");
            return 3;
        }

        Console.WriteLine("Restart Validation");
        Console.WriteLine("==================");
        Console.WriteLine($"Module Base : {FormatAddress(moduleBase)}");
        Console.WriteLine($"RVA         : 0x{rva:X8}");
        Console.WriteLine($"Offset      : 0x{fieldOffset:X}");

        if (!TryReadDsDeathsChain(
                reader,
                moduleBase,
                rva,
                fieldOffset,
                out ulong pointer,
                out ulong fieldAddress,
                out int readValue,
                out int errorCode))
        {
            Console.WriteLine($"Read error  : {DescribeWin32Error(errorCode)}");
            Console.WriteLine("RESULT      : FAILED");
            return 3;
        }

        bool matches = readValue == expected;
        Console.WriteLine($"Pointer     : {FormatAddress(pointer)}");
        Console.WriteLine($"Death Addr  : {FormatAddress(fieldAddress)}");
        Console.WriteLine($"Expected    : {expected}");
        Console.WriteLine($"Read        : {readValue}");
        Console.WriteLine($"RESULT      : {(matches ? "MATCH" : "MISMATCH")}");

        return matches ? 0 : 3;
    }

    private static bool TryReadDsDeathsChain(
        ProcessMemoryReader reader,
        ulong moduleBase,
        ulong rva,
        int fieldOffset,
        out ulong pointer,
        out ulong fieldAddress,
        out int deathCount,
        out int errorCode)
    {
        ulong pointerStorage = checked(moduleBase + rva);

        if (!reader.TryReadUInt64(pointerStorage, out pointer, out errorCode) || pointer == 0)
        {
            fieldAddress = 0;
            deathCount = 0;
            return false;
        }

        fieldAddress = checked(pointer + (ulong)fieldOffset);

        // DSDeaths reads 8 bytes at every step, including the final field, and then
        // casts the resulting 64-bit value to Int32. Reproduce that behavior exactly.
        if (!reader.TryReadUInt64(fieldAddress, out ulong rawValue, out errorCode))
        {
            deathCount = 0;
            return false;
        }

        deathCount = unchecked((int)rawValue);
        return true;
    }

    private static Process? SelectProcess(int? requestedProcessId)
    {
        if (requestedProcessId.HasValue)
        {
            try
            {
                Process requested = Process.GetProcessById(requestedProcessId.Value);
                if (!requested.ProcessName.Equals("eldenring", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"PID {requestedProcessId.Value} is {requested.ProcessName}, not eldenring.exe.");
                    requested.Dispose();
                    return null;
                }

                return requested;
            }
            catch (ArgumentException)
            {
                Console.Error.WriteLine($"PID {requestedProcessId.Value} does not exist.");
                return null;
            }
        }

        Process[] processes = Process.GetProcessesByName("eldenring");
        if (processes.Length == 0)
        {
            Console.Error.WriteLine("eldenring.exe was not found. Start Elden Ring offline with EAC disabled and load the character first.");
            return null;
        }

        Array.Sort(processes, (left, right) => left.Id.CompareTo(right.Id));

        if (processes.Length == 1)
        {
            return processes[0];
        }

        Console.WriteLine("Multiple eldenring.exe processes were found:");
        for (int index = 0; index < processes.Length; index++)
        {
            Console.WriteLine($"[{index}] PID {processes[index].Id}");
        }

        while (true)
        {
            Console.Write("Select process index: ");
            string? rawInput = Console.ReadLine();
            if (rawInput is null)
            {
                Console.WriteLine();
                Console.WriteLine("Console input closed.");
                for (int index = 0; index < processes.Length; index++)
                {
                    processes[index].Dispose();
                }

                return null;
            }

            string input = rawInput.Trim();
            if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int selectedIndex) &&
                selectedIndex >= 0 &&
                selectedIndex < processes.Length)
            {
                for (int index = 0; index < processes.Length; index++)
                {
                    if (index != selectedIndex)
                    {
                        processes[index].Dispose();
                    }
                }

                return processes[selectedIndex];
            }

            Console.WriteLine("Enter one of the listed indexes.");
        }
    }

    private static bool ConfirmOfflineMode(bool alreadyConfirmed)
    {
        Console.WriteLine("SAFETY");
        Console.WriteLine("======");
        Console.WriteLine("This tool only requests PROCESS_QUERY_INFORMATION and PROCESS_VM_READ.");
        Console.WriteLine("It must only be used offline with Easy Anti-Cheat disabled.");

        if (alreadyConfirmed)
        {
            Console.WriteLine("Offline/EAC-disabled mode confirmed by --offline.");
            Console.WriteLine();
            return true;
        }

        Console.Write("Type OFFLINE to confirm, or anything else to exit: ");
        string confirmation = (Console.ReadLine() ?? string.Empty).Trim();
        Console.WriteLine();
        if (confirmation.Equals("OFFLINE", StringComparison.Ordinal))
        {
            return true;
        }

        Console.WriteLine("Exiting without opening the game process.");
        return false;
    }

    private static int PromptForNonNegativeInt32(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            string? rawInput = Console.ReadLine();
            if (rawInput is null)
            {
                throw new InvalidOperationException("Console input closed before a value was entered.");
            }

            string input = rawInput.Trim();
            if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= 0)
            {
                return value;
            }

            if (_cancellationRequested)
            {
                return 0;
            }

            Console.WriteLine("Enter a non-negative decimal Int32 value.");
        }
    }

    private static void PrintCandidates(ProcessMemoryReader reader, IReadOnlyList<ulong> candidates)
    {
        int displayCount = Math.Min(candidates.Count, MaximumDisplayedCandidates);
        Console.WriteLine();
        Console.WriteLine($"Candidates (showing {displayCount} of {candidates.Count}):");

        for (int index = 0; index < displayCount; index++)
        {
            ulong address = candidates[index];
            string valueText = reader.TryReadInt32(address, out int value, out int errorCode)
                ? value.ToString(CultureInfo.InvariantCulture)
                : $"unreadable ({DescribeWin32Error(errorCode)})";

            Console.WriteLine($"[{index}] {FormatAddress(address)} = {valueText}");
        }

        if (candidates.Count > displayCount)
        {
            Console.WriteLine($"{candidates.Count - displayCount} additional candidates are not displayed.");
        }
    }

    private static bool TryParseManualCandidate(string input, int candidateCount, out int candidateIndex)
    {
        candidateIndex = -1;
        const string prefix = "USE ";
        if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string indexText = input.Substring(prefix.Length).Trim();
        return int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out candidateIndex) &&
               candidateIndex >= 0 &&
               candidateIndex < candidateCount;
    }

    private static void PrintReadErrors(ScanResult result)
    {
        if (result.SkippedChunks == 0)
        {
            return;
        }

        Console.WriteLine($"Read chunks skipped or partial: {result.SkippedChunks}");

        var errors = new List<KeyValuePair<int, int>>(result.ReadErrors);
        errors.Sort((left, right) => left.Key.CompareTo(right.Key));
        foreach (KeyValuePair<int, int> error in errors)
        {
            Console.WriteLine($"  Win32 {error.Key}: {error.Value} chunk(s) - {DescribeWin32Error(error.Key)}");
        }
    }

    private static void PrintHeader()
    {
        Console.WriteLine("DSDeaths Elden Ring Address Finder");
        Console.WriteLine("==================================");
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        PrintHeader();
        Console.WriteLine("Discovery:");
        Console.WriteLine("  DSDeaths.AddressFinder.exe --offline");
        Console.WriteLine("  DSDeaths.AddressFinder.exe --offline --known 123");
        Console.WriteLine();
        Console.WriteLine("Restart validation:");
        Console.WriteLine("  DSDeaths.AddressFinder.exe --offline --validate-rva 0x12345678 --offset 0x94 --expected 124");
        Console.WriteLine();
        Console.WriteLine("Signature research:");
        Console.WriteLine("  DSDeaths.AddressFinder.exe --offline --analyze-rva 0x12345678 --offset 0x94 --expected 124");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --offline              Assert that EAC is disabled and the game is offline.");
        Console.WriteLine("  --known <decimal>      Initial known cumulative death count.");
        Console.WriteLine("  --validate-rva <value> Validate one RVA instead of scanning.");
        Console.WriteLine("  --analyze-rva <value>  Find executable RIP-relative references to a validated RVA.");
        Console.WriteLine($"  --offset <value>       Field offset; defaults to legacy 0x{LegacyFieldOffset:X}.");
        Console.WriteLine("  --expected <decimal>   Expected count in validation or research mode.");
        Console.WriteLine("  --report <path>        Signature research report path.");
        Console.WriteLine("  --pid <decimal>        Select a specific eldenring.exe PID.");
        Console.WriteLine("  --help                 Show this help.");
        Console.WriteLine();
        Console.WriteLine("Hex values use a 0x prefix. All process access is read-only.");
    }

    private static bool IsCancellationRequested()
    {
        return _cancellationRequested;
    }

    private static string FormatAddress(ulong address)
    {
        return $"0x{address:X16}";
    }

    private static string FormatBytes(ulong bytes)
    {
        const double mebibyte = 1024D * 1024D;
        return $"{bytes / mebibyte:N1} MiB";
    }

    private static string DescribeWin32Error(int errorCode)
    {
        return errorCode == 0
            ? "partial read with no Win32 error code"
            : $"{new Win32Exception(errorCode).Message} (code {errorCode})";
    }

    private static ulong ToUInt64(IntPtr value)
    {
        return unchecked((ulong)value.ToInt64());
    }

    private sealed class ScanProgressPrinter
    {
        private readonly string _label;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _lastReportMilliseconds = -1000;

        internal ScanProgressPrinter(string label)
        {
            _label = label;
        }

        internal void Report(ScanProgress progress)
        {
            long elapsed = _stopwatch.ElapsedMilliseconds;
            if (elapsed - _lastReportMilliseconds < 250 && progress.ProcessedBytes < progress.TotalBytes)
            {
                return;
            }

            _lastReportMilliseconds = elapsed;
            Console.Write(
                $"\r{_label}: {FormatBytes(progress.ProcessedBytes)} / {FormatBytes(progress.TotalBytes)} | candidates: {progress.CandidateCount}      ");
        }

        internal void Complete(ScanResult result)
        {
            Complete(result.ProcessedBytes, result.TotalBytes, result.Addresses.Count);
        }

        internal void Complete(ulong processedBytes, ulong totalBytes, int candidateCount)
        {
            Report(new ScanProgress(processedBytes, totalBytes, candidateCount));
            Console.WriteLine();
        }
    }

    private sealed class Options
    {
        internal bool ShowHelp { get; private set; }
        internal bool OfflineConfirmed { get; private set; }
        internal int? KnownDeathCount { get; private set; }
        internal ulong? ValidationRva { get; private set; }
        internal ulong? AnalysisRva { get; private set; }
        internal int FieldOffset { get; private set; } = LegacyFieldOffset;
        internal int? ExpectedDeathCount { get; private set; }
        internal int? ProcessId { get; private set; }
        internal string? ReportPath { get; private set; }

        internal static Options Parse(string[] args)
        {
            var options = new Options();

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                switch (argument.ToLowerInvariant())
                {
                    case "--help":
                    case "-h":
                        options.ShowHelp = true;
                        break;

                    case "--offline":
                        options.OfflineConfirmed = true;
                        break;

                    case "--known":
                        options.KnownDeathCount = ParseNonNegativeInt32(ReadValue(args, ref index, argument), argument);
                        break;

                    case "--validate-rva":
                        options.ValidationRva = ParseUnsigned(ReadValue(args, ref index, argument), argument);
                        break;

                    case "--analyze-rva":
                        options.AnalysisRva = ParseUnsigned(ReadValue(args, ref index, argument), argument);
                        break;

                    case "--offset":
                        ulong offset = ParseUnsigned(ReadValue(args, ref index, argument), argument);
                        if (offset > int.MaxValue)
                        {
                            throw new ArgumentException("--offset must fit in a positive Int32.");
                        }

                        options.FieldOffset = (int)offset;
                        break;

                    case "--expected":
                        options.ExpectedDeathCount = ParseNonNegativeInt32(ReadValue(args, ref index, argument), argument);
                        break;

                    case "--pid":
                        int processId = ParseNonNegativeInt32(ReadValue(args, ref index, argument), argument);
                        if (processId == 0)
                        {
                            throw new ArgumentException("--pid must be greater than zero.");
                        }

                        options.ProcessId = processId;
                        break;

                    case "--report":
                        options.ReportPath = ReadValue(args, ref index, argument);
                        if (string.IsNullOrWhiteSpace(options.ReportPath))
                        {
                            throw new ArgumentException("--report requires a non-empty path.");
                        }
                        break;

                    default:
                        throw new ArgumentException($"Unknown option: {argument}");
                }
            }

            if (options.ValidationRva.HasValue && options.AnalysisRva.HasValue)
            {
                throw new ArgumentException("--validate-rva and --analyze-rva cannot be used together.");
            }

            if (options.ReportPath is not null && !options.AnalysisRva.HasValue)
            {
                throw new ArgumentException("--report can only be used with --analyze-rva.");
            }

            return options;
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"{option} requires a value.");
            }

            index++;
            return args[index];
        }

        private static int ParseNonNegativeInt32(string text, string option)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0)
            {
                throw new ArgumentException($"{option} requires a non-negative decimal Int32 value.");
            }

            return value;
        }

        private static ulong ParseUnsigned(string text, string option)
        {
            string normalized = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? text.Substring(2)
                : text;

            NumberStyles style = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? NumberStyles.AllowHexSpecifier
                : NumberStyles.None;

            if (!ulong.TryParse(normalized, style, CultureInfo.InvariantCulture, out ulong value))
            {
                throw new ArgumentException($"{option} requires a decimal value or a hexadecimal value prefixed with 0x.");
            }

            return value;
        }
    }
}
