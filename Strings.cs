using System;

namespace GalgameMetadata.Shared
{
    /// <summary>
    /// Minimal two-language string table for the plugin UI (settings view and the
    /// manual selection window). The selected language is stored per plugin in
    /// PluginSettings.UiLanguage and applied whenever a view is created.
    /// </summary>
    public static class L
    {
        private static string _ui = "zh";

        /// <summary>"zh" or "en". Anything else falls back to Chinese.</summary>
        public static string Ui
        {
            get { return _ui; }
            set { _ui = IsEnglish(value) ? "en" : "zh"; }
        }

        public static bool English
        {
            get { return _ui == "en"; }
        }

        public static bool IsEnglish(string value)
        {
            return string.Equals(value, "en", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Returns the English text when the UI language is English, otherwise the Chinese one.</summary>
        public static string T(string zh, string en)
        {
            return _ui == "en" ? en : zh;
        }
    }
}
