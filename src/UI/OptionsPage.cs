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
        protected override bool ShowStatusRow { get { return false; } }

        public OptionsPage(MainForm main)
            : base(main, "Opções", "Ajuste como a limpeza funciona. As mudanças valem na hora.")
        {
            var s = Main.Settings;
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true
            };
            Body.Controls.Add(flow);

            flow.Controls.Add(Heading("Limpeza"));
            flow.Controls.Add(Check("Na pasta Temp, apagar só arquivos com mais de 24 horas", s.TempOlderThan24h, v => s.TempOlderThan24h = v));
            flow.Controls.Add(Check("Pedir confirmação antes de limpar", s.ConfirmClean, v => s.ConfirmClean = v));
            flow.Controls.Add(Check("Exclusão segura: sobrescrever cada arquivo com zeros antes de apagar (mais lento)", s.SecureDelete, v => s.SecureDelete = v));
            flow.Controls.Add(Check("Usar também as regras do winapp2.ini (lista mantida pela comunidade)", s.LoadWinapp2, v => { s.LoadWinapp2 = v; Main.ReloadRules(); }));

            var winappRow = new FlowLayoutPanel { AutoSize = true, Margin = Theme.Pad(20, 2, 0, 8), WrapContents = false };
            Func<string> winappText = () => File.Exists(RuleCatalog.Winapp2Path)
                ? "winapp2.ini carregado de " + AppSettings.DataDir
                : "Para usar, salve um winapp2.ini em " + AppSettings.DataDir;
            var winappInfo = new Label { AutoSize = true, ForeColor = Theme.Grafite, Margin = Theme.Pad(0, 9, 8, 0), Text = winappText() };
            var openData = Theme.MakeButton("Abrir pasta", ButtonKind.Secondary, "folder-open");
            openData.Click += (o, e) => { Directory.CreateDirectory(AppSettings.DataDir); Process.Start("explorer.exe", "\"" + AppSettings.DataDir + "\""); };
            var reload = Theme.MakeButton("Recarregar regras", ButtonKind.Secondary, "refresh-cw");
            reload.Click += (o, e) => { Main.ReloadRules(); winappInfo.Text = winappText(); };
            winappRow.Controls.AddRange(new Control[] { winappInfo, openData, reload });
            flow.Controls.Add(winappRow);

            flow.Controls.Add(Heading("Pastas personalizadas"));
            flow.Controls.Add(Hint("Tudo dentro destas pastas é apagado pelo item Avançado > Pastas personalizadas da limpeza."));
            flow.Controls.Add(FolderList(s.IncludeFolders, true));

            flow.Controls.Add(Heading("Nunca apagar"));
            flow.Controls.Add(Hint("Nada dentro destas pastas é apagado, por nenhum item."));
            flow.Controls.Add(FolderList(s.ExcludeFolders, false));

            flow.Controls.Add(Heading("Limpeza automática"));
            flow.Controls.Add(Hint("Para limpar sem abrir a janela, agende no Agendador de Tarefas o comando abaixo.\n" +
                                   "Ele usa os itens marcados, pula programas abertos e registra o resultado em auto.log."));
            var cmd = new TextBox
            {
                Text = "\"" + Application.ExecutablePath + "\" /AUTO", ReadOnly = true, Font = Theme.Mono,
                Width = Theme.S(560), Margin = Theme.Pad(0, 2, 0, 6), BackColor = Theme.Papel
            };
            flow.Controls.Add(cmd);

            var resetRow = new FlowLayoutPanel { AutoSize = true, Margin = Theme.Pad(0, 18, 0, 0), WrapContents = false };
            var reset = Theme.MakeButton("Voltar à seleção padrão da limpeza", ButtonKind.Secondary, "rotate-ccw");
            reset.Margin = Padding.Empty;
            var resetDone = new Label { AutoSize = true, ForeColor = Theme.Verde, Margin = Theme.Pad(12, 9, 0, 0) };
            reset.Click += (o, e) =>
            {
                s.Rules.Clear();
                s.Save();
                Main.ReloadRules();
                resetDone.Text = "Seleção padrão restaurada.";
            };
            resetRow.Controls.Add(reset);
            resetRow.Controls.Add(resetDone);
            flow.Controls.Add(resetRow);

            flow.Controls.Add(Heading("Sobre"));
            flow.Controls.Add(Hint("OpenCleaner " + MainForm.Version + " · ícones Lucide (licença ISC) · sem telemetria."));
        }

        static Label Heading(string text)
        {
            return new Label { Text = text, Font = Theme.H2, AutoSize = true, Margin = Theme.Pad(0, 18, 0, 6) };
        }

        static Label Hint(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Theme.Grafite, Margin = Theme.Pad(0, 0, 0, 6) };
        }

        CheckBox Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = Theme.Pad(0, 3, 0, 3) };
            c.CheckedChanged += (o, e) => { set(c.Checked); Main.Settings.Save(); };
            return c;
        }

        Control FolderList(List<string> folders, bool reloadRules)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var box = new ListBox { Size = Theme.Sz(520, 96), IntegralHeight = false, Font = Theme.Mono, BorderStyle = BorderStyle.FixedSingle };
            foreach (var f in folders) box.Items.Add(f);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            var add = Theme.MakeButton("Adicionar pasta...", ButtonKind.Secondary, "folder-open");
            var remove = Theme.MakeButton("Remover", ButtonKind.Secondary, "x");
            remove.Enabled = false;
            box.SelectedIndexChanged += (o, e) => remove.Enabled = box.SelectedItem != null;
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
                    MessageBox.Show(this, "Esta é uma pasta do Windows ou do seu perfil e não pode ser apagada por inteiro.\nEscolha uma subpasta.",
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
                remove.Enabled = false;
                changed();
            };
            add.Margin = remove.Margin = Theme.Pad(10, 0, 0, 6);
            buttons.Controls.Add(add);
            buttons.Controls.Add(remove);
            row.Controls.Add(box);
            row.Controls.Add(buttons);
            return row;
        }
    }
}
