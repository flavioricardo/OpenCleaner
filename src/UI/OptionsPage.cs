using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    class OptionsPage : Page
    {
        public OptionsPage(MainForm main)
            : base(main, "Opções", "Ajuste como a limpeza funciona.")
        {
            var s = Main.Settings;
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true
            };
            Body.Controls.Add(flow);

            flow.Controls.Add(Heading("Limpeza"));
            flow.Controls.Add(Check("Apagar da pasta Temp apenas arquivos com mais de 24 horas", s.TempOlderThan24h, v => s.TempOlderThan24h = v));
            flow.Controls.Add(Check("Pedir confirmação antes de limpar", s.ConfirmClean, v => s.ConfirmClean = v));
            flow.Controls.Add(Check("Exclusão segura: sobrescrever arquivos com zeros antes de apagar (mais lento)", s.SecureDelete, v => s.SecureDelete = v));
            flow.Controls.Add(Check("Carregar regras extras do winapp2.ini (formato da comunidade)", s.LoadWinapp2, v => { s.LoadWinapp2 = v; Main.ReloadRules(); }));

            var winappRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(22, 0, 0, 8) };
            var winappInfo = new Label
            {
                AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 8, 0),
                Text = File.Exists(RuleCatalog.Winapp2Path) ? "winapp2.ini encontrado em " + AppSettings.DataDir
                                                             : "Coloque um winapp2.ini em " + AppSettings.DataDir
            };
            var openData = Theme.MakeButton("Abrir pasta", false, "folder-open");
            openData.Click += (o, e) => { Directory.CreateDirectory(AppSettings.DataDir); Process.Start("explorer.exe", "\"" + AppSettings.DataDir + "\""); };
            var reload = Theme.MakeButton("Recarregar regras", false, "refresh-cw");
            reload.Click += (o, e) =>
            {
                Main.ReloadRules();
                winappInfo.Text = File.Exists(RuleCatalog.Winapp2Path) ? "winapp2.ini encontrado em " + AppSettings.DataDir
                                                                       : "Coloque um winapp2.ini em " + AppSettings.DataDir;
            };
            winappRow.Controls.AddRange(new Control[] { winappInfo, openData, reload });
            flow.Controls.Add(winappRow);

            flow.Controls.Add(Heading("Pastas personalizadas"));
            flow.Controls.Add(Hint("Todo o conteúdo destas pastas é apagado pelo item \"Avançado > Pastas personalizadas\" da limpeza."));
            flow.Controls.Add(FolderList(s.IncludeFolders, true));

            flow.Controls.Add(Heading("Exclusões"));
            flow.Controls.Add(Hint("Nada dentro destas pastas é apagado por nenhuma regra."));
            flow.Controls.Add(FolderList(s.ExcludeFolders, false));

            flow.Controls.Add(Heading("Linha de comando"));
            flow.Controls.Add(Hint("OpenCleaner.exe /AUTO   limpa em segundo plano com os itens marcados e grava o resultado em auto.log.\n" +
                                   "Use no Agendador de Tarefas para limpezas automáticas."));

            var reset = Theme.MakeButton("Restaurar seleção padrão de itens");
            reset.Margin = new Padding(0, 16, 0, 0);
            reset.Click += (o, e) =>
            {
                s.Rules.Clear();
                s.Save();
                Main.ReloadRules();
                MessageBox.Show(this, "Seleção de itens restaurada.", "Opções");
            };
            flow.Controls.Add(reset);
        }

        static Label Heading(string text)
        {
            return new Label { Text = text, Font = Theme.H2, AutoSize = true, Margin = new Padding(0, 14, 0, 6) };
        }

        static Label Hint(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 6) };
        }

        CheckBox Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
            c.CheckedChanged += (o, e) => { set(c.Checked); Main.Settings.Save(); };
            return c;
        }

        Control FolderList(List<string> folders, bool reloadRules)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var box = new ListBox { Width = 520, Height = 96, IntegralHeight = false };
            foreach (var f in folders) box.Items.Add(f);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
            var add = Theme.MakeButton("Adicionar...");
            var remove = Theme.MakeButton("Remover");
            Action changed = () =>
            {
                Main.Settings.Save();
                if (reloadRules) Main.ReloadRules();
            };
            add.Click += (o, e) =>
            {
                var f = Prompt.Folder(this, "Escolha a pasta");
                if (f == null || folders.Contains(f)) return;
                if (reloadRules && Util.IsProtectedDir(f))
                {
                    MessageBox.Show(this, "Esta é uma pasta do sistema ou do seu perfil e não pode ser limpa por inteiro.",
                                    "Pasta protegida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                folders.Add(f);
                box.Items.Add(f);
                changed();
            };
            remove.Click += (o, e) =>
            {
                if (box.SelectedItem == null) return;
                folders.Remove((string)box.SelectedItem);
                box.Items.Remove(box.SelectedItem);
                changed();
            };
            add.Margin = remove.Margin = new Padding(8, 0, 0, 6);
            buttons.Controls.Add(add);
            buttons.Controls.Add(remove);
            row.Controls.Add(box);
            row.Controls.Add(buttons);
            return row;
        }
    }
}
