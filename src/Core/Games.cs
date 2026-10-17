using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    /// <summary>Algo que um otimizador mudou e que prejudica jogos, segurança ou bateria.</summary>
    class GameFinding
    {
        public string Id, Name, Why, Location, Current, Expected;
        public bool NeedsAdmin, DefaultChecked = true;
        public readonly List<RegEdit> Edits = new List<RegEdit>();
        /// <summary>Ação fora do registro (só a troca do plano de energia).</summary>
        public Func<bool> Run;
    }

    /// <summary>De onde o scanner lê e onde o conserto escreve. Os testes trocam as raízes por uma sandbox.</summary>
    class GameContext
    {
        public RegistryKey Hklm, Hkcu;
        public bool HasBattery;
        public Func<string, bool> SetActivePowerScheme;

        public static GameContext System()
        {
            return new GameContext
            {
                Hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default),
                Hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
                HasBattery = Util.HasBattery(),
                SetActivePowerScheme = PowerCfgSetActive
            };
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
            foreach (var check in new Action<GameContext, List<GameFinding>>[] { GameMode, GameDvr, Background, PowerPlan })
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

        static string Where(RegistryKey root, string key, string name)
        {
            return root.Name + "\\" + key + "  [" + name + "]";
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
                Expected = L.T("ligado (padrão do Windows)", "on (Windows default)")
            };
            f.Edits.Add(new RegEdit { Root = c.Hkcu, Key = GameBar, Name = "AutoGameModeEnabled", Value = 1 });
            if (blocked) f.Edits.Add(new RegEdit { Root = c.Hkcu, Key = GameBar, Name = "AllowAutoGameMode" });
            found.Add(f);
        }

        static void GameDvr(GameContext c, List<GameFinding> found)
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
                Expected = L.T("sem política (padrão do Windows)", "no policy (Windows default)"),
                NeedsAdmin = true
            };
            f.Edits.Add(new RegEdit { Root = c.Hklm, Key = GameDvrPolicy, Name = "AllowGameDVR" });
            found.Add(f);
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
                    Expected = L.T("cada app decide (padrão do Windows)", "each app decides (Windows default)")
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
                    Expected = L.T("sem política (padrão do Windows)", "no policy (Windows default)"),
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
                Location = c.Hkcu.Name + "\\" + BackgroundApps + "\\*  [Disabled, DisabledByUser]",
                Current = L.F("{0} desligados", "{0} off", disabled),
                Expected = L.T("cada app decide (padrão do Windows)", "each app decides (Windows default)"),
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
                Location = c.Hklm.Name + "\\" + PowerSchemes + "  [ActivePowerScheme = " + active + "]",
                Current = friendly,
                Expected = L.T("Equilibrado (padrão do Windows)", "Balanced (Windows default)"),
                Run = () => c.SetActivePowerScheme(Balanced)
            };
            found.Add(f);
        }

        // ---- Backup e conserto ----

        /// <summary>Exporta para um .reg as chaves que os consertos vão alterar.</summary>
        public static bool Backup(IEnumerable<GameFinding> findings, string target, out string error)
        {
            var keys = findings.SelectMany(f => f.Edits)
                               .Select(e => new KeyValuePair<string, RegistryView>(e.Root.Name + "\\" + e.Key, e.Root.View));
            return Util.ExportRegistry(keys, target, out error);
        }

        public static bool Fix(GameFinding f)
        {
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
