using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;

namespace OpenCleaner.Core
{
    // ======================= Programas (winget) =======================

    class PackageUpdate
    {
        public string Name, Id, Version, Available, Source;
        public bool Explicit; // pacote que o winget só atualiza quando pedido pelo ID
    }

    /// <summary>
    /// Atualizador de programas usando o Gerenciador de Pacotes do Windows (winget),
    /// que mantém o catálogo de versões e baixa instaladores verificados por hash.
    /// </summary>
    static class Winget
    {
        const string Common = " --accept-source-agreements --disable-interactivity";

        static int Run(string args, out string output, int timeoutMs, Action<string> onLine = null)
        {
            var psi = new ProcessStartInfo("winget", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            var sb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                DataReceivedEventHandler handler = (s, e) =>
                {
                    if (e.Data == null) return;
                    // Barras de progresso usam \r; interessa só o último estado da linha.
                    string line = e.Data.Split('\r').Last().Trim();
                    lock (sb) sb.AppendLine(line);
                    if (onLine != null && line.Length > 0 && line.Any(char.IsLetter)) onLine(line);
                };
                p.OutputDataReceived += handler;
                p.ErrorDataReceived += handler;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    output = sb.ToString();
                    return -1;
                }
                p.WaitForExit(); // garante que a saída assíncrona terminou
                output = sb.ToString();
                return p.ExitCode;
            }
        }

        public static bool IsAvailable(out string version)
        {
            version = null;
            try
            {
                string o;
                if (Run("--version", out o, 30000) != 0) return false;
                version = o.Trim();
                return true;
            }
            catch { return false; } // winget não instalado
        }

        public static List<PackageUpdate> ListUpgrades()
        {
            string output;
            int code = Run("upgrade" + Common, out output, 5 * 60 * 1000);
            if (code == -1) throw new TimeoutException(L.T("O winget demorou demais para responder.", "winget took too long to respond."));
            return Parse(output);
        }

