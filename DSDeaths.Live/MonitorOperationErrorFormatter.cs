namespace DSDeaths.Live {
    internal static class MonitorOperationErrorFormatter {
        internal static string Format(MonitorOperationErrorCode code, string fallback) {
            switch (code) {
                case MonitorOperationErrorCode.EldenRingNotConnected:
                    return Localization.Get("OperationEldenRingNotConnected");
                case MonitorOperationErrorCode.ValidEldenRingCountRequired:
                    return Localization.Get("OperationValidEldenRingCountRequired");
                case MonitorOperationErrorCode.InvalidZeroBaseline:
                    return Localization.Get("OperationInvalidZeroBaseline");
                case MonitorOperationErrorCode.SettingsSaveFailed:
                    return Localization.Format("OperationSettingsSaveFailed", fallback);
                default:
                    return fallback;
            }
        }
    }
}
