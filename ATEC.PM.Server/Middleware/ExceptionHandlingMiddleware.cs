using System.Text.Json;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Middleware;

/// <summary>
/// La rete sotto tutti gli endpoint: qualunque eccezione non gestita diventa una risposta JSON
/// nel contratto <see cref="ApiResponse{T}"/>, con una riga di log che permette di ritrovarla.
///
/// <para><b>Perché serve.</b> Quasi ogni action ha il suo <c>try/catch</c> (379 nel progetto), ma
/// quel che conta è cosa succede <b>fuori</b> da lì: senza questo middleware un'eccezione non
/// incapsulata torna al browser la <b>pagina HTML di errore</b> di ASP.NET. Il client web la
/// riceve dove si aspetta un <c>ApiResponse</c>, non trova <c>success</c>, e il difetto si
/// presenta come una schermata vuota invece che come un errore — la stessa forma di guasto per cui
/// esiste il fallback <c>/api/{**path}</c> in <c>Program.cs</c>.</para>
///
/// <para><b>Non sostituisce i catch esistenti</b> e non è un refactoring: quelli si tolgono man
/// mano che si passa sui controller. Questo è il fondo che c'è comunque.</para>
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _log;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log, IHostEnvironment env)
    {
        _next = next;
        _log = log;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await GestisciAsync(context, ex);
        }
    }

    private async Task GestisciAsync(HttpContext context, Exception ex)
    {
        // TraceIdentifier è l'id che ASP.NET assegna alla richiesta: finisce nel log e nella
        // risposta, così l'utente può leggere un codice e chi guarda i log trova esattamente
        // quella richiesta invece di cercare «l'errore di stamattina verso le dieci».
        string id = context.TraceIdentifier;

        _log.LogError(ex, "[Errore non gestito] {Metodo} {Percorso} — id {Id}: {Messaggio}",
            context.Request.Method, context.Request.Path, id, ex.Message);

        // Se la risposta è già partita non si può più cambiarne il formato: riscriverci sopra
        // produrrebbe un corpo mezzo JSON e mezzo altro, illeggibile da entrambe le parti.
        if (context.Response.HasStarted)
        {
            _log.LogWarning("[Errore non gestito] Risposta già iniziata (id {Id}): non modificabile.", id);
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";

        // In sviluppo serve lo stack per capire; in produzione no — un messaggio interno può
        // raccontare la struttura del database a chi non deve conoscerla. Resta l'id, che è
        // sufficiente per ritrovare tutto nel log del server.
        string messaggio = _env.IsDevelopment()
            ? $"{ex.Message}\n\n{ex.StackTrace}"
            : $"Errore interno del server. Riferimento per l'assistenza: {id}";

        // CamelCase come tutte le altre risposte (impostato in Program.cs): con le maiuscole il
        // client leggerebbe `success` come undefined e tratterebbe l'errore come una riuscita.
        string corpo = JsonSerializer.Serialize(
            ApiResponse<string>.Fail(messaggio),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        await context.Response.WriteAsync(corpo);
    }
}
