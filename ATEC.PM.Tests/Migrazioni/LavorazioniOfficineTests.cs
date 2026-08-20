using ATEC.PM.Server.Migrations;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Migrazioni;

/// <summary>
/// Segnalazione #83 — «Lavorazioni Officine» smette di copiare le righe di distinta e le guarda
/// dove stanno.
///
/// <para>I due guasti che questi test tengono fermi non si vedono da nessuna schermata:</para>
/// <list type="bullet">
/// <item>una riga senza <c>work_type</c> <b>non compare in nessuna delle due viste principali</b>.
/// Il gestionale funziona, la DDP è a posto, e il pezzo da costruire semplicemente non è dove
/// lo si va a cercare. Il Tipo veniva congelato solo dalla v63 in poi: tutto il pregresso lo ha
/// vuoto, e senza il backfill della v92 la pagina nuova nascerebbe mezza cieca.</item>
/// <item>la v92 <b>cancella</b> le bozze del vecchio pannello. Se il travaso di note e urgenze
/// non gira prima della DELETE, quello che le persone hanno scritto sparisce senza un errore.</item>
/// </list>
/// </summary>
public class LavorazioniOfficineTests
{
    // Il filtro delle viste NON si ricopia: si prende dal controller e si esegue quello.
    private const string FiltroViste =
        ATEC.PM.Server.Controllers.WorkRequestsController.RigheDdpFiltro;

    // ── 1. Il backfill del Tipo ───────────────────────────────────────────────

    /// <summary>
    /// Lo stato dice la natura del lavoro quasi sempre: DC/COS si costruisce, DO/RO/IO/CON si
    /// compra. Le righe che ce l'hanno già scritto non si toccano — anche quando lo stato
    /// direbbe il contrario, perché quella è una scelta fatta a mano nella colonna Tipo.
    /// </summary>
    [FactRichiedeMySql]
    public void IlTipoMancante_siRicostruisceDalloStato()
    {
        using var db = new DatabaseDiProva("v92_tipo_stato");
        db.CreaSchemaCompleto();
        using MySqlConnection c = db.Apri();
        int commessa = SeminaCommessa(c);

        int daCostruire = RigaOfficina(c, commessa, stato: "DC");
        int daOrdinare = RigaOfficina(c, commessa, stato: "DO");
        int giaScelta = RigaOfficina(c, commessa, stato: "DC", workType: "External");

        Applica(c);

        Assert.Equal("Internal", TipoDi(c, daCostruire));
        Assert.Equal("External", TipoDi(c, daOrdinare));
        Assert.Equal("External", TipoDi(c, giaScelta));
    }

