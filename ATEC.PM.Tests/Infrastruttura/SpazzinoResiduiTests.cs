using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// Lo spazzino dei database di prova (24/08/2026).
///
/// <para>Ogni <see cref="DatabaseDiProva"/> si cancella nel proprio <c>Dispose</c>, ma quando il
/// processo muore prima — host dei test in crash, corsa interrotta — quel Dispose non gira mai.
/// Nessuno raccoglieva quei residui: se n'erano accumulati <b>71</b>, il più vecchio di otto
/// giorni, ~119 tabelle l'uno, e concorrevano a far morire le corse successive. Ora il primo
/// database di prova di ogni processo fa il giro e li toglie.</para>
///
/// <para><b>La regola che conta è quella di sicurezza</b>: lo spazzino non deve mai portarsi via
/// il database di una corsa ancora viva. Sulla stessa macchina capita davvero che due sessioni
/// provino i test insieme (24/08/2026), e cancellare il database sotto i piedi di un collega
/// sarebbe molto peggio del problema che si voleva risolvere.</para>
/// </summary>
public class SpazzinoResiduiTests
{
    private static readonly DateTime Adesso = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Unspecified);
    private static readonly TimeSpan DueOre = TimeSpan.FromHours(2);

    [Fact]
    public void IlDatabaseDiUnaCorsaAppenaPartita_nonSiTocca()
    {
        // Un minuto fa: è la corsa di qualcuno, magari la propria.
        Assert.False(DatabaseDiProva.ResiduoDaTogliere(Adesso.AddMinutes(-1), Adesso, DueOre));
    }

    [Fact]
    public void IlDatabaseDiUnaCorsaAncoraInPiedi_nonSiTocca()
    {
        // La suite intera dura ~3 minuti: a mezz'ora si è ancora larghi con la soglia a 2 ore.
        Assert.False(DatabaseDiProva.ResiduoDaTogliere(Adesso.AddMinutes(-30), Adesso, DueOre));
    }

    [Fact]
    public void IlResiduoVecchio_siToglie()
    {
        Assert.True(DatabaseDiProva.ResiduoDaTogliere(Adesso.AddHours(-3), Adesso, DueOre));
        Assert.True(DatabaseDiProva.ResiduoDaTogliere(Adesso.AddDays(-8), Adesso, DueOre));
    }

    /// <summary>
    /// Esattamente sulla soglia si toglie: il confronto è &gt;=, e un residuo di due ore tonde
    /// non è di una corsa viva (la suite ne dura tre di minuti).
    /// </summary>
    [Fact]
    public void SullaSoglia_siToglie()
    {
        Assert.True(DatabaseDiProva.ResiduoDaTogliere(Adesso - DueOre, Adesso, DueOre));
    }

    /// <summary>
    /// Schema senza tabelle: si lascia stare. È la finestra di pochi millisecondi fra
    /// CREATE DATABASE e la prima tabella — cioè, molto probabilmente, una corsa appena nata.
    /// </summary>
    [Fact]
    public void LoSchemaSenzaTabelle_nonSiTocca()
    {
        Assert.False(DatabaseDiProva.ResiduoDaTogliere(null, Adesso, DueOre));
    }

    /// <summary>
    /// La soglia dichiarata deve restare molto più larga della durata della suite: se qualcuno
    /// la abbassasse a pochi minuti, lo spazzino comincerebbe a cancellare i database delle
    /// corse vive. Questo test è lì per rendere quel cambio rumoroso.
    /// </summary>
    [Fact]
    public void LaSogliaResta_moltoPiuLargaDellaDurataDellaSuite()
    {
        Assert.True(DatabaseDiProva.EtaMinimaResiduo >= TimeSpan.FromHours(1),
            $"soglia troppo stretta ({DatabaseDiProva.EtaMinimaResiduo}): una corsa viva rischia di essere cancellata");
    }

    /// <summary>
    /// La prova sul campo, col database vero: creato un database di prova <b>adesso</b>, un
    /// altro <see cref="DatabaseDiProva"/> — che fa partire lo spazzino — non se lo porta via.
    /// È l'invariante dei test paralleli, e senza database non si può fingere.
    /// </summary>
    [FactRichiedeMySql]
    public void ConUnDatabaseVeroAppenaCreato_loSpazzinoNonLoPortaVia()
    {
        using var vivo = new DatabaseDiProva("spazzino_vivo");
        vivo.CreaSoloRegistroMigrazioni();

        // Basta crearne un altro: è il costruttore a far girare lo spazzino.
        using (var altro = new DatabaseDiProva("spazzino_altro"))
        {
            altro.CreaSoloRegistroMigrazioni();
        }

        using MySqlConnection c = vivo.Apri();
        Assert.Equal(1, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @N",
            new { N = vivo.Nome }));
    }

    /// <summary>
    /// L'altra metà, quella per cui lo spazzino esiste: un database <b>abbandonato</b> — creato
    /// e mai chiuso, come lo lascia un processo morto — viene tolto di mezzo.
    ///
    /// <para>Il residuo vero è vecchio di ore e qui non si può aspettare, quindi si chiama lo
    /// spazzino con soglia zero <b>ristretta a questo solo prefisso</b>: fuori da qui non tocca
    /// niente, e la corsa parallela delle altre collection resta al sicuro.</para>
    /// </summary>
    [FactRichiedeMySql]
    public void UnResiduoAbbandonato_loSpazzinoLoToglie()
    {
        const string prefisso = "atec_pm_test_spazzino_abbandonato";

        // Niente `using`: è esattamente il caso da riprodurre — nessuno lo chiuderà.
        var abbandonato = new DatabaseDiProva("spazzino_abbandonato");
        abbandonato.CreaSoloRegistroMigrazioni();
        string nomeAbbandonato = abbandonato.Nome;

        using var testimone = new DatabaseDiProva("spazzino_testimone");
        testimone.CreaSoloRegistroMigrazioni();

        int tolti = DatabaseDiProva.PuliziaResidui(TimeSpan.Zero, prefisso);

        // «Almeno uno» e non «esattamente uno»: se una corsa precedente è morta lasciando il
        // suo abbandonato con lo stesso prefisso, lo spazzino se li porta via tutti — ed è
        // esattamente il suo mestiere. Un Assert.Equal(1) rendeva rosso questo test proprio
        // nella condizione che deve saper gestire (visto succedere il 24/08/2026).
        Assert.True(tolti >= 1, $"lo spazzino non ha tolto niente (tolti = {tolti})");
        using MySqlConnection c = testimone.Apri();
        Assert.Equal(0, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @N",
            new { N = nomeAbbandonato }));
        // …e il testimone, che ha un prefisso diverso, è ancora lì.
        Assert.Equal(1, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @N",
            new { N = testimone.Nome }));
    }

    /// <summary>
    /// La combinazione pericolosa — soglia corta su <b>tutti</b> gli schemi — non deve essere
    /// scrivibile per sbaglio: cancellerebbe i database delle corse ancora vive. Meglio
    /// un'eccezione in faccia a chi la scrive che un guasto a chi sta provando i test.
    /// </summary>
    [Fact]
    public void SogliaCorta_suTutti_none_permessa()
    {
        Assert.Throws<ArgumentException>(() => DatabaseDiProva.PuliziaResidui(TimeSpan.Zero));
        Assert.Throws<ArgumentException>(
            () => DatabaseDiProva.PuliziaResidui(TimeSpan.FromMinutes(5), soloPrefisso: null));
    }
}
