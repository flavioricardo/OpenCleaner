using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace OpenCleaner.Core
{
    class VideoAdapter
    {
        public string Name, PnpId;
    }

    /// <summary>
    /// Preferência de placa de vídeo por programa, a mesma de Configurações > Sistema > Tela > Elementos gráficos.
    /// Fica em HKCU (só o usuário atual, sem administrador) e vale na próxima vez que o jogo abrir.
    /// </summary>
    static class GpuPreference
    {
        public const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
        public const int WindowsDecides = 0, PowerSaving = 1, HighPerformance = 2;

        /// <summary>Placas de vídeo presentes agora (o WMI não lista as de drivers antigos que ficaram no registro).</summary>
        public static List<VideoAdapter> Adapters()
        {
            var list = new List<VideoAdapter>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController"))
                    foreach (ManagementObject o in s.Get())
                        using (o)
                            list.Add(new VideoAdapter { Name = Convert.ToString(o["Name"]), PnpId = Convert.ToString(o["PNPDeviceID"]) });
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Notebook com duas placas (integrada + dedicada): é onde um jogo pode acabar na placa mais fraca.
        /// Placas virtuais e o driver genérico da Microsoft não contam. Em PC de mesa (sem bateria) o monitor
        /// costuma estar na placa dedicada e a escolha não se aplica.
        /// </summary>
        public static bool IsHybridLaptop(IEnumerable<VideoAdapter> adapters, bool hasBattery)
        {
            if (!hasBattery) return false;
            int real = adapters.Count(a => (a.PnpId ?? "").StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) &&
                                           (a.Name ?? "").IndexOf("Microsoft Basic", StringComparison.OrdinalIgnoreCase) < 0);
            return real >= 2;
        }

        static int Parse(string value)
        {
            foreach (var part in (value ?? "").Split(';'))
            {
                string p = part.Trim();
                int v;
                if (p.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase) && int.TryParse(p.Substring(14), out v)) return v;
            }
            return WindowsDecides;
        }

        /// <summary>
        /// Troca só o item GpuPreference do valor e preserva os outros (ex.: SwapEffectUpgradeEnable=1;).
        /// Retorna null quando não sobra nada (o valor deve ser apagado).
        /// </summary>
        public static string Merge(string current, int pref)
        {
            var parts = (current ?? "").Split(';').Select(p => p.Trim())
                                       .Where(p => p.Length > 0 && !p.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase)).ToList();
            if (pref != WindowsDecides) parts.Insert(0, "GpuPreference=" + pref);
            return parts.Count == 0 ? null : string.Join(";", parts) + ";";
        }

        public static int Get(RegistryKey hkcu, string exe)
        {
            try
            {
                using (var k = hkcu.OpenSubKey(Key))
                    return k == null ? WindowsDecides : Parse(Util.ReadString(k, exe));
            }
            catch { return WindowsDecides; }
        }

        public static bool Set(RegistryKey hkcu, string exe, int pref)
        {
            try
            {
                using (var k = hkcu.CreateSubKey(Key))
                {
                    string merged = Merge(Util.ReadString(k, exe), pref);
                    if (merged == null) k.DeleteValue(exe, false);
                    else k.SetValue(exe, merged, RegistryValueKind.String);
                }
                return true;
            }
            catch { return false; }
        }
    }
}
