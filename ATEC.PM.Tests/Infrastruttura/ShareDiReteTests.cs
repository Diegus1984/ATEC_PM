using ATEC.PM.Server.Services;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// La sessione SMB si apre sulla radice <c>\\server\share</c>, non sulla cartella profonda:
/// se il calcolo della radice sbaglia, <c>WNetAddConnection2</c> fallisce e le immagini degli
/// articoli Danea tornano a non copiarsi (il guasto trovato il 25/08/2026).
/// </summary>
public class ShareDiReteTests
{
    [Theory]
    [InlineData(@"\\Server-maga\d\DANEA\NON TOCCARE DANEA\Archivi\Atec_PM - Allegati", @"\\Server-maga\d")]
    [InlineData(@"\\Server-maga\d", @"\\Server-maga\d")]
    [InlineData(@"\\Server-maga\d\", @"\\Server-maga\d")]
    [InlineData(@"//Server-maga/d/DANEA", @"\\Server-maga\d")]
    public void PercorsoDiRete_daLaRadiceDellaShare(string percorso, string atteso)
    {
        Assert.Equal(atteso, NetworkShareConnector.ShareRoot(percorso));
    }

    /// <summary>
    /// Percorsi locali e stringhe monche non hanno nessuna share da autenticare: devono dare
    /// null, altrimenti si proverebbe ad aprire una sessione verso il nulla a ogni copia.
    /// </summary>
    [Theory]
    [InlineData(@"D:\DANEA\Archivi\Atec_PM - Allegati")]
    [InlineData(@"\\Server-maga")]
    [InlineData("")]
    [InlineData(null)]
    public void PercorsoLocaleOMonco_nonHaShare(string? percorso)
    {
        Assert.Null(NetworkShareConnector.ShareRoot(percorso));
    }
}
