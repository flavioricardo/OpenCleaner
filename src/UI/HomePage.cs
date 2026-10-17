using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenCleaner.Core;
using Microsoft.Win32;

namespace OpenCleaner.UI
{
    /// <summary>
    /// Assinatura visual do OpenCleaner: o disco como uma régua. Ardósia = espaço usado,
    /// hachurado em Verde-limpo = o que a limpeza pode devolver, trilho vazio = espaço livre.
    /// Depois da limpeza, o trecho hachurado encolhe e o livre cresce.
    /// </summary>
    class SpaceMeter : Control
    {
        public long Total;
        double free, junk = -1;          // valores exibidos (animados)
        double fromFree, fromJunk, toFree, toJunk;
        DateTime animStart;
        readonly Timer timer = new Timer { Interval = 16 };
        const double AnimMs = 700;

        public SpaceMeter()
        {
            Height = Theme.S(70);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            timer.Tick += (s, e) =>
            {
                double t = Math.Min(1, (DateTime.Now - animStart).TotalMilliseconds / AnimMs);
                double k = 1 - Math.Pow(1 - t, 3); // desacelera no fim
                free = fromFree + (toFree - fromFree) * k;
                junk = fromJunk + (toJunk - fromJunk) * k;
                if (t >= 1) timer.Stop();
                Invalidate();
            };
        }

        /// <summary>junk &lt; 0 = ainda não verificado.</summary>
        public void Set(long total, long newFree, long newJunk, bool animate)
        {
            Total = total;
            if (!animate || !Theme.Motion || junk < 0)
            {
                free = newFree;
                junk = newJunk;
                Invalidate();
                return;
            }
            fromFree = free; fromJunk = junk; toFree = newFree; toJunk = newJunk;
            animStart = DateTime.Now;
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Papel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Total <= 0) return;

            float h = Theme.S(30f);
            var track = new RectangleF(0, 0, Width - 1, h);
            double used = Total - free;
            double j = Math.Max(0, junk);
            float wUsed = (float)(track.Width * used / Total);
            float wJunk = j <= 0 ? 0 : Math.Max(Theme.S(6f), (float)(track.Width * j / Total));
            wJunk = Math.Min(wJunk, wUsed);

            using (var path = Theme.Rounded(track, Theme.S(8f)))
            {
                using (var b = new SolidBrush(Color.FromArgb(0xE3, 0xE7, 0xEC))) g.FillPath(b, path);
                g.SetClip(path);
                using (var b = new SolidBrush(Theme.Ardosia)) g.FillRectangle(b, 0, 0, wUsed - wJunk, h);
                if (wJunk > 0)
                    using (var hb = new HatchBrush(HatchStyle.WideUpwardDiagonal, Theme.Verde, Theme.VerdeNevoa))
                        g.FillRectangle(hb, wUsed - wJunk, 0, wJunk, h);
                g.ResetClip();
            }

            // Legenda: amostra + rótulo + número
            float y = h + Theme.S(14f);
            float x = 0;
            x = Legend(g, x, y, Brushes.Transparent, Theme.Ardosia, L.T("Usado", "Used"), Util.FormatSize((long)(used - j)), false);
            x = Legend(g, x, y, null, Theme.Verde, L.T("Pode ser liberado", "Can be freed"), junk < 0 ? L.T("verifique para saber", "check to find out") : Util.FormatSize((long)j), true);
            Legend(g, x, y, null, Color.FromArgb(0xC9, 0xD0, 0xD8), L.T("Livre", "Free"), Util.FormatSize((long)free), false);
        }

