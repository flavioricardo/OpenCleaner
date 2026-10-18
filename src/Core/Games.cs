using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpenCleaner.Core
{
    /// <summary>Uma alteração de registro: gravar um DWORD conhecido ou apagar um valor (Value == null).</summary>
    class RegEdit
    {
        public RegistryKey Root;
        public string Key;
        public string Name;
        public int? Value;
    }

    /// <summary>
    /// Fix: desfeito pelo OpenCleaner. Recommended: opção do Windows que ajuda em jogos, escolha do usuário.
    /// Tradeoff: ganha desempenho em troca de proteção. Os dois últimos só leem e abrem a página do Windows.
    /// </summary>
    enum GameKind { Fix, Recommended, Tradeoff }

    /// <summary>Algo que um otimizador mudou e que prejudica, ou uma opção do Windows que vale conhecer.</summary>
    class GameFinding
    {
        public GameKind Kind = GameKind.Fix;
        /// <summary>Página do Windows onde o usuário muda a opção (Recommended e Tradeoff).</summary>
        public string SettingsUri;
        public string Id, Name, Why, Location, Current, Expected;
        /// <summary>Deixa o PC menos seguro: aparece primeiro e em destaque.</summary>
        public bool Security;
        public bool NeedsAdmin, DefaultChecked = true;
        public readonly List<RegEdit> Edits = new List<RegEdit>();
        /// <summary>Chaves que o Run altera por fora do registro (serviços) e que entram no backup.</summary>
        public readonly List<KeyValuePair<RegistryKey, string>> AlsoBackup = new List<KeyValuePair<RegistryKey, string>>();
        /// <summary>Ação fora do registro: trocar o plano de energia ou abrir uma página da Microsoft Store.</summary>
        public Func<bool> Run;
        /// <summary>O que dizer depois de aplicar, quando falta um passo do usuário (instalar pela Store).</summary>
        public string AfterApply;
    }

    /// <summary>De onde o scanner lê e onde o conserto escreve. Os testes trocam as raízes por uma sandbox.</summary>
    class GameContext
    {
        public RegistryKey Hklm, Hkcu;
        public bool HasBattery;
        public Func<string, bool> SetActivePowerScheme;
        /// <summary>Abre um endereço ms-windows-store:// do catálogo.</summary>
        public Func<string, bool> OpenStore;
        /// <summary>Número do build do Windows (22000 ou mais = Windows 11); 0 = desconhecido.</summary>
        public int WindowsBuild;
        /// <summary>Integridade de Memória (HVCI) em execução agora.</summary>
        public Func<bool> MemoryIntegrityRunning;
        /// <summary>Muda o tipo de inicialização de um serviço: nome, Start (2 automático, 3 manual) e se é atrasado.</summary>
        public Func<string, int, bool, bool> SetServiceStart;
        /// <summary>Saída de "bcdedit /enum {current}"; null sem administrador ou se falhar.</summary>
        public Func<string> BootConfig;
        /// <summary>Apaga um valor da entrada de boot atual ("bcdedit /deletevalue {current} nome").</summary>
        public Func<string, bool> DeleteBootValue;

        /// <summary>Únicos valores do BCD que o OpenCleaner lê e pode apagar: o relógio do sistema que boosters forçam.</summary>
        public static readonly string[] BootClockValues = { "useplatformclock", "useplatformtick", "disabledynamictick" };

        static string Bcdedit(string args)
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Sysnative\bcdedit.exe");
            if (!File.Exists(exe)) exe = Path.Combine(Environment.SystemDirectory, "bcdedit.exe"); // processo de 64 bits
            var psi = new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(30000);
                return p.HasExited && p.ExitCode == 0 ? output : null;
            }
        }

        static string ReadBootConfig()
        {
            if (!Util.IsAdmin()) return null; // sem administrador o bcdedit nega a leitura
            try { return Bcdedit("/enum {current}"); }
            catch { return null; }
        }

        static bool DeleteBootClockValue(string name)
        {
            if (!BootClockValues.Contains(name)) return false; // nada fora da lista vira argumento do bcdedit
            try { return Bcdedit("/deletevalue {current} " + name) != null; }
            catch { return false; }
        }

        public static GameContext System()
        {
            var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
            int build = 0;
            using (var k = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                if (k != null) int.TryParse(Util.ReadString(k, "CurrentBuild"), out build);
            return new GameContext
            {
                Hklm = hklm,
                Hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
                HasBattery = Util.HasBattery(),
                WindowsBuild = build,
                SetActivePowerScheme = PowerCfgSetActive,
                OpenStore = uri =>
                {
                    if (!uri.StartsWith("ms-windows-store://pdp/?ProductId=", StringComparison.Ordinal)) return false;
                    try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); return true; }
                    catch { return false; }
                },
                MemoryIntegrityRunning = HvciRunning,
                SetServiceStart = ScmSetStart,
                BootConfig = ReadBootConfig,
                DeleteBootValue = DeleteBootClockValue
            };
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr OpenSCManager(string machine, string database, uint access);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr OpenService(IntPtr scm, string name, uint access);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ChangeServiceConfig(IntPtr service, uint type, uint start, uint error, string path, string group,
                                               IntPtr tag, string dependencies, string user, string password, string display);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool ChangeServiceConfig2(IntPtr service, uint level, ref int delayedAutoStart);

        [DllImport("advapi32.dll")]
        static extern bool CloseServiceHandle(IntPtr handle);

        /// <summary>
        /// Muda o tipo de inicialização pelo Gerenciador de Serviços (escrever direto no registro só vale depois
        /// de reiniciar). Exige administrador. O nome vem do catálogo ou de uma subchave de Services já validada.
        /// </summary>
        static bool ScmSetStart(string name, int start, bool delayed)
        {
            if (!Regex.IsMatch(name ?? "", @"^[A-Za-z0-9_.\-]+$") || (start != 2 && start != 3)) return false;
            const uint NoChange = 0xFFFFFFFF;
            IntPtr scm = OpenSCManager(null, null, 0x0001); // SC_MANAGER_CONNECT
            if (scm == IntPtr.Zero) return false;
            try
            {
                IntPtr svc = OpenService(scm, name, 0x0002); // SERVICE_CHANGE_CONFIG
                if (svc == IntPtr.Zero) return false;
                try
                {
                    if (!ChangeServiceConfig(svc, NoChange, (uint)start, NoChange, null, null, IntPtr.Zero, null, null, null, null)) return false;
                    if (start != 2) return true;
                    int flag = delayed ? 1 : 0;
                    return ChangeServiceConfig2(svc, 3, ref flag); // SERVICE_CONFIG_DELAYED_AUTO_START_INFO
                }
                finally { CloseServiceHandle(svc); }
            }
            finally { CloseServiceHandle(scm); }
        }

        /// <summary>Win32_DeviceGuard: SecurityServicesRunning contém 2 quando a Integridade de Memória está ativa.</summary>
        static bool HvciRunning()
        {
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT SecurityServicesRunning FROM Win32_DeviceGuard"))
                    foreach (ManagementObject o in s.Get())
                        using (o)
                        {
                            var running = o["SecurityServicesRunning"] as uint[];
                            if (running != null && running.Contains(2u)) return true;
                        }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Abre uma página do Windows do catálogo. Só aceita os endereços fixos que o GameScanner usa:
        /// nada vindo do registro ou de arquivos vira linha de comando.
        /// </summary>
        public static bool OpenSettings(string uri)
        {
            try
            {
                if (uri == "main.cpl")
                    Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "control.exe"), "main.cpl") { UseShellExecute = false });
                else if (GameScanner.SettingsPages.Contains(uri))
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                else return false;
                return true;
            }
            catch { return false; }
        }

        static bool PowerCfgSetActive(string guid)
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), "/setactive " + guid)
                {
                    CreateNoWindow = true, UseShellExecute = false
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(30000);
                    return p.HasExited && p.ExitCode == 0;
                }
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Procura ajustes deixados por "otimizadores" e "game boosters" que atrapalham quem joga.
    /// Verificar só lê. Consertar volta ao padrão do Windows e se limita a gravar um DWORD conhecido,
    /// apagar um valor ou ativar o plano Equilibrado. O catálogo é fixo aqui no código de propósito:
    /// um arquivo de regras que grava em HKLM como administrador seria um vetor de ataque.
    /// </summary>
    static class GameScanner
    {
        public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

        const string GameBar = @"Software\Microsoft\GameBar";
        const string GameDvrPolicy = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
        const string BackgroundApps = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
        const string AppPrivacyPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
        const string PowerSchemes = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
        /// <summary>Pacotes da Store registrados para o usuário: uma subchave por pacote (Nome_Versão_Arq__Editor).</summary>
        const string Packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

        const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
        const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        const string DirectXUser = @"Software\Microsoft\DirectX\UserGpuPreferences";
        const string Mouse = @"Control Panel\Mouse";

        const string CapturesPage = "ms-settings:gaming-gamedvr";
        const string GraphicsPage = "ms-settings:display-advancedgraphics";
        const string CoreIsolationPage = "windowsdefender://coreisolation";
        const string PowerPage = "ms-settings:powersleep";

        /// <summary>As únicas páginas que GameContext.OpenSettings abre (além de main.cpl).</summary>
        public static readonly HashSet<string> SettingsPages = new HashSet<string>(StringComparer.Ordinal) { CapturesPage, GraphicsPage, CoreIsolationPage, PowerPage };

        /// <summary>Apps de sistema que fazem o login na conta Xbox dos jogos, com o ID de cada um na Microsoft Store.</summary>
        static readonly string[,] XboxApps =
        {
            { "Microsoft.XboxIdentityProvider", "Xbox Identity Provider", "9WZDNCRD1HKW" },
            { "Microsoft.Xbox.TCUI", "Xbox TCUI", "9NKNC0LD5NN6" },
            { "Microsoft.GamingServices", "Gaming Services", "9MWPM2CQNLHN" }
        };

        /// <summary>Planos que vêm com o Windows (inclusive as sobreposições do modo de energia).</summary>
        static readonly HashSet<string> BuiltInSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Balanced, "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "a1841308-3541-4fab-bc81-f71556f20b4a",
            "e9a42b02-d5df-448d-aa00-03f14749eb61", "3af9b8d9-7c97-431d-ad78-34a8bfea439f",
            "961cc777-2547-4f9d-8174-7d86181b8a7a", "ded574b5-45a0-4f42-8737-46345c09c238"
        };

        public static List<GameFinding> Scan(GameContext c)
        {
            var found = new List<GameFinding>();
            foreach (var check in new Action<GameContext, List<GameFinding>>[]
                { BrowserUpdates, WindowsUpdate, DefenderPolicy, UacOff, CpuMitigations, BootClock,
                  GameMode, GameDvrPolicyCheck, XboxSignIn, Background, PowerPlan,
                  BackgroundRecording, HardwareScheduling, WindowedOptimizations, MousePrecision, PowerMode, MemoryIntegrity })
            {
                try { check(c, found); }
                catch { }
            }
            return found;
        }

        static int? ReadDword(RegistryKey root, string key, string name)
        {
            try
            {
                using (var k = root.OpenSubKey(key))
                {
                    object v = k == null ? null : k.GetValue(name);
                    return v is int ? (int?)(int)v : null;
                }
            }
            catch { return null; }
        }

        /// <summary>Nome da raiz em forma curta (HKCU, HKLM); numa sandbox de teste, o caminho dela continua depois.</summary>
        static string Short(RegistryKey root)
        {
            return root.Name.Replace("HKEY_CURRENT_USER", "HKCU").Replace("HKEY_LOCAL_MACHINE", "HKLM");
        }

        static string Where(RegistryKey root, string key, string name)
        {
            return Short(root) + "\\" + key + "  [" + name + "]";
        }

        static void GameMode(GameContext c, List<GameFinding> found)
        {
            bool off = ReadDword(c.Hkcu, GameBar, "AutoGameModeEnabled") == 0;
            bool blocked = ReadDword(c.Hkcu, GameBar, "AllowAutoGameMode") == 0;
            if (!off && !blocked) return;
            var f = new GameFinding
            {
                Id = "game-mode-off",
                Name = L.T("Modo de Jogo desligado", "Game Mode turned off"),
                Why = L.T("O Modo de Jogo dá prioridade ao jogo e impede que o Windows Update instale drivers ou peça para reiniciar durante a partida. Otimizadores como o Nich FPS+ desligam o Modo de Jogo, o contrário do que prometem.",
                          "Game Mode gives the game priority and keeps Windows Update from installing drivers or asking to restart while you play. Optimizers such as Nich FPS+ turn it off, the opposite of what they promise."),
                Location = Where(c.Hkcu, GameBar, off ? "AutoGameModeEnabled" : "AllowAutoGameMode"),
                Current = L.T("desligado", "off"),
                Expected = L.T("ligado", "on")
            };
            f.Edits.Add(new RegEdit { Root = c.Hkcu, Key = GameBar, Name = "AutoGameModeEnabled", Value = 1 });
            if (blocked) f.Edits.Add(new RegEdit { Root = c.Hkcu, Key = GameBar, Name = "AllowAutoGameMode" });
            found.Add(f);
        }

        static void GameDvrPolicyCheck(GameContext c, List<GameFinding> found)
        {
            if (ReadDword(c.Hklm, GameDvrPolicy, "AllowGameDVR") != 0) return;
            var f = new GameFinding
            {
                Id = "gamedvr-policy",
                Name = L.T("Política que bloqueia a Xbox Game Bar", "Policy that blocks the Xbox Game Bar"),
                Why = L.T("Uma política de empresa impede a Game Bar de gravar e faz as Configurações dizerem que algumas opções são gerenciadas pela sua organização. Num PC de casa, ela foi gravada por um otimizador. Sem a política, você volta a escolher nas Configurações se quer gravar.",
                          "A company policy keeps the Game Bar from recording and makes Settings say some options are managed by your organization. On a home PC, an optimizer put it there. Without it, you choose again in Settings whether to record."),
                Location = Where(c.Hklm, GameDvrPolicy, "AllowGameDVR"),
                Current = L.T("bloqueada por política", "blocked by policy"),
                Expected = L.T("sem política", "no policy"),
                NeedsAdmin = true
            };
            f.Edits.Add(new RegEdit { Root = c.Hklm, Key = GameDvrPolicy, Name = "AllowGameDVR" });
            found.Add(f);
        }

        static void XboxSignIn(GameContext c, List<GameFinding> found)
        {
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var k = c.Hkcu.OpenSubKey(Packages))
            {
                if (k == null) return; // sem a lista não dá para afirmar que falta algo
                foreach (var full in k.GetSubKeyNames())
                {
                    int i = full.IndexOf('_');
                    installed.Add(i < 0 ? full : full.Substring(0, i));
                }
            }
            // Sem a Microsoft Store (Windows LTSC, imagens corporativas), esses apps nunca vieram e não há como instalar.
            if (!installed.Contains("Microsoft.WindowsStore")) return;

            var missing = new List<int>();
            for (int i = 0; i < XboxApps.GetLength(0); i++)
            {
                if (installed.Contains(XboxApps[i, 0])) continue;
                // Gaming Services só vem com o app Xbox; sem ele, a falta não quer dizer nada.
                if (XboxApps[i, 0] == "Microsoft.GamingServices" && !installed.Contains("Microsoft.GamingApp")) continue;
                missing.Add(i);
            }
            if (missing.Count == 0) return;

            string names = string.Join(", ", missing.Select(i => XboxApps[i, 1]));
            string first = XboxApps[missing[0], 1];
            string uri = "ms-windows-store://pdp/?ProductId=" + XboxApps[missing[0], 2];
            found.Add(new GameFinding
            {
                Id = "xbox-sign-in",
                Name = missing.Count == 1 ? L.F("Falta o {0}, usado no login dos jogos", "{0} is missing; games use it to sign in", first)
                                          : L.T("Faltam apps do Xbox usados no login dos jogos", "Xbox apps that games use to sign in are missing"),
                Why = L.T("Jogos da Microsoft Store e do PC Game Pass (Minecraft, Forza, Halo, Sea of Thieves) usam esses apps do sistema para entrar na conta Xbox. Sem eles, o jogo não faz login ou nem abre. Otimizadores removem esses apps junto com o resto do Xbox. O OpenCleaner não instala nada sozinho: aplicar abre a página do ",
                          "Microsoft Store and PC Game Pass games (Minecraft, Forza, Halo, Sea of Thieves) use these system apps to sign in to Xbox. Without them, the game cannot sign in or does not start at all. Optimizers remove them along with the rest of Xbox. OpenCleaner installs nothing by itself: applying opens the Microsoft Store page for ") +
                      first + L.T(" na Microsoft Store, onde você clica em Obter.", ", where you click Get."),
                Location = names,
                Current = missing.Count == 1 ? L.T("não instalado", "not installed") : L.F("{0} não instalados", "{0} not installed", missing.Count),
                Expected = L.T("instalados", "installed"),
                Run = () => c.OpenStore(uri),
                AfterApply = L.F("A Microsoft Store abriu na página do {0}: clique em Obter e depois em Verificar de novo.",
                                 "The Microsoft Store opened on the {0} page: click Get, then Check again.", first)
            });
        }

        static void Background(GameContext c, List<GameFinding> found)
        {
            const string why = "Apps da Microsoft Store desligados em segundo plano não mostram notificações nem alarmes, e isso inclui o app Xbox e a Game Bar. O ganho em jogos é nenhum: o Windows já suspende esses apps quando não estão em uso.";
            const string whyEn = "Microsoft Store apps turned off in the background show no notifications or alarms, including the Xbox app and the Game Bar. There is no gain in games: Windows already suspends these apps when they are not in use.";
            if (ReadDword(c.Hkcu, BackgroundApps, "GlobalUserDisabled") == 1)
            {
                var f = new GameFinding
                {
                    Id = "background-apps",
                    Name = L.T("Todos os apps proibidos de rodar em segundo plano", "All apps barred from running in the background"),
                    Why = L.T(why, whyEn),
                    Location = Where(c.Hkcu, BackgroundApps, "GlobalUserDisabled"),
                    Current = L.T("desligado para todos", "off for all"),
                    Expected = L.T("cada app decide", "each app decides")
                };
                f.Edits.Add(new RegEdit { Root = c.Hkcu, Key = BackgroundApps, Name = "GlobalUserDisabled" });
                found.Add(f);
            }
            if (ReadDword(c.Hklm, AppPrivacyPolicy, "LetAppsRunInBackground") == 2)
            {
                var f = new GameFinding
                {
                    Id = "background-apps-policy",
                    Name = L.T("Política que proíbe apps em segundo plano", "Policy that bars background apps"),
                    Why = L.T(why, whyEn),
                    Location = Where(c.Hklm, AppPrivacyPolicy, "LetAppsRunInBackground"),
                    Current = L.T("proibido por política", "barred by policy"),
                    Expected = L.T("sem política", "no policy"),
                    NeedsAdmin = true
                };
                f.Edits.Add(new RegEdit { Root = c.Hklm, Key = AppPrivacyPolicy, Name = "LetAppsRunInBackground" });
                found.Add(f);
            }

            // Otimizadores marcam Disabled=1 em todos os apps de uma vez. Quem desliga à mão desliga alguns.
            var bulk = new List<RegEdit>();
            int apps = 0;
            using (var k = c.Hkcu.OpenSubKey(BackgroundApps))
            {
                if (k == null) return;
                foreach (var name in k.GetSubKeyNames())
                {
                    apps++;
                    string sub = BackgroundApps + "\\" + name;
                    if (ReadDword(c.Hkcu, sub, "Disabled") == 1 && ReadDword(c.Hkcu, sub, "DisabledByUser") == 1)
                    {
                        bulk.Add(new RegEdit { Root = c.Hkcu, Key = sub, Name = "Disabled" });
                        bulk.Add(new RegEdit { Root = c.Hkcu, Key = sub, Name = "DisabledByUser" });
                    }
                }
            }
            int disabled = bulk.Count / 2;
            if (apps < 10 || disabled * 10 < apps * 8) return; // menos de 80% desligados: escolha do usuário
            var b = new GameFinding
            {
                Id = "background-apps-bulk",
                Name = L.F("{0} de {1} apps desligados em segundo plano, um por um", "{0} of {1} apps turned off in the background, one by one", disabled, apps),
                Why = L.T(why, whyEn) + L.T(" Desligar quase todos de uma vez é a marca de um otimizador. Se foi você quem escolheu, deixe este item desmarcado.",
                                            " Turning almost all of them off at once is an optimizer's signature. If you chose this yourself, leave this item unchecked."),
                Location = Short(c.Hkcu) + "\\" + BackgroundApps + "\\*  [Disabled, DisabledByUser]",
                Current = L.F("{0} desligados", "{0} off", disabled),
                Expected = L.T("cada app decide", "each app decides"),
                DefaultChecked = false
            };
            b.Edits.AddRange(bulk);
            found.Add(b);
        }

        static void PowerPlan(GameContext c, List<GameFinding> found)
        {
            if (!c.HasBattery) return; // em PC de mesa, um plano de alto desempenho não tem custo de bateria
            string active, friendly, description;
            using (var k = c.Hklm.OpenSubKey(PowerSchemes))
            {
                active = k == null ? null : Util.ReadString(k, "ActivePowerScheme");
                if (active == null || BuiltInSchemes.Contains(active)) return;
                using (var s = k.OpenSubKey(active))
                {
                    if (s == null) return;
                    friendly = Util.ReadString(s, "FriendlyName") ?? active;
                    description = Util.ReadString(s, "Description") ?? "";
                }
            }
            // Cópia do Desempenho Máximo (-18) ou do Alto desempenho (-12): planos de fabricante (Lenovo, Dell) têm descrição própria.
            bool copy = description.IndexOf(@"powrprof.dll,-18,", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        description.IndexOf(@"powrprof.dll,-12,", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!copy) return;
            if (friendly.StartsWith("@") && friendly.Contains(",")) friendly = friendly.Substring(friendly.LastIndexOf(',') + 1);
            var f = new GameFinding
            {
                Id = "power-plan-copy",
                Name = L.F("Plano de energia \"{0}\" ativo num notebook", "\"{0}\" power plan active on a laptop", friendly),
                Why = L.T("É uma cópia do Desempenho Máximo, feito para PCs de mesa. Num notebook, ele mantém o processador acelerado o tempo todo: mais calor, menos bateria e, com o calor, o processador reduz a velocidade justamente durante o jogo. O plano Equilibrado já acelera quando o jogo pede. O plano copiado continua na lista, e dá para voltar a ele em Opções de Energia.",
                          "It is a copy of Ultimate Performance, made for desktops. On a laptop it keeps the processor boosted all the time: more heat, less battery and, because of the heat, the processor slows down right when you are playing. The Balanced plan already boosts when the game needs it. The copied plan stays in the list, and you can switch back to it in Power Options."),
                Location = Short(c.Hklm) + "\\" + PowerSchemes + "  [ActivePowerScheme = " + active + "]",
                Current = friendly,
                Expected = L.T("Equilibrado", "Balanced"),
                Run = () => c.SetActivePowerScheme(Balanced)
            };
            found.Add(f);
        }

        // ---- Segurança: o que boosters desligam "para ganhar FPS" ----

        const string Services = @"SYSTEM\CurrentControlSet\Services";

        /// <summary>Serviço desativado (Start=4) que deve voltar ao padrão: Start 2 (automático) ou 3 (manual).</summary>
        class ServiceDefault
        {
            public string Name;
            public int Start;
            public bool Delayed;
        }

        /// <summary>Serviços que existem e estão desativados, com o padrão de cada um. Curingas só valem com o executável na pasta esperada.</summary>
        static List<ServiceDefault> DisabledServices(GameContext c, IEnumerable<ServiceDefault> catalog, string wildcardPrefix = null, string wildcardFolder = null, int wildcardStart = 2)
        {
            var result = new List<ServiceDefault>();
            using (var k = c.Hklm.OpenSubKey(Services))
            {
                if (k == null) return result;
                var all = k.GetSubKeyNames();
                var wanted = catalog.ToList();
                if (wildcardPrefix != null)
                    // Só nomes que o Gerenciador de Serviços aceitaria: nada de espaço, &, aspas.
                    foreach (var n in all.Where(n => n.StartsWith(wildcardPrefix, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(n, @"^[A-Za-z0-9_.\-]+$")))
                    {
                        // GoogleUpdaterService157.0.8089.0: aceita só se o executável estiver em Program Files\Google.
                        string image;
                        using (var s = k.OpenSubKey(n)) image = s == null ? null : Util.ExtractExePath(Util.ReadString(s, "ImagePath"));
                        if (image != null && UnderProgramFiles(image, wildcardFolder))
                            wanted.Add(new ServiceDefault { Name = n, Start = wildcardStart });
                    }
                foreach (var svc in wanted)
                    if (all.Contains(svc.Name, StringComparer.OrdinalIgnoreCase) && ReadDword(c.Hklm, Services + "\\" + svc.Name, "Start") == 4)
                        result.Add(svc);
            }
            return result;
        }

        static bool UnderProgramFiles(string exe, string folder)
        {
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                       Environment.GetEnvironmentVariable("ProgramW6432") })
                if (!string.IsNullOrEmpty(pf) && exe.StartsWith(Path.Combine(pf, folder) + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static void AddServiceFix(GameContext c, GameFinding f, List<ServiceDefault> services)
        {
            foreach (var s in services) f.AlsoBackup.Add(new KeyValuePair<RegistryKey, string>(c.Hklm, Services + "\\" + s.Name));
            var run = f.Run;
            f.Run = () =>
            {
                bool ok = run == null || run();
                foreach (var s in services) ok &= c.SetServiceStart != null && c.SetServiceStart(s.Name, s.Start, s.Delayed);
                return ok;
            };
        }

        static void BrowserUpdates(GameContext c, List<GameFinding> found)
        {
            var chrome = DisabledServices(c, new[]
            {
                new ServiceDefault { Name = "gupdate", Start = 2, Delayed = true },
                new ServiceDefault { Name = "gupdatem", Start = 3 },
                new ServiceDefault { Name = "GoogleChromeElevationService", Start = 3 }
            }, "GoogleUpdater", "Google");
            var edge = DisabledServices(c, new[]
            {
                new ServiceDefault { Name = "edgeupdate", Start = 2, Delayed = true },
                new ServiceDefault { Name = "edgeupdatem", Start = 3 },
                new ServiceDefault { Name = "MicrosoftEdgeElevationService", Start = 3 }
            });
            foreach (var pair in new[] { new { Name = "Google Chrome", List = chrome }, new { Name = "Microsoft Edge", List = edge } })
            {
                if (pair.List.Count == 0) continue;
                var f = new GameFinding
                {
                    Id = pair.Name == "Google Chrome" ? "chrome-updates" : "edge-updates",
                    Security = true,
                    NeedsAdmin = true,
                    Name = L.F("Atualização automática do {0} desativada", "{0} automatic updates disabled", pair.Name),
                    Why = L.F("O navegador é a porta de entrada da maior parte dos ataques, e as correções de segurança chegam pela atualização automática. Com esses serviços desativados, o {0} para de se atualizar sem avisar. Boosters desativam esses serviços para \"liberar memória\", com ganho nulo em jogos.",
                              "The browser is the entry point for most attacks, and security fixes arrive through automatic updates. With these services disabled, {0} silently stops updating. Boosters disable them to \"free memory\", with no gain in games.", pair.Name),
                    Location = string.Join(", ", pair.List.Select(s => s.Name)),
                    Current = L.T("desativada", "disabled"),
                    Expected = L.T("automática", "automatic")
                };
                AddServiceFix(c, f, pair.List);
                found.Add(f);
            }
        }

        static void WindowsUpdate(GameContext c, List<GameFinding> found)
        {
            const string wu = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
            var services = DisabledServices(c, new[]
            {
                new ServiceDefault { Name = "wuauserv", Start = 3 },
                new ServiceDefault { Name = "UsoSvc", Start = 2, Delayed = true }
            });
            var edits = new List<RegEdit>();
            bool wsus;
            using (var k = c.Hklm.OpenSubKey(wu)) wsus = k != null && Util.ReadString(k, "WUServer") != null;
            if (!wsus) // com WSUS, a empresa gerencia as atualizações por política: não é coisa de booster
            {
                if (ReadDword(c.Hklm, wu + @"\AU", "NoAutoUpdate") == 1) edits.Add(new RegEdit { Root = c.Hklm, Key = wu + @"\AU", Name = "NoAutoUpdate" });
                if (ReadDword(c.Hklm, wu, "DisableWindowsUpdateAccess") == 1) edits.Add(new RegEdit { Root = c.Hklm, Key = wu, Name = "DisableWindowsUpdateAccess" });
            }
            if (services.Count == 0 && edits.Count == 0) return;
            var f = new GameFinding
            {
                Id = "windows-update",
                Security = true,
                NeedsAdmin = true,
                Name = L.T("Windows Update desligado", "Windows Update turned off"),
                Why = L.T("Sem o Windows Update, o Windows não recebe correções de segurança nem drivers novos de placa de vídeo. O Modo de Jogo já impede que atualizações sejam instaladas durante a partida; desligar o Windows Update inteiro só deixa o PC exposto.",
                          "Without Windows Update, Windows gets no security fixes and no new graphics drivers. Game Mode already keeps updates from installing while you play; turning off Windows Update entirely just leaves the PC exposed."),
                Location = string.Join(", ", services.Select(s => s.Name).Concat(edits.Select(e => e.Name))),
                Current = L.T("desligado", "off"),
                Expected = L.T("ligado", "on")
            };
            f.Edits.AddRange(edits);
            if (services.Count > 0) AddServiceFix(c, f, services);
            found.Add(f);
        }

        static void DefenderPolicy(GameContext c, List<GameFinding> found)
        {
            const string def = @"SOFTWARE\Policies\Microsoft\Windows Defender";
            const string rtp = def + @"\Real-Time Protection";
            var edits = new List<RegEdit>();
            if (ReadDword(c.Hklm, def, "DisableAntiSpyware") == 1) edits.Add(new RegEdit { Root = c.Hklm, Key = def, Name = "DisableAntiSpyware" });
            foreach (var n in new[] { "DisableRealtimeMonitoring", "DisableBehaviorMonitoring" })
                if (ReadDword(c.Hklm, rtp, n) == 1) edits.Add(new RegEdit { Root = c.Hklm, Key = rtp, Name = n });
            if (edits.Count == 0) return;
            int? tamper = ReadDword(c.Hklm, @"SOFTWARE\Microsoft\Windows Defender\Features", "TamperProtection");
            bool tamperOn = tamper.HasValue && (tamper.Value & 1) == 1; // 5 (ou 1) = ligada; 4 (ou 0) = desligada
            var f = new GameFinding
            {
                Id = "defender-policy",
                Security = true,
                NeedsAdmin = true,
                Name = L.T("Políticas que desligam o Microsoft Defender", "Policies that turn off Microsoft Defender"),
                Why = L.T("Essas políticas de empresa desligam a proteção em tempo real do Defender. O custo do Defender em jogos é pequeno, e jogos piratas e \"otimizadores\" são justamente uma porta comum de vírus.",
                          "These company policies turn off Defender real-time protection. Defender costs little in games, and cracked games and \"optimizers\" are a common way in for malware.") +
                      (tamperOn ? L.T(" A Proteção contra Adulteração está ligada: o Defender já ignora essas políticas e pode recusar a remoção. Por isso o item vem desmarcado.",
                                      " Tamper Protection is on: Defender already ignores these policies and may refuse to remove them. That is why this item starts unchecked.") : ""),
                Location = Where(c.Hklm, def, string.Join(", ", edits.Select(e => e.Name))),
                Current = tamperOn ? L.T("desligado por política (ignorada)", "off by policy (ignored)") : L.T("desligado por política", "off by policy"),
                Expected = L.T("sem política", "no policy"),
                DefaultChecked = !tamperOn
            };
            f.Edits.AddRange(edits);
            found.Add(f);
        }

        static void UacOff(GameContext c, List<GameFinding> found)
        {
            const string sys = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
            if (ReadDword(c.Hklm, sys, "EnableLUA") != 0) return;
            var f = new GameFinding
            {
                Id = "uac-off",
                Security = true,
                NeedsAdmin = true,
                Name = L.T("Controle de Conta de Usuário (UAC) desligado", "User Account Control (UAC) turned off"),
                Why = L.T("Sem o UAC, qualquer programa roda com poder total sobre o Windows, sem pedir licença, e apps da Microsoft Store (inclusive jogos do Game Pass) podem nem abrir. O UAC não afeta o desempenho dos jogos.",
                          "Without UAC, any program runs with full control over Windows without asking, and Microsoft Store apps (including Game Pass games) may not open at all. UAC does not affect game performance."),
                Location = Where(c.Hklm, sys, "EnableLUA"),
                Current = L.T("desligado", "off"),
                Expected = L.T("ligado", "on"),
                AfterApply = L.T("Reinicie o PC para o UAC voltar a funcionar.", "Restart the PC for UAC to work again.")
            };
            f.Edits.Add(new RegEdit { Root = c.Hklm, Key = sys, Name = "EnableLUA", Value = 1 });
            found.Add(f);
        }

        static void CpuMitigations(GameContext c, List<GameFinding> found)
        {
            const string mm = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
            // 3 desliga as proteções contra Spectre (variante 2) e Meltdown. Outros valores (8, 72...) LIGAM proteções extras.
            if (ReadDword(c.Hklm, mm, "FeatureSettingsOverride") != 3) return;
            var f = new GameFinding
            {
                Id = "cpu-mitigations",
                Security = true,
                NeedsAdmin = true,
                Name = L.T("Proteções contra Spectre e Meltdown desligadas", "Spectre and Meltdown protections turned off"),
                Why = L.T("Essas proteções impedem que um programa leia a memória de outro por falhas do processador. Boosters as desligam prometendo FPS; em processadores dos últimos anos o ganho em jogos é desprezível, e o risco continua.",
                          "These protections stop one program from reading another's memory through processor flaws. Boosters turn them off promising FPS; on recent processors the gain in games is negligible, and the risk remains."),
                Location = Where(c.Hklm, mm, "FeatureSettingsOverride"),
                Current = L.T("desligadas", "off"),
                Expected = L.T("padrão do Windows", "Windows default"),
                AfterApply = L.T("Reinicie o PC para as proteções do processador voltarem.", "Restart the PC for the processor protections to come back.")
            };
            f.Edits.Add(new RegEdit { Root = c.Hklm, Key = mm, Name = "FeatureSettingsOverride" });
            f.Edits.Add(new RegEdit { Root = c.Hklm, Key = mm, Name = "FeatureSettingsOverrideMask" });
            found.Add(f);
        }

        static void BootClock(GameContext c, List<GameFinding> found)
        {
            string config = c.BootConfig == null ? null : c.BootConfig();
            if (string.IsNullOrEmpty(config)) return;
            // Os nomes dos valores não são traduzidos pelo bcdedit; os valores ("Yes", "Sim") são. Qualquer valor
            // presente já é diferente do padrão (o Windows não grava nenhum deles), e apagar é sempre seguro.
            var present = new List<KeyValuePair<string, string>>();
            foreach (var line in config.Split('\n'))
            {
                var m = Regex.Match(line.Trim(), @"^(\S+)\s+(.+)$");
                if (m.Success && GameContext.BootClockValues.Contains(m.Groups[1].Value.ToLowerInvariant()))
                    present.Add(new KeyValuePair<string, string>(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.Trim()));
            }
            if (present.Count == 0) return;
            var f = new GameFinding
            {
                Id = "boot-clock",
                NeedsAdmin = true,
                Name = L.T("Relógio do sistema forçado na configuração de inicialização", "System clock forced in the boot configuration"),
                Why = L.T("Boosters gravam useplatformclock, useplatformtick ou disabledynamictick na configuração de inicialização (BCD) prometendo menos latência. Forçar o relógio da plataforma (HPET) costuma deixar os jogos mais lentos, e desligar o tick dinâmico gasta mais energia. Sem esses valores, o Windows escolhe o melhor relógio para o seu hardware. Exige reiniciar, e o backup .reg não cobre a configuração de inicialização.",
                          "Boosters write useplatformclock, useplatformtick or disabledynamictick to the boot configuration (BCD) promising lower latency. Forcing the platform clock (HPET) tends to make games slower, and turning off the dynamic tick uses more power. Without these values, Windows picks the best clock for your hardware. Requires a restart, and the .reg backup does not cover the boot configuration."),
                Location = "bcdedit {current}: " + string.Join(", ", present.Select(p => p.Key + " = " + p.Value)),
                Current = string.Join(", ", present.Select(p => p.Key)),
                Expected = L.T("padrão do Windows", "Windows default"),
                AfterApply = L.T("Reinicie o PC para o relógio do sistema voltar ao padrão.", "Restart the PC for the system clock to go back to the default."),
                Run = () => present.All(p => c.DeleteBootValue != null && c.DeleteBootValue(p.Key))
            };
            found.Add(f);
        }

        // ---- Recomendado para jogos e Desempenho × segurança: só leem e abrem a página do Windows ----

        static GameFinding Advice(GameKind kind, string id, string page)
        {
            return new GameFinding { Kind = kind, Id = id, SettingsUri = page, DefaultChecked = false };
        }

        static void BackgroundRecording(GameContext c, List<GameFinding> found)
        {
            if (ReadDword(c.Hkcu, GameDvr, "HistoricalCaptureEnabled") != 1) return;
            var f = Advice(GameKind.Recommended, "background-recording", CapturesPage);
            f.Name = L.T("Gravação em segundo plano ligada", "Background recording on");
            f.Why = L.T("\"Gravar o que aconteceu\" grava os últimos minutos de todo jogo o tempo todo, para você salvar um lance depois. Isso usa a placa de vídeo e o disco durante a partida. Se você não usa, desligar devolve esse desempenho ao jogo.",
                        "\"Record what happened\" keeps recording the last minutes of every game all the time, so you can save a moment later. That uses the graphics card and the disk while you play. If you do not use it, turning it off gives that performance back to the game.");
            f.Location = Where(c.Hkcu, GameDvr, "HistoricalCaptureEnabled");
            f.Current = L.T("ligada", "on");
            f.Expected = L.T("desligar, se não usa", "turn off if unused");
            found.Add(f);
        }

        static void HardwareScheduling(GameContext c, List<GameFinding> found)
        {
            // Só quando está explicitamente desligado (1): ausente pode querer dizer que o driver nem oferece a opção.
            if (ReadDword(c.Hklm, GraphicsDrivers, "HwSchMode") != 1) return;
            var f = Advice(GameKind.Recommended, "hags", GraphicsPage);
            f.Name = L.T("Agendamento de GPU acelerado por hardware desligado", "Hardware-accelerated GPU scheduling off");
            f.Why = L.T("Com o agendamento ligado, a própria placa de vídeo organiza o trabalho dela, o que pode reduzir a latência. O Frame Generation da NVIDIA (DLSS 3) exige essa opção. Mudar pede reiniciar o PC.",
                        "With scheduling on, the graphics card organizes its own work, which can reduce latency. NVIDIA Frame Generation (DLSS 3) requires it. Changing it needs a restart.");
            f.Location = Where(c.Hklm, GraphicsDrivers, "HwSchMode");
            f.Current = L.T("desligado", "off");
            f.Expected = L.T("ligar", "turn on");
            found.Add(f);
        }

        static void WindowedOptimizations(GameContext c, List<GameFinding> found)
        {
            if (c.WindowsBuild < 22000) return; // opção do Windows 11
            string value;
            using (var k = c.Hkcu.OpenSubKey(DirectXUser))
                value = k == null ? null : Util.ReadString(k, "DirectXUserGlobalSettings");
            if (value == null || value.IndexOf("SwapEffectUpgradeEnable=0", StringComparison.OrdinalIgnoreCase) < 0) return;
            var f = Advice(GameKind.Recommended, "windowed-optimizations", GraphicsPage);
            f.Name = L.T("Otimizações para jogos em janela desligadas", "Optimizations for windowed games off");
            f.Why = L.T("No Windows 11, essa opção faz jogos em janela sem bordas funcionarem quase como em tela cheia: menos latência e suporte a taxa de atualização variável (G-Sync, FreeSync).",
                        "On Windows 11, this option makes borderless windowed games work almost like full screen: lower latency and variable refresh rate support (G-Sync, FreeSync).");
            f.Location = Where(c.Hkcu, DirectXUser, "DirectXUserGlobalSettings");
            f.Current = L.T("desligadas", "off");
            f.Expected = L.T("ligar", "turn on");
            found.Add(f);
        }

        static void MousePrecision(GameContext c, List<GameFinding> found)
        {
            string speed;
            using (var k = c.Hkcu.OpenSubKey(Mouse))
                speed = k == null ? null : Util.ReadString(k, "MouseSpeed");
            if (speed != "1" && speed != "2") return;
            var f = Advice(GameKind.Recommended, "mouse-precision", "main.cpl");
            f.Name = L.T("\"Aumentar a precisão do ponteiro\" ligado", "\"Enhance pointer precision\" on");
            f.Why = L.T("Essa opção acelera o ponteiro conforme a velocidade da mão: o mesmo movimento leva a mira a lugares diferentes. Em jogos de tiro, desligar deixa a mira previsível. Muitos jogos já ignoram a opção (entrada direta); vale para os que não ignoram. Fica na aba Opções de ponteiro.",
                        "This option speeds up the pointer based on how fast you move your hand: the same motion lands your aim in different places. In shooters, turning it off makes aiming predictable. Many games already ignore it (raw input); it matters for those that do not. It is on the Pointer Options tab.");
            f.Location = Where(c.Hkcu, Mouse, "MouseSpeed");
            f.Current = L.T("ligado", "on");
            f.Expected = L.T("desligar, para jogos de tiro", "turn off for shooters");
            found.Add(f);
        }

        // Modo de energia (o controle deslizante): GUIDs documentados em "Customize the Windows performance power slider".
        const string BetterBatteryOverlay = "961cc777-2547-4f9d-8174-7d86181b8a7a";
        const string BestPerformanceOverlay = "ded574b5-45a0-4f42-8737-46345c09c238";

        static void PowerMode(GameContext c, List<GameFinding> found)
        {
            // Só em notebook (em PC de mesa o controle depende do fabricante) e com o Equilibrado ativo
            // (com outro plano o Windows esconde o controle).
            if (!c.HasBattery) return;
            string active, ac;
            using (var k = c.Hklm.OpenSubKey(PowerSchemes))
            {
                if (k == null) return;
                active = Util.ReadString(k, "ActivePowerScheme");
                ac = Util.ReadString(k, "ActiveOverlayAcPowerScheme");
            }
            if (!string.Equals(active, Balanced, StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(ac, BestPerformanceOverlay, StringComparison.OrdinalIgnoreCase)) return;
            var f = Advice(GameKind.Recommended, "power-mode", PowerPage);
            f.Name = L.T("Modo de energia na tomada não é o de melhor desempenho", "Plugged-in power mode is not best performance");
            f.Why = L.T("Ligado na tomada, o modo de energia \"Melhor desempenho\" deixa o processador acelerar mais e por mais tempo, sem o custo de bateria. Ele vale só na tomada; na bateria, o Windows continua usando o modo que você escolheu para ela. Fica em Configurações > Sistema > Energia, ou no ícone da bateria na barra de tarefas.",
                        "When plugged in, the \"Best performance\" power mode lets the processor boost higher and for longer, at no battery cost. It applies only when plugged in; on battery, Windows keeps the mode you chose for it. It is in Settings > System > Power, or in the battery icon on the taskbar.");
            f.Location = Where(c.Hklm, PowerSchemes, "ActiveOverlayAcPowerScheme");
            f.Current = string.Equals(ac, BetterBatteryOverlay, StringComparison.OrdinalIgnoreCase)
                ? L.T("melhor duração da bateria", "better battery") : L.T("equilibrado", "balanced");
            f.Expected = L.T("melhor desempenho, na tomada", "best performance, plugged in");
            found.Add(f);
        }

        static void MemoryIntegrity(GameContext c, List<GameFinding> found)
        {
            if (c.MemoryIntegrityRunning == null || !c.MemoryIntegrityRunning()) return;
            var f = Advice(GameKind.Tradeoff, "memory-integrity", CoreIsolationPage);
            f.Name = L.T("Integridade de Memória ligada", "Memory integrity on");
            f.Why = L.T("A Integridade de Memória impede que drivers maliciosos ou com falha rodem no núcleo do Windows. A Microsoft diz que desligar pode melhorar o desempenho em alguns jogos, em troca de menos proteção. O OpenCleaner não recomenda nem desliga: a escolha é sua, em Segurança do Windows.",
                        "Memory integrity keeps malicious or faulty drivers from running in the Windows core. Microsoft says turning it off can improve performance in some games, at the cost of less protection. OpenCleaner neither recommends nor changes it: it is your choice, in Windows Security.");
            f.Location = L.T("Segurança do Windows > Isolamento de núcleo", "Windows Security > Core isolation");
            f.Current = L.T("ligada (mais proteção)", "on (more protection)");
            f.Expected = L.T("escolha sua", "your choice");
            found.Add(f);
        }

        // ---- Backup e conserto ----

        /// <summary>Chaves que os consertos vão alterar, no formato do Util.ExportRegistry.</summary>
        public static IEnumerable<KeyValuePair<string, RegistryView>> BackupKeys(IEnumerable<GameFinding> findings)
        {
            var list = findings.ToList();
            return list.SelectMany(f => f.Edits.Select(e => new KeyValuePair<RegistryKey, string>(e.Root, e.Key)).Concat(f.AlsoBackup))
                       .Select(k => new KeyValuePair<string, RegistryView>(k.Key.Name + "\\" + k.Value, k.Key.View));
        }

        /// <summary>Exporta para um .reg as chaves que os consertos vão alterar.</summary>
        public static bool Backup(IEnumerable<GameFinding> findings, string target, out string error)
        {
            return Util.ExportRegistry(BackupKeys(findings), target, out error);
        }

        public static bool Fix(GameFinding f)
        {
            if (f.Kind != GameKind.Fix) return false; // recomendações só abrem a página do Windows
            bool ok = true;
            foreach (var e in f.Edits)
            {
                try
                {
                    if (e.Value == null)
                    {
                        using (var k = e.Root.OpenSubKey(e.Key, true))
                            if (k != null) k.DeleteValue(e.Name, false);
                    }
                    else
                    {
                        using (var k = e.Root.CreateSubKey(e.Key))
                            k.SetValue(e.Name, e.Value.Value, RegistryValueKind.DWord);
                    }
                }
                catch { ok = false; }
            }
            if (f.Run != null) ok &= f.Run();
            return ok;
        }
    }
}
