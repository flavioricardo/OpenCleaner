using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenCleaner.Core;
using Microsoft.Win32;

namespace OpenCleaner.UI
{
    class HomePage : Page
    {
        class Card
        {
            public Panel Panel;
            public Label Value, Detail;
            public Button Action;
        }

        readonly Button btnCheck;
        readonly Label status;
        readonly Card junk, registry, startup, updates, disk;
        List<RuleResult> junkResults;

        public HomePage(MainForm main)
            : base(main, "Verificação de saúde", "Uma verificação rápida de arquivos desnecessários, registro, inicialização, atualizações e disco.")
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            Body.Controls.Add(flow);

            var sys = SystemCard();
            flow.Controls.Add(sys);
            flow.Resize += (s, e) => sys.Width = Math.Max(400, flow.ClientSize.Width - 8);

            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 18, 0, 10), WrapContents = false };
            btnCheck = Theme.MakeButton("Iniciar verificação", true, "heart-pulse");
            btnCheck.MinimumSize = new Size(200, 44);
            btnCheck.Font = Theme.H2;
            btnCheck.Click += (s, e) => Check();
            status = new Label { AutoSize = true, Margin = new Padding(14, 13, 0, 0), ForeColor = Theme.Muted, Text = "Nada é alterado até você confirmar." };
            row.Controls.Add(btnCheck);
            row.Controls.Add(status);
            flow.Controls.Add(row);

            var cards = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            junk = MakeCard(cards, "brush-cleaning", "Arquivos a limpar", "Limpar agora", CleanJunk);
            registry = MakeCard(cards, "database", "Registro", "Revisar", () => Main.Navigate("registry"));
            startup = MakeCard(cards, "power", "Inicialização", "Gerenciar", () => Main.Navigate("startup"));
            updates = MakeCard(cards, "refresh-cw", "Atualizações", "Atualizar", () => Main.Navigate("updates"));
            disk = MakeCard(cards, "hard-drive", "Espaço em disco", "Analisar", () => Main.Navigate("disk"));
            flow.Controls.Add(cards);
            UpdateDiskCard();
        }

        Control SystemCard()
        {
            var card = Theme.MakeCard();
            card.Size = new Size(860, 96);
            var info = new Label { Location = new Point(18, 14), AutoSize = true, Font = Theme.Base };
            var title = new Label { Location = new Point(18, 14), AutoSize = true, Font = Theme.H2, Text = Environment.MachineName };
            info.Location = new Point(18, 40);
            info.Text = SystemSummary();
            card.Controls.Add(title);
            card.Controls.Add(info);
            return card;
        }

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
                    os += " (build " + build + ", " + (Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits") + ")";
                }
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    cpu = (Util.ReadString(k, "ProcessorNameString") ?? "").Trim();
            }
            catch { }
            string ram = "";
            try
            {
                var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
                ram = Util.FormatSize((long)ci.TotalPhysicalMemory) + " de RAM (" + Util.FormatSize((long)ci.AvailablePhysicalMemory) + " livres)";
            }
            catch { }
            return os + "\n" + cpu + " · " + Environment.ProcessorCount + " núcleos lógicos · " + ram;
        }

        Card MakeCard(Control parent, string iconName, string title, string action, Action onClick)
        {
            var p = Theme.MakeCard();
            p.Size = new Size(200, 180);
            p.Margin = new Padding(0, 0, 14, 14);
            var icon = new IconBox(iconName, 22, Theme.Accent) { Location = new Point(16, 17) };
            var t = new Label { Text = title, Font = Theme.Bold, Location = new Point(46, 20), AutoSize = true };
            var value = new Label { Text = "—", Font = Theme.Big, Location = new Point(14, 52), AutoSize = true };
            var detail = new Label { Text = "", ForeColor = Theme.Muted, Location = new Point(17, 96), Size = new Size(170, 34) };
            var btn = Theme.MakeButton(action);
            btn.Location = new Point(16, 132);
            btn.Visible = false;
            btn.Click += (s, e) => onClick();
            p.Controls.AddRange(new Control[] { icon, t, value, detail, btn });
            parent.Controls.Add(p);
            return new Card { Panel = p, Value = value, Detail = detail, Action = btn };
        }

        void UpdateDiskCard()
        {
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
                double freePct = 100.0 * d.AvailableFreeSpace / d.TotalSize;
                disk.Value.Text = Util.FormatSize(d.AvailableFreeSpace);
                disk.Detail.Text = "livres em " + d.Name.TrimEnd('\\') + " (" + freePct.ToString("0") + "% de " + Util.FormatSize(d.TotalSize) + ")";
                disk.Value.ForeColor = freePct < 10 ? Theme.Danger : Theme.Text;
                disk.Action.Visible = true;
            }
            catch { }
        }

        void Check()
        {
            btnCheck.Enabled = false;
            foreach (var c in new[] { junk, registry, startup, updates }) { c.Value.Text = "..."; c.Detail.Text = ""; c.Action.Visible = false; c.Value.ForeColor = Theme.Text; }
            var engine = new CleanerEngine(Main.Settings);
            List<RuleResult> results = new List<RuleResult>();
            List<RegIssue> issues = null;
            int startupOn = 0, startupTotal = 0;
            List<PackageUpdate> outdated = null;
            string wingetVersion;

            RunBackground(() =>
            {
                var rules = Main.DetectedRules().Where(r => Main.Settings.IsRuleOn(r)).ToList();
                foreach (var r in rules)
                {
                    Ui(() => status.Text = "Analisando: " + r.FullName);
                    results.Add(engine.Analyze(r));
                }
                Ui(() => status.Text = "Verificando o registro...");
                var cats = RegistryScanner.Categories.Where(c => !Main.Settings.RegistryDisabled.Contains(c));
                issues = new RegistryScanner().Scan(cats);
                var st = Startup.List();
                startupOn = st.Count(e => e.Enabled);
                startupTotal = st.Count;
                if (Winget.IsAvailable(out wingetVersion))
                {
                    Ui(() => status.Text = "Procurando atualizações de programas...");
                    try { outdated = Winget.ListUpgrades(); }
                    catch { }
                }
            }, () =>
            {
                btnCheck.Enabled = true;
                junkResults = results.Where(r => r.ItemCount > 0).ToList();
                long bytes = junkResults.Sum(r => r.Bytes);
                junk.Value.Text = Util.FormatSize(bytes);
                junk.Detail.Text = junkResults.Sum(r => r.ItemCount) + " itens em " + junkResults.Count + " categorias";
                junk.Action.Visible = junkResults.Count > 0;

                registry.Value.Text = issues.Count.ToString();
                registry.Detail.Text = issues.Count == 0 ? "nenhum problema" : "entradas órfãs encontradas";
                registry.Action.Visible = issues.Count > 0;

                startup.Value.Text = startupOn.ToString();
                startup.Detail.Text = "de " + startupTotal + " programas abrem com o Windows";
                startup.Value.ForeColor = startupOn > 10 ? Theme.Danger : Theme.Text;
                startup.Action.Visible = startupTotal > 0;

                if (outdated == null)
                {
                    updates.Value.Text = "â";
                    updates.Detail.Text = "winget indisponível";
                }
                else
                {
                    updates.Value.Text = outdated.Count.ToString();
                    updates.Detail.Text = outdated.Count == 0 ? "tudo atualizado" : "com versão mais nova disponível";
                    updates.Action.Visible = outdated.Count > 0;
                }

                UpdateDiskCard();
                status.Text = "Verificação concluída. Revise cada item antes de agir.";
            });
        }

        void CleanJunk()
        {
            if (junkResults == null || junkResults.Count == 0) return;
            var rules = junkResults.Select(r => r.Rule).ToList();
            if (Main.Settings.ConfirmClean &&
                MessageBox.Show(this, "Remover " + junk.Value.Text + " de arquivos desnecessários?", "Limpar agora",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            rules = CleanFlow.HandleRunningApps(this, rules);
            if (rules == null) return;

            junk.Action.Visible = false;
            btnCheck.Enabled = false;
            var engine = new CleanerEngine(Main.Settings);
            long freed = 0;
            int failed = 0;
            RunBackground(() =>
            {
                foreach (var rule in rules)
                {
                    Ui(() => status.Text = "Limpando: " + rule.FullName);
                    var r = engine.Analyze(rule);
                    engine.Clean(r);
                    freed += r.Freed;
                    failed += r.Failed;
                }
            }, () =>
            {
                btnCheck.Enabled = true;
                junk.Value.Text = Util.FormatSize(freed);
                junk.Value.ForeColor = Theme.Good;
                junk.Detail.Text = "liberados" + (failed > 0 ? " · " + failed + " itens em uso ignorados" : "");
                junkResults = null;
                UpdateDiskCard();
                status.Text = "Limpeza concluída.";
            });
        }
    }
}
