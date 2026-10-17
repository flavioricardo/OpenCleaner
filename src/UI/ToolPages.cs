using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    // ======================= Desinstalar programas =======================

    class UninstallPage : Page
    {
        readonly ListView list;
        readonly TextBox search;
        readonly Label status;
        readonly ImageList icons = new ImageList { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
        List<InstalledProgram> programs = new List<InstalledProgram>();
        bool loaded;

        public UninstallPage(MainForm main)
            : base(main, "Desinstalar programas", "Remova programas instalados ou entradas que ficaram para trás.")
        {
            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 220, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16, 0, 0, 0) };
            SoftwareUpdatePage.SideButton(side, "Desinstalar", true, "trash-2", Uninstall);
            SoftwareUpdatePage.SideButton(side, "Excluir entrada", false, "x", DeleteEntry);
            SoftwareUpdatePage.SideButton(side, "Abrir local", false, "folder-open", OpenLocation);
            SoftwareUpdatePage.SideButton(side, "Atualizar lista", false, "refresh-cw", Reload);
            SoftwareUpdatePage.SideButton(side, "Salvar lista...", false, "save", Export);

            var card = Theme.MakeCard();
            card.Dock = DockStyle.Fill;
            list = Theme.MakeList();
            list.SmallImageList = icons;
            list.Columns.Add("Programa", 340);
            list.Columns.Add("Editor", 200);
            list.Columns.Add("Instalado em", 100);
            list.Columns.Add("Tamanho", 90, HorizontalAlignment.Right);
            list.Columns.Add("Versão", 130);
            Theme.EnableSorting(list);
            list.DoubleClick += (s, e) => Uninstall();
            card.Controls.Add(list);

            Body.Controls.Add(card);
            Body.Controls.Add(side);

            var top = new Panel { Dock = DockStyle.Top, Height = 44 };
            search = new TextBox { Width = 320, Location = new Point(0, 6), Font = Theme.Base };
            SetCue(search, "Pesquisar programas...");
            search.TextChanged += (s, e) => Fill();
            status = new Label { AutoSize = true, Location = new Point(336, 10), ForeColor = Theme.Muted };
            top.Controls.Add(search);
            top.Controls.Add(status);
            Body.Controls.Add(top);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public static void SetCue(TextBox box, string text)
        {
            box.HandleCreated += (s, e) => SendMessage(box.Handle, 0x1501, (IntPtr)1, text);
        }

        public override void Activated()
        {
            if (!loaded) Reload();
        }

        void Reload()
        {
            loaded = true;
            status.Text = "Carregando...";
            List<InstalledProgram> result = null;
            var iconMap = new Dictionary<string, Icon>();
            RunBackground(() =>
            {
                result = Programs.List();
                foreach (var p in result)
                {
                    string path = IconPath(p);
                    if (path == null || iconMap.ContainsKey(path)) continue;
                    try { iconMap[path] = Icon.ExtractAssociatedIcon(path); }
                    catch { iconMap[path] = null; }
                }
            }, () =>
            {
                programs = result;
                icons.Images.Clear();
                foreach (var kv in iconMap)
                    if (kv.Value != null) icons.Images.Add(kv.Key, kv.Value);
                Fill();
            });
        }

        static string IconPath(InstalledProgram p)
        {
            string s = p.DisplayIcon;
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().Trim('"');
            int comma = s.LastIndexOf(',');
            if (comma > 2) s = s.Substring(0, comma).Trim('"');
            s = Environment.ExpandEnvironmentVariables(s);
            return File.Exists(s) ? s : null;
        }

        void Fill()
        {
            string q = search.Text.Trim();
            var filtered = programs.Where(p => q.Length == 0 ||
                p.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                (p.Publisher ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var p in filtered)
            {
                var it = new ListViewItem(p.Name) { Tag = p, ImageKey = IconPath(p) ?? "" };
                it.SubItems.Add(p.Publisher ?? "");
                DateTime d;
                var dateCell = new ListViewItem.ListViewSubItem(it, p.InstallDate);
                if (DateTime.TryParseExact(p.InstallDate, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out d)) dateCell.Tag = d;
                it.SubItems.Add(dateCell);
                if (p.SizeKB > 0) Theme.SizeCell(it, p.SizeKB * 1024);
                else it.SubItems.Add(new ListViewItem.ListViewSubItem(it, "") { Tag = 0L });
                it.SubItems.Add(p.Version ?? "");
                list.Items.Add(it);
            }
            list.EndUpdate();
            long total = filtered.Sum(p => p.SizeKB) * 1024;
            status.Text = filtered.Count + " programas · " + Util.FormatSize(total) + " (estimado)";
        }

        InstalledProgram Selected
        {
            get { return list.SelectedItems.Count == 0 ? null : (InstalledProgram)list.SelectedItems[0].Tag; }
        }

        void Uninstall()
        {
            var p = Selected;
            if (p == null) return;
            if (MessageBox.Show(this, "Executar o desinstalador de \"" + p.Name + "\"?", "Desinstalar",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                var proc = Programs.Uninstall(p);
                status.Text = "Desinstalador iniciado. A lista será atualizada quando ele terminar.";
                if (proc != null)
                    RunBackground(() => proc.WaitForExit(), Reload);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível iniciar o desinstalador:\n" + ex.Message +
                                "\n\nSe o programa já foi removido, use \"Excluir entrada\".", "Desinstalar",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void DeleteEntry()
        {
            var p = Selected;
            if (p == null) return;
            if (MessageBox.Show(this, "Remover \"" + p.Name + "\" da lista de programas instalados?\n\n" +
                                "Isto NÃO desinstala o programa; use apenas para entradas de programas que já foram removidos.",
                                "Excluir entrada", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (Programs.DeleteEntry(p)) { programs.Remove(p); Fill(); }
            else MessageBox.Show(this, "Sem permissão. Execute o OpenCleaner como administrador.", "Excluir entrada",
                                 MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void OpenLocation()
        {
            var p = Selected;
            if (p == null) return;
            string dir = p.InstallLocation;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir.Trim('"')))
            {
                string exe = Util.ExtractExePath(p.UninstallString);
                dir = exe != null ? Path.GetDirectoryName(exe) : null;
            }
            if (dir != null && Directory.Exists(dir.Trim('"'))) Util.ShowInExplorer(dir.Trim('"'));
            else status.Text = "Local de instalação desconhecido.";
        }

        void Export()
        {
            using (var d = new SaveFileDialog { Filter = "Texto (*.txt)|*.txt", FileName = "programas-instalados.txt" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder();
                foreach (var p in programs)
                    sb.AppendLine(string.Join("\t", p.Name, p.Publisher, p.Version, p.InstallDate));
                File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
            }
        }
    }

    // ======================= Inicialização =======================

    class StartupPage : Page
    {
        readonly ListView list;
        readonly Label status;

        public StartupPage(MainForm main)
            : base(main, "Inicialização", "Programas que abrem junto com o Windows. Desativar é reversível; excluir não.")
        {
            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 220, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16, 0, 0, 0) };
            SoftwareUpdatePage.SideButton(side, "Ativar", false, "toggle-right", () => SetEnabled(true));
            SoftwareUpdatePage.SideButton(side, "Desativar", true, "toggle-left", () => SetEnabled(false));
            SoftwareUpdatePage.SideButton(side, "Excluir", false, "trash-2", Delete);
            SoftwareUpdatePage.SideButton(side, "Abrir local", false, "folder-open", OpenLocation);
            SoftwareUpdatePage.SideButton(side, "Atualizar lista", false, "refresh-cw", Reload);

            var card = Theme.MakeCard();
            card.Dock = DockStyle.Fill;
            list = Theme.MakeList();
            list.Columns.Add("Ativado", 70);
            list.Columns.Add("Programa", 220);
            list.Columns.Add("Editor", 180);
            list.Columns.Add("Local", 180);
            list.Columns.Add("Comando", 420);
            Theme.EnableSorting(list);
            card.Controls.Add(list);

            status = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Muted };
            Body.Controls.Add(card);
            Body.Controls.Add(side);
            Body.Controls.Add(status);
        }

        public override void Activated() { Reload(); }

        void Reload()
        {
            var entries = Startup.List();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var e in entries.OrderBy(x => !x.Enabled).ThenBy(x => x.Name))
            {
                var it = new ListViewItem(new[] { e.Enabled ? "Sim" : "Não", e.Name, e.Publisher, e.Location, e.Command }) { Tag = e };
                if (!e.Enabled) it.ForeColor = Theme.Muted;
                list.Items.Add(it);
            }
            list.EndUpdate();
            int on = entries.Count(x => x.Enabled);
            status.Text = on + " de " + entries.Count + " programas ativados na inicialização" +
                          (Util.IsAdmin() ? "" : " · alterar itens de \"Todos os usuários\" exige administrador");
        }

        StartupEntry Selected
        {
            get { return list.SelectedItems.Count == 0 ? null : (StartupEntry)list.SelectedItems[0].Tag; }
        }

        void SetEnabled(bool on)
        {
            var e = Selected;
            if (e == null) return;
            try { Startup.SetEnabled(e, on); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível alterar: " + ex.Message, "Inicialização", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            Reload();
        }

        void Delete()
        {
            var e = Selected;
            if (e == null) return;
            if (MessageBox.Show(this, "Remover \"" + e.Name + "\" da inicialização?\n\nPrefira Desativar se quiser poder voltar atrás.",
                                "Excluir", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { Startup.Delete(e); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível excluir: " + ex.Message, "Inicialização", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            Reload();
        }

        void OpenLocation()
        {
            var e = Selected;
            if (e == null) return;
            string path = e.IsFile ? e.FilePath : Util.ExtractExePath(e.Command);
            if (path != null && (File.Exists(path) || Directory.Exists(path))) Util.ShowInExplorer(path);
            else status.Text = "Arquivo não encontrado: " + (path ?? e.Command);
        }
    }
}
