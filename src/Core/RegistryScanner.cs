using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace OpenCleaner.Core
{
    class RegIssue
    {
        public string Category;
        public string Problem;
        public string Data;
        public RegistryHive Hive;
        public RegistryView View;
        public string KeyPath;
        public string ValueName;   // null = apagar a chave KeyPath inteira

        public string DisplayKey
        {
            get
            {
                string s = Util.HiveShort(Hive) + "\\" + KeyPath;
                if (View == RegistryView.Registry32 && Hive == RegistryHive.LocalMachine && Environment.Is64BitOperatingSystem)
                    s += " (32 bits)";
                return s;
            }
        }
    }

    /// <summary>
    /// Procura entradas órfãs no registro. Todas as verificações são conservadoras:
    /// só aponta um problema quando o arquivo/pasta referenciado comprovadamente não existe.
    /// </summary>
    class RegistryScanner
    {
        public static readonly string[] Categories =
        {
            "DLLs compartilhadas ausentes",
            "Caminhos de aplicativos",
            "Programas desinstalados",
            "Inicialização com arquivo ausente",
            "Cache MUI",
            "Pastas de instaladores",
            "Fontes ausentes"
        };

        public Action<string> Progress;

        static IEnumerable<RegistryView> Views
        {
            get
            {
                if (Environment.Is64BitOperatingSystem) return new[] { RegistryView.Registry64, RegistryView.Registry32 };
                return new[] { RegistryView.Default };
            }
        }

        static IEnumerable<RegistryView> ViewsFor(RegistryHive hive)
        {
            return hive == RegistryHive.CurrentUser ? new[] { RegistryView.Default } : Views;
        }

        public List<RegIssue> Scan(IEnumerable<string> categories)
        {
            var issues = new List<RegIssue>();
            foreach (var c in categories)
            {
                if (Progress != null) Progress(c);
                try
                {
                    switch (Array.IndexOf(Categories, c))
                    {
                        case 0: SharedDlls(issues); break;
                        case 1: AppPaths(issues); break;
                        case 2: Uninstall(issues); break;
                        case 3: RunKeys(issues); break;
                        case 4: MuiCache(issues); break;
                        case 5: InstallerFolders(issues); break;
                        case 6: Fonts(issues); break;
                    }
                }
                catch { }
            }
            // Chaves compartilhadas entre as visões 32/64 aparecem duas vezes: mantém uma
            return issues.GroupBy(i => i.Hive + "|" + i.KeyPath + "|" + i.ValueName + "|" + i.Data)
                         .Select(g => g.First()).ToList();
        }

        static RegistryKey Open(RegistryHive hive, RegistryView view, string path)
        {
            try { return RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path); }
            catch { return null; }
        }

        static bool FileMissing(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!Path.IsPathRooted(path) || path.StartsWith(@"\\")) return false; // não verifica caminhos de rede
            try { return !File.Exists(path) && !Directory.Exists(path); }
            catch { return false; }
        }

        void SharedDlls(List<RegIssue> issues)
        {
            const string p = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
            foreach (var view in Views)
                using (var k = Open(RegistryHive.LocalMachine, view, p))
                {
                    if (k == null) continue;
                    foreach (var name in k.GetValueNames())
                        if (FileMissing(name))
                            issues.Add(new RegIssue { Category = Categories[0], Problem = "DLL compartilhada ausente", Data = name,
                                                      Hive = RegistryHive.LocalMachine, View = view, KeyPath = p, ValueName = name });
                }
        }

        void AppPaths(List<RegIssue> issues)
        {
            const string p = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (var view in ViewsFor(hive))
                    using (var k = Open(hive, view, p))
                    {
                        if (k == null) continue;
                        foreach (var sub in k.GetSubKeyNames())
                            using (var s = k.OpenSubKey(sub))
                            {
                                if (s == null) continue;
                                string exe = Util.ReadString(s, "");
                                if (exe != null && FileMissing(exe))
                                    issues.Add(new RegIssue { Category = Categories[1], Problem = "Caminho de aplicativo inválido", Data = exe,
                                                              Hive = hive, View = view, KeyPath = p + "\\" + sub });
                            }
                    }
        }

        void Uninstall(List<RegIssue> issues)
        {
            const string p = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (var view in ViewsFor(hive))
                    using (var k = Open(hive, view, p))
                    {
                        if (k == null) continue;
                        foreach (var sub in k.GetSubKeyNames())
                            using (var s = k.OpenSubKey(sub))
                            {
                                if (s == null) continue;
                                string name = Util.ReadString(s, "DisplayName");
                                string cmd = Util.ReadString(s, "UninstallString");
                                string loc = Util.ReadString(s, "InstallLocation");
                                if (string.IsNullOrEmpty(cmd)) continue;
                                string exe = Util.ExtractExePath(cmd);
                                if (exe == null) continue; // MsiExec, rundll32 etc.
                                string exeName = Path.GetFileName(exe).ToLowerInvariant();
                                if (exeName == "msiexec.exe" || exeName == "rundll32.exe") continue;
                                if (!FileMissing(exe)) continue;
                                if (!string.IsNullOrWhiteSpace(loc) && !FileMissing(loc)) continue;
                                issues.Add(new RegIssue { Category = Categories[2], Problem = "Desinstalador ausente" + (name != null ? ": " + name : ""),
                                                          Data = exe, Hive = hive, View = view, KeyPath = p + "\\" + sub });
                            }
                    }
        }

        void RunKeys(List<RegIssue> issues)
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (var view in ViewsFor(hive))
                    foreach (var p in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" })
                        using (var k = Open(hive, view, p))
                        {
                            if (k == null) continue;
                            foreach (var name in k.GetValueNames())
                            {
                                string exe = Util.ExtractExePath(Util.ReadString(k, name));
                                if (exe != null && FileMissing(exe))
                                    issues.Add(new RegIssue { Category = Categories[3], Problem = "Programa de inicialização ausente: " + name, Data = exe,
                                                              Hive = hive, View = view, KeyPath = p, ValueName = name });
                            }
                        }
        }

        void MuiCache(List<RegIssue> issues)
        {
            const string p = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
            using (var k = Open(RegistryHive.CurrentUser, RegistryView.Default, p))
            {
                if (k == null) return;
                foreach (var name in k.GetValueNames())
                {
                    if (name.StartsWith("@")) continue;
                    int i = name.LastIndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    string exe = name.Substring(0, i + 4);
                    if (FileMissing(exe))
                        issues.Add(new RegIssue { Category = Categories[4], Problem = "Referência a programa removido", Data = exe,
                                                  Hive = RegistryHive.CurrentUser, View = RegistryView.Default, KeyPath = p, ValueName = name });
                }
            }
        }

        void InstallerFolders(List<RegIssue> issues)
        {
            const string p = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders";
            using (var k = Open(RegistryHive.LocalMachine, RegistryView.Default, p))
            {
                if (k == null) return;
                foreach (var name in k.GetValueNames())
                    if (FileMissing(name))
                        issues.Add(new RegIssue { Category = Categories[5], Problem = "Pasta de instalador inexistente", Data = name,
                                                  Hive = RegistryHive.LocalMachine, View = RegistryView.Default, KeyPath = p, ValueName = name });
            }
        }

        void Fonts(List<RegIssue> issues)
        {
            const string p = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
            string sysFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string userFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts");
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                using (var k = Open(hive, RegistryView.Default, p))
                {
                    if (k == null) continue;
                    foreach (var name in k.GetValueNames())
                    {
                        string file = Util.ReadString(k, name);
                        if (string.IsNullOrWhiteSpace(file)) continue;
                        bool missing = Path.IsPathRooted(file)
                            ? FileMissing(file)
                            : !File.Exists(Path.Combine(sysFonts, file)) && !File.Exists(Path.Combine(userFonts, file));
                        if (missing)
                            issues.Add(new RegIssue { Category = Categories[6], Problem = "Fonte ausente: " + name, Data = file,
                                                      Hive = hive, View = RegistryView.Default, KeyPath = p, ValueName = name });
                    }
                }
        }

        // ---- Backup e correção ----

        /// <summary>Exporta (via reg.exe) todas as chaves afetadas para um único .reg.</summary>
        public static bool Backup(IEnumerable<RegIssue> issues, string target, out string error)
        {
            error = null;
            var keys = issues.Select(i => new { Path = Util.HiveShort(i.Hive) + "\\" + i.KeyPath, i.View })
                             .Distinct().ToList();
            var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
            string tmp = Path.Combine(Path.GetTempPath(), "opencleaner_" + Guid.NewGuid().ToString("N") + ".reg");
            try
            {
                foreach (var k in keys)
                {
                    string viewArg = k.View == RegistryView.Registry32 ? " /reg:32" : k.View == RegistryView.Registry64 ? " /reg:64" : "";
                    var psi = new ProcessStartInfo("reg.exe", "export \"" + k.Path + "\" \"" + tmp + "\" /y" + viewArg)
                    {
                        CreateNoWindow = true, UseShellExecute = false
                    };
                    using (var proc = Process.Start(psi))
                    {
                        proc.WaitForExit(60000);
                        if (proc.ExitCode != 0 || !File.Exists(tmp)) continue;
                    }
                    string content = File.ReadAllText(tmp, Encoding.Unicode);
                    int nl = content.IndexOf('\n');
                    sb.Append(nl >= 0 ? content.Substring(nl + 1) : content);
                    File.Delete(tmp);
                }
                File.WriteAllText(target, sb.ToString(), Encoding.Unicode);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Fix(RegIssue issue)
        {
            try
            {
                using (var b = RegistryKey.OpenBaseKey(issue.Hive, issue.View))
                {
                    if (issue.ValueName == null)
                    {
                        b.DeleteSubKeyTree(issue.KeyPath, false);
                        return true;
                    }
                    using (var k = b.OpenSubKey(issue.KeyPath, true))
                    {
                        if (k == null) return true;
                        k.DeleteValue(issue.ValueName, false);
                        return true;
                    }
                }
            }
            catch { return false; }
        }
    }
}
