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
            : base(main, "Desinstalar programas", "Remova programas instalados ou entradas de programas que já não existem.")
        {
            btnUninstall = AddAction("Desinstalar", ButtonKind.Primary, "trash-2", Uninstall);
            btnOpen = AddAction("Abrir local", ButtonKind.Secondary, "folder-open", OpenLocation);
            ToolbarRight.Controls.Add(Theme.MakeMenuButton("Mais",
                Theme.Item("Remover da lista...", RemoveEntry),
                Theme.Item("Atualizar lista", Reload),
                Theme.Item("Salvar lista como texto...", Export)));

            search = new TextBox { Width = Theme.S(300), Font = Theme.Base, Margin = Theme.Pad(0, 5, 0, 0) };
            SetCue(search, "Pesquisar por nome ou desenvolvedor");
            search.TextChanged += (s, e) => Fill();
            ToolbarLeft.Controls.Add(search);

            var list = Theme.MakeList();
            icons = new ImageList { ImageSize = Theme.Sz(24, 24), ColorDepth = ColorDepth.Depth32Bit };
            list.SmallImageList = icons;
            Theme.Col(list, "Programa", 330);
            Theme.Col(list, "Desenvolvedor", 200);
            Theme.Col(list, "Instalado em", 100);
            Theme.Col(list, "Tamanho", 90, HorizontalAlignment.Right);
            Theme.Col(list, "Versão", 120);
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
            SetStatus("Lendo a lista de programas...");
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
                ? filtered.Count + " programas instalados · " + Util.FormatSize(total) + " estimados"
                : filtered.Count + " de " + programs.Count + " programas");
            results.SetEmpty(q.Length == 0 ? "Nenhum programa encontrado." : "Nenhum programa corresponde a \"" + q + "\".", "search");
        }

        InstalledProgram Selected
        {
            get { return results.List.SelectedItems.Count == 0 ? null : (InstalledProgram)results.List.SelectedItems[0].Tag; }
        }

        void Uninstall()
        {
            var p = Selected;
            if (p == null) return;
            if (MessageBox.Show(this, "Abrir o desinstalador de \"" + p.Name + "\"?", "Desinstalar",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                var proc = Programs.Uninstall(p);
                SetStatus("Desinstalador de " + p.Name + " aberto. A lista é atualizada quando ele fechar.");
                if (proc != null) RunBackground(() => proc.WaitForExit(), Reload);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "O desinstalador não abriu: " + ex.Message +
                                "\n\nSe o programa já foi apagado, use Mais > Remover da lista.", "Desinstalar",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void RemoveEntry()
        {
            var p = Selected;
            if (p == null) { SetStatus("Selecione um programa na lista.", StatusKind.Warning); return; }
            if (p.Hive == RegistryHive.LocalMachine && !Main.RequireAdmin(this, "Remover este programa da lista")) return;
            if (MessageBox.Show(this, "Remover \"" + p.Name + "\" da lista de programas instalados?\n\n" +
                                "Isto não desinstala nada. Use só para programas que já foram apagados e continuam aparecendo.",
                                "Remover da lista", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (Programs.DeleteEntry(p)) { programs.Remove(p); Fill(); SetStatus(p.Name + " removido da lista.", StatusKind.Good); }
            else SetStatus("Não foi possível remover " + p.Name + " da lista.", StatusKind.Danger);
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
            else SetStatus("A pasta de instalação de " + p.Name + " não é conhecida.", StatusKind.Warning);
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
                SetStatus("Lista salva em " + d.FileName, StatusKind.Good);
            }
        }
    }

    // ======================= Inicialização =======================

    class StartupPage : Page
    {
        readonly ListCard results;
        readonly Button btnToggle, btnDelete, btnOpen;

        public StartupPage(MainForm main)
            : base(main, "Inicialização", "Programas que abrem junto com o Windows. Desativar pode ser desfeito; excluir, não.")
        {
            btnToggle = AddAction("Desativar", ButtonKind.Primary, "toggle-left", ToggleSelected);
            btnDelete = AddAction("Excluir", ButtonKind.Danger, "trash-2", Delete);
            btnOpen = AddAction("Abrir local", ButtonKind.Secondary, "folder-open", OpenLocation);
            ToolbarRight.Controls.Add(Theme.MakeMenuButton("Mais", Theme.Item("Atualizar lista", Reload)));

            var list = Theme.MakeList();
            Theme.Col(list, "Status", 100);
            Theme.Col(list, "Programa", 220);
            Theme.Col(list, "Desenvolvedor", 180);
            Theme.Col(list, "Origem", 170);
            Theme.Col(list, "Comando", 300);
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
                var it = new ListViewItem(e.Enabled ? "Ativo" : "Desativado") { Tag = e };
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
            SetStatus(on + " de " + entries.Count + " programas abrem com o Windows.");
            results.SetEmpty("Nenhum programa abre junto com o Windows.", "power");
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
            btnToggle.Text = enable ? "Ativar" : "Desativar";
        }

        void ToggleSelected()
        {
            var e = Selected;
            if (e == null) return;
            if (e.Hive == RegistryHive.LocalMachine && !Main.RequireAdmin(this, "Alterar um programa de todos os usuários")) return;
            bool enable = !e.Enabled;
            try
            {
                Startup.SetEnabled(e, enable);
                SetStatus(e.Name + (enable ? " ativado." : " desativado."), StatusKind.Good);
            }
            catch (Exception ex) { SetStatus("Não foi possível alterar " + e.Name + ": " + ex.Message, StatusKind.Danger); }
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
            if (e.Hive == RegistryHive.LocalMachine && !Main.RequireAdmin(this, "Excluir um programa de todos os usuários")) return;
            if (MessageBox.Show(this, "Excluir \"" + e.Name + "\" da inicialização?\n\nIsto não pode ser desfeito. Se quiser só impedir que abra, use Desativar.",
                                "Excluir", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                Startup.Delete(e);
                Reload();
                SetStatus(e.Name + " excluído da inicialização.", StatusKind.Good);
            }
            catch (Exception ex) { SetStatus("Não foi possível excluir " + e.Name + ": " + ex.Message, StatusKind.Danger); }
        }

        void OpenLocation()
        {
            var e = Selected;
            if (e == null) return;
            string path = e.IsFile ? e.FilePath : Util.ExtractExePath(e.Command);
            if (path != null && (File.Exists(path) || Directory.Exists(path))) Util.ShowInExplorer(path);
            else SetStatus("O arquivo de " + e.Name + " não existe mais: " + (path ?? e.Command), StatusKind.Warning);
        }
    }
}
