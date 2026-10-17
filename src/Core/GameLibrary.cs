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
            @"^(unins.*|.*crashhandler.*|.*crashreport.*|.*redist.*|vc_?redist.*|dxsetup|dxwebsetup|directx.*|easyanticheat.*|beservice.*|battleye.*|" +
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

            // Steam sem nada visto pelo Windows: palpite pelos .exe da pasta.
            foreach (var g in games.Where(g => g.Store == "Steam" && g.Exes.Count == 0))
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

        static bool IsGameExe(string exe)
        {
            return !string.IsNullOrEmpty(exe) && Path.IsPathRooted(exe) && !exe.StartsWith(@"\\") &&
                   exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                   exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) < 0 &&
                   !Helper.IsMatch(Path.GetFileName(exe)) && File.Exists(exe);
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
                        string exe = c == null ? null : Util.ReadString(c, "MatchedExeFullPath");
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
                    if (id == "228980" || string.IsNullOrEmpty(dir) || dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || dir.Contains("..")) continue; // 228980 = Steamworks Common Redistributables
                    string folder = Path.Combine(apps, "common", dir);
                    if (!Directory.Exists(folder) || games.Any(g => g.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))) continue;
                    games.Add(new InstalledGame { Name = name ?? dir, Store = "Steam", Folder = folder });
                }
            }
        }

        /// <summary>Executáveis até três níveis abaixo da pasta do jogo, sem auxiliares, sem seguir junções.</summary>
        static List<string> GuessExes(string folder)
        {
            var result = new List<string>();
            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(folder, 0));
            while (queue.Count > 0 && result.Count < 30)
            {
                var cur = queue.Dequeue();
                try
                {
                    foreach (var exe in Directory.EnumerateFiles(cur.Key, "*.exe"))
                        if (IsGameExe(exe)) result.Add(exe);
                    if (cur.Value >= 3) continue;
                    foreach (var sub in Directory.EnumerateDirectories(cur.Key))
                        if (!Util.IsReparsePoint(sub) && !HelperDir.IsMatch(Path.GetFileName(sub)))
                            queue.Enqueue(new KeyValuePair<string, int>(sub, cur.Value + 1));
                }
                catch { }
            }
            return result;
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
                if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(launch) || !Path.IsPathRooted(folder) || launch.Contains("..")) continue;
                folder = folder.Replace('/', '\\').TrimEnd('\\');
                string exe = Path.Combine(folder, launch.Replace('/', '\\'));
                if (!IsGameExe(exe) || games.Any(g => g.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))) continue;
                var game = new InstalledGame { Name = name ?? Path.GetFileName(folder), Store = "Epic", Folder = folder };
                game.Exes.Add(exe);
                games.Add(game);
            }
        }
    }
}
