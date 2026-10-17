using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace OpenCleaner.Core
{
    // ======================= Programas instalados =======================

    class InstalledProgram
    {
        public string Name, Publisher, Version, InstallDate, InstallLocation, UninstallString, DisplayIcon;
        public long SizeKB;
        public RegistryHive Hive;
        public RegistryView View;
        public string KeyPath;
        public bool IsMsi;
    }

    static class Programs
    {
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<InstalledProgram> List()
        {
            var list = new List<InstalledProgram>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<KeyValuePair<RegistryHive, RegistryView>>
            {
                new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry64),
                new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.LocalMachine, RegistryView.Registry32),
                new KeyValuePair<RegistryHive, RegistryView>(RegistryHive.CurrentUser, RegistryView.Default),
            };
            foreach (var src in sources)
            {
                RegistryKey root;
                try { root = RegistryKey.OpenBaseKey(src.Key, src.Value).OpenSubKey(UninstallKey); }
                catch { continue; }
                if (root == null) continue;
                using (root)
                    foreach (var sub in root.GetSubKeyNames())
                        using (var k = root.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            string name = Util.ReadString(k, "DisplayName");
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            if (Util.ReadString(k, "SystemComponent") == "1") continue;
                            if (Util.ReadString(k, "ParentKeyName") != null) continue; // atualizações
                            string uninst = Util.ReadString(k, "UninstallString");
                            if (string.IsNullOrWhiteSpace(uninst)) continue;
                            string version = Util.ReadString(k, "DisplayVersion");
                            if (!seen.Add(name + "|" + version)) continue;
                            long size = 0;
                            long.TryParse(Util.ReadString(k, "EstimatedSize"), out size);
                            list.Add(new InstalledProgram
                            {
                                Name = name.Trim(),
                                Publisher = Util.ReadString(k, "Publisher"),
                                Version = version,
                                InstallDate = FormatDate(Util.ReadString(k, "InstallDate")),
                                InstallLocation = Util.ReadString(k, "InstallLocation"),
                                UninstallString = uninst,
                                DisplayIcon = Util.ReadString(k, "DisplayIcon"),
                                SizeKB = size,
                                Hive = src.Key,
                                View = src.Value,
                                KeyPath = UninstallKey + "\\" + sub,
                                IsMsi = Util.ReadString(k, "WindowsInstaller") == "1"
                            });
                        }
            }
            return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static string FormatDate(string yyyymmdd)
        {
            if (yyyymmdd == null || yyyymmdd.Length != 8) return "";
            return yyyymmdd.Substring(6, 2) + "/" + yyyymmdd.Substring(4, 2) + "/" + yyyymmdd.Substring(0, 4);
        }

        public static Process Uninstall(InstalledProgram p)
        {
            string file, args;
            Util.SplitCommand(p.UninstallString, out file, out args);
            // MSI: "/I{GUID}" abre o modo de manutenção; "/X" desinstala direto.
            if (Path.GetFileName(file).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
                file.Equals("MsiExec", StringComparison.OrdinalIgnoreCase))
                args = System.Text.RegularExpressions.Regex.Replace(args, @"(?i)/I(\{)", "/X$1");
            return Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
        }

        public static bool DeleteEntry(InstalledProgram p)
        {
            try
            {
                using (var b = RegistryKey.OpenBaseKey(p.Hive, p.View))
                    b.DeleteSubKeyTree(p.KeyPath, false);
                return true;
            }
            catch { return false; }
        }
    }

    // ======================= Inicialização =======================

    class StartupEntry
    {
        public string Name, Command, Location, Publisher;
        public bool Enabled;
        public bool IsFile;
        public string FilePath;          // atalho na pasta Inicializar
        public RegistryHive Hive;
        public RegistryView View;
        public string KeyPath;           // chave Run
        public string ApprovedKey;       // chave StartupApproved correspondente
    }

    static class Startup
    {
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupEntry> List()
        {
            var list = new List<StartupEntry>();
            AddRun(list, RegistryHive.CurrentUser, RegistryView.Default, "Run", L.T("Usuário atual", "Current user"));
            AddRun(list, RegistryHive.LocalMachine, RegistryView.Registry64, "Run", L.T("Todos os usuários", "All users"));
            if (Environment.Is64BitOperatingSystem)
                AddRun(list, RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", L.T("Todos os usuários (32 bits)", "All users (32-bit)"));
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, L.T("Pasta Inicializar (usuário)", "Startup folder (user)"));
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, L.T("Pasta Inicializar (todos)", "Startup folder (all users)"));
            return list;
        }

        static void AddRun(List<StartupEntry> list, RegistryHive hive, RegistryView view, string approvedName, string location)
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            try
            {
                using (var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(runKey))
                {
                    if (k == null) return;
                    foreach (var name in k.GetValueNames())
                    {
                        if (name.Length == 0) continue;
                        string cmd = Util.ReadString(k, name) ?? "";
                        list.Add(new StartupEntry
                        {
                            Name = name, Command = cmd, Location = location,
                            Publisher = PublisherOf(Util.ExtractExePath(cmd)),
                            Hive = hive, View = view, KeyPath = runKey,
                            ApprovedKey = Approved + approvedName,
                            Enabled = IsApproved(hive, Approved + approvedName, name)
                        });
                    }
                }
            }
            catch { }
        }

        static void AddFolder(List<StartupEntry> list, string folder, RegistryHive hive, string location)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            foreach (var f in Directory.GetFiles(folder))
            {
                string fn = Path.GetFileName(f);
                if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(f), Command = f, Location = location,
                    Publisher = PublisherOf(f), IsFile = true, FilePath = f, Hive = hive,
                    ApprovedKey = Approved + "StartupFolder",
                    Enabled = IsApproved(hive, Approved + "StartupFolder", fn)
                });
            }
        }

        static string PublisherOf(string exe)
        {
            try
            {
                if (exe != null && File.Exists(exe) && !exe.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    return FileVersionInfo.GetVersionInfo(exe).CompanyName ?? "";
            }
            catch { }
            return "";
        }

        // Mesmo mecanismo do Gerenciador de Tarefas: valor binário em StartupApproved,
        // primeiro byte par = ativado, ímpar = desativado.
        static bool IsApproved(RegistryHive hive, string key, string name)
        {
            try
            {
                using (var k = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(key))
                {
                    var data = k == null ? null : k.GetValue(name) as byte[];
                    return data == null || data.Length == 0 || (data[0] & 1) == 0;
                }
            }
            catch { return true; }
        }

        static string ValueName(StartupEntry e) { return e.IsFile ? Path.GetFileName(e.FilePath) : e.Name; }

        public static void SetEnabled(StartupEntry e, bool enabled)
        {
            var data = new byte[12];
            data[0] = (byte)(enabled ? 2 : 3);
            if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            using (var k = RegistryKey.OpenBaseKey(e.Hive, RegistryView.Default).CreateSubKey(e.ApprovedKey))
                k.SetValue(ValueName(e), data, RegistryValueKind.Binary);
            e.Enabled = enabled;
        }

        public static void Delete(StartupEntry e)
        {
            if (e.IsFile) Util.SendToRecycleBin(e.FilePath);
            else
                using (var k = RegistryKey.OpenBaseKey(e.Hive, e.View).OpenSubKey(e.KeyPath, true))
                    if (k != null) k.DeleteValue(e.Name, false);
            try
            {
                using (var k = RegistryKey.OpenBaseKey(e.Hive, RegistryView.Default).OpenSubKey(e.ApprovedKey, true))
                    if (k != null) k.DeleteValue(ValueName(e), false);
            }
            catch { }
        }
    }

    // ======================= Analisador de disco =======================

    struct FileEntry
    {
        public string Path;
        public long Size;
        public DateTime Modified;
    }

    class DiskCategory
    {
        public string Name;
        public long Bytes;
        public long Count;
        public List<FileEntry> Files = new List<FileEntry>(); // só os maiores (TopFiles)

        public const int TopFiles = 2000;

        public void Trim()
        {
            if (Files.Count <= TopFiles) return;
            Files = Files.OrderByDescending(f => f.Size).Take(TopFiles).ToList();
        }
    }

    class DiskAnalyzer
    {
        static readonly Dictionary<string, string> ExtMap = BuildMap();
        public volatile bool Cancel;
        public long ScannedFiles;
        public string CurrentDir;

        public static readonly string[] CategoryNames =
            { L.T("Imagens", "Images"), L.T("Música", "Music"), L.T("Vídeos", "Videos"), L.T("Documentos", "Documents"), L.T("Compactados", "Archives"), L.T("E-mails", "Email"), L.T("Executáveis", "Executables"), L.T("Outros", "Other") };

        static Dictionary<string, string> BuildMap()
        {
            var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> add = (cat, exts) => { foreach (var e in exts.Split(' ')) m["." + e] = cat; };
            add(L.T("Imagens", "Images"), "jpg jpeg png gif bmp tif tiff webp heic heif raw cr2 nef arw dng psd svg ico");
            add(L.T("Música", "Music"), "mp3 wav flac aac ogg wma m4a opus aiff mid midi");
            add(L.T("Vídeos", "Videos"), "mp4 mkv avi mov wmv flv webm m4v mpg mpeg 3gp ts vob");
            add(L.T("Documentos", "Documents"), "pdf doc docx xls xlsx ppt pptx odt ods odp txt rtf csv md epub");
            add(L.T("Compactados", "Archives"), "zip rar 7z tar gz bz2 xz iso cab tgz zst");
            add(L.T("E-mails", "Email"), "pst ost eml msg mbox");
            add(L.T("Executáveis", "Executables"), "exe msi dll sys bat cmd com msix appx");
            return m;
        }

        public Dictionary<string, DiskCategory> Scan(string root)
        {
            var result = CategoryNames.ToDictionary(n => n, n => new DiskCategory { Name = n });
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0 && !Cancel)
            {
                string dir = stack.Pop();
                CurrentDir = dir;
                try
                {
                    var di = new DirectoryInfo(dir);
                    foreach (var fi in di.EnumerateFiles())
                    {
                        string cat;
                        if (!ExtMap.TryGetValue(fi.Extension, out cat)) cat = L.T("Outros", "Other");
                        var c = result[cat];
                        long len = fi.Length;
                        c.Bytes += len;
                        c.Count++;
                        c.Files.Add(new FileEntry { Path = fi.FullName, Size = len, Modified = fi.LastWriteTime });
                        if (c.Files.Count > DiskCategory.TopFiles * 10) c.Trim();
                        ScannedFiles++;
                    }
                    foreach (var sub in di.EnumerateDirectories())
                        if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(sub.FullName);
                }
                catch { }
            }
            foreach (var c in result.Values) c.Trim();
            return result;
        }
    }

    // ======================= Localizador de duplicados =======================

    class DuplicateFinder
    {
        const int CloudRecallOnOpen = 0x40000, CloudRecallOnDataAccess = 0x400000;
        public volatile bool Cancel;
        public string Status = "";
        public long MinSize = 1024;
        public bool MatchName;
        public bool SkipHidden = true;

        public List<List<FileEntry>> Find(string root)
        {
            Status = L.T("Listando arquivos...", "Listing files...");
            var bySize = new Dictionary<long, List<FileEntry>>();
            var stack = new Stack<string>();
            stack.Push(root);
            long count = 0;
            while (stack.Count > 0 && !Cancel)
            {
                string dir = stack.Pop();
                try
                {
                    var di = new DirectoryInfo(dir);
                    foreach (var fi in di.EnumerateFiles())
                    {
                        if (fi.Length < MinSize) continue;
                        if (SkipHidden && (fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        // Arquivos só na nuvem (OneDrive etc.): calcular o hash baixaria o arquivo inteiro.
                        if (((int)fi.Attributes & (CloudRecallOnOpen | CloudRecallOnDataAccess | (int)FileAttributes.Offline)) != 0) continue;
                        List<FileEntry> l;
                        if (!bySize.TryGetValue(fi.Length, out l)) bySize[fi.Length] = l = new List<FileEntry>();
                        l.Add(new FileEntry { Path = fi.FullName, Size = fi.Length, Modified = fi.LastWriteTime });
                        if (++count % 500 == 0) Status = L.T("Listando arquivos... ", "Listing files... ") + count;
                    }
                    foreach (var sub in di.EnumerateDirectories())
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (SkipHidden && (sub.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        stack.Push(sub.FullName);
                    }
                }
                catch { }
            }

            var groups = new List<List<FileEntry>>();
            var candidates = bySize.Values.Where(l => l.Count > 1).ToList();
            int done = 0;
            foreach (var sameSize in candidates)
            {
                if (Cancel) break;
                Status = L.T("Comparando conteúdo... ", "Comparing contents... ") + (++done) + "/" + candidates.Count;
                IEnumerable<List<FileEntry>> sets = new[] { sameSize };
                if (MatchName)
                    sets = sameSize.GroupBy(f => Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase)
                                   .Select(g => g.ToList()).Where(l => l.Count > 1);
                foreach (var set in sets)
                {
                    // Primeiro um hash rápido (64 KB iniciais), depois o hash completo.
                    foreach (var quick in set.GroupBy(f => Hash(f.Path, 64 * 1024)).Where(g => g.Key != null && g.Count() > 1))
                        foreach (var full in quick.GroupBy(f => f.Size <= 64 * 1024 ? quick.Key : Hash(f.Path, long.MaxValue))
                                                  .Where(g => g.Key != null && g.Count() > 1))
                            groups.Add(full.OrderBy(f => f.Modified).ToList());
                }
            }
            return groups.OrderByDescending(g => g[0].Size * (g.Count - 1)).ToList();
        }

        static string Hash(string path, long limit)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
                {
                    if (limit == long.MaxValue) return BitConverter.ToString(md5.ComputeHash(fs));
                    var buf = new byte[limit];
                    int n = fs.Read(buf, 0, buf.Length);
                    return BitConverter.ToString(md5.ComputeHash(buf, 0, n));
                }
            }
            catch { return null; }
        }
    }
}
