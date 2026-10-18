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

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern uint GetLongPathName(string shortPath, System.Text.StringBuilder longPath, uint size);

        /// <summary>
        /// Forma canônica de um caminho existente: resolve "." e "..", barras duplicadas, ponto final
        /// e nomes curtos 8.3 (C:\PROGRA~1). Sem isso, "%Temp%\..\..\..", "C:\Windows\\System32" ou
        /// "C:\PROGRA~1" escapariam da lista de pastas protegidas por não serem iguais como texto.
        /// </summary>
        public static string Canonical(string p)
        {
            try
            {
                // Prefixos de caminho bruto: \\?\C:\Windows e \\.\C:\Windows são C:\Windows.
                if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p.Substring(8);
                else if (p.StartsWith(@"\\?\") || p.StartsWith(@"\\.\")) p = p.Substring(4);
                string full = Path.GetFullPath(p);
                var sb = new System.Text.StringBuilder(1024);
                uint n = GetLongPathName(full, sb, (uint)sb.Capacity);
                if (n > 0 && n < sb.Capacity) full = sb.ToString();
                full = full.TrimEnd('\\');
                if (full.Length == 2 && full[1] == ':') full += "\\";
                return full;
            }
            catch { return p; }
        }

        static string NormalizeDir(string p)
        {
            p = p.TrimEnd('\\');
            if (p.Length == 2 && p[1] == ':') p += "\\";
            if (p.IndexOfAny(Wild) < 0) return Canonical(p);
            // Com curinga: resolve "." e ".." no texto; o resultado final passa por Canonical em ResolveDirs.
            p = string.Join("\\", ResolveDots(p.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)));
            if (p.Length == 2 && p[1] == ':') p += "\\";
            return p;
        }

        static IEnumerable<string> ResolveDots(string[] parts)
        {
            var stack = new List<string>();
            foreach (var part in parts)
            {
                if (part == ".") continue;
                if (part == "..") { if (stack.Count > 1) stack.RemoveAt(stack.Count - 1); continue; }
                stack.Add(part);
            }
            return stack;
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
                result.AddRange(current.Select(Canonical));
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

        // Path.GetFileName/GetDirectoryName lançam PathTooLongException acima de 260 caracteres no
        // .NET Framework, mas a enumeração devolve caminhos assim (FullName não é validado).
        public static string FileNameOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.Substring(path.LastIndexOfAny(new[] { '\\', '/' }) + 1);
        }

        public static string DirectoryOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            int i = path.LastIndexOfAny(new[] { '\\', '/' });
            if (i < 0) return "";
            return i == 2 && path[1] == ':' ? path.Substring(0, 3) : path.Substring(0, i); // C:\x -> C:\
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

        /// <summary>
        /// Exporta (via reg.exe) as chaves para um único .reg. Cada chave é "HKCU\..." ou o nome completo
        /// ("HKEY_CURRENT_USER\..."), com a visão 32/64 bits. Chaves que não existem são puladas.
        /// </summary>
        public static bool ExportRegistry(IEnumerable<KeyValuePair<string, RegistryView>> keys, string target, out string error)
        {
            error = null;
            var sb = new System.Text.StringBuilder("Windows Registry Editor Version 5.00\r\n");
            string tmp = Path.Combine(Path.GetTempPath(), "opencleaner_" + Guid.NewGuid().ToString("N") + ".reg");
            try
            {
                foreach (var k in keys.Distinct())
                {
                    if (k.Key.Contains("\"")) continue; // não monta argumentos do reg.exe com aspas vindas do registro
                    string viewArg = k.Value == RegistryView.Registry32 ? " /reg:32" : k.Value == RegistryView.Registry64 ? " /reg:64" : "";
                    var psi = new ProcessStartInfo("reg.exe", "export \"" + k.Key + "\" \"" + tmp + "\" /y" + viewArg)
                    {
                        CreateNoWindow = true, UseShellExecute = false
                    };
                    using (var proc = Process.Start(psi))
                    {
                        proc.WaitForExit(60000);
                        if (proc.ExitCode != 0 || !File.Exists(tmp)) continue;
                    }
                    string content = File.ReadAllText(tmp, System.Text.Encoding.Unicode);
                    int nl = content.IndexOf('\n');
                    sb.Append(nl >= 0 ? content.Substring(nl + 1) : content);
                    File.Delete(tmp);
                }
                File.WriteAllText(target, sb.ToString(), System.Text.Encoding.Unicode);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct SystemPowerStatus
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        /// <summary>True se o PC tem bateria (notebook, tablet); false em PC de mesa ou se não der para saber.</summary>
        public static bool HasBattery()
        {
            SystemPowerStatus s;
            return GetSystemPowerStatus(out s) && s.BatteryFlag != 128 && s.BatteryFlag != 255; // 128 = sem bateria, 255 = desconhecido
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
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string users = Path.GetDirectoryName(profile);
                if (users != null) set.Add(users.TrimEnd('\\'));
                // Pastas de conteúdo do usuário e sincronizadas na nuvem.
                foreach (var name in new[] { "Downloads", "OneDrive", "Favorites", "Links", "Contacts", "Saved Games", "Searches", "3D Objects", "Videos", "source" })
                    set.Add(Path.Combine(profile, name));
                foreach (var env in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
                {
                    string p = Environment.GetEnvironmentVariable(env);
                    if (!string.IsNullOrEmpty(p)) set.Add(p.TrimEnd('\\'));
                }
                foreach (var f in new[] { Environment.SpecialFolder.Favorites, Environment.SpecialFolder.CommonDocuments,
                                          Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu })
                {
                    string p = Environment.GetFolderPath(f);
                    if (!string.IsNullOrEmpty(p)) set.Add(p.TrimEnd('\\'));
                }
                // Os backups do registro do próprio OpenCleaner.
                set.Add(AppSettings.DataDir);
                protectedDirs = set;
            }
            string d = Canonical(dir).TrimEnd('\\');
            if (d.Length <= 2) return true; // raiz de unidade
            return protectedDirs.Contains(d);
        }

        /// <summary>
        /// Recusa alvos amplos demais para uma regra, mesmo vinda de um winapp2.ini malicioso ou mal escrito:
        /// pastas protegidas; "tudo" dentro de uma pasta logo abaixo do perfil (Downloads, OneDrive, projetos)
        /// ou logo abaixo da raiz da unidade (C:\Intel, D:\Fotos); e curingas logo abaixo de uma pasta
        /// protegida (%AppData%\* pegaria a pasta de todos os programas).
        /// </summary>
        public static bool IsTooBroad(string dir, bool recurse, bool matchAll, bool fromWildcard, bool userChosen = false)
        {
            dir = Canonical(dir);
            if ((recurse || matchAll) && IsProtectedDir(dir)) return true;
            if (!matchAll || userChosen) return false;
            string d = dir.TrimEnd('\\');
            string parent = Path.GetDirectoryName(d);
            if (parent == null) return true;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
            if (parent.TrimEnd('\\').Equals(profile, StringComparison.OrdinalIgnoreCase)) return true;
            string name = Path.GetFileName(d).ToLowerInvariant();
            if (parent.TrimEnd('\\').Length <= 2 && name != "temp" && name != "tmp") return true;
            if (recurse && fromWildcard && IsProtectedDir(parent)) return true;
            return false;
        }
    }
}
