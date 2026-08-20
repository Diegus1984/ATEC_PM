using ATEC.PM.Server.Migrations;
using ATEC.PM.Tests.Infrastruttura;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Migrazioni;

/// <summary>
/// Il <see cref="MigrationRunner"/> — l'infrastruttura del blocco A1, che sostituisce i blocchi
/// <c>if</c> dentro <c>DbService</c> con una classe per migrazione.
/// </summary>
public class MigrationRunnerTests
{
    /// <summary>Migrazione finta, per provare il runner senza toccare quelle vere.</summary>
    private sealed class Finta : IMigrazione
    {
        private readonly Action<MySqlConnection>? _lavoro;
        public Finta(int versione, Action<MySqlConnection>? lavoro = null, bool facoltativa = false)
        {
            Versione = versione;
            _lavoro = lavoro;
            Facoltativa = facoltativa;
        }
        public int Versione { get; }
        public string Descrizione => $"migrazione di prova v{Versione}";
        public bool Facoltativa { get; }
        public void Applica(MySqlConnection c, ILogger log) => _lavoro?.Invoke(c);
    }

    private static MigrationRunner Runner(params IMigrazione[] m) =>
        new(NullLogger.Instance, m);

    // ── le guardie ────────────────────────────────────────────────────────────

    /// <summary>
    /// Due migrazioni con lo stesso numero: una delle due non verrebbe mai applicata (la seconda
    /// troverebbe la versione già registrata) e nessuno se ne accorgerebbe. Meglio non partire.
    /// </summary>
    [Fact]
    public void DueMigrazioniConLaStessaVersione_fermanoLAvvio()
    {
        var errore = Assert.Throws<InvalidOperationException>(
            () => Runner(new Finta(90), new Finta(90)));

        Assert.Contains("v90", errore.Message);
    }

    /// <summary>Le migrazioni vere del progetto devono rispettare la regola in ogni momento.</summary>
    [Fact]
    public void LeMigrazioniVere_sonoCoerentiEsenzaDoppioni()
    {
        MigrationRunner reale = new(NullLogger.Instance);   // le scopre dall'assembly

        Assert.NotEmpty(reale.Migrazioni);
        Assert.Equal(reale.Migrazioni.Count, reale.Migrazioni.Select(m => m.Versione).Distinct().Count());
        Assert.All(reale.Migrazioni, m => Assert.False(string.IsNullOrWhiteSpace(m.Descrizione)));
    }

    /// <summary>
    /// La serie delle versioni non deve avere buchi: <c>DbService</c> considera pendente ogni numero
    /// da 1 alla più alta, quindi un numero saltato (un file cancellato, o una migrazione nuova
    /// numerata troppo avanti) farebbe fallire il controllo di chiusura e <b>non far partire il
    /// server</b> — qui si vede subito, invece che al primo avvio dopo il deploy.
    /// </summary>
    [Fact]
    public void LeMigrazioniVere_copronoLaSerieSenzaBuchi()
    {
        MigrationRunner reale = new(NullLogger.Instance);

        int[] versioni = reale.Migrazioni.Select(m => m.Versione).OrderBy(v => v).ToArray();
        int[] mancanti = Enumerable.Range(1, reale.VersioneMassima).Except(versioni).ToArray();

        Assert.Empty(mancanti);
    }

    /// <summary>
    /// Il nome del file dice la versione: <c>M073_…</c> deve essere la v73. Sono 87 file e si
    /// leggono per numero — un nome che mente manderebbe a leggere la migrazione sbagliata.
    /// </summary>
    [Fact]
    public void IlNomeDellaClasse_corrispondeAllaVersione()
    {
        MigrationRunner reale = new(NullLogger.Instance);

        foreach (IMigrazione m in reale.Migrazioni)
            Assert.StartsWith($"M{m.Versione:000}_", m.GetType().Name);
    }

    // ── il comportamento ──────────────────────────────────────────────────────

    [FactRichiedeMySql]
    public void MigrazioneRiuscita_vieneRegistrataDalRunner()
    {
        using var db = new DatabaseDiProva("runner_ok");
        db.CreaSoloRegistroMigrazioni();

        using MySqlConnection c = db.Apri();
        HashSet<int> applicate = new(db.VersioniApplicate());

        bool haLavorato = false;
        EsitoMigrazioni esito = Runner(new Finta(900, _ => haLavorato = true)).Applica(c, applicate, stopOnError: true);

        Assert.Equal(1, esito.Applicate);
        Assert.True(haLavorato);
        // La riga la scrive il RUNNER: la migrazione non deve preoccuparsene (prima ogni blocco
        // scriveva la propria, e chi se ne dimenticava tornava a girare a ogni avvio per sempre).
        Assert.Contains(900, db.VersioniApplicate());
        Assert.Equal("migrazione di prova v900", db.Descrizione(900));
    }

