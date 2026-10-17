using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpenCleaner.Core
{
    static class Util
    {
        static readonly char[] Wild = { '*', '?' };

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            if (i == 0) return bytes + " B";
            return v.ToString(v >= 100 ? "0" : "0.0") + " " + units[i];
        }

        // ---- Variáveis de caminho (compatíveis com winapp2.ini) ----

        static Dictionary<string, string[]> vars;

        static Dictionary<string, string[]> Vars
        {
            get { if (vars == null) vars = BuildVars(); return vars; }
        }

        static string[] One(string s) { return string.IsNullOrEmpty(s) ? new string[0] : new[] { s.TrimEnd('\\') }; }

        static string[] Many(params string[] items)
        {
            return items.Where(s => !string.IsNullOrEmpty(s)).Select(s => s.TrimEnd('\\'))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        static Dictionary<string, string[]> BuildVars()
        {
            var d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            Func<Environment.SpecialFolder, string> sf = Environment.GetFolderPath;
            string profile = sf(Environment.SpecialFolder.UserProfile);
            string win = sf(Environment.SpecialFolder.Windows);

            d["AppData"] = One(sf(Environment.SpecialFolder.ApplicationData));
            d["LocalAppData"] = One(sf(Environment.SpecialFolder.LocalApplicationData));
            d["LocalLowAppData"] = One(Path.Combine(profile, @"AppData\LocalLow"));
            d["UserProfile"] = One(profile);
            d["WinDir"] = One(win);
            d["SystemRoot"] = d["WinDir"];
            d["SystemDrive"] = One(Path.GetPathRoot(win).TrimEnd('\\'));
            d["CommonAppData"] = One(sf(Environment.SpecialFolder.CommonApplicationData));
            d["ProgramData"] = d["CommonAppData"];
            d["ProgramFiles"] = Many(Environment.GetEnvironmentVariable("ProgramW6432"),
                                     sf(Environment.SpecialFolder.ProgramFilesX86),
                                     sf(Environment.SpecialFolder.ProgramFiles));
            d["CommonProgramFiles"] = Many(Environment.GetEnvironmentVariable("CommonProgramW6432"),
                                           sf(Environment.SpecialFolder.CommonProgramFilesX86),
                                           sf(Environment.SpecialFolder.CommonProgramFiles));
            d["Documents"] = One(sf(Environment.SpecialFolder.MyDocuments));
            d["Desktop"] = One(sf(Environment.SpecialFolder.DesktopDirectory));
            d["Pictures"] = One(sf(Environment.SpecialFolder.MyPictures));
            d["Music"] = One(sf(Environment.SpecialFolder.MyMusic));
            d["Video"] = One(sf(Environment.SpecialFolder.MyVideos));
            d["Public"] = One(Environment.GetEnvironmentVariable("PUBLIC"));
            d["Temp"] = One(Path.GetTempPath());
            d["Tmp"] = d["Temp"];
            return d;
        }

        /// <summary>Expande %Variáveis%; %ProgramFiles% pode gerar mais de um caminho.</summary>
        public static List<string> ExpandVars(string path)
        {
            var m = Regex.Match(path, @"%([^%\\]+)%");
            if (!m.Success) return new List<string> { path };
            string[] values;
            if (!Vars.TryGetValue(m.Groups[1].Value, out values))
                values = One(Environment.GetEnvironmentVariable(m.Groups[1].Value));
            var result = new List<string>();
            foreach (var v in values)
                result.AddRange(ExpandVars(path.Substring(0, m.Index) + v + path.Substring(m.Index + m.Length)));
            return result;
        }

        static string NormalizeDir(string p)
        {
            p = p.TrimEnd('\\');
            if (p.Length == 2 && p[1] == ':') p += "\\";
            return p;
        }

        /// <summary>Resolve variáveis e curingas em segmentos de pasta (ex.: User Data\*\Cache).</summary>
        public static List<string> ResolveDirs(string pattern)
        {
            var result = new List<string>();
            foreach (var raw in ExpandVars(pattern))
            {
                string full = NormalizeDir(raw);
                if (full.IndexOfAny(Wild) < 0)
                {
                    if (Directory.Exists(full)) result.Add(full);
                    continue;
                }
                string[] parts = full.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || parts[0].IndexOfAny(Wild) >= 0) continue;
                var current = new List<string> { parts[0] + "\\" };
                for (int i = 1; i < parts.Length && current.Count > 0; i++)
                {
                    var next = new List<string>();
                    foreach (var dir in current)
                    {
                        if (parts[i].IndexOfAny(Wild) >= 0)
                        {
                            try
                            {
                                foreach (var sub in Directory.EnumerateDirectories(dir, parts[i]))
                                    if (!IsReparsePoint(sub)) next.Add(sub);
                            }
                            catch { }
                        }
                        else
                        {
                            string c = Path.Combine(dir, parts[i]);
                            if (Directory.Exists(c)) next.Add(c);
                        }
                    }
                    current = next;
                }
                result.AddRange(current);
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>True se o caminho (pasta ou arquivo, com curingas) existe.</summary>
        public static bool PathExists(string pattern)
        {
            foreach (var raw in ExpandVars(pattern))
            {
                string full = NormalizeDir(raw);
                if (full.IndexOfAny(Wild) < 0)
                {
                    if (Directory.Exists(full) || File.Exists(full)) return true;
                    continue;
                }
                if (ResolveDirs(full).Count > 0) return true;
                string parent = Path.GetDirectoryName(full);
                string leaf = Path.GetFileName(full);
                if (parent == null) continue;
                foreach (var dir in ResolveDirs(parent))
                {
                    try { if (Directory.EnumerateFiles(dir, leaf).Any()) return true; }
                    catch { }
                }
            }
            return false;
        }

        public static bool IsReparsePoint(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return true; }
        }

        public static Regex WildcardToRegex(string pattern)
        {
            if (pattern == "*.*" || pattern == "*") return new Regex(".*");
            string rx = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // ---- Linhas de comando ----

        /// <summary>
        /// Extrai o executável de uma linha de comando. Retorna null quando não dá para
        /// determinar com segurança (caminho relativo, rundll32 com argumentos, etc.).
        /// </summary>
        public static string ExtractExePath(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                return end > 1 ? cmd.Substring(1, end - 1) : null;
            }
            bool absolute = (cmd.Length > 2 && cmd[1] == ':' && cmd[2] == '\\') || cmd.StartsWith(@"\\");
            if (!absolute) return null;

            string candidate = null;
            int idx = -1;
            while (true)
            {
                idx = cmd.IndexOf(' ', idx + 1);
                string part = idx < 0 ? cmd : cmd.Substring(0, idx);
                if (File.Exists(part)) return part;
                if (candidate == null && Regex.IsMatch(part, @"\.(exe|com|bat|cmd|dll|scr)$", RegexOptions.IgnoreCase))
                    candidate = part;
                if (idx < 0) break;
            }
            return candidate;
        }

        public static void SplitCommand(string cmd, out string file, out string args)
        {
            cmd = Environment.ExpandEnvironmentVariables((cmd ?? "").Trim());
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 0)
                {
                    file = cmd.Substring(1, end - 1);
                    args = cmd.Substring(end + 1).Trim();
                    return;
                }
            }
            string exe = ExtractExePath(cmd);
            if (exe != null && cmd.StartsWith(exe, StringComparison.OrdinalIgnoreCase))
            {
                file = exe;
                args = cmd.Substring(exe.Length).Trim();
                return;
            }
            int sp = cmd.IndexOf(' ');
            file = sp < 0 ? cmd : cmd.Substring(0, sp);
            args = sp < 0 ? "" : cmd.Substring(sp + 1).Trim();
        }

        // ---- Processos e sistema ----

        public static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        public static List<Process> FindProcesses(IEnumerable<string> names)
        {
            var list = new List<Process>();
            foreach (var n in names)
            {
                string name = n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n;
                try { list.AddRange(Process.GetProcessesByName(name)); }
                catch { }
            }
            return list;
        }

        public static void SendToRecycleBin(string path)
        {
            if (Directory.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }

        public static void ShowInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
        }

        // ---- Registro ----

        public static RegistryKey OpenRoot(string name, RegistryView view)
        {
            switch (name.ToUpperInvariant())
            {
                case "HKCU": case "HKEY_CURRENT_USER": return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                case "HKLM": case "HKEY_LOCAL_MACHINE": return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                case "HKCR": case "HKEY_CLASSES_ROOT": return RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
                case "HKU": case "HKEY_USERS": return RegistryKey.OpenBaseKey(RegistryHive.Users, view);
            }
            return null;
        }

        /// <summary>Divide "HKCU\Software\X" em raiz e subchave.</summary>
        public static bool SplitRegPath(string full, out string root, out string sub)
        {
            full = (full ?? "").Trim().TrimEnd('\\');
            int i = full.IndexOf('\\');
            root = i < 0 ? full : full.Substring(0, i);
            sub = i < 0 ? "" : full.Substring(i + 1);
            return OpenRoot(root, RegistryView.Default) != null;
        }

        public static string HiveShort(RegistryHive hive)
        {
            switch (hive)
            {
                case RegistryHive.CurrentUser: return "HKCU";
                case RegistryHive.LocalMachine: return "HKLM";
                case RegistryHive.ClassesRoot: return "HKCR";
                case RegistryHive.Users: return "HKU";
            }
            return hive.ToString();
        }

        public static string ReadString(RegistryKey key, string name)
        {
            try
            {
                object v = key.GetValue(name);
                return v == null ? null : v.ToString();
            }
            catch { return null; }
        }

        // ---- Pastas que nunca devem ser varridas recursivamente ----

        static HashSet<string> protectedDirs;

        public static bool IsProtectedDir(string dir)
        {
            if (protectedDirs == null)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in new[] { "WinDir", "UserProfile", "AppData", "LocalAppData", "LocalLowAppData", "CommonAppData",
                                          "ProgramFiles", "CommonProgramFiles", "Documents", "Desktop", "Pictures", "Music", "Video", "Public" })
                    foreach (var p in ExpandVars("%" + v + "%")) set.Add(p.TrimEnd('\\'));
                set.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"));
                set.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"));
                string users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                if (users != null) set.Add(users.TrimEnd('\\'));
                protectedDirs = set;
            }
            string d = dir.TrimEnd('\\');
            if (d.Length <= 2) return true; // raiz de unidade
            return protectedDirs.Contains(d);
        }
    }
}
