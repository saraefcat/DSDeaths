using System;
using System.Collections.Generic;

namespace DSDeaths.Live {
    internal static class SettingsWarningFormatter {
        internal static string Format(LiveSettingsWarning[] warnings) {
            var messages = new List<string>();
            foreach (LiveSettingsWarning warning in warnings ?? new LiveSettingsWarning[0]) {
                switch (warning.Code) {
                    case LiveSettingsWarningCode.MalformedLine:
                        messages.Add(Localization.Format("SettingsWarningMalformedLine", warning.Argument0));
                        break;
                    case LiveSettingsWarningCode.InvalidValue:
                        messages.Add(Localization.Format(
                            "SettingsWarningInvalidValue",
                            warning.Argument0,
                            warning.Argument1));
                        break;
                    case LiveSettingsWarningCode.ReadFailed:
                        messages.Add(Localization.Format(
                            "SettingsWarningReadFailed",
                            warning.Argument0,
                            warning.Argument1));
                        break;
                }
            }
            return string.Join(Environment.NewLine, messages.ToArray());
        }

        internal static string Format(OffsetSettingsWarning[] warnings) {
            var messages = new List<string>();
            foreach (OffsetSettingsWarning warning in warnings ?? new OffsetSettingsWarning[0]) {
                switch (warning.Code) {
                    case OffsetSettingsWarningCode.MalformedLine:
                        messages.Add(Localization.Format("OffsetWarningMalformedLine", warning.Argument0));
                        break;
                    case OffsetSettingsWarningCode.InvalidValue:
                        messages.Add(Localization.Format(
                            "OffsetWarningInvalidValue",
                            warning.Argument0,
                            warning.Argument1));
                        break;
                    case OffsetSettingsWarningCode.ReadFailed:
                        messages.Add(Localization.Format(
                            "SettingsWarningReadFailed",
                            warning.Argument0,
                            warning.Argument1));
                        break;
                }
            }
            return string.Join(Environment.NewLine, messages.ToArray());
        }
    }
}
