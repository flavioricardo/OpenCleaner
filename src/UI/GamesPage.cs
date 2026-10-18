using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    /// <summary>
    /// PC para jogos: desfaz o que otimizadores mudaram e põe os jogos na placa dedicada em notebooks
    /// com duas placas. Verificar só lê; nada muda até Aplicar marcados.
    /// </summary>
    class GamesPage : Page
    {
        /// <summary>
        /// Pontos de troca para o teste de fumaça: de onde a página lê, onde grava e como pergunta ao usuário.
        /// No app ficam sempre nos valores padrão (o PC de verdade e os diálogos do Windows).
        /// </summary>
        public static class Hooks
        {
            public static Func<GameContext> Context;
            public static Func<GameSources> Sources;
            public static Func<bool> Hybrid;
            public static Func<RegistryKey> GpuRoot;
            public static Func<bool> IsAdmin;
            /// <summary>Responde às perguntas (texto, botões); null = MessageBox.</summary>
            public static Func<string, MessageBoxButtons, DialogResult> Ask;
            /// <summary>Caminho do backup sugerido -> caminho escolhido (null cancela); null = SaveFileDialog.</summary>
            public static Func<string, string> PickBackup;

            static Hooks() { Reset(); }

            public static void Reset()
            {
                Context = GameContext.System;
                Sources = GameSources.System;
                Hybrid = () => GpuPreference.IsHybridLaptop(GpuPreference.Adapters(), Util.HasBattery());
                GpuRoot = () => Registry.CurrentUser;
                IsAdmin = Util.IsAdmin;
                Ask = null;
                PickBackup = null;
            }
        }

        DialogResult AskUser(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Hooks.Ask != null ? Hooks.Ask(text, buttons) : MessageBox.Show(this, text, title, buttons, icon);
        }

        readonly ListCard results;
        readonly Label why;
        readonly LinkLabel openSettings;
        readonly Button btnScan, btnApply;
        string adaptersText = "";
        bool busy, scanned;

        public GamesPage(MainForm main)
            : base(main, L.T("PC para jogos", "Gaming check"), L.T("Desfaz o que otimizadores mudaram e põe cada jogo na placa de vídeo certa.", "Undoes what optimizers changed and puts each game on the right graphics card."))
        {
            btnApply = AddAction(L.T("Aplicar marcados", "Apply checked"), ButtonKind.Primary, "wrench", Apply);
            btnScan = AddAction(L.T("Verificar de novo", "Check again"), ButtonKind.Secondary, "refresh-cw", () => Scan());
            SetStatus(L.T("Nada foi verificado ainda.", "Nothing has been checked yet."));

            var list = Theme.MakeList(true);
            Theme.Col(list, L.T("Item", "Item"), 330);
            Theme.Col(list, L.T("Agora → Depois", "Now → After"), 300);
            Theme.Col(list, L.T("Onde", "Where"), 260);
            var menu = new ContextMenuStrip { Font = Theme.Base };
            menu.Items.Add(L.T("Marcar todos", "Check all"), null, (s, e) => CheckAll(true));
            menu.Items.Add(L.T("Desmarcar todos", "Uncheck all"), null, (s, e) => CheckAll(false));
            var reset = menu.Items.Add(L.T("Voltar este jogo para \"Windows decide\"", "Set this game back to \"Let Windows decide\""), null, (s, e) => ResetGpu());
            var folder = menu.Items.Add(L.T("Abrir a pasta do jogo", "Open the game folder"), null, (s, e) => { var g = SelectedGame(); if (g != null) Util.ShowInExplorer(g.Folder); });
            menu.Opening += (s, e) => reset.Visible = folder.Visible = SelectedGame() != null;
            list.ContextMenuStrip = menu;
            list.SelectedIndexChanged += (s, e) => ShowWhy();
            // Recomendações não se aplicam daqui: não têm caixa (sem ela, o clique não marca) e o link abre a
            // página do Windows. A barra de espaço marcaria mesmo sem caixa, então é ignorada nelas.
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space && list.SelectedItems.Cast<ListViewItem>().Any(IsAdvice)) e.SuppressKeyPress = e.Handled = true;
            };
            list.ItemActivate += (s, e) => OpenSelectedSettings();
            results = new ListCard(list) { Dock = DockStyle.Fill };
            results.SetEmpty(L.T("Verificando...", "Checking..."), "gamepad-2");
            BindToSelection(btnApply, list, true, () => !busy);

            var whyCard = Theme.MakeCard();
            whyCard.Dock = DockStyle.Bottom;
            whyCard.Height = Theme.S(104);
            whyCard.Padding = Theme.Pad(16, 12, 16, 12);
            why = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Grafite };
            openSettings = new LinkLabel
            {
                Dock = DockStyle.Bottom, AutoSize = true, Font = Theme.Bold, Visible = false,
                LinkColor = Theme.Verde, ActiveLinkColor = Theme.Verde, LinkBehavior = LinkBehavior.HoverUnderline
            };
            openSettings.LinkClicked += (s, e) => OpenSelectedSettings();
            whyCard.Controls.Add(why);
            whyCard.Controls.Add(openSettings);

            Body.Controls.Add(results);
            Body.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = Theme.S(12) });
            Body.Controls.Add(whyCard);
            ShowWhy();
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct LvItem
        {
            public int mask, iItem, iSubItem, state, stateMask;
            public IntPtr pszText;
            public int cchTextMax, iImage;
            public IntPtr lParam;
            public int iIndent, iGroupId, cColumns;
            public IntPtr puColumns;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LvItem lParam);

        /// <summary>Tira a caixa de seleção de uma linha (imagem de estado 0): recomendações não se aplicam daqui.</summary>
        static void HideCheckBox(ListView lv, int index)
        {
            if (!lv.IsHandleCreated) return;
            var item = new LvItem { stateMask = 0xF000, state = 0 }; // LVIS_STATEIMAGEMASK
            SendMessage(lv.Handle, 0x102B, (IntPtr)index, ref item);   // LVM_SETITEMSTATE
        }

        public override void Activated()
        {
            if (!scanned) Scan(); // só lê e leva menos de um segundo
        }

        InstalledGame SelectedGame()
        {
            return results.List.SelectedItems.Count == 0 ? null : results.List.SelectedItems[0].Tag as InstalledGame;
        }

        static bool IsAdvice(ListViewItem it)
        {
            var f = it.Tag as GameFinding;
            return f != null && f.Kind != GameKind.Fix;
        }

        void CheckAll(bool on)
        {
            results.List.BeginUpdate();
            foreach (ListViewItem it in results.List.Items)
                if (!IsAdvice(it)) it.Checked = on;
            results.List.EndUpdate();
        }

        GameFinding SelectedAdvice()
        {
            var f = results.List.SelectedItems.Count == 0 ? null : results.List.SelectedItems[0].Tag as GameFinding;
            return f != null && f.SettingsUri != null ? f : null;
        }

        void OpenSelectedSettings()
        {
            var f = SelectedAdvice();
            if (f != null && !GameContext.OpenSettings(f.SettingsUri))
                SetStatus(L.T("Não foi possível abrir a página do Windows.", "Could not open the Windows page."), StatusKind.Danger);
        }

        void ShowWhy()
        {
            var advice = SelectedAdvice();
            openSettings.Visible = advice != null;
            if (advice != null)
                openSettings.Text = advice.SettingsUri == "main.cpl" ? L.T("Abrir Propriedades do Mouse", "Open Mouse Properties")
                                  : advice.SettingsUri.StartsWith("windowsdefender:") ? L.T("Abrir Segurança do Windows", "Open Windows Security")
                                  : L.T("Abrir nas Configurações", "Open in Settings");
            if (results.List.SelectedItems.Count == 0)
            {
                why.Text = L.T("Selecione um item para ver por que ele importa. Itens de otimizadores voltam ao padrão do Windows; dá para salvar um backup antes. As recomendações só abrem a página certa do Windows.",
                               "Select an item to see why it matters. Optimizer items go back to the Windows default; you can save a backup first. Recommendations only open the right Windows page.");
                return;
            }
            var tag = results.List.SelectedItems[0].Tag;
            var f = tag as GameFinding;
            if (f != null) { why.Text = f.Why; return; }
            var g = (InstalledGame)tag;
            why.Text = L.T("Em notebooks com duas placas de vídeo (", "On laptops with two graphics cards (") + adaptersText +
                       L.T("), o Windows pode rodar o jogo na integrada, mais fraca. Alto desempenho faz o jogo usar a placa dedicada sempre que abrir, e gasta mais bateria enquanto ele roda. É a mesma opção de Configurações > Sistema > Tela > Elementos gráficos, só para o seu usuário. Executáveis: ",
                           "), Windows may run the game on the weaker integrated one. High performance makes the game use the dedicated card every time it starts, and uses more battery while it runs. It is the same option as Settings > System > Display > Graphics, for your user only. Executables: ") +
                       string.Join(", ", g.Exes.Select(Path.GetFileName));
        }

        /// <summary>Verifica (só lê) e preenche a lista; no fim chama then, com a lista já atualizada.</summary>
        void Scan(Action then = null)
        {
            if (busy) return;
            busy = true;
            scanned = true;
            btnScan.Enabled = false;
            Reevaluate(btnApply);
            var list = results.List;
            list.Items.Clear();
            list.Groups.Clear();
            results.SetEmpty(null);
            Progress.StartIndeterminate();
            SetStatus(L.T("Verificando...", "Checking..."));
            List<GameFinding> findings = null;
            List<InstalledGame> games = null;
            var states = new Dictionary<InstalledGame, int>();
            bool hybrid = false;
            RunBackground(() =>
            {
                findings = GameScanner.Scan(Hooks.Context());
                hybrid = Hooks.Hybrid();
                if (hybrid) adaptersText = string.Join(" + ", GpuPreference.Adapters().Select(a => a.Name));
                games = hybrid ? GameLibrary.Find(Hooks.Sources()) : new List<InstalledGame>();
                var gpuRoot = Hooks.GpuRoot();
                foreach (var g in games) states[g] = GpuPreference.StateOf(gpuRoot, g);
            }, () =>
            {
                busy = false;
                btnScan.Enabled = true;
                Progress.Stop();
                findings = findings ?? new List<GameFinding>();
                games = games ?? new List<InstalledGame>();
                var fixes = findings.Where(f => f.Kind == GameKind.Fix).ToList();
                var advice = findings.Where(f => f.Kind == GameKind.Recommended).ToList();
                var tradeoffs = findings.Where(f => f.Kind == GameKind.Tradeoff).ToList();
                list.BeginUpdate();
                Action<string, string, List<GameFinding>> addGroup = (key, title, items) =>
                {
                    if (items.Count == 0) return;
                    var group = list.Groups.Add(key, title + " (" + items.Count + ")");
                    foreach (var f in items)
                    {
                        var it = new ListViewItem(f.Name, group) { Tag = f, Checked = f.Kind == GameKind.Fix && f.DefaultChecked };
                        it.SubItems.Add(f.Current + " → " + f.Expected);
                        Theme.MonoCell(it, f.Location);
                        if (f.Kind == GameKind.Tradeoff || f.Security) it.ForeColor = Theme.Ambar; // segurança vem primeiro (ordem do scanner)
                        list.Items.Add(it);
                    }
                };
                addGroup("fix", L.T("Deixado por otimizadores", "Left by optimizers"), fixes);
                int pending = 0;
                if (games.Count > 0)
                {
                    var group = list.Groups.Add("gpu", L.T("Placa de vídeo dos jogos", "Graphics card for games") + " (" + games.Count + ")");
                    foreach (var g in games)
                    {
                        int state = states.ContainsKey(g) ? states[g] : GpuPreference.WindowsDecides;
                        bool ok = state == GpuPreference.HighPerformance;
                        if (!ok) pending++;
                        var it = new ListViewItem(g.Name + "  ·  " + g.Store, group) { Tag = g, Checked = !ok };
                        it.UseItemStyleForSubItems = false;
                        var cell = it.SubItems.Add(ok ? GpuPreference.Label(state) : GpuPreference.Label(state) + " → " + GpuPreference.Label(GpuPreference.HighPerformance));
                        cell.ForeColor = state == GpuPreference.PowerSaving ? Theme.Ambar : ok ? Theme.Grafite : Theme.Tinta;
                        Theme.MonoCell(it, g.Exes[0] + (g.Exes.Count > 1 ? "  (+" + (g.Exes.Count - 1) + ")" : ""));
                        list.Items.Add(it);
                    }
                }
                addGroup("advice", L.T("Recomendado para jogos: muda nas Configurações", "Recommended for gaming: change it in Settings"), advice);
                addGroup("tradeoff", L.T("Desempenho × segurança: escolha sua", "Performance × security: your choice"), tradeoffs);
                Theme.Refit(list);
                list.EndUpdate();
                foreach (ListViewItem it in list.Items)
                    if (IsAdvice(it)) HideCheckBox(list, it.Index);
                Reevaluate(btnApply);

                var parts = new List<string>();
                if (fixes.Count == 1) parts.Add(L.T("1 ajuste deixado por otimizadores", "1 setting left by optimizers"));
                else if (fixes.Count > 1) parts.Add(L.F("{0} ajustes deixados por otimizadores", "{0} settings left by optimizers", fixes.Count));
                if (pending == 1) parts.Add(L.T("1 jogo fora da placa dedicada", "1 game not set to the dedicated card"));
                else if (pending > 1) parts.Add(L.F("{0} jogos fora da placa dedicada", "{0} games not set to the dedicated card", pending));
                if (parts.Count > 0)
                    SetStatus(string.Join(L.T(" e ", " and "), parts) + L.T(". Revise e clique em Aplicar marcados.", ". Review them and click Apply checked."));
                else if (advice.Count + tradeoffs.Count > 0)
                    SetStatus(L.T("Nada para consertar. Veja abaixo as opções do Windows que valem para jogos.", "Nothing to fix. See below the Windows options that matter for gaming."), StatusKind.Good);
                else
                    SetStatus(L.T("Tudo certo para jogar.", "All set for gaming.") +
                              (hybrid ? "" : L.T(" Este PC não é um notebook com duas placas de vídeo, então não há placa para escolher.", " This PC is not a laptop with two graphics cards, so there is no card to choose.")), StatusKind.Good);
                results.SetEmpty(L.T("Nenhum ajuste de otimizador encontrado.\nA verificação só olha: nada muda até você aplicar.",
                                     "No optimizer settings found.\nThe check only looks: nothing changes until you apply."), "circle-check");
                ShowWhy();
                if (then != null) then();
            });
        }

        void Apply()
        {
            var items = results.List.CheckedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0 || busy) return;

            // Políticas em HKLM exigem administrador; o resto é do usuário atual.
            Func<ListViewItem, bool> admin = i => { var f = i.Tag as GameFinding; return f != null && f.NeedsAdmin; };
            if (!Hooks.IsAdmin() && items.Any(admin))
            {
                var answer = AskUser(
                    L.T("Alguns itens marcados são do sistema e só podem ser alterados com permissão de administrador.\n\n", "Some checked items belong to the system and can only be changed with administrator permission.\n\n") +
                    L.T("Sim: reiniciar o OpenCleaner como administrador\nNão: aplicar só os itens do usuário atual\nCancelar: voltar", "Yes: restart OpenCleaner as administrator\nNo: apply only the current user's items\nCancel: go back"),
                    L.T("Permissão de administrador", "Administrator permission"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes) { Main.RestartElevated(); return; }
                items = items.Where(i => !admin(i)).ToList();
                if (items.Count == 0) return;
            }
            var findings = items.Select(i => i.Tag).OfType<GameFinding>().Where(f => f.Kind == GameKind.Fix).ToList();
            var games = items.Select(i => i.Tag).OfType<InstalledGame>().ToList();

            var gpuRoot = Hooks.GpuRoot();
            var keys = GameScanner.BackupKeys(findings).ToList();
            if (games.Count > 0) keys.Add(new KeyValuePair<string, RegistryView>(gpuRoot.Name + "\\" + GpuPreference.Key, gpuRoot.View));
            // Abrir a Store ou trocar o plano de energia não mexe no registro: aí não há o que salvar.
            var backup = keys.Count == 0 ? DialogResult.No
                : AskUser(L.T("Salvar um backup (.reg) do que vai mudar?\nCom ele dá para desfazer depois.", "Save a backup (.reg) of what will change?\nIt lets you undo it later."),
                          L.T("Backup", "Backup"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (backup == DialogResult.Cancel) return;
            if (backup == DialogResult.Yes)
            {
                string dir = Path.Combine(AppSettings.DataDir, "Backups");
                string suggested = Path.Combine(dir, L.T("jogos-", "games-") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".reg");
                string target = PickBackupFile(suggested);
                if (target == null) return;
                Cursor = Cursors.WaitCursor;
                string error;
                bool saved = Util.ExportRegistry(keys, target, out error);
                Cursor = Cursors.Default;
                if (!saved && AskUser(L.T("O backup não foi salvo: ", "The backup was not saved: ") + error + L.T("\n\nAplicar mesmo assim?", "\n\nApply anyway?"),
                                      L.T("Backup", "Backup"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            int done = 0, failed = 0;
            Cursor = Cursors.WaitCursor;
            foreach (var f in findings)
                if (GameScanner.Fix(f)) done++; else failed++;
            foreach (var g in games)
            {
                bool ok = true;
                foreach (var exe in g.Exes) ok &= GpuPreference.Set(gpuRoot, exe, GpuPreference.HighPerformance);
                if (ok) done++; else failed++;
            }
            Cursor = Cursors.Default;
            // O passo que falta ao usuário (instalar pela Store) vem primeiro: a linha de status corta o fim.
            string msg = string.Concat(findings.Where(f => f.AfterApply != null).Select(f => f.AfterApply + " ")) +
                         done + L.T(" itens aplicados", " items applied") + (failed > 0 ? " · " + failed + L.T(" não puderam ser alterados", " could not be changed") : ".") +
                         (games.Count > 0 ? L.T(" Os jogos usam a placa nova na próxima vez que abrirem.", " Games use the new card the next time they start.") : "");
            Scan(() => SetStatus(msg, failed > 0 ? StatusKind.Danger : StatusKind.Good));
        }

        void ResetGpu()
        {
            var g = SelectedGame();
            if (g == null || busy) return;
            var gpuRoot = Hooks.GpuRoot();
            foreach (var exe in g.Exes) GpuPreference.Set(gpuRoot, exe, GpuPreference.WindowsDecides);
            Scan();
        }

        /// <summary>Onde salvar o backup; null se o usuário cancelar.</summary>
        string PickBackupFile(string suggested)
        {
            if (Hooks.PickBackup != null) return Hooks.PickBackup(suggested);
            Directory.CreateDirectory(Path.GetDirectoryName(suggested));
            using (var d = new SaveFileDialog
            {
                InitialDirectory = Path.GetDirectoryName(suggested), FileName = Path.GetFileName(suggested),
                Filter = L.T("Arquivo de registro (*.reg)|*.reg", "Registry file (*.reg)|*.reg")
            })
                return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
        }
    }
}
