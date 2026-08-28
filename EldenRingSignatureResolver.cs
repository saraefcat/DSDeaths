using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DSDeaths {
    internal static class EldenRingSignatureResolver {
        private const uint MemCommit = 0x1000;
        private const uint PageExecute = 0x10;
        private const uint PageExecuteRead = 0x20;
        private const uint PageExecuteReadWrite = 0x40;
        private const uint PageExecuteWriteCopy = 0x80;
        private const uint PageGuard = 0x100;
        private const uint PageNoAccess = 0x01;
        private const int ChunkSize = 1024 * 1024;

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation {
            internal IntPtr BaseAddress;
            internal IntPtr AllocationBase;
            internal uint AllocationProtect;
            internal ushort PartitionId;
            internal ushort Reserved;
            internal UIntPtr RegionSize;
            internal uint State;
            internal uint Protect;
            internal uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQueryEx(
            IntPtr process,
            IntPtr address,
            out MemoryBasicInformation buffer,
            UIntPtr bufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr process,
            IntPtr baseAddress,
            byte[] buffer,
            int size,
            ref int bytesRead);

        internal static bool TryResolve(
            IntPtr process,
            IntPtr moduleBase,
            int moduleSize,
            out int pointerStorageRva,
            out long instructionAddress,
            out string error) {
            pointerStorageRva = 0;
            instructionAddress = 0;
            error = null;

            if (process == IntPtr.Zero) {
                error = "The game process handle is invalid.";
                return false;
            }

            if (moduleSize <= 0) {
                error = "The eldenring.exe module size is invalid.";
                return false;
            }

            long moduleStart = moduleBase.ToInt64();
            long moduleEnd;
            try {
                moduleEnd = checked(moduleStart + moduleSize);
            } catch (OverflowException) {
                error = "The eldenring.exe module range overflowed.";
                return false;
            }

            var matches = new List<EldenRingSignatureMatch>();
            long queryAddress = moduleStart;
            int memoryBasicInformationSize = Marshal.SizeOf(typeof(MemoryBasicInformation));

            while (queryAddress < moduleEnd) {
                MemoryBasicInformation region;
                UIntPtr queryResult = VirtualQueryEx(
                    process,
                    new IntPtr(queryAddress),
                    out region,
                    new UIntPtr((uint)memoryBasicInformationSize));

                if (queryResult == UIntPtr.Zero) {
                    int errorCode = Marshal.GetLastWin32Error();
                    error = "Could not inspect eldenring.exe memory: " + DescribeWin32Error(errorCode);
                    return false;
                }

                long regionBase = region.BaseAddress.ToInt64();
                ulong regionSizeUnsigned = region.RegionSize.ToUInt64();
                if (regionSizeUnsigned == 0 || regionSizeUnsigned > long.MaxValue) {
                    error = "Windows returned an invalid memory-region size.";
                    return false;
                }

                long regionEnd;
                try {
                    regionEnd = checked(regionBase + (long)regionSizeUnsigned);
                } catch (OverflowException) {
                    error = "A Windows memory-region range overflowed.";
                    return false;
                }

                if (regionEnd <= queryAddress) {
                    error = "Windows returned a non-advancing memory region.";
                    return false;
                }

                long clippedStart = Math.Max(regionBase, moduleStart);
                long clippedEnd = Math.Min(regionEnd, moduleEnd);

                if (clippedStart < clippedEnd && IsReadableExecutable(region)) {
                    if (!ScanRegion(
                            process,
                            clippedStart,
                            clippedEnd,
                            matches,
                            out error)) {
                        return false;
                    }
                }

                queryAddress = regionEnd;
            }

            if (matches.Count != 1) {
                error = "The Elden Ring death-count signature matched " + matches.Count +
                        " locations; exactly one is required. This game version is not supported safely.";
                return false;
            }

            EldenRingSignatureMatch match = matches[0];
            if (match.PointerStorageAddress < moduleStart ||
                match.PointerStorageAddress > moduleEnd - sizeof(long)) {
                error = "The signature resolved outside the eldenring.exe module.";
                return false;
            }

            long rva = match.PointerStorageAddress - moduleStart;
            if (rva < 0 || rva > int.MaxValue) {
                error = "The resolved pointer-storage RVA is outside the supported range.";
                return false;
            }

            byte[] pointerStorage = new byte[sizeof(long)];
            int bytesRead = 0;
            if (!ReadProcessMemory(
                    process,
                    new IntPtr(match.PointerStorageAddress),
                    pointerStorage,
                    pointerStorage.Length,
                    ref bytesRead) ||
                bytesRead != pointerStorage.Length) {
                int errorCode = Marshal.GetLastWin32Error();
                error = "The resolved pointer storage could not be read: " + DescribeWin32Error(errorCode);
                return false;
            }

            pointerStorageRva = (int)rva;
            instructionAddress = match.InstructionAddress;
            return true;
        }

        private static bool ScanRegion(
            IntPtr process,
            long regionStart,
            long regionEnd,
            List<EldenRingSignatureMatch> matches,
            out string error) {
            error = null;
            long uniqueStart = regionStart;

            while (uniqueStart < regionEnd) {
                long uniqueEnd = Math.Min(regionEnd, uniqueStart + ChunkSize);
                long readEnd = Math.Min(
                    regionEnd,
                    uniqueEnd + EldenRingSignature.PatternLength - 1);
                int readLength = checked((int)(readEnd - uniqueStart));
                var buffer = new byte[readLength];
                int bytesRead = 0;

                if (!ReadProcessMemory(
                        process,
                        new IntPtr(uniqueStart),
                        buffer,
                        readLength,
                        ref bytesRead) ||
                    bytesRead != readLength) {
                    int errorCode = Marshal.GetLastWin32Error();
                    error = "Could not scan executable game memory at 0x" +
                            uniqueStart.ToString("X16") + ": " + DescribeWin32Error(errorCode);
                    return false;
                }

                List<EldenRingSignatureMatch> chunkMatches = EldenRingSignature.Find(buffer, uniqueStart);
                foreach (EldenRingSignatureMatch match in chunkMatches) {
                    if (match.InstructionAddress >= uniqueStart &&
                        match.InstructionAddress < uniqueEnd) {
                        matches.Add(match);
                    }
                }

                uniqueStart = uniqueEnd;
            }

            return true;
        }

        private static bool IsReadableExecutable(MemoryBasicInformation region) {
            if (region.State != MemCommit ||
                (region.Protect & PageGuard) != 0 ||
                (region.Protect & PageNoAccess) != 0) {
                return false;
            }

            uint basicProtection = region.Protect & 0xFF;
            return basicProtection == PageExecute ||
                   basicProtection == PageExecuteRead ||
                   basicProtection == PageExecuteReadWrite ||
                   basicProtection == PageExecuteWriteCopy;
        }

        private static string DescribeWin32Error(int errorCode) {
            if (errorCode == 0) {
                return "unknown Windows error";
            }

            return new Win32Exception(errorCode).Message + " (code " + errorCode + ")";
        }
    }
}
