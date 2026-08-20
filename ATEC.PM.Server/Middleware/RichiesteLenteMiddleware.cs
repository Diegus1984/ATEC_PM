using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Routing;

namespace ATEC.PM.Server.Middleware;

/// <summary>
/// Blocco E1 del piano — <b>misurare prima di correggere</b>. Cronometra ogni richiesta e scrive
/// una riga di log solo per quelle che superano la soglia (500 ms di default), con la rotta e la
/// durata. È metà della misura: l'altra metà è lo slow query log di MySQL
/// (<c>deploy/slow-query-log.ps1</c>). Insieme dicono se una pagina è lenta per colpa del
/// database (query pesante o N+1) o per il resto.
///
/// <para><b>Perché non basta lo slow query log.</b> Una richiesta che fa 300 query da 5 ms non
/// compare in nessuno slow query log — eppure impiega un secondo e mezzo. È esattamente la forma
/// degli N+1 di E3 (166 punti nel progetto): si vede solo cronometrando la richiesta intera.</para>
///
/// <para><b>Cosa non finisce nel log</b>, e non è pigrizia:
/// <list type="bullet">
///   <item>la <b>query string</b>: gli hub SignalR ci passano il JWT (<c>?access_token=…</c>), e
///   questi log restano sul server 30 giorni. Un token nel log è una sessione regalata a chiunque
///   apra il file;</item>
///   <item>gli <b>hub</b> (<c>/hubs</c>): sono WebSocket, durano quanto la sessione dell'utente.
///   Comparirebbero tutti come «lentissimi» seppellendo le richieste vere;</item>
///   <item>gli <b>asset statici</b> (<c>/assets</c>, <c>/uploads</c>): al primo caricamento del
///   mattino sono decine di file, e il mattino è proprio quando si sta indagando.</item>
/// </list></para>
///
/// <para>Livello <c>Information</c> e non <c>Warning</c>: una richiesta lenta non è un guasto, e
/// mescolarla ai warning veri (migrazioni, login falliti, CORS aperto) renderebbe quelli meno
/// visibili. Il prefisso <c>[Lenta]</c> serve a filtrarle:
/// <c>Select-String "\[Lenta\]" C:\ATEC_PM\Logs\server-*.log</c>.</para>
/// </summary>
public class RichiesteLenteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RichiesteLenteMiddleware> _log;
    private readonly int _sogliaMs;
    private readonly string[] _esclusi;

    /// <summary>Prefissi ignorati se <c>Diagnostics:EscludiPercorsi</c> non dice altro.</summary>
    public static readonly string[] EsclusiDiDefault = { "/hubs", "/assets", "/uploads" };

    public RichiesteLenteMiddleware(
        RequestDelegate next, ILogger<RichiesteLenteMiddleware> log, IConfiguration config)
    {
        _next = next;
        _log = log;
        _sogliaMs = config.GetValue("Diagnostics:SlowRequestMs", 500);
        _esclusi = config.GetSection("Diagnostics:EscludiPercorsi").Get<string[]>() ?? EsclusiDiDefault;
    }

    /// <summary>La misura è spenta con soglia a 0 o negativa: si toglie da configurazione, senza ripubblicare.</summary>
    public bool Attivo => _sogliaMs > 0;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!Attivo || DaIgnorare(context.Request.Path, _esclusi))
        {
            await _next(context);
            return;
        }

        // GetTimestamp invece di uno Stopwatch: nessuna allocazione per richiesta. Su un endpoint
        // veloce il costo della misura non deve essere paragonabile a quello che misura.
        long inizio = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            // finally e non dopo la await: una richiesta che finisce in eccezione è di solito la
            // più lenta di tutte (timeout del database), ed è quella che si vuole vedere.
            double ms = Stopwatch.GetElapsedTime(inizio).TotalMilliseconds;
            if (ms >= _sogliaMs)
                Registra(context, ms);
        }
    }

    /// <summary>
    /// Il confronto è sul segmento intero: <c>/hubs</c> non deve escludere <c>/hubsomething</c>,
    /// e senza <see cref="StringComparison.OrdinalIgnoreCase"/> un <c>/Uploads</c> maiuscolo
    /// passerebbe (i path di Windows arrivano come li scrive il client).
    /// </summary>
    public static bool DaIgnorare(PathString percorso, string[] esclusi) =>
        esclusi.Any(p => percorso.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private void Registra(HttpContext context, double ms)
    {
        // Il template della rotta (/api/projects/{id}/costing) e non solo il path concreto
        // (/api/projects/847/costing): senza, ogni commessa è una riga diversa e non si può
        // contare quante volte quell'endpoint è lento. Il path resta accanto per riprodurre il caso.
        string? template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        string rotta = string.IsNullOrWhiteSpace(template) ? context.Request.Path.ToString() : template;

        // L'id della persona, non il nome: serve a capire se «va lento» è di uno o di tutti,
        // senza scrivere nel log chi stava facendo cosa.
        string persona = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "-";

        _log.LogInformation("[Lenta] {Durata} ms · {Metodo} {Rotta} (status {Status}, persona {Persona}, path {Path})",
            (long)ms, context.Request.Method, rotta, context.Response.StatusCode, persona,
            context.Request.Path.ToString());
    }
}
