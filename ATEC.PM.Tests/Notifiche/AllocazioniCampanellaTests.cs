using ATEC.PM.Server.Services;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Notifiche;

/// <summary>
/// #148 — le notifiche a campanella del planner Risorse. La composizione dei testi è logica pura
/// (<see cref="AllocazioniCampanella.Componi"/>): chi, cosa, periodo; mai all'autore; una
/// riassegnazione avvisa il vecchio e il nuovo dipendente; dal VPS senza autore parla «il
/// programma ATEC Risorse». Le cose da difendere sono i testi che i colleghi leggeranno.
/// </summary>
public class AllocazioniCampanellaComponiTests
{
    private const int Rossi = 1, Verdi = 2, Bianchi = 3, Commessa = 10;
    private static readonly DateOnly Inizio = new(2026, 9, 7);
    private static readonly DateOnly Fine = new(2026, 9, 11);

    private static AllocazioniCampanella.Nomi Nomi() => new()
    {
        Dipendenti = { [Rossi] = "Mario Rossi", [Verdi] = "Anna Verdi", [Bianchi] = "Luca Bianchi" },
        Commesse = { [Commessa] = ("C20260901.001", "Impianto Minebea") },
        Service = { [5] = "SRV-77" },
        AltreAttivita = { [6] = "Formazione sicurezza" },
    };

    private static AllocazioneCampanella Riga(int dipendente, string tipo = "OP", int? commessa = Commessa, string? descrizione = null,
        DateOnly? inizio = null, DateOnly? fine = null, int? service = null, int? altra = null) =>
        new(dipendente, tipo, inizio ?? Inizio, fine ?? Fine, commessa, service, altra, descrizione);

    [Fact]
    public void Creata_in_PM_avvisa_il_dipendente_con_chi_cosa_e_periodo()
    {
        var e = new EventoAllocazione("creata", 42, Riga(Rossi), null, Verdi, "pm");

        NotificaAllocazione n = Assert.Single(AllocazioniCampanella.Componi(e, Nomi()));

        Assert.Equal(Rossi, n.Destinatario);
        Assert.Equal("INFO", n.Severita);
        Assert.Equal("Nuova attività nel planner — C20260901.001 — Impianto Minebea", n.Titolo);
        Assert.Equal("Anna Verdi ti ha assegnato l'attività C20260901.001 — Impianto Minebea dal 07/09/2026 al 11/09/2026.", n.Messaggio);
    }

    [Fact]
    public void Chi_si_assegna_da_solo_non_riceve_niente()
    {
        Assert.Empty(AllocazioniCampanella.Componi(new EventoAllocazione("creata", 42, Riga(Rossi), null, Rossi, "pm"), Nomi()));
        Assert.Empty(AllocazioniCampanella.Componi(new EventoAllocazione("rimossa", 42, Riga(Rossi), null, Rossi, "pm"), Nomi()));
        Assert.Empty(AllocazioniCampanella.Componi(new EventoAllocazione("modificata", 42, Riga(Rossi, inizio: new DateOnly(2026, 9, 8)), Riga(Rossi), Rossi, "pm"), Nomi()));
    }

    [Fact]
    public void Riassegnazione_avvisa_il_vecchio_e_il_nuovo()
    {
        var e = new EventoAllocazione("modificata", 42, Riga(Bianchi), Riga(Rossi), Verdi, "pm");

        List<NotificaAllocazione> n = AllocazioniCampanella.Componi(e, Nomi());

        Assert.Equal(2, n.Count);
        Assert.Equal(Rossi, n[0].Destinatario);
        Assert.Equal("WARNING", n[0].Severita);
        Assert.Equal("Attività tolta dal planner — C20260901.001 — Impianto Minebea", n[0].Titolo);
        Assert.Equal("Anna Verdi ha spostato a Luca Bianchi la tua attività C20260901.001 — Impianto Minebea dal 07/09/2026 al 11/09/2026.", n[0].Messaggio);
        Assert.Equal(Bianchi, n[1].Destinatario);
        Assert.Equal("INFO", n[1].Severita);
        Assert.Equal("Anna Verdi ti ha assegnato l'attività C20260901.001 — Impianto Minebea dal 07/09/2026 al 11/09/2026 (prima era di Mario Rossi).", n[1].Messaggio);
    }

