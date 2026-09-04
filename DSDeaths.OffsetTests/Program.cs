using System;
using System.IO;

namespace DSDeaths {
    static class OffsetTestsProgram {
        static int failures;

        static int Main() {
            string testDirectory = Path.Combine(Path.GetTempPath(), "DSDeaths.OffsetTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);

            try {
                TestAdjustment();
                TestSettingsRoundTrip(testDirectory);
                TestInvalidSettings(testDirectory);
            } finally {
                Directory.Delete(testDirectory, true);
            }

            if (failures == 0) {
                Console.WriteLine("All offset tests passed.");
                return 0;
            }

            Console.Error.WriteLine(failures + " offset test(s) failed.");
            return 1;
        }

        static void TestAdjustment() {
            var settings = new EldenRingOffsetSettings();
            AssertEqual("disabled offset returns raw count", 33506, settings.Apply(33506));

            settings.Offset = 33504;
            settings.Enabled = true;
            AssertEqual("enabled offset subtracts baseline", 2, settings.Apply(33506));
            AssertEqual("current baseline displays zero", 0, settings.Apply(33504));
            AssertEqual("value below baseline clamps to zero", 0, settings.Apply(100));
        }

        static void TestSettingsRoundTrip(string testDirectory) {
            string path = Path.Combine(testDirectory, "round-trip.ini");
            var settings = new EldenRingOffsetSettings { Enabled = true, Offset = 33506 };
            string error;
            AssertTrue("initial settings save", settings.TrySave(path, out error), error);

            string warning;
            OffsetSettingsWarning[] warningDetails;
            EldenRingOffsetSettings loaded = EldenRingOffsetSettings.Load(
                path,
                out warning,
                out warningDetails);
            AssertEqual("saved enabled state is restored", true, loaded.Enabled);
            AssertEqual("saved baseline is restored", 33506, loaded.Offset);
            AssertEqual("valid settings have no warning", null, warning);
            AssertEqual("valid settings have no structured warnings", 0, warningDetails.Length);

            loaded.Enabled = false;
            loaded.Offset = 40000;
            AssertTrue("existing settings are replaced", loaded.TrySave(path, out error), error);

            EldenRingOffsetSettings replaced = EldenRingOffsetSettings.Load(path, out warning);
            AssertEqual("replaced enabled state is restored", false, replaced.Enabled);
            AssertEqual("replaced baseline is restored", 40000, replaced.Offset);
        }

        static void TestInvalidSettings(string testDirectory) {
            string path = Path.Combine(testDirectory, "invalid.ini");
            File.WriteAllLines(path, new[]
            {
                "EldenRingOffsetEnabled=on",
                "EldenRingDeathOffset=-1",
                "malformed"
            });

            string warning;
            OffsetSettingsWarning[] warningDetails;
            EldenRingOffsetSettings loaded = EldenRingOffsetSettings.Load(
                path,
                out warning,
                out warningDetails);
            AssertEqual("on is accepted for enabled state", true, loaded.Enabled);
            AssertEqual("negative baseline is ignored", 0, loaded.Offset);
            AssertTrue("invalid settings produce a warning", !string.IsNullOrEmpty(warning), warning);
            AssertEqual("invalid settings produce structured warnings", 2, warningDetails.Length);
            AssertEqual(
                "invalid baseline warning is structured",
                OffsetSettingsWarningCode.InvalidValue,
                warningDetails[0].Code);
            AssertEqual(
                "malformed line warning is structured",
                OffsetSettingsWarningCode.MalformedLine,
                warningDetails[1].Code);
        }

        static void AssertTrue(string name, bool actual, string details) {
            if (actual) {
                Console.WriteLine("PASS: " + name);
                return;
            }

            failures++;
            Console.Error.WriteLine("FAIL: " + name + (string.IsNullOrEmpty(details) ? string.Empty : " (" + details + ")"));
        }

        static void AssertEqual<T>(string name, T expected, T actual) {
            if (object.Equals(expected, actual)) {
                Console.WriteLine("PASS: " + name);
                return;
            }

            failures++;
            Console.Error.WriteLine("FAIL: " + name + " (expected: " + expected + ", actual: " + actual + ")");
        }
    }
}
