using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    /// <summary>Faixa de aviso em Âmbar, com ícone.</summary>
    class Notice : Panel
    {
        public Notice(string text, string icon = "triangle-alert")
        {
            Dock = DockStyle.Top;
            Height = Theme.S(52);
            Padding = Theme.Pad(0, 0, 0, 12);
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.AmbarNevoa };
            inner.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = Theme.Pad(0, 0, 8, 0),
                ForeColor = Theme.Ambar, Text = text
            });
            var slot = new Panel { Dock = DockStyle.Left, Width = Theme.S(40) };
            slot.Controls.Add(new IconBox(icon, Theme.S(18), Theme.Ambar) { Location = Theme.Pt(13, 11) });
            inner.Controls.Add(slot);
            Controls.Add(inner);
        }
    }

    // ======================= Atualizar programas (winget) =======================

    class SoftwareUpdatePage : Page
    {
        const int StatusCol = 4;
        readonly ListCard results;
        readonly Button btnCheck, btnUpdate;
        bool loaded, busy, wingetMissing;

        public SoftwareUpdatePage(MainForm main)
            : base(main, L.T("Atualizar programas", "Update programs"), L.T("Programas com versão nova disponível, instalados pelo Gerenciador de Pacotes do Windows (winget).", "Programs with a newer version available, installed through Windows Package Manager (winget)."))
        {
            btnUpdate = AddAction(L.T("Atualizar marcados", "Update checked"), ButtonKind.Primary, "download", UpdateChecked);
            btnCheck = AddAction(L.T("Procurar atualizações", "Check for updates"), ButtonKind.Secondary, "refresh-cw", Check);
            var markAll = Theme.MakeButton(L.T("Marcar todos", "Check all"), ButtonKind.Link, "list-checks");
            markAll.Margin = Theme.Pad(0, 0, 4, 0);
            markAll.Click += (s, e) => SetAll(true);
            var clear = Theme.MakeButton(L.T("Desmarcar todos", "Uncheck all"), ButtonKind.Link, "x");
            clear.Click += (s, e) => SetAll(false);
            ToolbarLeft.Controls.Add(markAll);
            ToolbarLeft.Controls.Add(clear);

            var list = Theme.MakeList(true);
            Theme.Col(list, L.T("Programa", "Program"), 280);
            Theme.Col(list, L.T("Instalada", "Installed"), 120);
            Theme.Col(list, L.T("Disponível", "Available"), 120);
            Theme.Col(list, L.T("ID no winget", "winget ID"), 200);
            Theme.Col(list, L.T("Situação", "Progress"), 200);
            Theme.EnableSorting(list);
            results = new ListCard(list) { Dock = DockStyle.Fill };
            Body.Controls.Add(results);
            BindToSelection(btnUpdate, list, true, () => !busy);
        }

        public override void Activated()
        {
            if (!loaded) Check();
        }

        void SetAll(bool on)
        {
            foreach (ListViewItem it in results.List.Items) it.Checked = on;
        }

        void Check()
        {
            if (busy) return;
            if (wingetMissing) { Process.Start("ms-windows-store://pdp/?productid=9NBLGGH4NNS1"); return; }
            loaded = true;
            busy = true;
            btnCheck.Enabled = false;
            Reevaluate(btnUpdate);
            SetStatus(L.T("Procurando atualizações...", "Looking for updates..."));
            Progress.StartIndeterminate();
            results.List.Items.Clear();
            results.SetEmpty(null);
            List<PackageUpdate> found = null;
            bool available = false;
            string version = null;
            RunBackground(() =>
            {
                available = Winget.IsAvailable(out version);
                if (available) found = Winget.ListUpgrades();
            }, () =>
            {
                busy = false;
                btnCheck.Enabled = true;
                Progress.Stop();
                if (!available)
                {
                    wingetMissing = true;
                    btnCheck.Text = L.T("Instalar o winget", "Install winget");
                    SetStatus(L.T("O winget não está instalado neste computador.", "winget is not installed on this computer."), StatusKind.Warning);
                    results.SetEmpty(L.T("Para atualizar programas, instale o \"Instalador de Aplicativo\" da Microsoft Store\n(botão Instalar o winget, acima) e volte aqui.", "To update programs, install \"App Installer\" from the Microsoft Store\n(Install winget button, above) and come back here."), "download");
                    Reevaluate(btnUpdate);
                    return;
                }
                if (found == null)
                {
                    SetStatus(L.T("O winget não respondeu. Tente Procurar atualizações de novo.", "winget did not respond. Try Check for updates again."), StatusKind.Danger);
                    return;
                }
                var list = results.List;
                list.BeginUpdate();
                foreach (var p in found)
                {
                    var it = new ListViewItem(p.Name) { Tag = p, Checked = !p.Explicit };
                    it.SubItems.Add(p.Version);
                    it.SubItems.Add(p.Available);
                    Theme.MonoCell(it, p.Id);
                    it.SubItems.Add(p.Explicit ? L.T("Só atualiza se marcado", "Updates only when checked") : "");
                    list.Items.Add(it);
                }
                Theme.Refit(list);
                list.EndUpdate();
                Reevaluate(btnUpdate);
                if (found.Count == 0) SetStatus(L.T("Todos os programas estão atualizados.", "All programs are up to date."), StatusKind.Good);
                else SetStatus(found.Count + L.T(" programas têm versão nova. Feche-os antes de atualizar.", " programs have a newer version. Close them before updating."));
                results.SetEmpty(L.T("Todos os programas conhecidos pelo winget estão na versão mais recente.", "Every program known to winget is on its latest version."), "circle-check");
            });
        }

        void UpdateChecked()
        {
            if (busy) return;
            var items = results.List.CheckedItems.Cast<ListViewItem>().Where(i => i.Tag is PackageUpdate).ToList();
            if (items.Count == 0) return;
            if (MessageBox.Show(this, L.T("Atualizar ", "Update ") + items.Count + L.T(" programa(s)?\n\nFeche esses programas antes. ", " program(s)?\n\nClose these programs first. ") +
                                L.T("Alguns instaladores pedem permissão de administrador.", "Some installers ask for administrator permission."), L.T("Atualizar marcados", "Update checked"),
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

            busy = true;
            btnCheck.Enabled = false;
            Reevaluate(btnUpdate);
            Progress.Start(items.Count);
            int ok = 0, failed = 0, index = 0;
            RunBackground(() =>
            {
                foreach (var it in items)
                {
                    var pkg = (PackageUpdate)it.Tag;
                    var item = it;
                    int n = index++;
                    Ui(() =>
                    {
                        SetStatus(L.T("Atualizando ", "Updating ") + pkg.Name + " (" + (n + 1) + L.T(" de ", " of ") + items.Count + ")...");
                        Progress.Value = n;
                        item.SubItems[StatusCol].Text = L.T("Atualizando...", "Updating...");
                        item.EnsureVisible();
                    });
                    string message;
                    bool success = Winget.Upgrade(pkg, line => Ui(() => item.SubItems[StatusCol].Text = Short(line)), out message);
                    if (success) ok++; else failed++;
                    Ui(() =>
                    {
                        item.SubItems[StatusCol].Text = success ? L.T("Atualizado", "Updated") : L.T("Falhou: ", "Failed: ") + Short(message);
                        item.ForeColor = success ? Theme.Verde : Theme.Vermelho;
                        if (success) { item.SubItems[1].Text = pkg.Available; item.Checked = false; }
                    });
                }
            }, () =>
            {
                busy = false;
                btnCheck.Enabled = true;
                Progress.Stop();
                Reevaluate(btnUpdate);
                if (failed > 0) SetStatus(ok + L.T(" atualizado(s) · ", " updated · ") + failed + L.T(" falharam (veja a coluna Situação)", " failed (see the Progress column)"), StatusKind.Danger);
                else SetStatus(ok + L.T(" programa(s) atualizado(s).", " program(s) updated."), StatusKind.Good);
            });
        }

        static string Short(string s)
        {
            s = (s ?? "").Trim();
            return s.Length > 80 ? s.Substring(0, 77) + "..." : s;
        }
    }

    // ======================= Atualizar drivers (Windows Update) =======================

    class DriverUpdatePage : Page
    {
        readonly ListCard results;
        readonly Button btnSearch, btnInstall;
        readonly DriverUpdater updater = new DriverUpdater();
        bool busy;

        public DriverUpdatePage(MainForm main)
            : base(main, L.T("Atualizar drivers", "Update drivers"), L.T("Drivers oferecidos pelo Windows Update, certificados e publicados pelos fabricantes.", "Drivers offered by Windows Update, certified and published by the manufacturers."))
        {
            btnInstall = AddAction(L.T("Instalar marcados", "Install checked"), ButtonKind.Primary, "download", Install);
            btnSearch = AddAction(L.T("Procurar drivers", "Search for drivers"), ButtonKind.Secondary, "search", Search);
            SetStatus(L.T("Nada foi procurado ainda.", "Nothing has been searched yet."));

            var list = Theme.MakeList(true);
            Theme.Col(list, "Driver", 340);
            Theme.Col(list, L.T("Fabricante", "Manufacturer"), 170);
            Theme.Col(list, L.T("Tipo", "Type"), 120);
            Theme.Col(list, L.T("Data", "Date"), 90);
            Theme.Col(list, L.T("Tamanho", "Size"), 90, HorizontalAlignment.Right);
            Theme.EnableSorting(list);
            results = new ListCard(list) { Dock = DockStyle.Fill };
            results.SetEmpty(L.T("Clique em Procurar drivers. A consulta ao Windows Update leva cerca de um minuto.", "Click Search for drivers. Checking Windows Update takes about a minute."), "cpu");
            Body.Controls.Add(results);
            Body.Controls.Add(new Notice(L.T("Em notebooks, o driver de fábrica costuma ser o mais adequado. Antes de instalar, o OpenCleaner cria um ponto de restauração.", "On laptops, the factory driver is usually the best fit. Before installing, OpenCleaner creates a restore point.")));
            BindToSelection(btnInstall, list, true, () => !busy);
            Disposed += (s, e) => updater.Dispose();
        }

        void Search()
        {
            if (busy) return;
            busy = true;
            btnSearch.Enabled = false;
            Reevaluate(btnInstall);
            results.List.Items.Clear();
            results.SetEmpty(null);
            Progress.StartIndeterminate();
            var sw = Stopwatch.StartNew();
            var timer = new Timer { Interval = 1000 };
            timer.Tick += (s, e) => SetStatus(L.T("Consultando o Windows Update... ", "Checking Windows Update... ") + (int)sw.Elapsed.TotalSeconds + " s");
            timer.Start();
            SetStatus(L.T("Consultando o Windows Update...", "Checking Windows Update..."));
            updater.Post(() => updater.Search(), (found, error) => Ui(() =>
            {
                timer.Dispose();
                Progress.Stop();
                busy = false;
                btnSearch.Enabled = true;
                if (error != null)
                {
                    SetStatus(L.T("O Windows Update não respondeu: ", "Windows Update did not respond: ") + error.Message, StatusKind.Danger);
                    results.SetEmpty(L.T("Verifique a conexão com a internet e tente Procurar drivers de novo.", "Check your internet connection and try Search for drivers again."), "circle-alert");
                    return;
                }
                var list = results.List;
                list.BeginUpdate();
                foreach (var d in found)
                {
                    var it = new ListViewItem(d.Title) { Tag = d };
                    it.SubItems.Add(d.Manufacturer);
                    it.SubItems.Add(d.DriverClass);
                    it.SubItems.Add(new ListViewItem.ListViewSubItem(it, d.Date.HasValue ? d.Date.Value.ToString("dd/MM/yyyy") : "")
                    {
                        Tag = d.Date ?? DateTime.MinValue
                    });
                    Theme.SizeCell(it, d.Size);
                    list.Items.Add(it);
                }
                Theme.Refit(list);
                list.EndUpdate();
                Reevaluate(btnInstall);
                if (found.Count == 0) SetStatus(L.T("Nenhum driver novo no Windows Update.", "No new drivers on Windows Update."), StatusKind.Good);
                else SetStatus(found.Count + L.T(" driver(s) disponível(is). Marque os que quer instalar.", " driver(s) available. Check the ones you want to install."));
                results.SetEmpty(L.T("Os drivers deste computador estão em dia com o Windows Update.", "This computer's drivers are up to date with Windows Update."), "circle-check");
            }));
        }

        void Install()
        {
            if (busy) return;
            var items = results.List.CheckedItems.Cast<ListViewItem>().Where(i => i.Tag is DriverUpdate).ToList();
            if (items.Count == 0) return;
            if (!Main.RequireAdmin(this, L.T("Instalar drivers", "Installing drivers"))) return;
            if (MessageBox.Show(this, L.T("Instalar ", "Install ") + items.Count + L.T(" driver(s) do Windows Update?\n\nUm ponto de restauração é criado antes.", " driver(s) from Windows Update?\n\nA restore point is created first."),
                                L.T("Instalar marcados", "Install checked"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            var drivers = items.Select(i => (DriverUpdate)i.Tag).ToList();
            busy = true;
            btnSearch.Enabled = false;
            Reevaluate(btnInstall);
            Progress.StartIndeterminate();
            SetStatus(L.T("Criando ponto de restauração...", "Creating a restore point..."));
            string rpError = null;
            bool rpOk = false;
            RunBackground(() => rpOk = DriverUpdater.CreateRestorePoint("OpenCleaner: antes de atualizar drivers", out rpError), () =>
            {
                if (!rpOk && MessageBox.Show(this, L.T("O ponto de restauração não foi criado (", "The restore point was not created (") + rpError + L.T(").\n\nInstalar mesmo assim?", ").\n\nInstall anyway?"),
                                             L.T("Ponto de restauração", "Restore point"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    busy = false;
                    btnSearch.Enabled = true;
                    Progress.Stop();
                    Reevaluate(btnInstall);
                    SetStatus(L.T("Instalação cancelada. Nada foi alterado.", "Installation canceled. Nothing was changed."));
                    return;
                }
                updater.Post(() => updater.Install(drivers, msg => Ui(() => SetStatus(msg))), (res, error) => Ui(() =>
                {
                    busy = false;
                    btnSearch.Enabled = true;
                    Progress.Stop();
                    if (error != null)
                    {
                        Reevaluate(btnInstall);
                        SetStatus(L.T("A instalação falhou: ", "Installation failed: ") + error.Message, StatusKind.Danger);
                        return;
                    }
                    foreach (var it in items)
                    {
                        bool failed = res.Errors.Contains(((DriverUpdate)it.Tag).Title);
                        it.ForeColor = failed ? Theme.Vermelho : Theme.Verde;
                        it.Checked = false;
                    }
                    Reevaluate(btnInstall);
                    string text = res.Succeeded + L.T(" driver(s) instalado(s)", " driver(s) installed") + (res.Failed > 0 ? " · " + res.Failed + L.T(" falharam", " failed") : "") +
                                  (res.RebootRequired ? L.T(" · reinicie o computador para concluir", " · restart the computer to finish") : "");
                    SetStatus(text, res.Failed > 0 ? StatusKind.Danger : res.RebootRequired ? StatusKind.Warning : StatusKind.Good);
                }));
            });
        }
    }
}