    /// <summary>
    /// PAR e MIT non dicono se il pezzo si costruisce o si compra: lo dice l'ultimo stato
    /// attraversato che lo diceva. Sono proprio le righe <b>in corso</b> — quelle che devono
    /// comparire nella pagina oggi, non domani.
    /// </summary>
    [FactRichiedeMySql]
    public void PerLeRigheInLavorazione_ilTipoVieneDallaCronistoria()
    {
        using var db = new DatabaseDiProva("v92_tipo_storia");
        db.CreaSchemaCompleto();
        using MySqlConnection c = db.Apri();
        int commessa = SeminaCommessa(c);

        int parziale = RigaOfficina(c, commessa, stato: "PAR");
        Evento(c, parziale, commessa, "DO", "DC", giorniFa: 5);   // prima era da ordinare…
        Evento(c, parziale, commessa, "DC", "PAR", giorniFa: 1);  // …poi si è deciso di farla in casa

        int inTrattamento = RigaOfficina(c, commessa, stato: "MIT");
        Evento(c, inTrattamento, commessa, "DC", "MIT", giorniFa: 2);

        // Nessuna cronistoria: resta il fornitore a dire che il pezzo è stato comprato.
        int soloFornitore = RigaOfficina(c, commessa, stato: "PAR", fornitore: "Officina Bianchi");

        // Premessa del test: la cronistoria dice davvero quello che diamo per scontato.
        Assert.Equal("DC", c.ExecuteScalar<string>(@"
            SELECT ev.to_status FROM ddp_item_events ev
            WHERE ev.item_type = 'OFFICINA' AND ev.item_id = @R
              AND UPPER(TRIM(COALESCE(ev.to_status,''))) IN ('DC','COS','DO','RO','IO','CON')
            ORDER BY ev.changed_at DESC, ev.id DESC LIMIT 1", new { R = parziale }));

        Applica(c);

        Assert.Equal("Internal", TipoDi(c, parziale));
        Assert.Equal("Internal", TipoDi(c, inTrattamento));
        Assert.Equal("External", TipoDi(c, soloFornitore));
    }

    // ── 2. Il filtro delle viste, eseguito com'è in produzione ────────────────

    /// <summary>
    /// Le regole della segnalazione, parola per parola: interna da costruire e esterna da
    /// ordinare ci sono; il parziale resta; il materiale in trattamento c'è (lo raccoglie la
    /// vista Trattamenti); l'ordine già emesso (IO) e la riga chiusa (COS) escono.
    /// </summary>
    [FactRichiedeMySql]
    public void LeViste_prendonoSoloLeRigheCheLaSegnalazioneChiede()
    {
        using var db = new DatabaseDiProva("v92_viste");
        db.CreaSchemaCompleto();
        using MySqlConnection c = db.Apri();
        int commessa = SeminaCommessa(c);

        int internaDaFare = RigaOfficina(c, commessa, stato: "DC", workType: "Internal");
        int esternaDaOrdinare = RigaOfficina(c, commessa, stato: "DO", workType: "External");
        int parziale = RigaOfficina(c, commessa, stato: "PAR", workType: "Internal");
        int inTrattamento = RigaOfficina(c, commessa, stato: "MIT", workType: "Internal");
        int ordineEmesso = RigaOfficina(c, commessa, stato: "IO", workType: "External");
        int costruita = RigaOfficina(c, commessa, stato: "COS", workType: "Internal");

        List<int> visibili = c.Query<int>($@"
            SELECT o.id FROM ddp_officina_items o
            JOIN projects p ON p.id = o.project_id
            WHERE COALESCE(p.status,'') <> 'CANCELLED'
            {FiltroViste}").ToList();

        Assert.Contains(internaDaFare, visibili);
        Assert.Contains(esternaDaOrdinare, visibili);
        Assert.Contains(parziale, visibili);
        Assert.Contains(inTrattamento, visibili);
        Assert.DoesNotContain(ordineEmesso, visibili);
        Assert.DoesNotContain(costruita, visibili);
    }

    /// <summary>
    /// Segnalazione #87 — la Stampa 3D si costruisce in casa: entra nelle viste con gli stessi
    /// stati delle interne. È il difetto che la #83 aveva già insegnato a temere: un Tipo che il
    /// filtro non nomina non toglie la riga dalla DDP, la toglie <b>dalla pagina di chi la deve
    /// fare</b>, e non se ne accorge nessuno.
    /// </summary>
    [FactRichiedeMySql]
    public void LeRigheInStampa3D_stannoNelleViste_comeLeInterne()
    {
        using var db = new DatabaseDiProva("v95_stampa3d");
        db.CreaSchemaCompleto();
        using MySqlConnection c = db.Apri();
        int commessa = SeminaCommessa(c);

        int daStampare = RigaOfficina(c, commessa, stato: "DC", workType: "Print3D");
        int stampaParziale = RigaOfficina(c, commessa, stato: "PAR", workType: "Print3D");
        int stampaFinita = RigaOfficina(c, commessa, stato: "COS", workType: "Print3D");
        // Lo stato «da ordinare» non appartiene a un pezzo che si stampa in casa: se ci
        // finisce, è una riga classificata male e non deve comparire come lavoro da fare.
        int stampaDaOrdinare = RigaOfficina(c, commessa, stato: "DO", workType: "Print3D");

        List<int> visibili = c.Query<int>($@"
            SELECT o.id FROM ddp_officina_items o
            JOIN projects p ON p.id = o.project_id
            WHERE COALESCE(p.status,'') <> 'CANCELLED'
            {FiltroViste}").ToList();

        Assert.Contains(daStampare, visibili);
        Assert.Contains(stampaParziale, visibili);
        Assert.DoesNotContain(stampaFinita, visibili);
        Assert.DoesNotContain(stampaDaOrdinare, visibili);
    }

    /// <summary>
    /// La v95 ricostruisce la tariffa oraria dalle righe già calcolate — ma <b>solo</b> quando
    /// il rapporto costo ÷ ore cade esattamente su una tariffa d'anagrafica. Un numero
    /// inventato lì dentro sarebbe peggio del vuoto: la finestra del particolare lo mostrerebbe
    /// come se qualcuno l'avesse scelto, e la prima correzione alle ore rifarebbe il conto su
    /// quello.
    /// </summary>
    [FactRichiedeMySql]
    public void LaTariffaOraria_siRicostruisceSoloSeCoincideConUnaDiAnagrafica()
    {
        using var db = new DatabaseDiProva("v95_tariffe");
        db.CreaSchemaCompleto();
        using MySqlConnection c = db.Apri();
        int commessa = SeminaCommessa(c);

        int cinqueOreA50 = RigaOfficina(c, commessa, stato: "DC", workType: "Internal");
        c.Execute("UPDATE ddp_officina_items SET work_hours = 5, unit_cost = 250, hourly_rate = NULL WHERE id = @R",
            new { R = cinqueOreA50 });

        int costoFuoriTariffa = RigaOfficina(c, commessa, stato: "DC", workType: "Internal");
        c.Execute("UPDATE ddp_officina_items SET work_hours = 3, unit_cost = 100, hourly_rate = NULL WHERE id = @R",
            new { R = costoFuoriTariffa });

        int soloCostoAMano = RigaOfficina(c, commessa, stato: "DC", workType: "Internal");
        c.Execute("UPDATE ddp_officina_items SET work_hours = NULL, unit_cost = 90, hourly_rate = NULL WHERE id = @R",
            new { R = soloCostoAMano });

        new M095_Stampa3D().Applica(c, NullLogger.Instance);

        Assert.Equal(50m, TariffaDi(c, cinqueOreA50));
        Assert.Null(TariffaDi(c, costoFuoriTariffa));   // 100 ÷ 3 = 33,33 €/h: non è una tariffa
        Assert.Null(TariffaDi(c, soloCostoAMano));

        // Le tre tariffe della segnalazione, con il loro nome.
        Assert.Equal("Stampa 3D", c.ExecuteScalar<string>(
            "SELECT label FROM tariff_options WHERE tariff_type='HOURLY_RATE' AND value=20"));
        Assert.Equal("Meccanica", c.ExecuteScalar<string>(
            "SELECT label FROM tariff_options WHERE tariff_type='HOURLY_RATE' AND value=50"));
    }

    // ── 3. Quello che era scritto a mano non si perde ─────────────────────────

    /// <summary>
    /// La v92 cancella le bozze. Note e urgenze scritte nel vecchio pannello devono essere già
    /// sulla riga di distinta quando succede: sono l'unica cosa, in quelle bozze, che non fosse
    /// una copia di qualcos'altro.
    /// </summary>
    [FactRichiedeMySql]
    public void LeNoteEleUrgenzeDelVecchioPannello_finisconoSullaRigaDiDistinta()
    {
        using var db = new DatabaseDiProva("v92_travaso");
        db.CreaSchemaCompleto();
        using MySqlConnection c = db.Apri();
        int commessa = SeminaCommessa(c);
        int riga = RigaOfficina(c, commessa, stato: "DC");

        c.Execute(@"
            INSERT INTO project_work_requests
                (project_id, description, notes, is_ultra_critical, is_staging, ddp_officina_item_id, created_at)
            VALUES (@P, 'Bozza dalla distinta', 'Sentire Paolo per il grezzo', 1, 1, @R, 0)",
            new { P = commessa, R = riga });

        Applica(c);

        var dopo = c.QueryFirst<(string Note, bool Urgente)>(@"
            SELECT COALESCE(workshop_notes,'') AS Note, is_ultra_critical AS Urgente
            FROM ddp_officina_items WHERE id = @R", new { R = riga });

        Assert.Equal("Sentire Paolo per il grezzo", dopo.Note);
        Assert.True(dopo.Urgente, "La segnalazione ultra critica non è stata travasata.");
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM project_work_requests WHERE is_staging = 1"));
    }

    /// <summary>
    /// La riga battuta a mano può non avere commessa (#83): finché <c>project_id</c> è
    /// obbligatoria una riga così non si salva, e il messaggio d'errore parla di chiavi esterne.
    /// </summary>
    [FactRichiedeMySql]
    public void UnaRigaManuale_puoNonAvereCommessa()
    {
        using var db = new DatabaseDiProva("v92_manuale");
        db.CreaSchemaCompleto();
        using MySqlConnection c = db.Apri();

        c.Execute(@"
            INSERT INTO project_work_requests (project_id, description, part_number, quantity, created_at)
            VALUES (NULL, 'Staffa da rifare', '101.9999', 2, 0)");

        Assert.Equal(1, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_work_requests WHERE project_id IS NULL"));
    }

    // ── 4. Chi lavorava nella Inbox Officina non resta fuori ──────────────────

    /// <summary>
    /// La pagina Inbox Officina sparisce. Dalla v87 i permessi non hanno più un livello che
    /// rimedi da sé: senza il travaso, il responsabile officina troverebbe il menu vuoto al
    /// primo accesso dopo l'aggiornamento.
    /// </summary>
    [FactRichiedeMySql]
    public void ChiVedevaLaInboxOfficina_vedeLavorazioniOfficine()
    {
        using var db = new DatabaseDiProva("v92_permessi");
        db.CreaSchemaCompleto();
        using MySqlConnection c = db.Apri();

        int persona = Inserisci(c, "INSERT INTO employees (first_name, last_name) VALUES ('Paolo', 'Officina')");
        c.Execute(@"INSERT INTO employee_feature_access (employee_id, feature_key, access, origin)
                    VALUES (@P, 'nav.officina_inbox', 'FULL', 'CLASSE')", new { P = persona });
        c.Execute("DELETE FROM employee_feature_access WHERE employee_id = @P AND feature_key = 'nav.work_requests'",
            new { P = persona });

        Applica(c);

        Assert.Equal("FULL", c.ExecuteScalar<string>(@"
            SELECT access FROM employee_feature_access
            WHERE employee_id = @P AND feature_key = 'nav.work_requests'", new { P = persona }));
    }

    // ── aiuti ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// La v92 è già passata quando lo schema è stato creato: la si riesegue sui dati appena
    /// seminati. Deve poterlo sopportare — è la stessa cosa che succede a ogni riavvio quando
    /// una migrazione è rimasta pendente.
    /// </summary>
    private static void Applica(MySqlConnection c) =>
        new M092_LavorazioniOfficine().Applica(c, NullLogger.Instance);

    private static int SeminaCommessa(MySqlConnection c)
    {
        int cliente = Inserisci(c, "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int pm = Inserisci(c, "INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        return Inserisci(c,
            @"INSERT INTO projects (code, title, customer_id, pm_id, status)
              VALUES ('C20260815.992', 'Commessa di prova', @Cliente, @Pm, 'ACTIVE')",
            new { Cliente = cliente, Pm = pm });
    }

    private static int RigaOfficina(
        MySqlConnection c, int commessa, string stato,
        string workType = "", string fornitore = "") =>
        Inserisci(c, @"
            INSERT INTO ddp_officina_items
                (project_id, part_number, description, quantity, item_status, work_type, supplier_name)
            VALUES (@P, '101.0001', 'Piastra', 3, @S, @W, @F)",
            new { P = commessa, S = stato, W = workType, F = fornitore });

    private static void Evento(
        MySqlConnection c, int riga, int commessa, string da, string a, int giorniFa) =>
        c.Execute(@"
            INSERT INTO ddp_item_events (item_type, item_id, project_id, from_status, to_status, changed_at)
            VALUES ('OFFICINA', @R, @P, @Da, @A, DATE_SUB(NOW(), INTERVAL @G DAY))",
            new { R = riga, P = commessa, Da = da, A = a, G = giorniFa });

    private static string TipoDi(MySqlConnection c, int riga) =>
        c.ExecuteScalar<string>("SELECT work_type FROM ddp_officina_items WHERE id = @R", new { R = riga }) ?? "";

    private static decimal? TariffaDi(MySqlConnection c, int riga) =>
        c.ExecuteScalar<decimal?>("SELECT hourly_rate FROM ddp_officina_items WHERE id = @R", new { R = riga });

    private static int Inserisci(MySqlConnection c, string sql, object? par = null)
    {
        c.Execute(sql, par);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }
}
