using ATEC.PM.Server.Services;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Guardie del ciclo RDO — la regola «una gara = un articolo».
///
/// <para>Sbaglia in silenzio e costa dati: aggiudicare una gara mista riscrive su ogni
/// riga l'identità dell'articolo del vincitore (codice, descrizione, articolo Danea,
/// codice ATEC) e nella distinta non resta traccia di cos'erano le altre righe. Questi
/// test tengono ferma la guardia: se un refactor la allentasse (per esempio tornando a
/// confrontare le righe con la testata, o lasciando passare le gare di sole righe non
/// mappate), qui diventa rosso e il deploy si ferma.</para>
/// </summary>
public class RdoGuardieTests
{
    // ── Normalizzazione: la guardia confronta codici che arrivano formattati ──

    [Theory]
    [InlineData("2010.5121.8001", "201051218001")]  // formattato coi punti (FormatCodice)
    [InlineData("  201051218001 ", "201051218001")] // spazi di contorno
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Normalizza_TogliePuntiESpazi(string? grezzo, string atteso)
    {
        Assert.Equal(atteso, RdoGuardie.Normalizza(grezzo));
    }

    /// <summary>
    /// LoadDetail formatta i codici coi punti per mostrarli a video: lo stesso codice,
    /// puntato e non, deve contare come UNO — altrimenti ogni gara sana verrebbe
    /// rifiutata come mista.
    /// </summary>
    [Fact]
    public void CodiciInGara_StessoCodicePuntatoENo_ContaUno()
    {
        var codici = RdoGuardie.CodiciInGara(new[] { "2010.5121.8001", "201051218001" });
        Assert.Single(codici);
    }

    // ── Gara mista: codici diversi → rifiuto ──

    [Fact]
    public void GaraMista_CodiciDiversi_Rifiuta()
    {
        var codici = RdoGuardie.CodiciInGara(new[] { "201051218001", "301101018006" });
        string? errore = RdoGuardie.GaraMista(codici, numeroRighe: 2);
        Assert.NotNull(errore);
        Assert.Contains("articoli diversi", errore);
    }

    [Fact]
    public void GaraMista_RigaCodificataERigaSenzaCodice_Rifiuta()
    {
        var codici = RdoGuardie.CodiciInGara(new[] { "201051218001", "" });
        Assert.NotNull(RdoGuardie.GaraMista(codici, numeroRighe: 2));
    }

    // ── Più righe tutte senza codice: mista per definizione ──

    /// <summary>
    /// Due righe non mappate sono articoli che nessuno può giurare siano lo stesso
    /// (RequestOffers infatti le mette una per RDO): aggiudicarle in blocco le
    /// riscriverebbe tutte con l'identità del vincitore — il difetto originale,
    /// sopravvissuto in questa forma alla prima stesura della guardia.
    /// </summary>
    [Fact]
    public void GaraMista_PiuRigheTutteSenzaCodice_Rifiuta()
    {
        var codici = RdoGuardie.CodiciInGara(new[] { "", "", "" });
        string? errore = RdoGuardie.GaraMista(codici, numeroRighe: 3);
        Assert.NotNull(errore);
        Assert.Contains("senza Cod. ATEC", errore);
    }

    /// <summary>Una riga sola senza codice resta aggiudicabile: non c'è nessun'altra riga da confondere.</summary>
    [Fact]
    public void GaraMista_UnaSolaRigaSenzaCodice_Passa()
    {
        var codici = RdoGuardie.CodiciInGara(new[] { "" });
        Assert.Null(RdoGuardie.GaraMista(codici, numeroRighe: 1));
    }

    [Fact]
    public void GaraMista_RigheConLoStessoCodice_Passa()
    {
        var codici = RdoGuardie.CodiciInGara(new[] { "201051218001", "2010.5121.8001" });
        Assert.Null(RdoGuardie.GaraMista(codici, numeroRighe: 2));
    }

    // ── Prezzo aggiudicabile ──

    /// <summary>
    /// Il prezzo dell'offerta vincente finisce dritto in <c>bom_items.unit_cost</c>, nel
    /// Bilancio e nell'ordine Danea: senza prezzo non si aggiudica (il vecchio ripiego sul
    /// costo di riga inventava prezzi che nessuno aveva offerto), e a zero o in negativo
    /// nemmeno — nessun controllo lo fermava e l'ordine partiva a 0 €.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.5)]
    public void PrezzoNonAggiudicabile_AssenteZeroONegativo_Rifiuta(double? prezzo)
    {
        decimal? valore = prezzo.HasValue ? (decimal)prezzo.Value : null;
        Assert.NotNull(RdoGuardie.PrezzoNonAggiudicabile(valore));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(12.50)]
    public void PrezzoNonAggiudicabile_PrezzoVero_Passa(double prezzo)
    {
        Assert.Null(RdoGuardie.PrezzoNonAggiudicabile((decimal)prezzo));
    }

    // ── Creazione: righe fuori dal codice della gara ──

    [Fact]
    public void RigheFuoriCodice_TutteAllineate_NessunaEsclusa()
    {
        var fuori = RdoGuardie.RigheFuoriCodice("201051218001", new[]
        {
            ("Pressostato", (string?)"2010.5121.8001"),
            ("Pressostato di scorta", (string?)"201051218001"),
        });
        Assert.Empty(fuori);
    }

    /// <summary>
    /// Un bundle web vecchio manda ancora tutte le righe sotto il codice della prima:
    /// il server deve nominare le intruse e rifiutare, non lasciar nascere la gara
    /// mista (che a valle sarebbe comunque inaggiudicabile, ma con le righe occupate
    /// e le email ai fornitori già sbagliate).
    /// </summary>
    [Fact]
    public void RigheFuoriCodice_CodiceDiversoOVuoto_Nominate()
    {
        var fuori = RdoGuardie.RigheFuoriCodice("201051218001", new[]
        {
            ("Pressostato", (string?)"201051218001"),
            ("Guarnizione", (string?)"301101018006"),
            ("Riga non mappata", (string?)""),
        });
        Assert.Equal(new[] { "Guarnizione", "Riga non mappata" }, fuori);
    }

    /// <summary>Senza descrizione si mostra il codice: un elenco di righe anonime non aiuta nessuno.</summary>
    [Fact]
    public void RigheFuoriCodice_SenzaDescrizione_MostraIlCodice()
    {
        var fuori = RdoGuardie.RigheFuoriCodice("201051218001", new[]
        {
            ("", (string?)"3011.0101.8006"),
        });
        Assert.Equal(new[] { "301101018006" }, fuori);
    }
}
