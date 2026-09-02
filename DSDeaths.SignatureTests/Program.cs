using System;
using System.Collections.Generic;

namespace DSDeaths {
    internal static class SignatureTestsProgram {
        private static int failures;

        private static int Main() {
            TestEldenRing116Evidence();
            TestEldenRing117Evidence();
            TestDisplacementIsWildcardedByMatching();
            TestChangedFieldOffsetIsRejected();
            TestMultipleMatchesAreReported();

            if (failures == 0) {
                Console.WriteLine("All Elden Ring signature tests passed.");
                return 0;
            }

            Console.Error.WriteLine(failures + " Elden Ring signature test(s) failed.");
            return 1;
        }

        private static void TestEldenRing116Evidence() {
            const long instructionAddress = 0x00007FF6F00B6050L;
            const long expectedPointerStorage = 0x00007FF6F3BBDF38L;
            byte[] bytes = {
                0x48, 0x8B, 0x05, 0xE1, 0x7E, 0xB0, 0x03,
                0x48, 0x85, 0xC0, 0x74, 0x07,
                0x8B, 0x80, 0x94, 0x00, 0x00, 0x00,
                0xC3, 0xC3
            };

            List<EldenRingSignatureMatch> matches = EldenRingSignature.Find(bytes, instructionAddress);

            AssertEqual("1.16 focused signature count", 1, matches.Count);
            AssertEqual("1.16 pointer storage", expectedPointerStorage, matches[0].PointerStorageAddress);
        }

        private static void TestEldenRing117Evidence() {
            const long instructionAddress = 0x00007FF69C036020L;
            const long expectedPointerStorage = 0x00007FF69FB41F98L;
            byte[] bytes = {
                0x48, 0x8B, 0x05, 0x71, 0xBF, 0xB0, 0x03,
                0x48, 0x85, 0xC0, 0x74, 0x07,
                0x8B, 0x80, 0x94, 0x00, 0x00, 0x00,
                0xC3, 0xC3
            };

            List<EldenRingSignatureMatch> matches = EldenRingSignature.Find(bytes, instructionAddress);

            AssertEqual("1.17 focused signature count", 1, matches.Count);
            AssertEqual("1.17 pointer storage", expectedPointerStorage, matches[0].PointerStorageAddress);
        }

        private static void TestDisplacementIsWildcardedByMatching() {
            const long bufferAddress = 0x0000000010001000L;
            const long expectedPointerStorage = 0x0000000010000010L;
            byte[] bytes = CreateSignatureBytes();
            int displacement = checked((int)(expectedPointerStorage - (bufferAddress + 7)));
            WriteInt32LittleEndian(bytes, 3, displacement);

            List<EldenRingSignatureMatch> matches = EldenRingSignature.Find(bytes, bufferAddress);

            AssertEqual("negative displacement signature count", 1, matches.Count);
            AssertEqual("negative displacement pointer storage", expectedPointerStorage, matches[0].PointerStorageAddress);
        }

        private static void TestChangedFieldOffsetIsRejected() {
            byte[] bytes = CreateSignatureBytes();
            bytes[14] = 0x98;

            List<EldenRingSignatureMatch> matches = EldenRingSignature.Find(bytes, 0x1000);

            AssertEqual("changed field offset is rejected", 0, matches.Count);
        }

        private static void TestMultipleMatchesAreReported() {
            byte[] signature = CreateSignatureBytes();
            var buffer = new byte[signature.Length * 2 + 10];
            Array.Copy(signature, 0, buffer, 2, signature.Length);
            Array.Copy(signature, 0, buffer, signature.Length + 6, signature.Length);

            List<EldenRingSignatureMatch> matches = EldenRingSignature.Find(buffer, 0x2000);

            AssertEqual("multiple signatures are all reported", 2, matches.Count);
        }

        private static byte[] CreateSignatureBytes() {
            return new byte[] {
                0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x00,
                0x48, 0x85, 0xC0, 0x74, 0x07,
                0x8B, 0x80, 0x94, 0x00, 0x00, 0x00,
                0xC3, 0xC3
            };
        }

        private static void WriteInt32LittleEndian(byte[] buffer, int index, int value) {
            uint unsigned = unchecked((uint)value);
            buffer[index] = (byte)unsigned;
            buffer[index + 1] = (byte)(unsigned >> 8);
            buffer[index + 2] = (byte)(unsigned >> 16);
            buffer[index + 3] = (byte)(unsigned >> 24);
        }

        private static void AssertEqual<T>(string name, T expected, T actual) {
            if (Equals(expected, actual)) {
                Console.WriteLine("PASS: " + name);
                return;
            }

            failures++;
            Console.Error.WriteLine(
                "FAIL: " + name + " (expected: " + expected + ", actual: " + actual + ")");
        }
    }
}
