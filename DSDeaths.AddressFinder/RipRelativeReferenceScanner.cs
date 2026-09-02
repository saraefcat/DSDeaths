using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace DSDeaths.AddressFinder;

internal readonly record struct RipRelativeReference(
    ulong InstructionAddress,
    int InstructionLength,
    int DisplacementOffset,
    ulong ResolvedAddress,
    string Kind);

internal readonly record struct SignatureWindow(
    ulong StartAddress,
    string ExactBytes,
    string Pattern);

internal static class RipRelativeReferenceScanner
{
    private const int ContextBefore = 16;
    private const int ContextAfter = 24;

    internal static List<RipRelativeReference> Find(
        ReadOnlySpan<byte> buffer,
        ulong bufferAddress,
        ulong targetAddress)
    {
        var references = new List<RipRelativeReference>();

        for (int instructionIndex = 0; instructionIndex < buffer.Length; instructionIndex++)
        {
            if (!TryDecode(
                    buffer,
                    bufferAddress,
                    instructionIndex,
                    out RipRelativeReference reference))
            {
                continue;
            }

            if (reference.ResolvedAddress != targetAddress)
            {
                continue;
            }

            ulong instructionEnd = checked(
                reference.InstructionAddress + (ulong)reference.InstructionLength);
            int duplicateIndex = references.FindIndex(existing =>
                existing.ResolvedAddress == reference.ResolvedAddress &&
                checked(existing.InstructionAddress + (ulong)existing.InstructionLength) == instructionEnd);

            if (duplicateIndex < 0)
            {
                references.Add(reference);
            }
            else if (reference.InstructionAddress < references[duplicateIndex].InstructionAddress)
            {
                references[duplicateIndex] = reference;
            }
        }

        return references;
    }

    internal static string FormatInstructionBytes(
        ReadOnlySpan<byte> buffer,
        ulong bufferAddress,
        RipRelativeReference reference)
    {
        int instructionIndex = checked((int)(reference.InstructionAddress - bufferAddress));
        return FormatBytes(buffer.Slice(instructionIndex, reference.InstructionLength));
    }

    internal static SignatureWindow CreateSignatureWindow(
        ReadOnlySpan<byte> buffer,
        ulong bufferAddress,
        RipRelativeReference reference)
    {
        int instructionIndex = checked((int)(reference.InstructionAddress - bufferAddress));
        int windowStart = Math.Max(0, instructionIndex - ContextBefore);
        int windowEnd = Math.Min(
            buffer.Length,
            checked(instructionIndex + reference.InstructionLength + ContextAfter));

        var exactParts = new List<string>(windowEnd - windowStart);
        var patternParts = new List<string>(windowEnd - windowStart);
        int displacementStart = checked(instructionIndex + reference.DisplacementOffset);
        int displacementEnd = checked(displacementStart + sizeof(int));

        for (int index = windowStart; index < windowEnd; index++)
        {
            string formatted = buffer[index].ToString("X2");
            exactParts.Add(formatted);
            patternParts.Add(index >= displacementStart && index < displacementEnd ? "??" : formatted);
        }

        return new SignatureWindow(
            checked(bufferAddress + (ulong)windowStart),
            string.Join(" ", exactParts),
            string.Join(" ", patternParts));
    }