    [Fact]
    public void Riassegnazione_fatta_dal_nuovo_dipendente_avvisa_solo_il_vecchio()
    {
        var e = new EventoAllocazione("modificata", 42, Riga(Bianchi), Riga(Rossi), Bianchi, "pm");

        NotificaAllocazione n = Assert.Single(AllocazioniCampanella.Componi(e, Nomi()));

        Assert.Equal(Rossi, n.Destinatario);
        Assert.StartsWith("Luca Bianchi ha spostato a Luca Bianchi", n.Messaggio);
    }

    [Fact]
    public void Date_cambiate_raccontano_prima_e_dopo()
    {
        var prima = Riga(Rossi);
        var dopo = Riga(Rossi, inizio: new DateOnly(2026, 9, 14), fine: new DateOnly(2026, 9, 18));

        NotificaAllocazione n = Assert.Single(AllocazioniCampanella.Componi(new EventoAllocazione("modificata", 42, dopo, prima, Verdi, "pm"), Nomi()));

        Assert.Equal("Attività modificata nel planner — C20260901.001 — Impianto Minebea", n.Titolo);
        Assert.Equal("Anna Verdi ha modificato la tua attività C20260901.001 — Impianto Minebea: ora dal 14/09/2026 al 18/09/2026 (prima dal 07/09/2026 al 11/09/2026).", n.Messaggio);
    }

    [Fact]
    public void Commessa_e_tipo_cambiati_si_elencano_in_ordine_cosa_tipo_periodo()
    {
        var prima = Riga(Rossi);
        var dopo = Riga(Rossi, "FLEX", commessa: null, descrizione: "Supporto officina", fine: new DateOnly(2026, 9, 12));

        NotificaAllocazione n = Assert.Single(AllocazioniCampanella.Componi(new EventoAllocazione("modificata", 42, dopo, prima, Verdi, "pm"), Nomi()));

        Assert.Equal("Anna Verdi ha modificato la tua attività C20260901.001 — Impianto Minebea: ora Supporto officina (prima C20260901.001 — Impianto Minebea), " +
                     "ora attività flessibile (prima attività operativa), ora dal 07/09/2026 al 12/09/2026 (prima dal 07/09/2026 al 11/09/2026).", n.Messaggio);
    }

    [Fact]
    public void Modificata_senza_la_riga_di_prima_dice_il_periodo_attuale()
    {
        NotificaAllocazione n = Assert.Single(AllocazioniCampanella.Componi(new EventoAllocazione("modificata", 42, Riga(Rossi), null, Verdi, "pm"), Nomi()));

        Assert.Equal("Anna Verdi ha modificato la tua attività C20260901.001 — Impianto Minebea: ora dal 07/09/2026 al 11/09/2026.", n.Messaggio);
    }

    [Fact]
    public void Rimossa_e_un_avviso_ambra()
    {
        NotificaAllocazione n = Assert.Single(AllocazioniCampanella.Componi(new EventoAllocazione("rimossa", 42, Riga(Rossi, descrizione: "Nota"), null, Verdi, "pm"), Nomi()));

        Assert.Equal("WARNING", n.Severita);
        Assert.Equal("Attività tolta dal planner — C20260901.001 — Impianto Minebea", n.Titolo);
        Assert.Equal("Anna Verdi ha tolto la tua attività C20260901.001 — Impianto Minebea dal 07/09/2026 al 11/09/2026.", n.Messaggio);
    }

    [Fact]
    public void Ferie_dal_VPS_senza_autore_parlano_del_programma()
    {
        var ferie = Riga(Rossi, "FERIE", commessa: null, inizio: new DateOnly(2026, 8, 10), fine: new DateOnly(2026, 8, 21));

        NotificaAllocazione n = Assert.Single(AllocazioniCampanella.Componi(new EventoAllocazione("creata", 42, ferie, null, null, "vps"), Nomi()));

        Assert.Equal("Ferie nel planner — dal 10/08/2026 al 21/08/2026", n.Titolo);
        Assert.Equal("Il programma ATEC Risorse ha messo in pianificazione le tue ferie dal 10/08/2026 al 21/08/2026.", n.Messaggio);

        NotificaAllocazione tolte = Assert.Single(AllocazioniCampanella.Componi(new EventoAllocazione("rimossa", 42, ferie, null, null, "vps"), Nomi()));
        Assert.Equal("Ferie tolte dal planner", tolte.Titolo);
        Assert.Equal("Il programma ATEC Risorse ha tolto le tue ferie dal 10/08/2026 al 21/08/2026.", tolte.Messaggio);
    }

