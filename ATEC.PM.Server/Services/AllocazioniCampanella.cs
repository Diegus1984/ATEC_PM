using MySqlConnector;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Un'allocazione del planner come serve alla campanella: i campi che fanno il testo. Tipo già
/// normalizzato (OP | FLEX | FERIE), descrizione senza spazi ai lati e vuota ≡ null: due record
/// uguali sono la stessa allocazione (record: uguaglianza per valore).
/// </summary>
public sealed record AllocazioneCampanella(
    int EmployeeId,
    string Tipo,
    DateOnly Inizio,
    DateOnly Fine,
    int? ProjectId,
    int? ServiceId,
    int? OtherActivityId,
    string? Descrizione);

/// <summary>
/// Un fatto del planner da raccontare al dipendente coinvolto (segnalazione #148).
/// <c>Azione</c> = creata | modificata | rimossa. <c>Dopo</c> è la riga com'è adesso (per
/// «rimossa»: com'era prima di sparire); <c>Prima</c> solo per «modificata». <c>AutoreId</c> è
/// l'id in PM di chi ha fatto la modifica (null se non si sa); <c>Origine</c> = pm | vps (dal
/// programma ATEC Risorse, via sincronizzazione, con l'autore già tradotto in id PM).
/// </summary>
public sealed record EventoAllocazione(
    string Azione,
    int AssignmentId,
    AllocazioneCampanella Dopo,
    AllocazioneCampanella? Prima,
    int? AutoreId,
    string Origine);

/// <summary>Una notifica pronta: a chi, con che severità e che testo.</summary>
public sealed record NotificaAllocazione(int Destinatario, string Severita, string Titolo, string Messaggio);

/// <summary>
/// Le notifiche a campanella del planner Risorse (segnalazione #148): quando un collega crea,
/// modifica o toglie un'allocazione (OP/FLEX/FERIE) di un dipendente — dal planner di ATEC PM
/// (<c>ResourcesController</c>) o dal programma ATEC Risorse sul VPS, via sincronizzazione
/// (<c>RisorseSyncService</c>) — il dipendente lo vede nella campanella, con chi, cosa e periodo.
/// Mai a chi ha fatto la modifica (niente auto-notifica). Una riassegnazione avvisa tutti e due:
/// il vecchio dipendente («spostata a …») e il nuovo («ti ha assegnato …»).
/// <para>Tipo e riferimento sono <see cref="Tipo"/> con <c>reference_id</c> = id dell'allocazione:
/// il clic sulla notifica apre il planner su quella riga (<c>/risorse?alloc=ID</c>), e la pulizia
/// dei promemoria di <c>NotificationService.CleanResolvedNotifications</c> tiene per persona
/// solo l'avviso più recente sulla stessa allocazione. La composizione dei testi è logica pura
/// (<see cref="Componi"/>), con i suoi test; qui dentro si risolvono solo i nomi e si scrive con
/// <see cref="NotificationService"/>. Un errore qui non deve mai far fallire la modifica del
/// planner: <see cref="Segnala"/> non solleva.</para>
/// </summary>
public sealed class AllocazioniCampanella
{
    /// <summary>notification_type (VARCHAR(30)) e reference_type (VARCHAR(20)) delle notifiche del planner.</summary>
    public const string Tipo = "RES_ASSIGNMENT";

    private readonly DbService _db;
    private readonly NotificationService _notif;
    private readonly ILogger<AllocazioniCampanella> _logger;

    public AllocazioniCampanella(DbService db, NotificationService notif, ILogger<AllocazioniCampanella> logger)
    {
        _db = db;
        _notif = notif;
        _logger = logger;
    }

    /// <summary>I nomi che servono ai testi, letti una volta per chiamata.</summary>
    public sealed class Nomi
    {
        public Dictionary<int, string> Dipendenti { get; init; } = new();
        public Dictionary<int, (string Code, string? Title)> Commesse { get; init; } = new();
        public Dictionary<int, string> Service { get; init; } = new();
        public Dictionary<int, string> AltreAttivita { get; init; } = new();
    }

    /// <summary>
    /// Scrive le notifiche per gli eventi dati e ritorna quante ne ha scritte. Non solleva mai:
    /// un nome non leggibile o una INSERT fallita finiscono nel log come avviso, la modifica del
    /// planner è già fatta e resta fatta.
    /// </summary>
    public int Segnala(IReadOnlyCollection<EventoAllocazione> eventi)
    {
        if (eventi.Count == 0) return 0;

        List<(EventoAllocazione Evento, NotificaAllocazione Notifica)> daScrivere;
        try
        {
            Nomi nomi = LeggiNomi(eventi);
            daScrivere = eventi.SelectMany(e => Componi(e, nomi).Select(n => (e, n))).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Campanella] Allocazioni: nomi non leggibili, nessuna notifica: {Msg}", ex.Message);
            return 0;
        }

