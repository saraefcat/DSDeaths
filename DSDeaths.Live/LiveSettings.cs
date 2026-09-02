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
        }

        public string Language { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool OverlayVisible { get; set; }

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
                "OverlayVisible=" + OverlayVisible.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()
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
    }
}
