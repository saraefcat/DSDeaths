namespace DSDeaths.Live {
    internal static class MonitorMessageFormatter {
        internal static string Format(MonitorSnapshot snapshot) {
            return Format(
                snapshot.MessageCode,
                snapshot.Message,
                snapshot.MessageArgument0,
                snapshot.MessageArgument1);
        }

        internal static string Format(
            MonitorMessageCode messageCode,
            string fallback,
            string argument0,
            string argument1) {
            switch (messageCode) {
                case MonitorMessageCode.UnexpectedFailure:
                    return Localization.Format("MonitorDetailUnexpectedFailure", argument0);
                case MonitorMessageCode.OpenProcessFailed:
                    return Localization.Format("MonitorDetailOpenProcessFailed", argument0);
                case MonitorMessageCode.InspectModuleFailed:
                    return Localization.Format("MonitorDetailInspectModuleFailed", argument0);
                case MonitorMessageCode.ArchitectureDetectionFailed:
                    return Localization.Format("MonitorDetailArchitectureFailed", argument0);
                case MonitorMessageCode.EldenRingRequires64Bit:
                    return Localization.Get("MonitorDetailEldenRingRequires64Bit");
                case MonitorMessageCode.SignatureResolved:
                    return Localization.Format("MonitorDetailSignatureResolved", argument0, argument1);
                case MonitorMessageCode.SignatureResolutionFailed:
                    return Localization.Get("MonitorDetailSignatureResolutionFailed");
                case MonitorMessageCode.UnsupportedVariant:
                    return Localization.Get("MonitorDetailUnsupportedVariant");
                case MonitorMessageCode.NullPointer:
                    return Localization.Get("MonitorDetailNullPointer");
                case MonitorMessageCode.ReadMemoryFailed:
                    return Localization.Format("MonitorDetailReadMemoryFailed", argument0);
                default:
                    return fallback;
            }
        }
    }
}
