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
            : base(main, L.T("Opções", "Options"), L.T("Ajuste como a limpeza funciona. As mudanças valem na hora.", "Adjust how cleaning works. Changes apply right away."))
        {
            var s = Main.Settings;
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true
            };
            Body.Controls.Add(flow);

            // Título nos dois idiomas: quem não lê o idioma atual ainda encontra a opção.
            flow.Controls.Add(Heading("Idioma · Language"));
            var lang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(300), Font = Theme.Base, Margin = Theme.Pad(0, 2, 0, 6) };
            string[] codes = { L.Auto, L.Pt, L.En };
            lang.Items.AddRange(new object[]
            {
                L.T("Automático (idioma do Windows)", "Automatic (Windows language)"),
                "Português (Brasil)",
                "English"
            });
            lang.SelectedIndex = Math.Max(0, Array.IndexOf(codes, s.Language));
            lang.SelectedIndexChanged += (o, e) =>
            {
                string chosen = codes[lang.SelectedIndex];
                if (chosen == s.Language) return;
                s.Language = chosen;
                s.Save();
                bool willBeEnglish = chosen == L.En || (chosen == L.Auto && !L.SystemIsPortuguese);
                if (willBeEnglish == L.English) return; // nada muda na tela
                if (MessageBox.Show(this, L.T("O novo idioma vale depois de reiniciar o OpenCleaner. Reiniciar agora?",
                                              "The new language applies after OpenCleaner restarts. Restart now?"),
                                    "OpenCleaner", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true });
                    Main.Close();
                }
            };
            flow.Controls.Add(lang);

            flow.Controls.Add(Heading(L.T("Limpeza", "Cleaning")));
            flow.Controls.Add(Check(L.T("Na pasta Temp, apagar só arquivos com mais de 24 horas", "In the Temp folder, only delete files older than 24 hours"), s.TempOlderThan24h, v => s.TempOlderThan24h = v));
            flow.Controls.Add(Check(L.T("Pedir confirmação antes de limpar", "Ask for confirmation before cleaning"), s.ConfirmClean, v => s.ConfirmClean = v));
            flow.Controls.Add(Check(L.T("Exclusão segura: sobrescrever cada arquivo com zeros antes de apagar (mais lento)", "Secure deletion: overwrite each file with zeros before deleting (slower)"), s.SecureDelete, v => s.SecureDelete = v));
            flow.Controls.Add(Check(L.T("Usar também as regras do winapp2.ini (lista mantida pela comunidade)", "Also use the winapp2.ini rules (community-maintained list)"), s.LoadWinapp2, v => { s.LoadWinapp2 = v; Main.ReloadRules(); }));

            var winappRow = new FlowLayoutPanel { AutoSize = true, Margin = Theme.Pad(20, 2, 0, 8), WrapContents = false };
            Func<string> winappText = () => File.Exists(RuleCatalog.Winapp2Path)
                ? L.T("winapp2.ini carregado de ", "winapp2.ini loaded from ") + AppSettings.DataDir
                : L.T("Para usar, salve um winapp2.ini em ", "To use it, save a winapp2.ini in ") + AppSettings.DataDir;
            var winappInfo = new Label { AutoSize = true, ForeColor = Theme.Grafite, Margin = Theme.Pad(0, 9, 8, 0), Text = winappText() };
            var openData = Theme.MakeButton(L.T("Abrir pasta", "Open folder"), ButtonKind.Secondary, "folder-open");
            openData.Click += (o, e) => { Directory.CreateDirectory(AppSettings.DataDir); Process.Start("explorer.exe", "\"" + AppSettings.DataDir + "\""); };
            var reload = Theme.MakeButton(L.T("Recarregar regras", "Reload rules"), ButtonKind.Secondary, "refresh-cw");
            reload.Click += (o, e) => { Main.ReloadRules(); winappInfo.Text = winappText(); };
            winappRow.Controls.AddRange(new Control[] { winappInfo, openData, reload });
            flow.Controls.Add(winappRow);

            flow.Controls.Add(Heading(L.T("Pastas personalizadas", "Custom folders")));
            flow.Controls.Add(Hint(L.T("Tudo dentro destas pastas é apagado pelo item Avançado > Pastas personalizadas da limpeza.", "Everything inside these folders is deleted by the Advanced > Custom folders cleaning item.")));
            flow.Controls.Add(FolderList(s.IncludeFolders, true));

            flow.Controls.Add(Heading(L.T("Nunca apagar", "Never delete")));
            flow.Controls.Add(Hint(L.T("Nada dentro destas pastas é apagado, por nenhum item.", "Nothing inside these folders is deleted by any item.")));
            flow.Controls.Add(FolderList(s.ExcludeFolders, false));

            flow.Controls.Add(Heading(L.T("Limpeza automática", "Automatic cleaning")));
            flow.Controls.Add(Hint(L.T("Para limpar sem abrir a janela, agende no Agendador de Tarefas o comando abaixo.\n", "To clean without opening the window, schedule the command below in Task Scheduler.\n") +
                                   L.T("Ele usa os itens marcados, pula programas abertos e registra o resultado em auto.log.", "It uses the checked items, skips open programs and logs the result to auto.log.")));
            var cmd = new TextBox
            {
                Text = "\"" + Application.ExecutablePath + "\" /AUTO", ReadOnly = true, Font = Theme.Mono,
                Width = Theme.S(560), Margin = Theme.Pad(0, 2, 0, 6), BackColor = Theme.Papel
            };
            flow.Controls.Add(cmd);

            var resetRow = new FlowLayoutPanel { AutoSize = true, Margin = Theme.Pad(0, 18, 0, 0), WrapContents = false };
            var reset = Theme.MakeButton(L.T("Voltar à seleção padrão da limpeza", "Restore the default cleaning selection"), ButtonKind.Secondary, "rotate-ccw");
            reset.Margin = Padding.Empty;
            var resetDone = new Label { AutoSize = true, ForeColor = Theme.Verde, Margin = Theme.Pad(12, 9, 0, 0) };
            reset.Click += (o, e) =>
            {
                s.Rules.Clear();
                s.Save();
                Main.ReloadRules();
                resetDone.Text = L.T("Seleção padrão restaurada.", "Default selection restored.");
            };
            resetRow.Controls.Add(reset);
            resetRow.Controls.Add(resetDone);
            flow.Controls.Add(resetRow);

            flow.Controls.Add(Heading(L.T("Sobre", "About")));
            flow.Controls.Add(Hint("OpenCleaner " + MainForm.Version + L.T(" · ícones Lucide (licença ISC) · sem telemetria.", " · Lucide icons (ISC license) · no telemetry.")));
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
            var add = Theme.MakeButton(L.T("Adicionar pasta...", "Add folder..."), ButtonKind.Secondary, "folder-open");
            var remove = Theme.MakeButton(L.T("Remover", "Remove"), ButtonKind.Secondary, "x");
            remove.Enabled = false;
            box.SelectedIndexChanged += (o, e) => remove.Enabled = box.SelectedItem != null;
            Action changed = () =>
            {
                Main.Settings.Save();
                if (reloadRules) Main.ReloadRules();
            };
            add.Click += (o, e) =>
            {
                var f = Prompt.Folder(this, L.T("Escolha a pasta", "Choose the folder"));
                if (f == null || folders.Contains(f)) return;
                if (reloadRules && Util.IsProtectedDir(f))
                {
                    MessageBox.Show(this, L.T("Esta é uma pasta do Windows ou do seu perfil e não pode ser apagada por inteiro.\nEscolha uma subpasta.", "This is a Windows or user profile folder and cannot be deleted entirely.\nChoose a subfolder."),
                                    L.T("Pasta protegida", "Protected folder"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
