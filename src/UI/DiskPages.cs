using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    /// <summary>Barra de proporções na linguagem do medidor de espaço: trilho arredondado com segmentos.</summary>
    class SegmentBar : Control
    {
        public List<KeyValuePair<long, Brush>> Segments = new List<KeyValuePair<long, Brush>>();
        public long Total;

        public SegmentBar()
        {
            Height = Theme.S(14);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Papel);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var track = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Rounded(track, Height / 2f))
            {
                using (var b = new SolidBrush(Color.FromArgb(0xE3, 0xE7, 0xEC))) g.FillPath(b, path);
                if (Total <= 0) return;
                g.SetClip(path);
                float x = 0;
                foreach (var seg in Segments)
                {
                    float w = track.Width * seg.Key / Total;
                    g.FillRectangle(seg.Value, x, 0, w + 0.5f, Height);
                    x += w;
                }
                g.ResetClip();
            }
        }
    }

    // ======================= Analisador de disco =======================

    class DiskPage : Page
    {
        // Tons derivados da identidade (Verde-limpo, Ardósia, Âmbar), não um arco-íris.
        static readonly Color[] Palette =
        {
            Color.FromArgb(0x0E, 0x7C, 0x66), Color.FromArgb(0x2F, 0x6F, 0xAE), Color.FromArgb(0x8E, 0x5B, 0xB5), Color.FromArgb(0xB7, 0x79, 0x1F),
            Color.FromArgb(0xB4, 0x53, 0x4B), Color.FromArgb(0x5B, 0x8C, 0x2A), Color.FromArgb(0x3A, 0x41, 0x4B), Color.FromArgb(0x9A, 0xA3, 0xAD)
        };

        readonly ComboBox drives;
        readonly Button btnScan, btnStop, btnFolder;
        readonly ListView catList;
        readonly ListCard files;
        readonly SegmentBar bar;
        readonly Panel catCard;
        Dictionary<string, DiskCategory> data;
        DiskAnalyzer analyzer;
        string customFolder;

        public DiskPage(MainForm main)
            : base(main, "Analisador de disco", "Descubra que tipos de arquivo ocupam mais espaço e quais são os maiores.")
        {
            btnScan = AddAction("Analisar", ButtonKind.Primary, "search", Scan);
            btnStop = AddAction("Parar", ButtonKind.Secondary, "square", () => { if (analyzer != null) analyzer.Cancel = true; });
            btnStop.Visible = false;

            drives = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(290), Margin = Theme.Pad(0, 6, 8, 0), Font = Theme.Base };
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)))
                drives.Items.Add(new DriveItem(d));
            if (drives.Items.Count > 0) drives.SelectedIndex = 0;
            drives.SelectedIndexChanged += (s, e) => customFolder = null;
            btnFolder = Theme.MakeButton("Escolher pasta...", ButtonKind.Secondary, "folder-open");
            btnFolder.Margin = Theme.Pad(0, 0, 0, 0);
            btnFolder.Click += (s, e) =>
            {
                var f = Prompt.Folder(this, "Pasta a analisar");
                if (f != null) { customFolder = f; Scan(); }
            };
            ToolbarLeft.Controls.Add(drives);
            ToolbarLeft.Controls.Add(btnFolder);
            SetStatus("Escolha uma unidade ou pasta e clique em Analisar.");

            // ---- Arquivos da categoria selecionada ----
            var fileList = Theme.MakeList();
            Theme.Col(fileList, "Arquivo", 280);
            Theme.Col(fileList, "Tamanho", 100, HorizontalAlignment.Right);
            Theme.Col(fileList, "Modificado", 130);
            Theme.Col(fileList, "Pasta", 300);
            Theme.EnableSorting(fileList);
            fileList.DoubleClick += (s, e) => OpenSelected();
            var menu = new ContextMenuStrip { Font = Theme.Base };
            menu.Items.Add("Abrir local", null, (s, e) => OpenSelected());
            menu.Items.Add("Mover para a Lixeira", null, (s, e) => RecycleSelected());
            fileList.ContextMenuStrip = menu;
            files = new ListCard(fileList) { Dock = DockStyle.Fill };
            files.SetEmpty("Os maiores arquivos de cada categoria aparecem aqui depois da análise.", "hard-drive");
            Body.Controls.Add(files);
            Body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(14) });

            // ---- Categorias: barra de proporções + lista ----
            catCard = Theme.MakeCard();
            catCard.Dock = DockStyle.Top;
            catCard.Height = Theme.S(262);
            catList = Theme.MakeList();
            catList.SmallImageList = new ImageList { ImageSize = Theme.Sz(14, 26), ColorDepth = ColorDepth.Depth32Bit };
            for (int i = 0; i < DiskAnalyzer.CategoryNames.Length; i++)
                catList.SmallImageList.Images.Add(Swatch(Palette[i % Palette.Length]));
            Theme.Col(catList, "Categoria", 200);
            Theme.Col(catList, "Tamanho", 110, HorizontalAlignment.Right);
            Theme.Col(catList, "Parte do total", 120, HorizontalAlignment.Right);
            Theme.Col(catList, "Arquivos", 120);
            catList.SelectedIndexChanged += (s, e) => ShowCategory();
            catCard.Controls.Add(catList);
            var barHolder = new Panel { Dock = DockStyle.Top, Height = Theme.S(34), Padding = Theme.Pad(12, 12, 12, 8), BackColor = Theme.Papel };
            bar = new SegmentBar { Dock = DockStyle.Fill };
            barHolder.Controls.Add(bar);
            catCard.Controls.Add(barHolder);
            Body.Controls.Add(catCard);
        }

        static Bitmap Swatch(Color c)
        {
            var bmp = new Bitmap(Theme.S(14), Theme.S(26));
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(c))
                using (var path = Theme.Rounded(new RectangleF(1, Theme.S(8f), Theme.S(10f), Theme.S(10f)), Theme.S(2f)))
                    g.FillPath(b, path);
            }
            return bmp;
        }

        class DriveItem
        {
            public readonly DriveInfo Drive;
            public DriveItem(DriveInfo d) { Drive = d; }
            public override string ToString()
            {
                string label = string.IsNullOrEmpty(Drive.VolumeLabel) ? "Disco local" : Drive.VolumeLabel;
                return Drive.Name.TrimEnd('\\') + " " + label + " · " + Util.FormatSize(Drive.AvailableFreeSpace) + " livres de " + Util.FormatSize(Drive.TotalSize);
            }
        }

        void Scan()
        {
            string root = customFolder ?? (drives.SelectedItem is DriveItem ? ((DriveItem)drives.SelectedItem).Drive.RootDirectory.FullName : null);
            if (root == null) return;
            btnScan.Visible = false;
            btnStop.Visible = true;
            btnFolder.Enabled = drives.Enabled = false;
            catList.Items.Clear();
            files.List.Items.Clear();
            files.SetEmpty(null);
            data = null;
            bar.Total = 0;
            bar.Invalidate();
            Progress.StartIndeterminate();
            analyzer = new DiskAnalyzer();
            var a = analyzer;
            var timer = new Timer { Interval = 250 };
            timer.Tick += (s, e) => SetStatus("Lendo " + a.ScannedFiles.ToString("N0") + " arquivos · " + a.CurrentDir);
            timer.Start();
            Dictionary<string, DiskCategory> result = null;
            RunBackground(() => result = a.Scan(root), () =>
            {
                timer.Dispose();
                Progress.Stop();
                btnScan.Visible = true;
                btnStop.Visible = false;
                btnFolder.Enabled = drives.Enabled = true;
                data = result;
                long total = result.Values.Sum(c => c.Bytes);
                SetStatus((a.Cancel ? "Análise interrompida: " : "") + a.ScannedFiles.ToString("N0") + " arquivos somam " +
                          Util.FormatSize(total) + " em " + root);

                bar.Segments.Clear();
                catList.BeginUpdate();
                for (int i = 0; i < DiskAnalyzer.CategoryNames.Length; i++)
                {
                    var c = result[DiskAnalyzer.CategoryNames[i]];
                    bar.Segments.Add(new KeyValuePair<long, Brush>(c.Bytes, new SolidBrush(Palette[i % Palette.Length])));
                    var it = new ListViewItem(c.Name, i) { Tag = c };
                    Theme.SizeCell(it, c.Bytes);
                    it.SubItems.Add(total == 0 ? "0%" : (100.0 * c.Bytes / total).ToString("0.0") + "%");
                    it.SubItems.Add(new ListViewItem.ListViewSubItem(it, c.Count.ToString("N0") + " arquivos") { Tag = c.Count });
                    catList.Items.Add(it);
                }
                catList.EndUpdate();
                bar.Total = total;
                bar.Invalidate();
                files.SetEmpty("Escolha uma categoria acima para ver os maiores arquivos.", "chart-pie");
                if (catList.Items.Count > 0) catList.Items[0].Selected = true;
            });
        }

        void ShowCategory()
        {
            if (catList.SelectedItems.Count == 0) return;
            var c = (DiskCategory)catList.SelectedItems[0].Tag;
            var list = files.List;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var f in c.Files.OrderByDescending(f => f.Size))
            {
                var it = new ListViewItem(Path.GetFileName(f.Path)) { Tag = f.Path };
                Theme.SizeCell(it, f.Size);
                it.SubItems.Add(new ListViewItem.ListViewSubItem(it, f.Modified.ToString("dd/MM/yyyy HH:mm")) { Tag = f.Modified });
                Theme.MonoCell(it, Path.GetDirectoryName(f.Path));
                list.Items.Add(it);
            }
            Theme.Refit(list);
            list.EndUpdate();
            files.SetEmpty("Nenhum arquivo de " + c.Name.ToLowerInvariant() + " encontrado.", "chart-pie");
        }

        void OpenSelected()
        {
            if (files.List.SelectedItems.Count > 0) Util.ShowInExplorer((string)files.List.SelectedItems[0].Tag);
        }

        void RecycleSelected()
        {
            var items = files.List.SelectedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0) return;
            if (MessageBox.Show(this, "Mover " + items.Count + " arquivo(s) para a Lixeira?", "Mover para a Lixeira",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int moved = 0;
            foreach (var it in items)
            {
                try { Util.SendToRecycleBin((string)it.Tag); files.List.Items.Remove(it); moved++; }
                catch (Exception ex) { SetStatus("Não foi possível mover " + it.Text + ": " + ex.Message, StatusKind.Danger); break; }
            }
            if (moved > 0) SetStatus(moved + " arquivo(s) movidos para a Lixeira.", StatusKind.Good);
        }
    }

    // ======================= Localizador de duplicados =======================

    class DuplicatePage : Page
    {
        readonly TextBox folder;
        readonly NumericUpDown minSize;
        readonly CheckBox matchName, skipHidden;
        readonly Button btnFind, btnStop, btnDelete;
        readonly ListCard results;
        DuplicateFinder finder;

        public DuplicatePage(MainForm main)
            : base(main, "Localizador de duplicados", "Encontra arquivos com conteúdo idêntico. Os que você excluir vão para a Lixeira.")
        {
            btnFind = AddAction("Procurar", ButtonKind.Primary, "search", Find);
            btnStop = AddAction("Parar", ButtonKind.Secondary, "square", () => { if (finder != null) finder.Cancel = true; });
            btnStop.Visible = false;

            folder = new TextBox { Width = Theme.S(300), Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Margin = Theme.Pad(0, 6, 0, 0), Font = Theme.Base };
            var browse = Theme.MakeIconButton("folder-open", "Escolher pasta");
            browse.Click += (s, e) => { var f = Prompt.Folder(this, "Pasta onde procurar duplicados", folder.Text); if (f != null) folder.Text = f; };
            ToolbarLeft.Controls.Add(folder);
            ToolbarLeft.Controls.Add(browse);
            SetStatus("Escolha uma pasta e clique em Procurar.");

            // ---- Ações sobre a seleção, abaixo da lista ----
            var selectionBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(48), Padding = Theme.Pad(0, 10, 0, 0), WrapContents = false };
            btnDelete = Theme.MakeButton("Mover marcados para a Lixeira", ButtonKind.Danger, "trash-2");
            btnDelete.Margin = Theme.Pad(0, 0, 8, 0);
            btnDelete.Click += (s, e) => DeleteChecked();
            var btnSelect = Theme.MakeButton("Marcar cópias (manter o mais antigo)", ButtonKind.Link, "list-checks");
            btnSelect.Click += (s, e) => SelectCopies();
            var btnClear = Theme.MakeButton("Desmarcar tudo", ButtonKind.Link, "x");
            btnClear.Click += (s, e) => { foreach (ListViewItem it in results.List.Items) it.Checked = false; };
            selectionBar.Controls.AddRange(new Control[] { btnDelete, btnSelect, btnClear });

            var list = Theme.MakeList(true);
            Theme.Col(list, "Arquivo", 260);
            Theme.Col(list, "Tamanho", 100, HorizontalAlignment.Right);
            Theme.Col(list, "Modificado", 130);
            Theme.Col(list, "Pasta", 380);
            list.DoubleClick += (s, e) => { if (list.SelectedItems.Count > 0) Util.ShowInExplorer((string)list.SelectedItems[0].Tag); };
            results = new ListCard(list) { Dock = DockStyle.Fill };
            results.SetEmpty("Arquivos iguais aparecem agrupados aqui.\nVocê escolhe quais cópias apagar; o mais antigo de cada grupo fica por padrão.", "copy");
            Body.Controls.Add(results);
            Body.Controls.Add(selectionBar);
            BindToSelection(btnDelete, list, true);

            // ---- Filtros ----
            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(40), WrapContents = false, Padding = Theme.Pad(0, 0, 0, 8) };
            filters.Controls.Add(new Label { Text = "Ignorar arquivos menores que", AutoSize = true, ForeColor = Theme.Grafite, Margin = Theme.Pad(0, 7, 6, 0) });
            minSize = new NumericUpDown { Minimum = 0, Maximum = 10000000, Value = 100, Width = Theme.S(80), Margin = Theme.Pad(0, 4, 4, 0), Font = Theme.Base };
            filters.Controls.Add(minSize);
            filters.Controls.Add(new Label { Text = "KB", AutoSize = true, ForeColor = Theme.Grafite, Margin = Theme.Pad(0, 7, 20, 0) });
            matchName = new CheckBox { Text = "Exigir o mesmo nome", AutoSize = true, Margin = Theme.Pad(0, 6, 16, 0) };
            skipHidden = new CheckBox { Text = "Ignorar arquivos ocultos e do sistema", AutoSize = true, Checked = true, Margin = Theme.Pad(0, 6, 0, 0) };
            filters.Controls.Add(matchName);
            filters.Controls.Add(skipHidden);
            Body.Controls.Add(filters);
        }

        void Find()
        {
            if (!Directory.Exists(folder.Text)) { SetStatus("A pasta \"" + folder.Text + "\" não existe.", StatusKind.Warning); return; }
            finder = new DuplicateFinder
            {
                MinSize = (long)minSize.Value * 1024,
                MatchName = matchName.Checked,
                SkipHidden = skipHidden.Checked
            };
            var f = finder;
            string root = folder.Text;
            btnFind.Visible = false;
            btnStop.Visible = true;
            var list = results.List;
            list.Items.Clear();
            list.Groups.Clear();
            results.SetEmpty(null);
            Progress.StartIndeterminate();
            var timer = new Timer { Interval = 250 };
            timer.Tick += (s, e) => SetStatus(f.Status);
            timer.Start();
            List<List<FileEntry>> groups = null;
            RunBackground(() => groups = f.Find(root), () =>
            {
                timer.Dispose();
                Progress.Stop();
                btnFind.Visible = true;
                btnStop.Visible = false;
                list.BeginUpdate();
                int n = 0;
                foreach (var g in groups.Take(3000))
                {
                    var lg = list.Groups.Add("g" + n, "Grupo " + (++n) + " · " + g.Count + " cópias de " + Util.FormatSize(g[0].Size));
                    foreach (var fe in g)
                    {
                        var it = new ListViewItem(Path.GetFileName(fe.Path), lg) { Tag = fe.Path };
                        Theme.SizeCell(it, fe.Size);
                        it.SubItems.Add(fe.Modified.ToString("dd/MM/yyyy HH:mm"));
                        Theme.MonoCell(it, Path.GetDirectoryName(fe.Path));
                        list.Items.Add(it);
                    }
                }
                Theme.Refit(list);
                list.EndUpdate();
                Reevaluate(btnDelete);
                long waste = groups.Sum(g => g[0].Size * (g.Count - 1));
                if (groups.Count == 0) SetStatus((f.Cancel ? "Busca interrompida. " : "") + "Nenhum arquivo duplicado encontrado.", StatusKind.Good);
                else SetStatus((f.Cancel ? "Busca interrompida · " : "") + groups.Count + " grupos de duplicados · " + Util.FormatSize(waste) +
                               " podem ser liberados" + (groups.Count > 3000 ? " (mostrando 3000 grupos)" : ""));
                results.SetEmpty("Nenhum arquivo duplicado em " + root + ".", "circle-check");
            });
        }

        void SelectCopies()
        {
            // Mantém o arquivo mais antigo (primeiro de cada grupo) e marca os demais.
            var list = results.List;
            list.BeginUpdate();
            foreach (ListViewGroup g in list.Groups)
                for (int i = 0; i < g.Items.Count; i++) g.Items[i].Checked = i > 0;
            Theme.Refit(list);
            list.EndUpdate();
        }

        void DeleteChecked()
        {
            var list = results.List;
            var items = list.CheckedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0) return;
            bool wholeGroup = list.Groups.Cast<ListViewGroup>().Any(g => g.Items.Count > 0 && g.Items.Cast<ListViewItem>().All(i => i.Checked));
            string msg = "Mover " + items.Count + " arquivo(s) para a Lixeira?";
            if (wholeGroup) msg = "Atenção: em algum grupo todas as cópias estão marcadas, e nenhuma sobraria.\n\n" + msg;
            if (MessageBox.Show(this, msg, "Mover para a Lixeira", MessageBoxButtons.YesNo,
                                wholeGroup ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;
            int ok = 0, failed = 0;
            list.BeginUpdate();
            foreach (var it in items)
            {
                try { Util.SendToRecycleBin((string)it.Tag); list.Items.Remove(it); ok++; }
                catch { it.ForeColor = Theme.Vermelho; failed++; }
            }
            Theme.Refit(list);
            list.EndUpdate();
            Reevaluate(btnDelete);
            SetStatus(ok + " arquivo(s) movidos para a Lixeira" + (failed > 0 ? " · " + failed + " não puderam ser movidos" : "."),
                      failed > 0 ? StatusKind.Danger : StatusKind.Good);
        }
    }
}
