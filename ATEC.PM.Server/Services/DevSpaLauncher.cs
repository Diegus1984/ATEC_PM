using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ATEC.PM.Server.Services;

/// <summary>
/// In Debug (ambiente Development) avvia il dev server Vite di <c>atec-pm-web</c> quando parte
/// il server: così premendo Avvia (F5) in Visual Studio partono insieme API e client web, senza
/// SpaProxy. Apre il browser su http://localhost:5173 quando Vite è pronto; se Vite è già in
/// ascolto lo riusa (utile sui riavvii del debug). In Release non viene chiamato: la SPA è
/// servita da <c>wwwroot</c> sullo stesso host dell'API.
/// </summary>
public static class DevSpaLauncher
{
    private const int VitePort = 5173;
    private static readonly string ViteUrl = $"http://localhost:{VitePort}";

    public static void Launch(ILogger logger, string contentRootPath)
    {
        try
        {
            // Il server è in ATEC.PM.Server; il client web è la cartella sorella atec-pm-web.
            string webDir = Path.GetFullPath(Path.Combine(contentRootPath, "..", "atec-pm-web"));

            if (IsPortListening(VitePort))
            {
                logger.LogInformation("[DevSpa] Vite già in ascolto su {Port}: lo riuso.", VitePort);
                _ = Task.Run(() => OpenBrowser(logger));
                return;
            }

            if (!OperatingSystem.IsWindows())
            {
                logger.LogWarning("[DevSpa] Avvio automatico Vite supportato solo su Windows: lancia `npm run dev` a mano.");
                return;
            }
            if (!Directory.Exists(webDir) || !File.Exists(Path.Combine(webDir, "package.json")))
            {
                logger.LogWarning("[DevSpa] Cartella client web non trovata in {Dir}.", webDir);
                return;
            }

            // Lancio diretto (no ShellExecute) così funziona anche fuori da una sessione desktop;
            // i log di Vite ereditano la console del server. Node viene aggiunto al PATH del figlio
            // (su questa macchina spesso non è nel PATH di sistema).
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c npm run dev",
                WorkingDirectory = webDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            EnsureNodeOnPath(psi, logger);
            Process.Start(psi);
            logger.LogInformation("[DevSpa] Avvio Vite (npm run dev) in {Dir}", webDir);

            _ = Task.Run(() => WaitAndOpenBrowser(logger));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[DevSpa] Impossibile avviare il dev server Vite. Avvialo a mano con `npm run dev` in atec-pm-web.");
        }
    }

    private static void EnsureNodeOnPath(ProcessStartInfo psi, ILogger logger)
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "node"),
        };
        string nodeDir = candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "npm.cmd"))) ?? "";
        if (nodeDir.Length == 0)
        {
            return; // npm probabilmente già nel PATH; se no, Vite non parte e lo segnaliamo dopo.
        }
        string path = psi.Environment.TryGetValue("PATH", out string? p) ? (p ?? "") : "";
        if (!path.Contains(nodeDir, StringComparison.OrdinalIgnoreCase))
        {
            psi.Environment["PATH"] = nodeDir + ";" + path;
            logger.LogInformation("[DevSpa] Node aggiunto al PATH del processo figlio: {Dir}", nodeDir);
        }
    }

    private static void WaitAndOpenBrowser(ILogger logger)
    {
        // Attende che Vite accetti connessioni (max ~60s), poi apre il browser.
        for (int i = 0; i < 120; i++)
        {
            if (CanConnect(VitePort))
            {
                OpenBrowser(logger);
                return;
            }
            Thread.Sleep(500);
        }
        logger.LogWarning("[DevSpa] Vite non disponibile entro il timeout: apri {Url} a mano.", ViteUrl);
    }

    private static void OpenBrowser(ILogger logger)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ViteUrl) { UseShellExecute = true });
            logger.LogInformation("[DevSpa] Browser aperto su {Url}", ViteUrl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DevSpa] Impossibile aprire il browser su {Url}", ViteUrl);
        }
    }

    private static bool IsPortListening(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(ep => ep.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private static bool CanConnect(int port)
    {
        try
        {
            using var client = new TcpClient();
            IAsyncResult result = client.BeginConnect("localhost", port, null, null);
            bool ok = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(300));
            if (ok)
            {
                client.EndConnect(result);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
