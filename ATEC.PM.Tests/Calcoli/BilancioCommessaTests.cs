using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// I calcoli del Bilancio commessa che si romperebbero <b>in silenzio</b>: nessun errore a video,
/// solo numeri diversi da quelli veri.
///
/// <para>Il filo che li lega è uno solo: <b>«non calcolato» e «calcolato, fa zero» sono due cose
/// diverse</b>. La prima si mostra «—», la seconda «0,00 €». Ogni «semplificazione» che sostituisce
/// una somma-o-null con una somma normale cancella la distinzione, e da lì in poi il Bilancio
/// afferma numeri che nessuno ha mai calcolato.</para>
/// </summary>
public class BilancioCommessaTests
{
    // ── Totale Ordine ─────────────────────────────────────────────────────────

    /// <summary>
    /// Nessuna riga con importo → «—». Il `lines.Sum(l => l.Amount ?? 0)` che verrebbe naturale
    /// scrivere restituirebbe 0, e allora il <b>Margine di Sicurezza</b> smetterebbe di essere
    /// nullo: comparirebbe un rosso pari a tutto il costo di vendita su commesse che l'ordine non
    /// ce l'hanno ancora.
    /// </summary>
    [Fact]
    public void OrdineSenzaImporti_nonHaTotale()
    {
        var righe = new[]
        {
            new ProjectOrderLineDto { Amount = null },
            new ProjectOrderLineDto { Amount = null },
        };

        Assert.Null(ProjectEconomics.DisplayOrderTotal(righe));
        Assert.Null(ProjectEconomics.DisplayOrderTotal(Array.Empty<ProjectOrderLineDto>()));
    }

    /// <summary>Un ordine da 0,00 € è un dato: qualcuno l'ha scritto. Non è un ordine mancante.</summary>
    [Fact]
    public void OrdineDaZero_nonEUnOrdineMancante()
    {
        var righe = new[]
        {
            new ProjectOrderLineDto { Amount = null },
            new ProjectOrderLineDto { Amount = 0m },
        };

        Assert.Equal(0m, ProjectEconomics.DisplayOrderTotal(righe));
    }

    [Fact]
    public void OrdineMultiRiga_sommaSoloLeRigheValorizzate()
    {
        var righe = new[]
        {
            new ProjectOrderLineDto { Amount = null },
            new ProjectOrderLineDto { Amount = 1500.50m },
            new ProjectOrderLineDto { Amount = 2000m },
        };

        Assert.Equal(3500.50m, ProjectEconomics.DisplayOrderTotal(righe));
    }

    // ── righe delle calcolatrici ──────────────────────────────────────────────

    /// <summary>
    /// La riga <b>a forfait</b>: senza quantità, il costo unitario vale come importo della riga.
    /// Chi la riscrivesse in `Quantity * UnitCost` porterebbe a zero tutte le righe forfettarie.
    /// Il moltiplicatore si applica lo stesso.
    /// </summary>
    [Fact]
    public void RigaAForfait_valeIlCostoUnitarioPerIlMoltiplicatore()
    {
        var riga = new ProjectCalcRowDto { Quantity = null, UnitCost = 1200m, Multiplier = 3m };

        Assert.Equal(3600m, riga.ComputedAmount);
    }

    [Fact]
    public void RigaAQuantita_moltiplicaQuantitaPerCosto_eLaVenditaApplicaIlRicarico()
    {
        var riga = new ProjectCalcRowDto { Quantity = 10m, UnitCost = 45m };

        Assert.Equal(450m, riga.ComputedAmount);
        // MarkupValue vale 1,450 di default: è un moltiplicatore, non una percentuale.
        Assert.Equal(652.50m, riga.SaleAmount);
    }

    [Fact]
    public void RigaSenzaCostoUnitario_nonHaImporto()
    {
        var riga = new ProjectCalcRowDto { Quantity = 10m, UnitCost = null };

        Assert.Null(riga.ComputedAmount);
    }

    /// <summary>Quantità zero è una quantità: fa 0, non «non calcolato».</summary>
    [Fact]
    public void RigaConQuantitaZero_valeZeroNonNulla()
    {
        var riga = new ProjectCalcRowDto { Quantity = 0m, UnitCost = 50m };

        Assert.Equal(0m, riga.ComputedAmount);
    }

    /// <summary>Col lucchetto l'importo digitato vince sul calcolo.</summary>
    [Fact]
    public void RigaConLucchetto_usaLImportoDigitato()
    {
        var riga = new ProjectCalcRowDto
        {
            Quantity = 10m, UnitCost = 45m,   // calcolerebbe 450
            AmountPinned = true, Amount = 999m,
        };

        Assert.Equal(450m, riga.ComputedAmount);
        Assert.Equal(999m, riga.EffectiveAmount);
    }

    // ── totali del foglio ─────────────────────────────────────────────────────

    /// <summary>
    /// Le righe <b>senza sezione</b> (GroupKey vuoto) pesano sul totale del foglio ma su nessuna
    /// delle due sezioni: è la differenza che il Riepilogo mostra come «Lavorazioni Officine non
    /// classificate». Se sparisse, quei soldi uscirebbero dal Bilancio senza che nessuno lo veda.
    /// </summary>
    [Fact]
    public void RigheSenzaSezione_pesanoSulTotaleMaNonSulleSezioni()
    {
        var foglio = new ProjectCalcSheetDto
        {
            Rows = new List<ProjectCalcRowDto>
            {
                new() { GroupKey = CalcGroups.External, Quantity = null, UnitCost = 100m },
                new() { GroupKey = CalcGroups.Internal, Quantity = 2m, UnitCost = 100m },
                new() { GroupKey = "", Quantity = null, UnitCost = 50m },
            },
        };

        ProjectCalcSheets.WithTotals(foglio);

        Assert.Equal(350m, foglio.Total);
        Assert.Equal(507.50m, foglio.SaleTotal);
        Assert.Equal(100m, ProjectCalcSheets.GroupTotal(foglio, CalcGroups.External));
        Assert.Equal(200m, ProjectCalcSheets.GroupTotal(foglio, CalcGroups.Internal));

        decimal nonClassificate = foglio.Total!.Value
            - ProjectCalcSheets.GroupTotal(foglio, CalcGroups.External)!.Value
            - ProjectCalcSheets.GroupTotal(foglio, CalcGroups.Internal)!.Value;
        Assert.Equal(50m, nonClassificate);
    }

    /// <summary>Una sezione senza righe non vale zero: non è stata compilata.</summary>
    [Fact]
    public void SezioneSenzaRighe_nonHaTotale()
    {
        var foglio = new ProjectCalcSheetDto
        {
            Rows = new List<ProjectCalcRowDto>
            {
                new() { GroupKey = CalcGroups.External, Quantity = null, UnitCost = 100m },
            },
        };

        ProjectCalcSheets.WithTotals(foglio);

        Assert.Equal(100m, ProjectCalcSheets.GroupTotal(foglio, CalcGroups.External));
        Assert.Null(ProjectCalcSheets.GroupTotal(foglio, CalcGroups.Internal));
    }

    [Fact]
    public void FoglioMaiCompilato_nonHaTotali()
    {
        var foglio = new ProjectCalcSheetDto { Rows = new List<ProjectCalcRowDto>() };

        ProjectCalcSheets.WithTotals(foglio);

        Assert.Null(foglio.Total);
        Assert.Null(foglio.SaleTotal);
    }
}
