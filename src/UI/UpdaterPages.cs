using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenCleaner.Core;

namespace OpenCleaner.UI
{
    // ======================= Atualizador de programas (winget) =======================

    class SoftwareUpdatePage : Page
    {
        readonly ListView list;
        readonly Label status;
        readonly Button btnCheck, btnUpdate, btnStore;
        bool loaded, busy;

        public SoftwareUpdatePage(MainForm main)
            : base(main, "Atualizar programas", "Programas com versão mais nova disponível, instalada pelo Gerenciador de Pacotes do Windows (winget).")
        {
            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 220, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16, 0, 0, 0) };
            btnUpdate = SideButton(side, "Atualizar selecionados", true, "download", UpdateChecked);
            btnCheck = SideButton(side, "Procurar atualizações", false, "refresh-cw", Check);
            SideButton(side, "Marcar todos", false, "circle-check", () => SetAll(true));
            SideButton(side, "Desmarcar todos", false, "x", () => SetAll(false));
            btnStore = SideButton(side, "Instalar o winget", false, "download",
                () => Process.Start("ms-windows-store://pdp/?productid=9NBLGGH4NNS1"));
            btnStore.Visible = false;

            var card = Theme.MakeCard();
            card.Dock = DockStyle.Fill;
            list = Theme.MakeList(true);
            list.Columns.Add("Programa", 260);
            list.Columns.Add("Instalada", 120);
            list.Columns.Add("Disponível", 120);
            list.Columns.Add("Situação", 220);
            list.Columns.Add("ID", 200);
            Theme.EnableSorting(list);
            card.Controls.Add(list);

