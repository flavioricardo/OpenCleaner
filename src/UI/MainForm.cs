using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    class MainForm : Form
    {
        public readonly AppSettings Settings;
        public List<CleanRule> Rules;
        List<CleanRule> detected;
        readonly object detectLock = new object();

        readonly Panel content;
        readonly Dictionary<string, Page> pages = new Dictionary<string, Page>();
        readonly List<NavItem> navItems = new List<NavItem>();
        readonly Dictionary<string, Func<Page>> factories;

        public static string Version
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        public MainForm()
        {
            Settings = AppSettings.Load();
            Rules = RuleCatalog.LoadAll(Settings);

            Text = "OpenCleaner" + (Util.IsAdmin() ? " (administrador)" : "");
            Font = Theme.Base;
            BackColor = Theme.Nevoa;
            MinimumSize = Theme.Sz(920, 600);
            // Cabe na área útil da tela (em 1366x768 a janela de 760 px ficava sob a barra de tarefas).
            var area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Theme.S(1200), area.Width), Math.Min(Theme.S(780), area.Height));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            factories = new Dictionary<string, Func<Page>>
            {
                { "home", () => new HomePage(this) },
                { "clean", () => new CleanerPage(this) },
                { "registry", () => new RegistryPage(this) },
                { "uninstall", () => new UninstallPage(this) },
                { "startup", () => new StartupPage(this) },
                { "disk", () => new DiskPage(this) },
                { "dupes", () => new DuplicatePage(this) },
                { "updates", () => new SoftwareUpdatePage(this) },
                { "drivers", () => new DriverUpdatePage(this) },
                { "options", () => new OptionsPage(this) },
            };

            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Nevoa };
            Controls.Add(content);
            Controls.Add(BuildSidebar());
            Navigate("home");
        }

        Control BuildSidebar()
        {
            var side = new Panel { Dock = DockStyle.Left, Width = Theme.S(232), BackColor = Theme.Papel };
            side.Paint += (s, e) => { using (var p = new Pen(Theme.Linha)) e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height); };

            var items = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = Theme.Pad(12, 4, 12, 0), BackColor = Theme.Papel, AutoScroll = true
            };
            side.Controls.Add(items);

            Func<string, string, string, NavItem> nav = (key, icon, text) =>
            {
                var n = new NavItem(key, icon, text);
                n.Click += (s, e) => Navigate(key);
                navItems.Add(n);
                return n;
            };
            Action<string> heading = text =>
            {
                var l = Theme.SectionLabel(text);
                l.Margin = Theme.Pad(12, 14, 0, 6);
                items.Controls.Add(l);
            };

            items.Controls.Add(nav("home", "heart-pulse", "Verificação de saúde"));
            items.Controls.Add(nav("clean", "brush-cleaning", "Limpeza personalizada"));
            items.Controls.Add(nav("registry", "database", "Registro"));
            heading("Programas");
            items.Controls.Add(nav("updates", "refresh-cw", "Atualizar programas"));
            items.Controls.Add(nav("drivers", "cpu", "Atualizar drivers"));
            items.Controls.Add(nav("uninstall", "package-minus", "Desinstalar programas"));
            items.Controls.Add(nav("startup", "power", "Inicialização"));
            heading("Arquivos");
            items.Controls.Add(nav("disk", "chart-pie", "Analisador de disco"));
            items.Controls.Add(nav("dupes", "copy", "Localizador de duplicados"));

            var logo = new Panel { Dock = DockStyle.Top, Height = Theme.S(76), BackColor = Theme.Papel };
            logo.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var mark = new RectangleF(Theme.S(24f), Theme.S(22f), Theme.S(32f), Theme.S(32f));
                using (var path = Theme.Rounded(mark, Theme.S(8f)))
                using (var b = new SolidBrush(Theme.Verde)) g.FillPath(b, path);
                Lucide.Draw(g, "brush-cleaning", RectangleF.Inflate(mark, -Theme.S(7f), -Theme.S(7f)), Color.White);
                TextRenderer.DrawText(g, "OpenCleaner", Theme.Wordmark, new Point(Theme.S(64), Theme.S(25)), Theme.Tinta);
            };
            side.Controls.Add(logo);

            // Rodapé fixo: Opções e o modo de execução (o único lugar que fala de administrador).
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                Padding = Theme.Pad(12, 8, 12, 14), BackColor = Theme.Papel
            };
            footer.Controls.Add(nav("options", "settings", "Opções"));
            footer.Controls.Add(new ModeChip(this));
            side.Controls.Add(footer);
            return side;
        }

        public void RestartElevated()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" });
                Close();
            }
            catch (System.ComponentModel.Win32Exception) { /* usuário recusou o UAC */ }
        }

        /// <summary>
        /// Fluxo único para ações que precisam de administrador: explica o motivo e oferece reiniciar.
        /// Retorna true se o app já está elevado.
        /// </summary>
        public bool RequireAdmin(IWin32Window owner, string what)
        {
            if (Util.IsAdmin()) return true;
            if (MessageBox.Show(owner, what + " precisa de permissão de administrador.\n\nReiniciar o OpenCleaner como administrador?",
                                "Permissão de administrador", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                RestartElevated();
            return false;
        }

        public void Navigate(string key)
        {
            Page page;
            if (!pages.TryGetValue(key, out page))
            {
                page = factories[key]();
                page.Dock = DockStyle.Fill;
                pages[key] = page;
                content.Controls.Add(page);
            }
            foreach (var n in navItems) n.Active = n.Key == key;
            page.BringToFront();
            page.Visible = true;
            foreach (var p in pages.Values) if (p != page) p.Visible = false;
            page.Activated();
        }

        public T GetPage<T>(string key) where T : Page
        {
            Page p;
            return pages.TryGetValue(key, out p) ? p as T : null;
        }

        /// <summary>Regras cujo programa está instalado (calculado uma vez; seguro para threads).</summary>
        public List<CleanRule> DetectedRules()
        {
            lock (detectLock)
            {
                if (detected == null)
                    detected = Rules.Where(r => r.Group == RuleCatalog.CustomGroup || r.IsDetected()).ToList();
                return detected;
            }
        }

        public void ReloadRules()
        {
            lock (detectLock)
            {
                Rules = RuleCatalog.LoadAll(Settings);
                detected = null;
            }
            var cleaner = GetPage<CleanerPage>("clean");
            if (cleaner != null) cleaner.ReloadTree();
        }
    }

    /// <summary>Item do menu lateral: navegável por Tab, setas, Enter e Espaço, com foco visível.</summary>
    class NavItem : Control
    {
        public readonly string Key;
        readonly string icon;
        bool hover, active;

        public NavItem(string key, string icon, string text)
        {
            Key = key;
            this.icon = icon;
            Text = text;
            AccessibleName = text;
            AccessibleRole = AccessibleRole.PageTab;
            Size = Theme.Sz(208, 38);
            Margin = Theme.Pad(0, 1, 0, 1);
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        public bool Active
        {
            get { return active; }
            set { active = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                var form = FindForm();
                if (form != null) form.SelectNextControl(this, e.KeyCode == Keys.Down, true, true, true);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Papel);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new RectangleF(0, 0, Width - 1, Height - 1);
            if (active || hover)
                using (var path = Theme.Rounded(rect, Theme.S(6f)))
                using (var b = new SolidBrush(active ? Theme.VerdeNevoa : Theme.Hover)) g.FillPath(b, path);
            if (Focused && ShowFocusCues)
                using (var path = Theme.Rounded(RectangleF.Inflate(rect, -1, -1), Theme.S(6f)))
                using (var p = new Pen(Theme.Verde, Theme.S(2f))) g.DrawPath(p, path);

            Lucide.Draw(g, icon, new RectangleF(Theme.S(12f), (Height - Theme.S(18f)) / 2f, Theme.S(18f), Theme.S(18f)),
                        active ? Theme.Verde : Theme.Grafite, 1.75f);
            TextRenderer.DrawText(g, Text, active ? Theme.Bold : Theme.Base,
                new Rectangle(Theme.S(42), 0, Width - Theme.S(44), Height), active ? Theme.VerdeEscuro : Theme.Tinta,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Mostra se o app roda como administrador e, se não, oferece reiniciar.</summary>
    class ModeChip : Control
    {
        readonly MainForm main;
        readonly bool admin = Util.IsAdmin();
        bool hover;

        public ModeChip(MainForm main)
        {
            this.main = main;
            Size = Theme.Sz(208, 50);
            Margin = Theme.Pad(0, 8, 0, 0);
            TabStop = !admin;
            Cursor = admin ? Cursors.Default : Cursors.Hand;
            AccessibleName = admin ? "Executando como administrador" : "Reiniciar como administrador";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        }

        protected override void OnClick(EventArgs e) { if (!admin) main.RestartElevated(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!admin && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)) main.RestartElevated();
            base.OnKeyDown(e);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Papel);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(rect, Theme.S(6f)))
            {
                using (var b = new SolidBrush(hover && !admin ? Theme.Hover : Theme.Nevoa)) g.FillPath(b, path);
                if (Focused && ShowFocusCues) using (var p = new Pen(Theme.Verde, Theme.S(2f))) g.DrawPath(p, path);
            }
            Lucide.Draw(g, admin ? "shield-check" : "shield-check", new RectangleF(Theme.S(12f), Theme.S(9f), Theme.S(16f), Theme.S(16f)),
                        admin ? Theme.Verde : Theme.Grafite);
            TextRenderer.DrawText(g, admin ? "Administrador" : "Modo padrão", Theme.Bold, new Point(Theme.S(36), Theme.S(8)), Theme.Tinta);
            TextRenderer.DrawText(g, admin ? "Todas as funções liberadas" : "Reiniciar como administrador", Theme.Small,
                new Point(Theme.S(36), Theme.S(27)), admin ? Theme.Grafite : Theme.VerdeEscuro);
        }
    }
}
