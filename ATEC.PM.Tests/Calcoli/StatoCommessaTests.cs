using ATEC.PM.Shared;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// La definizione unica di «commessa chiusa» (<see cref="ProjectStatuses.IsClosed"/>).
///
/// <para>È un interruttore piccolo con conseguenze larghe: rende il foglio SAL di sola lettura,
/// e decide quali commesse spariscono dalle liste per default. <b>Sospesa non è chiusa</b>: se
/// <c>ON_HOLD</c> entrasse nell'elenco, tutte le commesse sospese diventerebbero non modificabili
/// con un messaggio che parla di «commessa chiusa» — e non solo nel SAL.</para>
/// </summary>
public class StatoCommessaTests
{
    [Theory]
    [InlineData(ProjectStatuses.Completed)]
    [InlineData(ProjectStatuses.Cancelled)]
    public void LavoroFinitoOAnnullato_eChiusa(string stato)
    {
        Assert.True(ProjectStatuses.IsClosed(stato));
    }

    [Theory]
    [InlineData(ProjectStatuses.Draft)]
    [InlineData(ProjectStatuses.Active)]
    [InlineData(ProjectStatuses.OnHold)]   // sospesa: il lavoro riprenderà
    public void LavoroInCorsoOSospeso_nonEChiusa(string stato)
    {
        Assert.False(ProjectStatuses.IsClosed(stato));
    }

    /// <summary>
    /// Il valore arriva dal database e dal client: spazi e maiuscole non devono cambiare la
    /// risposta, o una commessa completata resterebbe modificabile per un carattere di troppo.
    /// </summary>
    [Theory]
    [InlineData(" completed ")]
    [InlineData("Completed")]
    [InlineData("CANCELLED")]
    public void LoStato_vieneNormalizzato(string stato)
    {
        Assert.True(ProjectStatuses.IsClosed(stato));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("STATO_INVENTATO")]
    public void StatoMancanteOSconosciuto_nonEChiusa(string? stato)
    {
        Assert.False(ProjectStatuses.IsClosed(stato));
    }
}
