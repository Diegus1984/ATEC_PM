using ATEC.PM.Server.Services;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Specchio prezzi vecchio archivio Danea → Atec_PM (01/09/2026).
///
/// <para>Il giro automatico riscrive prezzi in un archivio gestionale ogni 12 ore: se il
/// confronto sbaglia, sbaglia in silenzio e in produzione. I due modi di sbagliare che
/// questi test tengono fermi: contare come «diverso» un NULL che vale zero (stesso
/// UPDATE a ogni giro, per sempre, su articoli che nessuno ha toccato) e lasciarsi
/// scappare una differenza vera (il caso FCA00017733, a Catalogo 10,64 quando in Danea
/// era gia' 8,78).</para>
/// </summary>
public class PrezziSpecchioTests
{
    private static decimal?[] Prezzi(decimal? nettoForn, decimal? ivatoForn, decimal? netto1, decimal? ivato1) =>
        new[] { nettoForn, ivatoForn, netto1, ivato1 };

    // ── Quando NON si tocca nulla ─────────────────────────────────────────

    [Fact]
    public void StessiPrezzi_NessunaDifferenza()
    {
        var differenze = PrezziSpecchio.Differenze(
            "FCA00017733", 13596,
            Prezzi(8.78m, 10.712m, 8.78m, 10.712m),
            Prezzi(8.78m, 10.712m, 8.78m, 10.712m));

        Assert.Empty(differenze);
    }

    /// <summary>
    /// Firebird rende 8,780 dove il vecchio ha 8,78: stesso prezzo, scala diversa. Se
    /// contasse come differenza, lo specchio riscriverebbe l'articolo a ogni giro.
    /// </summary>
    [Fact]
    public void StessoPrezzoConScalaDiversa_NessunaDifferenza()
    {
        var differenze = PrezziSpecchio.Differenze(
            "FCA00017733", 13596,
            Prezzi(8.780m, 10.712m, 8.780m, 10.712m),
            Prezzi(8.78m, 10.712m, 8.78m, 10.712m));

        Assert.Empty(differenze);
    }

    /// <summary>In Danea «prezzo assente» e «prezzo a zero» sono la stessa cosa: uno dei
    /// due archivi puo' avere NULL dove l'altro ha 0 senza che nessuno abbia cambiato
    /// niente (visto su PrezzoIvatoForn).</summary>
    [Theory]
    [InlineData(null, 0.0)]
    [InlineData(0.0, null)]
    [InlineData(null, null)]
    public void NullEZeroSonoLoStessoPrezzo(double? vecchio, double? nuovo)
    {
        var differenze = PrezziSpecchio.Differenze(
            "LUC-Z15", 13620,
            Prezzi((decimal?)vecchio, 0m, 0m, 0m),
            Prezzi((decimal?)nuovo, 0m, 0m, 0m));

        Assert.Empty(differenze);
    }

    // ── Quando si riscrive ────────────────────────────────────────────────

    [Fact]
    public void PrezzoRitoccatoNelVecchio_DifferenzaSulCampoGiusto()
    {
        var differenze = PrezziSpecchio.Differenze(
            "FCA00017733", 13596,
            Prezzi(8.78m, 10.712m, 8.78m, 10.712m),
            Prezzi(10.64m, 12.981m, 10.64m, 12.981m));

        Assert.Equal(4, differenze.Count);
        var netto = Assert.Single(differenze, d => d.Campo == "PrezzoNettoForn");
        Assert.Equal("FCA00017733", netto.CodArticolo);
        Assert.Equal(13596, netto.IdInAtecPm);
        Assert.Equal(10.64m, netto.Prima);   // quello che c'e' adesso in Atec_PM
        Assert.Equal(8.78m, netto.Dopo);     // quello del vecchio archivio, che vince
    }

    /// <summary>Il caso LUC-Z15: articolo arrivato senza prezzo, listino vero solo nel vecchio.</summary>
    [Fact]
    public void PrezzoMancanteInAtecPm_SiRiempieDalVecchio()
    {
        var differenze = PrezziSpecchio.Differenze(
            "LUC-Z15", 13620,
            Prezzi(150m, 183m, 150m, 183m),
            Prezzi(0m, null, 0m, null));

        Assert.Equal(4, differenze.Count);
        Assert.All(differenze, d => Assert.Equal(0m, d.Prima));
    }

    /// <summary>Un solo campo fuori posto non deve trascinarsi dietro gli altri tre.</summary>
    [Fact]
    public void SoloUnCampoDiverso_UnaSolaDifferenza()
    {
        var differenze = PrezziSpecchio.Differenze(
            "RLEH12 1012 ES", 13593,
            Prezzi(150.5m, 183.61m, 150.5m, 183.61m),
            Prezzi(150.5m, 183.61m, 150.07m, 183.61m));

        var sola = Assert.Single(differenze);
        Assert.Equal("PrezzoNetto1", sola.Campo);
    }

    // ── Guardia ───────────────────────────────────────────────────────────

    /// <summary>Se qualcuno allunga PrezziSpecchio.Campi senza toccare la lettura, meglio
    /// un'eccezione subito che un UPDATE con le colonne sfasate.</summary>
    [Fact]
    public void ValoriInNumeroSbagliato_Esplode()
    {
        Assert.Throws<ArgumentException>(() => PrezziSpecchio.Differenze(
            "FCA00017733", 13596, new decimal?[] { 1m, 2m }, Prezzi(1m, 2m, 3m, 4m)));
    }
}
