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

            Text = "OpenCleaner" + (Util.IsAdmin() ? " (Administrador)" : "");
            Size = new Size(1180, 760);
            MinimumSize = new Size(960, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.Base;
            BackColor = Theme.Background;
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

            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            Controls.Add(content);
            Controls.Add(BuildSidebar());
            Navigate("home");
        }

        Control BuildSidebar()
        {
            var side = new Panel { Dock = DockStyle.Left, Width = 236, BackColor = Theme.Sidebar };

            var items = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = new Padding(10, 6, 10, 0), BackColor = Theme.Sidebar
            };
            side.Controls.Add(items);

            Action<string, string, string> nav = (key, icon, text) =>
            {
                var n = new NavItem(key, icon, text);
                n.Click += (s, e) => Navigate(key);
                navItems.Add(n);
                items.Controls.Add(n);
            };
            Action<string> heading = text => items.Controls.Add(new Label
            {
                Text = text, ForeColor = Color.FromArgb(120, 132, 150), Font = new Font("Segoe UI Semibold", 8f),
                AutoSize = false, Size = new Size(212, 26), TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(8, 0, 0, 4)
            });

            nav("home", "heart-pulse", "Verificação de saúde");
            nav("clean", "brush-cleaning", "Limpeza personalizada");
            nav("registry", "database", "Registro");
            heading("FERRAMENTAS");
            nav("updates", "refresh-cw", "Atualizar programas");
            nav("drivers", "cpu", "Atualizar drivers");
            nav("uninstall", "package-minus", "Desinstalar programas");
            nav("startup", "power", "Inicialização");
            nav("disk", "chart-pie", "Analisador de disco");
            nav("dupes", "copy", "Localizador de duplicados");
            heading("");
            nav("options", "settings", "Opções");

            var logo = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.Sidebar };
            logo.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, 20, 22, 34, 34);
                Lucide.Draw(g, "brush-cleaning", new RectangleF(28, 30, 18, 18), Color.White);
                using (var f = new Font("Segoe UI Semibold", 15f))
                    TextRenderer.DrawText(g, "OpenCleaner", f, new Point(60, 25), Color.White);
            };
            side.Controls.Add(logo);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Theme.Sidebar, Padding = new Padding(20, 6, 14, 10) };
            var ver = new Label { Text = "Versão " + Version, Dock = DockStyle.Bottom, Height = 20, ForeColor = Color.FromArgb(120, 132, 150) };
            footer.Controls.Add(ver);
            if (Util.IsAdmin())
            {
                var admin = new Panel { Dock = DockStyle.Top, Height = 24 };
                admin.Controls.Add(new Label { Text = "Executando como administrador", Location = new Point(22, 3), AutoSize = true, ForeColor = Theme.SidebarText });
                admin.Controls.Add(new IconBox("shield-check", 16, Color.FromArgb(110, 220, 205)) { Location = new Point(0, 3) });
                footer.Controls.Add(admin);
            }
            else
            {
                var elevate = new LinkLabel
                {
                    Text = "Reiniciar como administrador", Dock = DockStyle.Top, Height = 24,
                    LinkColor = Color.FromArgb(110, 210, 200), ActiveLinkColor = Color.White, LinkBehavior = LinkBehavior.HoverUnderline
                };
                elevate.Click += (s, e) => RestartElevated();
                footer.Controls.Add(elevate);
            }
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

    /// <summary>Item do menu lateral.</summary>
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
            Size = new Size(214, 38);
            Margin = new Padding(0, 1, 0, 1);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public bool Active
        {
            get { return active; }
            set { active = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(active ? Theme.SidebarActive : hover ? Theme.SidebarHover : Theme.Sidebar);
            if (active)
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, 8, 4, Height - 16);
            var color = active ? Color.White : Theme.SidebarText;
            Lucide.Draw(g, icon, new RectangleF(16, (Height - 18) / 2f, 18, 18), active ? Color.FromArgb(110, 220, 205) : color, 1.75f);
            TextRenderer.DrawText(g, Text, active ? Theme.Bold : Theme.Base, new Rectangle(48, 0, Width - 50, Height), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
