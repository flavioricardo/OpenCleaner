using System;
using System.Runtime.InteropServices;

namespace OpenCleaner.Core
{
    /// <summary>
    /// Proteção contra DLL plantada: um instalador ou app aberto da pasta Downloads não deve carregar
    /// uma dnsapi.dll (ou outra) colocada ao lado dele. Restringe a busca de DLLs ao System32.
    /// </summary>
    static class Hardening
    {
        const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetDefaultDllDirectories(uint flags);

        public static void Apply()
        {
            try { SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32); }
            catch { /* Windows sem a KB2533623: ainda vale o atributo DefaultDllImportSearchPaths */ }
        }
    }
}
