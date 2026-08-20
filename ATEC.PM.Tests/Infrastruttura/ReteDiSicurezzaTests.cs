using System.Net;
using System.Text.Json;
using ATEC.PM.Server.Middleware;
using ATEC.PM.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// Blocco B del piano — la rete di sicurezza: quando qualcosa va storto, il server deve dirlo
/// invece di fingere che vada bene.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class ReteDiSicurezzaTests
{

    private readonly SchemaCondiviso _schema;

    /// <summary>
    /// xUnit costruisce una istanza per ogni test: qui si riporta il database condiviso a
    /// com'era appena creato (~45 ms), invece di costruirne uno nuovo (~5 s).
    /// </summary>
    public ReteDiSicurezzaTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }
    // ── B1: il middleware degli errori ────────────────────────────────────────

    private sealed class AmbienteFinto : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "ATEC.PM.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static async Task<(int Status, string ContentType, string Corpo)> PassaDalMiddleware(
        RequestDelegate prossimo, string ambiente = "Production")
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";
        context.Request.Path = "/api/qualcosa";

        var middleware = new ExceptionHandlingMiddleware(
            prossimo,
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            new AmbienteFinto { EnvironmentName = ambiente });

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        string corpo = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, context.Response.ContentType ?? "", corpo);
    }

    /// <summary>
    /// Il caso per cui il middleware esiste: senza, questa eccezione tornerebbe al client la
    /// pagina HTML di errore di ASP.NET, dove il client web si aspetta { success, data, message }.
    /// </summary>
    [Fact]
    public async Task EccezioneNonGestita_diventaUnApiResponseInJson()
    {
        (int status, string contentType, string corpo) = await PassaDalMiddleware(
            _ => throw new InvalidOperationException("qualcosa è andato storto"));

        Assert.Equal((int)HttpStatusCode.InternalServerError, status);
        Assert.Contains("application/json", contentType);

        using JsonDocument doc = JsonDocument.Parse(corpo);
        // camelCase: con le maiuscole il client leggerebbe `success` come undefined e
        // scambierebbe l'errore per una riuscita.
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("message").GetString()));
    }

    /// <summary>
    /// Si legge il campo `message` DECODIFICATO, non il testo grezzo: `JsonSerializer` scrive
    /// l'apostrofo come `'`, quindi cercare «l'assistenza» nel corpo fallirebbe pur essendo
    /// giusto quello che il client riceve dopo il parse. (Costato il primo rosso di questo test.)
    /// </summary>
    private static string MessaggioDi(string corpo)
    {
        using JsonDocument doc = JsonDocument.Parse(corpo);
        return doc.RootElement.GetProperty("message").GetString() ?? "";
    }

    /// <summary>In produzione il messaggio interno non esce: resta l'id per cercare nel log.</summary>
    [Fact]
    public async Task InProduzione_ilMessaggioInternoNonEsce()
    {
        (_, _, string corpo) = await PassaDalMiddleware(
            _ => throw new InvalidOperationException("SELECT segreto FROM tabella_interna"));

        string messaggio = MessaggioDi(corpo);
        Assert.DoesNotContain("tabella_interna", messaggio);
        Assert.Contains("Riferimento per l'assistenza", messaggio);
    }

    /// <summary>In sviluppo invece serve vedere tutto, altrimenti si debugga alla cieca.</summary>
    [Fact]
    public async Task InSviluppo_ilMessaggioCompareTuttoIntero()
    {
        (_, _, string corpo) = await PassaDalMiddleware(
            _ => throw new InvalidOperationException("dettaglio-che-serve-a-chi-sviluppa"),
            "Development");

        Assert.Contains("dettaglio-che-serve-a-chi-sviluppa", MessaggioDi(corpo));
    }

    /// <summary>Una richiesta che va a buon fine non deve essere toccata dal middleware.</summary>
    [Fact]
    public async Task RichiestaRiuscita_passaIntatta()
    {
        (int status, _, string corpo) = await PassaDalMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("{\"success\":true}");
        });

        Assert.Equal(200, status);
        Assert.Contains("\"success\":true", corpo);
    }

    // ── B3: la risposta di validazione ────────────────────────────────────────

    /// <summary>
    /// Senza questa traduzione ASP.NET risponderebbe con un `ProblemDetails`, che il client web
    /// non sa leggere: cerca `success` e `message`, non trova né l'uno né l'altro, e l'utente
    /// vede un errore generico al posto del campo da correggere.
    /// </summary>
    [Fact]
    public void ValidazioneFallita_rispondeNelContrattoApiResponse()
    {
        var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        modelState.AddModelError("Importo", "Il valore deve essere maggiore o uguale a 0.");

        var risultato = Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(
            RispostaValidazione.Crea(modelState));
        var corpo = Assert.IsType<ATEC.PM.Shared.DTOs.ApiResponse<string>>(risultato.Value);

        Assert.False(corpo.Success);
        Assert.Contains("Importo", corpo.Message);
        Assert.Contains("maggiore o uguale a 0", corpo.Message);
    }

    /// <summary>Più campi sbagliati insieme: si elencano tutti, non solo il primo.</summary>
    [Fact]
    public void PiuCampiNonValidi_compaionoTuttiNelMessaggio()
    {
        var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        modelState.AddModelError("Descrizione", "Campo obbligatorio.");
        modelState.AddModelError("Quantita", "Deve essere positiva.");

        string messaggio = RispostaValidazione.Messaggio(modelState);

        Assert.Contains("Descrizione", messaggio);
        Assert.Contains("Quantita", messaggio);
    }

    /// <summary>
    /// Il binder di ASP.NET nomina i campi del corpo JSON come «$.importo»: quel «$.» non
    /// significa niente per chi legge il messaggio e va tolto.
    /// </summary>
    [Fact]
    public void IlNomeDelCampo_nonMostraLaSintassiInternaDelBinder()
    {
        var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        modelState.AddModelError("$.importo", "Valore non convertibile.");

        string messaggio = RispostaValidazione.Messaggio(modelState);

        Assert.DoesNotContain("$.", messaggio);
        Assert.Contains("importo", messaggio);
    }

    /// <summary>
    /// Le annotazioni servono a qualcosa solo se scattano davvero. Qui si prova il caso che oggi
    /// arriverebbe fino a MySQL e tornerebbe come «Data too long for column» — cioè, per chi sta
    /// salvando, un «Errore interno del server» che non dice cosa correggere.
    /// </summary>
    [Fact]
    public void RepartoConValoriFuoriMisura_vieneFermatoPrimaDelDatabase()
    {
        var richiesta = new ATEC.PM.Shared.DTOs.DepartmentSaveRequest
        {
            Code = new string('X', 30),      // la colonna è VARCHAR(10)
            Name = "Reparto",
            HourlyCost = -5m,                // un costo orario negativo non esiste
            DefaultMarkup = 1.450m,
        };

        var errori = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        bool valido = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            richiesta,
            new System.ComponentModel.DataAnnotations.ValidationContext(richiesta),
            errori,
            validateAllProperties: true);

        Assert.False(valido);
        string testo = string.Join(" · ", errori.Select(e => e.ErrorMessage));
        Assert.Contains("10 caratteri", testo);
        Assert.Contains("costo orario", testo);
        // I messaggi devono essere in italiano: le annotazioni di default rispondono in inglese,
        // e su un gestionale tutto italiano sarebbe un peggioramento visibile all'utente.
        Assert.DoesNotContain("field", testo, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Un reparto normale deve continuare a passare: le annotazioni non stringono l'uso vero.</summary>
    [Fact]
    public void RepartoNormale_passaSenzaErrori()
    {
        var richiesta = new ATEC.PM.Shared.DTOs.DepartmentSaveRequest
        {
            Code = "UTE", Name = "Ufficio Tecnico", HourlyCost = 45m, DefaultMarkup = 1.450m,
        };

        bool valido = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            richiesta,
            new System.ComponentModel.DataAnnotations.ValidationContext(richiesta),
            new List<System.ComponentModel.DataAnnotations.ValidationResult>(),
            validateAllProperties: true);

        Assert.True(valido);
    }

    // ── B2: la sonda del database ─────────────────────────────────────────────

    private static DbService ServizioCon(string connectionString)
    {
        IConfiguration cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString }).Build();
        return new DbService(cfg, NullLogger<DbService>.Instance);
    }

    [FactRichiedeMySql]
    public async Task DatabaseRaggiungibile_laSondaTace()
    {

        string? errore = await ServizioCon(_schema.ConnectionString).ProvaDatabaseAsync();

        Assert.Null(errore);
    }

    /// <summary>
    /// Il caso che conta: <c>/api/health</c> risponde «ok» anche con MySQL spento, e fino al
    /// 14/08/2026 gli script di aggiornamento si fidavano di quello — un deploy che rompeva la
    /// connessione al database veniva dichiarato riuscito e il rollback non scattava.
    /// Qui si punta a una porta dove non c'è nessuno: la sonda deve accorgersene.
    /// </summary>
    [Fact]
    public async Task DatabaseIrraggiungibile_laSondaLoDice()
    {
        string? errore = await ServizioCon(
            "Server=127.0.0.1;Port=59999;Database=inesistente;User=nessuno;Password=x;" +
            "ConnectionTimeout=3;").ProvaDatabaseAsync();

        Assert.False(string.IsNullOrWhiteSpace(errore));
    }
}
