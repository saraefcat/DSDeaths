using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DSDeaths.Live {
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

        public static LiveSettings Load(string path, out string warning) {
            var settings = new LiveSettings();
            var warnings = new List<string>();
            if (!File.Exists(path)) {
                warning = null;
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
                        warnings.Add("Ignored malformed GUI settings line: " + rawLine);
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
                            warnings.Add("Ignored invalid Language value: " + value);
                        }
                    } else if (key.Equals("MinimizeToTray", StringComparison.OrdinalIgnoreCase)) {
                        bool parsed;
                        if (bool.TryParse(value, out parsed)) {
                            settings.MinimizeToTray = parsed;
                        } else {
                            warnings.Add("Ignored invalid MinimizeToTray value: " + value);
                        }
                    } else if (key.Equals("OverlayVisible", StringComparison.OrdinalIgnoreCase)) {
                        bool parsed;
                        if (bool.TryParse(value, out parsed)) {
                            settings.OverlayVisible = parsed;
                        } else {
                            warnings.Add("Ignored invalid OverlayVisible value: " + value);
                        }
                    } else if (key.Equals("OverlayBackgroundOpacity", StringComparison.OrdinalIgnoreCase)) {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
                            parsed >= 0 && parsed <= 100) {
                            settings.OverlayBackgroundOpacity = parsed;
                        } else {
                            warnings.Add("Ignored invalid OverlayBackgroundOpacity value: " + value);
                        }
                    } else if (key.Equals("OverlayTextColor", StringComparison.OrdinalIgnoreCase)) {
                        if (IsHexColor(value)) {
                            settings.OverlayTextColor = value.ToUpperInvariant();
                        } else {
                            warnings.Add("Ignored invalid OverlayTextColor value: " + value);
                        }
                    } else if (key.Equals("OverlayFontFamily", StringComparison.OrdinalIgnoreCase)) {
                        if (!string.IsNullOrWhiteSpace(value) && value.Length <= 100) {
                            settings.OverlayFontFamily = value;
                        } else {
                            warnings.Add("Ignored invalid OverlayFontFamily value: " + value);
                        }
                    } else if (key.Equals("OverlayFontSize", StringComparison.OrdinalIgnoreCase)) {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
                            parsed >= 24 && parsed <= 96) {
                            settings.OverlayFontSize = parsed;
                        } else {
                            warnings.Add("Ignored invalid OverlayFontSize value: " + value);
                        }
                    } else if (key.Equals("OverlayScalePercent", StringComparison.OrdinalIgnoreCase)) {
                        int parsed;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
                            parsed >= 50 && parsed <= 200) {
                            settings.OverlayScalePercent = parsed;
                        } else {
                            warnings.Add("Ignored invalid OverlayScalePercent value: " + value);
                        }
                    } else if (key.Equals("OverlayTextShadow", StringComparison.OrdinalIgnoreCase)) {
                        if (IsTextShadow(value)) {
                            settings.OverlayTextShadow = value.ToLowerInvariant();
                        } else {
                            warnings.Add("Ignored invalid OverlayTextShadow value: " + value);
                        }
                    }
                }
            } catch (IOException exception) {
                warnings.Add("Could not read " + FileName + ": " + exception.Message);
            } catch (UnauthorizedAccessException exception) {
                warnings.Add("Could not read " + FileName + ": " + exception.Message);
            }

            warning = warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings.ToArray());
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
                "OverlayTextShadow=" + OverlayTextShadow
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
    }
}
