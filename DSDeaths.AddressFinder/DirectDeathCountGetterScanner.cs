using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace DSDeaths.AddressFinder;

internal readonly record struct DirectDeathCountGetter(
    ulong InstructionAddress,
    ulong ResolvedPointerStorage,
    string ExactBytes,
    string Pattern);

internal static class DirectDeathCountGetterScanner
{
    private const int DisplacementOffset = 3;
    private const int RipInstructionLength = 7;
    private const int PatternLength = 20;

    internal static List<DirectDeathCountGetter> Find(
        ReadOnlySpan<byte> buffer,
        ulong bufferAddress,
        int fieldOffset)
    {
        var matches = new List<DirectDeathCountGetter>();

        for (int index = 0; index <= buffer.Length - PatternLength; index++)
        {
            if (!MatchesFixedBytes(buffer.Slice(index, PatternLength), fieldOffset))
            {
                continue;
            }

            int displacement = BinaryPrimitives.ReadInt32LittleEndian(
                buffer.Slice(index + DisplacementOffset, sizeof(int)));
            ulong instructionAddress = checked(bufferAddress + (ulong)index);
            long resolvedSigned = checked(
                (long)instructionAddress + RipInstructionLength + displacement);

            if (resolvedSigned < 0)
            {
                continue;
            }

            ReadOnlySpan<byte> exactBytes = buffer.Slice(index, PatternLength);
            matches.Add(new DirectDeathCountGetter(
                instructionAddress,
                (ulong)resolvedSigned,
                FormatBytes(exactBytes),
                CreatePattern(exactBytes)));
        }

        return matches;
    }

    private static bool MatchesFixedBytes(ReadOnlySpan<byte> candidate, int fieldOffset)
    {
        return candidate[0] == 0x48 &&
               candidate[1] == 0x8B &&
               candidate[2] == 0x05 &&
               candidate[7] == 0x48 &&
               candidate[8] == 0x85 &&
               candidate[9] == 0xC0 &&
               candidate[10] == 0x74 &&
               candidate[11] == 0x07 &&
               candidate[12] == 0x8B &&
               candidate[13] == 0x80 &&
               BinaryPrimitives.ReadInt32LittleEndian(candidate.Slice(14, sizeof(int))) == fieldOffset &&
               candidate[18] == 0xC3 &&
               candidate[19] == 0xC3;
    }

    private static string CreatePattern(ReadOnlySpan<byte> bytes)
    {
        var parts = new string[bytes.Length];
        for (int index = 0; index < bytes.Length; index++)
        {
            parts[index] = index >= DisplacementOffset && index < DisplacementOffset + sizeof(int)
                ? "??"
                : bytes[index].ToString("X2");
        }

        return string.Join(" ", parts);
    }

    private static string FormatBytes(ReadOnlySpan<byte> bytes)
    {
        var parts = new string[bytes.Length];
        for (int index = 0; index < bytes.Length; index++)
        {
            parts[index] = bytes[index].ToString("X2");
        }

        return string.Join(" ", parts);
    }
}
