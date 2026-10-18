using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using OpenCleaner.Core;

[assembly: AssemblyTitle("OpenCleaner")]
[assembly: AssemblyDescription("Limpador de sistema gratuito e de código aberto")]
[assembly: AssemblyProduct("OpenCleaner")]
[assembly: AssemblyCopyright("OpenCleaner")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]
// P/Invoke só procura DLLs do sistema no System32 (não na pasta do executável).
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]

namespace OpenCleaner
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Hardening.Apply();
            if (args.Any(a => a.Equals("/AUTO", StringComparison.OrdinalIgnoreCase)))
                return AutoClean();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UI.MainForm());
            return 0;
        }

        /// <summary>
        /// Modo silencioso (equivalente ao /AUTO do CCleaner): limpa os itens marcados,
        /// pula programas abertos e registra o resultado em auto.log.
        /// </summary>
        static int AutoClean()
        {
            var log = new StringBuilder();
            log.AppendLine("== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ==");
            try
            {
                var settings = AppSettings.Load();
                L.Init(settings.Language);
                var engine = new CleanerEngine(settings);
                long freed = 0;
                foreach (var rule in RuleCatalog.LoadAll(settings).Where(r => settings.IsRuleOn(r) && r.IsDetected()))
                {
                    if (rule.Processes.Count > 0 && Util.FindProcesses(rule.Processes).Count > 0)
                    {
                        log.AppendLine(L.T("Pulado (programa aberto): ", "Skipped (program open): ") + rule.FullName);
                        continue;
                    }
                    var r = engine.Analyze(rule);
                    if (r.ItemCount == 0) continue;
                    engine.Clean(r);
                    freed += r.Freed;
                    log.AppendLine(rule.FullName + ": " + Util.FormatSize(r.Freed) + (r.Failed > 0 ? " (" + r.Failed + L.T(" em uso)", " in use)") : ""));
                }
                log.AppendLine(L.T("Total liberado: ", "Total freed: ") + Util.FormatSize(freed));
                return 0;
            }
            catch (Exception ex)
            {
                log.AppendLine(L.T("Erro: ", "Error: ") + ex);
                return 1;
            }
            finally
            {
                try
                {
                    Directory.CreateDirectory(AppSettings.DataDir);
                    File.AppendAllText(Path.Combine(AppSettings.DataDir, "auto.log"), log.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }
    }
}
