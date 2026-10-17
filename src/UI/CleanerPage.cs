using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    class CleanerPage : Page
    {
        static readonly string[] CategoryOrder = { "Windows", "Navegadores", "Aplicativos", "winapp2.ini" };
        const int Off = 0, On = 1, Mixed = 2;

        readonly TreeView tree;
        readonly ListCard results;
        readonly Button btnAnalyze, btnClean, btnStop;
        CleanerEngine engine;
        bool busy, treeLoaded;

        public CleanerPage(MainForm main)
            : base(main, "Limpeza personalizada", "Escolha o que limpar, analise para ver quanto espaço volta e então execute a limpeza.")
        {
            btnClean = AddAction("Executar limpeza", ButtonKind.Primary, "brush-cleaning", () => Run(CheckedRules(), true));
            btnAnalyze = AddAction("Analisar", ButtonKind.Secondary, "search", () => Run(CheckedRules(), false));
            btnStop = AddAction("Parar", ButtonKind.Secondary, "square", () => { if (engine != null) engine.Cancel = true; });
            btnStop.Visible = false;
            SetStatus("Nada foi analisado ainda.");

            // ---- Resultados ----
            var right = new Panel { Dock = DockStyle.Fill, Padding = Theme.Pad(16, 0, 0, 0) };
            var list = Theme.MakeList();
            Theme.Col(list, "Item", 290);
            Theme.Col(list, "Itens", 70, HorizontalAlignment.Right);
            Theme.Col(list, "Tamanho", 100, HorizontalAlignment.Right);
            Theme.Col(list, "Observação", 200);
            Theme.EnableSorting(list);
            list.DoubleClick += (s, e) => ShowDetails();
            var menu = new ContextMenuStrip { Font = Theme.Base };
            menu.Items.Add("Ver arquivos", null, (s, e) => ShowDetails());
            menu.Items.Add("Limpar somente este item", null, (s, e) => CleanSelectedOnly());
            list.ContextMenuStrip = menu;
            results = new ListCard(list) { Dock = DockStyle.Fill };
            results.SetEmpty("Marque à esquerda o que deseja limpar e clique em Analisar.\nNada é apagado na análise.", "brush-cleaning");
            right.Controls.Add(results);
            Body.Controls.Add(right);

            // ---- Árvore de itens (três estados: marcado, desmarcado, parcial) ----
            var left = Theme.MakeCard();
            left.Dock = DockStyle.Left;
            left.Width = Theme.S(330);
            left.Padding = Theme.Pad(1, 6, 1, 1);
            tree = new TreeView
            {
                Dock = DockStyle.Fill, CheckBoxes = false, BorderStyle = BorderStyle.None, ShowLines = false,
                ItemHeight = Theme.S(26), Font = Theme.Base, ForeColor = Theme.Tinta, FullRowSelect = true,
                HotTracking = false, Indent = Theme.S(18), ShowNodeToolTips = true, StateImageList = CheckImages()
            };
            tree.NodeMouseClick += (s, e) =>
            {
                if (tree.HitTest(e.Location).Location == TreeViewHitTestLocations.StateImage) Toggle(e.Node);
            };
            tree.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space && tree.SelectedNode != null) { Toggle(tree.SelectedNode); e.Handled = true; }
            };
            var tmenu = new ContextMenuStrip { Font = Theme.Base };
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

        /// <summary>Caixas de seleção da identidade: borda Grafite claro, marcada em Verde-limpo.</summary>
        static ImageList CheckImages()
        {
            int s = Theme.S(16);
            var il = new ImageList { ImageSize = new Size(s, s), ColorDepth = ColorDepth.Depth32Bit };
            for (int state = 0; state < 3; state++)
            {
                var bmp = new Bitmap(s, s);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var box = new RectangleF(1.5f, 1.5f, s - 3, s - 3);
                    using (var path = Theme.Rounded(box, Theme.S(3f)))
                    {
                        if (state == Off)
                        {
                            using (var b = new SolidBrush(Color.White)) g.FillPath(b, path);
                            using (var p = new Pen(Color.FromArgb(0x8A, 0x93, 0x9E), Theme.S(1.2f))) g.DrawPath(p, path);
                        }
                        else
                        {
                            using (var b = new SolidBrush(Theme.Verde)) g.FillPath(b, path);
                            using (var p = new Pen(Color.White, Theme.S(1.8f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                            {
                                if (state == On)
                                    g.DrawLines(p, new[] { new PointF(s * 0.28f, s * 0.52f), new PointF(s * 0.44f, s * 0.68f), new PointF(s * 0.73f, s * 0.36f) });
                                else
                                    g.DrawLine(p, s * 0.3f, s * 0.5f, s * 0.7f, s * 0.5f);
                            }
                        }
                    }
                }
                il.Images.Add(bmp);
            }
            return il;
        }

        public override void Activated()
        {
            if (!treeLoaded) ReloadTree();
        }

        public void ReloadTree()
        {
            treeLoaded = true;
            tree.Nodes.Clear();
            tree.Nodes.Add("Carregando itens...");
            List<CleanRule> rules = null;
            RunBackground(() => rules = Main.DetectedRules(), () => FillTree(rules));
        }

        void FillTree(List<CleanRule> rules)
        {
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
                        n.StateImageIndex = Main.Settings.IsRuleOn(r) ? On : Off;
                        if (r.Warning != null) n.ToolTipText = r.Warning;
                    }
                    gNode.StateImageIndex = StateOf(gNode);
                    if (cat == "Windows") gNode.Expand();
                }
                catNode.StateImageIndex = StateOf(catNode);
                if (cat != "winapp2.ini") catNode.Expand();
            }
            tree.EndUpdate();
            if (tree.Nodes.Count > 0) tree.Nodes[0].EnsureVisible();
        }

        static int StateOf(TreeNode node)
        {
            if (node.Nodes.Count == 0) return node.StateImageIndex == On ? On : Off;
            bool any = false, all = true;
            foreach (TreeNode c in node.Nodes)
            {
                int st = c.StateImageIndex;
                if (st != Off) any = true;
                if (st != On) all = false;
            }
            return all ? On : any ? Mixed : Off;
        }

        void Toggle(TreeNode node)
        {
            if (busy) return;
            bool on = node.StateImageIndex != On;
            var rule = node.Tag as CleanRule;
            if (rule != null)
            {
                if (on && rule.Warning != null &&
                    MessageBox.Show(this, rule.Warning + "\n\nMarcar este item?", rule.FullName,
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                node.StateImageIndex = on ? On : Off;
                Main.Settings.SetRule(rule, on);
            }
            else SetBranch(node, on);
            for (var p = node.Parent; p != null; p = p.Parent) p.StateImageIndex = StateOf(p);
            Main.Settings.Save();
        }

        void SetBranch(TreeNode node, bool on)
        {
            foreach (TreeNode child in node.Nodes)
            {
                var rule = child.Tag as CleanRule;
                if (rule != null)
                {
                    // Itens com aviso (senhas, cookies...) só são marcados um a um.
                    if (on && rule.Warning != null) continue;
                    child.StateImageIndex = on ? On : Off;
                    Main.Settings.SetRule(rule, on);
                }
                else SetBranch(child, on);
            }
            node.StateImageIndex = StateOf(node);
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
                    if (r != null && n.StateImageIndex == On) list.Add(r);
                    walk(n.Nodes);
                }
            };
            walk(tree.Nodes);
            return list;
        }

        void ShowDetails()
        {
            if (results.List.SelectedItems.Count == 0) return;
            var r = results.List.SelectedItems[0].Tag as RuleResult;
            if (r != null)
                using (var f = new DetailsForm(r)) f.ShowDialog(this);
        }

        void CleanSelectedOnly()
        {
            if (results.List.SelectedItems.Count == 0) return;
            var r = results.List.SelectedItems[0].Tag as RuleResult;
            if (r != null) Run(new List<CleanRule> { r.Rule }, true);
        }

        void SetBusy(bool on)
        {
            busy = on;
            btnAnalyze.Visible = btnClean.Visible = !on;
            btnStop.Visible = on;
            tree.Enabled = !on;
        }

        void Run(List<CleanRule> rules, bool clean)
        {
            if (busy) return;
            if (rules.Count == 0)
            {
                SetStatus("Marque ao menos um item na lista à esquerda.", StatusKind.Warning);
                return;
            }
            if (clean)
            {
                if (Main.Settings.ConfirmClean &&
                    MessageBox.Show(this, "Os arquivos dos " + rules.Count + " itens marcados serão apagados permanentemente.\n\nExecutar a limpeza?",
                                    "Executar limpeza", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                rules = CleanFlow.HandleRunningApps(this, rules);
                if (rules == null) return;
            }

            SetBusy(true);
            var list = results.List;
            list.Items.Clear();
            list.Groups.Clear();
            results.SetEmpty(null);
            Progress.Start(rules.Count);
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
                        SetStatus((clean ? "Limpando " : "Analisando ") + rule.FullName + "...");
                        Progress.Value = index;
                    });
                    var res = eng.Analyze(rule);
                    if (rule.Processes.Count > 0 && Util.FindProcesses(rule.Processes).Count > 0 && res.Note == null)
                        res.Note = "Programa aberto: alguns arquivos podem estar em uso";
                    if (clean) eng.Clean(res);
                    all.Add(res);
                    Ui(() => AddRow(res, clean));
                }
            }, () =>
            {
                SetBusy(false);
                Progress.Stop();
                string secs = sw.Elapsed.TotalSeconds.ToString("0.0") + " s";
                if (clean)
                {
                    long freed = all.Sum(r => r.Freed);
                    int failed = all.Sum(r => r.Failed);
                    SetStatus((eng.Cancel ? "Limpeza interrompida: " : "Limpeza concluída: ") + Util.FormatSize(freed) + " liberados em " + secs +
                              (failed > 0 ? " · " + failed + " itens em uso ficaram para trás" : ""), StatusKind.Good);
                }
                else
                {
                    long bytes = all.Sum(r => r.Bytes);
                    int items = all.Sum(r => r.ItemCount);
                    SetStatus((eng.Cancel ? "Análise interrompida: " : "Análise concluída: ") + Util.FormatSize(bytes) +
                              " podem ser liberados (" + items + " itens, " + secs + ")");
                }
                results.SetEmpty(clean ? "Nada restou para limpar nos itens marcados." : "Os itens marcados já estão limpos.", "circle-check");
            });
        }

        void AddRow(RuleResult r, bool clean)
        {
            if (r.ItemCount == 0 && r.Failed == 0 && r.Note == null) return;
            var list = results.List;
            ListViewGroup group = list.Groups.Cast<ListViewGroup>().FirstOrDefault(g => g.Header == r.Rule.Category);
            if (group == null) group = list.Groups.Add(r.Rule.Category, r.Rule.Category);
            var it = new ListViewItem(r.Rule.FullName, group) { Tag = r };
            var count = clean ? r.Removed : r.ItemCount;
            it.SubItems.Add(new ListViewItem.ListViewSubItem(it, count.ToString("N0")) { Tag = (long)count });
            Theme.SizeCell(it, clean ? r.Freed : r.Bytes);
            string note = r.Note ?? "";
            if (clean && r.Failed > 0) note = r.Failed + " em uso ou sem permissão" + (note.Length > 0 ? " · " + note : "");
            it.SubItems.Add(note);
            if (r.Failed > 0 || r.Note != null) it.ForeColor = Theme.Ambar;
            list.Items.Add(it);
        }
    }
}
