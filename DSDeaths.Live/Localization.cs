using System.Globalization;
using System.Reflection;
using System.Resources;

namespace DSDeaths.Live {
    internal static class Localization {
        private static readonly ResourceManager Resources = new ResourceManager(
            "DSDeaths.Live.Resources.Strings",
            Assembly.GetExecutingAssembly());
        private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
        private static CultureInfo culture = English;

        internal static string Language { get; private set; }

        internal static void SetLanguage(string language) {
            Language = string.IsNullOrEmpty(language) ? "auto" : language;
            if (Language == "auto") {
                culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja"
                    ? CultureInfo.GetCultureInfo("ja")
                    : English;
            } else {
                culture = Language == "ja" ? CultureInfo.GetCultureInfo("ja") : English;
            }
        }

        internal static string Get(string key) {
            return Resources.GetString(key, culture) ?? Resources.GetString(key, English) ?? key;
        }

        internal static string Format(string key, params object[] arguments) {
            return string.Format(culture, Get(key), arguments);
        }
    }
}