        float Legend(Graphics g, float x, float y, Brush unused, Color color, string label, string value, bool hatched)
        {
            var sw = new RectangleF(x, y + Theme.S(3f), Theme.S(12f), Theme.S(12f));
            using (var path = Theme.Rounded(sw, Theme.S(3f)))
            {
                if (hatched)
                    using (var hb = new HatchBrush(HatchStyle.WideUpwardDiagonal, color, Theme.VerdeNevoa)) g.FillPath(hb, path);
                else
                    using (var b = new SolidBrush(color)) g.FillPath(b, path);
            }
            x += Theme.S(18f);
            var labelSize = TextRenderer.MeasureText(g, label, Theme.Base);
            TextRenderer.DrawText(g, label, Theme.Base, new Point((int)x, (int)y), Theme.Grafite);
            x += labelSize.Width;
            var valueSize = TextRenderer.MeasureText(g, value, Theme.Bold);
            TextRenderer.DrawText(g, value, Theme.Bold, new Point((int)x, (int)y), Theme.Tinta);
            return x + valueSize.Width + Theme.S(22f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    class HomePage : Page
    {
        /// <summary>Cartão de resultado: explica o que verifica até haver um número para mostrar.</summary>
        class ResultCard : Panel
        {
            readonly Label value, detail, about;
            readonly Button action;

            public ResultCard(string icon, string title, string description, string actionText, Action onClick)
            {
                BackColor = Theme.Papel;
                Height = Theme.S(168);
                Margin = Theme.Pad(0, 0, 16, 0);
                Paint += (s, e) => { using (var p = new Pen(Theme.Linha)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); };
                Resize += (s, e) => { Invalidate(); detail.Width = about.Width = Width - Theme.S(40); };

                Controls.Add(new IconBox(icon, Theme.S(20), Theme.Verde) { Location = Theme.Pt(20, 20) });
                Controls.Add(new Label { Text = title, Font = Theme.H2, Location = Theme.Pt(50, 20), AutoSize = true });
                about = new Label { Text = description, ForeColor = Theme.Grafite, Location = Theme.Pt(20, 56), Size = Theme.Sz(200, 60) };
                value = new Label { Font = Theme.Big, Location = Theme.Pt(17, 48), AutoSize = true, Visible = false };
                detail = new Label { ForeColor = Theme.Grafite, Location = Theme.Pt(20, 96), Size = Theme.Sz(200, 22), Visible = false, AutoEllipsis = true };
                action = Theme.MakeButton(actionText, ButtonKind.Link);
                action.Location = Theme.Pt(10, 122);
                action.Visible = false;
                action.Click += (s, e) => onClick();
                Controls.AddRange(new Control[] { about, value, detail, action });
            }

            public void ShowResult(string number, string text, bool showAction, bool alert = false)
            {
                about.Visible = false;
                value.Text = number;
                value.ForeColor = alert ? Theme.Ambar : Theme.Tinta;
                value.Visible = true;
                detail.Text = text;
                detail.Visible = true;
                action.Visible = showAction;
            }

            public void Busy()
            {
                about.Visible = false;
                value.Text = "…";
                value.ForeColor = Theme.Grafite;
                value.Visible = true;
                detail.Visible = action.Visible = false;
            }
        }

        readonly SpaceMeter meter;
        readonly Label headline, headlineSub, verdict;
        readonly Button btnCheck, btnRecheck, btnClean;
        readonly ResultCard registry, startup, updates;
        readonly FlowLayoutPanel cards;
        List<RuleResult> junkResults;
        DriveInfo drive;
        bool busy;

        public HomePage(MainForm main)
            : base(main, L.T("Verificação de saúde", "Health check"), L.T("Quanto espaço dá para recuperar e o que mais merece atenção neste computador.", "How much space you can get back and what else needs attention on this computer."))
        {
            // ---- Cartão principal: o disco ----
            var hero = new Panel { Dock = DockStyle.Top, Height = Theme.S(250), BackColor = Theme.Papel, Padding = Theme.Pad(28, 24, 28, 22) };
            hero.Paint += (s, e) => { using (var p = new Pen(Theme.Linha)) e.Graphics.DrawRectangle(p, 0, 0, hero.Width - 1, hero.Height - 1); };
            hero.Resize += (s, e) => hero.Invalidate();

            var bottomRow = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(40) };
            verdict = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Grafite, AutoEllipsis = true };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            btnCheck = Theme.MakeButton(L.T("Verificar agora", "Check now"), ButtonKind.Primary, "heart-pulse");
            btnCheck.Click += (s, e) => Check();
            btnClean = Theme.MakeButton(L.T("Limpar", "Clean"), ButtonKind.Primary, "brush-cleaning");
            btnClean.Visible = false;
            btnClean.Click += (s, e) => CleanJunk();
            // Depois da primeira verificação a ação principal passa a ser Limpar; verificar de novo vira secundária.
            btnRecheck = Theme.MakeButton(L.T("Verificar de novo", "Check again"), ButtonKind.Secondary, "refresh-cw");
            btnRecheck.Visible = false;
            btnRecheck.Click += (s, e) => Check();
            actions.Controls.Add(btnClean);
            actions.Controls.Add(btnRecheck);
            actions.Controls.Add(btnCheck);
            bottomRow.Controls.Add(verdict);
            bottomRow.Controls.Add(actions);
            hero.Controls.Add(bottomRow);

            meter = new SpaceMeter { Dock = DockStyle.Top };
            hero.Controls.Add(meter);
            hero.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(16) });

            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(78) };
            var eyebrow = Theme.SectionLabel(L.T("Disco do sistema", "System drive"));
            eyebrow.Location = Theme.Pt(0, 0);
            headline = new Label { Font = Theme.Big, AutoSize = true, Location = Theme.Pt(-3, 18) };
            headlineSub = new Label { ForeColor = Theme.Grafite, AutoSize = true };
            headline.SizeChanged += (s, e) => headlineSub.Location = new Point(headline.Right + Theme.S(6), headline.Top + headline.Height - headlineSub.Height - Theme.S(6));
            head.Controls.AddRange(new Control[] { eyebrow, headline, headlineSub });
            hero.Controls.Add(head);

            // ---- Outros resultados ----
            cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(168), WrapContents = false };
            registry = new ResultCard("database", L.T("Registro", "Registry"), L.T("Entradas que apontam para programas e arquivos que não existem mais.", "Entries that point to programs and files that no longer exist."),
                                      L.T("Revisar no Registro", "Review in Registry"), () => Main.Navigate("registry"));
            startup = new ResultCard("power", L.T("Inicialização", "Startup"), L.T("Programas que abrem junto com o Windows e deixam a entrada mais lenta.", "Programs that open with Windows and slow down sign-in."),
                                     L.T("Gerenciar inicialização", "Manage startup"), () => Main.Navigate("startup"));
            updates = new ResultCard("refresh-cw", L.T("Atualizações", "Updates"), L.T("Programas instalados que têm versão mais nova disponível.", "Installed programs that have a newer version available."),
                                     L.T("Atualizar programas", "Update programs"), () => Main.Navigate("updates"));
            updates.Margin = Padding.Empty;
            cards.Controls.AddRange(new Control[] { registry, startup, updates });
            cards.Resize += (s, e) =>
            {
                int w = (cards.ClientSize.Width - Theme.S(32)) / 3;
                foreach (Control c in cards.Controls) c.Width = Math.Max(Theme.S(180), w);
            };

            var system = new Label { Dock = DockStyle.Bottom, Height = Theme.S(26), ForeColor = Theme.Grafite, Font = Theme.Small, Text = SystemSummary(), TextAlign = ContentAlignment.BottomLeft };

            Body.Controls.Add(system);
            Body.Controls.Add(cards);
            Body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(16) });
            Body.Controls.Add(hero);

            UpdateDisk(0, false);
            verdict.Text = L.T("A verificação só olha: nada é apagado ou alterado até você confirmar.", "The check only looks: nothing is deleted or changed until you confirm.");
        }

        protected override bool ShowStatusRow { get { return false; } }

        static string SystemSummary()
        {
            string os = "Windows", cpu = "";
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    os = Util.ReadString(k, "ProductName") ?? "Windows";
                    int build;
                    if (int.TryParse(Util.ReadString(k, "CurrentBuild"), out build) && build >= 22000) os = os.Replace("Windows 10", "Windows 11");
                    string dv = Util.ReadString(k, "DisplayVersion");
                    if (dv != null) os += " " + dv;
                }
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    cpu = System.Text.RegularExpressions.Regex.Replace(Util.ReadString(k, "ProcessorNameString") ?? "", @"\s+", " ").Trim();
            }
            catch { }
            string ram = "";
            try { ram = Util.FormatSize((long)new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory) + L.T(" de RAM", " of RAM"); }
            catch { }
            return string.Join("  ·  ", new[] { Environment.MachineName, os, cpu, ram }.Where(s => !string.IsNullOrEmpty(s)));
        }

        void UpdateDisk(long junk, bool animate)
        {
            try
            {
                drive = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
                headline.Text = Util.FormatSize(drive.AvailableFreeSpace) + L.T(" livres", " free");
                headlineSub.Text = L.T("de ", "of ") + Util.FormatSize(drive.TotalSize) + L.T(" no disco ", " on drive ") + drive.Name.TrimEnd('\\');
                headline.ForeColor = drive.AvailableFreeSpace < drive.TotalSize / 10 ? Theme.Ambar : Theme.Tinta;
                meter.Set(drive.TotalSize, drive.AvailableFreeSpace, junkResults == null && !animate ? -1 : junk, animate);
            }
            catch { }
        }

        void Check()
        {
            if (busy) return;
            busy = true;
            btnCheck.Enabled = btnRecheck.Enabled = false;
            btnClean.Visible = false;
            registry.Busy(); startup.Busy(); updates.Busy();
            var engine = new CleanerEngine(Main.Settings);
            var results = new List<RuleResult>();
            List<RegIssue> issues = null;
            int startupOn = 0, startupTotal = 0;
            List<PackageUpdate> outdated = null;
            string wingetVersion;

            RunBackground(() =>
            {
                var rules = Main.DetectedRules().Where(r => Main.Settings.IsRuleOn(r)).ToList();
                foreach (var r in rules)
                {
                    Ui(() => verdict.Text = L.T("Analisando ", "Analyzing ") + r.FullName + "...");
                    results.Add(engine.Analyze(r));
                }
                Ui(() => verdict.Text = L.T("Verificando o registro...", "Checking the registry..."));
                issues = new RegistryScanner().Scan(RegistryScanner.Categories.Where(c => !Main.Settings.RegistryDisabled.Contains(c)));
                var st = Startup.List();
                startupOn = st.Count(e => e.Enabled);
                startupTotal = st.Count;
                if (Winget.IsAvailable(out wingetVersion))
                {
                    Ui(() => verdict.Text = L.T("Procurando atualizações de programas...", "Looking for program updates..."));
                    try { outdated = Winget.ListUpgrades(); }
                    catch { }
                }
            }, () =>
            {
                busy = false;
                btnCheck.Enabled = btnRecheck.Enabled = true;
                btnCheck.Visible = false;
                btnRecheck.Visible = true;
                junkResults = results.Where(r => r.ItemCount > 0).ToList();
                long bytes = junkResults.Sum(r => r.Bytes);
                UpdateDisk(bytes, false);
                if (bytes > 0)
                {
                    verdict.Text = Util.FormatSize(bytes) + L.T(" em ", " in ") + junkResults.Count + L.T(" categorias de arquivos desnecessários (itens marcados na Limpeza personalizada).", " categories of unneeded files (items checked in Custom clean).");
                    verdict.ForeColor = Theme.Tinta;
                    btnClean.Text = L.T("Limpar ", "Clean ") + Util.FormatSize(bytes);
                    btnClean.Visible = true;
                }
                else
                {
                    verdict.Text = L.T("Nenhum arquivo desnecessário nos itens marcados.", "No unneeded files in the checked items.");
                    verdict.ForeColor = Theme.Verde;
                }

                registry.ShowResult(issues.Count.ToString(), issues.Count == 0 ? L.T("nenhuma entrada órfã", "no orphaned entries") : L.T("entradas órfãs para revisar", "orphaned entries to review"), issues.Count > 0, issues.Count > 0);
                startup.ShowResult(startupOn.ToString(), L.T("de ", "of ") + startupTotal + L.T(" programas abrem com o Windows", " programs open with Windows"), startupTotal > 0, startupOn > 10);
                if (outdated == null) updates.ShowResult("—", "winget não instalado", true);
                else updates.ShowResult(outdated.Count.ToString(), outdated.Count == 0 ? L.T("tudo atualizado", "everything up to date") : L.T("com versão nova disponível", "with a newer version available"), outdated.Count > 0, outdated.Count > 0);
            });
        }

        void CleanJunk()
        {
            if (busy || junkResults == null || junkResults.Count == 0) return;
            var rules = junkResults.Select(r => r.Rule).ToList();
            if (Main.Settings.ConfirmClean &&
                MessageBox.Show(this, L.T("Apagar ", "Delete ") + Util.FormatSize(junkResults.Sum(r => r.Bytes)) + L.T(" de arquivos desnecessários?", " of unneeded files?"), L.T("Limpar", "Clean"),
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            rules = CleanFlow.HandleRunningApps(this, rules);
            if (rules == null) return;

            busy = true;
            btnClean.Visible = false;
            btnRecheck.Enabled = false;
            var engine = new CleanerEngine(Main.Settings);
            long freed = 0, left = 0;
            int failed = 0;
            RunBackground(() =>
            {
                foreach (var rule in rules)
                {
                    Ui(() => verdict.Text = L.T("Limpando ", "Cleaning ") + rule.FullName + "...");
                    var r = engine.Analyze(rule);
                    engine.Clean(r);
                    freed += r.Freed;
                    failed += r.Failed;
                    left += Math.Max(0, r.Bytes - r.Freed);
                }
            }, () =>
            {
                busy = false;
                btnRecheck.Enabled = true;
                junkResults = null;
                UpdateDisk(left, true); // o trecho hachurado encolhe até o que ficou em uso
                verdict.Text = Util.FormatSize(freed) + L.T(" liberados.", " freed.") + (failed > 0 ? " " + failed + L.T(" arquivos em uso ficaram para trás.", " files in use were left behind.") : "");
                verdict.ForeColor = Theme.Verde;
            });
        }
    }
}
