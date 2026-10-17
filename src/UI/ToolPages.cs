using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    // ======================= Desinstalar programas =======================

    class UninstallPage : Page
    {
        readonly ListCard results;
        readonly TextBox search;
        readonly Button btnUninstall, btnOpen;
        readonly ImageList icons;
        List<InstalledProgram> programs = new List<InstalledProgram>();
        bool loaded;

        public UninstallPage(MainForm main)
            : base(main, L.T("Desinstalar programas", "Uninstall programs"), L.T("Remova programas instalados ou entradas de programas que já não existem.", "Remove installed programs or entries of programs that no longer exist."))
        {
            btnUninstall = AddAction(L.T("Desinstalar", "Uninstall"), ButtonKind.Primary, "trash-2", Uninstall);
            btnOpen = AddAction(L.T("Abrir local", "Open location"), ButtonKind.Secondary, "folder-open", OpenLocation);
            ToolbarRight.Controls.Add(Theme.MakeMenuButton(L.T("Mais", "More"),
                Theme.Item(L.T("Remover da lista...", "Remove from list..."), RemoveEntry),
                Theme.Item(L.T("Atualizar lista", "Refresh list"), Reload),
                Theme.Item(L.T("Salvar lista como texto...", "Save list as text..."), Export)));

            search = new TextBox { Width = Theme.S(300), Font = Theme.Base, Margin = Theme.Pad(0, 5, 0, 0) };
            SetCue(search, L.T("Pesquisar por nome ou desenvolvedor", "Search by name or developer"));
            search.TextChanged += (s, e) => Fill();
            ToolbarLeft.Controls.Add(search);

            var list = Theme.MakeList();
            icons = new ImageList { ImageSize = Theme.Sz(24, 24), ColorDepth = ColorDepth.Depth32Bit };
            list.SmallImageList = icons;
            Theme.Col(list, L.T("Programa", "Program"), 330);
            Theme.Col(list, L.T("Desenvolvedor", "Developer"), 200);
            Theme.Col(list, L.T("Instalado em", "Installed on"), 100);
            Theme.Col(list, L.T("Tamanho", "Size"), 90, HorizontalAlignment.Right);
            Theme.Col(list, L.T("Versão", "Version"), 120);
            Theme.EnableSorting(list);
            list.DoubleClick += (s, e) => Uninstall();
            results = new ListCard(list) { Dock = DockStyle.Fill };
            Body.Controls.Add(results);
            BindToSelection(btnUninstall, list);
            BindToSelection(btnOpen, list);
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
            SetStatus(L.T("Lendo a lista de programas...", "Reading the list of programs..."));
            Progress.StartIndeterminate();
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
                Progress.Stop();
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
            var list = results.List;
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
            Theme.Refit(list);
            list.EndUpdate();
            Reevaluate(btnUninstall, btnOpen);
            long total = filtered.Sum(p => p.SizeKB) * 1024;
            SetStatus(q.Length == 0
                ? filtered.Count + L.T(" programas instalados · ", " programs installed · ") + Util.FormatSize(total) + L.T(" estimados", " estimated")
                : filtered.Count + L.T(" de ", " of ") + programs.Count + L.T(" programas", " programs"));
            results.SetEmpty(q.Length == 0 ? L.T("Nenhum programa encontrado.", "No programs found.") : L.T("Nenhum programa corresponde a \"", "No program matches \"") + q + "\".", "search");
        }

        InstalledProgram Selected
        {
            get { return results.List.SelectedItems.Count == 0 ? null : (InstalledProgram)results.List.SelectedItems[0].Tag; }
        }

        void Uninstall()
        {
            var p = Selected;
            if (p == null) return;
            if (MessageBox.Show(this, L.T("Abrir o desinstalador de \"", "Open the uninstaller for \"") + p.Name + "\"?", L.T("Desinstalar", "Uninstall"),
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                var proc = Programs.Uninstall(p);
                SetStatus(L.T("Desinstalador de ", "Uninstaller for ") + p.Name + L.T(" aberto. A lista é atualizada quando ele fechar.", " opened. The list refreshes when it closes."));
                if (proc != null) RunBackground(() => proc.WaitForExit(), Reload);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.T("O desinstalador não abriu: ", "The uninstaller did not open: ") + ex.Message +
                                L.T("\n\nSe o programa já foi apagado, use Mais > Remover da lista.",
                                    "\n\nIf the program was already deleted, use More > Remove from list."), L.T("Desinstalar", "Uninstall"),
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void RemoveEntry()
        {
            var p = Selected;
            if (p == null) { SetStatus(L.T("Selecione um programa na lista.", "Select a program in the list."), StatusKind.Warning); return; }
            if (p.Hive == RegistryHive.LocalMachine && !Main.RequireAdmin(this, L.T("Remover este programa da lista", "Removing this program from the list"))) return;
            if (MessageBox.Show(this, L.T("Remover \"", "Remove \"") + p.Name + L.T("\" da lista de programas instalados?\n\n", "\" from the list of installed programs?\n\n") +
                                L.T("Isto não desinstala nada. Use só para programas que já foram apagados e continuam aparecendo.", "This does not uninstall anything. Use it only for programs that were already deleted and still show up."),
                                L.T("Remover da lista", "Remove from list"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (Programs.DeleteEntry(p)) { programs.Remove(p); Fill(); SetStatus(p.Name + L.T(" removido da lista.", " removed from the list."), StatusKind.Good); }
            else SetStatus(L.T("Não foi possível remover ", "Could not remove ") + p.Name + L.T(" da lista.", " from the list."), StatusKind.Danger);
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
            else SetStatus(L.T("A pasta de instalação de ", "The installation folder of ") + p.Name + L.T(" não é conhecida.", " is unknown."), StatusKind.Warning);
        }

        void Export()
        {
            using (var d = new SaveFileDialog { Filter = L.T("Texto (*.txt)|*.txt", "Text (*.txt)|*.txt"), FileName = "programas-instalados.txt" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder();
                foreach (var p in programs)
                    sb.AppendLine(string.Join("\t", p.Name, p.Publisher, p.Version, p.InstallDate));
                File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
                SetStatus(L.T("Lista salva em ", "List saved to ") + d.FileName, StatusKind.Good);
            }
        }
    }

    // ======================= Inicialização =======================

    class StartupPage : Page
    {
        readonly ListCard results;
        readonly Button btnToggle, btnDelete, btnOpen;

        public StartupPage(MainForm main)
            : base(main, L.T("Inicialização", "Startup"), L.T("Programas que abrem junto com o Windows. Desativar pode ser desfeito; excluir, não.", "Programs that open with Windows. Disabling can be undone; deleting cannot."))
        {
            btnToggle = AddAction(L.T("Desativar", "Disable"), ButtonKind.Primary, "toggle-left", ToggleSelected);
            btnDelete = AddAction(L.T("Excluir", "Delete"), ButtonKind.Danger, "trash-2", Delete);
            btnOpen = AddAction(L.T("Abrir local", "Open location"), ButtonKind.Secondary, "folder-open", OpenLocation);
            ToolbarRight.Controls.Add(Theme.MakeMenuButton(L.T("Mais", "More"), Theme.Item(L.T("Atualizar lista", "Refresh list"), Reload)));

            var list = Theme.MakeList();
            Theme.Col(list, "Status", 100);
            Theme.Col(list, L.T("Programa", "Program"), 220);
            Theme.Col(list, L.T("Desenvolvedor", "Developer"), 180);
            Theme.Col(list, L.T("Origem", "Source"), 170);
            Theme.Col(list, L.T("Comando", "Command"), 300);
            Theme.EnableSorting(list);
            list.SelectedIndexChanged += (s, e) => UpdateToggle();
            results = new ListCard(list) { Dock = DockStyle.Fill };
            Body.Controls.Add(results);
            BindToSelection(btnToggle, list);
            BindToSelection(btnDelete, list);
            BindToSelection(btnOpen, list);
        }

        public override void Activated() { Reload(); }

        void Reload()
        {
            var entries = Startup.List();
            var list = results.List;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var e in entries.OrderBy(x => !x.Enabled).ThenBy(x => x.Name))
            {
                var it = new ListViewItem(e.Enabled ? L.T("Ativo", "Enabled") : L.T("Desativado", "Disabled")) { Tag = e };
                it.SubItems.Add(e.Name);
                it.SubItems.Add(e.Publisher);
                it.SubItems.Add(e.Location);
                Theme.MonoCell(it, e.Command);
                if (!e.Enabled) foreach (ListViewItem.ListViewSubItem sub in it.SubItems) sub.ForeColor = Theme.Grafite;
                else it.SubItems[0].ForeColor = Theme.Verde;
                list.Items.Add(it);
            }
            Theme.Refit(list);
            list.EndUpdate();
            Reevaluate(btnToggle, btnDelete, btnOpen);
            int on = entries.Count(x => x.Enabled);
            SetStatus(on + L.T(" de ", " of ") + entries.Count + L.T(" programas abrem com o Windows.", " programs open with Windows."));
            results.SetEmpty(L.T("Nenhum programa abre junto com o Windows.", "No programs open with Windows."), "power");
        }

        StartupEntry Selected
        {
            get { return results.List.SelectedItems.Count == 0 ? null : (StartupEntry)results.List.SelectedItems[0].Tag; }
        }

        /// <summary>O botão principal alterna entre Ativar e Desativar conforme o item selecionado.</summary>
        void UpdateToggle()
        {
            var e = Selected;
            bool enable = e != null && !e.Enabled;
            btnToggle.Text = enable ? L.T("Ativar", "Enable") : L.T("Desativar", "Disable");
        }

        void ToggleSelected()
        {
            var e = Selected;
            if (e == null) return;
            if (e.Hive == RegistryHive.LocalMachine && !Main.RequireAdmin(this, L.T("Alterar um programa de todos os usuários", "Changing a program for all users"))) return;
            bool enable = !e.Enabled;
            try
            {
                Startup.SetEnabled(e, enable);
                SetStatus(e.Name + (enable ? L.T(" ativado.", " enabled.") : L.T(" desativado.", " disabled.")), StatusKind.Good);
            }
            catch (Exception ex) { SetStatus(L.T("Não foi possível alterar ", "Could not change ") + e.Name + ": " + ex.Message, StatusKind.Danger); }
            string key = e.Name;
            string msg = StatusText;
            Reload();
            SetStatus(msg, StatusKind.Good);
            foreach (ListViewItem it in results.List.Items)
                if (((StartupEntry)it.Tag).Name == key) { it.Selected = true; it.Focused = true; break; }
        }

        void Delete()
        {
            var e = Selected;
            if (e == null) return;
            if (e.Hive == RegistryHive.LocalMachine && !Main.RequireAdmin(this, L.T("Excluir um programa de todos os usuários", "Deleting a program for all users"))) return;
            if (MessageBox.Show(this, L.T("Excluir \"", "Delete \"") + e.Name + L.T("\" da inicialização?\n\nIsto não pode ser desfeito. Se quiser só impedir que abra, use Desativar.",
                                                                             "\" from startup?\n\nThis cannot be undone. To just keep it from opening, use Disable."),
                                L.T("Excluir", "Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                Startup.Delete(e);
                Reload();
                SetStatus(e.Name + L.T(" excluído da inicialização.", " deleted from startup."), StatusKind.Good);
            }
            catch (Exception ex) { SetStatus(L.T("Não foi possível excluir ", "Could not delete ") + e.Name + ": " + ex.Message, StatusKind.Danger); }
        }

        void OpenLocation()
        {
            var e = Selected;
            if (e == null) return;
            string path = e.IsFile ? e.FilePath : Util.ExtractExePath(e.Command);
            if (path != null && (File.Exists(path) || Directory.Exists(path))) Util.ShowInExplorer(path);
            else SetStatus(L.T("O arquivo de ", "The file for ") + e.Name + L.T(" não existe mais: ", " no longer exists: ") + (path ?? e.Command), StatusKind.Warning);
        }
    }
}
