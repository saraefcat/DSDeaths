using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DSDeaths.AddressFinder;

internal readonly record struct MemoryRegion(ulong Start, ulong EndExclusive)
{
    internal ulong Length => EndExclusive - Start;
}

internal readonly record struct ScanProgress(ulong ProcessedBytes, ulong TotalBytes, int CandidateCount);

internal sealed class ScanResult
{
    internal ScanResult(
        List<ulong> addresses,
        ulong processedBytes,
        ulong totalBytes,
        int skippedChunks,
        Dictionary<int, int> readErrors,
        bool cancelled)
    {
        Addresses = addresses;
        ProcessedBytes = processedBytes;
        TotalBytes = totalBytes;
        SkippedChunks = skippedChunks;
        ReadErrors = readErrors;
        Cancelled = cancelled;
    }

    internal List<ulong> Addresses { get; }
    internal ulong ProcessedBytes { get; }
    internal ulong TotalBytes { get; }
    internal int SkippedChunks { get; }
    internal Dictionary<int, int> ReadErrors { get; }
    internal bool Cancelled { get; }
}

internal readonly record struct FilterResult(List<ulong> Addresses, int UnreadableCandidates);

internal sealed class ProcessMemoryReader
{
    private const int ChunkSize = 1024 * 1024;

    private readonly Process _process;
    private readonly ProcessSafeHandle _handle;

    internal ProcessMemoryReader(Process process, ProcessSafeHandle handle)
    {
        _process = process;
        _handle = handle;
    }

    internal List<MemoryRegion> EnumerateReadableCommittedRegions(
        Func<bool> cancellationRequested,
        out int failedQueries)
    {
        if (IntPtr.Size != 8)
        {
            throw new InvalidOperationException("Address Finder must run as a 64-bit process.");
        }

        NativeMethods.GetNativeSystemInfo(out NativeMethods.SystemInfo systemInfo);

        ulong current = ToUInt64(systemInfo.MinimumApplicationAddress);
        ulong maximum = ToUInt64(systemInfo.MaximumApplicationAddress);
        ulong maximumExclusive = checked(maximum + 1);
        ulong pageSize = Math.Max(systemInfo.PageSize, 4096U);
        nuint informationSize = (nuint)Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();

        var regions = new List<MemoryRegion>();
        failedQueries = 0;

        while (current < maximumExclusive && !ShouldStop(cancellationRequested))
        {
            nuint queried = NativeMethods.VirtualQueryEx(
                _handle,
                ToIntPtr(current),
                out NativeMethods.MemoryBasicInformation information,
                informationSize);

            if (queried == 0)
            {
                failedQueries++;
                current = AddClamped(current, pageSize, maximumExclusive);
                continue;
            }

            ulong regionStart = ToUInt64(information.BaseAddress);
            ulong regionSize = information.RegionSize;
            ulong regionEnd;

            try
            {
                regionEnd = checked(regionStart + regionSize);
            }
            catch (OverflowException)
            {
                regionEnd = maximumExclusive;
            }

            regionEnd = Math.Min(regionEnd, maximumExclusive);

            if (information.State == NativeMethods.MemCommit &&
                IsReadable(information.Protect) &&
                regionEnd > regionStart)
            {
                regions.Add(new MemoryRegion(regionStart, regionEnd));
            }

            current = regionEnd > current
                ? regionEnd
                : AddClamped(current, pageSize, maximumExclusive);
        }

        return regions;
    }

    internal ScanResult ScanExact(
        IReadOnlyList<MemoryRegion> regions,
        byte[] pattern,
        Action<ScanProgress>? reportProgress,
        Func<bool> cancellationRequested)
    {
        if (pattern.Length == 0)
        {
            throw new ArgumentException("The search pattern cannot be empty.", nameof(pattern));
        }

        ulong totalBytes = SumRegionLengths(regions);
        ulong processedBytes = 0;
        int skippedChunks = 0;
        var readErrors = new Dictionary<int, int>();
        var candidates = new List<ulong>();
        var buffer = new byte[checked(ChunkSize + pattern.Length - 1)];

        foreach (MemoryRegion region in regions)
        {
            ulong cursor = region.Start;
            bool firstChunk = true;

            while (cursor < region.EndExclusive)
            {
                if (ShouldStop(cancellationRequested))
                {
                    return new ScanResult(
                        candidates,
                        processedBytes,
                        totalBytes,
                        skippedChunks,
                        readErrors,
                        cancelled: true);
                }

                ulong remainingBytes = region.EndExclusive - cursor;
                ulong step = Math.Min((ulong)ChunkSize, remainingBytes);
                ulong nextCursor = checked(cursor + step);

                ulong readStart = firstChunk
                    ? cursor
                    : checked(cursor - (ulong)(pattern.Length - 1));

                int requestedBytes = checked((int)(nextCursor - readStart));
                bool readSucceeded = TryRead(
                    readStart,
                    buffer,
                    requestedBytes,
                    out int bytesRead,
                    out int errorCode);

                if (bytesRead >= pattern.Length)
                {
                    FindMatches(buffer, bytesRead, pattern, readStart, candidates);
                }

                if (!readSucceeded || bytesRead != requestedBytes)
                {
                    skippedChunks++;
                    IncrementError(readErrors, errorCode);
                }

                processedBytes += nextCursor - cursor;
                reportProgress?.Invoke(new ScanProgress(processedBytes, totalBytes, candidates.Count));

                cursor = nextCursor;
                firstChunk = false;
            }
        }

        return new ScanResult(
            candidates,
            processedBytes,
            totalBytes,
            skippedChunks,
            readErrors,
            cancelled: false);
    }

