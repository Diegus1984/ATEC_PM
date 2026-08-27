using System.Net;
using ATEC.PM.Server.Services.Hr;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Il client EcosAgile senza rete: parsing delle risposte (la forma è quella osservata sul
/// campo, PIANO-HR-PRESENZE.md §4-§5) e paginazione con un handler HTTP finto.
///
/// <para>Le forme insidiose dell'API, tutte coperte: <c>ECOSAGILE_DATA_ROW</c> che è un
/// array O un oggetto singolo, <c>ECOSAGILE_DATA</c> che con zero righe è una stringa
/// vuota, gli errori dentro il JSON con status HTTP 200, le risposte HTML.</para>
/// </summary>
public class EcosClientTests
{
    // ── PARSING TOKEN ─────────────────────────────────────────────────────────

    [Fact]
    public void Estrae_il_token_dalla_risposta_di_TokenGet()
    {
        const string json = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "MESSAGE": "" },
                "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": { "AuthToken": "abc-123" } } } }
            """;
        Assert.Equal("abc-123", EcosClient.EstraiToken(json));
    }

    [Fact]
    public void Token_assente_da_null_non_esplode()
    {
        const string json = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "FAIL", "MESSAGE": "Invalid login" },
                "ECOSAGILE_DATA": "" } }
            """;
        Assert.Null(EcosClient.EstraiToken(json));
    }

    [Fact]
    public void Risposta_html_solleva_EcosApiException_con_messaggio_leggibile()
    {
        var ex = Assert.Throws<EcosApiException>(
            () => EcosClient.EstraiToken("<html><body>Maintenance</body></html>"));
        Assert.Contains("non JSON", ex.Message);
    }

    // ── PARSING PAGINE ────────────────────────────────────────────────────────

    private static readonly string[] Campi = { "StampID", "StampDateTime", "EmplCode" };

    [Fact]
    public void Pagina_con_array_di_righe_e_LASTPAGE_false()
    {
        const string json = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "LASTPAGE": "FALSE" },
                "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": [
                    { "StampID": 1, "StampDateTime": "2026-02-05 07:58:12", "EmplCode": "42" },
                    { "StampID": 2, "StampDateTime": "2026-02-05 17:04:00", "EmplCode": "42" } ] } } }
            """;
        (List<Dictionary<string, string>> righe, bool? ultima) =
            EcosClient.EstraiPagina(json, Campi, "PeopleStampGetAll");

        Assert.Equal(2, righe.Count);
        Assert.False(ultima);
        // I numeri JSON diventano stringhe: a valle si parsa una volta sola.
        Assert.Equal("1", righe[0]["StampID"]);
        Assert.Equal("2026-02-05 07:58:12", righe[0]["StampDateTime"]);
    }

    [Fact]
    public void Riga_singola_come_oggetto_invece_che_array()
    {
        const string json = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "LASTPAGE": "TRUE" },
                "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW":
                    { "StampID": 7, "StampDateTime": "2026-02-05 08:00:00", "EmplCode": "9" } } } }
            """;
        (List<Dictionary<string, string>> righe, bool? ultima) =
            EcosClient.EstraiPagina(json, Campi, "PeopleStampGetAll");

        Assert.Single(righe);
        Assert.True(ultima);
        Assert.Equal("7", righe[0]["StampID"]);
    }

    [Fact]
    public void Nessun_dato_ECOSAGILE_DATA_stringa_vuota()
    {
        const string json = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "LASTPAGE": "TRUE" },
                "ECOSAGILE_DATA": "" } }
            """;
        (List<Dictionary<string, string>> righe, bool? ultima) =
            EcosClient.EstraiPagina(json, Campi, "PeopleStampGetAll");

        Assert.Empty(righe);
        Assert.True(ultima);
    }

    /// <summary>
    /// 🪤 LASTPAGE assente = «non dichiarato», non «ultima pagina»: decide il chiamante
    /// contando le righe. Col default a true una risposta senza quel campo troncava lo
    /// scarico alla prima pagina e l'import si dichiarava riuscito.
    /// </summary>
    [Fact]
    public void LASTPAGE_assente_non_significa_ultima_pagina()
    {
        const string json = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK" },
                "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": [
                    { "StampID": 1, "StampDateTime": "2026-02-05 07:58:12", "EmplCode": "42" } ] } } }
            """;
        (_, bool? ultima) = EcosClient.EstraiPagina(json, Campi, "PeopleStampGetAll");
        Assert.Null(ultima);
    }

    [Fact]
    public void Valori_JSON_non_stringa_letti_come_testo()
    {
        const string json = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "LASTPAGE": "TRUE" },
                "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW":
                    { "StampID": 7, "StampDateTime": "2026-02-05 08:00:00", "EmplCode": 42 } } } }
            """;
        (List<Dictionary<string, string>> righe, _) =
            EcosClient.EstraiPagina(json, Campi, "PeopleStampGetAll");

        // EmplCode arriva come numero JSON: deve diventare "42", non stringa vuota
        // (altrimenti la timbratura non si abbina più a nessuno).
        Assert.Equal("42", righe[0]["EmplCode"]);
    }

    [Fact]
    public void Errore_API_solleva_eccezione_invece_di_dati_parziali()
    {
        const string json = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "FAIL",
                    "MESSAGE": "User doesn't have the Service/Right to execute the API" } } }
            """;
        var ex = Assert.Throws<EcosApiException>(
            () => EcosClient.EstraiPagina(json, Campi, "PeopleAbsenceRequestPost"));
        Assert.Contains("Service/Right", ex.Message);
    }

    // ── DATE ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-02-05 07:58:12", true)]
    [InlineData("2026-02-05T07:58:12", true)]
    [InlineData("2026-02-05", true)]
    [InlineData("", false)]
    [InlineData("non-una-data", false)]
    // 🪤 Una data all'italiana NON si indovina: «05/02/2026» col parser invariante
    // diventava il 2 maggio, e la timbratura finiva nel giorno sbagliato in silenzio.
    // Meglio scartarla e loggare.
    [InlineData("05/02/2026 07:58:12", false)]
    public void Riconosce_il_formato_data_osservato_sul_campo(string valore, bool atteso)
    {
        Assert.Equal(atteso, EcosClient.ProvaData(valore, out _));
    }

    // ── PAGINAZIONE VIA HTTP FINTO ────────────────────────────────────────────

    [Fact]
    public async Task Scarica_tutte_le_pagine_e_scarta_le_righe_senza_orario()
    {
        const string pagina1 = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "LASTPAGE": "FALSE" },
                "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": [
                    { "StampID": 1, "StampDateTime": "2026-02-05 07:58:12", "EmplID": 5,
                      "EmplCode": "42", "NameComplete": "Rossi, Mario", "VersusCode": "IN",
                      "StampLocationName": "Sede", "YearMonth": "202602" },
                    { "StampID": 2, "StampDateTime": "", "EmplCode": "42",
                      "NameComplete": "Rossi, Mario", "VersusCode": "OUT" } ] } } }
            """;
        const string pagina2 = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "LASTPAGE": "TRUE" },
                "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW":
                    { "StampID": 3, "StampDateTime": "2026-02-05 17:04:00", "EmplID": 5,
                      "EmplCode": "42", "NameComplete": "Rossi, Mario", "VersusCode": "OUT",
                      "StampLocationName": "", "YearMonth": "202602" } } } }
            """;
        var handler = new RisposteInSequenza(pagina1, pagina2);
        EcosClient client = CreaClient(handler);

        List<EcosTimbratura> timbrature = await client.TimbratureAsync("tok", updateDa: null);

        // La riga senza orario si scarta (loggata), non si inventa.
        Assert.Equal(2, timbrature.Count);
        Assert.Equal("1", timbrature[0].IdEsterno);
        Assert.Equal(new DateTime(2026, 2, 5, 7, 58, 12), timbrature[0].Orario);
        Assert.Equal("IN", timbrature[0].Verso);
        Assert.Equal("Sede", timbrature[0].Luogo);
        Assert.Equal("3", timbrature[1].IdEsterno);

        Assert.Equal(2, handler.UrlChiamati.Count);
        Assert.Contains("PageNumber=1", handler.UrlChiamati[0]);
        Assert.Contains("PageNumber=2", handler.UrlChiamati[1]);
        Assert.Contains("AuthToken=tok", handler.UrlChiamati[0]);
    }

    [Fact]
    public async Task Badge_InForce_diventa_booleano()
    {
        const string risposta = """
            { "ECOSAGILE_TABLE_DATA": {
                "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "LASTPAGE": "TRUE" },
                "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": [
                    { "EmplID": 5, "EmplCode": "42", "NameComplete": "Rossi, Mario", "InForce": "TRUE" },
                    { "EmplID": 6, "EmplCode": "43", "NameComplete": "Verdi, Anna", "InForce": "FALSE" },
                    { "EmplID": 7, "EmplCode": "", "NameComplete": "Senza codice", "InForce": "TRUE" } ] } } }
            """;
        EcosClient client = CreaClient(new RisposteInSequenza(risposta));

        List<EcosBadge> badges = await client.BadgesAsync("tok");

        // Il badge senza EmplCode non serve a niente: la mappatura aggancia i codici.
        Assert.Equal(2, badges.Count);
        Assert.True(badges[0].InForza);
        Assert.False(badges[1].InForza);
    }

    [Fact]
    public async Task Senza_credenziali_TokenAsync_rifiuta_subito()
    {
        var config = new ConfigurationBuilder().Build();
        var client = new EcosClient(config, NullLogger<EcosClient>.Instance,
            new HttpClient(new RisposteInSequenza()));

        Assert.False(client.Configurato);
        await Assert.ThrowsAsync<EcosApiException>(() => client.TokenAsync());
    }

    // ── ATTREZZI ──────────────────────────────────────────────────────────────

    private static EcosClient CreaClient(HttpMessageHandler handler)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ecos:UserId"] = "utente",
                ["Ecos:Password"] = "segreta",
                ["Ecos:ClientId"] = "atec",
            })
            .Build();
        return new EcosClient(config, NullLogger<EcosClient>.Instance, new HttpClient(handler));
    }

    /// <summary>Handler HTTP che risponde i corpi in coda, uno per chiamata, e registra gli URL.</summary>
    private sealed class RisposteInSequenza : HttpMessageHandler
    {
        private readonly Queue<string> _corpi;
        public List<string> UrlChiamati { get; } = new();

        public RisposteInSequenza(params string[] corpi) => _corpi = new Queue<string>(corpi);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            UrlChiamati.Add(request.RequestUri!.ToString());
            string corpo = _corpi.Count > 0 ? _corpi.Dequeue() : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(corpo),
            });
        }
    }
}
