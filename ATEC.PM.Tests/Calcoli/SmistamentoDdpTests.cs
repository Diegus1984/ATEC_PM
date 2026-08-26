using ATEC.PM.Server.Services;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Segnalazione #119 — dove finisce un componente quando si importa un gruppo Codex in commessa.
///
/// <para>È una regola che sbaglia <b>in silenzio</b>: una riga finita nella DDP sbagliata non dà
/// nessun errore, semplicemente non la vede chi la deve ordinare (o la vede chi non c'entra), e
/// il pezzo salta fuori mancante in montaggio. Nessuna schermata segnala che il pezzo è di là.</para>
///
/// <para>Il punto che questi test difendono davvero è il <b>ripiego</b>: prima della #119 tutto
/// andava in officina, quindi ciò che la regola non riconosce deve continuare ad andarci. Un
/// ripiego cambiato in «commerciale» sposterebbe di colpo i codici scritti a mano e i 4xx
/// storici in una griglia dove nessuno li ha mai cercati.</para>
/// </summary>
public class SmistamentoDdpTests
{
    [Theory]
    [InlineData("201231219001")]   // commerciale generico
    [InlineData("211240726002")]   // commerciale elettrico
    [InlineData("221010125001")]   // commerciale pneumatico
    [InlineData("301101018006")]   // elemento di fissaggio: si compra
    [InlineData("202010324001")]   // famiglia storica 2xx
    public void CodiciDaComprare_vannoInCommerciale(string codice)
    {
        Assert.Equal(DdpDestinazione.Commerciale, DdpSmistamento.Destinazione(codice));
        Assert.True(DdpSmistamento.VaInCommerciale(codice));
    }

    [Theory]
    [InlineData("101231219003")]   // particolare a disegno: si costruisce
    [InlineData("102010221001")]   // famiglia storica 1xx
    [InlineData("501140621001")]   // gruppo meccanico annidato
    [InlineData("511250826001")]   // gruppo custom: si comporta come il 501
    [InlineData("601230919001")]   // assieme
    [InlineData("701010422001")]   // layout
    public void GruppiEParticolari_restanoInOfficina(string codice)
    {
        Assert.Equal(DdpDestinazione.Officina, DdpSmistamento.Destinazione(codice));
        Assert.False(DdpSmistamento.VaInCommerciale(codice));
    }

    /// <summary>
    /// Il 511 è nato il 25/08/2026 come clone del 501: se un giorno qualcuno lo smistasse
    /// altrove, la colonnina luminosa finirebbe in una griglia diversa dal gruppo meccanico
    /// che le sta accanto nella stessa commessa. Devono restare indistinguibili.
    /// </summary>
    [Fact]
    public void Il511SiComportaComeIl501()
    {
        Assert.Equal(
            DdpSmistamento.Destinazione("501140621001"),
            DdpSmistamento.Destinazione("511140621001"));
    }

    /// <summary>
    /// In DDP il codice è salvato col punto (<c>201231219.001</c>), nel Codex senza. Le due
    /// forme devono decidere uguale, altrimenti la stessa riga cambierebbe griglia a seconda
    /// di chi la sta guardando.
    /// </summary>
    [Fact]
    public void PuntoESpazi_nonCambianoLaDestinazione()
    {
        Assert.Equal(
            DdpSmistamento.Destinazione("201231219001"),
            DdpSmistamento.Destinazione("201231219.001"));
        Assert.Equal(
            DdpSmistamento.Destinazione("301101018006"),
            DdpSmistamento.Destinazione(" 301101018.006 "));
    }

    /// <summary>
    /// Il ripiego. Codice vuoto, part number scritto a mano, famiglia 401 ritirata: tutto in
    /// officina, perché è dove finivano prima della #119. Lo sconosciuto non si sposta da solo.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("401010222004")]           // materia prima, famiglia ritirata il 25/08/2026
    [InlineData("VITE M8 DA MAGAZZINO")]   // part number scritto a mano
    public void CodiciSconosciuti_restanoInOfficinaComePrima(string? codice)
    {
        Assert.Equal(DdpDestinazione.Officina, DdpSmistamento.Destinazione(codice));
    }
}
