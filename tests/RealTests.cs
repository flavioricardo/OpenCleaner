// Testes reais do OpenCleaner: funcionais, de segurança e de desempenho.
// Compilado por tests\run-real.ps1. Regras de convivência com o PC de quem roda:
//   * arquivos: tudo acontece numa sandbox em %TEMP% que espelha os caminhos de cada regra;
//   * registro: só chaves e valores com "OpenCleanerTest" no nome, removidos no final;
//   * regras perigosas (pastas do sistema) são apenas ANALISADAS, nunca limpas;
//   * desempenho: leitura do disco e do registro reais, sem alterar nada.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenCleaner.Core;

static class RealTests
{
    static int failures, passes;
    static string sandbox;
    static readonly List<string> report = new List<string>();
    static readonly List<string> perf = new List<string>();
    const string TestKey = @"Software\OpenCleanerTest";

    static void Check(bool ok, string what)
    {
        string line = (ok ? "  [ok]    " : "  [FALHA] ") + what;
        Console.WriteLine(line);
        report.Add(line);
        if (ok) passes++; else failures++;
    }

    static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine("== " + name + " ==");
        report.Add("");
        report.Add("## " + name);
    }

    static void Perf(string what, Stopwatch sw, string extra = "")
    {
        string line = string.Format("  {0,-52} {1,9:N0} ms  {2}", what, sw.ElapsedMilliseconds, extra);
        Console.WriteLine(line);
        perf.Add(line);
    }

    static string Mb(long bytes) { return (bytes / 1048576.0).ToString("0.0") + " MB"; }

    static void Write(string path, int size = 1024, DateTime? time = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, Enumerable.Repeat((byte)'x', size).ToArray());
        if (time.HasValue) File.SetLastWriteTime(path, time.Value);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string newFile, string existing, IntPtr sec);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateSymbolicLink(string link, string target, int flags);

    static bool Junction(string link, string target)
    {
        var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
        p.WaitForExit();
        return Directory.Exists(link) && Util.IsReparsePoint(link);
    }

    [STAThread]
    static int Main(string[] args)
    {
        Hardening.Apply();
        Console.OutputEncoding = Encoding.UTF8;
        string outDir = args.Length > 0 ? args[0] : ".";
        bool drivers = args.Contains("--drivers");
        bool fullDisk = !args.Contains("--quick");
        L.Init(L.Pt);
        sandbox = Path.Combine(Path.GetTempPath(), "OCReal_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(sandbox);
        var total = Stopwatch.StartNew();
        try
        {
            DllPlanting(outDir);   // primeiro: antes de qualquer código carregar a dnsapi.dll do sistema
            RuleSemantics();
            Specials();
            SecurityEngine();
            SecurityPaths();
            RegistryReal();
            StartupReal();
            GamesReal();
            GpuReal();
            UninstallReal(outDir);
            DiskAnalyzerReal(fullDisk);
            DuplicatesReal();
            WingetReal();
            if (drivers) DriversReal();
            Robustness();
            AutoMode(outDir);
            AppStartup(outDir);
        }
        catch (Exception ex)
        {
            Check(false, "exceção não tratada nos testes: " + ex);
        }
        finally
        {
            Cleanup();
        }

        Section("Desempenho (resumo)");
        foreach (var p in perf) report.Add(p);
        report.Add("");
        report.Add(string.Format("Resultado: {0} ok, {1} falha(s), {2:0.0} s", passes, failures, total.Elapsed.TotalSeconds));
        File.WriteAllLines(Path.Combine(outDir, "real-tests-report.txt"), report, Encoding.UTF8);
        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "TUDO OK (" + passes + " verificações)" : failures + " FALHA(S) de " + (passes + failures));
        return failures;
    }

    // =====================================================================
    // 1. Cada regra embutida, executada de verdade numa cópia da sua estrutura de pastas
    // =====================================================================

    static string Mirror(string path, string root)
    {
        // %Var%\resto -> root\vars\Var\resto (com %SystemDrive%\ incluído)
        return Regex.Replace(path, @"^%([^%]+)%\\?", m => Path.Combine(root, "vars", m.Groups[1].Value) + "\\").TrimEnd('\\');
    }

    static string Concrete(string dirPattern)
    {
        return string.Join("\\", dirPattern.Split('\\').Select((s, i) => i == 0 ? s : s.Replace("*", "P1").Replace("?", "a")));
    }

    static string SampleName(string pattern, int n)
    {
        if (pattern == "*.*" || pattern == "*") return "lixo" + n + ".tmp";
        return pattern.Replace("*", "x" + n).Replace("?", "a");
    }

    static Regex DirRegex(string dirPattern)
    {
        return new Regex("^" + Regex.Escape(dirPattern).Replace(@"\*", @"[^\\]*").Replace(@"\?", @"[^\\]") + "$", RegexOptions.IgnoreCase);
    }

    /// <summary>Oráculo independente do motor: o arquivo deveria ser apagado por alguma FileKey da regra?</summary>
    static bool ShouldDelete(string file, List<FileKey> keys)
    {
        string dir = Path.GetDirectoryName(file);
        string name = Path.GetFileName(file);
        foreach (var fk in keys)
        {
            if (!fk.Patterns.Any(p => Util.WildcardToRegex(p).IsMatch(name))) continue;
            var rx = DirRegex(fk.Path.TrimEnd('\\'));
            for (string d = dir; d != null && d.Length >= sandbox.Length; d = Path.GetDirectoryName(d))
            {
                if (rx.IsMatch(d)) return true;
                if (!fk.Recurse) break;
            }
        }
        return false;
    }

    static void RuleSemantics()
    {
        Section("Limpeza: todas as regras embutidas, executadas de verdade numa sandbox");
        var settings = new AppSettings();
        var rules = RuleCatalog.LoadAll(settings).Where(r => r.Source == "builtin" && !r.IsCustom).ToList();
        string root = Path.Combine(sandbox, "rules");
        var engine = new CleanerEngine(settings);
        var old = DateTime.Now.AddDays(-30);
        int ruleOk = 0, fileRules = 0, regRules = 0;
        long totalFiles = 0;
        var sw = Stopwatch.StartNew();

        foreach (var rule in rules)
        {
            if (rule.FileKeys.Count == 0 && rule.RegKeys.Count == 0) continue;
            string rroot = Path.Combine(root, Regex.Replace(rule.KeyGroup + "_" + rule.KeyName, @"[^\w]+", "_"));
            var mirror = new CleanRule
            {
                Name = rule.Name, Group = rule.Group, KeyName = rule.KeyName, KeyGroup = rule.KeyGroup, Category = rule.Category,
                Source = "teste", TempAge = rule.TempAge, MinAgeHours = rule.MinAgeHours
            };
            foreach (var fk in rule.FileKeys)
                mirror.FileKeys.Add(new FileKey { Path = Mirror(fk.Path, rroot), Patterns = fk.Patterns, Recurse = fk.Recurse, RemoveSelf = fk.RemoveSelf });

            // Popula: alvos, alvos em subpastas, iscas com outro nome e uma pasta vizinha fora da regra
            var created = new List<string>();
            foreach (var fk in mirror.FileKeys)
            {
                string dir = Concrete(fk.Path);
                for (int i = 0; i < fk.Patterns.Length; i++)
                {
                    created.Add(Path.Combine(dir, SampleName(fk.Patterns[i], i)));
                    created.Add(Path.Combine(dir, "sub", SampleName(fk.Patterns[i], i)));
                }
                created.Add(Path.Combine(dir, "isca-nao-apagar.keep"));
                created.Add(Path.Combine(Path.GetDirectoryName(dir) ?? dir, "vizinha", SampleName(fk.Patterns[0], 9)));
            }
            foreach (var f in created.Distinct(StringComparer.OrdinalIgnoreCase)) Write(f, 512, old);

            // Registro: espelha HKCU\Software\X em HKCU\Software\OpenCleanerTest\rules\X
            foreach (var rk in rule.RegKeys)
            {
                string sub = TestKey + @"\rules\" + Regex.Replace(rk.Key, @"^HKCU\\Software\\", "", RegexOptions.IgnoreCase);
                var spec = new RegKeySpec { Key = @"HKCU\" + sub, Value = rk.Value };
                mirror.RegKeys.Add(spec);
                using (var k = Registry.CurrentUser.CreateSubKey(sub))
                {
                    k.SetValue(rk.Value ?? "item", "teste");
                    k.SetValue("OpenCleanerTestIsca", "fica");
                }
            }

            var expected = new HashSet<string>(created.Where(f => ShouldDelete(f, mirror.FileKeys)), StringComparer.OrdinalIgnoreCase);
            var res = engine.Analyze(mirror);
            var found = new HashSet<string>(res.Files, StringComparer.OrdinalIgnoreCase);
            bool analyzeOk = found.SetEquals(expected);
            engine.Clean(res);
            bool goneOk = expected.All(f => !File.Exists(f));
            var survivors = created.Distinct(StringComparer.OrdinalIgnoreCase).Where(f => !expected.Contains(f)).ToList();
            bool keptOk = survivors.All(File.Exists);
            bool regOk = true;
            foreach (var spec in mirror.RegKeys)
            {
                string sub = spec.Key.Substring(5);
                using (var k = Registry.CurrentUser.OpenSubKey(sub))
                {
                    if (spec.Value == null) regOk &= k == null;
                    else regOk &= k != null && k.GetValue(spec.Value) == null && k.GetValue("OpenCleanerTestIsca") != null;
                }
            }
            bool ok = analyzeOk && goneOk && keptOk && regOk && res.Failed == 0;
            if (ok) ruleOk++;
            else
            {
                Check(false, rule.FullName + ": análise=" + analyzeOk + " (esperado " + expected.Count + ", achou " + found.Count +
                      ") apagou=" + goneOk + " preservou=" + keptOk + " registro=" + regOk + " falhas=" + res.Failed);
                foreach (var f in expected.Except(found)) Console.WriteLine("      não achou: " + f.Substring(sandbox.Length));
                foreach (var f in found.Except(expected)) Console.WriteLine("      achou a mais: " + f.Substring(sandbox.Length));
            }
            if (mirror.FileKeys.Count > 0) fileRules++;
            if (mirror.RegKeys.Count > 0) regRules++;
            totalFiles += created.Count;
        }
        sw.Stop();
        int tested = rules.Count(r => r.FileKeys.Count > 0 || r.RegKeys.Count > 0);
        Check(ruleOk == tested, ruleOk + " de " + tested + " regras apagaram exatamente o esperado e preservaram as iscas (" +
              fileRules + " com arquivos, " + regRules + " com registro, " + totalFiles + " arquivos criados)");
        Perf("Criar, analisar e limpar " + tested + " regras na sandbox", sw);
    }

    // =====================================================================
    // 2. Itens especiais
    // =====================================================================

    static void Specials()
    {
        Section("Itens especiais (DNS, área de transferência, Lixeira)");
        var settings = new AppSettings();
        var rules = RuleCatalog.LoadAll(settings);
        var engine = new CleanerEngine(settings);

        var dns = engine.Analyze(rules.First(r => r.Special == "DnsCache"));
        engine.Clean(dns);
        Check(dns.Removed == 1 && dns.Failed == 0, "cache de DNS limpo pela API DnsFlushResolverCache");

        // Área de transferência: guarda o texto atual, testa e devolve.
        string saved = null;
        RunSta(() => { if (Clipboard.ContainsText()) saved = Clipboard.GetText(); });
        RunSta(() => Clipboard.SetText("OpenCleanerTest " + Guid.NewGuid()));
        var clip = engine.Analyze(rules.First(r => r.Special == "Clipboard"));
        engine.Clean(clip);
        bool empty = false;
        RunSta(() => { var d = Clipboard.GetDataObject(); empty = d == null || d.GetFormats().Length == 0; });
        Check(clip.SpecialItems == 1 && empty, "área de transferência detectada e esvaziada");
        if (saved != null)
        {
            RunSta(() => Clipboard.SetText(saved));
            string back = null;
            RunSta(() => back = Clipboard.GetText());
            Check(back == saved, "texto que estava na área de transferência foi devolvido");
        }

        var bin = engine.Analyze(rules.First(r => r.Special == "RecycleBin"));
        Check(bin.Failed == 0, "Lixeira consultada (" + bin.SpecialItems + " itens, " + Mb(bin.Bytes) + "); NÃO esvaziada no teste");
    }

    static void RunSta(Action a)
    {
        var t = new Thread(() => { try { a(); } catch { } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(5000);
    }

    // =====================================================================
    // 3. Segurança do motor de limpeza
    // =====================================================================

    static CleanRule Rule(string fileKeys, string source = "teste")
    {
        return RuleParser.Parse("[Teste]\nCategory=Teste\nGroup=Teste\nItem=Teste\n" + fileKeys, source)[0];
    }

    static void SecurityEngine()
    {
        Section("Segurança: junções, troca de pasta durante a limpeza, links e arquivos travados");
        var settings = new AppSettings();
        var engine = new CleanerEngine(settings);
        string r = Path.Combine(sandbox, "sec");
        string precious = Path.Combine(sandbox, "precioso");
        Write(Path.Combine(precious, "a.tmp"));
        Write(Path.Combine(precious, "documento.tmp"));

        // Junção dentro da pasta limpa não é seguida
        Write(Path.Combine(r, "j1", "lixo.tmp"));
        Junction(Path.Combine(r, "j1", "atalho"), precious);
        var res = engine.Analyze(Rule("FileKey1=" + Path.Combine(r, "j1") + "|*.*|RECURSE"));
        engine.Clean(res);
        Check(File.Exists(Path.Combine(precious, "a.tmp")) && !File.Exists(Path.Combine(r, "j1", "lixo.tmp")),
              "junção dentro da pasta não é seguida: o destino fica intacto");

        // TOCTOU: depois da análise, a subpasta vira junção para a pasta preciosa
        string t = Path.Combine(r, "toctou");
        Write(Path.Combine(t, "sub", "a.tmp"));
        res = engine.Analyze(Rule("FileKey1=" + t + "|*.*|RECURSE"));
        bool analyzed = res.Files.Any(f => f.EndsWith(@"sub\a.tmp", StringComparison.OrdinalIgnoreCase));
        Directory.Move(Path.Combine(t, "sub"), Path.Combine(t, "sub_antiga"));
        bool swapped = Junction(Path.Combine(t, "sub"), precious);
        engine.Clean(res);
        Check(analyzed && swapped && File.Exists(Path.Combine(precious, "a.tmp")) && res.Skipped >= 1,
              "troca de subpasta por junção entre análise e limpeza é detectada e o alvo não é apagado (ignorados: " + res.Skipped + ")");

        // Link simbólico de arquivo: apaga o link, não o alvo (exige modo desenvolvedor ou administrador)
        string s = Path.Combine(r, "sym");
        Directory.CreateDirectory(s);
        bool symOk = CreateSymbolicLink(Path.Combine(s, "link.tmp"), Path.Combine(precious, "documento.tmp"), 2);
        if (symOk)
        {
            res = engine.Analyze(Rule("FileKey1=" + s + "|*.*"));
            engine.Clean(res);
            Check(!File.Exists(Path.Combine(s, "link.tmp")) && File.Exists(Path.Combine(precious, "documento.tmp")),
                  "link simbólico de arquivo: some o link, o arquivo original fica");
        }
        else Console.WriteLine("  (link simbólico de arquivo não testado: o Windows exige modo desenvolvedor ou administrador)");

        // Link físico + exclusão segura: não pode zerar o conteúdo do original
        string h = Path.Combine(r, "hard");
        Directory.CreateDirectory(h);
        File.WriteAllText(Path.Combine(precious, "dados.dat"), "conteúdo original que não pode virar zeros");
        bool hl = CreateHardLink(Path.Combine(h, "link.tmp"), Path.Combine(precious, "dados.dat"), IntPtr.Zero);
        var secure = new CleanerEngine(new AppSettings { SecureDelete = true });
        res = secure.Analyze(Rule("FileKey1=" + h + "|*.*"));
        secure.Clean(res);
        Check(hl && File.ReadAllText(Path.Combine(precious, "dados.dat")) == "conteúdo original que não pode virar zeros" &&
              !File.Exists(Path.Combine(h, "link.tmp")),
              "exclusão segura em link físico não sobrescreve o arquivo original");

        // Exclusão segura de verdade num arquivo comum
        string sd = Path.Combine(r, "seguro", "segredo.tmp");
        Write(sd, 200000);
        res = secure.Analyze(Rule("FileKey1=" + Path.GetDirectoryName(sd) + "|*.*"));
        secure.Clean(res);
        Check(!File.Exists(sd) && res.Removed == 1, "exclusão segura apaga arquivo comum de 200 KB");

        // Arquivo travado, somente leitura, oculto e de sistema
        string a = Path.Combine(r, "attrs");
        Write(Path.Combine(a, "travado.tmp"));
        Write(Path.Combine(a, "leitura.tmp"));
        Write(Path.Combine(a, "oculto.tmp"));
        Write(Path.Combine(a, "sistema.tmp"));
        File.SetAttributes(Path.Combine(a, "leitura.tmp"), FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(a, "oculto.tmp"), FileAttributes.Hidden);
        File.SetAttributes(Path.Combine(a, "sistema.tmp"), FileAttributes.System | FileAttributes.Hidden);
        using (new FileStream(Path.Combine(a, "travado.tmp"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            res = engine.Analyze(Rule("FileKey1=" + a + "|*.*"));
            engine.Clean(res);
        }
        Check(res.Removed == 3 && res.Failed == 1 && File.Exists(Path.Combine(a, "travado.tmp")),
              "arquivo em uso é pulado sem erro; somente leitura, oculto e de sistema são apagados");

        // Nomes com acentos, emoji, espaços, ponto inicial e sem extensão
        string u = Path.Combine(r, "unicode");
        var names = new[] { "relatório ação.tmp", "日本語.tmp", "emoji 😀.tmp", ".oculto", "sem extensão", "colchetes [1] (2).tmp" };
        foreach (var n in names) Write(Path.Combine(u, n));
        res = engine.Analyze(Rule("FileKey1=" + u + "|*.*"));
        engine.Clean(res);
        Check(names.All(n => !File.Exists(Path.Combine(u, n))), "nomes com acentos, japonês, emoji, espaços e sem extensão são apagados");

        // Caminho com mais de 260 caracteres
        string deep = Path.Combine(r, "longo");
        string longDir = deep + "\\" + string.Join("\\", Enumerable.Repeat(new string('d', 60), 5));
        bool longCreated = false;
        try
        {
            var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c mkdir \"\\\\?\\" + longDir + "\" && echo x> \"\\\\?\\" + longDir + "\\arquivo.tmp\"")
            { CreateNoWindow = true, UseShellExecute = false });
            p.WaitForExit();
            longCreated = true;
        }
        catch { }
        Exception crash = null;
        try { res = engine.Analyze(Rule("FileKey1=" + deep + "|*.*|RECURSE")); engine.Clean(res); }
        catch (Exception ex) { crash = ex; }
        Check(longCreated && crash == null, "caminho com mais de 260 caracteres não derruba a análise nem a limpeza (" +
              (res.Files.Count > 0 ? "limpou" : "ignorado pelo .NET Framework") + ")");

        // Exclusões das Opções
        string ex1 = Path.Combine(r, "excl");
        Write(Path.Combine(ex1, "manter", "a.tmp"));
        Write(Path.Combine(ex1, "apagar", "b.tmp"));
        var exEngine = new CleanerEngine(new AppSettings { ExcludeFolders = new List<string> { Path.Combine(ex1, "manter") } });
        res = exEngine.Analyze(Rule("FileKey1=" + ex1 + "|*.*|RECURSE"));
        exEngine.Clean(res);
        Check(File.Exists(Path.Combine(ex1, "manter", "a.tmp")) && !File.Exists(Path.Combine(ex1, "apagar", "b.tmp")),
              "pasta em Opções > Nunca apagar é preservada");

        // Volume grande: 30 mil arquivos
        string big = Path.Combine(r, "volume");
        var sw = Stopwatch.StartNew();
        for (int d = 0; d < 300; d++)
        {
            string dd = Path.Combine(big, "d" + d);
            Directory.CreateDirectory(dd);
            for (int f = 0; f < 100; f++) File.WriteAllBytes(Path.Combine(dd, f + ".tmp"), new byte[100]);
        }
        Perf("Criar 30.000 arquivos (preparação)", sw);
        sw.Restart();
        res = engine.Analyze(Rule("FileKey1=" + big + "|*.*|RECURSE"));
        Perf("Analisar 30.000 arquivos", sw, (res.Files.Count / Math.Max(1, sw.Elapsed.TotalSeconds)).ToString("N0") + " arquivos/s");
        sw.Restart();
        engine.Clean(res);
        Perf("Apagar 30.000 arquivos", sw, (res.Removed / Math.Max(1, sw.Elapsed.TotalSeconds)).ToString("N0") + " arquivos/s");
        Check(res.Files.Count == 30000 && res.Removed == 30000, "30.000 arquivos analisados e apagados");
    }

    static void SecurityPaths()
    {
        Section("Segurança: regras maliciosas ou amplas demais (só análise, nada é apagado)");
        var engine = new CleanerEngine(new AppSettings());
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var evil = new[]
        {
            @"%WinDir%|*.*|RECURSE", @"%UserProfile%|*.*|RECURSE", @"%UserProfile%\*|*.*|RECURSE", @"%UserProfile%\*|*.*",
            @"%AppData%\*|*.*|RECURSE", @"%LocalAppData%\*|*.*|RECURSE", @"%CommonAppData%\*|*.*|RECURSE",
            @"%SystemDrive%\|*.*|RECURSE", @"%SystemDrive%\*|*.*|RECURSE", @"%ProgramFiles%|*.*|RECURSE",
            @"%ProgramFiles%\*|*.*|RECURSE", @"%Documents%|*.*", @"%Desktop%|*.*|RECURSE", @"%UserProfile%\Downloads|*.*|RECURSE",
            @"%UserProfile%\OneDrive|*.*|RECURSE", @"%AppData%\OpenCleaner|*.*|RECURSE",
            @"%Temp%\..\..\..|*.*|RECURSE", @"%Temp%\..\..|*.*|RECURSE", @"%WinDir%\System32\..|*.*|RECURSE",
            win + @"\\System32|*.*|RECURSE", win + @"\.|*.*|RECURSE", win + ".|*.*|RECURSE",
            @"C:\PROGRA~1|*.*|RECURSE", @"C:\Users|*.*|RECURSE", @"\\?\C:\Windows|*.*|RECURSE", @"C:\Windows\System32\drivers\..\..|*.*|RECURSE"
        };
        int blocked = 0;
        foreach (var fk in evil)
        {
            CleanRule rule;
            try { rule = Rule("FileKey1=" + fk, "winapp2.ini"); }
            catch { blocked++; continue; }
            var sw = Stopwatch.StartNew();
            var res = engine.Analyze(rule);
            // C:\Temp é aceito de propósito em regras da raiz; qualquer outro arquivo achado é falha
            var bad = res.Files.Where(f => !f.StartsWith(@"C:\Temp\", StringComparison.OrdinalIgnoreCase) &&
                                           !f.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)).Take(3).ToList();
            if (bad.Count == 0) blocked++;
            else Check(false, "regra \"" + fk + "\" alcançaria arquivos como " + string.Join(", ", bad));
        }
        Check(blocked == evil.Length, blocked + " de " + evil.Length + " regras perigosas (pastas do sistema, perfil, Downloads, OneDrive, \"..\", 8.3, barras duplicadas) recusadas");

        // Pasta personalizada apontando para Documentos
        var settings = new AppSettings { IncludeFolders = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) } };
        var custom = RuleCatalog.LoadAll(settings).First(x => x.IsCustom);
        var cr = new CleanerEngine(settings).Analyze(custom);
        Check(cr.Files.Count == 0 && cr.Note != null, "pasta personalizada apontando para Documentos é recusada");

        // A pasta Temp continua sendo limpa (a proteção não pode bloquear o uso normal)
        var temp = RuleCatalog.LoadAll(new AppSettings { TempOlderThan24h = false }).First(x => x.KeyName == "Arquivos temporários");
        var tr = engine.Analyze(temp);
        Check(tr.Note == null, "regra de Temp do usuário segue liberada (" + tr.Files.Count + " arquivos achados, nada apagado)");
    }

    // =====================================================================
    // 4. DLL plantada ao lado do executável
    // =====================================================================

    static void DllPlanting(string outDir)
    {
        Section("Segurança: DLL plantada ao lado do executável");
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string fake = Path.Combine(exeDir, "dnsapi.dll");
        File.WriteAllBytes(fake, Encoding.ASCII.GetBytes("MZ isto não é uma DLL de verdade"));
        try
        {
            var engine = new CleanerEngine(new AppSettings());
            var res = engine.Analyze(RuleCatalog.LoadAll(new AppSettings()).First(r => r.Special == "DnsCache"));
            engine.Clean(res);
            string loaded = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Where(m => m.ModuleName.Equals("dnsapi.dll", StringComparison.OrdinalIgnoreCase)).Select(m => m.FileName).FirstOrDefault();
            Check(res.Removed == 1 && loaded != null && loaded.StartsWith(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase),
                  "dnsapi.dll falsa ao lado do exe é ignorada; carregada a do sistema: " + loaded);
        }
        finally { try { File.Delete(fake); } catch { } }
    }

    // =====================================================================
    // 5. Registro: entradas órfãs reais no HKCU, backup, correção e restauração
    // =====================================================================

    static void RegistryReal()
    {
        Section("Registro: entradas órfãs de teste no HKCU, backup .reg, correção e restauração");
        string missing = @"C:\OpenCleanerTest\nao-existe.exe";
        string exists = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        const string mui = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
        const string appPaths = @"Software\Microsoft\Windows\CurrentVersion\App Paths";
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string uninst = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        const string fonts = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

        using (var k = Registry.CurrentUser.CreateSubKey(mui))
        {
            k.SetValue(missing + ".FriendlyAppName", "OpenCleanerTest");
            k.SetValue(exists + ".OpenCleanerTestFriendly", "existe");
        }
        using (var k = Registry.CurrentUser.CreateSubKey(appPaths + @"\OpenCleanerTest.exe")) k.SetValue("", missing);
        using (var k = Registry.CurrentUser.CreateSubKey(appPaths + @"\OpenCleanerTestOk.exe")) k.SetValue("", exists);
        using (var k = Registry.CurrentUser.CreateSubKey(run))
        {
            k.SetValue("OpenCleanerTestOrfao", "\"" + missing + "\" --minimized");
            k.SetValue("OpenCleanerTestRede", @"\\servidor\share\app.exe");
            k.SetValue("OpenCleanerTestRelativo", "app.exe --x");
        }
        using (var k = Registry.CurrentUser.CreateSubKey(uninst + @"\OpenCleanerTestOrfao"))
        {
            k.SetValue("DisplayName", "OpenCleaner Test Órfão");
            k.SetValue("UninstallString", "\"" + missing + "\" /uninstall");
        }
        using (var k = Registry.CurrentUser.CreateSubKey(fonts)) k.SetValue("OpenCleanerTest (TrueType)", @"C:\OpenCleanerTest\fonte.ttf");

        var sw = Stopwatch.StartNew();
        var issues = new RegistryScanner().Scan(RegistryScanner.Categories);
        Perf("Varrer o registro (7 categorias)", sw, issues.Count + " problemas");
        var ours = issues.Where(i => (i.Data ?? "").Contains("OpenCleanerTest") || (i.KeyPath ?? "").Contains("OpenCleanerTest") ||
                                     (i.ValueName ?? "").Contains("OpenCleanerTest")).ToList();
        Check(ours.Count == 5, "as 5 entradas órfãs de teste foram encontradas (achou " + ours.Count + ")");
        Check(!issues.Any(i => (i.Data ?? "").Equals(exists, StringComparison.OrdinalIgnoreCase)), "entradas que apontam para arquivos existentes não são acusadas");
        Check(!ours.Any(i => (i.ValueName ?? "") == "OpenCleanerTestRede" || (i.ValueName ?? "") == "OpenCleanerTestRelativo"),
              "caminhos de rede e relativos não são acusados");

        string backup = Path.Combine(sandbox, "backup.reg");
        string error;
        Check(RegistryScanner.Backup(ours, backup, out error) && File.Exists(backup) && File.ReadAllText(backup, Encoding.Unicode).Contains("OpenCleanerTest"),
              "backup .reg das entradas gerado");
        int fixedCount = ours.Count(RegistryScanner.Fix);
        var after = new RegistryScanner().Scan(RegistryScanner.Categories)
            .Where(i => (i.Data ?? "").Contains("OpenCleanerTest") || (i.KeyPath ?? "").Contains("OpenCleanerTest") || (i.ValueName ?? "").Contains("OpenCleanerTest")).ToList();
        Check(fixedCount == 5 && after.Count == 0, "as 5 entradas foram corrigidas");
        using (var k = Registry.CurrentUser.OpenSubKey(mui))
            Check(k.GetValue(exists + ".OpenCleanerTestFriendly") != null, "vizinha válida na mesma chave continua lá");

        var p = Process.Start(new ProcessStartInfo("reg.exe", "import \"" + backup + "\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true });
        p.WaitForExit();
        var restored = new RegistryScanner().Scan(RegistryScanner.Categories)
            .Count(i => (i.Data ?? "").Contains("OpenCleanerTest") || (i.KeyPath ?? "").Contains("OpenCleanerTest") || (i.ValueName ?? "").Contains("OpenCleanerTest"));
        Check(p.ExitCode == 0 && restored == 5, "reg import do backup restaurou as 5 entradas");
    }

    // =====================================================================
    // 6. Inicialização
    // =====================================================================

    static void StartupReal()
    {
        Section("Inicialização: ativar, desativar e excluir uma entrada de teste");
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        using (var k = Registry.CurrentUser.CreateSubKey(run))
            k.SetValue("OpenCleanerTestStartup", "\"" + Path.Combine(Environment.SystemDirectory, "notepad.exe") + "\"");
        var sw = Stopwatch.StartNew();
        var e = Startup.List().FirstOrDefault(x => x.Name == "OpenCleanerTestStartup");
        Perf("Listar a inicialização", sw);
        Check(e != null && e.Enabled && e.Publisher.Contains("Microsoft"), "entrada listada como ativa, com o editor lido do executável");
        Startup.SetEnabled(e, false);
        byte[] v;
        using (var k = Registry.CurrentUser.OpenSubKey(approved)) v = k.GetValue("OpenCleanerTestStartup") as byte[];
        var again = Startup.List().First(x => x.Name == "OpenCleanerTestStartup");
        Check(v != null && v.Length == 12 && v[0] == 3 && !again.Enabled, "desativar grava o mesmo valor do Gerenciador de Tarefas (03 + data)");
        Startup.SetEnabled(again, true);
        Check(Startup.List().First(x => x.Name == "OpenCleanerTestStartup").Enabled, "reativar funciona");
        Startup.Delete(Startup.List().First(x => x.Name == "OpenCleanerTestStartup"));
        using (var k = Registry.CurrentUser.OpenSubKey(run)) Check(k.GetValue("OpenCleanerTestStartup") == null, "excluir remove a entrada");
        using (var k = Registry.CurrentUser.OpenSubKey(approved)) Check(k == null || k.GetValue("OpenCleanerTestStartup") == null, "e o valor de aprovação");
    }

    // =====================================================================
    // 6b. Jogos: ajustes de otimizadores (registro numa sandbox em HKCU\Software\OpenCleanerTest)
    // =====================================================================

    static void Dword(RegistryKey root, string key, string name, int value)
    {
        using (var k = root.CreateSubKey(key)) k.SetValue(name, value, RegistryValueKind.DWord);
    }

    static object Value(RegistryKey root, string key, string name)
    {
        using (var k = root.OpenSubKey(key)) return k == null ? null : k.GetValue(name);
    }

    static void GamesReal()
    {
        Section("Jogos: ajustes deixados por otimizadores, numa sandbox do registro");
        const string gameBar = @"Software\Microsoft\GameBar";
        const string dvr = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
        const string bg = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
        const string schemes = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
        const string nich = "243ed252-7df6-4cfa-90ad-c9a5f636f763";

        using (var hklm = Registry.CurrentUser.CreateSubKey(TestKey + @"\Games\HKLM"))
        using (var hkcu = Registry.CurrentUser.CreateSubKey(TestKey + @"\Games\HKCU"))
        {
            string activated = null;
            var ctx = new GameContext { Hklm = hklm, Hkcu = hkcu, HasBattery = true, SetActivePowerScheme = g => { activated = g; return true; } };
            Check(GameScanner.Scan(ctx).Count == 0, "registro vazio (padrão do Windows): nada é apontado");

            // Valores no padrão, ou com o tipo errado, não são apontados.
            Dword(hkcu, gameBar, "AutoGameModeEnabled", 1);
            using (var k = hklm.CreateSubKey(dvr)) k.SetValue("AllowGameDVR", "0", RegistryValueKind.String);
            Dword(hkcu, bg, "GlobalUserDisabled", 0);
            for (int i = 0; i < 12; i++)
                if (i < 5) { Dword(hkcu, bg + @"\App" + i, "Disabled", 1); Dword(hkcu, bg + @"\App" + i, "DisabledByUser", 1); }
                else hkcu.CreateSubKey(bg + @"\App" + i).Dispose();
            using (var k = hklm.CreateSubKey(schemes))
            {
                k.SetValue("ActivePowerScheme", "11111111-2222-3333-4444-555555555555");
                using (var s = k.CreateSubKey("11111111-2222-3333-4444-555555555555"))
                {
                    s.SetValue("FriendlyName", "Lenovo Balanced");
                    s.SetValue("Description", "Plano do fabricante");
                }
            }
            var none = GameScanner.Scan(ctx);
            Check(none.Count == 0, "Modo de Jogo ligado, política com tipo errado, 5 de 12 apps desligados à mão e plano de fabricante não são apontados" +
                                   (none.Count > 0 ? " (achou " + string.Join(", ", none.Select(f => f.Id)) + ")" : ""));

            // O que o Nich FPS+ grava.
            Dword(hkcu, gameBar, "AutoGameModeEnabled", 0);
            Dword(hkcu, gameBar, "AllowAutoGameMode", 0);
            Dword(hkcu, gameBar, "UseNexusForGameBarEnabled", 0);
            Dword(hklm, dvr, "AllowGameDVR", 0);
            Dword(hklm, dvr, "OpenCleanerTestVizinho", 7);
            Dword(hkcu, bg, "GlobalUserDisabled", 1);
            for (int i = 0; i < 11; i++) { Dword(hkcu, bg + @"\App" + i, "Disabled", 1); Dword(hkcu, bg + @"\App" + i, "DisabledByUser", 1); }
            using (var k = hklm.CreateSubKey(schemes))
            {
                k.SetValue("ActivePowerScheme", nich);
                using (var s = k.CreateSubKey(nich))
                {
                    s.SetValue("FriendlyName", "Nich FPS+");
                    s.SetValue("Description", @"@%SystemRoot%\system32\powrprof.dll,-18,Provides ultimate performance on higher end PCs.");
                }
            }

            var sw = Stopwatch.StartNew();
            var found = GameScanner.Scan(ctx);
            Perf("Verificar ajustes de jogos (sandbox)", sw);
            var ids = found.Select(f => f.Id).ToList();
            string[] expected = { "game-mode-off", "gamedvr-policy", "background-apps", "background-apps-bulk", "power-plan-copy" };
            Check(expected.All(ids.Contains) && ids.Count == expected.Length, "os 5 ajustes do Nich FPS+ foram encontrados (achou " + string.Join(", ", ids) + ")");
            var bulk = found.FirstOrDefault(f => f.Id == "background-apps-bulk");
            Check(bulk != null && !bulk.DefaultChecked && bulk.Edits.Count == 22, "apps desligados um por um: 11 de 12, vem desmarcado e mexe só nesses 11");
            Check(found.Where(f => f.Id == "gamedvr-policy").All(f => f.NeedsAdmin) && found.Where(f => f.Id == "game-mode-off").All(f => !f.NeedsAdmin),
                  "política em HKLM pede administrador; Modo de Jogo (HKCU) não");
            var plan = found.FirstOrDefault(f => f.Id == "power-plan-copy");
            Check(plan != null && plan.Current == "Nich FPS+", "plano de energia mostrado pelo nome (" + (plan != null ? plan.Current : "-") + ")");
            ctx.HasBattery = false;
            Check(!GameScanner.Scan(ctx).Any(f => f.Id == "power-plan-copy"), "em PC sem bateria, a cópia do Desempenho Máximo não é apontada");
            ctx.HasBattery = true;

            string backup = Path.Combine(sandbox, "jogos.reg");
            string error;
            bool saved = GameScanner.Backup(found, backup, out error);
            string text = saved && File.Exists(backup) ? File.ReadAllText(backup, Encoding.Unicode) : "";
            Check(saved && text.Contains("AutoGameModeEnabled") && text.Contains("AllowGameDVR") && text.Contains("GlobalUserDisabled"),
                  "backup .reg com as chaves que vão mudar" + (error != null ? " (" + error + ")" : ""));

            int fixedCount = found.Count(GameScanner.Fix);
            var after = GameScanner.Scan(ctx).Where(f => f.Id != "power-plan-copy").ToList(); // a troca de plano é simulada
            Check(fixedCount == found.Count && after.Count == 0, "os 5 itens foram consertados e a nova verificação não acha nada");
            Check(Equals(Value(hkcu, gameBar, "AutoGameModeEnabled"), 1) && Value(hkcu, gameBar, "AllowAutoGameMode") == null,
                  "Modo de Jogo religado (AutoGameModeEnabled=1) e o bloqueio antigo apagado");
            Check(Equals(Value(hkcu, gameBar, "UseNexusForGameBarEnabled"), 0) && Equals(Value(hklm, dvr, "OpenCleanerTestVizinho"), 7) &&
                  Value(hklm, dvr, "AllowGameDVR") == null, "só os valores do conserto mudaram: os vizinhos continuam lá");
            Check(Value(hkcu, bg + @"\App0", "Disabled") == null && Value(hkcu, bg, "GlobalUserDisabled") == null, "apps em segundo plano voltam ao padrão");
            Check(activated == GameScanner.Balanced, "plano de energia: pede a troca para o Equilibrado");

            var p = Process.Start(new ProcessStartInfo("reg.exe", "import \"" + backup + "\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true });
            p.WaitForExit();
            var restored = GameScanner.Scan(ctx).Select(f => f.Id).ToList();
            Check(p.ExitCode == 0 && expected.All(restored.Contains), "reg import do backup trouxe os ajustes de volta");
        }

        var real = Stopwatch.StartNew();
        var onThisPc = GameScanner.Scan(GameContext.System());
        Perf("Verificar ajustes de jogos (registro real, só leitura)", real, onThisPc.Count + " itens: " + string.Join(", ", onThisPc.Select(f => f.Id)));
        Check(real.ElapsedMilliseconds < 1000, "verificação no registro real em menos de 1 s");
    }

    // =====================================================================
    // 6c. Jogos: GPU por jogo (descoberta numa sandbox de pastas e de registro)
    // =====================================================================

    static void GpuReal()
    {
        Section("Jogos: GPU por jogo em notebooks com duas placas");
        Check(GpuPreference.Merge(null, 2) == "GpuPreference=2;", "valor novo: GpuPreference=2;");
        Check(GpuPreference.Merge("GpuPreference=1;", 2) == "GpuPreference=2;", "economia de energia vira alto desempenho");
        Check(GpuPreference.Merge("SwapEffectUpgradeEnable=1;", 2) == "GpuPreference=2;SwapEffectUpgradeEnable=1;", "outros itens do valor são preservados");
        Check(GpuPreference.Merge("GpuPreference=2;SwapEffectUpgradeEnable=1;", 0) == "SwapEffectUpgradeEnable=1;", "\"Windows decide\" tira só o GpuPreference");
        Check(GpuPreference.Merge("GpuPreference=2;", 0) == null, "\"Windows decide\" sem outros itens apaga o valor");

        var intel = new VideoAdapter { Name = "Intel(R) HD Graphics Family", PnpId = @"PCI\VEN_8086&DEV_0A16&SUBSYS_13FD1043" };
        var nvidia = new VideoAdapter { Name = "NVIDIA GeForce 820M", PnpId = @"PCI\VEN_10DE&DEV_1140&SUBSYS_227A1043" };
        var arc = new VideoAdapter { Name = "Intel(R) Arc(TM) A370M Graphics", PnpId = @"PCI\VEN_8086&DEV_5693" };
        var basic = new VideoAdapter { Name = "Microsoft Basic Display Adapter", PnpId = @"PCI\VEN_10DE&DEV_2520" };
        var virt = new VideoAdapter { Name = "Parsec Virtual Display Adapter", PnpId = @"ROOT\DISPLAY\0000" };
        Check(GpuPreference.IsHybridLaptop(new[] { intel, nvidia }, true), "Intel + NVIDIA com bateria: híbrido");
        Check(!GpuPreference.IsHybridLaptop(new[] { intel, nvidia }, false), "Intel + NVIDIA sem bateria (PC de mesa): não");
        Check(!GpuPreference.IsHybridLaptop(new[] { nvidia }, true), "uma placa só: não");
        Check(GpuPreference.IsHybridLaptop(new[] { intel, arc }, true), "Intel integrada + Intel Arc: híbrido");
        Check(!GpuPreference.IsHybridLaptop(new[] { intel, basic, virt }, true), "driver genérico da Microsoft e placa virtual não contam");

        // Pastas: uma Steam com duas bibliotecas, um manifesto da Epic e o registro de jogos do Windows.
        string root = Path.Combine(sandbox, "jogos");
        string steam = Path.Combine(root, "Steam"), lib2 = Path.Combine(root, "Biblioteca 2");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(steam, "steamapps")).FullName, "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace(@"\", @"\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + lib2.Replace(@"\", @"\\") + "\"\n\t}\n}\n");
        Func<string, string, string, string> acf = (id, name, dir) => "\"AppState\"\n{\n\t\"appid\"\t\t\"" + id + "\"\n\t\"name\"\t\t\"" + name + "\"\n\t\"installdir\"\t\t\"" + dir + "\"\n}\n";
        File.WriteAllText(Path.Combine(steam, @"steamapps\appmanifest_100.acf"), acf("100", "Jogo Steam A", "Jogo A"));
        File.WriteAllText(Path.Combine(steam, @"steamapps\appmanifest_228980.acf"), acf("228980", "Steamworks Common Redistributables", "Steamworks Shared"));
        Directory.CreateDirectory(Path.Combine(lib2, "steamapps"));
        File.WriteAllText(Path.Combine(lib2, @"steamapps\appmanifest_200.acf"), acf("200", "Jogo Steam B", "Jogo B"));
        string a = Path.Combine(steam, @"steamapps\common\Jogo A");
        string exeA = Path.Combine(a, "JogoA.exe");
        foreach (var f in new[] { exeA, Path.Combine(a, "unins000.exe"), Path.Combine(a, "UnityCrashHandler64.exe"), Path.Combine(a, @"_CommonRedist\instalar.exe"),
                                  Path.Combine(steam, @"steamapps\common\Steamworks Shared\redist.exe") })
            Write(f);
        string b = Path.Combine(lib2, @"steamapps\common\Jogo B");
        string exeB = Path.Combine(b, @"Binaries\Win64\JogoB-Win64-Shipping.exe");
        Write(exeB);
        Write(Path.Combine(b, "JogoB.exe"));

        string epicDir = Path.Combine(root, @"Epic Games\Jogo Epic");
        string exeEpic = Path.Combine(epicDir, @"Bin\JogoEpic.exe");
        Write(exeEpic);
        string manifests = Directory.CreateDirectory(Path.Combine(root, "Manifests")).FullName;
        File.WriteAllText(Path.Combine(manifests, "1.item"), "{\n\t\"DisplayName\": \"Jogo Epic\",\n\t\"InstallLocation\": \"" + epicDir.Replace(@"\", @"\\") + "\",\n\t\"LaunchExecutable\": \"Bin/JogoEpic.exe\"\n}");
        File.WriteAllText(Path.Combine(manifests, "2.item"), "{\"DisplayName\": \"Malicioso\", \"InstallLocation\": \"" + epicDir.Replace(@"\", @"\\") + "\", \"LaunchExecutable\": \"..\\\\..\\\\..\\\\notepad.exe\"}");

        string exeValorant = Path.Combine(root, @"Riot Games\VALORANT\live\ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe");
        string exeRiot = Path.Combine(root, @"Riot Games\Riot Client\RiotClientServices.exe");
        Write(exeValorant);
        Write(exeRiot);

        using (var hkcu = Registry.CurrentUser.CreateSubKey(TestKey + @"\Gpu\HKCU"))
        {
            string[] seen = { exeValorant, exeB, exeRiot, Path.Combine(root, "nao-existe.exe") };
            for (int i = 0; i < seen.Length; i++)
                using (var k = hkcu.CreateSubKey(@"System\GameConfigStore\Children\c" + i)) k.SetValue("MatchedExeFullPath", seen[i]);

            var sources = new GameSources { Hkcu = hkcu, EpicManifests = manifests };
            sources.SteamRoots.Add(steam);
            var sw = Stopwatch.StartNew();
            var games = GameLibrary.Find(sources);
            Perf("Encontrar jogos (sandbox)", sw, games.Count + " jogos");
            Func<string, InstalledGame> game = n => games.FirstOrDefault(g => g.Name == n);
            Check(games.Count == 4 && game("Jogo Steam A") != null && game("Jogo Steam B") != null && game("Jogo Epic") != null && game("VALORANT-Win64-Shipping") != null,
                  "4 jogos: Steam (2 bibliotecas), Epic e um visto só pelo Windows (achou " + string.Join(", ", games.Select(g => g.Name + " [" + g.Store + "]")) + ")");
            var gA = game("Jogo Steam A");
            Check(gA != null && gA.Exes.Count == 1 && gA.Exes[0] == exeA, "Steam sem registro do Windows: palpite ignora desinstalador, crash handler e _CommonRedist");
            var gB = game("Jogo Steam B");
            Check(gB != null && gB.Exes.Count == 1 && gB.Exes[0] == exeB, "Steam com registro do Windows: usa o executável que rodou, não o palpite");
            var gE = game("Jogo Epic");
            Check(gE != null && gE.Exes.Count == 1 && gE.Exes[0] == exeEpic, "Epic: LaunchExecutable do manifesto; manifesto com \"..\" é recusado");
            Check(!games.SelectMany(g => g.Exes).Any(e => e.EndsWith("RiotClientServices.exe") || e.Contains("Steamworks Shared") || e.Contains("nao-existe")),
                  "launchers, redistribuíveis da Steam e executáveis apagados ficam de fora");

            Check(GpuPreference.Get(hkcu, exeA) == GpuPreference.WindowsDecides, "sem valor: Windows decide");
            Check(GpuPreference.Set(hkcu, exeA, GpuPreference.HighPerformance) && GpuPreference.Get(hkcu, exeA) == GpuPreference.HighPerformance, "gravar alto desempenho");
            using (var k = hkcu.CreateSubKey(GpuPreference.Key)) k.SetValue(exeB, "GpuPreference=1;SwapEffectUpgradeEnable=1;");
            Check(GpuPreference.Get(hkcu, exeB) == GpuPreference.PowerSaving, "lê economia de energia num valor com outros itens");
            GpuPreference.Set(hkcu, exeB, GpuPreference.HighPerformance);
            Check(Equals(Value(hkcu, GpuPreference.Key, exeB), "GpuPreference=2;SwapEffectUpgradeEnable=1;"), "trocar a GPU preserva o SwapEffectUpgradeEnable");
            GpuPreference.Set(hkcu, exeA, GpuPreference.WindowsDecides);
            Check(Value(hkcu, GpuPreference.Key, exeA) == null, "voltar para \"Windows decide\" apaga o valor");
        }

        var real = Stopwatch.StartNew();
        var mine = GameLibrary.Find(GameSources.System());
        var adapters = GpuPreference.Adapters();
        bool hybrid = GpuPreference.IsHybridLaptop(adapters, Util.HasBattery());
        Perf("Encontrar jogos e placas (PC real, só leitura)", real, mine.Count + " jogos (" + string.Join(", ", mine.Select(g => g.Name)) + "); " +
             string.Join(" + ", adapters.Select(x => x.Name)) + (hybrid ? "; híbrido" : ""));
        Check(real.ElapsedMilliseconds < 5000, "descoberta de jogos e placas no PC real em menos de 5 s");
    }

    // =====================================================================
    // 7. Desinstalar programas
    // =====================================================================

    static void UninstallReal(string outDir)
    {
        Section("Desinstalar: programa de teste com desinstalador real");
        string dir = Path.Combine(sandbox, "programa teste");
        Directory.CreateDirectory(dir);
        string marker = Path.Combine(dir, "desinstalado.txt");
        string src = Path.Combine(dir, "u.cs");
        File.WriteAllText(src, "class U { static void Main(string[] a) { System.IO.File.WriteAllText(@\"" + marker + "\", string.Join(\" \", a)); } }");
        string exe = Path.Combine(dir, "desinstalar teste.exe");
        string csc = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
        var c = Process.Start(new ProcessStartInfo(csc, "/nologo /target:winexe \"/out:" + exe + "\" \"" + src + "\"") { CreateNoWindow = true, UseShellExecute = false });
        c.WaitForExit();

        const string key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenCleanerTestApp";
        using (var k = Registry.CurrentUser.CreateSubKey(key))
        {
            k.SetValue("DisplayName", "OpenCleaner Test App");
            k.SetValue("Publisher", "OpenCleanerTest");
            k.SetValue("DisplayVersion", "9.9.9");
            k.SetValue("UninstallString", "\"" + exe + "\" /uninstall /quiet");
            k.SetValue("InstallLocation", dir);
            k.SetValue("EstimatedSize", 1234, RegistryValueKind.DWord);
        }
        var sw = Stopwatch.StartNew();
        var list = Programs.List();
        Perf("Listar programas instalados", sw, list.Count + " programas");
        var app = list.FirstOrDefault(p => p.Name == "OpenCleaner Test App");
        Check(app != null && app.Version == "9.9.9" && app.SizeKB == 1234, "programa de teste listado com versão e tamanho");
        var proc = Programs.Uninstall(app);
        proc.WaitForExit(15000);
        Check(File.Exists(marker) && File.ReadAllText(marker) == "/uninstall /quiet", "desinstalador executado com os argumentos certos (caminho com espaços)");
        Check(Programs.DeleteEntry(app) && Registry.CurrentUser.OpenSubKey(key) == null, "Remover da lista apaga a entrada");
    }

    // =====================================================================
    // 8. Analisador de disco
    // =====================================================================

    static void DiskAnalyzerReal(bool fullDisk)
    {
        Section("Analisador de disco");
        string d = Path.Combine(sandbox, "disco");
        Write(Path.Combine(d, "foto1.jpg"), 300000);
        Write(Path.Combine(d, "sub", "foto2.PNG"), 200000);
        Write(Path.Combine(d, "musica.mp3"), 150000);
        Write(Path.Combine(d, "video.mkv"), 900000);
        Write(Path.Combine(d, "doc.pdf"), 50000);
        Write(Path.Combine(d, "pacote.zip"), 70000);
        Write(Path.Combine(d, "app.exe"), 30000);
        Write(Path.Combine(d, "outro.xyz"), 10000);
        string outside = Path.Combine(sandbox, "fora-do-disco");
        Write(Path.Combine(outside, "enorme.mkv"), 5000000);
        Junction(Path.Combine(d, "atalho"), outside);
        var cats = new DiskAnalyzer().Scan(d);
        Func<string, long> bytes = k => cats[k].Bytes;
        var names = DiskAnalyzer.CategoryNames;
        Check(bytes(names[0]) == 500000 && bytes(names[1]) == 150000 && bytes(names[2]) == 900000 && bytes(names[3]) == 50000 &&
              bytes(names[4]) == 70000 && bytes(names[6]) == 30000 && bytes(names[7]) == 10000,
              "tamanhos por categoria exatos (imagens, música, vídeos, documentos, compactados, executáveis, outros)");
        Check(!cats.Values.SelectMany(x => x.Files).Any(f => f.Path.Contains("enorme")), "junção para fora da pasta não é seguida");

        if (fullDisk)
        {
            GC.Collect();
            var proc = Process.GetCurrentProcess();
            proc.Refresh();
            long before = proc.PrivateMemorySize64;
            var an = new DiskAnalyzer();
            var sw = Stopwatch.StartNew();
            var all = an.Scan(Path.GetPathRoot(Environment.SystemDirectory));
            sw.Stop();
            proc.Refresh();
            long total = all.Values.Sum(x => x.Bytes);
            Perf("Analisar o disco C: inteiro (só leitura)", sw, an.ScannedFiles.ToString("N0") + " arquivos, " + Mb(total) +
                 ", memória +" + Mb(proc.PrivateMemorySize64 - before) + ", pico " + Mb(proc.PeakWorkingSet64));
            Check(all.Values.All(x => x.Files.Count <= DiskCategory.TopFiles), "memória limitada: no máximo " + DiskCategory.TopFiles + " maiores arquivos por categoria");
        }
    }

    // =====================================================================
    // 9. Duplicados
    // =====================================================================

    static void DuplicatesReal()
    {
        Section("Localizador de duplicados");
        string d = Path.Combine(sandbox, "dups");
        var rnd = new Random(42);
        byte[] a = new byte[300000]; rnd.NextBytes(a);
        byte[] b = (byte[])a.Clone(); b[b.Length - 1] ^= 0xFF;   // mesmos primeiros 64 KB, final diferente
        byte[] c = new byte[300000]; rnd.NextBytes(c);           // mesmo tamanho, conteúdo diferente
        Action<string, byte[]> put = (p, data) => { Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, data); };
        put(Path.Combine(d, "a.bin"), a);
        put(Path.Combine(d, "copia", "a-copia.bin"), a);
        put(Path.Combine(d, "copia2", "a.bin"), a);
        put(Path.Combine(d, "b.bin"), b);
        put(Path.Combine(d, "c.bin"), c);
        put(Path.Combine(d, "oculto.bin"), a);
        File.SetAttributes(Path.Combine(d, "oculto.bin"), FileAttributes.Hidden);
        put(Path.Combine(d, "offline.bin"), a);
        File.SetAttributes(Path.Combine(d, "offline.bin"), FileAttributes.Offline);
        put(Path.Combine(d, "pequeno1.txt"), new byte[10]);
        put(Path.Combine(d, "pequeno2.txt"), new byte[10]);

        var groups = new DuplicateFinder { MinSize = 1024 }.Find(d);
        Check(groups.Count == 1 && groups[0].Count == 3, "acha 1 grupo com as 3 cópias idênticas (achou " + groups.Count + " grupo(s))");
        Check(!groups.SelectMany(g => g).Any(f => f.Path.EndsWith("b.bin") || f.Path.EndsWith("c.bin")),
              "mesmo tamanho ou mesmos primeiros 64 KB com conteúdo diferente não são duplicados");
        Check(!groups.SelectMany(g => g).Any(f => f.Path.Contains("oculto") || f.Path.Contains("offline")),
              "ignora ocultos e arquivos só na nuvem (atributo Offline), sem baixar nada");
        var named = new DuplicateFinder { MinSize = 1024, MatchName = true }.Find(d);
        Check(named.Count == 1 && named[0].Count == 2, "com \"Exigir o mesmo nome\", só as 2 cópias chamadas a.bin");

        // Mover para a Lixeira: some da pasta e aparece na Lixeira; depois tiramos só o nosso item de lá
        string victim = Path.Combine(d, "copia", "a-copia.bin");
        Util.SendToRecycleBin(victim);
        bool gone = !File.Exists(victim);
        bool inBin = PurgeFromRecycleBin(victim);
        Check(gone && inBin, "\"Mover para a Lixeira\" manda a cópia para a Lixeira (e o teste a retira de lá)");

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        GC.Collect();
        var proc = Process.GetCurrentProcess(); proc.Refresh();
        long before = proc.PrivateMemorySize64;
        var finder = new DuplicateFinder { MinSize = 100 * 1024 };
        var sw = Stopwatch.StartNew();
        var real = finder.Find(local);
        sw.Stop(); proc.Refresh();
        Perf("Procurar duplicados em %LocalAppData% (>100 KB, só leitura)", sw, real.Count + " grupos, memória +" + Mb(proc.PrivateMemorySize64 - before));
    }

    /// <summary>Tira da Lixeira apenas o item que veio do caminho indicado (arquivos $I e $R).</summary>
    static bool PurgeFromRecycleBin(string originalPath)
    {
        bool found = false;
        string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
        string bin = Path.Combine(Path.GetPathRoot(originalPath), "$Recycle.Bin", sid);
        try
        {
            foreach (var info in Directory.GetFiles(bin, "$I*"))
            {
                byte[] data = File.ReadAllBytes(info);
                string text = Encoding.Unicode.GetString(data);
                if (text.IndexOf(originalPath, StringComparison.OrdinalIgnoreCase) < 0) continue;
                found = true;
                string r = Path.Combine(bin, "$R" + Path.GetFileName(info).Substring(2));
                try { File.Delete(r); } catch { }
                try { File.Delete(info); } catch { }
            }
        }
        catch { }
        return found;
    }

    // =====================================================================
    // 10. winget e drivers
    // =====================================================================

    static void WingetReal()
    {
        Section("Atualizar programas (winget)");
        string v;
        if (!Winget.IsAvailable(out v)) { Console.WriteLine("  winget indisponível; pulado"); return; }
        var sw = Stopwatch.StartNew();
        var ups = Winget.ListUpgrades();
        Perf("Consultar atualizações no winget", sw, ups.Count + " pacotes");
        Check(ups.All(u => Winget.SafeId(u.Id)), "todos os IDs do winget passam na validação");
        // Tabela com colunas alinhadas, como a do winget, com um ID malicioso numa linha.
        Func<string, string, string, string, string, string> row = (a, b, c, e, f) => string.Format("{0,-8}{1,-16}{2,-12}{3,-12}{4}\n", a, b, c, e, f);
        string evil = row("Nome", "ID", "Versão", "Disponível", "Origem") + new string('-', 56) + "\n" +
                      row("Mal", "Evil.\"&calc", "1.0", "2.0", "winget") +
                      row("Bom", "Good.App", "1.0", "2.0", "winget");
        var parsed = Winget.Parse(evil);
        Check(parsed.Count == 1 && parsed[0].Id == "Good.App", "ID com aspas e & vindo da saída do winget é descartado; o válido continua");
        string msg;
        sw.Restart();
        bool ok = Winget.Upgrade(new PackageUpdate { Id = "Evil\" --source x", Name = "x" }, null, out msg);
        Check(!ok && msg == "invalid id", "Upgrade recusa ID com aspas sem chamar o winget");
        ok = Winget.Upgrade(new PackageUpdate { Id = "OpenCleanerTest.NaoExiste", Name = "x" }, null, out msg);
        Perf("winget upgrade de pacote inexistente", sw);
        Check(!ok, "pacote inexistente retorna falha com mensagem (\"" + msg + "\"), sem travar");
    }

    static void DriversReal()
    {
        Section("Atualizar drivers (Windows Update, só busca)");
        using (var du = new DriverUpdater())
        {
            var done = new ManualResetEvent(false);
            List<DriverUpdate> found = null;
            Exception err = null;
            var sw = Stopwatch.StartNew();
            du.Post(() => du.Search(), (l, e) => { found = l; err = e; done.Set(); });
            done.WaitOne(TimeSpan.FromMinutes(10));
            Perf("Procurar drivers no Windows Update", sw, found == null ? "erro" : found.Count + " drivers");
            Check(err == null && found != null, "busca de drivers concluída (" + (found == null ? 0 : found.Count) + " disponíveis)");
        }
    }

    // =====================================================================
    // 11. Robustez: configurações corrompidas e regras malformadas
    // =====================================================================

    static void Robustness()
    {
        Section("Robustez: configurações corrompidas e regras malformadas");
        string ini = Path.Combine(sandbox, "corrompido.ini");
        var rnd = new Random(7);
        int crashes = 0;
        for (int i = 0; i < 200; i++)
        {
            var bytes = new byte[rnd.Next(1, 4000)];
            rnd.NextBytes(bytes);
            File.WriteAllBytes(ini, bytes);
            try { AppSettings.Load(ini); } catch { crashes++; }
        }
        File.WriteAllText(ini, "[Options]\nLanguage=xx\nTempOlderThan24h=talvez\n[Rules]\n=\n==\n[Include]\nF0=\n[Registry]\nX\n");
        AppSettings odd = null;
        try { odd = AppSettings.Load(ini); } catch { crashes++; }
        Check(crashes == 0 && odd != null && odd.Language == L.Auto, "200 arquivos de configuração aleatórios e um malformado carregam sem erro");

        int ruleCrashes = 0;
        var pieces = new[] { "[", "]", "FileKey1=", "RegKey1=", "|", "*", "%", "%Foo%", "RECURSE", "\n", "=", "Detect=", "ExcludeKey1=FILE|", "..\\", "\\\\", "?" };
        for (int i = 0; i < 500; i++)
        {
            var sb = new StringBuilder("[R]\n");
            for (int j = 0; j < 30; j++) sb.Append(pieces[rnd.Next(pieces.Length)]);
            try
            {
                foreach (var r in RuleParser.Parse(sb.ToString(), "fuzz"))
                    foreach (var fk in r.FileKeys) Util.ResolveDirs(fk.Path);
            }
            catch { ruleCrashes++; }
        }
        Check(ruleCrashes == 0, "500 regras winapp2 aleatórias são lidas e resolvidas sem erro");
    }

    // =====================================================================
    // 12. Modo /AUTO e inicialização do app
    // =====================================================================

    static string AppExe(string outDir)
    {
        string p = Path.Combine(Path.GetTempPath(), @"OpenCleaner-build\OpenCleaner.exe");
        return File.Exists(p) ? p : null;
    }

    static void AutoMode(string outDir)
    {
        Section("Modo /AUTO com todos os itens desmarcados (o seu .ini é guardado e devolvido)");
        string exe = AppExe(outDir);
        if (exe == null) { Check(false, "OpenCleaner.exe compilado não encontrado"); return; }
        string ini = AppSettings.FilePath;
        string log = Path.Combine(AppSettings.DataDir, "auto.log");
        string iniBackup = File.Exists(ini) ? File.ReadAllText(ini, Encoding.UTF8) : null;
        string logBackup = File.Exists(log) ? File.ReadAllText(log, Encoding.UTF8) : null;
        try
        {
            var off = new AppSettings();
            foreach (var r in RuleCatalog.LoadAll(off)) off.Rules[r.Id] = false;
            off.Save();
            var sw = Stopwatch.StartNew();
            var p = Process.Start(new ProcessStartInfo(exe, "/AUTO") { UseShellExecute = false, CreateNoWindow = true });
            p.WaitForExit(120000);
            Perf("OpenCleaner.exe /AUTO (nada marcado)", sw);
            string text = File.Exists(log) ? File.ReadAllText(log, Encoding.UTF8) : "";
            Check(p.ExitCode == 0 && text.Contains("0 B"), "/AUTO roda em segundo plano, grava auto.log e não limpa nada quando nada está marcado");
        }
        finally
        {
            if (iniBackup != null) File.WriteAllText(ini, iniBackup, Encoding.UTF8); else if (File.Exists(ini)) File.Delete(ini);
            if (logBackup != null) File.WriteAllText(log, logBackup, Encoding.UTF8); else if (File.Exists(log)) File.Delete(log);
            try { if (Directory.Exists(AppSettings.DataDir) && !Directory.EnumerateFileSystemEntries(AppSettings.DataDir).Any()) Directory.Delete(AppSettings.DataDir); } catch { }
        }
    }

    static void AppStartup(string outDir)
    {
        Section("Abertura do app");
        string exe = AppExe(outDir);
        if (exe == null) return;
        var times = new List<long>();
        long mem = 0;
        for (int i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            var p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
            p.WaitForInputIdle(30000);
            while (p.MainWindowHandle == IntPtr.Zero && sw.ElapsedMilliseconds < 30000) { Thread.Sleep(20); p.Refresh(); }
            times.Add(sw.ElapsedMilliseconds);
            Thread.Sleep(1500);
            p.Refresh();
            mem = Math.Max(mem, p.PrivateMemorySize64);
            p.CloseMainWindow();
            if (!p.WaitForExit(5000)) p.Kill();
        }
        string line = string.Format("  {0,-52} {1,9:N0} ms  (3 aberturas: {2}) memória {3}", "Abrir o app até a janela aparecer",
                                    times.Min(), string.Join(" / ", times), Mb(mem));
        Console.WriteLine(line);
        perf.Add(line);
        Check(times.Min() < 5000, "janela aparece em menos de 5 s");
    }

    // =====================================================================

    static void Cleanup()
    {
        Section("Limpeza do que o teste criou");
        try { Directory.Delete(sandbox, true); } catch { }
        // junções podem impedir o Delete recursivo de primeira
        if (Directory.Exists(sandbox))
        {
            var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c rmdir /s /q \"" + sandbox + "\"") { CreateNoWindow = true, UseShellExecute = false });
            p.WaitForExit();
        }
        try { Registry.CurrentUser.DeleteSubKeyTree(TestKey, false); } catch { }
        string[] valueKeys =
        {
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\NT\CurrentVersion\Fonts",
            @"Software\Microsoft\Windows NT\CurrentVersion\Fonts",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
        };
        foreach (var kp in valueKeys)
            using (var k = Registry.CurrentUser.OpenSubKey(kp, true))
                if (k != null) foreach (var n in k.GetValueNames().Where(n => n.Contains("OpenCleanerTest"))) k.DeleteValue(n, false);
        foreach (var kp in new[] { @"Software\Microsoft\Windows\CurrentVersion\App Paths", @"Software\Microsoft\Windows\CurrentVersion\Uninstall" })
            using (var k = Registry.CurrentUser.OpenSubKey(kp, true))
                if (k != null) foreach (var n in k.GetSubKeyNames().Where(n => n.Contains("OpenCleanerTest"))) k.DeleteSubKeyTree(n, false);

        bool leftFiles = Directory.Exists(sandbox);
        int leftReg = 0;
        foreach (var kp in valueKeys)
            using (var k = Registry.CurrentUser.OpenSubKey(kp))
                if (k != null) leftReg += k.GetValueNames().Count(n => n.Contains("OpenCleanerTest"));
        Check(!leftFiles && leftReg == 0 && Registry.CurrentUser.OpenSubKey(TestKey) == null,
              "sandbox apagada e nenhuma entrada OpenCleanerTest sobrou no registro");
    }
}
