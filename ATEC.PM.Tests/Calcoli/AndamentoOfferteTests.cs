using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;

using Riga = ATEC.PM.Server.Services.AndamentoOfferteCalcolo.Riga;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// I conti dell'Andamento (<see cref="AndamentoOfferteCalcolo"/>).
///
/// <para>È la pagina che il venditore mette davanti alla proprietà: se un numero è sbagliato
/// non se ne accorge nessuno finché qualcuno non prende una decisione su quel numero. Le
/// regole ricalcano <c>APP.stats</c> del vecchio applicativo, esclusioni comprese, e sono
/// queste esclusioni — bozze fuori, righe senza importo fuori — la parte che si dimentica.</para>
/// </summary>
public class AndamentoOfferteTests
{
    // ── Le esclusioni ────────────────────────────────────────────────────────────

    /// <summary>
    /// Una bozza è un numero riservato, non un'offerta emessa: entra nel conteggio delle righe
    /// ma non nelle statistiche. Contarla gonfierebbe le emesse e abbasserebbe la conversione
    /// senza che nessuno capisca perché.
    /// </summary>
    [Fact]
    public void LeBozze_nonSonoOfferteEmesse()
    {
        AndamentoOfferteDto d = Calcola(
            Offerta(1, stato: "aperta", importo: 1000),
            Offerta(2, stato: "bozza", importo: null));

        Assert.Equal(2, d.Kpi.RigheTotali);
        Assert.Equal(1, d.Kpi.Bozze);
        Assert.Equal(1, d.Kpi.Emesse);
        Assert.Equal(1000, d.Kpi.ValoreEmesse);
    }

    /// <summary>
    /// Le righe senza importo restano fuori dai valori — ma si <b>dichiarano</b>: un totale che
    /// non torna col numero di righe, senza spiegazione, è il modo più veloce per far perdere
    /// fiducia in una pagina.
    /// </summary>
    [Fact]
    public void LeRigheSenzaImporto_restanoFuoriMaSiContano()
    {
        AndamentoOfferteDto d = Calcola(
            Offerta(1, stato: "aperta", importo: 1000),
            Offerta(2, stato: "aperta", importo: null),
            Offerta(3, stato: "aperta", importo: 0));

        Assert.Equal(1, d.Kpi.Emesse);
        Assert.Equal(2, d.Kpi.SenzaImporto);
        Assert.Equal(1000, d.Kpi.ValoreEmesse);
    }

    [Fact]
    public void UnAltroAnno_nonEntraNeiConti()
    {
        AndamentoOfferteDto d = AndamentoOfferteCalcolo.Calcola(new List<Riga>
        {
            Offerta(1, stato: "aperta", importo: 1000),
            Offerta(2, stato: "aperta", importo: 5000, anno: 2025),
        }, 2026);

        Assert.Equal(1, d.Kpi.Emesse);
        Assert.Equal(1000, d.Kpi.ValoreEmesse);
    }

    // ── Le prese, che sono tre casi ──────────────────────────────────────────────

    /// <summary>
    /// «Presa» si spacca in tre: a corpo (con l'importo d'ordine), a consuntivo (senza, per
    /// definizione) e <b>incompleta</b> — né l'uno né l'altro. La terza non è un caso teorico:
    /// sullo storico importato sono 41 righe, ed è un debito dei dati che la pagina deve
    /// mostrare invece di sommare alla cieca.
    /// </summary>
    [Fact]
    public void LePrese_siDividonoInCorpoConsuntivoEIncomplete()
    {
        AndamentoOfferteDto d = Calcola(
            Offerta(1, stato: "presa", importo: 1000, importoOrdine: 950),
            Offerta(2, stato: "presa", importo: 2000, aConsuntivo: true),
            Offerta(3, stato: "presa", importo: 3000));

        Assert.Equal(3, d.Kpi.Prese);
        Assert.Equal(1, d.Kpi.PreseACorpo);
        Assert.Equal(1, d.Kpi.PreseAConsuntivo);
        Assert.Equal(1, d.Kpi.PreseIncomplete);
        // Il valore preso è quello dell'OFFERTA; gli ordini si sommano a parte.
        Assert.Equal(6000, d.Kpi.ValorePrese);
        Assert.Equal(950, d.Kpi.ValoreOrdini);
    }

