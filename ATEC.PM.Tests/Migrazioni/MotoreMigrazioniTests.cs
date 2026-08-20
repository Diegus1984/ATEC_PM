using ATEC.PM.Server.Migrations;
using ATEC.PM.Server.Services;
using ATEC.PM.Tests.Infrastruttura;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATEC.PM.Tests.Migrazioni;

/// <summary>
/// Il motore delle migrazioni (<c>DbService.InitDatabase</c> → <c>ApplyVersionedMigrations</c>).
///
/// <para><b>Perché questi test esistono.</b> Fino al 14/08/2026 una migrazione veniva saltata se
/// <c>MAX(version)</c> era già oltre il suo numero, e ogni fallimento era un warning che lasciava
/// proseguire l'avvio. Bastava che la vN fallisse e la vN+1 riuscisse perché la vN non venisse
/// <b>mai più</b> ritentata: schema a metà e nessuno che se ne accorgesse. Nei log del server la
/// v75 e la v80 sono fallite davvero (08/08/2026) e si sono salvate solo perché in quei minuti
/// non è passata nessuna migrazione successiva.</para>
///
/// <para>Ogni test lavora su un database <b>usa-e-getta</b>, creato e distrutto qui dentro:
/// <c>atec_pm</c> non viene toccato.</para>
/// </summary>
public class MotoreMigrazioniTests
{
    /// <summary>
    /// L'ultima migrazione scritta. <b>Non è un numero da tenere aggiornato</b>: lo chiede al
    /// <see cref="MigrationRunner"/>, che scopre le migrazioni dall'assembly. Fino al 15/08/2026
    /// era una costante, ed è bastata la v88 di un'altra sessione per far diventare rossi due
    /// test che non c'entravano niente.
    /// </summary>
    private static readonly int UltimaVersione =
        new MigrationRunner(NullLogger.Instance).VersioneMassima;

    [FactRichiedeMySql]
    public void DatabaseVuoto_creaLoSchemaCompletoERegistraTutteLeVersioni()
    {
        using var db = new DatabaseDiProva("vuoto");

        db.CreaSchemaCompleto();

        List<int> versioni = db.VersioniApplicate();
        int[] mancanti = Enumerable.Range(1, UltimaVersione).Where(v => !versioni.Contains(v)).ToArray();

        Assert.Empty(mancanti);
        Assert.Equal(UltimaVersione, versioni.Max());
        Assert.True(db.ContaTabelle() > 80, $"Solo {db.ContaTabelle()} tabelle: lo schema non è completo.");
    }

    /// <summary>
    /// Il difetto storico, riprodotto: buchi SOTTO il massimo. Con la vecchia regola quelle
    /// versioni erano perse per sempre. Ora vengono <b>sanate senza essere rieseguite</b>: 16 delle
    /// 87 migrazioni farebbero danni a rigirare (la v22 riporta l'IVA a 22% sulle righe SAL messe a
    /// esente, la v26 fa risorgere lavorazioni cancellate, la v55 duplica la cronistoria DDP).
    /// </summary>
    [FactRichiedeMySql]
    public void BuchiSottoIlMassimo_vengonoSanatiMaNonRieseguiti()
    {
        using var db = new DatabaseDiProva("buchi");
        db.CreaSchemaCompleto();
        db.Esegui("DELETE FROM schema_migrations WHERE version IN (12, 85)");

        db.Servizio().InitDatabase(productionMode: true);

        List<int> versioni = db.VersioniApplicate();
        Assert.Contains(12, versioni);
        Assert.Contains(85, versioni);
        Assert.StartsWith("backfill", db.Descrizione(12));
        Assert.StartsWith("backfill", db.Descrizione(85));
    }

