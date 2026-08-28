using System;
using System.Collections.Generic;

namespace DSDeaths {
    internal sealed class EldenRingSignatureMatch {
        internal EldenRingSignatureMatch(long instructionAddress, long pointerStorageAddress) {
            InstructionAddress = instructionAddress;
            PointerStorageAddress = pointerStorageAddress;
        }

        internal long InstructionAddress { get; private set; }
        internal long PointerStorageAddress { get; private set; }
    }

    internal static class EldenRingSignature {
        internal const int FieldOffset = 0x94;
        internal const int PatternLength = 20;
        internal const string PatternText =
            "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 07 8B 80 94 00 00 00 C3 C3";

        internal static List<EldenRingSignatureMatch> Find(byte[] buffer, long bufferAddress) {
            if (buffer == null) {
                throw new ArgumentNullException("buffer");
            }

            var matches = new List<EldenRingSignatureMatch>();

            for (int index = 0; index <= buffer.Length - PatternLength; index++) {
                if (!MatchesFixedBytes(buffer, index)) {
                    continue;
                }

                int displacement = ReadInt32LittleEndian(buffer, index + 3);
                long instructionAddress = checked(bufferAddress + index);
                long pointerStorageAddress = checked(instructionAddress + 7 + displacement);
                matches.Add(new EldenRingSignatureMatch(instructionAddress, pointerStorageAddress));
            }

            return matches;
        }

        private static bool MatchesFixedBytes(byte[] buffer, int index) {
            return buffer[index] == 0x48 &&
                   buffer[index + 1] == 0x8B &&
                   buffer[index + 2] == 0x05 &&
                   buffer[index + 7] == 0x48 &&
                   buffer[index + 8] == 0x85 &&
                   buffer[index + 9] == 0xC0 &&
                   buffer[index + 10] == 0x74 &&
                   buffer[index + 11] == 0x07 &&
                   buffer[index + 12] == 0x8B &&
                   buffer[index + 13] == 0x80 &&
                   buffer[index + 14] == 0x94 &&
                   buffer[index + 15] == 0x00 &&
                   buffer[index + 16] == 0x00 &&
                   buffer[index + 17] == 0x00 &&
                   buffer[index + 18] == 0xC3 &&
                   buffer[index + 19] == 0xC3;
        }

        private static int ReadInt32LittleEndian(byte[] buffer, int index) {
            uint value = buffer[index] |
                         ((uint)buffer[index + 1] << 8) |
                         ((uint)buffer[index + 2] << 16) |
                         ((uint)buffer[index + 3] << 24);
            return unchecked((int)value);
        }
    }
}