    internal FilterResult FilterInt32(IReadOnlyList<ulong> candidates, int expectedValue)
    {
        var remaining = new List<ulong>(candidates.Count);
        int unreadable = 0;

        foreach (ulong address in candidates)
        {
            if (TryReadInt32(address, out int value, out _))
            {
                if (value == expectedValue)
                {
                    remaining.Add(address);
                }
            }
            else
            {
                unreadable++;
            }
        }

        return new FilterResult(remaining, unreadable);
    }

    internal bool TryReadInt32(ulong address, out int value, out int errorCode)
    {
        var buffer = new byte[sizeof(int)];
        bool success = TryRead(address, buffer, buffer.Length, out int bytesRead, out errorCode);
        value = bytesRead == buffer.Length ? BitConverter.ToInt32(buffer, 0) : 0;
        return success && bytesRead == buffer.Length;
    }

    internal bool TryReadUInt64(ulong address, out ulong value, out int errorCode)
    {
        var buffer = new byte[sizeof(ulong)];
        bool success = TryRead(address, buffer, buffer.Length, out int bytesRead, out errorCode);
        value = bytesRead == buffer.Length ? BitConverter.ToUInt64(buffer, 0) : 0;
        return success && bytesRead == buffer.Length;
    }

    internal static List<MemoryRegion> ClipRegions(
        IReadOnlyList<MemoryRegion> regions,
        ulong rangeStart,
        ulong rangeLength)
    {
        ulong rangeEnd = checked(rangeStart + rangeLength);
        var clipped = new List<MemoryRegion>();

        foreach (MemoryRegion region in regions)
        {
            ulong start = Math.Max(region.Start, rangeStart);
            ulong end = Math.Min(region.EndExclusive, rangeEnd);
            if (end > start)
            {
                clipped.Add(new MemoryRegion(start, end));
            }
        }

        return clipped;
    }

    internal static ulong SumRegionLengths(IReadOnlyList<MemoryRegion> regions)
    {
        ulong total = 0;
        foreach (MemoryRegion region in regions)
        {
            total = checked(total + region.Length);
        }

        return total;
    }

    private bool TryRead(
        ulong address,
        byte[] buffer,
        int requestedBytes,
        out int bytesRead,
        out int errorCode)
    {
        bool success = NativeMethods.ReadProcessMemory(
            _handle,
            ToIntPtr(address),
            buffer,
            (nuint)requestedBytes,
            out nuint nativeBytesRead);

        bytesRead = nativeBytesRead > int.MaxValue
            ? requestedBytes
            : (int)nativeBytesRead;

        errorCode = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    private bool ShouldStop(Func<bool> cancellationRequested)
    {
        if (cancellationRequested())
        {
            return true;
        }

        try
        {
            return _process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool IsReadable(uint protection)
    {
        if ((protection & NativeMethods.PageGuard) != 0 ||
            (protection & NativeMethods.PageNoAccess) != 0)
        {
            return false;
        }

        uint baseProtection = protection & 0xFF;
        return baseProtection == NativeMethods.PageReadOnly ||
               baseProtection == NativeMethods.PageReadWrite ||
               baseProtection == NativeMethods.PageWriteCopy ||
               baseProtection == NativeMethods.PageExecuteRead ||
               baseProtection == NativeMethods.PageExecuteReadWrite ||
               baseProtection == NativeMethods.PageExecuteWriteCopy;
    }

    private static void FindMatches(
        byte[] buffer,
        int bytesRead,
        byte[] pattern,
        ulong bufferAddress,
        List<ulong> matches)
    {
        ReadOnlySpan<byte> haystack = buffer.AsSpan(0, bytesRead);
        ReadOnlySpan<byte> needle = pattern;
        int searchStart = 0;

        while (searchStart <= haystack.Length - needle.Length)
        {
            int relativeIndex = haystack.Slice(searchStart).IndexOf(needle);
            if (relativeIndex < 0)
            {
                break;
            }

            int matchIndex = checked(searchStart + relativeIndex);
            matches.Add(checked(bufferAddress + (ulong)matchIndex));
            searchStart = checked(matchIndex + 1);
        }
    }

    private static void IncrementError(Dictionary<int, int> errors, int errorCode)
    {
        errors.TryGetValue(errorCode, out int count);
        errors[errorCode] = checked(count + 1);
    }

    private static ulong AddClamped(ulong value, ulong increment, ulong maximumExclusive)
    {
        if (maximumExclusive - value <= increment)
        {
            return maximumExclusive;
        }

        return value + increment;
    }

    private static ulong ToUInt64(IntPtr value)
    {
        return unchecked((ulong)value.ToInt64());
    }

    private static IntPtr ToIntPtr(ulong value)
    {
        if (value > long.MaxValue)
        {
            throw new OverflowException($"Address 0x{value:X16} cannot be represented by IntPtr.");
        }

        return new IntPtr((long)value);
    }
}
