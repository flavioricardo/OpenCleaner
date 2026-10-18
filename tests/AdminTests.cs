// Testes que exigem administrador: a troca real do tipo de inicialização de um serviço pelo Gerenciador de
// Serviços (o mesmo código que a página Jogos usa nos serviços dos navegadores e do Windows Update).
// Compilado por tests\run-admin-tests.ps1, que cria e remove o serviço de teste OpenCleanerTestSvc.
// O serviço aponta para um executável que não existe e nunca é iniciado; nenhum serviço real é tocado.
using System;
using System.Linq;
using System.Management;
using System.Text;
using Microsoft.Win32;
using OpenCleaner.Core;

static class AdminTests
{
    const string Name = "OpenCleanerTestSvc";
    static int failures, passes;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  [ok]    " : "  [FALHA] ") + what);
        if (ok) passes++; else failures++;
    }

    static object Value(string name)
    {
        using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + Name))
            return k == null ? null : k.GetValue(name);
    }

    /// <summary>StartMode ("Auto", "Manual", "Disabled") e State ("Stopped"...) vistos pelo WMI, não pelo registro.</summary>
    static string Wmi(string property)
    {
        using (var s = new ManagementObjectSearcher("SELECT StartMode, State FROM Win32_Service WHERE Name='" + Name + "'"))
            foreach (ManagementObject o in s.Get())
                using (o) return Convert.ToString(o[property]);
        return null;
    }

    static int Main()
    {
        Hardening.Apply();
        Console.OutputEncoding = Encoding.UTF8;
        L.Init(L.Pt);
        Console.WriteLine("== Gerenciador de Serviços: troca real do tipo de inicialização (serviço de teste) ==");
        if (!Util.IsAdmin()) { Console.WriteLine("  precisa de administrador"); return 1; }

        var set = GameContext.System().SetServiceStart;
        Check(Equals(Value("Start"), 4) && Wmi("StartMode") == "Disabled", "serviço de teste existe e está desativado");

        Check(set(Name, 2, true), "SetServiceStart(automático atrasado) retorna sucesso");
        Check(Equals(Value("Start"), 2) && Equals(Value("DelayedAutostart"), 1) && Wmi("StartMode") == "Auto",
              "o Gerenciador de Serviços registrou automático atrasado (Start=2, DelayedAutostart=1)");

        Check(set(Name, 2, false) && Equals(Value("Start"), 2) && Equals(Value("DelayedAutostart"), 0), "automático sem atraso (DelayedAutostart=0)");

        Check(set(Name, 3, false) && Equals(Value("Start"), 3) && Wmi("StartMode") == "Manual", "manual (Start=3)");

        Check(Wmi("State") == "Stopped", "a troca não inicia o serviço");

        Check(!set(Name, 4, false) && Equals(Value("Start"), 3), "Start=4 (desativado) é recusado: o app só religa serviços");
        Check(!set(Name + " & calc", 2, false) && !set(Name + "\"", 2, false), "nomes com espaço, & ou aspas são recusados antes do Gerenciador de Serviços");
        Check(!set("OpenCleanerTestSvcNaoExiste", 2, false), "serviço inexistente retorna falha, sem exceção");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "TUDO OK (" + passes + " verificações)" : failures + " FALHA(S) de " + (passes + failures));
        return failures;
    }
}