    [FactRichiedeMySql]
    public void MigrazioneGiaApplicata_nonGiraDiNuovo()
    {
        using var db = new DatabaseDiProva("runner_idem");
        db.CreaSoloRegistroMigrazioni();
        db.Esegui("INSERT INTO schema_migrations (version, description) VALUES (901, 'gia fatta')");

        using MySqlConnection c = db.Apri();
        HashSet<int> applicate = new(db.VersioniApplicate());

        bool haLavorato = false;
        EsitoMigrazioni esito = Runner(new Finta(901, _ => haLavorato = true)).Applica(c, applicate, stopOnError: true);

        Assert.Equal(0, esito.Applicate);
        Assert.False(haLavorato);
    }

    /// <summary>
    /// Una migrazione che fallisce NON deve risultare applicata: se venisse registrata lo stesso,
    /// il suo effetto mancherebbe per sempre senza che nessuno possa accorgersene.
    /// </summary>
    [FactRichiedeMySql]
    public void MigrazioneFallita_nonVieneRegistrata()
    {
        using var db = new DatabaseDiProva("runner_ko");
        db.CreaSoloRegistroMigrazioni();

        using MySqlConnection c = db.Apri();
        HashSet<int> applicate = new(db.VersioniApplicate());
        var rotta = new Finta(902, _ => throw new InvalidOperationException("guasto finto"));

        Assert.Throws<InvalidOperationException>(
            () => Runner(rotta).Applica(c, applicate, stopOnError: true));

        Assert.DoesNotContain(902, db.VersioniApplicate());
    }

    /// <summary>Con StopOnError=false si prosegue, ma la versione resta pendente per il riavvio dopo.</summary>
    [FactRichiedeMySql]
    public void MigrazioneFallita_conStopOnErrorFalse_restaPendenteEProsegue()
    {
        using var db = new DatabaseDiProva("runner_ko_tollerante");
        db.CreaSoloRegistroMigrazioni();

        using MySqlConnection c = db.Apri();
        HashSet<int> applicate = new(db.VersioniApplicate());

        bool successivaHaLavorato = false;
        EsitoMigrazioni esito = Runner(
                new Finta(903, _ => throw new InvalidOperationException("guasto finto")),
                new Finta(904, _ => successivaHaLavorato = true))
            .Applica(c, applicate, stopOnError: false);

        Assert.Equal(1, esito.Applicate);
        Assert.True(successivaHaLavorato);
        Assert.DoesNotContain(903, db.VersioniApplicate());
        Assert.Contains(904, db.VersioniApplicate());
    }

    // ── esito, durata e migrazioni facoltative (blocco A2) ────────────────────

    /// <summary>
    /// Il fallimento deve restare <b>scritto nel database</b>, non solo in un log che si ruota
    /// dopo 30 giorni: chi apre il registro il giorno dopo deve leggere quale migrazione si è
    /// rotta e perché. La riga c'è, ma con <c>success = 0</c>: non è applicata.
    /// </summary>
    [FactRichiedeMySql]
    public void MigrazioneFallita_lasciaLErroreScrittoNelRegistro()
    {
        using var db = new DatabaseDiProva("runner_errore");
        db.CreaSoloRegistroMigrazioni();

        using MySqlConnection c = db.Apri();
        HashSet<int> applicate = new(db.VersioniApplicate());

        Assert.Throws<InvalidOperationException>(() => Runner(
            new Finta(905, _ => throw new InvalidOperationException("il guasto raccontato per esteso")))
            .Applica(c, applicate, stopOnError: true));

        DatabaseDiProva.RigaMigrazione? riga = db.Riga(905);
        Assert.NotNull(riga);
        Assert.False(riga!.Riuscita);
        Assert.Contains("il guasto raccontato per esteso", riga.Errore);
        Assert.NotNull(riga.DurataMs);
        // …e resta pendente: una riga di fallimento non deve valere come «già fatta».
        Assert.DoesNotContain(905, db.VersioniApplicate());
    }

