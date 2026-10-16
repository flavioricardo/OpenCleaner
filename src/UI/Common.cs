using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FreeCleaner.Core;

namespace FreeCleaner.UI
{
    static class Theme
    {
        public static readonly Color Sidebar = Color.FromArgb(22, 28, 40);
        public static readonly Color SidebarHover = Color.FromArgb(34, 43, 60);
        public static readonly Color SidebarActive = Color.FromArgb(42, 54, 76);
        public static readonly Color SidebarText = Color.FromArgb(205, 213, 225);
        public static readonly Color Accent = Color.FromArgb(0, 150, 136);
        public static readonly Color AccentHover = Color.FromArgb(0, 128, 116);
        public static readonly Color Background = Color.FromArgb(244, 246, 249);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(222, 226, 232);
        public static readonly Color Text = Color.FromArgb(33, 37, 41);
        public static readonly Color Muted = Color.FromArgb(108, 117, 125);
        public static readonly Color Danger = Color.FromArgb(200, 50, 60);
        public static readonly Color Good = Color.FromArgb(25, 135, 84);

        public static readonly Font Base = new Font("Segoe UI", 9.5f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 17f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 11.5f);
        public static readonly Font Big = new Font("Segoe UI Light", 22f);
        public static readonly Font Icons = new Font("Segoe MDL2 Assets", 12f);

        public static Button MakeButton(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(110, 34),
                Padding = new Padding(10, 0, 10, 0),
                FlatStyle = FlatStyle.Flat,
                Font = Bold,
                Cursor = Cursors.Hand,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Text,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(236, 240, 244);
            return b;
        }

        public static ListView MakeList(bool checkboxes = false)
        {
            var lv = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                CheckBoxes = checkboxes,
                BorderStyle = BorderStyle.None,
                Font = Base,
                Dock = DockStyle.Fill
            };
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                           .SetValue(lv, true, null);
            return lv;
        }

        /// <summary>Painel com borda fina, usado como "cartão".</summary>
        public static Panel MakeCard()
        {
            var p = new Panel { BackColor = Card, Padding = new Padding(1) };
            p.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }

