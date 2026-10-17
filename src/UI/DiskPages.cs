using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    // ======================= Analisador de disco =======================

    class DiskPage : Page
    {
        static readonly Color[] Palette =
        {
            Color.FromArgb(0, 150, 136), Color.FromArgb(63, 81, 181), Color.FromArgb(233, 30, 99), Color.FromArgb(255, 152, 0),
            Color.FromArgb(121, 85, 72), Color.FromArgb(0, 188, 212), Color.FromArgb(156, 39, 176), Color.FromArgb(158, 158, 158)
        };

        readonly ComboBox drives;
        readonly Button btnScan, btnStop, btnFolder;
        readonly Label status;
        readonly ListView catList, fileList;
        readonly Panel barPanel;
        Dictionary<string, DiskCategory> data;
        DiskAnalyzer analyzer;
        string customFolder;

        public DiskPage(MainForm main)
            : base(main, "Analisador de disco", "Descubra quais tipos de arquivo ocupam mais espaço.")
        {
            var filesCard = Theme.MakeCard();
            filesCard.Dock = DockStyle.Fill;
            fileList = Theme.MakeList();
            fileList.Columns.Add("Arquivo", 300);
            fileList.Columns.Add("Tamanho", 100, HorizontalAlignment.Right);
            fileList.Columns.Add("Modificado", 130);
            fileList.Columns.Add("Pasta", 460);
            Theme.EnableSorting(fileList);
            fileList.DoubleClick += (s, e) => OpenSelected();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir local", null, (s, e) => OpenSelected());
            menu.Items.Add("Mover para a Lixeira", null, (s, e) => RecycleSelected());
            fileList.ContextMenuStrip = menu;
            filesCard.Controls.Add(fileList);
            Body.Controls.Add(filesCard);

            var spacer = new Panel { Dock = DockStyle.Top, Height = 12 };
            Body.Controls.Add(spacer);

            var catCard = Theme.MakeCard();
            catCard.Dock = DockStyle.Top;
            catCard.Height = 236;
            catList = Theme.MakeList();
            catList.Columns.Add("Categoria", 200);
            catList.Columns.Add("Arquivos", 100, HorizontalAlignment.Right);
            catList.Columns.Add("Tamanho", 110, HorizontalAlignment.Right);
            catList.Columns.Add("% do total", 90, HorizontalAlignment.Right);
            catList.SelectedIndexChanged += (s, e) => ShowCategory();
            catCard.Controls.Add(catList);
            barPanel = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.White };
            barPanel.Paint += PaintBar;
            catCard.Controls.Add(barPanel);
            Body.Controls.Add(catCard);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false };
            top.Controls.Add(new Label { Text = "Unidade:", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
            drives = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(0, 5, 8, 0) };
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)))
                drives.Items.Add(new DriveItem(d));
            if (drives.Items.Count > 0) drives.SelectedIndex = 0;
            drives.SelectedIndexChanged += (s, e) => customFolder = null;
            btnFolder = Theme.MakeButton("Escolher pasta...", false, "folder-open");
            btnFolder.Click += (s, e) =>
            {
                var f = Prompt.Folder(this, "Pasta a analisar");
                if (f != null) { customFolder = f; Scan(); }
            };
            btnScan = Theme.MakeButton("Analisar", true, "search");
            btnScan.Click += (s, e) => Scan();
            btnStop = Theme.MakeButton("Parar", false, "square");
            btnStop.Visible = false;
            btnStop.Click += (s, e) => { if (analyzer != null) analyzer.Cancel = true; };
            status = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(8, 9, 0, 0) };
            top.Controls.AddRange(new Control[] { drives, btnFolder, btnScan, btnStop, status });
            foreach (Control c in new Control[] { btnFolder, btnScan, btnStop }) c.Margin = new Padding(0, 2, 8, 0);
            Body.Controls.Add(top);
        }

        class DriveItem
        {
            public readonly DriveInfo Drive;
            public DriveItem(DriveInfo d) { Drive = d; }
            public override string ToString()
            {
                string label = string.IsNullOrEmpty(Drive.VolumeLabel) ? "Disco local" : Drive.VolumeLabel;
                return Drive.Name.TrimEnd('\\') + " " + label + " (" + Util.FormatSize(Drive.AvailableFreeSpace) + " livres de " + Util.FormatSize(Drive.TotalSize) + ")";
            }
        }

        void Scan()
        {
            string root = customFolder ?? (drives.SelectedItem is DriveItem ? ((DriveItem)drives.SelectedItem).Drive.RootDirectory.FullName : null);
            if (root == null) return;
            btnScan.Enabled = btnFolder.Enabled = drives.Enabled = false;
            btnStop.Visible = true;
            catList.Items.Clear();
            fileList.Items.Clear();
            data = null;
            barPanel.Invalidate();
            analyzer = new DiskAnalyzer();
            var a = analyzer;
            var timer = new Timer { Interval = 250 };
            timer.Tick += (s, e) => status.Text = a.ScannedFiles.ToString("N0") + " arquivos · " + a.CurrentDir;
            timer.Start();
            Dictionary<string, DiskCategory> result = null;
            RunBackground(() => result = a.Scan(root), () =>
            {
                timer.Dispose();
                btnScan.Enabled = btnFolder.Enabled = drives.Enabled = true;
                btnStop.Visible = false;
                data = result;
                long total = result.Values.Sum(c => c.Bytes);
                status.Text = (a.Cancel ? "Interrompido: " : "") + a.ScannedFiles.ToString("N0") + " arquivos, " + Util.FormatSize(total) + " em " + root;
                catList.BeginUpdate();
                int i = 0;
                foreach (var name in DiskAnalyzer.CategoryNames)
                {
                    var c = result[name];
                    var it = new ListViewItem(c.Name) { Tag = c, ForeColor = Palette[i++ % Palette.Length] };
                    it.SubItems.Add(c.Count.ToString("N0"));
                    it.SubItems.Add(Util.FormatSize(c.Bytes));
                    it.SubItems.Add(total == 0 ? "0%" : (100.0 * c.Bytes / total).ToString("0.0") + "%");
                    catList.Items.Add(it);
                }
                catList.EndUpdate();
                barPanel.Invalidate();
                if (catList.Items.Count > 0) catList.Items[0].Selected = true;
            });
        }

        void PaintBar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var rect = new Rectangle(8, 6, barPanel.Width - 16, barPanel.Height - 10);
            using (var b = new SolidBrush(Color.FromArgb(235, 238, 242))) g.FillRectangle(b, rect);
            if (data == null) return;
            long total = data.Values.Sum(c => c.Bytes);
            if (total == 0) return;
            float x = rect.X;
            int i = 0;
            foreach (var name in DiskAnalyzer.CategoryNames)
            {
                float w = rect.Width * (float)data[name].Bytes / total;
                using (var b = new SolidBrush(Palette[i++ % Palette.Length])) g.FillRectangle(b, x, rect.Y, w, rect.Height);
                x += w;
            }
        }

        void ShowCategory()
        {
            if (catList.SelectedItems.Count == 0) return;
            var c = (DiskCategory)catList.SelectedItems[0].Tag;
            fileList.BeginUpdate();
            fileList.Items.Clear();
            foreach (var f in c.Files.OrderByDescending(f => f.Size))
            {
                var it = new ListViewItem(Path.GetFileName(f.Path)) { Tag = f.Path };
                Theme.SizeCell(it, f.Size);
                it.SubItems.Add(new ListViewItem.ListViewSubItem(it, f.Modified.ToString("dd/MM/yyyy HH:mm")) { Tag = f.Modified });
                it.SubItems.Add(Path.GetDirectoryName(f.Path));
                fileList.Items.Add(it);
            }
            fileList.EndUpdate();
        }

        void OpenSelected()
        {
            if (fileList.SelectedItems.Count > 0) Util.ShowInExplorer((string)fileList.SelectedItems[0].Tag);
        }

        void RecycleSelected()
        {
            var items = fileList.SelectedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0) return;
            if (MessageBox.Show(this, "Mover " + items.Count + " arquivo(s) para a Lixeira?", "Analisador de disco",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var it in items)
            {
                try { Util.SendToRecycleBin((string)it.Tag); fileList.Items.Remove(it); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Warning); break; }
            }
        }
    }

    // ======================= Localizador de duplicados =======================

    class DuplicatePage : Page
    {
        readonly TextBox folder;
        readonly NumericUpDown minSize;
        readonly CheckBox matchName, skipHidden;
        readonly Button btnFind, btnStop;
        readonly ListView list;
        readonly Label status;
        DuplicateFinder finder;

        public DuplicatePage(MainForm main)
            : base(main, "Localizador de duplicados", "Encontra arquivos com conteúdo idêntico. Os excluídos vão para a Lixeira.")
        {
            var card = Theme.MakeCard();
            card.Dock = DockStyle.Fill;
            list = Theme.MakeList(true);
            list.Columns.Add("Arquivo", 280);
            list.Columns.Add("Pasta", 440);
            list.Columns.Add("Tamanho", 100, HorizontalAlignment.Right);
            list.Columns.Add("Modificado", 130);
            list.DoubleClick += (s, e) => { if (list.SelectedItems.Count > 0) Util.ShowInExplorer((string)list.SelectedItems[0].Tag); };
            card.Controls.Add(list);
            Body.Controls.Add(card);

            var bar = Theme.MakeButtonBar();
            var btnDelete = Theme.MakeButton("Mover selecionados para a Lixeira", true, "trash-2");
            btnDelete.Click += (s, e) => DeleteChecked();
            var btnSelect = Theme.MakeButton("Selecionar cópias", false, "copy");
            btnSelect.Click += (s, e) => SelectCopies();
            var btnClear = Theme.MakeButton("Limpar seleção", false, "x");
            btnClear.Click += (s, e) => { foreach (ListViewItem it in list.Items) it.Checked = false; };
            bar.Controls.AddRange(new Control[] { btnDelete, btnSelect, btnClear });
            Body.Controls.Add(bar);

            status = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
            Body.Controls.Add(status);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 84, WrapContents = true };
            top.Controls.Add(new Label { Text = "Pasta:", AutoSize = true, Margin = new Padding(0, 9, 6, 0) });
            folder = new TextBox { Width = 280, Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Margin = new Padding(0, 6, 4, 0) };
            var browse = Theme.MakeButton("...");
            browse.AutoSize = false;
            browse.MinimumSize = Size.Empty;
            browse.Size = new Size(36, 26);
            browse.Padding = Padding.Empty;
            browse.Click += (s, e) => { var f = Prompt.Folder(this, "Pasta onde procurar duplicados", folder.Text); if (f != null) folder.Text = f; };
            top.Controls.Add(folder);
            top.Controls.Add(browse);
            top.Controls.Add(new Label { Text = "Mínimo (KB):", AutoSize = true, Margin = new Padding(12, 9, 6, 0) });
            minSize = new NumericUpDown { Minimum = 0, Maximum = 10000000, Value = 100, Width = 80, Margin = new Padding(0, 6, 8, 0) };
            top.Controls.Add(minSize);
            matchName = new CheckBox { Text = "Mesmo nome", AutoSize = true, Margin = new Padding(4, 8, 4, 0) };
            skipHidden = new CheckBox { Text = "Ignorar ocultos/sistema", AutoSize = true, Checked = true, Margin = new Padding(4, 8, 12, 0) };
            top.Controls.Add(matchName);
            top.Controls.Add(skipHidden);
            btnFind = Theme.MakeButton("Procurar", true, "search");
            btnFind.Click += (s, e) => Find();
            btnStop = Theme.MakeButton("Parar", false, "square");
            btnStop.Visible = false;
            btnStop.Click += (s, e) => { if (finder != null) finder.Cancel = true; };
            browse.Margin = new Padding(0, 5, 8, 0);
            foreach (var b in new[] { btnFind, btnStop }) b.Margin = new Padding(0, 2, 8, 0);
            top.Controls.Add(btnFind);
            top.Controls.Add(btnStop);
            Body.Controls.Add(top);
        }

        void Find()
        {
            if (!Directory.Exists(folder.Text)) { status.Text = "Pasta não encontrada."; return; }
            finder = new DuplicateFinder
            {
                MinSize = (long)minSize.Value * 1024,
                MatchName = matchName.Checked,
                SkipHidden = skipHidden.Checked
            };
            var f = finder;
            string root = folder.Text;
            btnFind.Enabled = false;
            btnStop.Visible = true;
            list.Items.Clear();
            list.Groups.Clear();
            var timer = new Timer { Interval = 250 };
            timer.Tick += (s, e) => status.Text = f.Status;
            timer.Start();
            List<List<FileEntry>> groups = null;
            RunBackground(() => groups = f.Find(root), () =>
            {
                timer.Dispose();
                btnFind.Enabled = true;
                btnStop.Visible = false;
                list.BeginUpdate();
                int n = 0;
                foreach (var g in groups.Take(3000))
                {
                    var lg = list.Groups.Add("g" + n, "Grupo " + (++n) + " · " + g.Count + " arquivos de " + Util.FormatSize(g[0].Size));
                    foreach (var fe in g)
                    {
                        var it = new ListViewItem(Path.GetFileName(fe.Path), lg) { Tag = fe.Path };
                        it.SubItems.Add(Path.GetDirectoryName(fe.Path));
                        Theme.SizeCell(it, fe.Size);
                        it.SubItems.Add(fe.Modified.ToString("dd/MM/yyyy HH:mm"));
                        list.Items.Add(it);
                    }
                }
                list.EndUpdate();
                long waste = groups.Sum(g => g[0].Size * (g.Count - 1));
                status.Text = (f.Cancel ? "Interrompido · " : "") + groups.Count + " grupos de duplicados · " +
                              Util.FormatSize(waste) + " podem ser recuperados" + (groups.Count > 3000 ? " (mostrando 3000 grupos)" : "");
            });
        }

        void SelectCopies()
        {
            // Mantém o arquivo mais antigo (primeiro de cada grupo) e marca os demais.
            list.BeginUpdate();
            foreach (ListViewGroup g in list.Groups)
                for (int i = 0; i < g.Items.Count; i++) g.Items[i].Checked = i > 0;
            list.EndUpdate();
        }

        void DeleteChecked()
        {
            var items = list.CheckedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0) { status.Text = "Nenhum arquivo marcado."; return; }
            bool wholeGroup = list.Groups.Cast<ListViewGroup>().Any(g => g.Items.Count > 0 && g.Items.Cast<ListViewItem>().All(i => i.Checked));
            string msg = "Mover " + items.Count + " arquivo(s) para a Lixeira?";
            if (wholeGroup) msg = "ATENÇÃO: em algum grupo TODAS as cópias estão marcadas, então nenhuma sobraria.\n\n" + msg;
            if (MessageBox.Show(this, msg, "Duplicados", MessageBoxButtons.YesNo,
                                wholeGroup ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;
            int ok = 0;
            list.BeginUpdate();
            foreach (var it in items)
            {
                try { Util.SendToRecycleBin((string)it.Tag); list.Items.Remove(it); ok++; }
                catch { it.ForeColor = Theme.Danger; }
            }
            list.EndUpdate();
            status.Text = ok + " arquivo(s) movidos para a Lixeira.";
        }
    }
}
