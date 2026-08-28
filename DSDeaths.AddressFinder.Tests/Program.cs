using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace DSDeaths.AddressFinder;

internal static class AddressFinderTestsProgram
{
    private static int _failures;

    private static int Main()
    {
        TestReferenceDetection();
        TestNegativeDisplacement();
        TestAddressOverrideIsIgnored();
        TestPrefixLikePreviousByteDoesNotHideReference();
        TestSignatureWildcardsOnlyTargetDisplacement();

        if (_failures == 0)
        {
            Console.WriteLine("All Address Finder scanner tests passed.");
            return 0;
        }

        Console.Error.WriteLine($"{_failures} Address Finder scanner test(s) failed.");
        return 1;
    }

    private static void TestReferenceDetection()
    {
        const ulong bufferAddress = 0x0000000010000000;
        const ulong targetAddress = 0x0000000010000200;
        var buffer = new byte[128];
        Array.Fill(buffer, (byte)0x90);

        WriteRipInstruction(buffer, bufferAddress, 10, new byte[] { 0x48, 0x8B, 0x05 }, targetAddress, 0);
        WriteRipInstruction(buffer, bufferAddress, 40, new byte[] { 0x8B, 0x0D }, targetAddress, 0);
        WriteRipInstruction(buffer, bufferAddress, 70, new byte[] { 0x48, 0x83, 0x3D }, targetAddress, 1);
        buffer[78] = 0;

        List<RipRelativeReference> references = RipRelativeReferenceScanner.Find(
            buffer,
            bufferAddress,
            targetAddress);

        AssertEqual("three supported references are detected", 3, references.Count);
        AssertEqual("MOV load instruction length", 7, references[0].InstructionLength);
        AssertEqual("non-REX instruction length", 6, references[1].InstructionLength);
        AssertEqual("immediate instruction length", 8, references[2].InstructionLength);
    }

    private static void TestNegativeDisplacement()
    {
        const ulong bufferAddress = 0x0000000020001000;
        const ulong targetAddress = 0x0000000020000010;
        var buffer = new byte[32];
        Array.Fill(buffer, (byte)0x90);
        WriteRipInstruction(buffer, bufferAddress, 8, new byte[] { 0x48, 0x8B, 0x0D }, targetAddress, 0);

        List<RipRelativeReference> references = RipRelativeReferenceScanner.Find(
            buffer,
            bufferAddress,
            targetAddress);

        AssertEqual("negative disp32 is resolved", 1, references.Count);
        AssertEqual("negative disp32 target", targetAddress, references[0].ResolvedAddress);
    }

    private static void TestAddressOverrideIsIgnored()
    {
        const ulong bufferAddress = 0x0000000030000000;
        const ulong targetAddress = 0x0000000030000100;
        var buffer = new byte[32];
        Array.Fill(buffer, (byte)0x90);
        WriteRipInstruction(buffer, bufferAddress, 4, new byte[] { 0x67, 0x48, 0x8B, 0x05 }, targetAddress, 0);

        List<RipRelativeReference> references = RipRelativeReferenceScanner.Find(
            buffer,
            bufferAddress,
            targetAddress);

        AssertEqual("address-size override is not treated as RIP-relative", 0, references.Count);
    }

    private static void TestSignatureWildcardsOnlyTargetDisplacement()
    {
        const ulong bufferAddress = 0x0000000040000000;
        const ulong targetAddress = 0x0000000040000100;
        var buffer = new byte[64];
        Array.Fill(buffer, (byte)0x90);
        WriteRipInstruction(buffer, bufferAddress, 20, new byte[] { 0x48, 0x8B, 0x05 }, targetAddress, 0);

        RipRelativeReference reference = RipRelativeReferenceScanner.Find(
            buffer,
            bufferAddress,
            targetAddress)[0];
        SignatureWindow window = RipRelativeReferenceScanner.CreateSignatureWindow(
            buffer,
            bufferAddress,
            reference);

        AssertTrue(
            "signature wildcards the target disp32",
            window.Pattern.Contains("48 8B 05 ?? ?? ?? ??", StringComparison.Ordinal));
        AssertEqual("signature contains exactly four wildcards", 4, CountWildcards(window.Pattern));
        AssertTrue(
            "exact window retains displacement bytes",
            !window.ExactBytes.Contains("??", StringComparison.Ordinal));
    }

    private static void TestPrefixLikePreviousByteDoesNotHideReference()
    {
        const ulong bufferAddress = 0x0000000038000000;
        const ulong targetAddress = 0x0000000038000100;
        var buffer = new byte[48];
        Array.Fill(buffer, (byte)0x90);
        buffer[7] = 0x48;
        WriteRipInstruction(buffer, bufferAddress, 8, new byte[] { 0x48, 0x8B, 0x05 }, targetAddress, 0);

        List<RipRelativeReference> references = RipRelativeReferenceScanner.Find(
            buffer,
            bufferAddress,
            targetAddress);

        AssertEqual("prefix-like preceding byte does not hide a reference", 1, references.Count);
        AssertEqual("reference starts at the real REX byte", bufferAddress + 8, references[0].InstructionAddress);
    }

    private static void WriteRipInstruction(
        byte[] buffer,
        ulong bufferAddress,
        int index,
        byte[] bytesBeforeDisplacement,
        ulong targetAddress,
        int immediateLength)
    {
        Array.Copy(bytesBeforeDisplacement, 0, buffer, index, bytesBeforeDisplacement.Length);
        int instructionLength = bytesBeforeDisplacement.Length + sizeof(int) + immediateLength;
        ulong nextInstruction = checked(bufferAddress + (ulong)index + (ulong)instructionLength);
        int displacement = checked((int)((long)targetAddress - (long)nextInstruction));
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(index + bytesBeforeDisplacement.Length, sizeof(int)),
            displacement);
    }

    private static int CountWildcards(string pattern)
    {
        int count = 0;
        foreach (string part in pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "??")
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertTrue(string name, bool actual)
    {
        if (actual)
        {
            Console.WriteLine("PASS: " + name);
            return;
        }

        _failures++;
        Console.Error.WriteLine("FAIL: " + name);
    }

    private static void AssertEqual<T>(string name, T expected, T actual)
    {
        if (Equals(expected, actual))
        {
            Console.WriteLine("PASS: " + name);
            return;
        }

        _failures++;
        Console.Error.WriteLine($"FAIL: {name} (expected: {expected}, actual: {actual})");
    }
}