    // ── Conversione ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(3, 1, 75.0)]
    [InlineData(1, 1, 50.0)]
    [InlineData(0, 4, 0.0)]
    public void LaConversione_ePreseSuChiuse(int prese, int perse, double atteso)
    {
        Assert.Equal((decimal)atteso, AndamentoOfferteCalcolo.Conversione(prese, perse));
    }

    /// <summary>
    /// Senza niente di chiuso la conversione è <b>ignota</b>, non zero: uno zero direbbe «non
    /// ne prendiamo nessuna», che è un'altra cosa e a fine anno pesa.
    /// </summary>
    [Fact]
    public void SenzaChiuse_laConversioneNonEsiste()
    {
        Assert.Null(AndamentoOfferteCalcolo.Conversione(0, 0));

        AndamentoOfferteDto d = Calcola(Offerta(1, stato: "aperta", importo: 1000));
        Assert.Null(d.Kpi.ConversionePct);
    }

    /// <summary>Le aperte non entrano nel denominatore: una trattativa in corso non è una sconfitta.</summary>
    [Fact]
    public void LeAperte_nonAbbassanoLaConversione()
    {
        AndamentoOfferteDto d = Calcola(
            Offerta(1, stato: "presa", importo: 1000, importoOrdine: 1000),
            Offerta(2, stato: "persa", importo: 1000),
            Offerta(3, stato: "aperta", importo: 9000),
            Offerta(4, stato: "aperta", importo: 9000));

        Assert.Equal(50m, d.Kpi.ConversionePct);
    }

    // ── Portafoglio ponderato ────────────────────────────────────────────────────

    /// <summary>
    /// Il ponderato pesa ogni aperta per la sua chance. Le aperte <b>senza</b> chance non
    /// valgono zero: restano fuori, e quante ne restano fuori si dice — altrimenti il totale
    /// sembra un portafoglio povero quando è solo un portafoglio non stimato.
    /// </summary>
    [Fact]
    public void IlPonderato_pesaSoloLeAperteConChance()
    {
        AndamentoOfferteDto d = Calcola(
            Offerta(1, stato: "aperta", importo: 10_000, chance: 70),
            Offerta(2, stato: "aperta", importo: 10_000, chance: 20),
            Offerta(3, stato: "aperta", importo: 50_000),          // non valutata
            Offerta(4, stato: "presa", importo: 90_000, chance: 100, importoOrdine: 90_000));

        Assert.Equal(9000m, d.Kpi.PortafoglioPonderato); // 7.000 + 2.000
        Assert.Equal(2, d.Kpi.ApertConChance);
        Assert.Equal(3, d.Kpi.Aperte);
        Assert.Equal(70_000, d.Kpi.ValoreAperte);
    }

    [Fact]
    public void LaChanceZero_eUnValoreVeroNonUnVuoto()
    {
        AndamentoOfferteDto d = Calcola(
            Offerta(1, stato: "aperta", importo: 10_000, chance: 0),
            Offerta(2, stato: "aperta", importo: 10_000));

        // Zero pesa zero, ma è una stima fatta: conta fra quelle valutate.
        Assert.Equal(0m, d.Kpi.PortafoglioPonderato);
        Assert.Equal(1, d.Kpi.ApertConChance);

        AndamentoChanceDto? nonValutata = d.PerChance.FirstOrDefault(x => x.Livello < 0);
        AndamentoChanceDto? zero = d.PerChance.FirstOrDefault(x => x.Livello == 0);
        Assert.NotNull(nonValutata);
        Assert.NotNull(zero);
        Assert.Equal("Non valutata", nonValutata!.Etichetta);
        Assert.Equal("0%", zero!.Etichetta);
    }

    // ── Forbice di prezzo ────────────────────────────────────────────────────────

    [Fact]
    public void LaForbice_siSommaSoloDoveCe()
    {
        AndamentoOfferteDto d = Calcola(
            Offerta(1, stato: "aperta", importo: 1000, importoMax: 1500),
            Offerta(2, stato: "aperta", importo: 2000));

        Assert.Equal(3000, d.Kpi.ValoreEmesse);
        Assert.Equal(3500, d.Kpi.ValoreEmesseMax); // 1500 + 2000
        Assert.True(d.Kpi.HaForbice);
    }

