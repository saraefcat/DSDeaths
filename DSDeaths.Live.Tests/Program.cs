using System;
using System.IO;
using DSDeaths.Live;

internal static class Program {
    private static int failures;

    private static int Main() {
        TestDefaults();
        TestRoundTrip();
        TestInvalidValues();

        if (failures == 0) {
            Console.WriteLine("All DSDeaths Live settings tests passed.");
            return 0;
        }

        Console.Error.WriteLine(failures + " DSDeaths Live settings test(s) failed.");
        return 1;
    }

    private static void TestDefaults() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ini");
        string warning;
        LiveSettings settings = LiveSettings.Load(path, out warning);

        Check(settings.Language == "auto", "default language is automatic");
        Check(settings.MinimizeToTray, "notification-area mode is enabled by default");
        Check(!settings.OverlayVisible, "overlay is hidden by default");
        Check(warning == null, "missing settings file has no warning");
    }

    private static void TestRoundTrip() {
        string directory = Path.Combine(Path.GetTempPath(), "DSDeaths.Live.Tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, LiveSettings.FileName);
        Directory.CreateDirectory(directory);

        try {
            var original = new LiveSettings {
                Language = "ja",
                MinimizeToTray = false,
                OverlayVisible = true
            };
            string error;
            Check(original.TrySave(path, out error), "GUI settings are saved");
            Check(error == null, "successful GUI settings save has no error");

            string warning;
            LiveSettings restored = LiveSettings.Load(path, out warning);
            Check(restored.Language == "ja", "saved language is restored");
            Check(!restored.MinimizeToTray, "saved notification-area mode is restored");
            Check(restored.OverlayVisible, "saved overlay visibility is restored");
            Check(warning == null, "valid GUI settings have no warning");
        } finally {
            Directory.Delete(directory, true);
        }
    }

    private static void TestInvalidValues() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ini");
        try {
            File.WriteAllLines(path, new[] {
                "Language=xx",
                "MinimizeToTray=perhaps",
                "OverlayVisible=maybe"
            });

            string warning;
            LiveSettings settings = LiveSettings.Load(path, out warning);
            Check(settings.Language == "auto", "invalid language keeps the default");
            Check(settings.MinimizeToTray, "invalid notification-area value keeps the default");
            Check(!settings.OverlayVisible, "invalid overlay value keeps the default");
            Check(!string.IsNullOrEmpty(warning), "invalid GUI settings produce a warning");
        } finally {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void Check(bool condition, string name) {
        if (condition) {
            Console.WriteLine("PASS: " + name);
        } else {
            Console.Error.WriteLine("FAIL: " + name);
            failures++;
        }
    }
}
