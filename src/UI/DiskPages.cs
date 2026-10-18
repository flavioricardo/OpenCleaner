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
            : base(main, L.T("Analisador de disco", "Disk analyzer"), L.T("Descubra que tipos de arquivo ocupam mais espaço e quais são os maiores.", "Find out which file types take up the most space and which files are the largest."))
        {
            btnScan = AddAction(L.T("Analisar", "Analyze"), ButtonKind.Primary, "search", Scan);
            btnStop = AddAction(L.T("Parar", "Stop"), ButtonKind.Secondary, "square", () => { if (analyzer != null) analyzer.Cancel = true; });
            btnStop.Visible = false;

            drives = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(290), Margin = Theme.Pad(0, 6, 8, 0), Font = Theme.Base };
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)))
                drives.Items.Add(new DriveItem(d));
            if (drives.Items.Count > 0) drives.SelectedIndex = 0;
            drives.SelectedIndexChanged += (s, e) => customFolder = null;
            btnFolder = Theme.MakeButton(L.T("Escolher pasta...", "Choose folder..."), ButtonKind.Secondary, "folder-open");
            btnFolder.Margin = Theme.Pad(0, 0, 0, 0);
            btnFolder.Click += (s, e) =>
            {
                var f = Prompt.Folder(this, L.T("Pasta a analisar", "Folder to analyze"));
                if (f != null) { customFolder = f; Scan(); }
            };
            ToolbarLeft.Controls.Add(drives);
            ToolbarLeft.Controls.Add(btnFolder);
            SetStatus(L.T("Escolha uma unidade ou pasta e clique em Analisar.", "Choose a drive or folder and click Analyze."));

            // ---- Arquivos da categoria selecionada ----
            var fileList = Theme.MakeList();
            Theme.Col(fileList, L.T("Arquivo", "File"), 280);
            Theme.Col(fileList, L.T("Tamanho", "Size"), 100, HorizontalAlignment.Right);
            Theme.Col(fileList, L.T("Modificado", "Modified"), 130);
            Theme.Col(fileList, L.T("Pasta", "Folder"), 300);
            Theme.EnableSorting(fileList);
            fileList.DoubleClick += (s, e) => OpenSelected();
            var menu = new ContextMenuStrip { Font = Theme.Base };
            menu.Items.Add(L.T("Abrir local", "Open location"), null, (s, e) => OpenSelected());
            menu.Items.Add(L.T("Mover para a Lixeira", "Move to Recycle Bin"), null, (s, e) => RecycleSelected());
            fileList.ContextMenuStrip = menu;
            files = new ListCard(fileList) { Dock = DockStyle.Fill };
            files.SetEmpty(L.T("Os maiores arquivos de cada categoria aparecem aqui depois da análise.", "The largest files in each category show up here after the analysis."), "hard-drive");
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
            Theme.Col(catList, L.T("Categoria", "Category"), 200);
            Theme.Col(catList, L.T("Tamanho", "Size"), 110, HorizontalAlignment.Right);
            Theme.Col(catList, L.T("Parte do total", "Share of total"), 120, HorizontalAlignment.Right);
            Theme.Col(catList, L.T("Arquivos", "Files"), 120);
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
                string label = string.IsNullOrEmpty(Drive.VolumeLabel) ? L.T("Disco local", "Local disk") : Drive.VolumeLabel;
                return Drive.Name.TrimEnd('\\') + " " + label + " · " + Util.FormatSize(Drive.AvailableFreeSpace) + L.T(" livres de ", " free of ") + Util.FormatSize(Drive.TotalSize);
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
            timer.Tick += (s, e) => SetStatus(L.T("Lendo ", "Reading ") + a.ScannedFiles.ToString("N0") + L.T(" arquivos · ", " files · ") + a.CurrentDir);
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
                SetStatus((a.Cancel ? L.T("Análise interrompida: ", "Analysis stopped: ") : "") + a.ScannedFiles.ToString("N0") + L.T(" arquivos somam ", " files add up to ") +
                          Util.FormatSize(total) + L.T(" em ", " in ") + root);

                bar.Segments.Clear();
                catList.BeginUpdate();
                for (int i = 0; i < DiskAnalyzer.CategoryNames.Length; i++)
                {
                    var c = result[DiskAnalyzer.CategoryNames[i]];
                    bar.Segments.Add(new KeyValuePair<long, Brush>(c.Bytes, new SolidBrush(Palette[i % Palette.Length])));
                    var it = new ListViewItem(c.Name, i) { Tag = c };
                    Theme.SizeCell(it, c.Bytes);
                    it.SubItems.Add(total == 0 ? "0%" : (100.0 * c.Bytes / total).ToString("0.0") + "%");
                    it.SubItems.Add(new ListViewItem.ListViewSubItem(it, c.Count.ToString("N0") + L.T(" arquivos", " files")) { Tag = c.Count });
                    catList.Items.Add(it);
                }
                catList.EndUpdate();
                bar.Total = total;
                bar.Invalidate();
                files.SetEmpty(L.T("Escolha uma categoria acima para ver os maiores arquivos.", "Choose a category above to see the largest files."), "chart-pie");
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
                var it = new ListViewItem(Util.FileNameOf(f.Path)) { Tag = f.Path };
                Theme.SizeCell(it, f.Size);
                it.SubItems.Add(new ListViewItem.ListViewSubItem(it, f.Modified.ToString("dd/MM/yyyy HH:mm")) { Tag = f.Modified });
                Theme.MonoCell(it, Util.DirectoryOf(f.Path));
                list.Items.Add(it);
            }
            Theme.Refit(list);
            list.EndUpdate();
            files.SetEmpty(L.T("Nenhum arquivo de ", "No ") + c.Name.ToLowerInvariant() + L.T(" encontrado.", " files found."), "chart-pie");
        }

        void OpenSelected()
        {
            if (files.List.SelectedItems.Count > 0) Util.ShowInExplorer((string)files.List.SelectedItems[0].Tag);
        }

        void RecycleSelected()
        {
            var items = files.List.SelectedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0) return;
            if (MessageBox.Show(this, L.T("Mover ", "Move ") + items.Count + L.T(" arquivo(s) para a Lixeira?", " file(s) to the Recycle Bin?"), L.T("Mover para a Lixeira", "Move to Recycle Bin"),
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int moved = 0;
            foreach (var it in items)
            {
                try { Util.SendToRecycleBin((string)it.Tag); files.List.Items.Remove(it); moved++; }
                catch (Exception ex) { SetStatus(L.T("Não foi possível mover ", "Could not move ") + it.Text + ": " + ex.Message, StatusKind.Danger); break; }
            }
            if (moved > 0) SetStatus(moved + L.T(" arquivo(s) movidos para a Lixeira.", " file(s) moved to the Recycle Bin."), StatusKind.Good);
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
            : base(main, L.T("Localizador de duplicados", "Duplicate finder"), L.T("Encontra arquivos com conteúdo idêntico. Os que você excluir vão para a Lixeira.", "Finds files with identical contents. The ones you delete go to the Recycle Bin."))
        {
            btnFind = AddAction(L.T("Procurar", "Search"), ButtonKind.Primary, "search", Find);
            btnStop = AddAction(L.T("Parar", "Stop"), ButtonKind.Secondary, "square", () => { if (finder != null) finder.Cancel = true; });
            btnStop.Visible = false;

            folder = new TextBox { Width = Theme.S(300), Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Margin = Theme.Pad(0, 6, 0, 0), Font = Theme.Base };
            var browse = Theme.MakeIconButton("folder-open", L.T("Escolher pasta", "Choose folder"));
            browse.Click += (s, e) => { var f = Prompt.Folder(this, L.T("Pasta onde procurar duplicados", "Folder to search for duplicates"), folder.Text); if (f != null) folder.Text = f; };
            ToolbarLeft.Controls.Add(folder);
            ToolbarLeft.Controls.Add(browse);
            SetStatus(L.T("Escolha uma pasta e clique em Procurar.", "Choose a folder and click Search."));

            // ---- Ações sobre a seleção, abaixo da lista ----
            var selectionBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(48), Padding = Theme.Pad(0, 10, 0, 0), WrapContents = false };
            btnDelete = Theme.MakeButton(L.T("Mover marcados para a Lixeira", "Move checked to Recycle Bin"), ButtonKind.Danger, "trash-2");
            btnDelete.Margin = Theme.Pad(0, 0, 8, 0);
            btnDelete.Click += (s, e) => DeleteChecked();
            var btnSelect = Theme.MakeButton(L.T("Marcar cópias (manter o mais antigo)", "Check copies (keep the oldest)"), ButtonKind.Link, "list-checks");
            btnSelect.Click += (s, e) => SelectCopies();
            var btnClear = Theme.MakeButton(L.T("Desmarcar tudo", "Uncheck all"), ButtonKind.Link, "x");
            btnClear.Click += (s, e) => { foreach (ListViewItem it in results.List.Items) it.Checked = false; };
            selectionBar.Controls.AddRange(new Control[] { btnDelete, btnSelect, btnClear });

            var list = Theme.MakeList(true);
            Theme.Col(list, L.T("Arquivo", "File"), 260);
            Theme.Col(list, L.T("Tamanho", "Size"), 100, HorizontalAlignment.Right);
            Theme.Col(list, L.T("Modificado", "Modified"), 130);
            Theme.Col(list, L.T("Pasta", "Folder"), 380);
            list.DoubleClick += (s, e) => { if (list.SelectedItems.Count > 0) Util.ShowInExplorer((string)list.SelectedItems[0].Tag); };
            results = new ListCard(list) { Dock = DockStyle.Fill };
            results.SetEmpty(L.T("Arquivos iguais aparecem agrupados aqui.\nVocê escolhe quais cópias apagar; o mais antigo de cada grupo fica por padrão.", "Identical files show up grouped here.\nYou choose which copies to delete; the oldest in each group is kept by default."), "copy");
            Body.Controls.Add(results);
            Body.Controls.Add(selectionBar);
            BindToSelection(btnDelete, list, true);

            // ---- Filtros ----
            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(40), WrapContents = false, Padding = Theme.Pad(0, 0, 0, 8) };
            filters.Controls.Add(new Label { Text = L.T("Ignorar arquivos menores que", "Ignore files smaller than"), AutoSize = true, ForeColor = Theme.Grafite, Margin = Theme.Pad(0, 7, 6, 0) });
            minSize = new NumericUpDown { Minimum = 0, Maximum = 10000000, Value = 100, Width = Theme.S(80), Margin = Theme.Pad(0, 4, 4, 0), Font = Theme.Base };
            filters.Controls.Add(minSize);
            filters.Controls.Add(new Label { Text = "KB", AutoSize = true, ForeColor = Theme.Grafite, Margin = Theme.Pad(0, 7, 20, 0) });
            matchName = new CheckBox { Text = L.T("Exigir o mesmo nome", "Require the same name"), AutoSize = true, Margin = Theme.Pad(0, 6, 16, 0) };
            skipHidden = new CheckBox { Text = L.T("Ignorar arquivos ocultos e do sistema", "Ignore hidden and system files"), AutoSize = true, Checked = true, Margin = Theme.Pad(0, 6, 0, 0) };
            filters.Controls.Add(matchName);
            filters.Controls.Add(skipHidden);
            Body.Controls.Add(filters);
        }

        void Find()
        {
            if (!Directory.Exists(folder.Text)) { SetStatus(L.F("A pasta \"{0}\" não existe.", "The folder \"{0}\" does not exist.", folder.Text), StatusKind.Warning); return; }
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
                    var lg = list.Groups.Add("g" + n, L.T("Grupo ", "Group ") + (++n) + " · " + g.Count + L.T(" cópias de ", " copies of ") + Util.FormatSize(g[0].Size));
                    foreach (var fe in g)
                    {
                        var it = new ListViewItem(Util.FileNameOf(fe.Path), lg) { Tag = fe.Path };
                        Theme.SizeCell(it, fe.Size);
                        it.SubItems.Add(fe.Modified.ToString("dd/MM/yyyy HH:mm"));
                        Theme.MonoCell(it, Util.DirectoryOf(fe.Path));
                        list.Items.Add(it);
                    }
                }
                Theme.Refit(list);
                list.EndUpdate();
                Reevaluate(btnDelete);
                long waste = groups.Sum(g => g[0].Size * (g.Count - 1));
                if (groups.Count == 0) SetStatus((f.Cancel ? L.T("Busca interrompida. ", "Search stopped. ") : "") + L.T("Nenhum arquivo duplicado encontrado.", "No duplicate files found."), StatusKind.Good);
                else SetStatus((f.Cancel ? L.T("Busca interrompida · ", "Search stopped · ") : "") + groups.Count + L.T(" grupos de duplicados · ", " duplicate groups · ") + Util.FormatSize(waste) +
                               L.T(" podem ser liberados", " can be freed") + (groups.Count > 3000 ? L.T(" (mostrando 3000 grupos)", " (showing 3000 groups)") : ""));
                results.SetEmpty(L.T("Nenhum arquivo duplicado em ", "No duplicate files in ") + root + ".", "circle-check");
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
            string msg = L.T("Mover ", "Move ") + items.Count + L.T(" arquivo(s) para a Lixeira?", " file(s) to the Recycle Bin?");
            if (wholeGroup) msg = L.T("Atenção: em algum grupo todas as cópias estão marcadas, e nenhuma sobraria.\n\n", "Warning: in some group every copy is checked, so none would be left.\n\n") + msg;
            if (MessageBox.Show(this, msg, L.T("Mover para a Lixeira", "Move to Recycle Bin"), MessageBoxButtons.YesNo,
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
            SetStatus(ok + L.T(" arquivo(s) movidos para a Lixeira", " file(s) moved to the Recycle Bin") + (failed > 0 ? " · " + failed + L.T(" não puderam ser movidos", " could not be moved") : "."),
                      failed > 0 ? StatusKind.Danger : StatusKind.Good);
        }
    }
}