    [Fact]
    public void SenzaNessunaForbice_ilFinoANonSiMostra()
    {
        AndamentoOfferteDto d = Calcola(Offerta(1, stato: "aperta", importo: 1000, importoMax: 1000));
        Assert.False(d.Kpi.HaForbice);
    }

    // ── Serie e raggruppamenti ───────────────────────────────────────────────────

    [Fact]
    public void IlMensile_haSempreDodiciMesi()
    {
        AndamentoOfferteDto d = Calcola(
            Offerta(1, stato: "presa", importo: 1000, importoOrdine: 1000, data: new DateTime(2026, 3, 10)),
            Offerta(2, stato: "aperta", importo: 500, data: new DateTime(2026, 3, 20)));

        Assert.Equal(12, d.Mensile.Count);
        AndamentoMeseDto marzo = d.Mensile[2];
        Assert.Equal("Mar", marzo.Etichetta);
        Assert.Equal(1500, marzo.Emesse);
        Assert.Equal(1000, marzo.Prese);
        Assert.Equal(500, marzo.Aperte);
        Assert.Equal(0, d.Mensile[0].Emesse);
    }

    /// <summary>
    /// Gli anni <b>in mezzo</b> restano anche se vuoti: saltarne uno farebbe sembrare
    /// consecutivi due anni che non lo sono.
    /// </summary>
    [Fact]
    public void IlConfrontoPluriennale_tieneGliAnniVuotiInMezzo()
    {
        AndamentoOfferteDto d = AndamentoOfferteCalcolo.Calcola(new List<Riga>
        {
            Offerta(1, stato: "aperta", importo: 1000, data: new DateTime(2026, 5, 1)),
            Offerta(2, stato: "aperta", importo: 700, anno: 2022, data: new DateTime(2022, 5, 1)),
        }, 2026);

        Assert.Equal(5, d.MensileMultiAnno.Count);
        Assert.Equal(new[] { 2022, 2023, 2024, 2025, 2026 }, d.MensileMultiAnno.Select(s => s.Anno));
        Assert.All(d.MensileMultiAnno, s => Assert.Equal(12, s.Mesi.Count));
        Assert.Equal(700, d.MensileMultiAnno.Single(s => s.Anno == 2022).Mesi[4]);
    }

    /// <summary>
    /// Gli anni <b>prima</b> dell'inizio dello storico invece si tolgono: sui dati veri il
    /// registro comincia nel 2022, e guardando il 2025 il grafico disegnava una linea piatta
    /// a zero per il 2021 — non è un dato, è rumore che schiaccia la scala di tutti gli altri.
    /// </summary>
    [Fact]
    public void IlConfrontoPluriennale_scartaGliAnniPrimaDelloStorico()
    {
        AndamentoOfferteDto d = AndamentoOfferteCalcolo.Calcola(new List<Riga>
        {
            Offerta(1, stato: "aperta", importo: 1000, anno: 2025, data: new DateTime(2025, 5, 1)),
            Offerta(2, stato: "aperta", importo: 700, anno: 2024, data: new DateTime(2024, 5, 1)),
        }, 2025);

        Assert.Equal(new[] { 2024, 2025 }, d.MensileMultiAnno.Select(s => s.Anno));
    }

    [Fact]
    public void IRaggruppamenti_hannoIProprITotaliELaPropriaConversione()
    {
        AndamentoOfferteDto d = Calcola(
            Offerta(1, stato: "presa", importo: 1000, importoOrdine: 1000, venditore: "EC", tipo: "S", categoria: "Impianto"),
            Offerta(2, stato: "persa", importo: 500, venditore: "EC", tipo: "R", categoria: "Ricambio"),
            Offerta(3, stato: "aperta", importo: 4000, venditore: "RC", tipo: "S", categoria: "Impianto"));

        AndamentoGruppoDto ec = d.PerVenditore.Single(g => g.Chiave == "EC");
        Assert.Equal(2, ec.Emesse);
        Assert.Equal(1500, ec.ValoreEmesse);
        Assert.Equal(50m, ec.ConversionePct);

        AndamentoGruppoDto impianto = d.PerCategoria.Single(g => g.Chiave == "Impianto");
        Assert.Equal(5000, impianto.ValoreEmesse);
        // Ordinati per valore: l'analisi di mercato si legge dall'alto.
        Assert.Equal("Impianto", d.PerCategoria.First().Chiave);
    }

