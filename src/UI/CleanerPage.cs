using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FreeCleaner.Core;

namespace FreeCleaner.UI
{
    class CleanerPage : Page
    {
        static readonly string[] CategoryOrder = { "Windows", "Navegadores", "Aplicativos", "winapp2.ini" };

        readonly TreeView tree;
        readonly ListView results;
        readonly Label status;
        readonly ProgressBar progress;
        readonly Button btnAnalyze, btnClean, btnStop;
        CleanerEngine engine;
        bool updating, busy, treeLoaded;

        public CleanerPage(MainForm main)
            : base(main, "Limpeza personalizada", "Escolha o que limpar, analise para ver o espaço recuperável e execute a limpeza.")
        {
            // ---- Painel da direita: status + resultados + botões ----
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 0, 0) };
            Body.Controls.Add(right);

            var card = Theme.MakeCard();
            card.Dock = DockStyle.Fill;
            right.Controls.Add(card);

            results = Theme.MakeList();
            results.Columns.Add("Item", 320);
            results.Columns.Add("Itens", 80, HorizontalAlignment.Right);
            results.Columns.Add("Tamanho", 100, HorizontalAlignment.Right);
            results.Columns.Add("Observação", 240);
            Theme.EnableSorting(results);
            results.DoubleClick += (s, e) => ShowDetails();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Ver arquivos", null, (s, e) => ShowDetails());
            menu.Items.Add("Limpar somente este item", null, (s, e) => CleanSelectedOnly());
            results.ContextMenuStrip = menu;
            card.Controls.Add(results);