    /// <summary>
    /// Il ritentativo dopo la riparazione deve <b>sovrascrivere</b> la riga del fallimento. Con
    /// un <c>INSERT IGNORE</c> la riga vecchia resterebbe lì e quella versione risulterebbe rotta
    /// per sempre: l'avvio si fermerebbe ad ogni riavvio anche a migrazione ormai riuscita.
    /// </summary>
    [FactRichiedeMySql]
    public void MigrazioneRiparata_sovrascriveLaRigaDelFallimento()
    {
        using var db = new DatabaseDiProva("runner_riparata");
        db.CreaSoloRegistroMigrazioni();

        using MySqlConnection c = db.Apri();
        HashSet<int> applicate = new(db.VersioniApplicate());

        Assert.Throws<InvalidOperationException>(() => Runner(
            new Finta(906, _ => throw new InvalidOperationException("guasto finto")))
            .Applica(c, applicate, stopOnError: true));
        Assert.False(db.Riga(906)!.Riuscita);

        // stesso numero di versione, adesso funziona
        Runner(new Finta(906)).Applica(c, applicate, stopOnError: true);

        DatabaseDiProva.RigaMigrazione? riga = db.Riga(906);
        Assert.True(riga!.Riuscita);
        Assert.True(string.IsNullOrEmpty(riga.Errore));
        Assert.Contains(906, db.VersioniApplicate());
    }

    /// <summary>Ogni migrazione registra quanto ci ha messo: serve a vedere quali stanno diventando lente.</summary>
    [FactRichiedeMySql]
    public void MigrazioneRiuscita_registraLaDurata()
    {
        using var db = new DatabaseDiProva("runner_durata");
        db.CreaSoloRegistroMigrazioni();

        using MySqlConnection c = db.Apri();
        HashSet<int> applicate = new(db.VersioniApplicate());

        Runner(new Finta(907, _ => Thread.Sleep(15))).Applica(c, applicate, stopOnError: true);

        DatabaseDiProva.RigaMigrazione? riga = db.Riga(907);
        Assert.True(riga!.Riuscita);
        Assert.NotNull(riga.DurataMs);
        Assert.True(riga.DurataMs >= 10, $"Durata registrata {riga.DurataMs} ms: doveva essere almeno 10.");
    }

    /// <summary>
    /// Una PULIZIA che fallisce non deve tenere ferma l'azienda: l'avvio prosegue anche con
    /// <c>StopOnError = true</c>, la versione resta pendente e il motivo resta scritto.
    /// </summary>
    [FactRichiedeMySql]
    public void MigrazioneFacoltativaFallita_nonFermaLAvvio()
    {
        using var db = new DatabaseDiProva("runner_facoltativa");
        db.CreaSoloRegistroMigrazioni();

        using MySqlConnection c = db.Apri();
        HashSet<int> applicate = new(db.VersioniApplicate());

        bool successivaHaLavorato = false;
        EsitoMigrazioni esito = Runner(
                new Finta(908, _ => throw new InvalidOperationException("pulizia non riuscita"), facoltativa: true),
                new Finta(909, _ => successivaHaLavorato = true))
            .Applica(c, applicate, stopOnError: true);

        Assert.Equal(1, esito.Applicate);
        Assert.True(successivaHaLavorato);
        Assert.Equal(new[] { 908 }, esito.FacoltativeFallite);
        Assert.DoesNotContain(908, db.VersioniApplicate());
        Assert.Contains("pulizia non riuscita", db.Riga(908)!.Errore);
    }

    /// <summary>
    /// Una migrazione NON facoltativa che fallisce deve continuare a fermare l'avvio: il
    /// meccanismo delle pulizie non deve allentare la regola per tutte le altre.
    /// </summary>
    [FactRichiedeMySql]
    public void MigrazioneNormaleFallita_fermaLAvvioAncheConLeFacoltativeInGiro()
    {
        using var db = new DatabaseDiProva("runner_non_facoltativa");
        db.CreaSoloRegistroMigrazioni();

        using MySqlConnection c = db.Apri();
        HashSet<int> applicate = new(db.VersioniApplicate());

        var errore = Assert.Throws<InvalidOperationException>(() => Runner(
                new Finta(910, _ => throw new InvalidOperationException("guasto strutturale")))
            .Applica(c, applicate, stopOnError: true));

        Assert.Contains("v910", errore.Message);
    }

    /// <summary>
    /// Le pulizie marcate facoltative nel progetto vero: l'elenco è una decisione presa a mano
    /// (blocco A2), e questo test la rende visibile. Se domani se ne aggiunge una, va aggiunta
    /// anche qui — dopo aver verificato che nessuna migrazione successiva dipenda da lei.
    /// <para>La v94 (#85) è l'ottava: ritira le righe di permesso dell'import SAL, che non ha
    /// più né pagina né endpoint. Se salta, resta una voce morta nell'elenco delle funzioni.</para>
    /// </summary>
    [Fact]
    public void LeMigrazioniFacoltative_sonoSoloQuelleDecise()
    {
        MigrationRunner reale = new(NullLogger.Instance);

        int[] facoltative = reale.Migrazioni.Where(m => m.Facoltativa)
            .Select(m => m.Versione).OrderBy(v => v).ToArray();

        Assert.Equal(new[] { 22, 24, 31, 32, 46, 47, 65, 94 }, facoltative);
    }
}