    /// <summary>Il caso del deploy vero: la produzione era a v80, le 81…87 devono girare davvero.</summary>
    [FactRichiedeMySql]
    public void DatabaseIndietro_applicaLeVersioniMancantiPerDavvero()
    {
        using var db = new DatabaseDiProva("indietro");
        db.CreaSchemaCompleto();
        db.Esegui("DELETE FROM schema_migrations WHERE version > 80");

        db.Servizio().InitDatabase(productionMode: true);

        List<int> versioni = db.VersioniApplicate();
        for (int v = 81; v <= UltimaVersione; v++)
        {
            Assert.Contains(v, versioni);
            Assert.False(db.Descrizione(v).StartsWith("backfill"),
                $"La v{v} doveva essere ESEGUITA, non timbrata: sopra il massimo non si fa backfill.");
        }
    }

    /// <summary>
    /// Una migrazione che fallisce deve fermare l'avvio: un server che parte con lo schema a metà
    /// continua a lavorare e a scrivere dati, e il guaio si scopre dai numeri sbagliati.
    /// </summary>
    [FactRichiedeMySql]
    public void MigrazioneFallita_interrompeLAvvio()
    {
        using var db = new DatabaseDiProva("rotta");
        db.CreaSchemaCompleto();
        // La v81 aggiunge bom_items.created_by: senza quella tabella non può riuscire.
        db.Esegui("DELETE FROM schema_migrations WHERE version >= 81");
        db.Esegui("RENAME TABLE bom_items TO bom_items_nascosta");

        Exception? errore = Record.Exception(() => db.Servizio().InitDatabase(productionMode: true));

        Assert.NotNull(errore);
        Assert.Contains("v81", errore!.Message);
    }

    /// <summary>La via d'uscita d'emergenza (<c>Migrations:StopOnError=false</c>) deve far ripartire il server.</summary>
    [FactRichiedeMySql]
    public void MigrazioneFallita_conStopOnErrorFalse_lasciaProseguire()
    {
        using var db = new DatabaseDiProva("rotta_tollerante");
        db.CreaSchemaCompleto();
        db.Esegui("DELETE FROM schema_migrations WHERE version >= 81");
        db.Esegui("RENAME TABLE bom_items TO bom_items_nascosta");

        Exception? errore = Record.Exception(
            () => db.Servizio(stopOnError: false).InitDatabase(productionMode: true));

        Assert.Null(errore);
        // …e la versione fallita resta pendente, così al prossimo avvio viene ritentata.
        Assert.DoesNotContain(81, db.VersioniApplicate());
    }

    /// <summary>
    /// Registro azzerato ma dati veri presenti: è quello che lascia un ripristino da backup
    /// interrotto a metà (<c>FullBackupService.RipristinaDatabase</c> fa TRUNCATE anche di
    /// <c>schema_migrations</c>). Rieseguire le 87 migrazioni su dati di produzione li rovinerebbe:
    /// il server deve fermarsi.
    /// </summary>
    [FactRichiedeMySql]
    public void RegistroAzzeratoSuDatabasePopolato_nonRieseguiLeMigrazioni()
    {
        using var db = new DatabaseDiProva("registro_perso");
        db.CreaSchemaCompleto();
        db.Esegui("DELETE FROM schema_migrations");

        Exception? errore = Record.Exception(() => db.Servizio().InitDatabase(productionMode: true));

        Assert.NotNull(errore);
        Assert.Contains("schema_migrations", errore!.Message);
    }

    /// <summary>
    /// Il backfill non deve mai toccare le versioni FUTURE: se domani la v88 fallisce e la v89
    /// passa, timbrare la v88 come applicata rimetterebbe in circolo esattamente il difetto che
    /// è stato chiuso.
    /// </summary>
    [FactRichiedeMySql]
    public void VersioneFuturaMancante_nonVieneMaiTimbrata()
    {
        // Due numeri OLTRE l'ultima migrazione scritta: quello più alto risulta riuscito, quello
        // in mezzo è il buco che non deve essere timbrato.
        int buco = UltimaVersione + 1, riuscita = UltimaVersione + 2;

        using var db = new DatabaseDiProva("futuro");
        db.CreaSchemaCompleto();
        db.Esegui($"INSERT INTO schema_migrations (version, description) VALUES ({riuscita}, 'migrazione futura riuscita')");

        db.Servizio().InitDatabase(productionMode: true);

        Assert.DoesNotContain(buco, db.VersioniApplicate());
    }