    private static bool TryDecode(
        ReadOnlySpan<byte> buffer,
        ulong bufferAddress,
        int instructionIndex,
        out RipRelativeReference reference)
    {
        reference = default;

        if (HasAddressOverrideImmediatelyBefore(buffer, instructionIndex))
        {
            return false;
        }

        int cursor = instructionIndex;

        // Accept common legacy prefixes, but reject address-size override because
        // mod=00/rm=101 no longer represents RIP-relative addressing with 0x67.
        int prefixCount = 0;
        while (cursor < buffer.Length && IsLegacyPrefix(buffer[cursor]))
        {
            if (buffer[cursor] == 0x67 || prefixCount == 4)
            {
                return false;
            }

            cursor++;
            prefixCount++;
        }

        if (cursor < buffer.Length && buffer[cursor] >= 0x40 && buffer[cursor] <= 0x4F)
        {
            cursor++;
        }

        if (cursor >= buffer.Length)
        {
            return false;
        }

        byte opcode = buffer[cursor++];
        bool twoByteOpcode = false;
        byte secondOpcode = 0;

        if (opcode == 0x0F)
        {
            if (cursor >= buffer.Length)
            {
                return false;
            }

            twoByteOpcode = true;
            secondOpcode = buffer[cursor++];
            if (!IsSupportedTwoByteOpcode(secondOpcode))
            {
                return false;
            }
        }
        else if (!IsSupportedOneByteOpcode(opcode))
        {
            return false;
        }

        if (cursor >= buffer.Length)
        {
            return false;
        }

        byte modRm = buffer[cursor++];
        if ((modRm & 0xC7) != 0x05)
        {
            return false;
        }

        int displacementIndex = cursor;
        if (displacementIndex > buffer.Length - sizeof(int))
        {
            return false;
        }

        int immediateLength = twoByteOpcode ? 0 : GetImmediateLength(opcode, modRm);
        int instructionLength = checked(
            displacementIndex + sizeof(int) + immediateLength - instructionIndex);
        if (instructionIndex > buffer.Length - instructionLength)
        {
            return false;
        }

        int displacement = BinaryPrimitives.ReadInt32LittleEndian(
            buffer.Slice(displacementIndex, sizeof(int)));

        ulong nextInstruction = checked(bufferAddress + (ulong)instructionIndex + (ulong)instructionLength);
        long resolvedSigned = checked((long)nextInstruction + displacement);
        if (resolvedSigned < 0)
        {
            return false;
        }

        reference = new RipRelativeReference(
            checked(bufferAddress + (ulong)instructionIndex),
            instructionLength,
            checked(displacementIndex - instructionIndex),
            (ulong)resolvedSigned,
            DescribeOpcode(opcode, twoByteOpcode, secondOpcode));
        return true;
    }

    private static bool IsLegacyPrefix(byte value)
    {
        return value == 0x66 || value == 0x67 || value == 0xF0 ||
               value == 0xF2 || value == 0xF3 ||
               value == 0x2E || value == 0x36 || value == 0x3E ||
               value == 0x26 || value == 0x64 || value == 0x65;
    }

    private static bool HasAddressOverrideImmediatelyBefore(ReadOnlySpan<byte> buffer, int instructionIndex)
    {
        int cursor = instructionIndex - 1;
        int inspected = 0;

        while (cursor >= 0 && inspected < 5)
        {
            byte value = buffer[cursor];
            if (value == 0x67)
            {
                return true;
            }

            if (!IsLegacyPrefix(value) && (value < 0x40 || value > 0x4F))
            {
                break;
            }

            cursor--;
            inspected++;
        }

        return false;
    }

    private static bool IsSupportedOneByteOpcode(byte opcode)
    {
        return opcode == 0x03 || opcode == 0x0B || opcode == 0x13 || opcode == 0x1B ||
               opcode == 0x23 || opcode == 0x2B || opcode == 0x33 || opcode == 0x39 ||
               opcode == 0x3B || opcode == 0x63 || opcode == 0x80 || opcode == 0x81 ||
               opcode == 0x83 || opcode == 0x88 || opcode == 0x89 || opcode == 0x8A ||
               opcode == 0x8B || opcode == 0x8D || opcode == 0xC6 || opcode == 0xC7 ||
               opcode == 0xF6 || opcode == 0xF7 || opcode == 0xFF;
    }

    private static bool IsSupportedTwoByteOpcode(byte opcode)
    {
        return opcode == 0x10 || opcode == 0x11 || opcode == 0x28 || opcode == 0x29 ||
               opcode == 0x2E || opcode == 0x2F || opcode == 0xB6 || opcode == 0xB7 ||
               opcode == 0xBE || opcode == 0xBF;
    }

    private static int GetImmediateLength(byte opcode, byte modRm)
    {
        if (opcode == 0x80 || opcode == 0x83 || opcode == 0xC6)
        {
            return 1;
        }

        if (opcode == 0x81 || opcode == 0xC7)
        {
            return sizeof(int);
        }

        int groupOpcode = (modRm >> 3) & 0x07;
        if (opcode == 0xF6 && groupOpcode == 0)
        {
            return 1;
        }

        if (opcode == 0xF7 && groupOpcode == 0)
        {
            return sizeof(int);
        }

        return 0;
    }

    private static string DescribeOpcode(byte opcode, bool twoByteOpcode, byte secondOpcode)
    {
        if (twoByteOpcode)
        {
            return $"0F {secondOpcode:X2} RIP-relative";
        }

        return opcode switch
        {
            0x8B => "MOV load",
            0x89 => "MOV store",
            0x8D => "LEA",
            0x39 or 0x3B => "CMP",
            0xFF => "FF group",
            _ => $"opcode {opcode:X2} RIP-relative"
        };
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
