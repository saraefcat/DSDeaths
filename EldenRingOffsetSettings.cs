using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DSDeaths {
    sealed class EldenRingOffsetSettings {
        internal const string FileName = "DSDeaths.settings.ini";

        const string EnabledKey = "EldenRingOffsetEnabled";
        const string OffsetKey = "EldenRingDeathOffset";

        internal bool Enabled { get; set; }
        internal int Offset { get; set; }

        internal int Apply(int rawDeathCount) {
            if (!Enabled) {
                return rawDeathCount;
            }

            long adjusted = (long)rawDeathCount - Offset;
            return adjusted < 0 ? 0 : (int)adjusted;
        }

        internal static EldenRingOffsetSettings Load(string path, out string warning) {
            var settings = new EldenRingOffsetSettings();
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
                        warnings.Add("Ignored malformed settings line: " + rawLine);
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();

                    if (key.Equals(EnabledKey, StringComparison.OrdinalIgnoreCase)) {
                        bool enabled;
                        if (TryParseBoolean(value, out enabled)) {
                            settings.Enabled = enabled;
                        } else {
                            warnings.Add("Ignored invalid " + EnabledKey + " value: " + value);
                        }
                    } else if (key.Equals(OffsetKey, StringComparison.OrdinalIgnoreCase)) {
                        int offset;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out offset) && offset >= 0) {
                            settings.Offset = offset;
                        } else {
                            warnings.Add("Ignored invalid " + OffsetKey + " value: " + value);
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

        internal bool TrySave(string path, out string error) {
            string temporaryPath = path + ".tmp";
            string[] lines =
            {
                "# DSDeaths runtime settings. This file is updated by the application.",
                EnabledKey + "=" + Enabled.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                OffsetKey + "=" + Offset.ToString(CultureInfo.InvariantCulture)
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
                TryDeleteTemporaryFile(temporaryPath);
                error = exception.Message;
                return false;
            } catch (UnauthorizedAccessException exception) {
                TryDeleteTemporaryFile(temporaryPath);
                error = exception.Message;
                return false;
            }
        }

        static bool TryParseBoolean(string value, out bool result) {
            if (bool.TryParse(value, out result)) {
                return true;
            }

            if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase)) {
                result = true;
                return true;
            }

            if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("off", StringComparison.OrdinalIgnoreCase)) {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        static void TryDeleteTemporaryFile(string path) {
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
