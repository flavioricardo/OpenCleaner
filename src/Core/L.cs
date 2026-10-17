using System;
using System.Globalization;

namespace OpenCleaner.Core
{
    /// <summary>
    /// Idioma da interface: português ou inglês. Os textos ficam lado a lado no código,
    /// L.T("Analisar", "Analyze"), para que nenhuma tradução se perca do original.
    /// </summary>
    static class L
    {
        public const string Auto = "auto", Pt = "pt", En = "en";

        /// <summary>true quando a interface está em inglês.</summary>
        public static bool English { get; private set; }

        public static string Code { get { return English ? En : Pt; } }

        /// <summary>"auto" segue o idioma do Windows: português se for pt-*, senão inglês.</summary>
        public static void Init(string setting)
        {
            if (setting == Pt) English = false;
            else if (setting == En) English = true;
            else English = !SystemIsPortuguese;
        }

        public static bool SystemIsPortuguese
        {
            get { return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "pt" || CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt"; }
        }

        public static string T(string pt, string en) { return English ? en : pt; }

        public static string F(string pt, string en, params object[] args) { return string.Format(English ? en : pt, args); }

        /// <summary>Nome exibido das abas de regras (as chaves internas ficam em português).</summary>
        public static string Category(string key)
        {
            switch (key)
            {
                case "Navegadores": return T("Navegadores", "Browsers");
                case "Aplicativos": return T("Aplicativos", "Applications");
                default: return key;
            }
        }
    }
}
