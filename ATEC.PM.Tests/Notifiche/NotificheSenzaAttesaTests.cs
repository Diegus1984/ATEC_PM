using System.Text.RegularExpressions;
using ATEC.PM.Server.Hubs;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Tests.Notifiche;

/// <summary>
/// Blocco F3 del piano tecnico: nessuna notifica SignalR parte più come <c>_ = x.SendAsync(…)</c>
/// nudo. Erano 41: un push fallito spariva, e la pagina di qualcuno restava vecchia senza che
/// nessuno lo sapesse. Ora passano da <see cref="HubNotifica.SenzaAttesa"/>, che scrive nel
/// log il fallimento.
/// </summary>
public class NotificheSenzaAttesaTests
{
    [Fact]
    public void Nessun_SendAsync_nudo_nei_sorgenti_del_server()
    {
        string server = CartellaServer();
        var nudi = new List<string>();
        int sorvegliati = 0;
        var nudo = new Regex(@"_ = [^\r\n]*\.SendAsync\(", RegexOptions.Compiled);

        foreach (string file in Directory.EnumerateFiles(server, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            string testo = File.ReadAllText(file);
            foreach (Match m in nudo.Matches(testo))
                nudi.Add($"{Path.GetRelativePath(server, file)}: {m.Value}");
            sorvegliati += Regex.Matches(testo, @"\.SenzaAttesa\(""").Count;
        }

        Assert.True(nudi.Count == 0,
            "Notifiche SignalR fire-and-forget senza log (usa .SenzaAttesa(\"Evento\")):\n - " + string.Join("\n - ", nudi));
        // Paracadute: se il conteggio crolla, la regex ha smesso di vedere i sorgenti.
        Assert.True(sorvegliati >= 40, $"solo {sorvegliati} notifiche sorvegliate: la scansione dei sorgenti funziona ancora?");
    }

    [Fact]
    public async Task Un_push_fallito_finisce_nel_log_e_non_esplode()
    {
        var log = new LoggerFinto();
        ILogger prima = HubNotifica.Log;
        HubNotifica.Log = log;
        try
        {
            Task.FromException(new InvalidOperationException("hub spento")).SenzaAttesa("ProvaChanged");
            // La continuazione gira in sincrono sul task già fallito: un giro di scheduler basta.
            await Task.Delay(50);
            Assert.Contains(log.Righe, r => r.Contains("ProvaChanged") && r.Contains("hub spento"));

            // Un push riuscito non scrive niente.
            Task.CompletedTask.SenzaAttesa("ProvaOk");
            await Task.Delay(20);
            Assert.DoesNotContain(log.Righe, r => r.Contains("ProvaOk"));
        }
        finally
        {
            HubNotifica.Log = prima;
        }
    }

    private sealed class LoggerFinto : ILogger
    {
        public List<string> Righe { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Righe.Add($"{logLevel}: {formatter(state, exception)} {exception?.Message}");
    }

    private static string CartellaServer()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidato = Path.Combine(dir.FullName, "ATEC.PM.Server");
            if (Directory.Exists(candidato)) return candidato;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cartella ATEC.PM.Server non trovata risalendo da " + AppContext.BaseDirectory);
    }
}