            var top = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(0, 0, 0, 12) };
            status = new Label { Dock = DockStyle.Fill, Font = Theme.H2, TextAlign = ContentAlignment.MiddleLeft, Text = "Clique em Analisar para começar." };
            progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 8, Style = ProgressBarStyle.Continuous };
            top.Controls.Add(status);
            top.Controls.Add(progress);
            right.Controls.Add(top);

            var bar = Theme.MakeButtonBar();
            btnClean = Theme.MakeButton("Executar limpeza", true);
            btnAnalyze = Theme.MakeButton("Analisar");
            btnStop = Theme.MakeButton("Parar");
            btnStop.Visible = false;
            btnClean.Click += (s, e) => Run(CheckedRules(), true);
            btnAnalyze.Click += (s, e) => Run(CheckedRules(), false);
            btnStop.Click += (s, e) => { if (engine != null) engine.Cancel = true; };
            bar.Controls.AddRange(new Control[] { btnClean, btnAnalyze, btnStop });
            right.Controls.Add(bar);

            // ---- Árvore de regras à esquerda ----
            var left = Theme.MakeCard();
            left.Dock = DockStyle.Left;
            left.Width = 330;
            tree = new TreeView
            {
                Dock = DockStyle.Fill, CheckBoxes = true, BorderStyle = BorderStyle.None, ShowLines = false,
                ItemHeight = 24, Font = Theme.Base, FullRowSelect = true, HotTracking = false, Indent = 18, ShowNodeToolTips = true
            };
            tree.AfterCheck += Tree_AfterCheck;
            var tmenu = new ContextMenuStrip();
            tmenu.Items.Add("Restaurar seleção padrão", null, (s, e) =>
            {
                foreach (var r in Main.Rules) Main.Settings.Rules.Remove(r.Id);
                Main.Settings.Save();
                ReloadTree();
            });
            tmenu.Items.Add("Expandir tudo", null, (s, e) => tree.ExpandAll());
            tmenu.Items.Add("Recolher tudo", null, (s, e) => tree.CollapseAll());
            tree.ContextMenuStrip = tmenu;
            left.Controls.Add(tree);
            Body.Controls.Add(left);
        }

        public override void Activated()
        {
            if (!treeLoaded) ReloadTree();
        }

        public void ReloadTree()
        {
            treeLoaded = true;
            tree.Nodes.Clear();
            tree.Nodes.Add("Carregando regras...");
            List<CleanRule> rules = null;
            RunBackground(() => rules = Main.DetectedRules(), () => FillTree(rules));
        }

        void FillTree(List<CleanRule> rules)
        {
            updating = true;
            tree.BeginUpdate();
            tree.Nodes.Clear();
            var cats = rules.Select(r => r.Category).Distinct()
                            .OrderBy(c => { int i = Array.IndexOf(CategoryOrder, c); return i < 0 ? 99 : i; }).ToList();
            foreach (var cat in cats)
            {
                var catNode = tree.Nodes.Add(cat);
                catNode.NodeFont = Theme.Bold;
                var inCat = rules.Where(r => r.Category == cat).ToList();
                var groups = inCat.Select(r => r.Group).Distinct().ToList();
                if (cat != "Windows") groups.Sort(StringComparer.CurrentCultureIgnoreCase);
                foreach (var g in groups)
                {
                    var gNode = catNode.Nodes.Add(g);
                    foreach (var r in inCat.Where(r => r.Group == g))
                    {
                        var n = gNode.Nodes.Add(r.Name);
                        n.Tag = r;
                        n.Checked = Main.Settings.IsRuleOn(r);
                        if (r.Warning != null) n.ToolTipText = r.Warning;
                    }
                    gNode.Checked = gNode.Nodes.Cast<TreeNode>().Any(n => n.Checked);
                    if (cat == "Windows") gNode.Expand();
                }
                catNode.Checked = catNode.Nodes.Cast<TreeNode>().Any(n => n.Checked);
                if (cat != "winapp2.ini") catNode.Expand();
            }
            tree.EndUpdate();
            if (tree.Nodes.Count > 0) tree.Nodes[0].EnsureVisible();
            updating = false;
        }

        void Tree_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (updating) return;
            updating = true;
            try
            {
                var rule = e.Node.Tag as CleanRule;
                if (rule != null)
                {
                    if (e.Node.Checked && rule.Warning != null &&
                        MessageBox.Show(this, rule.Warning + "\n\nMarcar este item?", rule.FullName,
                                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        e.Node.Checked = false;
                        return;
                    }
                    Main.Settings.SetRule(rule, e.Node.Checked);
                }
                else
                {
                    SetChildren(e.Node, e.Node.Checked);
                }
                for (var p = e.Node.Parent; p != null; p = p.Parent)
                    p.Checked = p.Nodes.Cast<TreeNode>().Any(n => n.Checked);
                Main.Settings.Save();
            }
            finally { updating = false; }
        }

        void SetChildren(TreeNode node, bool on)
        {
            foreach (TreeNode child in node.Nodes)
            {
                var rule = child.Tag as CleanRule;
                if (rule != null)
                {
                    // Itens com aviso (senhas, cookies...) só são marcados individualmente.
                    if (on && rule.Warning != null) continue;
                    child.Checked = on;
                    Main.Settings.SetRule(rule, on);
                }
                else
                {
                    SetChildren(child, on);
                    child.Checked = child.Nodes.Cast<TreeNode>().Any(n => n.Checked);
                }
            }
        }

        List<CleanRule> CheckedRules()
        {
            var list = new List<CleanRule>();
            Action<TreeNodeCollection> walk = null;
            walk = nodes =>
            {
                foreach (TreeNode n in nodes)
                {
                    var r = n.Tag as CleanRule;
                    if (r != null && n.Checked) list.Add(r);
                    walk(n.Nodes);
                }
            };
            walk(tree.Nodes);
            return list;
        }

        void ShowDetails()
        {
            if (results.SelectedItems.Count == 0) return;
            var r = results.SelectedItems[0].Tag as RuleResult;
            if (r != null)
                using (var f = new DetailsForm(r)) f.ShowDialog(this);
        }

        void CleanSelectedOnly()
        {
            if (results.SelectedItems.Count == 0) return;
            var r = results.SelectedItems[0].Tag as RuleResult;
            if (r != null) Run(new List<CleanRule> { r.Rule }, true);
        }

        void SetBusy(bool on)
        {
            busy = on;
            btnAnalyze.Enabled = btnClean.Enabled = !on;
            tree.Enabled = !on;
            btnStop.Visible = on;
        }

        void Run(List<CleanRule> rules, bool clean)
        {
            if (busy) return;
            if (rules.Count == 0)
            {
                status.Text = "Nenhum item selecionado.";
                return;
            }
            if (clean)
            {
                if (Main.Settings.ConfirmClean &&
                    MessageBox.Show(this, "Os arquivos dos " + rules.Count + " itens selecionados serão removidos permanentemente.\n\nContinuar?",
                                    "Executar limpeza", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                rules = CleanFlow.HandleRunningApps(this, rules);
                if (rules == null) return;
            }

            SetBusy(true);
            results.Items.Clear();
            results.Groups.Clear();
            status.ForeColor = Theme.Text;
            progress.Value = 0;
            progress.Maximum = Math.Max(1, rules.Count);
            engine = new CleanerEngine(Main.Settings);
            var eng = engine;
            var all = new List<RuleResult>();
            var sw = Stopwatch.StartNew();

            RunBackground(() =>
            {
                for (int i = 0; i < rules.Count && !eng.Cancel; i++)
                {
                    var rule = rules[i];
                    int index = i;
                    Ui(() =>
                    {
                        status.Text = (clean ? "Limpando: " : "Analisando: ") + rule.FullName;
                        progress.Value = index;
                    });
                    var res = eng.Analyze(rule);
                    if (rule.Processes.Count > 0 && Util.FindProcesses(rule.Processes).Count > 0 && res.Note == null)
                        res.Note = "Programa em execução: alguns arquivos podem estar em uso";
                    if (clean) eng.Clean(res);
                    all.Add(res);
                    Ui(() => AddRow(res, clean));
                }
            }, () =>
            {
                SetBusy(false);
                progress.Value = progress.Maximum;
                string secs = sw.Elapsed.TotalSeconds.ToString("0.0") + " s";
                string prefix = eng.Cancel ? "Interrompido" : clean ? "Limpeza concluída" : "Análise concluída";
                if (clean)
                {
                    long freed = all.Sum(r => r.Freed);
                    int failed = all.Sum(r => r.Failed);
                    status.Text = prefix + " (" + secs + "): " + Util.FormatSize(freed) + " liberados" +
                                  (failed > 0 ? " · " + failed + " itens em uso foram ignorados" : "");
                    status.ForeColor = Theme.Good;
                }
                else
                {
                    long bytes = all.Sum(r => r.Bytes);
                    int items = all.Sum(r => r.ItemCount);
                    status.Text = prefix + " (" + secs + "): " + Util.FormatSize(bytes) + " a remover em " + items + " itens";
                }
                if (results.Items.Count == 0)
                    results.Items.Add(new ListViewItem("Nada a limpar nos itens selecionados."));
            });
        }

        void AddRow(RuleResult r, bool clean)
        {
            if (r.ItemCount == 0 && r.Failed == 0 && r.Note == null) return;
            ListViewGroup group = results.Groups.Cast<ListViewGroup>().FirstOrDefault(g => g.Header == r.Rule.Category);
            if (group == null) group = results.Groups.Add(r.Rule.Category, r.Rule.Category);
            var it = new ListViewItem(r.Rule.FullName, group) { Tag = r };
            var count = clean ? r.Removed : r.ItemCount;
            it.SubItems.Add(new ListViewItem.ListViewSubItem(it, count.ToString()) { Tag = (long)count });
            Theme.SizeCell(it, clean ? r.Freed : r.Bytes);
            string note = r.Note ?? "";
            if (clean && r.Failed > 0) note = r.Failed + " em uso ou sem permissão" + (note.Length > 0 ? " · " + note : "");
            it.SubItems.Add(note);
            if (r.Failed > 0 || r.Note != null) it.ForeColor = Color.FromArgb(150, 90, 0);
            results.Items.Add(it);
        }
    }
}