    [Fact]
    public void LeApertePiuGrandi_sonoQuindiciInOrdineDiImporto()
    {
        var righe = Enumerable.Range(1, 20)
            .Select(i => Offerta(i, stato: "aperta", importo: i * 1000))
            .ToList();

        AndamentoOfferteDto d = AndamentoOfferteCalcolo.Calcola(righe, 2026);

        Assert.Equal(15, d.TopAperte.Count);
        Assert.Equal(20_000, d.TopAperte[0].Importo);
        Assert.Equal(6000, d.TopAperte[14].Importo);
    }

    // ── Portafoglio nel tempo ────────────────────────────────────────────────────

    /// <summary>
    /// <b>Senza registro modifiche non si disegna niente.</b> Sullo storico importato non c'è
    /// nessuna riga di log: una linea piatta sarebbe una bugia disegnata bene, e chi la guarda
    /// non ha modo di accorgersene.
    /// </summary>
    [Fact]
    public void SenzaRegistroModifiche_ilPortafoglioNelTempoNonSiInventa()
    {
        List<AndamentoPortafoglioDto> p = AndamentoOfferteCalcolo.Portafoglio(
            new List<Riga> { Offerta(1, stato: "aperta", importo: 1000) },
            new List<(int, DateTime, string)>(),
            new DateTime(2026, 9, 4));

        Assert.Empty(p);
    }

    /// <summary>
    /// Col registro si torna indietro: un'offerta chiusa a luglio, a giugno era ancora aperta e
    /// nel portafoglio ci stava.
    /// </summary>
    [Fact]
    public void ColRegistro_ilPortafoglioSiRicostruisceAllIndietro()
    {
        var righe = new List<Riga>
        {
            Offerta(1, stato: "persa", importo: 10_000, data: new DateTime(2026, 1, 15)),
            Offerta(2, stato: "aperta", importo: 5000, data: new DateTime(2026, 1, 20)),
        };
        var cambi = new List<(int, DateTime, string)>
        {
            (1, new DateTime(2026, 7, 10), "aperta"), // il 10 luglio è passata da aperta a persa
        };

        List<AndamentoPortafoglioDto> p = AndamentoOfferteCalcolo.Portafoglio(
            righe, cambi, new DateTime(2026, 9, 30), mesi: 12);

        Assert.Equal(12, p.Count);
        Assert.Equal(15_000, p.Single(x => x.Mese.Month == 6 && x.Mese.Year == 2026).Valore);
        Assert.Equal(5000, p.Single(x => x.Mese.Month == 8 && x.Mese.Year == 2026).Valore);
    }

    /// <summary>Un'offerta non ancora emessa a quella data non era nel portafoglio.</summary>
    [Fact]
    public void PrimaDiEssereEmessa_lOffertaNonEnelPortafoglio()
    {
        var righe = new List<Riga> { Offerta(1, stato: "aperta", importo: 8000, data: new DateTime(2026, 8, 5)) };
        var cambi = new List<(int, DateTime, string)> { (99, new DateTime(2026, 8, 1), "aperta") };

        List<AndamentoPortafoglioDto> p = AndamentoOfferteCalcolo.Portafoglio(
            righe, cambi, new DateTime(2026, 9, 30), mesi: 3);

        Assert.Equal(0, p.Single(x => x.Mese.Month == 7).Valore);
        Assert.Equal(8000, p.Single(x => x.Mese.Month == 8).Valore);
    }

    // ── Aiuti ────────────────────────────────────────────────────────────────────

    private static AndamentoOfferteDto Calcola(params Riga[] righe) =>
        AndamentoOfferteCalcolo.Calcola(righe.ToList(), 2026);

    private static Riga Offerta(
        int id,
        string stato,
        decimal? importo,
        int anno = 2026,
        decimal? importoMax = null,
        int? chance = null,
        decimal? importoOrdine = null,
        bool aConsuntivo = false,
        string venditore = "EC",
        string tipo = "S",
        string categoria = "Impianto",
        DateTime? data = null) =>
        new(id, anno, id, tipo, tipo, categoria, venditore, venditore, data ?? new DateTime(anno, 6, 1),
            "Cliente", "Descrizione", importo, importoMax, stato, chance, importoOrdine, aConsuntivo);
}