    [Fact]
    public void Dal_VPS_con_l_autore_tradotto_la_coda_dice_da_dove_viene()
    {
        NotificaAllocazione n = Assert.Single(AllocazioniCampanella.Componi(new EventoAllocazione("creata", 42, Riga(Rossi), null, Verdi, "vps"), Nomi()));

        Assert.Equal("Anna Verdi ti ha assegnato l'attività C20260901.001 — Impianto Minebea dal 07/09/2026 al 11/09/2026 (dal programma ATEC Risorse).", n.Messaggio);
    }

    [Fact]
    public void In_PM_senza_autore_parla_un_collega()
    {
        NotificaAllocazione n = Assert.Single(AllocazioniCampanella.Componi(new EventoAllocazione("creata", 42, Riga(Rossi), null, null, "pm"), Nomi()));

        Assert.StartsWith("Un collega ti ha assegnato", n.Messaggio);
    }

    [Fact]
    public void Un_giorno_solo_si_dice_il()
    {
        var giorno = Riga(Rossi, "FLEX", commessa: null, descrizione: "Sopralluogo", inizio: Inizio, fine: Inizio);

        NotificaAllocazione n = Assert.Single(AllocazioniCampanella.Componi(new EventoAllocazione("creata", 42, giorno, null, Verdi, "pm"), Nomi()));

        Assert.Equal("Anna Verdi ti ha assegnato l'attività flessibile Sopralluogo il 07/09/2026.", n.Messaggio);
    }

    [Fact]
    public void Etichetta_commessa_poi_service_altra_attivita_descrizione_e_infine_il_tipo()
    {
        AllocazioniCampanella.Nomi nomi = Nomi();
        Assert.Equal("C20260901.001 — Impianto Minebea", AllocazioniCampanella.Etichetta(Riga(Rossi), nomi));
        Assert.Equal("SRV-77", AllocazioniCampanella.Etichetta(Riga(Rossi, commessa: null, service: 5, altra: 6, descrizione: "x"), nomi));
        Assert.Equal("Formazione sicurezza", AllocazioniCampanella.Etichetta(Riga(Rossi, commessa: null, altra: 6, descrizione: "x"), nomi));
        Assert.Equal("Manutenzione", AllocazioniCampanella.Etichetta(Riga(Rossi, commessa: null, descrizione: "Manutenzione"), nomi));
        Assert.Equal("Operativo", AllocazioniCampanella.Etichetta(Riga(Rossi, commessa: null), nomi));
        Assert.Equal("Flessibile", AllocazioniCampanella.Etichetta(Riga(Rossi, "FLEX", commessa: null), nomi));
        Assert.Equal("Ferie", AllocazioniCampanella.Etichetta(Riga(Rossi, "FERIE", commessa: null, descrizione: "mare"), nomi));
        // Commessa non in anagrafica (id senza nome): si passa oltre, come per il digest.
        Assert.Equal("Nota", AllocazioniCampanella.Etichetta(Riga(Rossi, commessa: 999, descrizione: "Nota"), nomi));
    }
}