        int scritte = 0;
        foreach ((EventoAllocazione e, NotificaAllocazione n) in daScrivere)
        {
            try
            {
                _notif.Create(Tipo, n.Severita, Tronca(n.Titolo, 200), Tronca(n.Messaggio, 500),
                    Tipo, e.AssignmentId, e.Dopo.ProjectId, e.AutoreId, new[] { n.Destinatario });
                scritte++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Campanella] Allocazione {Id}: notifica a {Dest} non scritta: {Msg}",
                    e.AssignmentId, n.Destinatario, ex.Message);
            }
        }
        return scritte;
    }

    // ── Nomi ─────────────────────────────────────────────────────

    private Nomi LeggiNomi(IReadOnlyCollection<EventoAllocazione> eventi)
    {
        var dipendenti = new HashSet<int>();
        var commesse = new HashSet<int>();
        var service = new HashSet<int>();
        var altre = new HashSet<int>();
        foreach (EventoAllocazione e in eventi)
        {
            if (e.AutoreId is int a) dipendenti.Add(a);
            foreach (AllocazioneCampanella? r in new[] { e.Dopo, e.Prima })
            {
                if (r == null) continue;
                dipendenti.Add(r.EmployeeId);
                if (r.ProjectId is int p) commesse.Add(p);
                if (r.ServiceId is int s) service.Add(s);
                if (r.OtherActivityId is int o) altre.Add(o);
            }
        }

        var nomi = new Nomi();
        using MySqlConnection c = _db.Open();
        if (dipendenti.Count > 0)
            foreach ((int id, string? nome) in c.Query<(int, string?)>(
                         "SELECT id, CONCAT_WS(' ', first_name, last_name) FROM employees WHERE id IN @Ids", new { Ids = dipendenti }))
                nomi.Dipendenti[id] = (nome ?? "").Trim();
        if (commesse.Count > 0)
            foreach ((int id, string? code, string? title) in c.Query<(int, string?, string?)>(
                         "SELECT id, code, title FROM projects WHERE id IN @Ids", new { Ids = commesse }))
                nomi.Commesse[id] = ((code ?? "").Trim(), string.IsNullOrWhiteSpace(title) ? null : title.Trim());
        if (service.Count > 0)
            foreach ((int id, string? cod) in c.Query<(int, string?)>(
                         "SELECT id, cod FROM res_services WHERE id IN @Ids", new { Ids = service }))
                nomi.Service[id] = (cod ?? "").Trim();
        if (altre.Count > 0)
            foreach ((int id, string? descrizione) in c.Query<(int, string?)>(
                         "SELECT id, descrizione FROM res_other_activities WHERE id IN @Ids", new { Ids = altre }))
                nomi.AltreAttivita[id] = (descrizione ?? "").Trim();
        return nomi;
    }

    // ── Composizione (logica pura) ───────────────────────────────

    /// <summary>
    /// Le notifiche per un evento: a chi e con che testo. Mai all'autore. Riassegnazione (la
    /// «modificata» cambia dipendente) = «tolta» al vecchio e «nuova» al nuovo.
    /// </summary>
    public static List<NotificaAllocazione> Componi(EventoAllocazione e, Nomi nomi)
    {
        var esito = new List<NotificaAllocazione>();
        AllocazioneCampanella d = e.Dopo;
        (string autore, bool conNome) = Autore(e, nomi);
        // Chi legge deve capire da dove viene la modifica; se l'autore è già «Il programma ATEC
        // Risorse» la coda sarebbe una ripetizione.
        string coda = e.Origine == "vps" && conNome ? " (dal programma ATEC Risorse)" : "";

        switch (e.Azione)
        {
            case "creata":
                if (d.EmployeeId != e.AutoreId)
                    esito.Add(new NotificaAllocazione(d.EmployeeId, "INFO", TitoloNuova(d, nomi),
                        $"{autore} {FraseAssegna(d, nomi)}{coda}."));
                break;

            case "modificata":
                AllocazioneCampanella? p = e.Prima;
                if (p != null && p.EmployeeId != d.EmployeeId)
                {
                    string nuovo = Nome(nomi, d.EmployeeId);
                    string vecchio = Nome(nomi, p.EmployeeId);
                    if (p.EmployeeId != e.AutoreId)
                        esito.Add(new NotificaAllocazione(p.EmployeeId, "WARNING", TitoloTolta(p, nomi),
                            $"{autore} ha spostato a {nuovo} {Cosa(p, nomi)} {Periodo(p)}{coda}."));
                    if (d.EmployeeId != e.AutoreId)
                        esito.Add(new NotificaAllocazione(d.EmployeeId, "INFO", TitoloNuova(d, nomi),
                            $"{autore} {FraseAssegna(d, nomi)} (prima era di {vecchio}){coda}."));
                }
                else if (d.EmployeeId != e.AutoreId)
                {
                    AllocazioneCampanella riferimento = p ?? d;
                    string cambiamenti = p == null ? $"ora {Periodo(d)}" : Differenze(p, d, nomi);
                    esito.Add(new NotificaAllocazione(d.EmployeeId, "INFO", TitoloModificata(riferimento, nomi),
                        $"{autore} ha modificato {Cosa(riferimento, nomi)}: {cambiamenti}{coda}."));
                }
                break;

            case "rimossa":
                if (d.EmployeeId != e.AutoreId)
                    esito.Add(new NotificaAllocazione(d.EmployeeId, "WARNING", TitoloTolta(d, nomi),
                        $"{autore} ha tolto {Cosa(d, nomi)} {Periodo(d)}{coda}."));
                break;
        }
        return esito;
    }

    /// <summary>Come <c>PlanNotificationService.Label</c>: commessa, poi service, altra attività, descrizione, tipo.</summary>
    public static string Etichetta(AllocazioneCampanella a, Nomi nomi)
    {
        if (a.Tipo == "FERIE")
            return "Ferie";
        if (a.ProjectId is int p && nomi.Commesse.TryGetValue(p, out (string Code, string? Title) com) && com.Code.Length > 0)
            return com.Title == null ? com.Code : $"{com.Code} — {com.Title}";
        if (a.ServiceId is int s && nomi.Service.TryGetValue(s, out string? cod) && cod.Length > 0)
            return cod;
        if (a.OtherActivityId is int o && nomi.AltreAttivita.TryGetValue(o, out string? altra) && altra.Length > 0)
            return altra;
        if (!string.IsNullOrWhiteSpace(a.Descrizione))
            return a.Descrizione;
        return a.Tipo == "FLEX" ? "Flessibile" : "Operativo";
    }

    /// <summary>«dal 07/09/2026 al 11/09/2026», oppure «il 07/09/2026» se è un giorno solo.</summary>
    public static string Periodo(AllocazioneCampanella a) =>
        a.Inizio == a.Fine ? $"il {a.Inizio:dd/MM/yyyy}" : $"dal {a.Inizio:dd/MM/yyyy} al {a.Fine:dd/MM/yyyy}";

    private static string Cosa(AllocazioneCampanella a, Nomi nomi) => a.Tipo switch
    {
        "FERIE" => "le tue ferie",
        "FLEX" => $"la tua attività flessibile {Etichetta(a, nomi)}",
        _ => $"la tua attività {Etichetta(a, nomi)}",
    };

    private static string FraseAssegna(AllocazioneCampanella a, Nomi nomi) => a.Tipo switch
    {
        "FERIE" => $"ha messo in pianificazione le tue ferie {Periodo(a)}",
        "FLEX" => $"ti ha assegnato l'attività flessibile {Etichetta(a, nomi)} {Periodo(a)}",
        _ => $"ti ha assegnato l'attività {Etichetta(a, nomi)} {Periodo(a)}",
    };

    private static string TipoDescrizione(string tipo) => tipo switch
    {
        "FERIE" => "ferie",
        "FLEX" => "attività flessibile",
        _ => "attività operativa",
    };

    private static string TitoloNuova(AllocazioneCampanella a, Nomi nomi) =>
        a.Tipo == "FERIE" ? $"Ferie nel planner — {Periodo(a)}" : $"Nuova attività nel planner — {Etichetta(a, nomi)}";

    private static string TitoloModificata(AllocazioneCampanella a, Nomi nomi) =>
        a.Tipo == "FERIE" ? "Ferie modificate nel planner" : $"Attività modificata nel planner — {Etichetta(a, nomi)}";

    private static string TitoloTolta(AllocazioneCampanella a, Nomi nomi) =>
        a.Tipo == "FERIE" ? "Ferie tolte dal planner" : $"Attività tolta dal planner — {Etichetta(a, nomi)}";

    /// <summary>Cosa è cambiato, in ordine: cosa (etichetta), tipo, periodo. Niente di visibile → il periodo attuale.</summary>
    private static string Differenze(AllocazioneCampanella prima, AllocazioneCampanella dopo, Nomi nomi)
    {
        var parti = new List<string>();
        string ePrima = Etichetta(prima, nomi), eDopo = Etichetta(dopo, nomi);
        if (ePrima != eDopo) parti.Add($"ora {eDopo} (prima {ePrima})");
        if (prima.Tipo != dopo.Tipo) parti.Add($"ora {TipoDescrizione(dopo.Tipo)} (prima {TipoDescrizione(prima.Tipo)})");
        if (prima.Inizio != dopo.Inizio || prima.Fine != dopo.Fine) parti.Add($"ora {Periodo(dopo)} (prima {Periodo(prima)})");
        return parti.Count == 0 ? $"ora {Periodo(dopo)}" : string.Join(", ", parti);
    }

    /// <summary>Il nome dell'autore; senza nome: dal VPS «Il programma ATEC Risorse», in PM «Un collega».</summary>
    private static (string Nome, bool ConNome) Autore(EventoAllocazione e, Nomi nomi)
    {
        if (e.AutoreId is int a && nomi.Dipendenti.TryGetValue(a, out string? nome) && nome.Length > 0)
            return (nome, true);
        return (e.Origine == "vps" ? "Il programma ATEC Risorse" : "Un collega", false);
    }

    private static string Nome(Nomi nomi, int employeeId) =>
        nomi.Dipendenti.TryGetValue(employeeId, out string? nome) && nome.Length > 0 ? nome : $"dipendente {employeeId}";

    private static string Tronca(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
