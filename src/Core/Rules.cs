using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenCleaner.Core
{
    class FileKey
    {
        public string Path;
        public string[] Patterns;
        public bool Recurse;
        public bool RemoveSelf;
    }

    class RegKeySpec
    {
        public string Key;
        public string Value; // null = apagar a chave inteira
    }

    class ExcludeSpec
    {
        public string Path;
        public string Pattern; // null = tudo dentro de Path
    }

    class CleanRule
    {
        public string Name;              // exibidos, no idioma da interface
        public string Group;
        public string KeyName, KeyGroup;  // em português: identificam a regra nas configurações
        public bool IsCustom;
        public string Category;
        public string Source = "builtin";
        public string Special;
        public string Warning;
        public bool Default;
        public bool TempAge;
        public int MinAgeHours;
        public List<string> Detect = new List<string>();
        public List<string> DetectFile = new List<string>();
        public List<string> Processes = new List<string>();
        public List<FileKey> FileKeys = new List<FileKey>();
        public List<RegKeySpec> RegKeys = new List<RegKeySpec>();
        public List<ExcludeSpec> Excludes = new List<ExcludeSpec>();

        public string Id { get { return Source + "|" + (KeyGroup ?? Group) + "|" + (KeyName ?? Name); } }
        public string FullName { get { return Group + " - " + Name; } }

        /// <summary>O programa ao qual a regra se refere está instalado?</summary>
        public bool IsDetected()
        {
            if (Detect.Count > 0 || DetectFile.Count > 0)
            {
                foreach (var k in Detect) if (RegistryKeyExists(k)) return true;
                foreach (var f in DetectFile) if (Util.PathExists(f)) return true;
                return false;
            }
            if (Special != null) return true;
            foreach (var fk in FileKeys) if (Util.ResolveDirs(fk.Path).Count > 0) return true;
            foreach (var rk in RegKeys) if (RegistryKeyExists(rk.Key)) return true;
            return false;
        }

        static bool RegistryKeyExists(string path)
        {
            string root, sub;
            if (!Util.SplitRegPath(path, out root, out sub)) return false;
            try
            {
                using (var b = Util.OpenRoot(root, Microsoft.Win32.RegistryView.Default))
                using (var k = b.OpenSubKey(sub))
                    return k != null;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Lê regras no formato winapp2.ini (o mesmo formato público que o CCleaner aceita),
    /// com algumas chaves extras usadas pelas regras embutidas.
    /// </summary>
    static class RuleParser
    {
        static readonly Dictionary<string, string> LangSec = new Dictionary<string, string>
        {
            { "3021", "Aplicativos|Applications" }, { "3022", "Internet|Internet" }, { "3023", "Multimídia|Multimedia" }, { "3024", "Utilitários|Utilities" },
            { "3025", "Windows" }, { "3026", "Firefox" }, { "3027", "Opera" }, { "3028", "Safari" },
            { "3029", "Google Chrome" }, { "3030", "Thunderbird" }, { "3031", "Windows Store" }, { "3032", "Chromium" }
        };

        static readonly Dictionary<string, string> SpecialDetect = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DET_CHROME", @"%LocalAppData%\Google\Chrome\User Data" },
            { "DET_MOZILLA", @"%AppData%\Mozilla\Firefox" },
            { "DET_THUNDERBIRD", @"%AppData%\Thunderbird" },
            { "DET_OPERA", @"%AppData%\Opera Software" },
            { "DET_EDGE", @"%LocalAppData%\Microsoft\Edge\User Data" },
            { "DET_WINSTORE", @"%LocalAppData%\Packages" }
        };

        public static List<CleanRule> Parse(string text, string source)
        {
            var rules = new List<CleanRule>();
            CleanRule cur = null;
            string section = null;
            string winapp2Section = null, langSec = null;
            string groupEn = null, itemEn = null, warningEn = null;
            Action flush = () =>
            {
                if (cur == null) return;
                string groupKey = cur.Group;
                if (groupKey == null)
                {
                    // winapp2.ini: grupo vem de Section= ou de LangSecRef (pares "português|inglês")
                    string g = winapp2Section;
                    if (g == null && langSec != null && LangSec.TryGetValue(langSec, out g))
                    {
                        var pair = g.Split('|');
                        g = pair[0];
                        if (pair.Length > 1) groupEn = groupEn ?? pair[1];
                    }
                    groupKey = g ?? "Outros";
                    if (g == null) groupEn = groupEn ?? "Other";
                }
                if (cur.Name == null) cur.Name = Regex.Replace(section, @"\s*\*\s*$", "");

                // O ID usa sempre o texto em português; a exibição segue o idioma escolhido.
                cur.KeyGroup = cur.Group = groupKey;
                cur.KeyName = cur.Name;
                if (L.English)
                {
                    if (groupEn != null) cur.Group = groupEn;
                    if (itemEn != null) cur.Name = itemEn;
                    if (warningEn != null) cur.Warning = warningEn;
                }
                if (cur.Category == null) cur.Category = source;
                if (cur.FileKeys.Count > 0 || cur.RegKeys.Count > 0 || cur.Special != null) rules.Add(cur);
            };

            foreach (var rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    flush();
                    section = line.Substring(1, line.Length - 2).Trim();
                    cur = new CleanRule { Source = source };
                    winapp2Section = null;
                    langSec = null;
                    groupEn = itemEn = warningEn = null;
                    continue;
                }
                if (cur == null) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = Regex.Replace(line.Substring(0, eq).Trim(), @"\d+$", "").ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "category": cur.Category = val; break;
                    case "group": cur.Group = val; break;
                    case "item": cur.Name = val; break;
                    case "groupen": groupEn = val; break;
                    case "itemen": itemEn = val; break;
                    case "warningen": warningEn = val; break;
                    case "section": winapp2Section = val; break;
                    case "langsecref": langSec = val; break;
                    case "default": cur.Default = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "warning": cur.Warning = val; break;
                    case "special": cur.Special = val; break;
                    case "tempage": cur.TempAge = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "minagehours": int.TryParse(val, out cur.MinAgeHours); break;
                    case "process":
                        cur.Processes.AddRange(val.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
                        break;
                    case "detect": cur.Detect.Add(val); break;
                    case "detectfile": cur.DetectFile.Add(val); break;
                    case "specialdetect":
                        string p;
                        if (SpecialDetect.TryGetValue(val, out p)) cur.DetectFile.Add(p);
                        break;
                    case "filekey":
                        var fk = ParseFileKey(val);
                        if (fk != null) cur.FileKeys.Add(fk);
                        break;
                    case "regkey":
                        var parts = val.Split('|');
                        cur.RegKeys.Add(new RegKeySpec { Key = parts[0].Trim(), Value = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : null });
                        break;
                    case "excludekey":
                        var ex = val.Split('|');
                        if (ex.Length >= 2 && !ex[0].Trim().Equals("REG", StringComparison.OrdinalIgnoreCase))
                            cur.Excludes.Add(new ExcludeSpec { Path = ex[1].Trim(), Pattern = ex.Length > 2 && ex[2].Trim().Length > 0 ? ex[2].Trim() : null });
                        break;
                }
            }
            flush();
            return rules;
        }

        static FileKey ParseFileKey(string val)
        {
            var parts = val.Split('|');
            if (parts[0].Trim().Length == 0) return null;
            var fk = new FileKey
            {
                Path = parts[0].Trim(),
                Patterns = parts.Length > 1 && parts[1].Trim().Length > 0
                    ? parts[1].Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray()
                    : new[] { "*.*" }
            };
            for (int i = 2; i < parts.Length; i++)
            {
                string flag = parts[i].Trim().ToUpperInvariant();
                if (flag == "RECURSE") fk.Recurse = true;
                else if (flag == "REMOVESELF") { fk.Recurse = true; fk.RemoveSelf = true; }
            }
            return fk;
        }
    }

    static class RuleCatalog
    {
        public const string CustomGroup = "Avançado";
        public const string CustomName = "Pastas personalizadas";

        // Navegadores baseados em Chromium compartilham a mesma estrutura de perfil.
        // {ROOT}=pasta User Data, {PROF}=pastas de perfil, {CACHE}=pastas de perfil com cache.
        const string ChromiumTemplate = @"
[{N} - Cache]
Category=Navegadores
Group={N}
Item=Cache da internet
ItemEn=Internet cache
Default=True
Process={P}
DetectFile={ROOT}
FileKey1={CACHE}\Cache|*.*|RECURSE
FileKey2={CACHE}\Code Cache|*.*|RECURSE
FileKey3={CACHE}\GPUCache|*.*|RECURSE
FileKey4={CACHE}\DawnGraphiteCache|*.*|RECURSE
FileKey5={CACHE}\DawnWebGPUCache|*.*|RECURSE
FileKey6={ROOT}\ShaderCache|*.*|RECURSE
FileKey7={ROOT}\GrShaderCache|*.*|RECURSE
FileKey8={ROOT}\GraphiteDawnCache|*.*|RECURSE

[{N} - Histórico]
Category=Navegadores
Group={N}
Item=Histórico de navegação e downloads
ItemEn=Browsing and download history
Default=False
Process={P}
DetectFile={ROOT}
Warning=Remove o histórico de páginas visitadas e de downloads deste navegador.
WarningEn=Removes the history of visited pages and downloads in this browser.
FileKey1={PROF}|History;History-journal;Visited Links;Top Sites;Top Sites-journal;Shortcuts;Shortcuts-journal;Network Action Predictor;Network Action Predictor-journal

[{N} - Cookies]
Category=Navegadores
Group={N}
Item=Cookies
Default=False
Process={P}
DetectFile={ROOT}
Warning=Você será desconectado de todos os sites neste navegador.
WarningEn=You will be signed out of every website in this browser.
FileKey1={PROF}\Network|Cookies;Cookies-journal
FileKey2={PROF}|Cookies;Cookies-journal

[{N} - Sessão]
Category=Navegadores
Group={N}
Item=Sessão (abas abertas)
ItemEn=Session (open tabs)
Default=False
Process={P}
DetectFile={ROOT}
FileKey1={PROF}\Sessions|*.*
FileKey2={PROF}|Current Session;Current Tabs;Last Session;Last Tabs

[{N} - Formulários]
Category=Navegadores
Group={N}
Item=Dados de formulários salvos
ItemEn=Saved form data
Default=False
Process={P}
DetectFile={ROOT}
Warning=Remove dados de preenchimento automático, endereços e cartões salvos neste navegador.
WarningEn=Removes autofill data, addresses and cards saved in this browser.
FileKey1={PROF}|Web Data;Web Data-journal

[{N} - Senhas]
Category=Navegadores
Group={N}
Item=Senhas salvas
ItemEn=Saved passwords
Default=False
Process={P}
DetectFile={ROOT}
Warning=As senhas salvas neste navegador serão apagadas permanentemente (exceto as sincronizadas na sua conta).
WarningEn=Passwords saved in this browser will be deleted permanently (except those synced to your account).
FileKey1={PROF}|Login Data;Login Data-journal;Login Data For Account;Login Data For Account-journal
";

        static readonly string[][] Chromium =
        {
            // nome, processo, ROOT, PROF, CACHE
            new[] { "Google Chrome", "chrome", @"%LocalAppData%\Google\Chrome\User Data", null, null },
            new[] { "Microsoft Edge", "msedge", @"%LocalAppData%\Microsoft\Edge\User Data", null, null },
            new[] { "Brave", "brave", @"%LocalAppData%\BraveSoftware\Brave-Browser\User Data", null, null },
            new[] { "Vivaldi", "vivaldi", @"%LocalAppData%\Vivaldi\User Data", null, null },
            new[] { "Opera", "opera", @"%AppData%\Opera Software\Opera Stable", @"%AppData%\Opera Software\Opera Stable", @"%LocalAppData%\Opera Software\Opera Stable" },
            new[] { "Opera GX", "opera", @"%AppData%\Opera Software\Opera GX Stable", @"%AppData%\Opera Software\Opera GX Stable", @"%LocalAppData%\Opera Software\Opera GX Stable" },
        };

        public static string Winapp2Path
        {
            get { return Path.Combine(AppSettings.DataDir, "winapp2.ini"); }
        }

        public static List<CleanRule> LoadAll(AppSettings settings)
        {
            var rules = new List<CleanRule>();

            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenCleaner.rules.builtin.ini"))
            using (var r = new StreamReader(s))
                rules.AddRange(RuleParser.Parse(r.ReadToEnd(), "builtin"));

            foreach (var b in Chromium)
            {
                string text = ChromiumTemplate.Replace("{N}", b[0]).Replace("{P}", b[1]).Replace("{ROOT}", b[2])
                    .Replace("{PROF}", b[3] ?? b[2] + @"\*").Replace("{CACHE}", b[4] ?? b[2] + @"\*");
                rules.AddRange(RuleParser.Parse(text, "builtin"));
            }

            var custom = new CleanRule
            {
                Category = "Windows", KeyGroup = CustomGroup, KeyName = CustomName, IsCustom = true, Default = false,
                Group = L.T(CustomGroup, "Advanced"), Name = L.T(CustomName, "Custom folders"),
                Warning = L.T("Todo o conteúdo das pastas configuradas em Opções será apagado.",
                              "Everything inside the folders set in Options will be deleted.")
            };
            foreach (var f in settings.IncludeFolders)
                custom.FileKeys.Add(new FileKey { Path = f, Patterns = new[] { "*.*" }, Recurse = true });
            rules.Add(custom);

            if (settings.LoadWinapp2)
            {
                foreach (var path in new[] { Winapp2Path, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "winapp2.ini") })
                {
                    if (!File.Exists(path)) continue;
                    try { rules.AddRange(RuleParser.Parse(File.ReadAllText(path), "winapp2.ini")); }
                    catch { }
                    break;
                }
            }
            return rules;
        }
    }
}
