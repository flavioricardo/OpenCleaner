using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FreeCleaner.Core
{
    /// <summary>Configurações em %AppData%\FreeCleaner\FreeCleaner.ini.</summary>
    class AppSettings
    {
        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreeCleaner"); }
        }

        public static string FilePath { get { return Path.Combine(DataDir, "FreeCleaner.ini"); } }

        public Dictionary<string, bool> Rules = new Dictionary<string, bool>();
        public List<string> IncludeFolders = new List<string>();
        public List<string> ExcludeFolders = new List<string>();
        public List<string> RegistryDisabled = new List<string>();
        public bool TempOlderThan24h = true;
        public bool SecureDelete;
        public bool ConfirmClean = true;
        public bool LoadWinapp2 = true;

        public bool IsRuleOn(CleanRule r)
        {
            bool v;
            return Rules.TryGetValue(r.Id, out v) ? v : r.Default;
        }

        public void SetRule(CleanRule r, bool on)
        {
            if (on == r.Default) Rules.Remove(r.Id);
            else Rules[r.Id] = on;
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            if (!File.Exists(FilePath)) return s;
            string section = "";
            foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                int eq = section == "Rules" ? line.LastIndexOf('=') : line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                bool b = v == "1";
                switch (section)
                {
                    case "Rules": s.Rules[k] = b; break;
                    case "Include": s.IncludeFolders.Add(v); break;
                    case "Exclude": s.ExcludeFolders.Add(v); break;
                    case "Registry": if (!b) s.RegistryDisabled.Add(k); break;
                    case "Options":
                        if (k == "TempOlderThan24h") s.TempOlderThan24h = b;
                        else if (k == "SecureDelete") s.SecureDelete = b;
                        else if (k == "ConfirmClean") s.ConfirmClean = b;
                        else if (k == "LoadWinapp2") s.LoadWinapp2 = b;
                        break;
                }
            }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(DataDir);
            var sb = new StringBuilder();
            Func<bool, string> B = x => x ? "1" : "0";
            sb.AppendLine("[Options]");
            sb.AppendLine("TempOlderThan24h=" + B(TempOlderThan24h));
            sb.AppendLine("SecureDelete=" + B(SecureDelete));
            sb.AppendLine("ConfirmClean=" + B(ConfirmClean));
            sb.AppendLine("LoadWinapp2=" + B(LoadWinapp2));
            sb.AppendLine("[Rules]");
            foreach (var kv in Rules.OrderBy(k => k.Key)) sb.AppendLine(kv.Key + "=" + B(kv.Value));
            sb.AppendLine("[Include]");
            for (int i = 0; i < IncludeFolders.Count; i++) sb.AppendLine("F" + i + "=" + IncludeFolders[i]);
            sb.AppendLine("[Exclude]");
            for (int i = 0; i < ExcludeFolders.Count; i++) sb.AppendLine("F" + i + "=" + ExcludeFolders[i]);
            sb.AppendLine("[Registry]");
            foreach (var c in RegistryDisabled) sb.AppendLine(c + "=0");
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }
    }
}