        /// <summary>
        /// Lê as tabelas do "winget upgrade". As colunas são localizadas e a versão pode ter
        /// espaços ("5.9.1 (2581)"), então cada linha é fatiada pelas posições do cabeçalho.
        /// </summary>
        public static List<PackageUpdate> Parse(string output)
        {
            var list = new List<PackageUpdate>();
            var lines = output.Replace("\r\n", "\n").Split('\n').Select(l => l.Split('\r').Last().TrimEnd()).ToList();
            int table = 0;
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i].Length < 10 || lines[i].Any(c => c != '-')) continue;
                string header = lines[i - 1];
                var starts = new List<int>();
                for (int c = 0; c < header.Length; c++)
                    if (header[c] != ' ' && (c == 0 || header[c - 1] == ' ')) starts.Add(c);
                if (starts.Count < 4) continue;

                for (int j = i + 1; j < lines.Count; j++)
                {
                    string row = lines[j];
                    if (row.Trim().Length == 0 || row.Length < starts[2]) break;
                    Func<int, string> col = k =>
                    {
                        if (k >= starts.Count || starts[k] >= row.Length) return "";
                        int end = k + 1 < starts.Count ? Math.Min(starts[k + 1], row.Length) : row.Length;
                        return row.Substring(starts[k], end - starts[k]).Trim();
                    };
                    var pkg = new PackageUpdate
                    {
                        Name = col(0), Id = col(1), Version = col(2), Available = col(3),
                        Source = starts.Count > 4 ? col(4) : "", Explicit = table > 0
                    };
                    // Linhas de rodapé ("15 atualizações disponíveis.") não têm ID válido.
                    if (pkg.Id.Length == 0 || pkg.Id.Contains(' ') || pkg.Available.Length == 0) break;
                    if (!SafeId(pkg.Id)) continue; // um ID estranho nunca vai para a linha de comando
                    list.Add(pkg);
                }
                table++;
            }
            return list;
        }

        /// <summary>IDs do winget são do tipo Fornecedor.Programa: só letras, números e . _ - +.</summary>
        public static bool SafeId(string id)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(id ?? "", @"^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$");
        }

        /// <summary>Atualiza um pacote. Retorna true se o winget terminou com sucesso.</summary>
        public static bool Upgrade(PackageUpdate pkg, Action<string> progress, out string message)
        {
            if (!SafeId(pkg.Id)) { message = "invalid id"; return false; }
            string output;
            int code = Run("upgrade --id \"" + pkg.Id + "\" --exact --silent --accept-package-agreements" + Common,
                           out output, 30 * 60 * 1000, progress);
            var meaningful = output.Split('\n').Select(l => l.Trim()).Where(l => l.Any(char.IsLetter)).ToList();
            message = meaningful.Count > 0 ? meaningful.Last() : "";
            if (code == -1) message = L.T("Tempo esgotado", "Timed out");
            return code == 0;
        }
    }

    // ======================= Drivers (Windows Update) =======================

    class DriverUpdate
    {
        public string Title, Manufacturer, DriverClass, Model, Provider, UpdateId;
        public DateTime? Date;
        public long Size;
        internal object Com; // IUpdate do Windows Update Agent
    }

    class DriverInstallResult
    {
        public int Succeeded, Failed;
        public bool RebootRequired;
        public List<string> Errors = new List<string>();
    }

    /// <summary>
    /// Procura e instala drivers pelo Windows Update Agent (a mesma fonte das "Atualizações
    /// opcionais" do Windows): drivers certificados, publicados pelos fabricantes.
    /// Os objetos COM do WUA vivem numa única thread STA dedicada.
    /// </summary>
    class DriverUpdater : IDisposable
    {
        readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        readonly Thread thread;
        dynamic session;

        public DriverUpdater()
        {
            thread = new Thread(() => { foreach (var a in queue.GetConsumingEnumerable()) a(); });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        public void Dispose() { queue.CompleteAdding(); }

        /// <summary>Executa na thread do WUA; done recebe o resultado ou a exceção.</summary>
        public void Post<T>(Func<T> work, Action<T, Exception> done)
        {
            queue.Add(() =>
            {
                T result = default(T);
                Exception error = null;
                try { result = work(); }
                catch (Exception ex) { error = ex; }
                done(result, error);
            });
        }

        dynamic Session
        {
            get
            {
                if (session == null)
                {
                    session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session"));
                    session.ClientApplicationID = "OpenCleaner";
                }
                return session;
            }
        }

        public List<DriverUpdate> Search()
        {
            var list = new List<DriverUpdate>();
            dynamic searcher = Session.CreateUpdateSearcher();
            dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'");
            dynamic updates = result.Updates;
            int count = updates.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic u = updates[i]; // Item é o membro padrão da coleção COM
                var d = new DriverUpdate
                {
                    Title = Clean((string)u.Title),
                    Manufacturer = Clean(Str(() => u.DriverManufacturer)),
                    DriverClass = Str(() => u.DriverClass),
                    Model = Str(() => u.DriverModel),
                    Provider = Str(() => u.DriverProvider),
                    UpdateId = (string)u.Identity.UpdateID,
                    Com = u
                };
                try { d.Date = (DateTime)u.DriverVerDate; } catch { }
                try { d.Size = (long)(decimal)u.MaxDownloadSize; } catch { }
                list.Add(d);
            }
            return list;
        }

        // Alguns títulos do Windows Update vêm com espaços duplicados.
        static string Clean(string s) { return System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(); }

        static string Str(Func<object> get)
        {
            try { var v = get(); return v == null ? "" : v.ToString(); }
            catch { return ""; }
        }

        public DriverInstallResult Install(List<DriverUpdate> drivers, Action<string> progress)
        {
            var res = new DriverInstallResult();
            dynamic coll = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl"));
            foreach (var d in drivers)
            {
                dynamic u = d.Com;
                if (!(bool)u.EulaAccepted) u.AcceptEula();
                coll.Add(u);
            }

            progress(L.T("Baixando ", "Downloading ") + drivers.Count + L.T(" driver(s)...", " driver(s)..."));
            dynamic downloader = Session.CreateUpdateDownloader();
            downloader.Updates = coll;
            downloader.Download();

            progress(L.T("Instalando ", "Installing ") + drivers.Count + L.T(" driver(s)...", " driver(s)..."));
            dynamic installer = Session.CreateUpdateInstaller();
            installer.Updates = coll;
            dynamic result = installer.Install();
            res.RebootRequired = (bool)result.RebootRequired;
            for (int i = 0; i < drivers.Count; i++)
            {
                int code = (int)result.GetUpdateResult(i).ResultCode;
                if (code == 2 || code == 3) res.Succeeded++; // orcSucceeded / orcSucceededWithErrors
                else
                {
                    res.Failed++;
                    res.Errors.Add(drivers[i].Title);
                }
            }
            return res;
        }

        /// <summary>Cria um ponto de restauração antes de mexer em drivers (exige administrador).</summary>
        public static bool CreateRestorePoint(string description, out string error)
        {
            error = null;
            try
            {
                using (var sr = new ManagementClass(@"\\.\root\default", "SystemRestore", null))
                {
                    var args = sr.GetMethodParameters("CreateRestorePoint");
                    args["Description"] = description;
                    args["RestorePointType"] = 10; // DEVICE_DRIVER_INSTALL
                    args["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE
                    var outp = sr.InvokeMethod("CreateRestorePoint", args, null);
                    uint rv = Convert.ToUInt32(outp["ReturnValue"]);
                    if (rv == 0) return true;
                    error = rv == 1058 ? L.T("a Proteção do Sistema está desativada", "System Protection is turned off") : L.T("código ", "code ") + rv;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