            status = new Label { Dock = DockStyle.Top, Height = 36, Font = Theme.H2, TextAlign = ContentAlignment.MiddleLeft };
            Body.Controls.Add(card);
            Body.Controls.Add(side);
            Body.Controls.Add(status);
        }

        internal static Button SideButton(Control parent, string text, bool primary, string icon, Action act)
        {
            var b = Theme.MakeButton(text, primary, icon);
            b.AutoSize = false;
            b.Size = new Size(200, 36);
            b.Margin = new Padding(0, 0, 0, 8);
            b.Click += (s, e) => act();
            parent.Controls.Add(b);
            return b;
        }

        public override void Activated()
        {
            if (!loaded) Check();
        }

        void SetBusy(bool on)
        {
            busy = on;
            btnCheck.Enabled = btnUpdate.Enabled = !on;
        }

        void SetAll(bool on)
        {
            foreach (ListViewItem it in list.Items) it.Checked = on;
        }

        void Check()
        {
            if (busy) return;
            loaded = true;
            SetBusy(true);
            status.ForeColor = Theme.Text;
            status.Text = "Procurando atualizações...";
            list.Items.Clear();
            List<PackageUpdate> found = null;
            bool available = false;
            string version = null;
            RunBackground(() =>
            {
                available = Winget.IsAvailable(out version);
                if (available) found = Winget.ListUpgrades();
            }, () =>
            {
                SetBusy(false);
                btnStore.Visible = !available;
                if (!available)
                {
                    status.Text = "O winget não está instalado.";
                    status.ForeColor = Theme.Danger;
                    list.Items.Add(new ListViewItem("Instale o \"Instalador de Aplicativo\" da Microsoft Store para usar esta função."));
                    btnUpdate.Enabled = false;
                    return;
                }
                if (found == null) { status.Text = "Não foi possível consultar o winget."; return; }
                list.BeginUpdate();
                foreach (var p in found)
                {
                    var it = new ListViewItem(new[] { p.Name, p.Version, p.Available, p.Explicit ? "Atualização manual" : "", p.Id })
                    {
                        Tag = p, Checked = !p.Explicit
                    };
                    list.Items.Add(it);
                }
                list.EndUpdate();
                status.Text = found.Count == 0 ? "Todos os programas estão atualizados." : found.Count + " programas podem ser atualizados";
                status.ForeColor = found.Count == 0 ? Theme.Good : Theme.Text;
                btnUpdate.Enabled = found.Count > 0;
            });
        }

        void UpdateChecked()
        {
            if (busy) return;
            var items = list.CheckedItems.Cast<ListViewItem>().Where(i => i.Tag is PackageUpdate).ToList();
            if (items.Count == 0) { status.Text = "Nenhum programa marcado."; return; }
            if (MessageBox.Show(this, "Atualizar " + items.Count + " programa(s)?\n\nFeche esses programas antes de continuar. " +
                                "Alguns instaladores podem pedir permissão de administrador.", "Atualizar programas",
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

            SetBusy(true);
            status.ForeColor = Theme.Text;
            int ok = 0, failed = 0;
            RunBackground(() =>
            {
                foreach (var it in items)
                {
                    var pkg = (PackageUpdate)it.Tag;
                    var item = it;
                    Ui(() =>
                    {
                        status.Text = "Atualizando " + pkg.Name + "...";
                        item.SubItems[3].Text = "Atualizando...";
                        item.EnsureVisible();
                    });
                    string message;
                    bool success = Winget.Upgrade(pkg, line => Ui(() => item.SubItems[3].Text = Short(line)), out message);
                    if (success) ok++; else failed++;
                    Ui(() =>
                    {
                        item.SubItems[3].Text = success ? "Atualizado" : "Falhou: " + Short(message);
                        item.ForeColor = success ? Theme.Good : Theme.Danger;
                        if (success) { item.SubItems[1].Text = pkg.Available; item.Checked = false; }
                    });
                }
            }, () =>
            {
                SetBusy(false);
                status.Text = ok + " atualizado(s)" + (failed > 0 ? " · " + failed + " com falha" : "");
                status.ForeColor = failed > 0 ? Theme.Danger : Theme.Good;
            });
        }

        static string Short(string s)
        {
            s = (s ?? "").Trim();
            return s.Length > 80 ? s.Substring(0, 77) + "..." : s;
        }
    }

    // ======================= Atualizador de drivers (Windows Update) =======================

    class DriverUpdatePage : Page
    {
        readonly ListView list;
        readonly Label status;
        readonly Button btnSearch, btnInstall;
        readonly DriverUpdater updater = new DriverUpdater();
        bool busy;

        public DriverUpdatePage(MainForm main)
            : base(main, "Atualizar drivers", "Drivers oferecidos pelo Windows Update, certificados e publicados pelos fabricantes.")
        {
            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 220, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16, 0, 0, 0) };
            btnInstall = SoftwareUpdatePage.SideButton(side, "Instalar selecionados", true, "download", Install);
            btnSearch = SoftwareUpdatePage.SideButton(side, "Procurar drivers", false, "search", Search);
            if (!Util.IsAdmin())
            {
                SoftwareUpdatePage.SideButton(side, "Reiniciar como admin", false, "shield-check", Main.RestartElevated);
                side.Controls.Add(new Label
                {
                    Text = "Instalar drivers exige executar como administrador.", ForeColor = Theme.Muted,
                    Size = new Size(200, 40), Margin = new Padding(0, 4, 0, 0)
                });
            }
            btnInstall.Enabled = false;

            var card = Theme.MakeCard();
            card.Dock = DockStyle.Fill;
            list = Theme.MakeList(true);
            list.Columns.Add("Driver", 320);
            list.Columns.Add("Fabricante", 150);
            list.Columns.Add("Classe", 110);
            list.Columns.Add("Data", 90);
            list.Columns.Add("Tamanho", 90, HorizontalAlignment.Right);
            Theme.EnableSorting(list);
            card.Controls.Add(list);

            var note = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(0, 0, 0, 10) };
            var noteInner = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(255, 248, 225) };
            noteInner.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 0, 8, 0),
                ForeColor = Color.FromArgb(110, 80, 0),
                Text = "Em notebooks, o driver de fábrica pode ser o mais adequado. Um ponto de restauração é criado antes de instalar."
            });
            var alertSlot = new Panel { Dock = DockStyle.Left, Width = 38 };
            alertSlot.Controls.Add(new IconBox("triangle-alert", 18, Color.FromArgb(190, 130, 0)) { Location = new Point(12, 9) });
            noteInner.Controls.Add(alertSlot);
            note.Controls.Add(noteInner);

            status = new Label { Dock = DockStyle.Top, Height = 36, Font = Theme.H2, TextAlign = ContentAlignment.MiddleLeft, Text = "Clique em Procurar drivers." };
            Body.Controls.Add(card);
            Body.Controls.Add(side);
            Body.Controls.Add(note);
            Body.Controls.Add(status);
            Disposed += (s, e) => updater.Dispose();
        }

        void SetBusy(bool on)
        {
            busy = on;
            btnSearch.Enabled = !on;
            btnInstall.Enabled = !on && list.Items.Cast<ListViewItem>().Any(i => i.Tag is DriverUpdate);
        }

        void Search()
        {
            if (busy) return;
            SetBusy(true);
            list.Items.Clear();
            status.ForeColor = Theme.Text;
            var sw = Stopwatch.StartNew();
            var timer = new Timer { Interval = 1000 };
            timer.Tick += (s, e) => status.Text = "Consultando o Windows Update... " + (int)sw.Elapsed.TotalSeconds + " s";
            timer.Start();
            updater.Post(() => updater.Search(), (found, error) => Ui(() =>
            {
                timer.Dispose();
                SetBusy(false);
                if (error != null)
                {
                    status.Text = "Falha ao consultar o Windows Update: " + error.Message;
                    status.ForeColor = Theme.Danger;
                    return;
                }
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
                list.EndUpdate();
                status.Text = found.Count == 0 ? "Nenhuma atualização de driver disponível." : found.Count + " atualização(ões) de driver disponível(is)";
                status.ForeColor = found.Count == 0 ? Theme.Good : Theme.Text;
                SetBusy(false);
            }));
        }

        void Install()
        {
            if (busy) return;
            var items = list.CheckedItems.Cast<ListViewItem>().Where(i => i.Tag is DriverUpdate).ToList();
            if (items.Count == 0) { status.Text = "Marque os drivers que deseja instalar."; return; }
            if (!Util.IsAdmin())
            {
                if (MessageBox.Show(this, "Instalar drivers exige permissão de administrador.\n\nReiniciar o OpenCleaner como administrador agora?",
                                    "Atualizar drivers", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Main.RestartElevated();
                return;
            }
            if (MessageBox.Show(this, "Instalar " + items.Count + " driver(s) do Windows Update?\n\nUm ponto de restauração será criado antes.",
                                "Atualizar drivers", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            var drivers = items.Select(i => (DriverUpdate)i.Tag).ToList();
            SetBusy(true);
            btnInstall.Enabled = false;
            status.ForeColor = Theme.Text;
            status.Text = "Criando ponto de restauração...";
            string rpError = null;
            bool rpOk = false;
            RunBackground(() => rpOk = DriverUpdater.CreateRestorePoint("OpenCleaner: antes de atualizar drivers", out rpError), () =>
            {
                if (!rpOk && MessageBox.Show(this, "Não foi possível criar o ponto de restauração (" + rpError + ").\n\nInstalar mesmo assim?",
                                             "Ponto de restauração", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    SetBusy(false);
                    status.Text = "Instalação cancelada.";
                    return;
                }
                updater.Post(() => updater.Install(drivers, msg => Ui(() => status.Text = msg)), (res, error) => Ui(() =>
                {
                    SetBusy(false);
                    if (error != null)
                    {
                        status.Text = "Falha na instalação: " + error.Message;
                        status.ForeColor = Theme.Danger;
                        return;
                    }
                    foreach (var it in items)
                    {
                        bool failed = res.Errors.Contains(((DriverUpdate)it.Tag).Title);
                        it.ForeColor = failed ? Theme.Danger : Theme.Good;
                        it.Checked = false;
                    }
                    status.Text = res.Succeeded + " instalado(s)" + (res.Failed > 0 ? " · " + res.Failed + " com falha" : "") +
                                  (res.RebootRequired ? " · reinicie o computador para concluir" : "");
                    status.ForeColor = res.Failed > 0 ? Theme.Danger : Theme.Good;
                }));
            });
        }
    }
}
