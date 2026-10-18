using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpenCleaner.Core
{
    class InstalledGame
    {
        public string Name, Store, Folder;
        public readonly List<string> Exes = new List<string>();
    }

    /// <summary>Onde procurar jogos. Os testes apontam tudo para uma sandbox.</summary>
    class GameSources
    {
        public RegistryKey Hkcu;
        public readonly List<string> SteamRoots = new List<string>();
        public string EpicManifests;
        /// <summary>Pastas XboxGames (onde o app Xbox instala os jogos do PC Game Pass), uma por unidade.</summary>
        public readonly List<string> XboxGamesRoots = new List<string>();

        public static GameSources System()
        {
            var s = new GameSources { Hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default) };
            using (var k = s.Hkcu.OpenSubKey(@"Software\Valve\Steam"))
            {
                string p = k == null ? null : Util.ReadString(k, "SteamPath");
                if (!string.IsNullOrEmpty(p)) s.SteamRoots.Add(p.Replace('/', '\\'));
            }
            s.SteamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            s.EpicManifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                {
                    string x = Path.Combine(d.RootDirectory.FullName, "XboxGames");
                    if (Directory.Exists(x)) s.XboxGamesRoots.Add(x);
                }
            }
            catch { }
            return s;
        }
    }

    /// <summary>
    /// Encontra os jogos instalados: os que o Windows já reconheceu como jogo (de qualquer loja),
    /// os da Steam e os da Epic Games. Só lê. Jogos da Microsoft Store ficam de fora por enquanto.
    /// </summary>
    static class GameLibrary
    {
        const string GameConfigStore = @"System\GameConfigStore\Children";

        /// <summary>Launchers e auxiliares: não são o jogo.</summary>
        static readonly Regex Helper = new Regex(
            @"^(unins.*|gamelaunchhelper|.*crashhandler.*|.*crashreport.*|.*redist.*|vc_?redist.*|dxsetup|dxwebsetup|directx.*|easyanticheat.*|beservice.*|battleye.*|" +
            @"cefsharp.*|qtwebengineprocess|.*helper|.*setup|.*installer|.*updater|steam|steamwebhelper|steamservice|epicgameslauncher|" +
            @"riotclient.*|battle\.net|eadesktop|ealauncher|galaxyclient|upc|ubisoftconnect|ubisoftgamelauncher)\.exe$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex HelperDir = new Regex(@"^(_commonredist|__installer|redist|redistributables|easyanticheat|battleye|support|directx|vcredist)$",
                                                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static List<InstalledGame> Find(GameSources s)
        {
            var games = new List<InstalledGame>();
            try { Steam(s, games); } catch { }
            try { Epic(s, games); } catch { }
            try { Xbox(s, games); } catch { }
            List<string> seen = new List<string>();
            try { seen = SeenByWindows(s.Hkcu); } catch { }

            // O que o Windows viu rodar vai para o jogo da loja ou vira um jogo novo.
            foreach (var exe in seen)
            {
                var owner = games.FirstOrDefault(g => IsUnder(exe, g.Folder));
                if (owner != null) { AddExe(owner, exe); continue; }
                string folder = Path.GetDirectoryName(exe);
                var same = games.FirstOrDefault(g => g.Store == "Windows" && g.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
                if (same != null) { AddExe(same, exe); continue; }
                var game = new InstalledGame { Name = ProductName(exe), Store = "Windows", Folder = folder };
                game.Exes.Add(exe);
                games.Add(game);
            }

            // Steam e Xbox sem nada visto pelo Windows: palpite pelos .exe da pasta.
            foreach (var g in games.Where(g => (g.Store == "Steam" || g.Store == "Xbox") && g.Exes.Count == 0))
                g.Exes.AddRange(GuessExes(g.Folder));
            games.RemoveAll(g => g.Exes.Count == 0);
            return games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static void AddExe(InstalledGame g, string exe)
        {
            if (!g.Exes.Contains(exe, StringComparer.OrdinalIgnoreCase)) g.Exes.Add(exe);
        }

        static bool IsUnder(string file, string folder)
        {
            string f = folder.TrimEnd('\\') + "\\";
            return file.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";

        /// <summary>Caminho absoluto e local de um .exe que existe, fora do Windows e da Store, e que não é auxiliar.</summary>
        static bool IsGameExe(string exe)
        {
            return !string.IsNullOrEmpty(exe) && exe.Length < 1024 && exe.Length > 3 && exe[1] == ':' && exe[2] == '\\' &&
                   exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                   exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) < 0 &&
                   !exe.StartsWith(WinDir, StringComparison.OrdinalIgnoreCase) &&
                   !Helper.IsMatch(Path.GetFileName(exe)) && File.Exists(exe);
        }

        /// <summary>Forma canônica (resolve "..", "." e nomes 8.3) para comparar caminhos que vieram de fora.</summary>
        static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            try { return Util.Canonical(path); }
            catch { return null; }
        }

        /// <summary>Executáveis que o Windows reconheceu como jogo e viu rodar (a mesma lista da Game Bar).</summary>
        static List<string> SeenByWindows(RegistryKey hkcu)
        {
            var list = new List<string>();
            using (var k = hkcu.OpenSubKey(GameConfigStore))
            {
                if (k == null) return list;
                foreach (var name in k.GetSubKeyNames())
                    using (var c = k.OpenSubKey(name))
                    {
                        // Só texto: um valor binário ou DWORD no lugar do caminho é ignorado.
                        string exe = c == null ? null : c.GetValue("MatchedExeFullPath") as string;
                        if (exe == null || exe.Length >= 1024) continue;
                        exe = Normalize(exe);
                        if (IsGameExe(exe) && !list.Contains(exe, StringComparer.OrdinalIgnoreCase)) list.Add(exe);
                    }
            }
            return list;
        }

        static string ProductName(string exe)
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(exe);
                string n = !string.IsNullOrWhiteSpace(v.ProductName) ? v.ProductName : v.FileDescription;
                if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
            }
            catch { }
            return Path.GetFileNameWithoutExtension(exe);
        }

        // ---- Steam: libraryfolders.vdf + appmanifest_*.acf ----

        static readonly Regex VdfPath = new Regex(@"""path""\s+""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase);

        static string VdfValue(string text, string key)
        {
            var m = Regex.Match(text, @"""" + key + @"""\s+""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Replace(@"\\", @"\") : null;
        }

        static void Steam(GameSources s, List<InstalledGame> games)
        {
            var libraries = new List<string>();
            foreach (var root in s.SteamRoots.Where(Directory.Exists))
            {
                libraries.Add(root);
                string vdf = Path.Combine(root, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in VdfPath.Matches(File.ReadAllText(vdf)))
                        libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            foreach (var lib in libraries.Select(l => l.TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    string text;
                    try { text = File.ReadAllText(acf); } catch { continue; }
                    string id = VdfValue(text, "appid"), name = VdfValue(text, "name"), dir = VdfValue(text, "installdir");
                    // installdir é o nome de uma pasta dentro de steamapps\common: nada de caminho absoluto, barras ou "..".
                    if (id == "228980" || string.IsNullOrEmpty(dir) || dir.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || dir.Trim('.', ' ').Length == 0) continue; // 228980 = Steamworks Common Redistributables
                    string folder = Normalize(Path.Combine(apps, "common", dir));
                    if (folder == null || !Directory.Exists(folder) || games.Any(g => g.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))) continue;
                    games.Add(new InstalledGame { Name = name ?? dir, Store = "Steam", Folder = folder });
                }
            }
        }

        const int MaxGuessedExes = 30;

        /// <summary>
        /// Executáveis até três níveis abaixo da pasta do jogo, sem auxiliares, sem seguir junções e no máximo 30.
        /// Pasta de jogo que é ela mesma uma junção não entra no palpite: o destino pode ser qualquer lugar.
        /// </summary>
        static List<string> GuessExes(string folder)
        {
            var result = new List<string>();
            if (Util.IsReparsePoint(folder)) return result;
            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(folder, 0));
            while (queue.Count > 0 && result.Count < MaxGuessedExes)
            {
                var cur = queue.Dequeue();
                try
                {
                    foreach (var exe in Directory.EnumerateFiles(cur.Key, "*.exe"))
                    {
                        if (IsGameExe(exe)) result.Add(exe);
                        if (result.Count >= MaxGuessedExes) return result;
                    }
                    if (cur.Value >= 3) continue;
                    foreach (var sub in Directory.EnumerateDirectories(cur.Key))
                        if (!Util.IsReparsePoint(sub) && !HelperDir.IsMatch(Path.GetFileName(sub)))
                            queue.Enqueue(new KeyValuePair<string, int>(sub, cur.Value + 1));
                }
                catch { }
            }
            return result;
        }

        // ---- Xbox / PC Game Pass: XboxGames\<jogo>\Content ----

        /// <summary>
        /// O app Xbox instala os jogos do PC Game Pass em X:\XboxGames\&lt;jogo&gt;\Content, com executáveis comuns
        /// (a preferência de GPU por caminho vale para eles). Jogos empacotados antigos, em WindowsApps, ficam de fora.
        /// </summary>
        static void Xbox(GameSources s, List<InstalledGame> games)
        {
            foreach (var root in s.XboxGamesRoots.Where(Directory.Exists))
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    if (Util.IsReparsePoint(dir)) continue;
                    string content = Normalize(Path.Combine(dir, "Content"));
                    if (content == null || !Directory.Exists(content) || games.Any(g => g.Folder.Equals(content, StringComparison.OrdinalIgnoreCase))) continue;
                    games.Add(new InstalledGame { Name = Path.GetFileName(dir), Store = "Xbox", Folder = content });
                }
        }

        // ---- Epic Games: um .item (JSON) por jogo ----

        static string JsonValue(string text, string key)
        {
            var m = Regex.Match(text, @"""" + key + @"""\s*:\s*""((?:[^""\\]|\\.)*)""");
            if (!m.Success) return null;
            try { return Regex.Unescape(m.Groups[1].Value); }
            catch { return null; }
        }

        static void Epic(GameSources s, List<InstalledGame> games)
        {
            if (string.IsNullOrEmpty(s.EpicManifests) || !Directory.Exists(s.EpicManifests)) return;
            foreach (var item in Directory.EnumerateFiles(s.EpicManifests, "*.item"))
            {
                string text;
                try { text = File.ReadAllText(item); } catch { continue; }
                string name = JsonValue(text, "DisplayName"), folder = JsonValue(text, "InstallLocation"), launch = JsonValue(text, "LaunchExecutable");
                if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(launch) || launch.Contains("..")) continue;
                folder = Normalize(folder.Replace('/', '\\'));
                launch = launch.Replace('/', '\\').TrimStart('\\');
                if (folder == null || Path.IsPathRooted(launch)) continue; // LaunchExecutable é relativo à pasta do jogo
                string exe = Normalize(Path.Combine(folder, launch));
                // O executável tem de ficar dentro da pasta do jogo.
                if (exe == null || !IsUnder(exe, folder) || !IsGameExe(exe) || games.Any(g => g.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))) continue;
                var game = new InstalledGame { Name = name ?? Path.GetFileName(folder), Store = "Epic", Folder = folder };
                game.Exes.Add(exe);
                games.Add(game);
            }
        }
    }
}
