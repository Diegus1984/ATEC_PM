using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Segnalazione #114 — la card «DDP Commesse» della Dashboard elenca le distinte aggiornate
/// <b>dai colleghi</b> e le lascia andare quando chi guarda le ha aperte.
///
/// <para>Sono due regole che nessuna schermata mostra mentre sbaglia:</para>
/// <list type="bullet">
/// <item>senza il filtro sull'autore l'elenco rimanda indietro <b>il lavoro di chi lo sta
/// guardando</b>: si apre la DDP, si trova quello che si è appena scritto, e dopo due volte
/// la card diventa rumore che nessuno legge più;</item>
/// <item>la presa visione non deve <b>chiudere</b> niente: se sparisse per sempre, la modifica
/// che un collega fa cinque minuti dopo non la vedrebbe più nessuno. Sparisce finché quella
/// distinta resta ferma, e torna appena viene toccata di nuovo.</item>
/// </list>
///
/// <para>La query NON si ricopia qui dentro: si esegue quella del controller
/// (<see cref="ATEC.PM.Server.Controllers.DdpManagerController.AggiornamentiDaVerificareSql"/>),
/// altrimenti il test sorveglia una copia e non la regola.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class DdpDaVerificareTests
{
    private const string Query =
        ATEC.PM.Server.Controllers.DdpManagerController.AggiornamentiDaVerificareSql;

    private readonly SchemaCondiviso _schema;

    /// <summary>
    /// xUnit costruisce una istanza per ogni test: qui si riporta il database condiviso a
    /// com'era appena creato (~45 ms), invece di costruirne uno nuovo (~5 s).
    /// </summary>
    public DdpDaVerificareTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    /// <summary>Il dipendente che sta guardando la Dashboard: lo semina il test, non lo si dà per id 1
    /// (lo schema nuovo nasce già con l'utenza di servizio dentro).</summary>
    private int _io;

    [FactRichiedeMySql]
    public void LaDistintaToccataDaUnCollega_finisceNellElenco()
    {
        using MySqlConnection c = _schema.Apri();
        var (commessa, collega) = SeminaCommessa(c);

        RigaCommerciale(c, commessa, autore: collega);

        List<DdpUpdatedItem> elenco = Elenco(c);

        DdpUpdatedItem voce = Assert.Single(elenco);
        Assert.Equal("COMMERCIAL", voce.DdpType);
        Assert.Equal("C20260819.114", voce.Code);
        Assert.Equal("Commessa di prova", voce.Title);      // il nome commessa richiesto dalla #114
        Assert.Equal("Anna Bianchi", voce.UpdatedBy);
    }

    /// <summary>
    /// «DDP aggiornate <b>da colleghi</b>»: quello che ho scritto io non me lo ritrovo da
    /// verificare. Vale anche quando la riga l'ho solo modificata — l'autore che conta è
    /// l'ultima firma, non chi l'aveva inserita.
    /// </summary>
    [FactRichiedeMySql]
    public void LeMieModifiche_nonMiTornanoIndietro()
    {
        using MySqlConnection c = _schema.Apri();
        var (commessa, collega) = SeminaCommessa(c);

        // Inserita dal collega, ma l'ultima mano sopra è la mia.
        RigaCommerciale(c, commessa, autore: collega, ultimaMano: _io);

        Assert.Empty(Elenco(c));
    }

    /// <summary>
    /// Una riga senza firma resta nell'elenco: nessuno l'ha rivendicata (righe storiche o
    /// passaggi fatti dal programma), quindi è lavoro che va comunque guardato.
    /// </summary>
    [FactRichiedeMySql]
    public void LaModificaSenzaFirma_restaDaVerificare()
    {
        using MySqlConnection c = _schema.Apri();
        var (commessa, _) = SeminaCommessa(c);

        RigaCommerciale(c, commessa, autore: null);

        DdpUpdatedItem voce = Assert.Single(Elenco(c));
        Assert.Equal("", voce.UpdatedBy);
    }

    /// <summary>Aprire la DDP nel Gestore la toglie dall'elenco di chi l'ha aperta.</summary>
    [FactRichiedeMySql]
    public void DopoLaPresaVisione_laVoceSparisce()
    {
        using MySqlConnection c = _schema.Apri();
        var (commessa, collega) = SeminaCommessa(c);

        RigaCommerciale(c, commessa, autore: collega);
        PresaVisione(c, commessa, "COMMERCIAL", minutiFa: 0);

        Assert.Empty(Elenco(c));
    }

    /// <summary>
    /// …ma non la chiude: la modifica arrivata <b>dopo</b> l'ultima occhiata rimette la voce
    /// nell'elenco. È la differenza fra «ho visto» e «è a posto».
    /// </summary>
    [FactRichiedeMySql]
    public void SeIlCollegaLaTocca_dopoCheLHoVista_laVoceTorna()
    {
        using MySqlConnection c = _schema.Apri();
        var (commessa, collega) = SeminaCommessa(c);

        PresaVisione(c, commessa, "COMMERCIAL", minutiFa: 30);
        RigaCommerciale(c, commessa, autore: collega);   // toccata adesso, dopo la mia occhiata

        Assert.Single(Elenco(c));
    }

    /// <summary>
    /// La presa visione è <b>per persona</b> e <b>per tipo di distinta</b>: aver guardato la
    /// commerciale non spegne l'avviso dell'officina, che si apre da un'altra pagina.
    /// </summary>
    [FactRichiedeMySql]
    public void LaPresaVisioneDellaCommerciale_nonSpegneLOfficina()
    {
        using MySqlConnection c = _schema.Apri();
        var (commessa, collega) = SeminaCommessa(c);

        RigaCommerciale(c, commessa, autore: collega);
        RigaOfficina(c, commessa, autore: collega);
        PresaVisione(c, commessa, "COMMERCIAL", minutiFa: 0);

        DdpUpdatedItem voce = Assert.Single(Elenco(c));
        Assert.Equal("OFFICINA", voce.DdpType);
    }

    /// <summary>
    /// Anche il solo cambio di stato registrato in cronistoria è un aggiornamento da vedere:
    /// la riga non cambia di una virgola, ma il materiale ora è ordinato (o consegnato).
    /// </summary>
    [FactRichiedeMySql]
    public void IlCambioDiStatoDelCollega_valeComeAggiornamento()
    {
        using MySqlConnection c = _schema.Apri();
        var (commessa, collega) = SeminaCommessa(c);

        // La riga è mia (non deve comparire da sola), il cambio di stato è del collega.
        int riga = RigaOfficina(c, commessa, autore: _io);
        c.Execute(@"
            INSERT INTO ddp_item_events
                (item_type, item_id, project_id, from_status, to_status, changed_at, changed_by_id, changed_by_name)
            VALUES ('OFFICINA', @R, @P, 'DO', 'IO', NOW(), @Chi, 'Anna Bianchi')",
            new { R = riga, P = commessa, Chi = collega });

        DdpUpdatedItem voce = Assert.Single(Elenco(c));
        Assert.Equal("OFFICINA", voce.DdpType);
        Assert.Equal("Anna Bianchi", voce.UpdatedBy);
    }

    /// <summary>
    /// Presa visione di una commessa che non esiste: la riga non si scrive e basta.
    /// Con l'INSERT … VALUES la foreign key faceva saltare la chiamata, e il client si
    /// prendeva un **500** (visto in produzione il 19/08/2026, con un id sbagliato).
    /// </summary>
    [FactRichiedeMySql]
    public void LaPresaVisioneDiUnaCommessaCheNonEsiste_nonEsplode()
    {
        using MySqlConnection c = _schema.Apri();
        var (commessa, _) = SeminaCommessa(c);

        int scritte = c.Execute(SqlPresaVisione,
            new { Me = _io, P = commessa + 99_000, T = "COMMERCIAL", M = 0 });

        Assert.Equal(0, scritte);
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM ddp_review_acks"));
    }

    /// <summary>Fuori dalla finestra dei giorni non si guarda: la card parla dell'ultima settimana.</summary>
    [FactRichiedeMySql]
    public void LaModificaVecchia_nonEntraNellaFinestra()
    {
        using MySqlConnection c = _schema.Apri();
        var (commessa, collega) = SeminaCommessa(c);

        RigaCommerciale(c, commessa, autore: collega, giorniFa: 30);

        Assert.Empty(Elenco(c));
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private List<DdpUpdatedItem> Elenco(MySqlConnection c, int giorni = 7) =>
        c.Query<DdpUpdatedItem>(
            // Nessun filtro bozze: il test guarda la regola degli aggiornamenti, non i permessi.
            string.Format(Query, ""),
            new { Me = _io, Days = giorni }).ToList();

    /// <summary>Commessa attiva + due persone: io (id 1) e il collega. Ritorna (commessa, collega).</summary>
    private (int Commessa, int Collega) SeminaCommessa(MySqlConnection c)
    {
        int cliente = Inserisci(c, "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        _io = Inserisci(c, "INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        int collega = Inserisci(c, "INSERT INTO employees (first_name, last_name) VALUES ('Anna', 'Bianchi')");

        int commessa = Inserisci(c,
            @"INSERT INTO projects (code, title, customer_id, pm_id, status)
              VALUES ('C20260819.114', 'Commessa di prova', @Cliente, @Pm, 'ACTIVE')",
            new { Cliente = cliente, Pm = _io });

        return (commessa, collega);
    }

    private static int RigaCommerciale(
        MySqlConnection c, int commessa, int? autore, int? ultimaMano = null, int giorniFa = 0) =>
        Inserisci(c, @"
            INSERT INTO bom_items
                (project_id, ddp_type, part_number, description, quantity, item_status,
                 created_by, updated_by, created_at, updated_at)
            VALUES (@P, 'COMMERCIAL', '101.0001', 'Cuscinetto', 2, 'DO',
                    @Autore, @Ultima, DATE_SUB(NOW(), INTERVAL @G DAY), DATE_SUB(NOW(), INTERVAL @G DAY))",
            new { P = commessa, Autore = autore, Ultima = ultimaMano, G = giorniFa });

    private static int RigaOfficina(MySqlConnection c, int commessa, int? autore, int giorniFa = 0) =>
        Inserisci(c, @"
            INSERT INTO ddp_officina_items
                (project_id, part_number, description, quantity, item_status,
                 created_by, created_at, updated_at)
            VALUES (@P, '201.0001', 'Piastra', 1, 'DO',
                    @Autore, DATE_SUB(NOW(), INTERVAL @G DAY), DATE_SUB(NOW(), INTERVAL @G DAY))",
            new { P = commessa, Autore = autore, G = giorniFa });

    /// <summary>
    /// La stessa INSERT del controller, con la data all'indietro per simulare «l'ho vista
    /// mezz'ora fa». La forma — INSERT … SELECT dalla commessa — è quella che regge un id
    /// inesistente senza far esplodere la chiamata.
    /// </summary>
    private const string SqlPresaVisione = @"
        INSERT INTO ddp_review_acks (employee_id, project_id, ddp_type, seen_at)
        SELECT @Me, p.id, @T, DATE_SUB(NOW(), INTERVAL @M MINUTE) FROM projects p WHERE p.id = @P
        ON DUPLICATE KEY UPDATE seen_at = VALUES(seen_at)";

    private void PresaVisione(MySqlConnection c, int commessa, string tipo, int minutiFa) =>
        c.Execute(SqlPresaVisione, new { Me = _io, P = commessa, T = tipo, M = minutiFa });

    private static int Inserisci(MySqlConnection c, string sql, object? param = null)
    {
        c.Execute(sql, param);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }
}