        /// <summary>Barra de botões alinhada à direita (ou à esquerda).</summary>
        public static FlowLayoutPanel MakeButtonBar(bool rightToLeft = true)
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                Padding = new Padding(0, 9, 0, 0),
                BackColor = Background,
                WrapContents = false
            };
        }

        /// <summary>Ordena um ListView ao clicar no cabeçalho, usando Tag (long/DateTime) quando houver.</summary>
        public static void EnableSorting(ListView lv)
        {
            int col = -1;
            bool asc = true;
            lv.ColumnClick += (s, e) =>
            {
                asc = e.Column == col ? !asc : true;
                col = e.Column;
                lv.ListViewItemSorter = new Sorter(col, asc);
            };
        }

        class Sorter : System.Collections.IComparer
        {
            readonly int col;
            readonly bool asc;
            public Sorter(int col, bool asc) { this.col = col; this.asc = asc; }

            public int Compare(object x, object y)
            {
                var a = ((ListViewItem)x).SubItems[col];
                var b = ((ListViewItem)y).SubItems[col];
                int r;
                if (a.Tag is IComparable && b.Tag != null && a.Tag.GetType() == b.Tag.GetType())
                    r = ((IComparable)a.Tag).CompareTo(b.Tag);
                else
                    r = string.Compare(a.Text, b.Text, StringComparison.CurrentCultureIgnoreCase);
                return asc ? r : -r;
            }
        }

        public static ListViewItem.ListViewSubItem SizeCell(ListViewItem item, long bytes)
        {
            var sub = new ListViewItem.ListViewSubItem(item, Util.FormatSize(bytes)) { Tag = bytes };
            item.SubItems.Add(sub);
            return sub;
        }
    }

    /// <summary>Base das páginas: cabeçalho com título e subtítulo.</summary>
    class Page : UserControl
    {
        protected readonly MainForm Main;
        protected readonly Panel Body;

        public Page(MainForm main, string title, string subtitle)
        {
            Main = main;
            BackColor = Theme.Background;
            Font = Theme.Base;
            ForeColor = Theme.Text;
            Padding = new Padding(28, 18, 28, 14);

            Body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            Controls.Add(Body);

            var header = new Panel { Dock = DockStyle.Top, Height = 70 };
            header.Controls.Add(new Label { Text = subtitle, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(1, 40) });
            header.Controls.Add(new Label { Text = title, Font = Theme.Title, AutoSize = true, Location = new Point(-2, 2) });
            Controls.Add(header);
        }

        /// <summary>Chamado sempre que a página é exibida.</summary>
        public virtual void Activated() { }

        protected void Ui(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); }
            catch (InvalidOperationException) { }
        }

        /// <summary>Executa trabalho numa thread de fundo e chama done na thread da interface.</summary>
        protected void RunBackground(Action work, Action done)
        {
            var t = new Thread(() =>
            {
                Exception error = null;
                try { work(); }
                catch (Exception ex) { error = ex; }
                Ui(() =>
                {
                    if (error != null)
                        MessageBox.Show(this, error.Message, "FreeCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    done();
                });
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }

    static class CleanFlow
    {
        /// <summary>
        /// Avisa sobre programas abertos que travam arquivos. Retorna as regras a executar
        /// (sem as dos programas que o usuário preferiu pular) ou null se cancelado.
        /// </summary>
        public static List<CleanRule> HandleRunningApps(IWin32Window owner, List<CleanRule> rules)
        {
            var running = rules.Where(r => r.Processes.Count > 0)
                               .GroupBy(r => r.Group)
                               .Where(g => Util.FindProcesses(g.First().Processes).Count > 0)
                               .ToList();
            if (running.Count == 0) return rules;

            string names = string.Join("\n", running.Select(g => "  • " + g.Key));
            var answer = MessageBox.Show(owner,
                "Estes programas estão abertos e travam alguns dos arquivos a limpar:\n\n" + names +
                "\n\nSim: fechar esses programas e continuar\nNão: pular os itens desses programas\nCancelar: voltar",
                "Programas em execução", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel) return null;

            var groups = new HashSet<string>(running.Select(g => g.Key));
            if (answer == DialogResult.No) return rules.Where(r => !groups.Contains(r.Group)).ToList();

            Cursor.Current = Cursors.WaitCursor;
            foreach (var g in running)
                foreach (var p in Util.FindProcesses(g.First().Processes))
                {
                    try
                    {
                        if (!p.CloseMainWindow() || !p.WaitForExit(5000))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    catch { }
                }
            Cursor.Current = Cursors.Default;
            return rules;
        }
    }

    /// <summary>Lista detalhada dos arquivos/chaves de uma regra.</summary>
    class DetailsForm : Form
    {
        public DetailsForm(RuleResult r)
        {
            Text = r.Rule.FullName;
            Size = new Size(860, 520);
            StartPosition = FormStartPosition.CenterParent;
            Font = Theme.Base;
            ShowIcon = false;
            MinimizeBox = false;

            var lv = Theme.MakeList();
            lv.Columns.Add("Local", 640);
            lv.Columns.Add("Tamanho", 120, HorizontalAlignment.Right);
            Theme.EnableSorting(lv);
            Controls.Add(lv);

            var info = new Label { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
            Controls.Add(info);

            const int limit = 20000;
            lv.BeginUpdate();
            for (int i = 0; i < r.Files.Count && i < limit; i++)
            {
                var it = new ListViewItem(r.Files[i]) { Tag = r.Files[i] };
                Theme.SizeCell(it, r.Sizes[i]);
                lv.Items.Add(it);
            }
            foreach (var rk in r.Regs)
                lv.Items.Add(new ListViewItem(new[] { rk.Key + (rk.Value != null ? "  [" + rk.Value + "]" : ""), "Registro" }));
            if (r.SpecialItems > 0)
            {
                var it = new ListViewItem(r.Rule.Name + " (" + r.SpecialItems + " itens)");
                Theme.SizeCell(it, r.Bytes - r.Sizes.Sum());
                lv.Items.Add(it);
            }
            lv.EndUpdate();

            info.Text = r.ItemCount + " itens, " + Util.FormatSize(r.Bytes) +
                        (r.Files.Count > limit ? "  (mostrando os primeiros " + limit + ")" : "") +
                        "   ·   Clique duplo para abrir o local";
            lv.DoubleClick += (s, e) =>
            {
                if (lv.SelectedItems.Count > 0 && lv.SelectedItems[0].Tag is string)
                    Util.ShowInExplorer((string)lv.SelectedItems[0].Tag);
            };
        }
    }

    /// <summary>Caixa de texto simples.</summary>
    static class Prompt
    {
        public static string Folder(IWin32Window owner, string description, string initial = null)
        {
            using (var d = new FolderBrowserDialog { Description = description, ShowNewFolderButton = false })
            {
                if (!string.IsNullOrEmpty(initial)) d.SelectedPath = initial;
                return d.ShowDialog(owner) == DialogResult.OK ? d.SelectedPath : null;
            }
        }
    }
}
