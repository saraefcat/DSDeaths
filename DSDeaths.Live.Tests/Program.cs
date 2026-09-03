using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Xml;
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
        TestOverlayGeometry();
        TestLocalizedOperationErrors();
        TestLocalizedSettingsWarnings();
        TestLocalizationResourceParity();
        TestOverlayAppearanceGridAlignment();
        TestDpiConfiguration();

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
        Check(!settings.OverlayPositionSet, "overlay position is unset by default");
        Check(!settings.OverlayPositionLocked, "overlay position is unlocked by default");
        Check(settings.OverlayShowBorder, "overlay border is shown by default");
        Check(settings.OverlayShowLabel, "overlay label is shown by default");
        Check(settings.OverlayTopmost, "overlay is topmost by default");
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
                OverlayTextShadow = "strong",
                OverlayPositionSet = true,
                OverlayLeft = -1234.5,
                OverlayTop = 234.25,
                OverlayPositionLocked = true,
                OverlayShowBorder = false,
                OverlayShowLabel = false,
                OverlayTopmost = false
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
            Check(restored.OverlayPositionSet, "saved overlay position flag is restored");
            Check(Math.Abs(restored.OverlayLeft - -1234.5) < 0.001, "saved overlay left position is restored");
            Check(Math.Abs(restored.OverlayTop - 234.25) < 0.001, "saved overlay top position is restored");
            Check(restored.OverlayPositionLocked, "saved overlay position lock is restored");
            Check(!restored.OverlayShowBorder, "saved overlay border setting is restored");
            Check(!restored.OverlayShowLabel, "saved overlay label setting is restored");
            Check(!restored.OverlayTopmost, "saved overlay topmost setting is restored");
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
                "OverlayTextShadow=neon",
                "OverlayPositionSet=perhaps",
                "OverlayLeft=NaN",
                "OverlayTop=Infinity",
                "OverlayPositionLocked=perhaps",
                "OverlayShowBorder=perhaps",
                "OverlayShowLabel=perhaps",
                "OverlayTopmost=perhaps"
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
            Check(!settings.OverlayPositionSet, "invalid position flag keeps the default");
            Check(settings.OverlayLeft == 0 && settings.OverlayTop == 0,
                "invalid overlay coordinates keep the defaults");
            Check(!settings.OverlayPositionLocked, "invalid position lock keeps the default");
            Check(settings.OverlayShowBorder, "invalid border setting keeps the default");
            Check(settings.OverlayShowLabel, "invalid label setting keeps the default");
            Check(settings.OverlayTopmost, "invalid topmost setting keeps the default");
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

    private static void TestOverlayGeometry() {
        var secondaryWorkArea = new Rect(-1920, 0, 1920, 1040);
        Point centered = OverlayGeometry.CenterTopLeft(secondaryWorkArea, new Size(430, 180));
        Check(Math.Abs(centered.X - -1175) < 0.001 && Math.Abs(centered.Y - 430) < 0.001,
            "overlay centers on a negative-coordinate monitor");

        Point clamped = OverlayGeometry.ClampTopLeft(
            secondaryWorkArea,
            new Size(430, 180),
            new Point(-2500, 1000));
        Check(Math.Abs(clamped.X - -1920) < 0.001 && Math.Abs(clamped.Y - 860) < 0.001,
            "overlay position is clamped to the selected work area");

        Point oversized = OverlayGeometry.ClampTopLeft(
            new Rect(0, 0, 300, 100),
            new Size(430, 180),
            new Point(50, 50));
        Check(oversized.X == 0 && oversized.Y == 0,
            "oversized overlay stays anchored to the work-area origin");
        Check(OverlayGeometry.AdditionalHeight(400, 450) == 50,
            "column balancing adds only the missing height");
        Check(OverlayGeometry.AdditionalHeight(450, 400) == 0,
            "column balancing never adds negative height");
    }

    private static void TestLocalizedOperationErrors() {
        Localization.SetLanguage("ja");
        Check(
            MonitorOperationErrorFormatter.Format(
                MonitorOperationErrorCode.EldenRingNotConnected,
                "fallback") == "Elden Ringに接続していません。",
            "offset operation error is localized in Japanese");
        Check(
            MonitorOperationErrorFormatter.Format(
                MonitorOperationErrorCode.SettingsSaveFailed,
                "Access denied") == "ゼロ基準の設定を保存できませんでした: Access denied",
            "offset settings save error is localized in Japanese");
        Localization.SetLanguage("auto");
    }

    private static void TestLocalizedSettingsWarnings() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ini");
        try {
            File.WriteAllText(path, "OverlayScalePercent=999");
            string fallback;
            LiveSettingsWarning[] warnings;
            LiveSettings.Load(path, out fallback, out warnings);
            Localization.SetLanguage("ja");
            string localized = SettingsWarningFormatter.Format(warnings);
            Check(localized == "正しくないGUI設定値を無視しました（OverlayScalePercent）: 999",
                "GUI settings warning is localized in Japanese");
        } finally {
            Localization.SetLanguage("auto");
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestLocalizationResourceParity() {
        string root = FindRepositoryRoot();
        var english = LoadResourceKeys(Path.Combine(
            root,
            "DSDeaths.Live",
            "Resources",
            "Strings.resx"));
        var japanese = LoadResourceKeys(Path.Combine(
            root,
            "DSDeaths.Live",
            "Resources",
            "Strings.ja.resx"));

        Check(english.SetEquals(japanese),
            "English and Japanese resources contain the same keys");
    }

    private static void TestOverlayAppearanceGridAlignment() {
        string root = FindRepositoryRoot();
        var document = new XmlDocument();
        document.Load(Path.Combine(root, "DSDeaths.Live", "MainWindow.xaml"));
        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace("x", "http://schemas.microsoft.com/winfx/2006/xaml");

        CheckGridCell(document, namespaces, "OverlayScaleSlider", 0, 0);
        CheckGridCell(document, namespaces, "OverlayFontFamilyComboBox", 0, 2);
        CheckGridCell(document, namespaces, "OverlayOpacitySlider", 2, 0);
        CheckGridCell(document, namespaces, "TextColorButton", 2, 2);
        CheckGridCell(document, namespaces, "OverlayFontSizeSlider", 4, 0);
        CheckGridCell(document, namespaces, "OverlayTextShadowComboBox", 4, 2);
        CheckGridCell(document, namespaces, "OverlayPositionLockedCheckBox", 0, 0);
        CheckGridCell(document, namespaces, "OverlayTopmostCheckBox", 0, 2);
        CheckGridCell(document, namespaces, "OverlayShowBorderCheckBox", 2, 0);
        CheckGridCell(document, namespaces, "OverlayShowLabelCheckBox", 2, 2);

        XmlElement window = document.DocumentElement;
        Check(window != null && window.GetAttribute("Width") == "920" &&
              window.GetAttribute("Height") == "800",
            "main window keeps the compact initial dimensions");
    }

    private static void CheckGridCell(
        XmlDocument document,
        XmlNamespaceManager namespaces,
        string controlName,
        int expectedRow,
        int expectedColumn) {
        XmlNode node = document.SelectSingleNode(
            "//*[@x:Name='" + controlName + "']",
            namespaces);
        Check(node != null, controlName + " exists in the layout");
        if (node == null) {
            return;
        }

        XmlNode cell = node.ParentNode != null && node.ParentNode.LocalName == "StackPanel"
            ? node.ParentNode
            : node;
        int row = ReadGridCoordinate(cell, "Grid.Row");
        int column = ReadGridCoordinate(cell, "Grid.Column");
        Check(row == expectedRow && column == expectedColumn,
            controlName + " remains aligned at row " + expectedRow + ", column " + expectedColumn);
    }

    private static int ReadGridCoordinate(XmlNode node, string attributeName) {
        XmlAttribute attribute = node.Attributes == null ? null : node.Attributes[attributeName];
        return attribute == null ? 0 : int.Parse(attribute.Value);
    }

    private static void TestDpiConfiguration() {
        string root = FindRepositoryRoot();
        string manifest = File.ReadAllText(Path.Combine(root, "DSDeaths.Live", "app.manifest"));
        string config = File.ReadAllText(Path.Combine(root, "DSDeaths.Live", "App.config"));
        Check(manifest.IndexOf("<dpiAwareness", StringComparison.Ordinal) >= 0 &&
              manifest.IndexOf("PerMonitorV2", StringComparison.Ordinal) >= 0,
            "application manifest enables Per-Monitor V2 DPI awareness");
        Check(config.IndexOf(
                  "Switch.System.Windows.DoNotScaleForDpiChanges=false",
                  StringComparison.Ordinal) >= 0,
            "WPF DPI-change scaling switch is enabled");
    }

    private static System.Collections.Generic.HashSet<string> LoadResourceKeys(string path) {
        var document = new XmlDocument();
        document.Load(path);
        var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        XmlNodeList nodes = document.SelectNodes("/root/data");
        foreach (XmlNode node in nodes) {
            keys.Add(node.Attributes["name"].Value);
        }
        return keys;
    }

    private static string FindRepositoryRoot() {
        DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "DSDeaths.sln"))) {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository root for source-file tests.");
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
