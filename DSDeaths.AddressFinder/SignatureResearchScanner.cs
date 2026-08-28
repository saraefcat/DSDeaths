using System;
using System.Collections.Generic;

namespace DSDeaths.AddressFinder;

internal readonly record struct SignatureResearchCandidate(
    RipRelativeReference Reference,
    string InstructionBytes,
    SignatureWindow Window);

internal sealed class SignatureResearchResult
{
    internal SignatureResearchResult(
        List<SignatureResearchCandidate> candidates,
        int executableRegionCount,
        ulong executableBytes,
        ulong processedBytes,
        int skippedChunks,
        Dictionary<int, int> readErrors,
        bool cancelled)
    {
        Candidates = candidates;
        ExecutableRegionCount = executableRegionCount;
        ExecutableBytes = executableBytes;
        ProcessedBytes = processedBytes;
        SkippedChunks = skippedChunks;
        ReadErrors = readErrors;
        Cancelled = cancelled;
    }

    internal List<SignatureResearchCandidate> Candidates { get; }
    internal int ExecutableRegionCount { get; }
    internal ulong ExecutableBytes { get; }
    internal ulong ProcessedBytes { get; }
    internal int SkippedChunks { get; }
    internal Dictionary<int, int> ReadErrors { get; }
    internal bool Cancelled { get; }
}

internal static class SignatureResearchScanner
{
    private const int ChunkSize = 1024 * 1024;
    private const int ContextOverlap = 64;

    internal static SignatureResearchResult Scan(
        ProcessMemoryReader reader,
        IReadOnlyList<MemoryRegion> moduleRegions,
        ulong pointerStorage,
        Action<ScanProgress>? reportProgress,
        Func<bool> cancellationRequested)
    {
        var executableRegions = new List<MemoryRegion>();
        ulong executableBytes = 0;

        foreach (MemoryRegion region in moduleRegions)
        {
            if (!ProcessMemoryReader.IsExecutable(region))
            {
                continue;
            }

            executableRegions.Add(region);
            executableBytes = checked(executableBytes + region.Length);
        }

        var candidatesByAddress = new Dictionary<ulong, SignatureResearchCandidate>();
        var readErrors = new Dictionary<int, int>();
        ulong processedBytes = 0;
        int skippedChunks = 0;

        foreach (MemoryRegion region in executableRegions)
        {
            ulong uniqueStart = region.Start;
            while (uniqueStart < region.EndExclusive)
            {
                if (cancellationRequested())
                {
                    return CreateResult(
                        candidatesByAddress,
                        executableRegions.Count,
                        executableBytes,
                        processedBytes,
                        skippedChunks,
                        readErrors,
                        cancelled: true);
                }

                ulong uniqueEnd = Math.Min(
                    region.EndExclusive,
                    checked(uniqueStart + Math.Min((ulong)ChunkSize, region.EndExclusive - uniqueStart)));
                ulong readStart = uniqueStart > region.Start + ContextOverlap
                    ? uniqueStart - ContextOverlap
                    : region.Start;
                ulong readEnd = region.EndExclusive - uniqueEnd > ContextOverlap
                    ? uniqueEnd + ContextOverlap
                    : region.EndExclusive;
                int readLength = checked((int)(readEnd - readStart));

                if (reader.TryReadBytes(readStart, readLength, out byte[] buffer, out int errorCode))
                {
                    List<RipRelativeReference> references = RipRelativeReferenceScanner.Find(
                        buffer,
                        readStart,
                        pointerStorage);

                    foreach (RipRelativeReference reference in references)
                    {
                        if (reference.InstructionAddress < uniqueStart ||
                            reference.InstructionAddress >= uniqueEnd ||
                            candidatesByAddress.ContainsKey(reference.InstructionAddress))
                        {
                            continue;
                        }

                        candidatesByAddress.Add(
                            reference.InstructionAddress,
                            new SignatureResearchCandidate(
                                reference,
                                RipRelativeReferenceScanner.FormatInstructionBytes(buffer, readStart, reference),
                                RipRelativeReferenceScanner.CreateSignatureWindow(buffer, readStart, reference)));
                    }
                }
                else
                {
                    skippedChunks++;
                    readErrors.TryGetValue(errorCode, out int count);
                    readErrors[errorCode] = checked(count + 1);
                }

                processedBytes = checked(processedBytes + uniqueEnd - uniqueStart);
                reportProgress?.Invoke(new ScanProgress(processedBytes, executableBytes, candidatesByAddress.Count));
                uniqueStart = uniqueEnd;
            }
        }

        return CreateResult(
            candidatesByAddress,
            executableRegions.Count,
            executableBytes,
            processedBytes,
            skippedChunks,
            readErrors,
            cancelled: false);
    }

    private static SignatureResearchResult CreateResult(
        Dictionary<ulong, SignatureResearchCandidate> candidatesByAddress,
        int executableRegionCount,
        ulong executableBytes,
        ulong processedBytes,
        int skippedChunks,
        Dictionary<int, int> readErrors,
        bool cancelled)
    {
        var addresses = new List<ulong>(candidatesByAddress.Keys);
        addresses.Sort();
        var candidates = new List<SignatureResearchCandidate>(addresses.Count);

        foreach (ulong address in addresses)
        {
            candidates.Add(candidatesByAddress[address]);
        }

        return new SignatureResearchResult(
            candidates,
            executableRegionCount,
            executableBytes,
            processedBytes,
            skippedChunks,
            readErrors,
            cancelled);
    }
}
