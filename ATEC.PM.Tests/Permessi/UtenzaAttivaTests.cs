using ATEC.PM.Server.Services;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// «Chi non è più in forza esce SUBITO»: <see cref="FeatureAccessService.IsUtenteAttivo"/>.
///
/// <para>È il controllo che <c>Program.cs</c> esegue nell'evento <c>OnTokenValidated</c>, cioè su
/// <b>ogni</b> richiesta autenticata e su ogni connessione agli hub. Prima esisteva solo al login:
/// il token dura 8 ore, quindi una persona cessata continuava a lavorare per mezza giornata.
/// Un difetto qui non si vede da nessuna parte — l'applicazione funziona benissimo, per chi non
/// dovrebbe più entrarci.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class UtenzaAttivaTests
{

    private readonly SchemaCondiviso _schema;

    /// <summary>
    /// xUnit costruisce una istanza per ogni test: qui si riporta il database condiviso a
    /// com'era appena creato (~45 ms), invece di costruirne uno nuovo (~5 s).
    /// </summary>
    public UtenzaAttivaTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }
    [FactRichiedeMySql]
    public void DipendenteInForza_puoEntrare()
    {
        int id = CreaDipendente(_schema, "ACTIVE");

        var access = new FeatureAccessService(_schema.Servizio());

        Assert.True(access.IsUtenteAttivo(id));
    }

    [FactRichiedeMySql]
    public void DipendenteCessato_nonPuoPiuEntrare()
    {
        int id = CreaDipendente(_schema, "TERMINATED");

        var access = new FeatureAccessService(_schema.Servizio());

        Assert.False(access.IsUtenteAttivo(id));
    }

    /// <summary>
    /// La risposta è in cache 30 secondi per non fare una query a ogni richiesta. Chi disattiva
    /// una persona chiama <c>DimenticaPersona</c> e non deve aspettare la scadenza: senza questo,
    /// fra il gesto e l'effetto passerebbe mezzo minuto in cui il cessato lavora ancora.
    /// </summary>
    [FactRichiedeMySql]
    public void CessatoDopoEsserStatoLetto_esceSubitoSeSiSvuotaLaCache()
    {
        int id = CreaDipendente(_schema, "ACTIVE");

        var access = new FeatureAccessService(_schema.Servizio());
        Assert.True(access.IsUtenteAttivo(id));   // la risposta finisce in cache

        _schema.Esegui($"UPDATE employees SET status = 'TERMINATED' WHERE id = {id}");
        Assert.True(access.IsUtenteAttivo(id));   // ancora dentro: è la cache, ed è voluto

        access.DimenticaPersona(id);
        Assert.False(access.IsUtenteAttivo(id));  // il gesto ha effetto immediato
    }

    /// <summary>Un token senza id valido non deve mai passare.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IdNonValido_nonPassaMai(int id)
    {
        // Nessun database: il controllo sull'id precede qualunque query.
        var access = new FeatureAccessService(null!);

        Assert.False(access.IsUtenteAttivo(id));
    }

    private static int CreaDipendente(SchemaCondiviso schema, string stato)
    {
        using var c = schema.Apri();
        c.Execute(
            @"INSERT INTO employees (first_name, last_name, username, status)
              VALUES ('Prova', 'Permessi', 'prova.permessi', @Stato)", new { Stato = stato });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }
}
