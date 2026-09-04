using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace DSDeaths.AddressFinder;

internal sealed class ExecutableSignatureMatch
{
    internal ExecutableSignatureMatch(ulong instructionRva, ulong pointerStorageRva)
    {
        InstructionRva = instructionRva;
        PointerStorageRva = pointerStorageRva;
    }

    internal ulong InstructionRva { get; }
    internal ulong PointerStorageRva { get; }
}

internal sealed class ExecutableSignatureScanResult
{
    internal ExecutableSignatureScanResult(
        ushort machine,
        uint imageSize,
        IReadOnlyList<ExecutableSignatureMatch> matches)
    {
        Machine = machine;
        ImageSize = imageSize;
        Matches = matches;
    }

    internal ushort Machine { get; }
    internal uint ImageSize { get; }
    internal IReadOnlyList<ExecutableSignatureMatch> Matches { get; }
    internal bool IsCompatible => Machine == 0x8664 && Matches.Count == 1;
}

internal static class EldenRingExecutableSignatureScanner
{
    private const ushort Amd64Machine = 0x8664;
    private const ushort Pe32PlusMagic = 0x020B;
    private const uint SectionExecutable = 0x20000000;
    private const int SectionHeaderSize = 40;
    private const int FieldOffset = 0x94;
    private const uint SignatureLength = 20;

    internal static ExecutableSignatureScanResult Scan(byte[] fileBytes)
    {
        if (fileBytes is null)
        {
            throw new ArgumentNullException(nameof(fileBytes));
        }

        if (fileBytes.Length < 0x40 || fileBytes[0] != (byte)'M' || fileBytes[1] != (byte)'Z')
        {
            throw new InvalidDataException("The file does not have a valid DOS header.");
        }

        int peOffset = ReadInt32(fileBytes, 0x3C);
        EnsureRange(fileBytes, peOffset, 24, "PE header");
        if (fileBytes[peOffset] != (byte)'P' || fileBytes[peOffset + 1] != (byte)'E' ||
            fileBytes[peOffset + 2] != 0 || fileBytes[peOffset + 3] != 0)
        {
            throw new InvalidDataException("The file does not have a valid PE signature.");
        }

        int coffOffset = peOffset + 4;
        ushort machine = ReadUInt16(fileBytes, coffOffset);
        ushort sectionCount = ReadUInt16(fileBytes, coffOffset + 2);
        ushort optionalHeaderSize = ReadUInt16(fileBytes, coffOffset + 16);
        int optionalHeaderOffset = coffOffset + 20;
        EnsureRange(fileBytes, optionalHeaderOffset, optionalHeaderSize, "optional header");
        if (optionalHeaderSize < 60 || ReadUInt16(fileBytes, optionalHeaderOffset) != Pe32PlusMagic)
        {
            throw new InvalidDataException("The executable is not a PE32+ image.");
        }

        uint imageSize = ReadUInt32(fileBytes, optionalHeaderOffset + 56);
        int sectionTableOffset = checked(optionalHeaderOffset + optionalHeaderSize);
        EnsureRange(
            fileBytes,
            sectionTableOffset,
            checked(sectionCount * SectionHeaderSize),
            "section table");

        var matches = new List<ExecutableSignatureMatch>();
        for (int sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
        {
            int sectionOffset = checked(sectionTableOffset + sectionIndex * SectionHeaderSize);
            uint rawSize = ReadUInt32(fileBytes, sectionOffset + 16);
            uint rawOffset = ReadUInt32(fileBytes, sectionOffset + 20);
            uint virtualAddress = ReadUInt32(fileBytes, sectionOffset + 12);
            uint characteristics = ReadUInt32(fileBytes, sectionOffset + 36);
            if ((characteristics & SectionExecutable) == 0 || rawSize == 0)
            {
                continue;
            }

            if (rawOffset > int.MaxValue || rawSize > int.MaxValue)
            {
                throw new InvalidDataException("An executable section is too large to inspect safely.");
            }
            EnsureRange(fileBytes, (int)rawOffset, (int)rawSize, "executable section");

            foreach (DirectDeathCountGetter getter in DirectDeathCountGetterScanner.Find(
                         fileBytes.AsSpan((int)rawOffset, (int)rawSize),
                         virtualAddress,
                         FieldOffset))
            {
                if (imageSize >= Math.Max(SignatureLength, sizeof(long)) &&
                    getter.InstructionAddress <= imageSize - SignatureLength &&
                    getter.ResolvedPointerStorage <= imageSize - sizeof(long))
                {
                    matches.Add(new ExecutableSignatureMatch(
                        getter.InstructionAddress,
                        getter.ResolvedPointerStorage));
                }
            }
        }

        if (machine != Amd64Machine)
        {
            matches.Clear();
        }

        return new ExecutableSignatureScanResult(machine, imageSize, matches);
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, sizeof(ushort), "UInt16 field");
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, sizeof(uint), "UInt32 field");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
    }

    private static int ReadInt32(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, sizeof(int), "Int32 field");
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
    }

    private static void EnsureRange(byte[] bytes, int offset, int length, string name)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new InvalidDataException($"The {name} extends outside the file.");
        }
    }
}
