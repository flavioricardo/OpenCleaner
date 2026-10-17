// Instalador do OpenCleaner. Compilado por installer\build-installer.ps1 com o OpenCleaner.exe
// embutido como recurso "payload.exe".
//
//   OpenCleaner-Setup.exe               instala com janela
//   OpenCleaner-Setup.exe /S            instala em silêncio
//   uninstall.exe /uninstall [/S]       desinstala (é o que o Windows chama em "Apps e recursos")
//
// Instala só para o usuário atual em %LocalAppData%\Programs\OpenCleaner: não pede administrador.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpenCleanerSetup
{
    static class Setup
    {
        const string AppName = "OpenCleaner";
        const string Publisher = "flavioricardo";
        const string RepoUrl = "https://github.com/flavioricardo/OpenCleaner";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenCleaner";

        public static readonly Color Verde = Color.FromArgb(0x0E, 0x7C, 0x66);
        public static readonly Color Tinta = Color.FromArgb(0x1B, 0x1F, 0x24);
        public static readonly Color Grafite = Color.FromArgb(0x5B, 0x63, 0x6E);

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\OpenCleaner"); }
        }

        static string AppExe { get { return Path.Combine(InstallDir, "OpenCleaner.exe"); } }
        static string UninstallExe { get { return Path.Combine(InstallDir, "uninstall.exe"); } }
        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "OpenCleaner.lnk"); } }
        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OpenCleaner.lnk"); } }
        static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenCleaner"); } }

        public static string InstalledVersion
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    return k == null ? null : k.GetValue("DisplayVersion") as string;
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            bool silent = args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase));
            bool uninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (uninstall) return Uninstall(silent);
            if (silent)
            {
                string error;
                return Install(false, out error) ? 0 : 1;
            }
            Application.Run(new SetupForm());
            return 0;
        }

        static void CloseRunningApp()
        {
            foreach (var p in Process.GetProcessesByName("OpenCleaner"))
            {
                try
                {
                    string path = p.MainModule.FileName;
                    if (!path.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!p.CloseMainWindow() || !p.WaitForExit(5000)) { p.Kill(); p.WaitForExit(3000); }
                }
                catch { }
            }
        }

        public static bool Install(bool desktopShortcut, out string error)
        {
            error = null;
            try
            {
                CloseRunningApp();
                Directory.CreateDirectory(InstallDir);

                // Extrai o app para um temporário e só então substitui: não deixa um exe pela metade.
                string tmp = AppExe + ".new";
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.exe"))
                using (var f = File.Create(tmp))
                    s.CopyTo(f);
                if (File.Exists(AppExe)) File.Delete(AppExe);
                File.Move(tmp, AppExe);

                string self = Assembly.GetExecutingAssembly().Location;
                if (!self.Equals(UninstallExe, StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, UninstallExe, true);

                Shortcut(StartMenuLink);
                if (desktopShortcut) Shortcut(DesktopLink);

                long sizeKb = Directory.GetFiles(InstallDir).Sum(p => new FileInfo(p).Length) / 1024;
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", AppName);
                    k.SetValue("DisplayVersion", BuildInfo.Version);
                    k.SetValue("Publisher", Publisher);
                    k.SetValue("DisplayIcon", AppExe + ",0");
                    k.SetValue("InstallLocation", InstallDir);
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    k.SetValue("UninstallString", "\"" + UninstallExe + "\" /uninstall");
                    k.SetValue("QuietUninstallString", "\"" + UninstallExe + "\" /uninstall /S");
                    k.SetValue("URLInfoAbout", RepoUrl);
                    k.SetValue("HelpLink", RepoUrl + "/issues");
                    k.SetValue("URLUpdateInfo", RepoUrl + "/releases");
                    k.SetValue("EstimatedSize", (int)sizeKb, RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static void Shortcut(string linkPath)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = AppExe;
            link.WorkingDirectory = InstallDir;
            link.Description = "Limpeza, registro, atualizações e ferramentas do sistema";
            link.IconLocation = AppExe + ",0";
            link.Save();
        }

        public static void Launch()
        {
            try { Process.Start(new ProcessStartInfo(AppExe) { UseShellExecute = true, WorkingDirectory = InstallDir }); }
            catch { }
        }

        static int Uninstall(bool silent)
        {
            bool removeData = false;
            if (!silent)
            {
                if (MessageBox.Show("Remover o OpenCleaner deste computador?", "Desinstalar o OpenCleaner",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return 1;
                removeData = Directory.Exists(DataDir) &&
                    MessageBox.Show("Apagar também suas configurações, regras winapp2.ini e backups do registro?\n\n" + DataDir +
                                    "\n\nEscolha Não se quiser guardar os backups do registro.",
                                    "Desinstalar o OpenCleaner", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                                    MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            }

            CloseRunningApp();
            foreach (var f in new[] { AppExe, StartMenuLink, DesktopLink })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (removeData) try { Directory.Delete(DataDir, true); } catch { }

            // O uninstall.exe está rodando de dentro da pasta: um cmd apaga a pasta depois que ele fechar.
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"")
            {
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false
            });

            if (!silent)
                MessageBox.Show("O OpenCleaner foi removido.", "Desinstalar o OpenCleaner", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
    }

    class SetupForm : Form
    {
        readonly CheckBox desktop, launch;
        readonly Button install, cancel;
        readonly Label body;
        bool done;

        public SetupForm()
        {
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi; // escala as posições fixas abaixo em telas com DPI alto
            string current = Setup.InstalledVersion;
            Text = "Instalar o OpenCleaner " + BuildInfo.Version;
            ClientSize = new Size(520, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var mark = new PictureBox { Location = new Point(28, 26), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom };
            try { mark.Image = new Icon(Icon, 48, 48).ToBitmap(); } catch { }
            var title = new Label
            {
                Text = current == null ? "Instalar o OpenCleaner" : "Atualizar o OpenCleaner",
                Font = new Font("Segoe UI Semibold", 15f), ForeColor = Setup.Tinta, AutoSize = true, Location = new Point(88, 26)
            };
            var version = new Label
            {
                Text = current == null ? "Versão " + BuildInfo.Version : "Versão " + current + " → " + BuildInfo.Version,
                ForeColor = Setup.Grafite, AutoSize = true, Location = new Point(90, 56)
            };
            body = new Label
            {
                Text = "Limpeza de arquivos, registro, atualizações de programas e drivers e ferramentas do sistema.\n\n" +
                       "Será instalado só para você, sem pedir permissão de administrador, em:\n" + Setup.InstallDir,
                ForeColor = Setup.Tinta, Location = new Point(30, 96), Size = new Size(462, 96)
            };
            desktop = new CheckBox { Text = "Criar atalho na Área de Trabalho", AutoSize = true, Location = new Point(30, 196) };
            launch = new CheckBox { Text = "Abrir o OpenCleaner ao terminar", AutoSize = true, Location = new Point(30, 222), Checked = true };

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(0xEE, 0xF1, 0xF4) };
            install = new Button
            {
                Text = current == null ? "Instalar" : "Atualizar", Size = new Size(110, 34), Location = new Point(392, 11),
                FlatStyle = FlatStyle.Flat, BackColor = Setup.Verde, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9.5f)
            };
            install.FlatAppearance.BorderColor = Setup.Verde;
            cancel = new Button { Text = "Cancelar", Size = new Size(100, 34), Location = new Point(284, 11), FlatStyle = FlatStyle.Flat, BackColor = Color.White };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(0xD9, 0xDE, 0xE4);
            install.Click += (s, e) => { if (done) Close(); else DoInstall(); };
            cancel.Click += (s, e) => Close();
            footer.Controls.AddRange(new Control[] { install, cancel });
            AcceptButton = install;
            CancelButton = cancel;

            Controls.AddRange(new Control[] { mark, title, version, body, desktop, launch, footer });
        }

        void DoInstall()
        {
            install.Enabled = cancel.Enabled = desktop.Enabled = launch.Enabled = false;
            Cursor = Cursors.WaitCursor;
            string error;
            bool ok = Setup.Install(desktop.Checked, out error);
            Cursor = Cursors.Default;
            if (!ok)
            {
                MessageBox.Show(this, "A instalação não foi concluída: " + error, "Instalar o OpenCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
                install.Enabled = cancel.Enabled = desktop.Enabled = launch.Enabled = true;
                return;
            }
            if (launch.Checked) Setup.Launch();
            body.Text = "O OpenCleaner foi instalado.\n\nEle está no menu Iniciar. Para remover, use Configurações > Apps > OpenCleaner.";
            desktop.Visible = launch.Visible = cancel.Visible = false;
            install.Text = "Concluir";
            install.Enabled = true;
            done = true;
        }
    }
}
