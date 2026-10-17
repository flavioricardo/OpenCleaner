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
            : base(main, "Atualizar programas", "Programas com versão nova disponível, instalados pelo Gerenciador de Pacotes do Windows (winget).")
        {
            btnUpdate = AddAction("Atualizar marcados", ButtonKind.Primary, "download", UpdateChecked);
            btnCheck = AddAction("Procurar atualizações", ButtonKind.Secondary, "refresh-cw", Check);
            var markAll = Theme.MakeButton("Marcar todos", ButtonKind.Link, "list-checks");
            markAll.Margin = Theme.Pad(0, 0, 4, 0);
            markAll.Click += (s, e) => SetAll(true);
            var clear = Theme.MakeButton("Desmarcar todos", ButtonKind.Link, "x");
            clear.Click += (s, e) => SetAll(false);
            ToolbarLeft.Controls.Add(markAll);
            ToolbarLeft.Controls.Add(clear);

            var list = Theme.MakeList(true);
            Theme.Col(list, "Programa", 280);
            Theme.Col(list, "Instalada", 120);
            Theme.Col(list, "Disponível", 120);
            Theme.Col(list, "ID no winget", 200);
            Theme.Col(list, "Situação", 200);
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
            SetStatus("Procurando atualizações...");
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
                    btnCheck.Text = "Instalar o winget";
                    SetStatus("O winget não está instalado neste computador.", StatusKind.Warning);
                    results.SetEmpty("Para atualizar programas, instale o \"Instalador de Aplicativo\" da Microsoft Store\n(botão Instalar o winget, acima) e volte aqui.", "download");
                    Reevaluate(btnUpdate);
                    return;
                }
                if (found == null)
                {
                    SetStatus("O winget não respondeu. Tente Procurar atualizações de novo.", StatusKind.Danger);
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
                    it.SubItems.Add(p.Explicit ? "Só atualiza se marcado" : "");
                    list.Items.Add(it);
                }
                Theme.Refit(list);
                list.EndUpdate();
                Reevaluate(btnUpdate);
                if (found.Count == 0) SetStatus("Todos os programas estão atualizados.", StatusKind.Good);
                else SetStatus(found.Count + " programas têm versão nova. Feche-os antes de atualizar.");
                results.SetEmpty("Todos os programas conhecidos pelo winget estão na versão mais recente.", "circle-check");
            });
        }

        void UpdateChecked()
        {
            if (busy) return;
            var items = results.List.CheckedItems.Cast<ListViewItem>().Where(i => i.Tag is PackageUpdate).ToList();
            if (items.Count == 0) return;
            if (MessageBox.Show(this, "Atualizar " + items.Count + " programa(s)?\n\nFeche esses programas antes. " +
                                "Alguns instaladores pedem permissão de administrador.", "Atualizar marcados",
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
                        SetStatus("Atualizando " + pkg.Name + " (" + (n + 1) + " de " + items.Count + ")...");
                        Progress.Value = n;
                        item.SubItems[StatusCol].Text = "Atualizando...";
                        item.EnsureVisible();
                    });
                    string message;
                    bool success = Winget.Upgrade(pkg, line => Ui(() => item.SubItems[StatusCol].Text = Short(line)), out message);
                    if (success) ok++; else failed++;
                    Ui(() =>
                    {
                        item.SubItems[StatusCol].Text = success ? "Atualizado" : "Falhou: " + Short(message);
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
                if (failed > 0) SetStatus(ok + " atualizado(s) · " + failed + " falharam (veja a coluna Situação)", StatusKind.Danger);
                else SetStatus(ok + " programa(s) atualizado(s).", StatusKind.Good);
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
            : base(main, "Atualizar drivers", "Drivers oferecidos pelo Windows Update, certificados e publicados pelos fabricantes.")
        {
            btnInstall = AddAction("Instalar marcados", ButtonKind.Primary, "download", Install);
            btnSearch = AddAction("Procurar drivers", ButtonKind.Secondary, "search", Search);
            SetStatus("Nada foi procurado ainda.");

            var list = Theme.MakeList(true);
            Theme.Col(list, "Driver", 340);
            Theme.Col(list, "Fabricante", 170);
            Theme.Col(list, "Tipo", 120);
            Theme.Col(list, "Data", 90);
            Theme.Col(list, "Tamanho", 90, HorizontalAlignment.Right);
            Theme.EnableSorting(list);
            results = new ListCard(list) { Dock = DockStyle.Fill };
            results.SetEmpty("Clique em Procurar drivers. A consulta ao Windows Update leva cerca de um minuto.", "cpu");
            Body.Controls.Add(results);
            Body.Controls.Add(new Notice("Em notebooks, o driver de fábrica costuma ser o mais adequado. Antes de instalar, o OpenCleaner cria um ponto de restauração."));
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
            timer.Tick += (s, e) => SetStatus("Consultando o Windows Update... " + (int)sw.Elapsed.TotalSeconds + " s");
            timer.Start();
            SetStatus("Consultando o Windows Update...");
            updater.Post(() => updater.Search(), (found, error) => Ui(() =>
            {
                timer.Dispose();
                Progress.Stop();
                busy = false;
                btnSearch.Enabled = true;
                if (error != null)
                {
                    SetStatus("O Windows Update não respondeu: " + error.Message, StatusKind.Danger);
                    results.SetEmpty("Verifique a conexão com a internet e tente Procurar drivers de novo.", "circle-alert");
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
                if (found.Count == 0) SetStatus("Nenhum driver novo no Windows Update.", StatusKind.Good);
                else SetStatus(found.Count + " driver(s) disponível(is). Marque os que quer instalar.");
                results.SetEmpty("Os drivers deste computador estão em dia com o Windows Update.", "circle-check");
            }));
        }

        void Install()
        {
            if (busy) return;
            var items = results.List.CheckedItems.Cast<ListViewItem>().Where(i => i.Tag is DriverUpdate).ToList();
            if (items.Count == 0) return;
            if (!Main.RequireAdmin(this, "Instalar drivers")) return;
            if (MessageBox.Show(this, "Instalar " + items.Count + " driver(s) do Windows Update?\n\nUm ponto de restauração é criado antes.",
                                "Instalar marcados", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            var drivers = items.Select(i => (DriverUpdate)i.Tag).ToList();
            busy = true;
            btnSearch.Enabled = false;
            Reevaluate(btnInstall);
            Progress.StartIndeterminate();
            SetStatus("Criando ponto de restauração...");
            string rpError = null;
            bool rpOk = false;
            RunBackground(() => rpOk = DriverUpdater.CreateRestorePoint("OpenCleaner: antes de atualizar drivers", out rpError), () =>
            {
                if (!rpOk && MessageBox.Show(this, "O ponto de restauração não foi criado (" + rpError + ").\n\nInstalar mesmo assim?",
                                             "Ponto de restauração", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    busy = false;
                    btnSearch.Enabled = true;
                    Progress.Stop();
                    Reevaluate(btnInstall);
                    SetStatus("Instalação cancelada. Nada foi alterado.");
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
                        SetStatus("A instalação falhou: " + error.Message, StatusKind.Danger);
                        return;
                    }
                    foreach (var it in items)
                    {
                        bool failed = res.Errors.Contains(((DriverUpdate)it.Tag).Title);
                        it.ForeColor = failed ? Theme.Vermelho : Theme.Verde;
                        it.Checked = false;
                    }
                    Reevaluate(btnInstall);
                    string text = res.Succeeded + " driver(s) instalado(s)" + (res.Failed > 0 ? " · " + res.Failed + " falharam" : "") +
                                  (res.RebootRequired ? " · reinicie o computador para concluir" : "");
                    SetStatus(text, res.Failed > 0 ? StatusKind.Danger : res.RebootRequired ? StatusKind.Warning : StatusKind.Good);
                }));
            });
        }
    }
}
