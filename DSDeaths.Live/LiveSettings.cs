using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DSDeaths.Live {
    internal enum LiveSettingsWarningCode {
        MalformedLine,
        InvalidValue,
        ReadFailed
    }

    internal sealed class LiveSettingsWarning {
        internal LiveSettingsWarning(
            LiveSettingsWarningCode code,
            string argument0,
            string argument1) {
            Code = code;
            Argument0 = argument0;
            Argument1 = argument1;
        }

        internal LiveSettingsWarningCode Code { get; private set; }
        internal string Argument0 { get; private set; }
        internal string Argument1 { get; private set; }
    }

    public sealed class LiveSettings {
        public const string FileName = "DSDeaths.Live.settings.ini";

        public LiveSettings() {
            Language = "auto";
            MinimizeToTray = true;
            OverlayBackgroundOpacity = 70;
            OverlayTextColor = "#FFFFFF";
            OverlayFontFamily = "Segoe UI";
            OverlayFontSize = 58;
            OverlayScalePercent = 100;
            OverlayTextShadow = "soft";
            OverlayShowBorder = true;
            OverlayShowLabel = true;
            OverlayTopmost = true;
        }

        public string Language { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool OverlayVisible { get; set; }
        public int OverlayBackgroundOpacity { get; set; }
        public string OverlayTextColor { get; set; }
        public string OverlayFontFamily { get; set; }
        public int OverlayFontSize { get; set; }
        public int OverlayScalePercent { get; set; }
        public string OverlayTextShadow { get; set; }
        public bool OverlayPositionSet { get; set; }
        public double OverlayLeft { get; set; }
        public double OverlayTop { get; set; }
        public bool OverlayPositionLocked { get; set; }
        public bool OverlayShowBorder { get; set; }
        public bool OverlayShowLabel { get; set; }
        public bool OverlayTopmost { get; set; }

        public static LiveSettings Load(string path, out string warning) {
            LiveSettingsWarning[] ignored;
            return Load(path, out warning, out ignored);
        }

        internal static LiveSettings Load(
            string path,
            out string warning,
            out LiveSettingsWarning[] warningDetails) {
            var settings = new LiveSettings();
            var warnings = new List<string>();
            var details = new List<LiveSettingsWarning>();
            bool hasValidOverlayLeft = false;
            bool hasValidOverlayTop = false;
            if (!File.Exists(path)) {
                warning = null;
                warningDetails = details.ToArray();
                return settings;
            }

            try {
                foreach (string rawLine in File.ReadAllLines(path)) {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) ||
                        line.StartsWith(";", StringComparison.Ordinal)) {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0) {
                        AddWarning(
                            warnings,
                            details,
                            "Ignored malformed GUI settings line: " + rawLine,
                            LiveSettingsWarningCode.MalformedLine,
                            rawLine,
                            null);
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (key.Equals("Language", StringComparison.OrdinalIgnoreCase)) {
                        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("ja", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("en", StringComparison.OrdinalIgnoreCase)) {
                            settings.Language = value.ToLowerInvariant();
                        } else {
                            AddInvalidValueWarning(warnings, details, "Language", value);
                        }
                    } else if (key.Equals("MinimizeToTray", StringComparison.OrdinalIgnoreCase)) {
                        bool parsed;
                        if (bool.TryParse(value, out parsed)) {
                            settings.MinimizeToTray = parsed;
                        } else {
                            AddInvalidValueWarning(warnings, details, "MinimizeToTray", value);
                        }
                    } else if (key.Equals("OverlayVisible", StringComparison.OrdinalIgnoreCase)) {
                        bool parsed;
                        if (bool.TryParse(value, out parsed)) {
                            settings.OverlayVisible = parsed;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayVisible", value);
                        }
                    } else if (key.Equals("OverlayBackgroundOpacity", StringComparison.OrdinalIgnoreCase)) {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
                            parsed >= 0 && parsed <= 100) {
                            settings.OverlayBackgroundOpacity = parsed;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayBackgroundOpacity", value);
                        }
                    } else if (key.Equals("OverlayTextColor", StringComparison.OrdinalIgnoreCase)) {
                        if (IsHexColor(value)) {
                            settings.OverlayTextColor = value.ToUpperInvariant();
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayTextColor", value);
                        }
                    } else if (key.Equals("OverlayFontFamily", StringComparison.OrdinalIgnoreCase)) {
                        if (!string.IsNullOrWhiteSpace(value) && value.Length <= 100) {
                            settings.OverlayFontFamily = value;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayFontFamily", value);
                        }
                    } else if (key.Equals("OverlayFontSize", StringComparison.OrdinalIgnoreCase)) {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
                            parsed >= 24 && parsed <= 96) {
                            settings.OverlayFontSize = parsed;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayFontSize", value);
                        }
                    } else if (key.Equals("OverlayScalePercent", StringComparison.OrdinalIgnoreCase)) {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
                            parsed >= 50 && parsed <= 200) {
                            settings.OverlayScalePercent = parsed;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayScalePercent", value);
                        }
                    } else if (key.Equals("OverlayTextShadow", StringComparison.OrdinalIgnoreCase)) {
                        if (IsTextShadow(value)) {
                            settings.OverlayTextShadow = value.ToLowerInvariant();
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayTextShadow", value);
                        }
                    } else if (key.Equals("OverlayPositionSet", StringComparison.OrdinalIgnoreCase)) {
                        bool parsed;
                        if (bool.TryParse(value, out parsed)) {
                            settings.OverlayPositionSet = parsed;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayPositionSet", value);
                        }
                    } else if (key.Equals("OverlayLeft", StringComparison.OrdinalIgnoreCase)) {
                        double parsed;
                        if (TryParseCoordinate(value, out parsed)) {
                            settings.OverlayLeft = parsed;
                            hasValidOverlayLeft = true;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayLeft", value);
                        }
                    } else if (key.Equals("OverlayTop", StringComparison.OrdinalIgnoreCase)) {
                        double parsed;
                        if (TryParseCoordinate(value, out parsed)) {
                            settings.OverlayTop = parsed;
                            hasValidOverlayTop = true;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayTop", value);
                        }
                    } else if (key.Equals("OverlayPositionLocked", StringComparison.OrdinalIgnoreCase)) {
                        bool parsed;
                        if (bool.TryParse(value, out parsed)) {
                            settings.OverlayPositionLocked = parsed;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayPositionLocked", value);
                        }
                    } else if (key.Equals("OverlayShowBorder", StringComparison.OrdinalIgnoreCase)) {
                        bool parsed;
                        if (bool.TryParse(value, out parsed)) {
                            settings.OverlayShowBorder = parsed;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayShowBorder", value);
                        }
                    } else if (key.Equals("OverlayShowLabel", StringComparison.OrdinalIgnoreCase)) {
                        bool parsed;
                        if (bool.TryParse(value, out parsed)) {
                            settings.OverlayShowLabel = parsed;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayShowLabel", value);
                        }
                    } else if (key.Equals("OverlayTopmost", StringComparison.OrdinalIgnoreCase)) {
                        bool parsed;
                        if (bool.TryParse(value, out parsed)) {
                            settings.OverlayTopmost = parsed;
                        } else {
                            AddInvalidValueWarning(warnings, details, "OverlayTopmost", value);
                        }
                    }
                }
            } catch (IOException exception) {
                AddWarning(
                    warnings,
                    details,
                    "Could not read " + FileName + ": " + exception.Message,
                    LiveSettingsWarningCode.ReadFailed,
                    FileName,
                    exception.Message);
            } catch (UnauthorizedAccessException exception) {
                AddWarning(
                    warnings,
                    details,
                    "Could not read " + FileName + ": " + exception.Message,
                    LiveSettingsWarningCode.ReadFailed,
                    FileName,
                    exception.Message);
            }

            if (settings.OverlayPositionSet && (!hasValidOverlayLeft || !hasValidOverlayTop)) {
                settings.OverlayPositionSet = false;
            }

            warning = warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings.ToArray());
            warningDetails = details.ToArray();
            return settings;
        }

        public bool TrySave(string path, out string error) {
            string temporaryPath = path + ".tmp";
            string[] lines =
            {
                "# DSDeaths Live GUI settings. This file is updated by the application.",
                "Language=" + Language,
                "MinimizeToTray=" + MinimizeToTray.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                "OverlayVisible=" + OverlayVisible.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                "OverlayBackgroundOpacity=" + OverlayBackgroundOpacity.ToString(CultureInfo.InvariantCulture),
                "OverlayTextColor=" + OverlayTextColor,
                "OverlayFontFamily=" + OverlayFontFamily,
                "OverlayFontSize=" + OverlayFontSize.ToString(CultureInfo.InvariantCulture),
                "OverlayScalePercent=" + OverlayScalePercent.ToString(CultureInfo.InvariantCulture),
                "OverlayTextShadow=" + OverlayTextShadow,
                "OverlayPositionSet=" + OverlayPositionSet.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                "OverlayLeft=" + OverlayLeft.ToString("R", CultureInfo.InvariantCulture),
                "OverlayTop=" + OverlayTop.ToString("R", CultureInfo.InvariantCulture),
                "OverlayPositionLocked=" + OverlayPositionLocked.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                "OverlayShowBorder=" + OverlayShowBorder.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                "OverlayShowLabel=" + OverlayShowLabel.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                "OverlayTopmost=" + OverlayTopmost.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()
            };

            try {
                File.WriteAllLines(temporaryPath, lines);
                if (File.Exists(path)) {
                    try {
                        File.Replace(temporaryPath, path, null);
                    } catch (IOException) {
                        File.Copy(temporaryPath, path, true);
                        File.Delete(temporaryPath);
                    } catch (PlatformNotSupportedException) {
                        File.Copy(temporaryPath, path, true);
                        File.Delete(temporaryPath);
                    }
                } else {
                    File.Move(temporaryPath, path);
                }
                error = null;
                return true;
            } catch (IOException exception) {
                TryDelete(temporaryPath);
                error = exception.Message;
                return false;
            } catch (UnauthorizedAccessException exception) {
                TryDelete(temporaryPath);
                error = exception.Message;
                return false;
            }
        }

        private static void TryDelete(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            }
        }

        private static bool IsHexColor(string value) {
            if (string.IsNullOrEmpty(value) || value.Length != 7 || value[0] != '#') {
                return false;
            }

            for (int index = 1; index < value.Length; index++) {
                char character = value[index];
                bool isHexDigit = (character >= '0' && character <= '9') ||
                                  (character >= 'a' && character <= 'f') ||
                                  (character >= 'A' && character <= 'F');
                if (!isHexDigit) {
                    return false;
                }
            }
            return true;
        }

        private static bool IsTextShadow(string value) {
            return value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("soft", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("strong", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseCoordinate(string value, out double coordinate) {
            if (!double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out coordinate) ||
                double.IsNaN(coordinate) ||
                double.IsInfinity(coordinate) ||
                coordinate < -100000 ||
                coordinate > 100000) {
                coordinate = 0;
                return false;
            }

            return true;
        }

        private static void AddInvalidValueWarning(
            List<string> warnings,
            List<LiveSettingsWarning> details,
            string key,
            string value) {
            AddWarning(
                warnings,
                details,
                "Ignored invalid " + key + " value: " + value,
                LiveSettingsWarningCode.InvalidValue,
                key,
                value);
        }

        private static void AddWarning(
            List<string> warnings,
            List<LiveSettingsWarning> details,
            string message,
            LiveSettingsWarningCode code,
            string argument0,
            string argument1) {
            warnings.Add(message);
            details.Add(new LiveSettingsWarning(code, argument0, argument1));
        }
    }
}
