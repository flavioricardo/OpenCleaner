using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    class RegistryPage : Page
    {
        /// <summary>Mostra o nome traduzido; a chave (em português) é o que vai para as configurações.</summary>
        class CategoryItem
        {
            public readonly string Key;
            public CategoryItem(string key) { Key = key; }
            public override string ToString() { return RegistryScanner.Label(Key); }
        }

        readonly CheckedListBox cats;
        readonly ListCard results;
        readonly Button btnScan, btnFix;
        bool busy;

        public RegistryPage(MainForm main)
            : base(main, L.T("Registro", "Registry"), L.T("Encontra entradas do registro que apontam para arquivos e programas que não existem mais.", "Finds registry entries that point to files and programs that no longer exist."))
        {
            btnFix = AddAction(L.T("Corrigir marcados", "Fix checked"), ButtonKind.Primary, "wrench", Fix);
            btnScan = AddAction(L.T("Procurar problemas", "Scan for issues"), ButtonKind.Secondary, "search", Scan);
            SetStatus(L.T("Nada foi verificado ainda.", "Nothing has been scanned yet."));

            var right = new Panel { Dock = DockStyle.Fill, Padding = Theme.Pad(16, 0, 0, 0) };
            var list = Theme.MakeList(true);
            Theme.Col(list, L.T("Problema", "Issue"), 240);
            Theme.Col(list, L.T("Arquivo ou pasta", "File or folder"), 220);
            Theme.Col(list, L.T("Chave do registro", "Registry key"), 300);
            Theme.EnableSorting(list);
            var menu = new ContextMenuStrip { Font = Theme.Base };
            menu.Items.Add(L.T("Marcar todos", "Check all"), null, (s, e) => CheckAll(true));
            menu.Items.Add(L.T("Desmarcar todos", "Uncheck all"), null, (s, e) => CheckAll(false));
            menu.Items.Add(L.T("Abrir no Editor do Registro", "Open in Registry Editor"), null, (s, e) => OpenInRegedit());
            list.ContextMenuStrip = menu;
            results = new ListCard(list) { Dock = DockStyle.Fill };
            results.SetEmpty(L.T("Clique em Procurar problemas.\nNada é alterado até você clicar em Corrigir marcados, e dá para salvar um backup antes.", "Click Scan for issues.\nNothing changes until you click Fix checked, and you can save a backup first."), "database");
            right.Controls.Add(results);
            Body.Controls.Add(right);
            BindToSelection(btnFix, list, true, () => !busy);

            var left = Theme.MakeCard();
            left.Dock = DockStyle.Left;
            left.Width = Theme.S(270);
            left.Padding = Theme.Pad(16, 14, 12, 12);
            cats = new CheckedListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, CheckOnClick = true, IntegralHeight = false,
                Font = Theme.Base, ForeColor = Theme.Tinta
            };
            cats.ItemHeight = Theme.S(24);
            foreach (var c in RegistryScanner.Categories)
                cats.Items.Add(new CategoryItem(c), !Main.Settings.RegistryDisabled.Contains(c));
            cats.ItemCheck += (s, e) =>
            {
                string c = ((CategoryItem)cats.Items[e.Index]).Key;
                Main.Settings.RegistryDisabled.Remove(c);
                if (e.NewValue == CheckState.Unchecked) Main.Settings.RegistryDisabled.Add(c);
                Main.Settings.Save();
            };
            left.Controls.Add(cats);
            var heading = Theme.SectionLabel(L.T("Verificar", "Scan"));
            heading.Dock = DockStyle.Top;
            heading.Padding = Theme.Pad(0, 0, 0, 8);
            left.Controls.Add(heading);
            Body.Controls.Add(left);
        }

        void CheckAll(bool on)
        {
            results.List.BeginUpdate();
            foreach (ListViewItem it in results.List.Items) it.Checked = on;
            results.List.EndUpdate();
        }

        void Scan()
        {
            var selected = cats.CheckedItems.Cast<CategoryItem>().Select(i => i.Key).ToList();
            if (selected.Count == 0) { SetStatus(L.T("Marque ao menos uma categoria à esquerda.", "Check at least one category on the left."), StatusKind.Warning); return; }
            busy = true;
            btnScan.Enabled = false;
            Reevaluate(btnFix);
            var list = results.List;
            list.Items.Clear();
            list.Groups.Clear();
            results.SetEmpty(null);
            Progress.StartIndeterminate();
            var scanner = new RegistryScanner();
            scanner.Progress = c => Ui(() => SetStatus(L.T("Verificando ", "Scanning ") + RegistryScanner.Label(c).ToLowerInvariant() + "..."));
            List<RegIssue> issues = null;
            RunBackground(() => issues = scanner.Scan(selected), () =>
            {
                busy = false;
                btnScan.Enabled = true;
                Progress.Stop();
                list.BeginUpdate();
                foreach (var g in issues.GroupBy(i => i.Category))
                {
                    var group = list.Groups.Add(g.Key, RegistryScanner.Label(g.Key) + " (" + g.Count() + ")");
                    foreach (var i in g)
                    {
                        var it = new ListViewItem(i.Problem, group) { Tag = i, Checked = true };
                        Theme.MonoCell(it, i.Data);
                        Theme.MonoCell(it, i.DisplayKey + (i.ValueName != null ? "  [" + i.ValueName + "]" : ""));
                        list.Items.Add(it);
                    }
                }
                Theme.Refit(list);
                list.EndUpdate();
                Reevaluate(btnFix);
                if (issues.Count == 0) SetStatus(L.T("Nenhum problema encontrado.", "No issues found."), StatusKind.Good);
                else SetStatus(issues.Count + L.T(" problemas encontrados. Revise e clique em Corrigir marcados.", " issues found. Review them and click Fix checked."));
                results.SetEmpty(L.T("Nenhuma entrada órfã nas categorias verificadas.", "No orphaned entries in the scanned categories."), "circle-check");
            });
        }

        void Fix()
        {
            var items = results.List.CheckedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0) return;

            // Itens do sistema (HKLM) exigem administrador; os do usuário atual, não.
            if (!Util.IsAdmin() && items.Any(i => ((RegIssue)i.Tag).Hive == RegistryHive.LocalMachine))
            {
                var answer = MessageBox.Show(this,
                    L.T("Alguns itens marcados são do sistema e só podem ser corrigidos com permissão de administrador.\n\n", "Some checked items belong to the system and can only be fixed with administrator permission.\n\n") +
                    L.T("Sim: reiniciar o OpenCleaner como administrador\nNão: corrigir só os itens do usuário atual\nCancelar: voltar", "Yes: restart OpenCleaner as administrator\nNo: fix only the current user's items\nCancel: go back"),
                    L.T("Permissão de administrador", "Administrator permission"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes) { Main.RestartElevated(); return; }
                items = items.Where(i => ((RegIssue)i.Tag).Hive != RegistryHive.LocalMachine).ToList();
                if (items.Count == 0) return;
            }
            var issues = items.Select(i => (RegIssue)i.Tag).ToList();

            var backup = MessageBox.Show(this, L.T("Salvar um backup (.reg) das entradas antes de corrigir?\nCom ele dá para desfazer a correção depois.", "Save a backup (.reg) of the entries before fixing?\nIt lets you undo the fix later."),
                                         L.T("Backup do registro", "Registry backup"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (backup == DialogResult.Cancel) return;
            if (backup == DialogResult.Yes)
            {
                string dir = Path.Combine(AppSettings.DataDir, "Backups");
                Directory.CreateDirectory(dir);
                using (var d = new SaveFileDialog
                {
                    InitialDirectory = dir, Filter = L.T("Arquivo de registro (*.reg)|*.reg", "Registry file (*.reg)|*.reg"),
                    FileName = "registro-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".reg"
                })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    Cursor = Cursors.WaitCursor;
                    string error;
                    bool ok = RegistryScanner.Backup(issues, d.FileName, out error);
                    Cursor = Cursors.Default;
                    if (!ok && MessageBox.Show(this, L.T("O backup não foi salvo: ", "The backup was not saved: ") + error + L.T("\n\nCorrigir mesmo assim?", "\n\nFix anyway?"),
                                               L.T("Backup do registro", "Registry backup"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                }
            }

            int fixedCount = 0, failed = 0;
            var list = results.List;
            list.BeginUpdate();
            foreach (var it in items)
            {
                if (RegistryScanner.Fix((RegIssue)it.Tag)) { fixedCount++; list.Items.Remove(it); }
                else { failed++; it.ForeColor = Theme.Vermelho; }
            }
            Theme.Refit(list);
            list.EndUpdate();
            Reevaluate(btnFix);
            if (failed > 0) SetStatus(fixedCount + L.T(" problemas corrigidos · ", " issues fixed · ") + failed + L.T(" não puderam ser alterados", " could not be changed"), StatusKind.Danger);
            else SetStatus(fixedCount + L.T(" problemas corrigidos.", " issues fixed."), StatusKind.Good);
            results.SetEmpty(L.T("Todos os problemas marcados foram corrigidos.", "All checked issues were fixed."), "circle-check");
        }

        void OpenInRegedit()
        {
            if (results.List.SelectedItems.Count == 0) return;
            var i = (RegIssue)results.List.SelectedItems[0].Tag;
            string root = i.Hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
            string path = i.KeyPath;
            if (i.View == RegistryView.Registry32 && i.Hive == RegistryHive.LocalMachine &&
                Environment.Is64BitOperatingSystem && path.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase))
                path = "SOFTWARE\\WOW6432Node\\" + path.Substring(9);
            try
            {
                // O Regedit usa o nome localizado da raiz ("Computador" em pt-BR).
                string computer = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "Computador" : "Computer";
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
                    k.SetValue("LastKey", computer + "\\" + root + "\\" + path);
                System.Diagnostics.Process.Start("regedit.exe", "/m");
            }
            catch { }
        }
    }
}