/// <summary>
/// <see cref="AllocazioniCampanella.Segnala"/> sul database: la notifica arriva al dipendente e
/// non all'autore, con tipo, riferimento e commessa giusti; sulla stessa allocazione la pulizia
/// dei promemoria tiene per persona solo l'avviso più recente.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class AllocazioniCampanellaSegnalaTests
{
    private readonly SchemaCondiviso _schema;

    public AllocazioniCampanellaSegnalaTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private sealed record Scenario(int Rossi, int Verdi, int Commessa);

    private Scenario Semina()
    {
        using MySqlConnection c = _schema.Apri();
        int rossi = Inserisci(c, "INSERT INTO employees (first_name, last_name, user_role) VALUES ('Mario', 'Rossi', 'TECH')");
        int verdi = Inserisci(c, "INSERT INTO employees (first_name, last_name, user_role) VALUES ('Anna', 'Verdi', 'PM')");
        int cliente = Inserisci(c, "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int commessa = Inserisci(c,
            "INSERT INTO projects (code, title, customer_id, pm_id, status) VALUES ('C20260903.001', 'Impianto di prova', @Cliente, @Pm, 'ACTIVE')",
            new { Cliente = cliente, Pm = verdi });
        return new Scenario(rossi, verdi, commessa);
    }

    private static int Inserisci(MySqlConnection c, string sql, object? param = null)
    {
        c.Execute(sql, param);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private NotificationService Notifiche() =>
        new(_schema.Servizio(), new AnagraficheCache(NullLogger<AnagraficheCache>.Instance));

    private AllocazioniCampanella Campanella() =>
        new(_schema.Servizio(), Notifiche(), NullLogger<AllocazioniCampanella>.Instance);

    private List<(int Destinatario, string Tipo, string RefTipo, int RefId, int? ProjectId, int? CreatedBy, string Titolo, string Messaggio)> Scritte()
    {
        using MySqlConnection c = _schema.Apri();
        return c.Query<(int, string, string, int, int?, int?, string, string)>(@"
            SELECT nr.employee_id, n.notification_type, n.reference_type, n.reference_id, n.project_id, n.created_by, n.title, n.message
            FROM notification_recipients nr JOIN notifications n ON n.id = nr.notification_id
            ORDER BY n.id, nr.id").ToList();
    }

    [FactRichiedeMySql]
    public void La_notifica_va_al_dipendente_con_tipo_riferimento_commessa_e_autore()
    {
        Scenario s = Semina();
        var riga = new AllocazioneCampanella(s.Rossi, "OP", new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), s.Commessa, null, null, null);

        int scritte = Campanella().Segnala(new[] { new EventoAllocazione("creata", 42, riga, null, s.Verdi, "pm") });

        Assert.Equal(1, scritte);
        var n = Assert.Single(Scritte());
        Assert.Equal(s.Rossi, n.Destinatario);
        Assert.Equal("RES_ASSIGNMENT", n.Tipo);
        Assert.Equal("RES_ASSIGNMENT", n.RefTipo);
        Assert.Equal(42, n.RefId);
        Assert.Equal(s.Commessa, n.ProjectId);
        Assert.Equal(s.Verdi, n.CreatedBy);
        Assert.Equal("Nuova attività nel planner — C20260903.001 — Impianto di prova", n.Titolo);
        Assert.Equal("Anna Verdi ti ha assegnato l'attività C20260903.001 — Impianto di prova dal 07/09/2026 al 11/09/2026.", n.Messaggio);
    }

    [FactRichiedeMySql]
    public void All_autore_non_arriva_niente_e_una_lista_vuota_non_scrive()
    {
        Scenario s = Semina();
        var riga = new AllocazioneCampanella(s.Rossi, "OP", new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), s.Commessa, null, null, null);
        AllocazioniCampanella campanella = Campanella();

        Assert.Equal(0, campanella.Segnala(new[] { new EventoAllocazione("creata", 42, riga, null, s.Rossi, "pm") }));
        Assert.Equal(0, campanella.Segnala(Array.Empty<EventoAllocazione>()));
        Assert.Empty(Scritte());
    }

    [FactRichiedeMySql]
    public void Sulla_stessa_allocazione_la_pulizia_tiene_solo_l_avviso_piu_recente()
    {
        Scenario s = Semina();
        var prima = new AllocazioneCampanella(s.Rossi, "OP", new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11), s.Commessa, null, null, null);
        var dopo = prima with { Fine = new DateOnly(2026, 9, 12) };
        AllocazioniCampanella campanella = Campanella();
        campanella.Segnala(new[] { new EventoAllocazione("creata", 42, prima, null, s.Verdi, "pm") });
        campanella.Segnala(new[] { new EventoAllocazione("modificata", 42, dopo, prima, s.Verdi, "pm") });
        Assert.Equal(2, Scritte().Count);

        Notifiche().CleanResolvedNotifications();

        var rimasta = Assert.Single(Scritte());
        Assert.StartsWith("Attività modificata nel planner", rimasta.Titolo);
    }
}
