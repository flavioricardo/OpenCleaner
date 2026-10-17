// Teste de fumaça: compilado junto com src/ por tests\run-smoke.ps1.
// Nada fora da pasta sandbox é apagado: as regras reais só são ANALISADAS.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenCleaner.Core;

static class SmokeTest
{
    static int failures;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    static IEnumerable<ListView> AllLists(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is ListView) yield return (ListView)c;
            foreach (var l in AllLists(c)) yield return l;
        }
    }

    static Button FindButton(Control root, string text)
    {
        foreach (Control c in root.Controls)
        {
            var b = c as Button;
            if (b != null && b.Text == text && b.Visible && c.Parent != null && c.Parent.Visible) return b;
            var found = FindButton(c, text);
            if (found != null) return found;
        }
        return null;
    }

    static void Check(bool cond, string what)
    {
        Console.WriteLine((cond ? "  [ok]   " : "  [FALHA] ") + what);
        if (!cond) failures++;
    }

    [STAThread]
    static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("== Regras ==");
        var settings = new AppSettings(); // padrão, sem ler o ini do usuário
        var rules = RuleCatalog.LoadAll(settings);
        Console.WriteLine("  total: " + rules.Count);
        foreach (var g in rules.GroupBy(r => r.Category)) Console.WriteLine("  " + g.Key + ": " + g.Count());
        Check(rules.Count > 40, "mais de 40 regras embutidas");
        Check(rules.Select(r => r.Id).Distinct().Count() == rules.Count, "ids de regra únicos");

        var sw = Stopwatch.StartNew();
        var detected = rules.Where(r => r.IsDetected()).ToList();
        Console.WriteLine("  detectadas: " + detected.Count + " em " + sw.ElapsedMilliseconds + " ms");
        Console.WriteLine("  grupos detectados: " + string.Join(", ", detected.Select(r => r.Group).Distinct()));

        Console.WriteLine("== Análise (somente leitura) de todas as regras detectadas ==");
        var engine = new CleanerEngine(settings);
        long total = 0;
        sw.Restart();
        foreach (var r in detected)
        {
            var res = engine.Analyze(r);
            total += res.Bytes;
            if (res.ItemCount > 0 || res.Note != null)
                Console.WriteLine(string.Format("  {0,-55} {1,6} itens {2,10}  {3}", r.FullName, res.ItemCount, Util.FormatSize(res.Bytes), res.Note));
        }
        Console.WriteLine("  total recuperável: " + Util.FormatSize(total) + " (" + sw.ElapsedMilliseconds + " ms)");

        Console.WriteLine("== Limpeza real em sandbox ==");
        string sandbox = Path.Combine(Path.GetTempPath(), "OpenCleanerSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(sandbox, @"Perfil 1\Cache\sub"));
        Directory.CreateDirectory(Path.Combine(sandbox, @"Perfil 2\Cache"));
        Directory.CreateDirectory(Path.Combine(sandbox, @"keep"));
        File.WriteAllText(Path.Combine(sandbox, @"Perfil 1\Cache\a.tmp"), new string('x', 1000));
        File.WriteAllText(Path.Combine(sandbox, @"Perfil 1\Cache\sub\b.dat"), new string('x', 2000));
        File.WriteAllText(Path.Combine(sandbox, @"Perfil 2\Cache\c.tmp"), new string('x', 500));
        File.WriteAllText(Path.Combine(sandbox, @"Perfil 2\Cache\manter.txt"), "x");
        File.WriteAllText(Path.Combine(sandbox, @"keep\d.tmp"), "x");
        var ro = Path.Combine(sandbox, @"Perfil 1\Cache\ro.tmp");
        File.WriteAllText(ro, "x");
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        var old = Path.Combine(sandbox, "velho.log");
        var fresh = Path.Combine(sandbox, "novo.log");
        File.WriteAllText(old, "x");
        File.WriteAllText(fresh, "x");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-3));

        string ini =
            "[Teste]\nCategory=Teste\nGroup=Teste\nItem=Cache\n" +
            "FileKey1=" + sandbox + @"\*\Cache|*.*|RECURSE" + "\n" +
            "ExcludeKey1=FILE|" + sandbox + @"\Perfil 2\Cache\|manter.txt" + "\n" +
            "[Idade]\nCategory=Teste\nGroup=Teste\nItem=Idade\nTempAge=True\n" +
            "FileKey1=" + sandbox + "|*.log\n" +
            "[Pasta]\nCategory=Teste\nGroup=Teste\nItem=RemoveSelf\n" +
            "FileKey1=" + sandbox + @"\Perfil 1|*.*|REMOVESELF" + "\n";
        var test = RuleParser.Parse(ini, "teste");
        Check(test.Count == 3, "parser leu 3 regras de teste");

        var r1 = engine.Analyze(test[0]);
        Check(r1.Files.Count == 4, "curinga em pasta + RECURSE + exclusão encontra 4 arquivos (achou " + r1.Files.Count + ")");
        Check(r1.Bytes == 3501, "soma de tamanhos = 3501 bytes (" + r1.Bytes + ")");
        engine.Clean(r1);
        Check(r1.Removed == 4 && r1.Failed == 0, "limpeza removeu 4, incluindo somente-leitura");
        Check(File.Exists(Path.Combine(sandbox, @"Perfil 2\Cache\manter.txt")), "arquivo excluído foi preservado");
        Check(File.Exists(Path.Combine(sandbox, @"keep\d.tmp")), "pasta fora da regra intacta");

        var r2 = engine.Analyze(test[1]);
        Check(r2.Files.Count == 1 && r2.Files[0] == old, "TempAge: só o arquivo com mais de 24h");

        var r3 = engine.Analyze(test[2]);
        engine.Clean(r3);
        Check(!Directory.Exists(Path.Combine(sandbox, "Perfil 1")), "REMOVESELF apagou a pasta");

        var bad = RuleParser.Parse("[Perigo]\nFileKey1=%WinDir%|*.*|RECURSE\nFileKey2=%UserProfile%|*.*|RECURSE\n", "teste")[0];
        var rb = engine.Analyze(bad);
        Check(rb.Files.Count == 0 && rb.Note != null, "pastas protegidas são recusadas");
        Directory.Delete(sandbox, true);

        Console.WriteLine("== Utilitários ==");
        Check(Util.ExtractExePath("\"C:\\Program Files\\X\\a.exe\" /s") == @"C:\Program Files\X\a.exe", "ExtractExePath com aspas");
        Check(Util.ExtractExePath(@"C:\Windows\System32\cmd.exe /c echo") == @"C:\Windows\System32\cmd.exe", "ExtractExePath sem aspas e com espaços");
        Check(Util.ExtractExePath("rundll32.exe foo.dll,Bar") == null, "ExtractExePath ignora relativo");
        Check(Util.ExtractExePath(@"C:\Nao Existe\app.exe -x") == @"C:\Nao Existe\app.exe", "ExtractExePath de exe ausente");
        Check(Util.FormatSize(1536) == "1.5 KB" || Util.FormatSize(1536) == "1,5 KB", "FormatSize");

        Console.WriteLine("== winget ==");
        string sample =
            "Nome                          ID                  Versão           Disponível       Origem\r\n" +
            "--------------------------------------------------------------------------------------------\r\n" +
            "7-Zip 21.07 (x64)             7zip.7zip           21.07            26.04            winget\r\n" +
            "Zoom                          Zoom.Zoom.EXE       5.9.1 (2581)     7.2.1 (48556)    winget\r\n" +
            "2 atualizações disponíveis.\r\n\r\n" +
            "Os seguintes pacotes têm uma atualização, mas exigem direcionamento explícito:\r\n" +
            "Name                          Id                  Version          Available        Source\r\n" +
            "--------------------------------------------------------------------------------------------\r\n" +
            "Microsoft Edge                Microsoft.Edge      155.0.1          155.0.2          winget\r\n";
        var parsed = Winget.Parse(sample);
        Check(parsed.Count == 3, "Parse leu 3 pacotes de 2 tabelas (" + parsed.Count + ")");
        Check(parsed.Count == 3 && parsed[1].Id == "Zoom.Zoom.EXE" && parsed[1].Version == "5.9.1 (2581)" && parsed[1].Available == "7.2.1 (48556)",
              "versões com espaço ficam inteiras");
        Check(parsed.Count == 3 && !parsed[0].Explicit && parsed[2].Explicit, "segunda tabela marcada como atualização manual");
        string wv;
        if (Winget.IsAvailable(out wv))
        {
            sw.Restart();
            var ups = Winget.ListUpgrades();
            Console.WriteLine("  winget " + wv + ": " + ups.Count + " atualizações em " + sw.ElapsedMilliseconds + " ms");
            foreach (var u in ups.Take(5)) Console.WriteLine("    " + u.Name + " | " + u.Id + " | " + u.Version + " -> " + u.Available);
            Check(ups.All(u => u.Id.Length > 0 && !u.Id.Contains(" ")), "IDs reais sem espaços");
        }
        else Console.WriteLine("  winget indisponível");

        if (args.Contains("--drivers"))
        {
            Console.WriteLine("== Drivers (Windows Update, somente busca) ==");
            using (var du = new DriverUpdater())
            {
                var done = new System.Threading.ManualResetEvent(false);
                sw.Restart();
                du.Post(() => du.Search(), (list, err) =>
                {
                    if (err != null) Console.WriteLine("  erro: " + err.Message);
                    else foreach (var d in list) Console.WriteLine("    " + d.Title + " | " + d.Manufacturer + " | " + d.DriverClass + " | " + d.Date);
                    Console.WriteLine("  " + (list == null ? 0 : list.Count) + " drivers em " + sw.Elapsed.TotalSeconds.ToString("0") + " s");
                    Check(err == null, "busca de drivers via Windows Update Agent");
                    done.Set();
                });
                done.WaitOne(TimeSpan.FromMinutes(10));
            }
        }

        Console.WriteLine("== Ícones Lucide ==");
        var icons = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith("OpenCleaner.icons.")).Select(n => n.Substring(18, n.Length - 22)).ToList();
        int blank = 0;
        foreach (var name in icons)
            using (var bmp = OpenCleaner.UI.Lucide.Render(name, 24, Color.Black))
            {
                int ink = 0;
                for (int x = 0; x < 24; x++) for (int y = 0; y < 24; y++) if (bmp.GetPixel(x, y).A > 0) ink++;
                if (ink < 10) { blank++; Console.WriteLine("    vazio: " + name); }
            }
        Check(icons.Count >= 20 && blank == 0, icons.Count + " ícones renderizados");

        Console.WriteLine("== Registro (somente leitura) ==");
        sw.Restart();
        var issues = new RegistryScanner().Scan(RegistryScanner.Categories);
        Console.WriteLine("  " + issues.Count + " problemas em " + sw.ElapsedMilliseconds + " ms");
        foreach (var g in issues.GroupBy(i => i.Category)) Console.WriteLine("    " + g.Key + ": " + g.Count());
        foreach (var i in issues.Take(12)) Console.WriteLine("    - " + i.Problem + " | " + i.Data + " | " + i.DisplayKey + (i.ValueName != null ? " [" + i.ValueName + "]" : ""));

        Console.WriteLine("== Ferramentas ==");
        var progs = Programs.List();
        Console.WriteLine("  programas instalados: " + progs.Count);
        Check(progs.Count > 5, "lista de programas");
        var st = Startup.List();
        Console.WriteLine("  inicialização: " + st.Count + " (" + st.Count(e => e.Enabled) + " ativados)");
        foreach (var e in st) Console.WriteLine("    " + (e.Enabled ? "[x] " : "[ ] ") + e.Name + " — " + e.Location);

        Console.WriteLine("== Capturas de tela ==");
        Application.EnableVisualStyles();
        var form = new OpenCleaner.UI.MainForm();
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-3000, 0);
        form.Show();
        Action<double> pump = secs =>
        {
            var until = DateTime.Now.AddSeconds(secs);
            while (DateTime.Now < until) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
        };
        Func<Func<bool>, double, bool> waitFor = (cond, secs) =>
        {
            var until = DateTime.Now.AddSeconds(secs);
            while (DateTime.Now < until) { if (cond()) return true; Application.DoEvents(); System.Threading.Thread.Sleep(50); }
            return false;
        };
        Action<string> shot = name =>
        {
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                string file = Path.Combine(outDir, "shot-" + name + ".png");
                bmp.Save(file);
                Console.WriteLine("  " + file);
            }
        };
        Check(form.Height <= Screen.PrimaryScreen.WorkingArea.Height, "janela cabe na área útil da tela");

        foreach (var page in new[] { "home", "clean", "registry", "updates", "drivers", "uninstall", "startup", "disk", "dupes", "options" })
        {
            form.Navigate(page);
            pump(page == "updates" ? 25 : page == "clean" || page == "uninstall" ? 6 : 1.5);
            shot(page);
            foreach (var lv in AllLists(form).Where(l => l.Visible && l.Items.Count > 0))
            {
                int cols = lv.Columns.Cast<ColumnHeader>().Sum(c => c.Width);
                bool hs = (GetWindowLong(lv.Handle, -16) & 0x00100000) != 0;
                Check(!hs, page + ": lista sem rolagem horizontal (colunas " + cols + " / área " + lv.ClientSize.Width + ")");
            }
        }

        // Estados com resultado (só leitura: verificar, analisar e procurar não alteram nada).
        Action<string, string, string, double> run = (page, button, doneText, secs) =>
        {
            form.Navigate(page);
            pump(1);
            var b = FindButton(form, button);
            if (b == null) { Check(false, "botão \"" + button + "\" em " + page); return; }
            b.PerformClick();
            bool done = waitFor(() => doneText == null ? b.Visible && b.Enabled : FindButton(form, doneText) != null, secs);
            pump(1);
            Check(done, page + ": \"" + button + "\" terminou");
            shot(page + "-result");
        };
        run("home", "Verificar agora", "Verificar de novo", 120);
        run("clean", "Analisar", null, 120);
        run("registry", "Procurar problemas", null, 60);
        form.Close();

        Console.WriteLine(failures == 0 ? "\nTUDO OK" : "\n" + failures + " FALHA(S)");
        return failures;
    }
}
