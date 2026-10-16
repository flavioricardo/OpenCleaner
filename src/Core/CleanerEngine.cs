using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace FreeCleaner.Core
{
    class RuleResult
    {
        public CleanRule Rule;
        public List<string> Files = new List<string>();
        public List<long> Sizes = new List<long>();
        public List<string> Dirs = new List<string>();
        public List<RegKeySpec> Regs = new List<RegKeySpec>();
        public long Bytes;
        public int SpecialItems;
        public int Removed;
        public long Freed;
        public int Failed;
        public string Note;

        public int ItemCount { get { return Files.Count + Regs.Count + SpecialItems; } }
    }

    /// <summary>Executa a análise e a limpeza de cada regra.</summary>
    class CleanerEngine
    {
        readonly AppSettings settings;
        public volatile bool Cancel;

        public CleanerEngine(AppSettings settings) { this.settings = settings; }

        class Exclusion { public string Dir; public Regex Pattern; }

        public RuleResult Analyze(CleanRule rule)
        {
            var r = new RuleResult { Rule = rule };
            int hours = rule.MinAgeHours;
            if (rule.TempAge && settings.TempOlderThan24h) hours = Math.Max(hours, 24);
            DateTime? cutoff = hours > 0 ? DateTime.Now.AddHours(-hours) : (DateTime?)null;

            var excludes = new List<Exclusion>();
            foreach (var ex in rule.Excludes)
                foreach (var d in Util.ExpandVars(ex.Path))
                    excludes.Add(new Exclusion { Dir = d.TrimEnd('\\'), Pattern = ex.Pattern == null ? null : Util.WildcardToRegex(ex.Pattern) });
            foreach (var d in settings.ExcludeFolders)
                excludes.Add(new Exclusion { Dir = Environment.ExpandEnvironmentVariables(d).TrimEnd('\\') });

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in rule.FileKeys)
            {
                if (Cancel) break;
                var patterns = fk.Patterns.Select(Util.WildcardToRegex).ToList();
                bool matchAll = fk.Patterns.Any(p => p == "*.*" || p == "*");
                foreach (var dir in Util.ResolveDirs(fk.Path))
                {
                    if ((fk.Recurse || matchAll) && Util.IsProtectedDir(dir))
                    {
                        r.Note = "Pasta protegida ignorada: " + dir;
                        continue;
                    }
                    if (IsExcluded(dir, null, excludes)) continue;
                    Collect(dir, patterns, fk.Recurse, cutoff, excludes, r, seen);
                    if (fk.RemoveSelf) CollectDirs(dir, r);
                }
            }

            foreach (var rk in rule.RegKeys)
                if (RegExists(rk)) r.Regs.Add(rk);

            if (rule.Special != null) AnalyzeSpecial(r);
            return r;
        }

        static bool IsExcluded(string path, string fileName, List<Exclusion> excludes)
        {
            foreach (var ex in excludes)
            {
                if (!path.StartsWith(ex.Dir, StringComparison.OrdinalIgnoreCase)) continue;
                if (path.Length > ex.Dir.Length && path[ex.Dir.Length] != '\\') continue;
                if (ex.Pattern == null) return true;
                if (fileName != null && ex.Pattern.IsMatch(fileName)) return true;
            }
            return false;
        }

        void Collect(string root, List<Regex> patterns, bool recurse, DateTime? cutoff,
                     List<Exclusion> excludes, RuleResult r, HashSet<string> seen)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0 && !Cancel)
            {
                string dir = stack.Pop();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        string name = Path.GetFileName(file);
                        if (!patterns.Any(p => p.IsMatch(name))) continue;
                        if (IsExcluded(file, name, excludes)) continue;
                        if (!seen.Add(file)) continue;
                        try
                        {
                            var fi = new FileInfo(file);
                            if (cutoff.HasValue && fi.LastWriteTime > cutoff.Value) continue;
                            r.Files.Add(file);
                            r.Sizes.Add(fi.Length);
                            r.Bytes += fi.Length;
                        }
                        catch { }
                    }
                    if (recurse)
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                            if (!Util.IsReparsePoint(sub) && !IsExcluded(sub, null, excludes)) stack.Push(sub);
                }
                catch { }
            }
        }

        static void CollectDirs(string root, RuleResult r)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                r.Dirs.Add(dir);
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        if (!Util.IsReparsePoint(sub)) stack.Push(sub);
                }
                catch { }
            }
        }

        public void Clean(RuleResult r)
        {
            for (int i = 0; i < r.Files.Count && !Cancel; i++)
            {
                try
                {
                    var fi = new FileInfo(r.Files[i]);
                    if (!fi.Exists) continue;
                    if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                        fi.Attributes &= ~FileAttributes.ReadOnly;
                    if (settings.SecureDelete) Overwrite(fi);
                    fi.Delete();
                    r.Removed++;
                    r.Freed += r.Sizes[i];
                }
                catch { r.Failed++; }
            }

            // Pastas mais profundas primeiro; só remove se ficaram vazias.
            foreach (var d in r.Dirs.OrderByDescending(d => d.Length))
            {
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); }
                catch { }
            }

            foreach (var rk in r.Regs)
            {
                if (RegDelete(rk)) r.Removed++;
                else r.Failed++;
            }

            if (r.Rule.Special != null) CleanSpecial(r);
        }

        static void Overwrite(FileInfo fi)
        {
            var buf = new byte[64 * 1024];
            using (var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                long left = fs.Length;
                while (left > 0)
                {
                    int n = (int)Math.Min(buf.Length, left);
                    fs.Write(buf, 0, n);
                    left -= n;
                }
                fs.Flush(true);
            }
        }

        // ---- Registro ----

        static bool RegExists(RegKeySpec rk)
        {
            string root, sub;
            if (!Util.SplitRegPath(rk.Key, out root, out sub)) return false;
            try
            {
                using (var b = Util.OpenRoot(root, RegistryView.Default))
                using (var k = b.OpenSubKey(sub))
                {
                    if (k == null) return false;
                    return rk.Value == null || k.GetValue(rk.Value) != null;
                }
            }
            catch { return false; }
        }

        static bool RegDelete(RegKeySpec rk)
        {
            string root, sub;
            if (!Util.SplitRegPath(rk.Key, out root, out sub) || sub.Length == 0) return false;
            try
            {
                using (var b = Util.OpenRoot(root, RegistryView.Default))
                {
                    if (rk.Value == null)
                    {
                        b.DeleteSubKeyTree(sub, false);
                        return true;
                    }
                    using (var k = b.OpenSubKey(sub, true))
                    {
                        if (k == null) return true;
                        k.DeleteValue(rk.Value, false);
                        return true;
                    }
                }
            }
            catch { return false; }
        }

        // ---- Itens especiais ----

        void AnalyzeSpecial(RuleResult r)
        {
            switch (r.Rule.Special)
            {
                case "RecycleBin":
                    long size, count;
                    if (QueryRecycleBin(out size, out count) && count > 0)
                    {
                        r.SpecialItems = (int)count;
                        r.Bytes += size;
                    }
                    break;
                case "DnsCache":
                    r.SpecialItems = 1;
                    break;
                case "Clipboard":
                    bool has = false;
                    RunSta(() =>
                    {
                        var d = System.Windows.Forms.Clipboard.GetDataObject();
                        has = d != null && d.GetFormats().Length > 0;
                    });
                    if (has) r.SpecialItems = 1;
                    break;
            }
        }

        void CleanSpecial(RuleResult r)
        {
            if (r.SpecialItems == 0) return;
            bool ok = false;
            switch (r.Rule.Special)
            {
                case "RecycleBin":
                    ok = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND) == 0;
                    break;
                case "DnsCache":
                    try { ok = DnsFlushResolverCache(); }
                    catch { ok = false; }
                    break;
                case "Clipboard":
                    RunSta(() => System.Windows.Forms.Clipboard.Clear());
                    ok = true;
                    break;
            }
            if (ok) { r.Removed += r.SpecialItems; r.Freed += r.Bytes - r.Sizes.Sum(); }
            else r.Failed += r.SpecialItems;
        }

        static void RunSta(Action a)
        {
            var t = new Thread(() => { try { a(); } catch { } });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(5000);
        }

        const int SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO64 { public int cbSize; public long i64Size; public long i64NumItems; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct SHQUERYRBINFO32 { public int cbSize; public long i64Size; public long i64NumItems; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
        static extern int SHQueryRecycleBin64(string root, ref SHQUERYRBINFO64 info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
        static extern int SHQueryRecycleBin32(string root, ref SHQUERYRBINFO32 info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHEmptyRecycleBinW")]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, int flags);

        [DllImport("dnsapi.dll")]
        static extern bool DnsFlushResolverCache();

        static bool QueryRecycleBin(out long size, out long count)
        {
            size = count = 0;
            try
            {
                if (IntPtr.Size == 8)
                {
                    var i = new SHQUERYRBINFO64 { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO64)) };
                    if (SHQueryRecycleBin64(null, ref i) != 0) return false;
                    size = i.i64Size; count = i.i64NumItems;
                }
                else
                {
                    var i = new SHQUERYRBINFO32 { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO32)) };
                    if (SHQueryRecycleBin32(null, ref i) != 0) return false;
                    size = i.i64Size; count = i.i64NumItems;
                }
                return true;
            }
            catch { return false; }
        }
    }
}