    /// <summary>Rilanciare l'avvio su uno schema già aggiornato non deve cambiare niente.</summary>
    [FactRichiedeMySql]
    public void AvvioRipetuto_nonCambiaNulla()
    {
        using var db = new DatabaseDiProva("idempotente");
        db.CreaSchemaCompleto();
        List<int> prima = db.VersioniApplicate();

        db.Servizio().InitDatabase(productionMode: true);
        db.Servizio().InitDatabase(productionMode: true);

        Assert.Equal(prima, db.VersioniApplicate());
    }

    /// <summary>
    /// Anti-regressione della v69: la vista del consuntivo ore deve contenere anche le ore delle
    /// <b>fasi locali</b> (quelle create dalla commessa, senza template). In produzione girava una
    /// versione della vista con una JOIN INNER che le buttava via: quelle ore sparivano dal costo
    /// consuntivo del Bilancio senza nessun errore.
    /// </summary>
    [FactRichiedeMySql]
    public void VistaTimesheet_tieneLeOreDelleFasiLocali()
    {
        using var db = new DatabaseDiProva("vista");
        db.CreaSchemaCompleto();

        using MySqlConnector.MySqlConnection c = db.Apri();

        int clienteId = Inserisci(c,
            "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int personaId = Inserisci(c,
            "INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        int commessaId = Inserisci(c,
            @"INSERT INTO projects (code, title, customer_id, pm_id)
              VALUES ('C20260814.999', 'Commessa di prova', @Cliente, @Pm)",
            new { Cliente = clienteId, Pm = personaId });

        // LA FASE LOCALE: nata dalla commessa, non da un template → phase_template_id resta NULL.
        int faseId = Inserisci(c,
            @"INSERT INTO project_phases (project_id, name, phase_template_id)
              VALUES (@Commessa, 'Montaggio in cantiere', NULL)",
            new { Commessa = commessaId });

        int oreId = Inserisci(c,
            @"INSERT INTO timesheet_entries (employee_id, project_phase_id, work_date, hours)
              VALUES (@Persona, @Fase, '2026-08-14', 7.5)",
            new { Persona = personaId, Fase = faseId });

        var riga = Dapper.SqlMapper.QueryFirstOrDefault<(int ProjectId, string PhaseName, decimal Hours)>(c,
            @"SELECT project_id AS ProjectId, phase_name AS PhaseName, hours AS Hours
              FROM v_timesheet_with_section WHERE entry_id = @Id", new { Id = oreId });

        // Se la JOIN su phase_templates tornasse INNER (com'era in produzione fino alla v69),
        // questa riga sparirebbe dalla vista — e con lei le ore dal consuntivo del Bilancio,
        // senza nessun errore da nessuna parte.
        Assert.Equal(commessaId, riga.ProjectId);
        Assert.Equal("Montaggio in cantiere", riga.PhaseName);
        Assert.Equal(7.5m, riga.Hours);
    }

    // ── il lock e le viste (blocco A2) ────────────────────────────────────────

    /// <summary>
    /// Due processi che migrano lo stesso database insieme farebbero girare la stessa migrazione
    /// due volte, e il DDL di MySQL non torna indietro. Se il lock è già in mano a qualcun altro,
    /// l'avvio si interrompe con un messaggio che dice cosa cercare.
    /// </summary>
    [FactRichiedeMySql]
    public void LockGiaPreso_fermaLAvvioInveceDiMigrareInDue()
    {
        using var db = new DatabaseDiProva("lock_occupato");
        db.CreaSchemaCompleto();

        // «L'altro processo»: una seconda sessione MySQL che tiene il lock.
        using MySqlConnector.MySqlConnection altro = db.Apri();
        int? preso = Dapper.SqlMapper.ExecuteScalar<int?>(altro,
            "SELECT GET_LOCK(@N, 0)", new { N = DbService.NomeLockMigrazioni(db.Nome) });
        Assert.Equal(1, preso);

        try
        {
            // Un secondo di attesa e basta: il test non deve durare quanto il timeout vero.
            Exception? errore = Record.Exception(
                () => db.Servizio(lockTimeoutSeconds: 1).InitDatabase(productionMode: true));

            Assert.NotNull(errore);
            Assert.Contains("lock", errore!.Message);
        }
        finally
        {
            Dapper.SqlMapper.ExecuteScalar<int?>(altro,
                "SELECT RELEASE_LOCK(@N)", new { N = DbService.NomeLockMigrazioni(db.Nome) });
        }
    }

    /// <summary>
    /// Il lock viene <b>rilasciato</b>: due avvii di fila devono passare tutti e due. Se restasse
    /// appeso alla connessione tornata nel pool, il secondo avvio si fermerebbe da solo.
    /// </summary>
    [FactRichiedeMySql]
    public void LockRilasciato_ilSecondoAvvioPassa()
    {
        using var db = new DatabaseDiProva("lock_rilasciato");
        db.CreaSchemaCompleto();

        Exception? errore = Record.Exception(() => db.Servizio().InitDatabase(productionMode: true));

        Assert.Null(errore);
    }

    /// <summary>
    /// La vista del consuntivo ore viene riallineata a OGNI avvio, anche in produzione su un
    /// database già aggiornato. Fino al 15/08/2026 la creava solo una migrazione: su un database
    /// dove quelle versioni risultavano già applicate (o erano state timbrate dal backfill) la
    /// definizione restava quella vecchia, e il Bilancio contava le ore con la regola sbagliata
    /// senza dare nessun errore. È anche il caso del ripristino da backup: i pacchetti contengono
    /// solo le TABELLE, la vista no.
    /// </summary>
    [FactRichiedeMySql]
    public void VistaCancellata_vieneRicreataAlPrimoAvvio()
    {
        using var db = new DatabaseDiProva("vista_ricreata");
        db.CreaSchemaCompleto();

        db.Esegui("DROP VIEW IF EXISTS v_timesheet_with_section");
        Assert.Equal(0, ContaVista(db));

        // Ramo PRODUZIONE su database già popolato: è quello che prima usciva prima di toccare
        // le viste, ed è dove il difetto è vissuto per mesi.
        db.Servizio().InitDatabase(productionMode: true);

        Assert.Equal(1, ContaVista(db));
    }

    /// <summary>
    /// Una vista rimasta indietro (per esempio la definizione pre-v69, che con la JOIN INNER
    /// buttava via le ore delle fasi locali) deve essere <b>sostituita</b>, non lasciata stare.
    /// </summary>
    [FactRichiedeMySql]
    public void VistaVecchia_vieneSostituitaDallaDefinizioneDiOggi()
    {
        using var db = new DatabaseDiProva("vista_vecchia");
        db.CreaSchemaCompleto();

        // La forma sbagliata di una volta: due colonne in croce e la JOIN INNER sui template.
        db.Esegui(@"CREATE OR REPLACE VIEW v_timesheet_with_section AS
            SELECT te.id AS entry_id, te.employee_id, te.hours
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id");

        db.Servizio().InitDatabase(productionMode: true);

        using MySqlConnector.MySqlConnection c = db.Apri();
        int colonne = Dapper.SqlMapper.ExecuteScalar<int>(c, @"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'v_timesheet_with_section'
              AND column_name IN ('is_extra', 'counts_in_project', 'cost_section_template_id')");

        Assert.Equal(3, colonne);
    }

    private static int ContaVista(DatabaseDiProva db)
    {
        using MySqlConnector.MySqlConnection c = db.Apri();
        return Dapper.SqlMapper.ExecuteScalar<int>(c, @"
            SELECT COUNT(*) FROM information_schema.views
            WHERE table_schema = DATABASE() AND table_name = 'v_timesheet_with_section'");
    }

    private static int Inserisci(MySqlConnector.MySqlConnection c, string sql, object? parametri = null)
    {
        Dapper.SqlMapper.Execute(c, sql, parametri);
        return Dapper.SqlMapper.ExecuteScalar<int>(c, "SELECT LAST_INSERT_ID()");
    }
}
