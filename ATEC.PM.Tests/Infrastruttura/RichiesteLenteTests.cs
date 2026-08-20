using ATEC.PM.Server.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// Blocco E1 — la misura delle richieste lente. Un cronometro che sbaglia è peggio di nessun
/// cronometro: manda a correggere l'endpoint sbagliato, o fa credere che vada tutto bene.
/// </summary>
public class RichiesteLenteTests
{
    /// <summary>Raccoglie le righe di log così come uscirebbero nel file del server.</summary>
    private sealed class LoggerDiProva : ILogger<RichiesteLenteMiddleware>
    {
        public List<string> Righe { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Righe.Add(formatter(state, exception));
    }

    private static IConfiguration Config(int sogliaMs, string[]? esclusi = null)
    {
        var valori = new Dictionary<string, string?> { ["Diagnostics:SlowRequestMs"] = sogliaMs.ToString() };
        foreach ((string p, int i) in (esclusi ?? RichiesteLenteMiddleware.EsclusiDiDefault).Select((p, i) => (p, i)))
            valori[$"Diagnostics:EscludiPercorsi:{i}"] = p;
        return new ConfigurationBuilder().AddInMemoryCollection(valori).Build();
    }

    private static DefaultHttpContext Richiesta(string percorso, string? queryString = null, string? rotta = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = percorso;
        if (queryString != null) context.Request.QueryString = new QueryString(queryString);
        if (rotta != null)
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask, RoutePatternFactory.Parse(rotta), 0, null, rotta));
        return context;
    }

    private static async Task<List<string>> Passa(
        HttpContext context, RequestDelegate prossimo, int sogliaMs, string[]? esclusi = null)
    {
        var logger = new LoggerDiProva();
        var middleware = new RichiesteLenteMiddleware(prossimo, logger, Config(sogliaMs, esclusi));
        await middleware.InvokeAsync(context);
        return logger.Righe;
    }

    /// <summary>Il caso per cui esiste: una richiesta oltre soglia lascia una traccia con la durata.</summary>
    [Fact]
    public async Task RichiestaOltreSoglia_lasciaUnaRigaConDurataERotta()
    {
        List<string> righe = await Passa(
            Richiesta("/api/projects/847/costing", rotta: "api/projects/{id}/costing"),
            async _ => await Task.Delay(40),
            sogliaMs: 1);

        string riga = Assert.Single(righe);
        Assert.Contains("[Lenta]", riga);
        Assert.Contains("ms", riga);
        // Il TEMPLATE della rotta, non solo il path: con l'id dentro, ogni commessa sarebbe una
        // riga diversa e non si potrebbe contare quante volte quell'endpoint è lento.
        Assert.Contains("api/projects/{id}/costing", riga);
    }

    /// <summary>
    /// Il rumore è il modo in cui una misura muore: se ogni richiesta finisce nel log, in una
    /// settimana nessuno lo apre più.
    /// </summary>
    [Fact]
    public async Task RichiestaVeloce_nonLasciaNiente()
    {
        List<string> righe = await Passa(
            Richiesta("/api/projects"), _ => Task.CompletedTask, sogliaMs: 5000);

        Assert.Empty(righe);
    }

    /// <summary>
    /// La richiesta più lenta è spesso quella che esplode (timeout del database), ed è proprio
    /// quella che si vuole vedere: misurare dopo la <c>await</c> invece che in <c>finally</c> la
    /// perderebbe in silenzio.
    /// </summary>
    [Fact]
    public async Task RichiestaCheFallisce_vieneMisurataLoStesso()
    {
        var logger = new LoggerDiProva();
        var middleware = new RichiesteLenteMiddleware(
            async _ => { await Task.Delay(40); throw new InvalidOperationException("timeout"); },
            logger, Config(1));

        // L'eccezione deve comunque risalire: la gestisce ExceptionHandlingMiddleware, che sta sopra.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(Richiesta("/api/projects")));

        Assert.Contains(logger.Righe, r => r.Contains("[Lenta]"));
    }

    /// <summary>
    /// Gli hub SignalR sono WebSocket: durano quanto la sessione dell'utente. Senza questa
    /// esclusione ogni connessione comparirebbe come una richiesta da ore, e le richieste vere
    /// sarebbero introvabili in mezzo.
    /// </summary>
    [Fact]
    public async Task ConnessioneAUnHub_nonVieneMisurata()
    {
        List<string> righe = await Passa(
            Richiesta("/hubs/project"), async _ => await Task.Delay(40), sogliaMs: 1);

        Assert.Empty(righe);
    }

    /// <summary>
    /// Il prefisso vale per segmenti interi: <c>/hubs</c> non deve zittire <c>/hubsomething</c>,
    /// o un endpoint vero sparirebbe dalla misura per via del nome.
    /// </summary>
    [Fact]
    public void IlPrefissoEscluso_valeSoloSuSegmentiInteri()
    {
        string[] esclusi = RichiesteLenteMiddleware.EsclusiDiDefault;

        Assert.True(RichiesteLenteMiddleware.DaIgnorare("/hubs/project", esclusi));
        Assert.True(RichiesteLenteMiddleware.DaIgnorare("/UPLOADS/cms/foto.jpg", esclusi));
        Assert.False(RichiesteLenteMiddleware.DaIgnorare("/hubsomething", esclusi));
        Assert.False(RichiesteLenteMiddleware.DaIgnorare("/api/projects", esclusi));
    }

    /// <summary>
    /// ⚠️ La riga di sicurezza di tutto il blocco: gli hub passano il JWT in query string
    /// (<c>?access_token=…</c>) e questi log restano sul server 30 giorni. Un token scritto lì è
    /// una sessione regalata a chiunque apra il file — e non se ne accorgerebbe nessuno.
    /// </summary>
    [Fact]
    public async Task LaQueryString_nonFinisceMaiNelLog()
    {
        List<string> righe = await Passa(
            Richiesta("/api/projects", queryString: "?access_token=eyJhbGciOiJIUzI1NiJ9.SEGRETO"),
            async _ => await Task.Delay(40),
            sogliaMs: 1);

        string riga = Assert.Single(righe);
        Assert.DoesNotContain("access_token", riga);
        Assert.DoesNotContain("SEGRETO", riga);
    }

    /// <summary>
    /// Il guardiano fra i due pezzi della misura: il middleware <b>scrive</b> e
    /// <c>deploy/misura-prestazioni.ps1</c> <b>legge</b>. Cambiare il testo del messaggio — anche
    /// solo il separatore — non romperebbe niente di visibile: lo script continuerebbe a girare e
    /// a rispondere «nessuna richiesta oltre soglia», cioè la frase più rassicurante che possa
    /// dire, e falsa. Qui il modello viene preso <b>dallo script vero</b> e applicato a una riga
    /// vera: se i due si separano, questo test diventa rosso.
    /// </summary>
    [Fact]
    public async Task IlFormatoDelLog_restaQuelloCheLoScriptSaLeggere()
    {
        string script = Path.Combine(CartellaDeploy(), "misura-prestazioni.ps1");
        Assert.True(File.Exists(script), $"Script della misura non trovato: {script}");

        var estratto = System.Text.RegularExpressions.Regex.Match(
            File.ReadAllText(script), @"\$modello\s*=\s*'([^']*)'");
        Assert.True(estratto.Success, "Nello script non si trova più la riga «$modello = '...'».");

        List<string> righe = await Passa(
            Richiesta("/api/projects/847/costing", rotta: "api/projects/{id}/costing"),
            async _ => await Task.Delay(40),
            sogliaMs: 1);

        var match = System.Text.RegularExpressions.Regex.Match(Assert.Single(righe), estratto.Groups[1].Value);
        Assert.True(match.Success, "Lo script non riconosce più la riga scritta dal middleware.");
        Assert.Equal("GET", match.Groups["met"].Value);
        Assert.Equal("api/projects/{id}/costing", match.Groups["rotta"].Value);
        Assert.Equal("200", match.Groups["st"].Value);
        Assert.True(int.Parse(match.Groups["ms"].Value) >= 40);
    }

    private static string CartellaDeploy()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidato = Path.Combine(dir.FullName, "deploy");
            if (Directory.Exists(candidato)) return candidato;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cartella deploy non trovata risalendo da " + AppContext.BaseDirectory);
    }

    /// <summary>Si spegne da configurazione, senza ripubblicare il server.</summary>
    [Fact]
    public async Task SogliaAZero_spegneLaMisura()
    {
        var middleware = new RichiesteLenteMiddleware(_ => Task.CompletedTask,
            new LoggerDiProva(), Config(0));
        Assert.False(middleware.Attivo);

        List<string> righe = await Passa(
            Richiesta("/api/projects"), async _ => await Task.Delay(40), sogliaMs: 0);

        Assert.Empty(righe);
    }
}
