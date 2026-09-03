using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DSDeaths {
    public enum OffsetSettingsWarningCode {
        MalformedLine,
        InvalidValue,
        ReadFailed
    }

    public sealed class OffsetSettingsWarning {
        internal OffsetSettingsWarning(
            OffsetSettingsWarningCode code,
            string argument0,
            string argument1) {
            Code = code;
            Argument0 = argument0;
            Argument1 = argument1;
        }

        public OffsetSettingsWarningCode Code { get; private set; }
        public string Argument0 { get; private set; }
        public string Argument1 { get; private set; }
    }

    public sealed class EldenRingOffsetSettings {
        public const string FileName = "DSDeaths.settings.ini";

        const string EnabledKey = "EldenRingOffsetEnabled";
        const string OffsetKey = "EldenRingDeathOffset";

        public bool Enabled { get; set; }
        public int Offset { get; set; }

        public int Apply(int rawDeathCount) {
            if (!Enabled) {
                return rawDeathCount;
            }

            long adjusted = (long)rawDeathCount - Offset;
            return adjusted < 0 ? 0 : (int)adjusted;
        }

        public static EldenRingOffsetSettings Load(string path, out string warning) {
            OffsetSettingsWarning[] ignored;
            return Load(path, out warning, out ignored);
        }

        public static EldenRingOffsetSettings Load(
            string path,
            out string warning,
            out OffsetSettingsWarning[] warningDetails) {
            var settings = new EldenRingOffsetSettings();
            var warnings = new List<string>();
            var details = new List<OffsetSettingsWarning>();

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
                            "Ignored malformed settings line: " + rawLine,
                            OffsetSettingsWarningCode.MalformedLine,
                            rawLine,
                            null);
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();

                    if (key.Equals(EnabledKey, StringComparison.OrdinalIgnoreCase)) {
                        bool enabled;
                        if (TryParseBoolean(value, out enabled)) {
                            settings.Enabled = enabled;
                        } else {
                            AddInvalidValueWarning(warnings, details, EnabledKey, value);
                        }
                    } else if (key.Equals(OffsetKey, StringComparison.OrdinalIgnoreCase)) {
                        int offset;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out offset) && offset >= 0) {
                            settings.Offset = offset;
                        } else {
                            AddInvalidValueWarning(warnings, details, OffsetKey, value);
                        }
                    }
                }
            } catch (IOException exception) {
                AddWarning(
                    warnings,
                    details,
                    "Could not read " + FileName + ": " + exception.Message,
                    OffsetSettingsWarningCode.ReadFailed,
                    FileName,
                    exception.Message);
            } catch (UnauthorizedAccessException exception) {
                AddWarning(
                    warnings,
                    details,
                    "Could not read " + FileName + ": " + exception.Message,
                    OffsetSettingsWarningCode.ReadFailed,
                    FileName,
                    exception.Message);
            }

            warning = warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings.ToArray());
            warningDetails = details.ToArray();
            return settings;
        }

        public bool TrySave(string path, out string error) {
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

        static void AddInvalidValueWarning(
            List<string> warnings,
            List<OffsetSettingsWarning> details,
            string key,
            string value) {
            AddWarning(
                warnings,
                details,
                "Ignored invalid " + key + " value: " + value,
                OffsetSettingsWarningCode.InvalidValue,
                key,
                value);
        }

        static void AddWarning(
            List<string> warnings,
            List<OffsetSettingsWarning> details,
            string message,
            OffsetSettingsWarningCode code,
            string argument0,
            string argument1) {
            warnings.Add(message);
            details.Add(new OffsetSettingsWarning(code, argument0, argument1));
        }
    }
}
