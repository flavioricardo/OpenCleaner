using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    class RegistryPage : Page
    {
        readonly CheckedListBox cats;
        readonly ListView list;
        readonly Label status;
        readonly Button btnScan, btnFix;
        RegistryScanner scanner;

        public RegistryPage(MainForm main)
            : base(main, "Registro", "Procura entradas do registro que apontam para arquivos e programas que não existem mais.")
        {
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 0, 0) };
            Body.Controls.Add(right);

            var card = Theme.MakeCard();
            card.Dock = DockStyle.Fill;
            right.Controls.Add(card);
            list = Theme.MakeList(true);
            list.Columns.Add("Problema", 250);
            list.Columns.Add("Dados", 260);
            list.Columns.Add("Chave do registro", 420);
            Theme.EnableSorting(list);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Marcar todos", null, (s, e) => CheckAll(true));
            menu.Items.Add("Desmarcar todos", null, (s, e) => CheckAll(false));
            menu.Items.Add("Abrir no Editor do Registro", null, (s, e) => OpenInRegedit());
            list.ContextMenuStrip = menu;
            card.Controls.Add(list);

            status = new Label { Dock = DockStyle.Top, Height = 44, Font = Theme.H2, TextAlign = ContentAlignment.MiddleLeft, Text = "Clique em Procurar problemas." };
            right.Controls.Add(status);

            var bar = Theme.MakeButtonBar();
            btnFix = Theme.MakeButton("Corrigir selecionados...", true, "wrench");
            btnScan = Theme.MakeButton("Procurar problemas", false, "search");
            btnFix.Enabled = false;
            btnFix.Click += (s, e) => Fix();
            btnScan.Click += (s, e) => Scan();
            bar.Controls.AddRange(new Control[] { btnFix, btnScan });
            right.Controls.Add(bar);

            var left = Theme.MakeCard();
            left.Dock = DockStyle.Left;
            left.Width = 270;
            left.Padding = new Padding(12);
            cats = new CheckedListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, CheckOnClick = true, IntegralHeight = false };
            foreach (var c in RegistryScanner.Categories)
                cats.Items.Add(c, !Main.Settings.RegistryDisabled.Contains(c));
            cats.ItemCheck += (s, e) =>
            {
                string c = (string)cats.Items[e.Index];
                Main.Settings.RegistryDisabled.Remove(c);
                if (e.NewValue == CheckState.Unchecked) Main.Settings.RegistryDisabled.Add(c);
                Main.Settings.Save();
            };
            left.Controls.Add(cats);
            if (!Util.IsAdmin())
                left.Controls.Add(new Label
                {
                    Text = "Itens em HKLM só podem ser corrigidos executando como administrador.",
                    Dock = DockStyle.Bottom, Height = 40, ForeColor = Theme.Muted
                });
            left.Controls.Add(new Label { Text = "Verificar", Font = Theme.H2, Dock = DockStyle.Top, Height = 32 });
            Body.Controls.Add(left);
        }

        void CheckAll(bool on)
        {
            list.BeginUpdate();
            foreach (ListViewItem it in list.Items) it.Checked = on;
            list.EndUpdate();
        }

        void Scan()
        {
            var selected = cats.CheckedItems.Cast<string>().ToList();
            if (selected.Count == 0) { status.Text = "Selecione ao menos uma categoria."; return; }
            btnScan.Enabled = btnFix.Enabled = false;
            list.Items.Clear();
            list.Groups.Clear();
            status.ForeColor = Theme.Text;
            scanner = new RegistryScanner();
            scanner.Progress = c => Ui(() => status.Text = "Verificando: " + c + "...");
            List<RegIssue> issues = null;
            RunBackground(() => issues = scanner.Scan(selected), () =>
            {
                btnScan.Enabled = true;
                list.BeginUpdate();
                foreach (var g in issues.GroupBy(i => i.Category))
                {
                    var group = list.Groups.Add(g.Key, g.Key + " (" + g.Count() + ")");
                    foreach (var i in g)
                        list.Items.Add(new ListViewItem(new[] { i.Problem, i.Data, i.DisplayKey + (i.ValueName != null ? "  [" + i.ValueName + "]" : "") }, group)
                        {
                            Tag = i, Checked = true
                        });
                }
                list.EndUpdate();
                btnFix.Enabled = issues.Count > 0;
                status.Text = issues.Count == 0 ? "Nenhum problema encontrado." : issues.Count + " problemas encontrados.";
                status.ForeColor = issues.Count == 0 ? Theme.Good : Theme.Text;
            });
        }

        void Fix()
        {
            var items = list.CheckedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0) { status.Text = "Nenhum item marcado."; return; }
            var issues = items.Select(i => (RegIssue)i.Tag).ToList();

            var answer = MessageBox.Show(this, "Deseja fazer backup das alterações no registro antes de corrigir?",
                                         "Backup do registro", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes)
            {
                string dir = Path.Combine(AppSettings.DataDir, "Backups");
                Directory.CreateDirectory(dir);
                using (var d = new SaveFileDialog
                {
                    InitialDirectory = dir, Filter = "Arquivo de registro (*.reg)|*.reg",
                    FileName = "registro-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".reg"
                })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    Cursor = Cursors.WaitCursor;
                    string error;
                    bool ok = RegistryScanner.Backup(issues, d.FileName, out error);
                    Cursor = Cursors.Default;
                    if (!ok && MessageBox.Show(this, "Não foi possível criar o backup: " + error + "\n\nCorrigir mesmo assim?",
                                               "Backup do registro", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                }
            }

            int fixedCount = 0, failed = 0;
            list.BeginUpdate();
            foreach (var it in items)
            {
                if (RegistryScanner.Fix((RegIssue)it.Tag)) { fixedCount++; list.Items.Remove(it); }
                else { failed++; it.ForeColor = Theme.Danger; }
            }
            list.EndUpdate();
            status.Text = fixedCount + " problemas corrigidos" + (failed > 0 ? " · " + failed + " sem permissão (execute como administrador)" : ".");
            status.ForeColor = failed > 0 ? Theme.Danger : Theme.Good;
            btnFix.Enabled = list.Items.Count > 0;
        }

        void OpenInRegedit()
        {
            if (list.SelectedItems.Count == 0) return;
            var i = (RegIssue)list.SelectedItems[0].Tag;
            string root = i.Hive == Microsoft.Win32.RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
            string path = i.KeyPath;
            if (i.View == Microsoft.Win32.RegistryView.Registry32 && i.Hive == Microsoft.Win32.RegistryHive.LocalMachine &&
                Environment.Is64BitOperatingSystem && path.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase))
                path = "SOFTWARE\\WOW6432Node\\" + path.Substring(9);
            try
            {
                // O Regedit usa o nome localizado da raiz ("Computador" em pt-BR).
                string computer = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "Computador" : "Computer";
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
                    k.SetValue("LastKey", computer + "\\" + root + "\\" + path);
                System.Diagnostics.Process.Start("regedit.exe", "/m");
            }
            catch { }
        }
    }
}
