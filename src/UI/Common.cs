using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    enum ButtonKind { Primary, Secondary, Danger, Link }
    enum StatusKind { Normal, Good, Warning, Danger }

    /// <summary>
    /// Identidade visual do OpenCleaner.
    /// Paleta: Névoa (fundo), Papel (superfícies), Tinta (texto), Grafite (texto secundário),
    /// Linha (bordas), Verde-limpo (ação), Ardósia (espaço usado no medidor), Âmbar (avisos).
    /// Tipos: Bahnschrift nos títulos e números, Segoe UI no corpo, Consolas em caminhos e chaves.
    /// </summary>
    static class Theme
    {
        public static readonly Color Nevoa = Color.FromArgb(0xEE, 0xF1, 0xF4);
        public static readonly Color Papel = Color.White;
        public static readonly Color Tinta = Color.FromArgb(0x1B, 0x1F, 0x24);
        public static readonly Color Grafite = Color.FromArgb(0x5B, 0x63, 0x6E);     // 5,4:1 sobre Névoa
        public static readonly Color Linha = Color.FromArgb(0xD9, 0xDE, 0xE4);
        public static readonly Color Hover = Color.FromArgb(0xF3, 0xF5, 0xF7);
        public static readonly Color Verde = Color.FromArgb(0x0E, 0x7C, 0x66);       // 5,1:1 com branco
        public static readonly Color VerdeEscuro = Color.FromArgb(0x0A, 0x63, 0x52);
        public static readonly Color VerdeNevoa = Color.FromArgb(0xE2, 0xF0, 0xEB);
        public static readonly Color Ardosia = Color.FromArgb(0x3A, 0x41, 0x4B);
        public static readonly Color Ambar = Color.FromArgb(0x8A, 0x5A, 0x12);
        public static readonly Color AmbarNevoa = Color.FromArgb(0xFF, 0xF4, 0xDC);
        public static readonly Color Vermelho = Color.FromArgb(0xB4, 0x23, 0x18);
        public static readonly Color VermelhoNevoa = Color.FromArgb(0xFD, 0xEC, 0xEA);

        // Nomes antigos usados pelo código das páginas
        public static Color Background { get { return Nevoa; } }
        public static Color Text { get { return Tinta; } }
        public static Color Muted { get { return Grafite; } }
        public static Color Accent { get { return Verde; } }
        public static Color Good { get { return Verde; } }
        public static Color Danger { get { return Vermelho; } }
        public static Color Border { get { return Linha; } }

        public static readonly Font Base = new Font("Segoe UI", 9.5f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Status = new Font("Segoe UI Semibold", 10.5f);
        public static readonly Font Mono = new Font("Consolas", 9f);
        public static readonly Font Title = Pick("Bahnschrift SemiBold", 19f, "Segoe UI Semibold", 17f);
        public static readonly Font H2 = Pick("Bahnschrift SemiBold", 12f, "Segoe UI Semibold", 11.5f);
        public static readonly Font Eyebrow = Pick("Bahnschrift SemiBold", 8.5f, "Segoe UI Semibold", 8f);
        public static readonly Font Big = Pick("Bahnschrift Light", 26f, "Segoe UI Light", 22f);
        public static readonly Font Wordmark = Pick("Bahnschrift SemiBold", 15f, "Segoe UI Semibold", 14f);

        static Font Pick(string name, float size, string fallback, float fallbackSize)
        {
            var f = new Font(name, size);
            if (f.Name == name) return f;
            f.Dispose();
            return new Font(fallback, fallbackSize);
        }

        // ---- Escala por DPI: todo tamanho em pixels passa por S() ----

        public static readonly float Scale = ReadScale();

        static float ReadScale()
        {
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f; }
            catch { return 1f; }
        }

        public static int S(int px) { return (int)Math.Round(px * Scale); }
        public static float S(float px) { return px * Scale; }
        public static Size Sz(int w, int h) { return new Size(S(w), S(h)); }
        public static Point Pt(int x, int y) { return new Point(S(x), S(y)); }
        public static Padding Pad(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }
        public static Padding Pad(int all) { return new Padding(S(all)); }

        public static bool Motion { get { return SystemInformation.UIEffectsEnabled; } }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---- Botões ----

        public static Button MakeButton(string text, bool primary = false, string icon = null)
        {
            return MakeButton(text, primary ? ButtonKind.Primary : ButtonKind.Secondary, icon);
        }

        public static Button MakeButton(string text, ButtonKind kind, string icon = null)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                MinimumSize = new Size(0, S(34)),
                Padding = Pad(10, 0, 12, 0),
                Margin = Pad(8, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                Font = kind == ButtonKind.Link ? Base : Bold,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Action paint = () =>
            {
                Color bg, fg, border, hover;
                switch (kind)
                {
                    case ButtonKind.Primary: bg = Verde; fg = Color.White; border = Verde; hover = VerdeEscuro; break;
                    case ButtonKind.Danger: bg = Papel; fg = Vermelho; border = Color.FromArgb(0xEC, 0xB8, 0xB2); hover = VermelhoNevoa; break;
                    case ButtonKind.Link: bg = Nevoa; fg = VerdeEscuro; border = Nevoa; hover = VerdeNevoa; break;
                    default: bg = Papel; fg = Tinta; border = Linha; hover = Hover; break;
                }
                if (!b.Enabled)
                {
                    bg = kind == ButtonKind.Primary ? Linha : kind == ButtonKind.Link ? Nevoa : Papel;
                    border = kind == ButtonKind.Link ? Nevoa : Linha;
                    fg = Grafite;
                }
                b.BackColor = bg;
                b.ForeColor = fg;
                b.FlatAppearance.BorderColor = border;
                b.FlatAppearance.MouseOverBackColor = hover;
                b.FlatAppearance.MouseDownBackColor = hover;
                if (icon != null)
                {
                    var old = b.Image;
                    b.Image = IconWithGap(icon, b.Enabled ? fg : Grafite);
                    if (old != null) old.Dispose();
                }
            };
            paint();
            b.EnabledChanged += (s, e) => paint();
            if (icon != null)
            {
                b.ImageAlign = ContentAlignment.MiddleLeft;
                b.TextImageRelation = TextImageRelation.ImageBeforeText;
            }
            return b;
        }

        /// <summary>Ícone de 16 px com um respiro à direita, para não encostar no texto do botão.</summary>
        static Bitmap IconWithGap(string icon, Color color)
        {
            int size = S(16), gap = S(6);
            var bmp = new Bitmap(size + gap, size);
            using (var g = Graphics.FromImage(bmp)) Lucide.Draw(g, icon, new RectangleF(0, 0, size, size), color);
            return bmp;
        }

        /// <summary>Botão só com ícone; o texto vai para a dica e para leitores de tela.</summary>
        public static Button MakeIconButton(string icon, string tooltip)
        {
            var b = new Button
            {
                Size = Sz(34, 30), FlatStyle = FlatStyle.Flat, BackColor = Papel, Cursor = Cursors.Hand,
                Image = Lucide.Render(icon, S(16), Tinta), AccessibleName = tooltip, UseVisualStyleBackColor = false,
                Margin = Pad(4, 4, 0, 0)
            };
            b.FlatAppearance.BorderColor = Linha;
            b.FlatAppearance.MouseOverBackColor = Hover;
            new ToolTip().SetToolTip(b, tooltip);
            return b;
        }

        /// <summary>Botão com menu (para ações menos usadas).</summary>
        public static Button MakeMenuButton(string text, params KeyValuePair<string, Action>[] items)
        {
            var b = MakeButton(text, ButtonKind.Secondary, "ellipsis");
            var menu = new ContextMenuStrip { Font = Base };
            foreach (var it in items)
            {
                var act = it.Value;
                menu.Items.Add(it.Key, null, (s, e) => act());
            }
            b.Click += (s, e) => menu.Show(b, new Point(0, b.Height));
            return b;
        }

        public static KeyValuePair<string, Action> Item(string text, Action a) { return new KeyValuePair<string, Action>(text, a); }

        // ---- Listas ----

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
                ForeColor = Tinta,
                Dock = DockStyle.Fill,
                // ImageList de 1 px de largura só para dar altura às linhas.
                SmallImageList = new ImageList { ImageSize = new Size(1, S(26)) }
            };
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                           .SetValue(lv, true, null);
            FillLastColumn(lv);
            return lv;
        }

        public static ColumnHeader Col(ListView lv, string text, int width, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            return lv.Columns.Add(text, S(width), align);
        }

        static readonly Dictionary<ListView, Action> fitters = new Dictionary<ListView, Action>();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>Reajusta a última coluna; chame antes do EndUpdate de listas longas.</summary>
        public static void Refit(ListView lv)
        {
            Action fit;
            if (!fitters.TryGetValue(lv, out fit) || !lv.IsHandleCreated) return;
            fit();              // antes do EndUpdate, para o Windows calcular as barras com a largura final
            lv.BeginInvoke(fit); // e de novo depois que o layout assentar
        }

        /// <summary>A última coluna ocupa o espaço que sobrar: sem barra de rolagem horizontal à toa.</summary>
        public static void FillLastColumn(ListView lv)
        {
            bool busy = false;
            Action fit = () =>
            {
                if (busy || lv.Columns.Count == 0 || lv.View != View.Details) return;
                busy = true;
                try
                {
                    int others = 0;
                    for (int i = 0; i < lv.Columns.Count - 1; i++) others += lv.Columns[i].Width;
                    int width = lv.ClientSize.Width;
                    // A barra vertical surge depois de preencher a lista sem mudar o ClientSize que o
                    // WinForms enxerga; se as linhas não cabem e ela ainda não está lá, reserva o espaço.
                    bool hasVScroll = lv.IsHandleCreated && (GetWindowLong(lv.Handle, -16) & 0x00200000) != 0;
                    if (!hasVScroll && lv.Items.Count > 0 && lv.IsHandleCreated)
                    {
                        // Altura da linha sem GetItemRect: ele lança exceção enquanto as linhas ainda não
                        // existem (durante Clear/BeginUpdate ou com grupos).
                        int row = Math.Max(lv.Font.Height + S(6), lv.SmallImageList != null ? lv.SmallImageList.ImageSize.Height : 0);
                        int rows = lv.Items.Count + (lv.ShowGroups ? lv.Groups.Count * 2 : 0) + 1; // +1 = cabeçalho
                        if (rows * row > lv.ClientSize.Height) width -= SystemInformation.VerticalScrollBarWidth;
                    }
                    lv.Columns[lv.Columns.Count - 1].Width = Math.Max(S(90), width - others - S(4));
                }
                catch { /* ajuste de layout é cosmético: nunca derruba o app */ }
                finally { busy = false; }
            };
            fitters[lv] = fit;
            lv.Disposed += (s, e) => fitters.Remove(lv);
            lv.ClientSizeChanged += (s, e) => fit();
            lv.ColumnWidthChanged += (s, e) => { if (e.ColumnIndex != lv.Columns.Count - 1) fit(); };
            lv.HandleCreated += (s, e) => fit();
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
                var ix = (ListViewItem)x;
                var iy = (ListViewItem)y;
                if (col >= ix.SubItems.Count || col >= iy.SubItems.Count) return 0;
                var a = ix.SubItems[col];
                var b = iy.SubItems[col];
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

        /// <summary>Célula em Consolas (caminhos, chaves de registro, IDs).</summary>
        public static ListViewItem.ListViewSubItem MonoCell(ListViewItem item, string text)
        {
            item.UseItemStyleForSubItems = false;
            var sub = new ListViewItem.ListViewSubItem(item, text ?? "") { Font = Mono, ForeColor = Grafite };
            item.SubItems.Add(sub);
            return sub;
        }

        /// <summary>Painel com borda fina, usado como "cartão".</summary>
        public static Panel MakeCard()
        {
            var p = new Panel { BackColor = Papel, Padding = new Padding(1) };
            p.Paint += (s, e) =>
            {
                using (var pen = new Pen(Linha))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            p.Resize += (s, e) => p.Invalidate();
            return p;
        }

        public static Label SectionLabel(string text)
        {
            return new Label { Text = text.ToUpperInvariant(), Font = Eyebrow, ForeColor = Grafite, AutoSize = true };
        }
    }

    /// <summary>Linha fina de progresso; anima de forma indeterminada quando o total é desconhecido.</summary>
    class ProgressLine : Control
    {
        int value, maximum = 1;
        bool indeterminate;
        float phase;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 };

        public ProgressLine()
        {
            Height = Theme.S(3);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            timer.Tick += (s, e) => { phase = (phase + 0.012f) % 1.3f; Invalidate(); };
            Visible = false;
        }

        public void Start(int max)
        {
            maximum = Math.Max(1, max);
            value = 0;
            indeterminate = false;
            timer.Stop();
            Visible = true;
            Invalidate();
        }

        public void StartIndeterminate()
        {
            indeterminate = true;
            phase = 0;
            Visible = true;
            if (Theme.Motion) timer.Start();
            Invalidate();
        }

        public int Value
        {
            set { this.value = Math.Min(value, maximum); Invalidate(); }
        }

        public void Stop()
        {
            timer.Stop();
            Visible = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Linha);
            using (var b = new SolidBrush(Theme.Verde))
            {
                if (indeterminate)
                {
                    float w = Width * 0.3f;
                    float x = Theme.Motion ? (phase - 0.3f) * Width : Width * 0.35f;
                    g.FillRectangle(b, x, 0, w, Height);
                }
                else g.FillRectangle(b, 0, 0, Width * (float)value / maximum, Height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Modelo único de página: cabeçalho (título e subtítulo), barra de ações (contexto à
    /// esquerda, ações à direita com a principal na ponta), linha de status e o conteúdo.
    /// </summary>
    class Page : UserControl
    {
        protected readonly MainForm Main;
        protected readonly Panel Body;
        protected readonly FlowLayoutPanel ToolbarLeft, ToolbarRight;
        readonly Panel toolbar, statusRow;
        readonly Label status;
        protected readonly ProgressLine Progress;

        public Page(MainForm main, string title, string subtitle)
        {
            Main = main;
            BackColor = Theme.Nevoa;
            Font = Theme.Base;
            ForeColor = Theme.Tinta;
            Padding = Theme.Pad(32, 22, 32, 20);

            Body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Nevoa };
            Controls.Add(Body);

            statusRow = new Panel { Dock = DockStyle.Top, Height = Theme.S(40) };
            status = new Label { Dock = DockStyle.Fill, Font = Theme.Status, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            Progress = new ProgressLine { Dock = DockStyle.Bottom };
            statusRow.Controls.Add(status);
            statusRow.Controls.Add(Progress);
            statusRow.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = Theme.S(8) });
            Controls.Add(statusRow);

            toolbar = new Panel { Dock = DockStyle.Top, Height = Theme.S(46) };
            ToolbarRight = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = Theme.Pad(0, 4, 0, 0)
            };
            ToolbarLeft = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = Theme.Pad(0, 4, 0, 0) };
            toolbar.Controls.Add(ToolbarLeft);
            toolbar.Controls.Add(ToolbarRight);
            Controls.Add(toolbar);

            var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(70) };
            header.Controls.Add(new Label { Text = subtitle, ForeColor = Theme.Grafite, AutoSize = true, Location = Theme.Pt(1, 38) });
            header.Controls.Add(new Label { Text = title, Font = Theme.Title, ForeColor = Theme.Tinta, AutoSize = true, Location = Theme.Pt(-1, 2) });
            Controls.Add(header);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            toolbar.Visible = ToolbarLeft.Controls.Count + ToolbarRight.Controls.Count > 0;
            statusRow.Visible = ShowStatusRow;
        }

        protected virtual bool ShowStatusRow { get { return true; } }

        /// <summary>Adiciona uma ação à direita. A primeira adicionada fica na ponta direita.</summary>
        protected Button AddAction(string text, ButtonKind kind, string icon, Action onClick)
        {
            var b = Theme.MakeButton(text, kind, icon);
            b.Click += (s, e) => onClick();
            ToolbarRight.Controls.Add(b);
            return b;
        }

        protected void SetStatus(string text, StatusKind kind = StatusKind.Normal)
        {
            status.Text = text;
            switch (kind)
            {
                case StatusKind.Good: status.ForeColor = Theme.Verde; break;
                case StatusKind.Warning: status.ForeColor = Theme.Ambar; break;
                case StatusKind.Danger: status.ForeColor = Theme.Vermelho; break;
                default: status.ForeColor = Theme.Tinta; break;
            }
        }

        protected string StatusText { get { return status.Text; } }

        /// <summary>Habilita o botão só quando há itens selecionados (ou marcados).</summary>
        protected static void BindToSelection(Button b, ListView lv, bool checkedItems = false, Func<bool> extra = null)
        {
            Action update = () =>
            {
                bool any = checkedItems ? lv.CheckedItems.Count > 0 : lv.SelectedItems.Count > 0;
                b.Enabled = any && (extra == null || extra());
            };
            lv.ItemSelectionChanged += (s, e) => update();
            lv.ItemChecked += (s, e) => update();
            lv.HandleCreated += (s, e) => update();
            b.Tag = update; // permite reavaliar depois de preencher a lista
            update();
        }

        protected static void Reevaluate(params Button[] buttons)
        {
            foreach (var b in buttons)
            {
                var a = b.Tag as Action;
                if (a != null) a();
            }
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
                        MessageBox.Show(this, error.Message, "OpenCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    done();
                });
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }

    /// <summary>Cartão com lista e um estado vazio que diz o que fazer.</summary>
    class ListCard : Panel
    {
        public readonly ListView List;
        readonly Panel empty;
        readonly IconBox emptyIcon;
        readonly Label emptyText;

        public ListCard(ListView list)
        {
            List = list;
            BackColor = Theme.Papel;
            Padding = new Padding(1);
            Paint += (s, e) => { using (var pen = new Pen(Theme.Linha)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); };
            Resize += (s, e) => Invalidate();

            empty = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Papel, Visible = false };
            emptyIcon = new IconBox("inbox", Theme.S(32), Theme.Grafite) { StrokeWidth = 1.5f };
            emptyText = new Label
            {
                AutoSize = true, MaximumSize = new Size(Theme.S(440), 0), TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Grafite, Font = Theme.Base
            };
            empty.Controls.Add(emptyIcon);
            empty.Controls.Add(emptyText);
            empty.Resize += (s, e) => CenterEmpty();
            Controls.Add(list);
            Controls.Add(empty);
            empty.BringToFront();
        }

        void CenterEmpty()
        {
            int total = emptyIcon.Height + Theme.S(12) + emptyText.Height;
            int top = Math.Max(Theme.S(16), (empty.Height - total) / 2 - Theme.S(10));
            emptyIcon.Location = new Point((empty.Width - emptyIcon.Width) / 2, top);
            emptyText.Location = new Point((empty.Width - emptyText.Width) / 2, top + emptyIcon.Height + Theme.S(12));
        }

        /// <summary>Mostra a mensagem quando a lista está vazia; texto null esconde o estado vazio.</summary>
        public void SetEmpty(string text, string icon = "inbox")
        {
            emptyText.Text = text ?? "";
            emptyIcon.Icon = icon;
            UpdateEmpty();
        }

        public void UpdateEmpty()
        {
            bool show = List.Items.Count == 0 && emptyText.Text.Length > 0;
            // Esconde a lista em vez de sobrepor: não depende da ordem de empilhamento.
            empty.Visible = show;
            List.Visible = !show;
            if (show) CenterEmpty();
            else Theme.Refit(List);
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
            Size = Theme.Sz(860, 520);
            StartPosition = FormStartPosition.CenterParent;
            Font = Theme.Base;
            BackColor = Theme.Papel;
            ShowIcon = false;
            MinimizeBox = false;

            var lv = Theme.MakeList();
            Theme.Col(lv, "Local", 640);
            Theme.Col(lv, "Tamanho", 110, HorizontalAlignment.Right);
            Theme.EnableSorting(lv);
            lv.Font = Theme.Mono;
            Controls.Add(lv);

            var info = new Label
            {
                Dock = DockStyle.Bottom, Height = Theme.S(32), TextAlign = ContentAlignment.MiddleLeft,
                Padding = Theme.Pad(8, 0, 0, 0), BackColor = Theme.Nevoa, ForeColor = Theme.Grafite
            };
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
                lv.Items.Add(new ListViewItem(new[] { rk.Key + (rk.Value != null ? "  [" + rk.Value + "]" : ""), "registro" }));
            if (r.SpecialItems > 0)
            {
                var it = new ListViewItem(r.Rule.Name + " (" + r.SpecialItems + " itens)");
                Theme.SizeCell(it, r.Bytes - r.Sizes.Sum());
                lv.Items.Add(it);
            }
            lv.EndUpdate();

            info.Text = r.ItemCount + " itens, " + Util.FormatSize(r.Bytes) +
                        (r.Files.Count > limit ? " (mostrando os primeiros " + limit + ")" : "") +
                        " · clique duas vezes para abrir o local";
            lv.DoubleClick += (s, e) =>
            {
                if (lv.SelectedItems.Count > 0 && lv.SelectedItems[0].Tag is string)
                    Util.ShowInExplorer((string)lv.SelectedItems[0].Tag);
            };
        }
    }

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
