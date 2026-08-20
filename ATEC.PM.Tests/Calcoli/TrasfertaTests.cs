using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Le due regole della trasferta che costano soldi veri se si rompono, e che si rompono senza
/// dire niente: <b>l'indennità fuori dal ricarico</b> (preventivo) e <b>i giorni decisi dal
/// motore</b> (consuntivo dal Timesheet).
/// </summary>
public class TrasfertaTests
{
    // ── preventivo: il K non tocca l'indennità ────────────────────────────────

    /// <summary>
    /// <b>L'indennità non è una spesa ricaricabile.</b> È l'unico punto in C# dove la separazione
    /// è scritta, e chi mettesse <c>AllowanceCost</c> dentro <c>MarkableCost</c> non vedrebbe
    /// nulla a video finché il K resta 1,000 (oggi lo è sempre): il difetto salterebbe fuori solo
    /// il giorno che il ricarico viene riacceso, con l'indennità gonfiata su tutte le commesse.
    /// </summary>
    [Fact]
    public void SpeseRicaricabili_nonComprendonoLIndennita()
    {
        var riga = new ProjectCostTravelRowDto
        {
            Nights = 2, NightPrice = 100m,   // alloggio 200
            MealCost = 30m,
            CarCost = 50m,
            TransportCost = 20m,
            AllowanceCost = 999m,            // NON deve entrare nelle ricaricabili
        };

        Assert.Equal(200m, riga.LodgingCost);
        Assert.Equal(300m, riga.MarkableCost);        // 200 + 30 + 50 + 20
        Assert.Equal(1299m, riga.TravelCost);         // ricaricabili + indennità
    }

    [Fact]
    public void TotaliDiTabella_tengonoSeparateRicaricabiliEIndennita()
    {
        var riga = new ProjectCostTravelRowDto
        {
            Nights = 2, NightPrice = 100m, MealCost = 30m,
            CarCost = 50m, TransportCost = 20m, AllowanceCost = 999m,
        };

        ProjectCostTravelTotalsDto totali = ProjectCostTravelTotalsDto.Of(new[] { riga });

        Assert.Equal(200m, totali.LodgingCost);
        Assert.Equal(999m, totali.AllowanceCost);
        Assert.Equal(300m, totali.MarkableCost);
        Assert.Equal(1299m, totali.TravelCost);
    }

    /// <summary>Alloggio incompleto = colonna vuota, e nei totali pesa zero (non fa saltare la somma).</summary>
    [Fact]
    public void AlloggioSenzaPrezzo_nonHaCostoEValeZeroNeiTotali()
    {
        var riga = new ProjectCostTravelRowDto { Nights = 3, NightPrice = null, MealCost = 25m };

        Assert.Null(riga.LodgingCost);
        Assert.Equal(25m, riga.MarkableCost);
    }

    // ── consuntivo: i giorni li decide il Timesheet ───────────────────────────

    /// <summary>
    /// <b>Lo zero esplicito è una decisione, non un vuoto.</b> Quando una persona lavora su due
    /// fasi di cantiere nello stesso giorno, il motore mette 1 giorno sulla prima riga e 0 sulle
    /// altre: è l'antidoto al doppio conteggio dell'indennità. Basta «pulire» il
    /// <c>TravelDays ?? …</c> in <c>TravelDays &gt; 0 ? … : …</c> perché lo 0 torni a essere
    /// calcolato dalle date e le giornate raddoppino, in silenzio, su tutte le persone in cantiere.
    /// </summary>
    [Fact]
    public void RigaDalTimesheetConZeroGiorni_restaAZeroENonRicalcolaDalleDate()
    {
        var riga = new TravelRowDto
        {
            Source = TravelSources.Timesheet,
            TravelDays = 0,
            StartDate = new DateTime(2026, 8, 3),
            EndDate = new DateTime(2026, 8, 7),   // 5 giorni, se ricalcolasse
        };

        Assert.Equal(0m, riga.Days);
        Assert.True(riga.IsDerived);
    }

    [Fact]
    public void RigaDalTimesheetConUnGiorno_valeUno()
    {
        var riga = new TravelRowDto
        {
            Source = TravelSources.Timesheet,
            TravelDays = 1m,
            StartDate = new DateTime(2026, 8, 3),
            EndDate = new DateTime(2026, 8, 7),
        };

        Assert.Equal(1m, riga.Days);
    }

    /// <summary>
    /// <b>La mezza giornata della #98 sopravvive al giro.</b> Il motore scrive 0,5 quando le
    /// ore scaricate sul tag cliente sono al più 4: la riga deve riportarla tale e quale,
    /// senza arrotondarla né ricalcolarla dalle date.
    /// </summary>
    [Fact]
    public void RigaDalTimesheetConMezzaGiornata_valeMezza()
    {
        var riga = new TravelRowDto
        {
            Source = TravelSources.Timesheet,
            TravelDays = 0.5m,
            StartDate = new DateTime(2026, 8, 3),
            EndDate = new DateTime(2026, 8, 7),
        };

        Assert.Equal(0.5m, riga.Days);
    }

    /// <summary>
    /// <b>La regola della #98, ai bordi.</b> Fino a 4 ore scaricate la giornata vale 0,5;
    /// oltre vale 1 — anche a 4,5 ore (la terra di mezzo fra 4 e 5 arrotonda in su, come
    /// da lettura del «da 5 a 10» di Zanoni) e anche oltre le 10 (un giorno di calendario
    /// non può valere più di 1). La stessa soglia è riscritta in SQL nella migrazione v98 e
    /// nel previsto di Summaries: chi la cambia qui deve cambiarla anche là.
    /// </summary>
    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(4, 0.5)]
    [InlineData(4.5, 1)]
    [InlineData(5, 1)]
    [InlineData(8, 1)]
    [InlineData(12, 1)]
    public void GiorniDaOre_mezzaGiornataFinoAQuattroOre(double ore, double attesi)
    {
        Assert.Equal((decimal)attesi, TravelMath.GiorniDaOre((decimal)ore));
    }

    /// <summary>La riga scritta a mano non ha giorni dal motore: li calcola dalle date, inclusive.</summary>
    [Fact]
    public void RigaAMano_calcolaIGiorniDalleDate()
    {
        var riga = new TravelRowDto
        {
            Source = TravelSources.Manual,
            TravelDays = null,
            StartDate = new DateTime(2026, 8, 3),
            EndDate = new DateTime(2026, 8, 7),
        };

        Assert.Equal(5m, riga.Days);
        Assert.False(riga.IsDerived);
    }

    /// <summary>
    /// Due fasi nello stesso giorno = un giorno solo di trasferta. È il conto che finisce
    /// sull'indennità e sulle card della pagina Trasferta.
    /// </summary>
    [Fact]
    public void DueFasiNelloStessoGiorno_valgonoUnGiornoSolo()
    {
        var prima = new TravelRowDto { Source = TravelSources.Timesheet, TravelDays = 1 };
        var seconda = new TravelRowDto { Source = TravelSources.Timesheet, TravelDays = 0 };

        TravelTotalsDto totali = TravelTotalsDto.Of(new[] { prima, seconda });

        Assert.Equal(1m, totali.Days);
    }
}
