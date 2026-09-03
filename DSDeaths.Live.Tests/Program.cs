using System;
using System.IO;
using System.Reflection;
using DSDeaths;
using DSDeaths.Live;

internal static class Program {
    private static int failures;

    private static int Main() {
        TestDefaults();
        TestRoundTrip();
        TestInvalidValues();
        TestReadProcessMemorySignatures();
        TestLocalizedMonitorMessages();

        if (failures == 0) {
            Console.WriteLine("All DSDeaths Live tests passed.");
            return 0;
        }

        Console.Error.WriteLine(failures + " DSDeaths Live test(s) failed.");
        return 1;
    }

    private static void TestDefaults() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ini");
        string warning;
        LiveSettings settings = LiveSettings.Load(path, out warning);

        Check(settings.Language == "auto", "default language is automatic");
        Check(settings.MinimizeToTray, "notification-area mode is enabled by default");
        Check(!settings.OverlayVisible, "overlay is hidden by default");
        Check(settings.OverlayBackgroundOpacity == 70, "overlay background opacity defaults to 70 percent");
        Check(settings.OverlayTextColor == "#FFFFFF", "overlay text defaults to white");
        Check(settings.OverlayFontFamily == "Segoe UI", "overlay font defaults to Segoe UI");
        Check(settings.OverlayFontSize == 58, "overlay font size defaults to 58");
        Check(settings.OverlayScalePercent == 100, "overlay scale defaults to 100 percent");
        Check(settings.OverlayTextShadow == "soft", "overlay text shadow defaults to soft");
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
                OverlayVisible = true,
                OverlayBackgroundOpacity = 35,
                OverlayTextColor = "#12ABEF",
                OverlayFontFamily = "Meiryo",
                OverlayFontSize = 72,
                OverlayScalePercent = 135,
                OverlayTextShadow = "strong"
            };
            string error;
            Check(original.TrySave(path, out error), "GUI settings are saved");
            Check(error == null, "successful GUI settings save has no error");

            string warning;
            LiveSettings restored = LiveSettings.Load(path, out warning);
            Check(restored.Language == "ja", "saved language is restored");
            Check(!restored.MinimizeToTray, "saved notification-area mode is restored");
            Check(restored.OverlayVisible, "saved overlay visibility is restored");
            Check(restored.OverlayBackgroundOpacity == 35, "saved overlay opacity is restored");
            Check(restored.OverlayTextColor == "#12ABEF", "saved overlay text color is restored");
            Check(restored.OverlayFontFamily == "Meiryo", "saved overlay font is restored");
            Check(restored.OverlayFontSize == 72, "saved overlay font size is restored");
            Check(restored.OverlayScalePercent == 135, "saved overlay scale is restored");
            Check(restored.OverlayTextShadow == "strong", "saved overlay text shadow is restored");
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
                "OverlayVisible=maybe",
                "OverlayBackgroundOpacity=101",
                "OverlayTextColor=rainbow",
                "OverlayFontFamily=",
                "OverlayFontSize=200",
                "OverlayScalePercent=250",
                "OverlayTextShadow=neon"
            });

            string warning;
            LiveSettings settings = LiveSettings.Load(path, out warning);
            Check(settings.Language == "auto", "invalid language keeps the default");
            Check(settings.MinimizeToTray, "invalid notification-area value keeps the default");
            Check(!settings.OverlayVisible, "invalid overlay value keeps the default");
            Check(settings.OverlayBackgroundOpacity == 70, "invalid opacity keeps the default");
            Check(settings.OverlayTextColor == "#FFFFFF", "invalid text color keeps the default");
            Check(settings.OverlayFontFamily == "Segoe UI", "invalid font keeps the default");
            Check(settings.OverlayFontSize == 58, "invalid font size keeps the default");
            Check(settings.OverlayScalePercent == 100, "invalid scale keeps the default");
            Check(settings.OverlayTextShadow == "soft", "invalid text shadow keeps the default");
            Check(!string.IsNullOrEmpty(warning), "invalid GUI settings produce a warning");
        } finally {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReadProcessMemorySignatures() {
        CheckReadProcessMemorySignature(typeof(DSDeathsMonitor), "monitor");
        CheckReadProcessMemorySignature(typeof(EldenRingSignatureResolver), "signature resolver");
    }

    private static void CheckReadProcessMemorySignature(Type owner, string name) {
        MethodInfo method = owner.GetMethod(
            "ReadProcessMemory",
            BindingFlags.NonPublic | BindingFlags.Static);
        Check(method != null, name + " declares ReadProcessMemory");
        if (method == null) {
            return;
        }

        ParameterInfo[] parameters = method.GetParameters();
        Check(parameters.Length == 5, name + " ReadProcessMemory has five parameters");
        if (parameters.Length != 5) {
            return;
        }

        Check(parameters[3].ParameterType == typeof(UIntPtr),
            name + " uses pointer-width nSize");
        Check(parameters[4].ParameterType == typeof(UIntPtr).MakeByRefType() && parameters[4].IsOut,
            name + " uses pointer-width bytesRead output");
    }

    private static void TestLocalizedMonitorMessages() {
        Localization.SetLanguage("ja");
        string japanese = MonitorMessageFormatter.Format(
            MonitorMessageCode.SignatureResolved,
            "fallback",
            "00000011",
            "00000022");
        Check(
            japanese == "シグネチャを一意に解決しました（getter RVA 0x00000011、pointer RVA 0x00000022）。",
            "signature success detail is localized in Japanese");
        Check(
            MonitorMessageFormatter.Format(
                MonitorMessageCode.SignatureResolutionFailed,
                "English diagnostic",
                null,
                null) ==
            "Elden Ringのデスカウンターを安全に解決できませんでした。このゲームバージョンには対応していません。",
            "signature failure detail is localized in Japanese");

        Localization.SetLanguage("en");
        string english = MonitorMessageFormatter.Format(
            MonitorMessageCode.ReadMemoryFailed,
            "fallback",
            "Access denied",
            null);
        Check(english == "Could not read game memory: Access denied",
            "monitor detail is localized in English");
        Check(
            MonitorMessageFormatter.Format(MonitorMessageCode.None, "fallback", null, null) == "fallback",
            "unstructured monitor detail keeps its fallback text");
        Localization.SetLanguage("auto");
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
