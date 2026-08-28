using System.Text.RegularExpressions;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// La versione del server esposta da <c>/api/health</c>: serve a vedere dall'applicazione
/// se il server è aggiornato, cosa che il build id in basso a sinistra non dice (quello è
/// del client web, e un deploy di solo C# non lo muove).
/// </summary>
public class VersioneServerTests
{
    [Fact]
    public void Ha_la_stessa_forma_del_build_id_del_client()
    {
        string build = VersioneServer.Build;

        Assert.False(string.IsNullOrEmpty(build));
        Assert.Matches(new Regex(@"^\d{8}-\d{4}$"), build);
    }

    [Fact]
    public void Si_calcola_una_volta_sola()
    {
        // È una proprietà di sola lettura inizializzata all'avvio: due letture devono dare
        // lo stesso valore anche se nel frattempo qualcuno tocca i file.
        Assert.Equal(VersioneServer.Build, VersioneServer.Build);
    }
}
