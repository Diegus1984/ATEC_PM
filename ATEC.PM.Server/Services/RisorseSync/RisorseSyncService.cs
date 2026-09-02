using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Shared.DTOs;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using MySqlConnector;

namespace ATEC.PM.Server.Services.RisorseSync;

/// <summary>
/// Il motore di sincronizzazione Risorse ATEC PM ⇄ ATEC Risorse (VPS) — PIANO-SYNC-RISORSE.md §4.1.
///
/// <para><b>Tre inneschi, un solo giro alla volta.</b></para>
/// <list type="number">
/// <item><b>hub</b>: il motore è collegato come client SignalR a <c>/hubs/resource-planner</c>
/// del VPS; a ogni <c>AssignmentsChanged</c>/<c>EmployeesChanged</c> parte un giro;</item>
/// <item><b>pm</b>: <c>ResourcesController</c> chiama <see cref="Trigger"/> dopo ogni scrittura
/// riuscita sulle allocazioni;</item>
/// <item><b>timer</b>: rete di sicurezza ogni 60 s (scritture SQL dirette del modulo HR,
/// riavvii, hub scollegato).</item>
/// </list>
///
/// <para>Le richieste arrivate durante un giro ne fanno partire <b>uno solo</b> dopo: la coda è
/// da un posto, il secondo Trigger ravvicinato si perde perché tanto il giro successivo
/// ricontrolla tutto. Un <see cref="SemaphoreSlim"/> garantisce che due giri non si
/// sovrappongano mai, nemmeno fra il loop e un «Esegui ora» dal pannello.</para>
///
/// <para><b>Fase 0</b>: login + stato del VPS + riga di registro. <b>Fase 1</b>: le anagrafiche
/// PM → VPS (dipendenti, reparti + legami, commesse) con il seme della mappa dipendenti —
/// vedi <see cref="SyncAnagraficheAsync"/>. <b>Fase 2</b>: le allocazioni nei due versi con le
/// regole di merge di §4.3 — vedi <see cref="SyncAllocazioniAsync"/>.</para>
///
/// <para>Se non è configurato o è spento: nessuna rete, hub scollegato, il loop dorme. Un
/// errore qualsiasi finisce nel log con prefisso <c>[RisorseSync]</c> e nel registro: non
/// deve mai far cadere l'host.</para>
/// </summary>
public sealed class RisorseSyncService : BackgroundService
{
    private static readonly TimeSpan RitardoIniziale = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IntervalloTimer = TimeSpan.FromSeconds(60);
    /// <summary>Quanto si aspetta il primo handshake con l'hub del VPS prima di proseguire col timer.</summary>
    private static readonly TimeSpan AttesaHub = TimeSpan.FromSeconds(15);
    /// <summary>Le righe di res_sync_log più vecchie di così vengono buttate (una pulizia al giorno).</summary>
    private const int GiorniRegistro = 60;

    /// <summary>Chiavi res_settings dei segnalibri della Fase 1 (anagrafiche).</summary>
    private const string ChiaveInvioCompletoAlle = "sync.anagrafiche_full_at";
    private const string ChiaveImprontaReparti = "sync.hash.reparti";
    /// <summary>Kind di <see cref="_saltateNote"/> per le righe del VPS che MySQL ha rifiutato (chiave = id VPS): non è un kind di res_sync_map.</summary>
    private const string KindAllocazionePm = "ASSIGNMENT_PM";
    /// <summary>Sotto questo numero di cancellazioni in PM in un giro il freno «più di metà delle coppie» non scatta.</summary>
    private const int SogliaCancellazioniPm = 10;

    private readonly ResourcesDbService _rdb;
    private readonly ILogger<RisorseSyncService> _logger;
    private readonly RisorseSyncSettingsStore _store;
    /// <summary>Solo nei test: un HttpClient con handler finto al posto di quello condiviso del client.</summary>
    private readonly HttpClient? _http;
    /// <summary>
    /// L'hub del planner di ATEC PM: dopo ogni scrittura in MySQL si manda lo stesso
    /// <c>AssignmentsChanged</c> di <c>ResourcesController.NotifyChange</c>, così chi ha il
    /// planner aperto vede la barra comparire come se l'avesse messa un collega. Null nei test.
    /// </summary>
    private readonly IHubContext<ResourcePlannerHub>? _hubPm;

    /// <summary>Coda da UN posto: più Trigger ravvicinati = un solo giro in più.</summary>
    private readonly Channel<string> _inneschi = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>Un giro alla volta, sempre: loop, «Esegui ora» e hub passano tutti di qui.</summary>
    private readonly SemaphoreSlim _giro = new(1, 1);

    private RisorseSyncClient? _client;
    private HubConnection? _hub;
    private string? _hubUrl;
    /// <summary>Il client con cui è stato costruito <see cref="_hub"/>: se cambia, l'hub si rifà.</summary>
    private RisorseSyncClient? _hubClient;
    private CancellationToken _ctHost = CancellationToken.None;
    /// <summary>Ultima pulizia di res_sync_log (UTC): se ne fa una al giorno.</summary>
    private DateTime _ultimaPulizia = DateTime.MinValue;

    /// <summary>
    /// Le righe che il VPS ha saltato, con l'impronta con cui sono partite e il suo messaggio.
    /// Una riga saltata non entra nella mappa, quindi al giro dopo partirebbe di nuovo: qui si
    /// ricorda e NON si rimanda finché il dato non cambia (impronta diversa) o non arriva
    /// l'invio completo (una volta al giorno, che svuota l'elenco). Senza questo freno un
    /// «skipped» stabile = una PUT ogni 60 s e una riga di registro al minuto.
    /// Si tocca solo dentro il giro (sotto semaforo): niente lock.
    /// </summary>
    private readonly Dictionary<(string Kind, int LocalId), (string Impronta, string Messaggio)> _saltateNote = new();

    // ── Stato esposto ────────────────────────────────────────────

    public bool IsSyncing { get; private set; }
    public bool HubConnected => _hub?.State == HubConnectionState.Connected;
    public DateTime? LastRun { get; private set; }
    public string? LastError { get; private set; }

    public RisorseSyncService(ResourcesDbService rdb, IConfiguration config, ILogger<RisorseSyncService> logger,
        IHubContext<ResourcePlannerHub>? hubPm = null)
        : this(rdb, config, logger, null, hubPm)
    {
    }

    /// <summary>Per i test: stesso motore, ma le chiamate HTTP passano dall'<paramref name="http"/> dato (handler finto) e l'hub di PM può mancare.</summary>
    internal RisorseSyncService(ResourcesDbService rdb, IConfiguration config, ILogger<RisorseSyncService> logger, HttpClient? http,
        IHubContext<ResourcePlannerHub>? hubPm = null)
    {
        _rdb = rdb;
        _logger = logger;
        _http = http;
        _hubPm = hubPm;
        _store = new RisorseSyncSettingsStore(rdb, config, logger);
    }

    // ── API pubblica ─────────────────────────────────────────────

    /// <summary>Accoda un giro (non aspetta). Chi lo chiama dice da dove viene: hub | pm | timer | manuale | impostazioni.</summary>
    public void Trigger(string innesco) => _inneschi.Writer.TryWrite(innesco);

    /// <summary>
    /// Esegue un giro ADESSO e ne ritorna l'esito. Se un giro è in corso aspetta che finisca e
    /// poi ne fa uno. Solleva se il motore non è configurato: l'«Esegui ora» del pannello deve
    /// dirlo, non tacere.
    /// <para>Il giro appartiene al servizio, non alla richiesta HTTP che lo chiede: senza un
    /// <paramref name="ct"/> esplicito si usa il token dell'host (chi chiude il browser a metà
    /// non lascia un giro interrotto a metà).</para>
    /// </summary>
    public async Task<RisorseSyncLogEntry> RunNowAsync(string innesco, CancellationToken ct = default)
    {
        RisorseSyncSettings s = _store.Leggi();
        if (!s.IsConfigured)
            throw new InvalidOperationException(s.Enabled
                ? "Sincronizzazione non configurata: mancano indirizzo del VPS, utente o password."
                : "Sincronizzazione spenta: accenderla dalle impostazioni prima di eseguire un giro.");

        AssicuraClient(s);
        return await EseguiGiroAsync(innesco, TokenDelGiro(ct));
    }

    /// <summary>Il token dell'host (fermo del servizio) se chi chiama non ne passa uno suo.</summary>
    private CancellationToken TokenDelGiro(CancellationToken ct) => ct.CanBeCanceled ? ct : _ctHost;

    public RisorseSyncSettingsDto GetSettingsDto() => _store.LeggiDto();

    public void SaveSettings(RisorseSyncSettingsDto dto) => _store.Salva(dto);

    /// <summary>Stato per il pannello: impostazioni, hub, giro in corso, ultimi 20 giri.</summary>
    public RisorseSyncStatusDto GetStatus()
    {
        RisorseSyncSettings s = _store.Leggi();
        List<RisorseSyncLogEntry> ultimi = new();
        try
        {
            using MySqlConnection c = _rdb.Open();
            ultimi = c.Query<RisorseSyncLogEntry>(@"
                SELECT run_utc AS RunUtc, innesco AS Innesco, esito AS Esito,
                       durata_ms AS DurataMs, dettaglio AS Dettaglio
                FROM res_sync_log ORDER BY id DESC LIMIT 20").ToList();
            // Sono UTC: dirlo, così serializzano con la Z e il client li mostra nell'ora giusta.
            foreach (RisorseSyncLogEntry e in ultimi)
                e.RunUtc = DateTime.SpecifyKind(e.RunUtc, DateTimeKind.Utc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RisorseSync] Registro giri non leggibile.");
        }

        DateTime? lastRun = LastRun ?? ParseLastRun(s.LastRun);

        return new RisorseSyncStatusDto
        {
            Enabled = s.Enabled,
            Configured = s.IsConfigured,
            HubConnected = HubConnected,
            InCorso = IsSyncing,
            LastRun = lastRun,
            LastEsito = s.LastEsito,
            LastError = LastError ?? s.LastError,
            UltimiGiri = ultimi,
        };
    }

    /// <summary>
    /// Il «Prova» del pannello: login + GET status con le impostazioni SALVATE, con un client
    /// usa-e-getta e senza toccare il registro. Basta che ci siano indirizzo, utente e
    /// password: l'interruttore può ancora essere spento, si prova prima di accendere.
    /// </summary>
    public async Task<SyncStatusDto> TestAsync(CancellationToken ct = default)
    {
        RisorseSyncSettings s = _store.Leggi();
        if (!s.HasCredentials)
            throw new InvalidOperationException("Mancano indirizzo del VPS, utente o password: salvare le impostazioni prima di provare.");
        // Le impostazioni possono venire dall'appsettings, che nessuno ha validato: stessa regola del salvataggio.
        string? erroreIndirizzo = RisorseSyncSettings.ErroreIndirizzo(s.BaseUrl);
        if (erroreIndirizzo != null)
            throw new InvalidOperationException(erroreIndirizzo);

        CancellationToken token = TokenDelGiro(ct);
        var client = new RisorseSyncClient(s, _logger);
        await client.LoginAsync(token);
        return await client.GetStatusAsync(token);
    }

    /// <summary>
    /// <c>sync.last_run</c> (testo «yyyy-MM-dd HH:mm:ss», scritto in UTC da <see cref="RisorseSyncSettingsStore.ScriviEsito"/>)
    /// → DateTime con Kind Utc, così nel JSON porta la Z e il client lo mostra nell'ora giusta.
    /// </summary>
    internal static DateTime? ParseLastRun(string? testo) =>
        DateTime.TryParseExact(testo, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out DateTime utc)
            ? utc
            : null;

    // ── Il loop ──────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _ctHost = ct;
        try { await Task.Delay(RitardoIniziale, ct); }
        catch (OperationCanceledException) { return; }

        // La lettura pendente sopravvive alle iterazioni: una ReadAsync abbandonata dopo il
        // timeout si mangerebbe il Trigger successivo senza che nessuno lo veda.
        Task<string>? lettura = null;
        bool avvisatoNonConfigurato = false;

        while (!ct.IsCancellationRequested)
        {
            string innesco;
            try
            {
                lettura ??= _inneschi.Reader.ReadAsync(ct).AsTask();
                Task finita = await Task.WhenAny(lettura, Task.Delay(IntervalloTimer, ct));
                if (finita == lettura)
                {
                    innesco = await lettura;
                    lettura = null;
                }
                else
                {
                    innesco = "timer";
                }
            }
            catch (OperationCanceledException) { break; }

            try
            {
                RisorseSyncSettings s = _store.Leggi();
                if (!s.IsConfigured)
                {
                    if (!avvisatoNonConfigurato)
                    {
                        _logger.LogInformation("[RisorseSync] {Motivo}: il motore resta a riposo.",
                            s.Enabled ? "Impostazioni incomplete" : "Sincronizzazione spenta");
                        avvisatoNonConfigurato = true;
                    }
                    await ChiudiHubAsync();
                    continue;
                }
                avvisatoNonConfigurato = false;

                AssicuraClient(s);
                await AssicuraHubAsync(s, ct);
                await EseguiGiroAsync(innesco, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // EseguiGiroAsync ha già il suo try/catch: qui arriva solo quello che sta fuori
                // (lettura impostazioni, hub). Comunque: mai far cadere l'host.
                _logger.LogError(ex, "[RisorseSync] Errore nel loop: {Msg}", ex.Message);
            }
        }

        await ChiudiHubAsync();
    }

    // ── Un giro ──────────────────────────────────────────────────

    /// <summary>
    /// Il giro vero e proprio, sotto semaforo: login se serve → stato del VPS → anagrafiche
    /// (Fase 1) → allocazioni (Fase 2) → riga di registro + esito in res_settings.
    /// Non solleva mai: l'esito, buono o cattivo, è nel valore di ritorno e nel registro.
    /// </summary>
    private async Task<RisorseSyncLogEntry> EseguiGiroAsync(string innesco, CancellationToken ct)
    {
        await _giro.WaitAsync(ct);
        var orologio = Stopwatch.StartNew();
        DateTime runUtc = DateTime.UtcNow;
        var voce = new RisorseSyncLogEntry { RunUtc = runUtc, Innesco = innesco };
        int righeVps = 0;
        // Gli esiti dei due passi vivono FUORI dal try e si riempiono passo dopo passo: se il
        // giro cade a metà (es. la PUT projects) il registro racconta lo stesso quello che la
        // PUT employees ha già creato e mappato.
        var anagrafiche = new EsitoAnagrafiche();
        var allocazioni = new EsitoAllocazioni();

        IsSyncing = true;
        try
        {
            RisorseSyncClient client = _client
                ?? throw new InvalidOperationException("Client non inizializzato.");

            SyncStatusDto stato = await client.GetStatusAsync(ct);
            righeVps = stato.Assignments;

            await SyncAnagraficheAsync(client, anagrafiche, innesco, ct);

            await SyncAllocazioniAsync(client, allocazioni, innesco, ct);

            voce.Esito = "ok";
            voce.Dettaglio = $"VPS: {stato.Employees} dipendenti, {stato.Assignments} allocazioni, " +
                             $"{stato.Projects} commesse, {stato.Departments} reparti (v{stato.Version}); " +
                             anagrafiche.Dettaglio() + "; " + allocazioni.Dettaglio();
            // Giro buono ma con righe rifiutate dal VPS: il pannello deve continuare a mostrarlo,
            // anche quando il timer non lascia più righe nel registro (già segnalate).
            LastError = anagrafiche.PrimoErrore() ?? allocazioni.PrimoErrore();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            voce.Esito = "errore";
            voce.Dettaglio = "Interrotto prima di finire (arresto del servizio o richiesta annullata).";
        }
        catch (Exception ex)
        {
            voce.Esito = "errore";
            voce.Dettaglio = MessaggioLeggibile(ex)
                             + (anagrafiche.Iniziato ? " — " + anagrafiche.Dettaglio() : "")
                             + (allocazioni.Iniziato ? "; " + allocazioni.Dettaglio() : "");
            LastError = voce.Dettaglio;
            _logger.LogWarning(ex, "[RisorseSync] Giro ({Innesco}) fallito: {Msg}", innesco, voce.Dettaglio);
        }
        finally
        {
            orologio.Stop();
            voce.DurataMs = (int)Math.Min(orologio.ElapsedMilliseconds, int.MaxValue);
            LastRun = runUtc;
            IsSyncing = false;
            // Contatori per le colonne di res_sync_log, da quello che è stato fatto davvero (anche a giro fallito).
            var contatori = new ContatoriGiro(
                RighePm: allocazioni.RighePm,
                RigheVps: allocazioni.Iniziato ? allocazioni.RigheVps : righeVps,
                CreatePm: allocazioni.CreatePm,
                CreateVps: anagrafiche.CreateVps + allocazioni.CreateVps,
                AggiornatePm: allocazioni.AggiornatePm,
                AggiornateVps: anagrafiche.AggiornateVps + allocazioni.AggiornateVps,
                CancellatePm: allocazioni.CancellatePm,
                CancellateVps: allocazioni.CancellateVps,
                Conflitti: allocazioni.Conflitti.Count,
                Saltate: anagrafiche.Saltate + anagrafiche.SaltateNote + allocazioni.NumeroSaltate + allocazioni.SaltateNote);
            // «Modifiche» = scritture sul VPS, in PM o sulla mappa in questo giro: righe
            // create/aggiornate/cancellate, abbinamenti (del seme e per contenuto), impronte
            // riallineate, la PUT dei reparti (il VPS risponde 0/0 se cambiano solo i legami) e
            // le righe saltate NUOVE. Quelle già segnalate no: se resta 0 un giro del timer non
            // lascia riga nel registro. Vedi Registra.
            int modifiche = anagrafiche.CreateVps + anagrafiche.AggiornateVps + anagrafiche.Saltate
                            + anagrafiche.Abbinati + (anagrafiche.RepartiInviati ? 1 : 0)
                            + allocazioni.Modifiche;
            Registra(voce, modifiche, contatori);
            _giro.Release();
        }

        if (voce.Esito == "ok")
            _logger.LogInformation("[RisorseSync] Giro ({Innesco}) ok in {Ms} ms: {Dettaglio}",
                innesco, voce.DurataMs, voce.Dettaglio);
        return voce;
    }

    // ── Fase 1: anagrafiche PM → VPS ─────────────────────────────

    /// <summary>
    /// Fase 1 — anagrafiche PM → VPS (PIANO-SYNC-RISORSE.md §4.1 punto 2, §5). In ordine:
    /// <list type="number">
    /// <item>legge da MySQL dipendenti (senza ADMIN/SYNC/admin e senza le wildcard «[PM]
    /// Generico»…), reparti, legami, commesse e la mappa EMPLOYEE/PROJECT;</item>
    /// <item><b>seme</b>: se ci sono dipendenti PM interni non mappati chiede l'elenco al VPS e
    /// li abbina (<see cref="AnagraficheSync.Abbina"/>); le coppie trovate entrano nella mappa
    /// con impronta vuota, così partono subito al passo dopo;</item>
    /// <item>dipendenti: mappati o interni, solo quelli cambiati (o tutti se l'ultimo invio
    /// completo ha più di 24 ore) e non già saltati dal VPS con lo stesso dato
    /// (<see cref="_saltateNote"/>); esiti riga per riga → mappa + impronta;</item>
    /// <item>reparti + legami dei mappati, se l'impronta del payload è cambiata;</item>
    /// <item>commesse: mappate o ACTIVE, stessa regola dei dipendenti;</item>
    /// <item>segnalibro dell'invio completo.</item>
    /// </list>
    /// <para>Mai cancellazioni, in nessun verso. Una riga <c>skipped</c> dal VPS si conta e si
    /// scrive nel dettaglio ma NON fa fallire il giro; un errore di rete o una risposta
    /// <c>Success=false</c> sì (sale al chiamante col messaggio leggibile del client).
    /// L'<paramref name="esito"/> lo dà il chiamante e si riempie passo dopo passo: a giro
    /// fallito a metà racconta comunque quello che è stato fatto.</para>
    /// </summary>
    private async Task SyncAnagraficheAsync(RisorseSyncClient client, EsitoAnagrafiche esito, string innesco, CancellationToken ct)
    {
        esito.Iniziato = true;
        // A) lettura da MySQL
        List<DipendentePm> dipendenti;
        List<RepartoPm> reparti;
        List<LegamePm> legami;
        List<CommessaPm> commesse;
        Dictionary<int, RisorseSyncMap.Voce> mappaDip;
        Dictionary<int, RisorseSyncMap.Voce> mappaCom;
        using (MySqlConnection c = _rdb.Open())
        {
            dipendenti = c.Query<DipendentePm>(@"
                SELECT id AS Id, first_name AS FirstName, last_name AS LastName, email AS Email,
                       COALESCE(NULLIF(emp_type, ''), 'INTERNAL') AS EmpType,
                       COALESCE(NULLIF(status, ''), 'ACTIVE') AS Status,
                       COALESCE(NULLIF(user_role, ''), 'TECH') AS UserRole,
                       username AS Username, password_hash AS PasswordHash
                FROM employees
                WHERE COALESCE(user_role, '') NOT IN ('ADMIN', 'SYNC')
                  AND COALESCE(username, '') <> 'admin'
                  AND first_name NOT LIKE '[%'
                ORDER BY id").ToList();
            // `[…`: le wildcard di reparto («[PM] Generico», …) che MoMDbService semina a ogni
            // avvio. Non sono risorse del planner e sul VPS il prefisso «[» è la convenzione degli
            // account di sistema, che non aggiorna mai: create una volta tornerebbero «skipped»
            // a ogni invio completo.
            reparti = c.Query<RepartoPm>(@"
                SELECT id AS Id, code AS Code, name AS Name, COALESCE(sort_order, 0) AS SortOrder,
                       COALESCE(is_active, 1) AS IsActive
                FROM departments ORDER BY code").ToList();
            legami = c.Query<LegamePm>(@"
                SELECT employee_id AS EmployeeId, department_id AS DepartmentId,
                       COALESCE(is_responsible, 0) AS IsResponsible, COALESCE(is_primary, 0) AS IsPrimary
                FROM employee_departments").ToList();
            commesse = c.Query<CommessaPm>(@"
                SELECT id AS Id, code AS Code, title AS Title, COALESCE(NULLIF(status, ''), 'DRAFT') AS Status
                FROM projects ORDER BY id").ToList();

            // B) la mappa
            mappaDip = RisorseSyncMap.Carica(c, RisorseSyncMap.Employee);
            mappaCom = RisorseSyncMap.Carica(c, RisorseSyncMap.Project);
        }

        DateTime? ultimoCompleto = ParseLastRun(_store.LeggiChiave(ChiaveInvioCompletoAlle));
        bool invioCompleto = AnagraficheSync.ServeInvioCompleto(ultimoCompleto, DateTime.UtcNow);
        esito.InvioCompleto = invioCompleto;
        // All'invio completo si riprova tutto, anche quello che il VPS ha già rifiutato; idem
        // quando l'operatore preme «Sincronizza adesso»: ha appena sistemato la causa sul VPS.
        if (invioCompleto || innesco == "manuale") _saltateNote.Clear();

        // C) seme della mappa dipendenti — solo se c'è un INTERNO non mappato: un esterno non
        // mappato non parte comunque (passo D) e terrebbe accesa la GET a ogni giro.
        if (dipendenti.Any(d => !mappaDip.ContainsKey(d.Id) && Interno(d)
                                && !_saltateNote.ContainsKey((RisorseSyncMap.Employee, d.Id))))
        {
            List<SyncEmployeeDto> vps = await client.GetEmployeesAsync(ct);
            EsitoAbbinamento ab = AnagraficheSync.Abbina(dipendenti, vps, mappaDip);
            if (ab.Abbinamenti.Count > 0)
            {
                using MySqlConnection c = _rdb.Open();
                foreach (Abbinamento a in ab.Abbinamenti)
                {
                    // Impronta vuota: «mappato ma mai inviato», al passo D parte comunque.
                    RisorseSyncMap.Salva(c, RisorseSyncMap.Employee, a.LocalId, a.RemoteId, null);
                    mappaDip[a.LocalId] = new RisorseSyncMap.Voce(a.RemoteId, null);
                    _saltateNote.Remove((RisorseSyncMap.Employee, a.LocalId)); // da creare → da aggiornare: si riprova
                    _logger.LogInformation("[RisorseSync] Seme: dipendente PM {Local} → VPS {Remote} (per {Criterio}).",
                        a.LocalId, a.RemoteId, a.Criterio);
                }
                esito.Abbinati = ab.Abbinamenti.Count;
            }
            // Gli interni non abbinati vengono creati al passo D e contati fra i «creati»: nel
            // dettaglio restano solo gli esterni, che non partono e quindi vanno nominati.
            esito.NonAbbinatiPm.AddRange(ab.NonAbbinatiPm
                .Where(d => !Interno(d))
                .Select(d => $"{d.FirstName} {d.LastName}".Trim() + " (esterno)"));
            // Gli account «[…]» del VPS (di sistema o wildcard) e admin non sono persone da abbinare.
            esito.SoloVps.AddRange(ab.SoloVps
                .Where(v => !(v.FirstName ?? "").StartsWith('[') && !string.Equals(v.Username, "admin", StringComparison.OrdinalIgnoreCase))
                .Select(v => $"{v.FirstName} {v.LastName}".Trim()));
        }

        // D) dipendenti
        List<RigaDaInviare<SyncEmployeeDto>> righeDip = AnagraficheSync.DipendentiDaInviare(dipendenti, mappaDip, invioCompleto);
        int candidatiDip = dipendenti.Count(d => mappaDip.ContainsKey(d.Id) || Interno(d));
        esito.DipInvariati += candidatiDip - righeDip.Count;   // uguali all'ultimo invio: non partiti
        righeDip = SenzaLeGiaSaltate(RisorseSyncMap.Employee, righeDip, esito.GiaSegnalate);
        if (righeDip.Count > 0)
        {
            List<SyncUpsertResultDto> esiti = await client.UpsertEmployeesAsync(righeDip.Select(r => r.Dto).ToList(), ct);
            using MySqlConnection c = _rdb.Open();
            Conteggio n = ApplicaEsiti(c, RisorseSyncMap.Employee, "dipendente", righeDip, esiti, mappaDip,
                r => $"{r.Dto.FirstName} {r.Dto.LastName}".Trim(), esito.Errori);
            esito.DipCreati += n.Creati;
            esito.DipAggiornati += n.Aggiornati;
            esito.DipInvariati += n.Invariati;
            esito.DipSaltati += n.Saltati;
        }

        // E) reparti + legami dei soli dipendenti mappati
        SyncDepartmentsRequest payloadReparti = AnagraficheSync.CostruisciReparti(reparti, legami, mappaDip);
        string improntaReparti = AnagraficheSync.ImprontaReparti(payloadReparti);
        if (payloadReparti.Departments.Count > 0
            && (invioCompleto || improntaReparti != _store.LeggiChiave(ChiaveImprontaReparti)))
        {
            SyncCountsDto conteggi = await client.UpsertDepartmentsAsync(payloadReparti, ct);
            _store.ScriviChiave(ChiaveImprontaReparti, improntaReparti);
            esito.RepartiInviati = true;
            esito.RepartiCreati = conteggi.Created;
            esito.RepartiAggiornati = conteggi.Updated;
        }

        // F) commesse
        List<RigaDaInviare<SyncProjectDto>> righeCom = AnagraficheSync.CommesseDaInviare(commesse, mappaCom, invioCompleto);
        int candidatiCom = commesse.Count(p => mappaCom.ContainsKey(p.Id) || p.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase));
        esito.ComInvariate += candidatiCom - righeCom.Count;
        righeCom = SenzaLeGiaSaltate(RisorseSyncMap.Project, righeCom, esito.GiaSegnalate);
        if (righeCom.Count > 0)
        {
            List<SyncUpsertResultDto> esiti = await client.UpsertProjectsAsync(righeCom.Select(r => r.Dto).ToList(), ct);
            using MySqlConnection c = _rdb.Open();
            Conteggio n = ApplicaEsiti(c, RisorseSyncMap.Project, "commessa", righeCom, esiti, mappaCom,
                r => r.Dto.Code, esito.Errori);
            esito.ComCreate += n.Creati;
            esito.ComAggiornate += n.Aggiornati;
            esito.ComInvariate += n.Invariati;
            esito.ComSaltate += n.Saltati;
        }

        // G) segnalibro dell'invio completo: solo se tutti i passi sono arrivati in fondo.
        if (invioCompleto)
            _store.ScriviChiave(ChiaveInvioCompletoAlle, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private static bool Interno(DipendentePm d) => d.EmpType.Equals("INTERNAL", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Toglie dalle righe da inviare quelle che il VPS ha già saltato con la STESSA impronta
    /// (<see cref="_saltateNote"/>): si riprova solo se il dato cambia o all'invio completo, che
    /// svuota l'elenco a monte. Le tolte finiscono nel dettaglio come «già segnalate».
    /// </summary>
    private List<RigaDaInviare<T>> SenzaLeGiaSaltate<T>(string kind, List<RigaDaInviare<T>> righe, List<string> giaSegnalate)
    {
        if (_saltateNote.Count == 0) return righe;
        var tenute = new List<RigaDaInviare<T>>(righe.Count);
        foreach (RigaDaInviare<T> r in righe)
        {
            if (_saltateNote.TryGetValue((kind, r.LocalId), out (string Impronta, string Messaggio) nota) && nota.Impronta == r.Impronta)
                giaSegnalate.Add(nota.Messaggio);
            else
                tenute.Add(r);
        }
        return tenute;
    }

    private readonly record struct Conteggio(int Creati, int Aggiornati, int Invariati, int Saltati);

    /// <summary>
    /// Applica gli esiti riga per riga di una PUT (employees o projects) alla mappa:
    /// <c>created</c> (o un Id diverso da quello mappato) → coppia con l'Id ricevuto;
    /// <c>updated</c>/<c>unchanged</c> → impronta aggiornata; <c>skipped</c> → contata, con
    /// l'errore nel dettaglio, e la mappa resta com'è. Se l'Id ricevuto è già di un altro
    /// oggetto PM (la mappa è una biiezione) → warning nel log e nessuna mappatura.
    /// Ogni riga saltata (per qualunque motivo) si ricorda in <see cref="_saltateNote"/> con
    /// l'impronta con cui è partita; una riga andata bene se ne cancella.
    /// </summary>
    private Conteggio ApplicaEsiti<T>(
        MySqlConnection c, string kind, string cosa,
        List<RigaDaInviare<T>> righe, List<SyncUpsertResultDto> esiti,
        Dictionary<int, RisorseSyncMap.Voce> mappa,
        Func<RigaDaInviare<T>, string> nome, List<string> errori)
    {
        // remote → local, per accorgersi di un Id già in mano a un altro.
        var inversa = new Dictionary<int, int>();
        foreach (KeyValuePair<int, RisorseSyncMap.Voce> kv in mappa)
            inversa[kv.Value.RemoteId] = kv.Key;
        Dictionary<int, SyncUpsertResultDto> perIndice = esiti
            .GroupBy(e => e.Index).ToDictionary(g => g.Key, g => g.First());

        int creati = 0, aggiornati = 0, invariati = 0, saltati = 0;
        for (int i = 0; i < righe.Count; i++)
        {
            RigaDaInviare<T> riga = righe[i];
            string etichetta = $"{cosa} {nome(riga)}";
            if (!perIndice.TryGetValue(i, out SyncUpsertResultDto? e))
            {
                saltati++;
                Salta(riga, $"{etichetta}: nessun esito dal VPS");
                continue;
            }
            string azione = (e.Action ?? "").ToLowerInvariant();
            if (azione == "skipped")
            {
                saltati++;
                Salta(riga, $"{etichetta}: {(string.IsNullOrWhiteSpace(e.Error) ? "saltato dal VPS" : e.Error)}");
                continue;
            }

            int? remoto = e.Id ?? (mappa.TryGetValue(riga.LocalId, out RisorseSyncMap.Voce v) ? v.RemoteId : null);
            if (remoto == null)
            {
                saltati++;
                Salta(riga, $"{etichetta}: il VPS non ha restituito l'id");
                continue;
            }
            if (inversa.TryGetValue(remoto.Value, out int altroLocale) && altroLocale != riga.LocalId)
            {
                saltati++;
                string msg = $"{etichetta}: l'id VPS {remoto} è già mappato a {cosa} PM {altroLocale}";
                Salta(riga, msg);
                _logger.LogWarning("[RisorseSync] {Msg}: nessuna mappatura ({Kind} {Local}).", msg, kind, riga.LocalId);
                continue;
            }

            RisorseSyncMap.Salva(c, kind, riga.LocalId, remoto.Value, riga.Impronta);
            mappa[riga.LocalId] = new RisorseSyncMap.Voce(remoto.Value, riga.Impronta);
            inversa[remoto.Value] = riga.LocalId;
            _saltateNote.Remove((kind, riga.LocalId));
            switch (azione)
            {
                case "created": creati++; break;
                case "unchanged": invariati++; break;
                default: aggiornati++; break;
            }
        }
        return new Conteggio(creati, aggiornati, invariati, saltati);

        // Nel dettaglio di questo giro e fra le già segnalate per i giri dopo.
        void Salta(RigaDaInviare<T> riga, string messaggio)
        {
            errori.Add(messaggio);
            _saltateNote[(kind, riga.LocalId)] = (riga.Impronta, messaggio);
        }
    }

    /// <summary>Cosa ha fatto il giro delle anagrafiche: contatori per res_sync_log e frase per il dettaglio.</summary>
    internal sealed class EsitoAnagrafiche
    {
        /// <summary>True appena il passo delle anagrafiche è partito: un errore prima (login, stato) non deve descrivere passi mai eseguiti.</summary>
        public bool Iniziato;
        public bool InvioCompleto;
        public int Abbinati;
        public int DipCreati, DipAggiornati, DipInvariati, DipSaltati;
        public bool RepartiInviati;
        public int RepartiCreati, RepartiAggiornati;
        public int ComCreate, ComAggiornate, ComInvariate, ComSaltate;
        public List<string> NonAbbinatiPm { get; } = new();
        public List<string> SoloVps { get; } = new();
        /// <summary>Le righe saltate dal VPS in QUESTO giro, col loro messaggio.</summary>
        public List<string> Errori { get; } = new();
        /// <summary>Le righe già saltate in un giro precedente con lo stesso dato: non rimandate, col messaggio di allora.</summary>
        public List<string> GiaSegnalate { get; } = new();

        public int CreateVps => DipCreati + RepartiCreati + ComCreate;
        public int AggiornateVps => DipAggiornati + RepartiAggiornati + ComAggiornate;
        /// <summary>Saltate nuove di questo giro.</summary>
        public int Saltate => DipSaltati + ComSaltate;
        /// <summary>Saltate già segnalate in un giro precedente e non rimandate.</summary>
        public int SaltateNote => GiaSegnalate.Count;

        /// <summary>Il primo messaggio di una riga rifiutata (nuova o già segnalata), null se il VPS ha preso tutto: va in <c>LastError</c>.</summary>
        public string? PrimoErrore() => Errori.Count > 0 ? Errori[0] : GiaSegnalate.Count > 0 ? GiaSegnalate[0] : null;

        /// <summary>Es. «Anagrafiche: dipendenti 2 creati / 3 aggiornati / 33 invariati, reparti inviati, commesse 15 create; non abbinati PM: …; solo VPS: …».</summary>
        public string Dettaglio()
        {
            var sb = new StringBuilder("Anagrafiche");
            if (InvioCompleto) sb.Append(" (invio completo)");
            sb.Append(": dipendenti ").Append(Riassunto(DipCreati, DipAggiornati, DipInvariati, DipSaltati, "creati", "aggiornati", "invariati", "saltati"));
            if (Abbinati > 0) sb.Append(" (").Append(Abbinati).Append(" abbinati)");
            sb.Append(", reparti ").Append(RepartiInviati ? "inviati" : "invariati");
            sb.Append(", commesse ").Append(Riassunto(ComCreate, ComAggiornate, ComInvariate, ComSaltate, "create", "aggiornate", "invariate", "saltate"));
            if (NonAbbinatiPm.Count > 0) sb.Append("; non abbinati PM: ").Append(string.Join(", ", NonAbbinatiPm));
            if (SoloVps.Count > 0) sb.Append("; solo VPS: ").Append(string.Join(", ", SoloVps));
            if (Errori.Count > 0) sb.Append("; errori: ").Append(string.Join("; ", Errori));
            if (GiaSegnalate.Count > 0)
                sb.Append("; saltate (già segnalate): ").Append(GiaSegnalate.Count).Append(" — ").Append(string.Join("; ", GiaSegnalate));
            return sb.ToString();
        }

        private static string Riassunto(int creati, int aggiornati, int invariati, int saltati,
            string pCreati, string pAggiornati, string pInvariati, string pSaltati)
        {
            var parti = new List<string>();
            if (creati > 0) parti.Add($"{creati} {pCreati}");
            if (aggiornati > 0) parti.Add($"{aggiornati} {pAggiornati}");
            if (invariati > 0 || parti.Count == 0) parti.Add($"{invariati} {pInvariati}");
            if (saltati > 0) parti.Add($"{saltati} {pSaltati}");
            return string.Join(" / ", parti);
        }
    }

    // ── Fase 2: allocazioni nei due versi ────────────────────────

    /// <summary>
    /// Fase 2 — allocazioni nei due versi (PIANO-SYNC-RISORSE.md §4.1 punto 3, §4.3). In ordine:
    /// <list type="number">
    /// <item>legge tutte le righe di <c>res_assignments</c>, tutte quelle del VPS
    /// (<c>GET /api/sync/assignments</c>) e le mappe EMPLOYEE, PROJECT, ASSIGNMENT;</item>
    /// <item>le porta nella forma comune (<see cref="RigaAlloc"/>, id di PM, ore in UTC); le
    /// righe di un dipendente non mappato o su una commessa non mappata — da una parte o
    /// dall'altra — si <b>saltano</b>: contate, nominate una volta, e fuori da tutto il resto
    /// (niente copie, niente cancellazioni); un dipendente cancellato in PM lascia le sue
    /// allocazioni sul VPS e libera le coppie ASSIGNMENT orfane;</item>
    /// <item>per ogni coppia della mappa <see cref="AllocazioniSync.Decidi"/>; le righe senza
    /// mappa prima si abbinano per contenuto (<see cref="AllocazioniSync.AbbinaPerContenuto"/>:
    /// stessa allocazione già su entrambi i lati → solo mappa), poi le PM rimaste si creano sul
    /// VPS e le VPS rimaste in PM;</item>
    /// <item><b>prima il VPS</b>: una POST con creazioni e aggiornamenti (esiti riga per riga →
    /// mappa e impronta, come per le anagrafiche, <see cref="ApplicaEsiti{T}"/>) e una POST di
    /// cancellazione con l'autore preso da <c>res_notify_pending</c>;</item>
    /// <item><b>poi PM</b>, in UNA transazione con un SAVEPOINT per riga: INSERT / UPDATE (con
    /// l'<c>updated_at</c> del VPS portato in ora locale, mai NOW()) / DELETE (annotata prima in
    /// <c>res_notify_pending</c> per il digest, come fa <c>ResourcesController</c>) + mappa;
    /// UPDATE e DELETE solo se l'<c>updated_at</c> è ancora quello letto (guardia di
    /// concorrenza); dopo il commit l'hub di PM riceve <c>AssignmentsChanged</c> come per una
    /// modifica a mano.</item>
    /// </list>
    /// <para>Due freni contro il ripristino accidentale, con lo stesso minimo
    /// (<see cref="SogliaCancellazioniPm"/> coppie mappate): un VPS che risponde 0 allocazioni con
    /// la mappa piena, o che ne ha perse più di metà in un giro, ferma il giro con un errore
    /// leggibile (solo «Sincronizza adesso» procede); sotto il minimo la cancellazione riga per
    /// riga procede da sola.</para>
    /// <para>L'ordine VPS → PM fa sì che una riga creata sul VPS abbia sempre la sua mappa prima
    /// che si tocchi PM; se la POST riesce ma la mappa non si salva, al giro dopo l'abbinamento
    /// per contenuto la ritrova senza duplicarla. Un errore di rete o <c>Success=false</c> fa
    /// fallire il giro (nessuna scrittura in PM); una riga <c>skipped</c> si conta e si scrive nel
    /// dettaglio, e non riparte finché il dato non cambia (<see cref="_saltateNote"/>).</para>
    /// </summary>
    private async Task SyncAllocazioniAsync(RisorseSyncClient client, EsitoAllocazioni esito, string innesco, CancellationToken ct)
    {
        esito.Iniziato = true;

        // A) lettura: righe PM (col nome del dipendente e il codice della commessa per il
        // dettaglio), righe VPS, mappe. EMPLOYEE e PROJECT solo con l'oggetto ancora vivo in PM:
        // la mappa non ha FK e una coppia orfana farebbe violare fk_resa_emp/fk_resa_project a ogni giro.
        List<AllocazionePm> righePm;
        Dictionary<int, string> nomi;
        Dictionary<int, RisorseSyncMap.Voce> mappaDip, mappaCom, mappaAll;
        using (MySqlConnection c = _rdb.Open())
        {
            righePm = c.Query<AllocazionePm>(@"
                SELECT a.id AS Id, a.employee_id AS EmployeeId, a.tipo AS Tipo,
                       a.data_inizio AS DataInizio, a.data_fine AS DataFine, a.project_id AS ProjectId,
                       a.descrizione AS Descrizione, a.updated_by AS UpdatedBy, a.updated_at AS UpdatedAt,
                       CONCAT_WS(' ', e.first_name, e.last_name) AS Dipendente, p.code AS Commessa
                FROM res_assignments a
                LEFT JOIN employees e ON e.id = a.employee_id
                LEFT JOIN projects p ON p.id = a.project_id
                ORDER BY a.id").ToList();
            nomi = c.Query<(int Id, string? Nome)>("SELECT id, CONCAT_WS(' ', first_name, last_name) FROM employees")
                .ToDictionary(x => x.Id, x => (x.Nome ?? "").Trim());
            mappaDip = RisorseSyncMap.Carica(c, RisorseSyncMap.Employee, "employees");
            mappaCom = RisorseSyncMap.Carica(c, RisorseSyncMap.Project, "projects");
            mappaAll = RisorseSyncMap.Carica(c, RisorseSyncMap.Assignment);
        }
        List<SyncAssignmentDto> righeVps = await client.GetAssignmentsAsync(ct);
        esito.RighePm = righePm.Count;
        esito.RigheVps = righeVps.Count;

        // Freno: un VPS che risponde «zero allocazioni» con la mappa piena non è una cancellazione
        // riga per riga (§4.3), è un database ripristinato vuoto o un bug di là. Il giro si ferma
        // con un errore leggibile e non tocca niente; solo «Sincronizza adesso» dal pannello, che è
        // una scelta dell'operatore, procede. Stesso minimo del freno gemello più sotto: sotto
        // SogliaCancellazioniPm coppie non c'è «massa» da proteggere, e cancellare l'ultima
        // allocazione sul VPS è una cancellazione legittima, che procede da sola.
        if (righeVps.Count == 0 && mappaAll.Count >= SogliaCancellazioniPm && innesco != "manuale")
            throw new RisorseSyncException(
                $"Il VPS ha risposto 0 allocazioni con {mappaAll.Count} righe mappate: giro fermato per non cancellare tutto in PM " +
                "(se è voluto, «Sincronizza adesso» dal pannello procede).");

        Dictionary<int, int> dipVpsPm = AllocazioniSync.Inversa(mappaDip);
        Dictionary<int, int> comVpsPm = AllocazioniSync.Inversa(mappaCom);
        Dictionary<int, int> allVpsPm = AllocazioniSync.Inversa(mappaAll);
        // L'updated_at letto adesso: la guardia di concorrenza delle scritture in PM (passo E).
        Dictionary<int, DateTime?> vistoPm = righePm.ToDictionary(a => a.Id, a => a.UpdatedAt);

        string Etichetta(RigaAlloc r) =>
            $"{(nomi.TryGetValue(r.EmployeeId, out string? nome) && nome.Length > 0 ? nome : "dipendente " + r.EmployeeId)} {AllocazioniSync.Periodo(r)}";

        // B) forma comune; le righe di un dipendente non mappato si saltano (§4.3: mai cancellate),
        // e così quelle su una commessa non mappata, da entrambi i lati: mandarle senza commessa
        // farebbe «vincere» al giro dopo la copia dell'altro lato, che perderebbe il legame.
        // Ripartono da sole quando la Fase 1 mappa la commessa.
        var pm = new Dictionary<int, RigaAlloc>();
        var saltatePm = new HashSet<int>();
        var nomiSaltati = new SortedSet<string>(StringComparer.Ordinal);
        var commesseSaltate = new SortedSet<string>(StringComparer.Ordinal);
        foreach (AllocazionePm a in righePm)
        {
            if (!mappaDip.ContainsKey(a.EmployeeId))
            {
                saltatePm.Add(a.Id);
                nomiSaltati.Add(string.IsNullOrWhiteSpace(a.Dipendente) ? $"dipendente PM {a.EmployeeId}" : a.Dipendente.Trim());
                continue;
            }
            // Una FERIE perde comunque la commessa nella forma comune: non la ferma.
            if (a.ProjectId is int p && !mappaCom.ContainsKey(p) && AllocazioniSync.NormalizzaTipo(a.Tipo) != "FERIE")
            {
                saltatePm.Add(a.Id);
                commesseSaltate.Add(string.IsNullOrWhiteSpace(a.Commessa) ? $"commessa PM {p}" : a.Commessa.Trim());
                continue;
            }
            pm[a.Id] = AllocazioniSync.DaPm(a);
        }
        // Lato VPS, a specchio: dipendente VPS non mappato, o commessa VPS non mappata (tipo ≠ FERIE),
        // → riga saltata, mai scritta in PM. Riparte da sola quando l'oggetto entra in mappa.
        // Un dipendente CANCELLATO in PM (hard delete, caso raro: di norma si mette TERMINATED)
        // porta via le sue allocazioni in cascata e la sua coppia EMPLOYEE esce dalla mappa (JOIN
        // del passo A): REGOLA SCELTA = le sue allocazioni sul VPS NON si cancellano (restano di là,
        // nominate «dipendente VPS N non mappato»), ma le coppie ASSIGNMENT rimaste orfane — local_id
        // che non esiste più in res_assignments — si tolgono dalla mappa nello stesso giro (passo C),
        // così non restano orfane per sempre.
        var vps = new Dictionary<int, RigaAlloc>();
        var saltateVps = new HashSet<int>();
        var dipendentiVpsSaltati = new SortedSet<int>();
        var commesseVpsSaltate = new SortedSet<int>();
        foreach (SyncAssignmentDto v in righeVps)
        {
            if (!dipVpsPm.ContainsKey(v.EmployeeId))
            {
                saltateVps.Add(v.Id);
                dipendentiVpsSaltati.Add(v.EmployeeId);
                continue;
            }
            if (v.ProjectId is int p && !comVpsPm.ContainsKey(p) && AllocazioniSync.NormalizzaTipo(v.Tipo) != "FERIE")
            {
                saltateVps.Add(v.Id);
                commesseVpsSaltate.Add(p);
                continue;
            }
            RigaAlloc? r = AllocazioniSync.DaVps(v, dipVpsPm, comVpsPm);
            if (r == null)
            {
                // Non dovrebbe succedere (i due casi sono già sopra): comunque saltata, mai azzerata.
                saltateVps.Add(v.Id);
                dipendentiVpsSaltati.Add(v.EmployeeId);
                continue;
            }
            vps[v.Id] = r;
        }
        esito.SaltateNonMappate = saltatePm.Count + saltateVps.Count;
        foreach (string n in nomiSaltati) esito.Saltate.Add($"dipendente non mappato: {n}");
        foreach (string n in commesseSaltate) esito.Saltate.Add($"commessa non mappata: {n}");
        foreach (int id in dipendentiVpsSaltati) esito.Saltate.Add($"dipendente VPS {id} non mappato");
        foreach (int id in commesseVpsSaltate) esito.Saltate.Add($"commessa VPS {id} non mappata");

        // C) decisioni: le coppie mappate con le regole di §4.3…
        var creaVps = new List<(int IdPm, RigaAlloc Riga)>();
        var aggiornaVps = new List<(int IdPm, int IdVps, RigaAlloc Riga)>();
        var cancellaVps = new List<(int IdPm, int IdVps)>();
        var creaPm = new List<(int IdVps, RigaAlloc Riga)>();
        // Conflitto = la frase «vince …» per il registro: per le scritture in PM viaggia nella tupla
        // e si conta SOLO a scrittura riuscita (passo E); se la guardia di concorrenza la rimanda,
        // nessuno ha vinto niente.
        var aggiornaPm = new List<(int IdPm, int IdVps, RigaAlloc Riga, DateTime? Visto, string? Conflitto)>();
        var cancellaPm = new List<(int IdPm, int IdVps, RigaAlloc Riga, DateTime? Visto, string? Conflitto)>();
        var soloImpronta = new List<(int IdPm, int IdVps, string Impronta)>();
        var mappeDaTogliere = new List<int>();
        var idPmEsistenti = righePm.Select(a => a.Id).ToHashSet();
        // Frasi «vince …» del verso VPS, per id PM: entrano nel registro solo dopo l'esito della POST.
        var conflittoVps = new Dictionary<int, string>();

        foreach ((int idPm, RisorseSyncMap.Voce voce) in mappaAll)
        {
            // Coppia orfana: la riga PM non esiste più (dipendente cancellato in PM, allocazioni in
            // cascata) e quella VPS è saltata (dipendente o commessa non mappati): la riga VPS resta
            // di là, la mappa se ne va — vedi la regola al passo B.
            if (!idPmEsistenti.Contains(idPm) && saltateVps.Contains(voce.RemoteId))
            {
                mappeDaTogliere.Add(idPm);
                continue;
            }
            // Un lato saltato (dipendente o commessa non mappati) tira fuori tutta la coppia: né copie né cancellazioni.
            if (saltatePm.Contains(idPm) || saltateVps.Contains(voce.RemoteId)) continue;
            RigaAlloc? rPm = pm.GetValueOrDefault(idPm);
            RigaAlloc? rVps = vps.GetValueOrDefault(voce.RemoteId);
            (AzioneMerge azione, bool conflitto) = AllocazioniSync.Decidi(rPm, rVps, voce.SyncedHash);
            string? fraseConflitto = !conflitto ? null : azione switch
            {
                AzioneMerge.AggiornaPm => $"vince il VPS: {Etichetta(rVps!)}",
                AzioneMerge.AggiornaVps => $"vince PM: {Etichetta(rPm!)}",
                AzioneMerge.CancellaPm => $"cancellata sul VPS e modificata in PM, vince la cancellazione: {Etichetta(rPm!)}",
                AzioneMerge.CancellaVps => $"cancellata in PM e modificata sul VPS, vince la cancellazione: {Etichetta(rVps!)}",
                _ => $"{azione}: {Etichetta(rPm ?? rVps!)}",
            };
            // Verso il VPS il conflitto si racconta solo quando la POST ha davvero scritto (created,
            // updated, unchanged, deleted, missing): una riga saltata dal VPS o trattenuta perché già
            // rifiutata non ha vinto niente, si ridecide al giro dopo.
            if (fraseConflitto != null && azione is AzioneMerge.AggiornaVps or AzioneMerge.CancellaVps)
                conflittoVps[idPm] = fraseConflitto;
            switch (azione)
            {
                case AzioneMerge.Niente: esito.Invariate++; break;
                case AzioneMerge.AggiornaHash:
                    if (rPm == null) mappeDaTogliere.Add(idPm);                       // sparite entrambe
                    else soloImpronta.Add((idPm, voce.RemoteId, AllocazioniSync.Impronta(rPm)));
                    break;
                case AzioneMerge.AggiornaPm: aggiornaPm.Add((idPm, voce.RemoteId, rVps!, vistoPm.GetValueOrDefault(idPm), fraseConflitto)); break;
                case AzioneMerge.AggiornaVps: aggiornaVps.Add((idPm, voce.RemoteId, rPm!)); break;
                case AzioneMerge.CancellaPm: cancellaPm.Add((idPm, voce.RemoteId, rPm!, vistoPm.GetValueOrDefault(idPm), fraseConflitto)); break;
                case AzioneMerge.CancellaVps: cancellaVps.Add((idPm, voce.RemoteId)); break;
            }
        }

        // Stesso freno, a metà strada: più di metà delle coppie mappate sparite dal VPS in un giro
        // solo (con un minimo, per non fermarsi su tre righe) è un ripristino, non una pulizia.
        if (innesco != "manuale" && cancellaPm.Count >= SogliaCancellazioniPm && cancellaPm.Count * 2 > mappaAll.Count)
            throw new RisorseSyncException(
                $"Sul VPS mancano {cancellaPm.Count} allocazioni su {mappaAll.Count} mappate: giro fermato per non cancellarle in PM " +
                "(se è voluto, «Sincronizza adesso» dal pannello procede).");

        // …e le righe senza mappa: prima l'abbinamento per contenuto (solo mappa), poi le creazioni.
        List<(int Id, RigaAlloc Riga)> pmLibere = pm.Where(kv => !mappaAll.ContainsKey(kv.Key)).Select(kv => (kv.Key, kv.Value)).ToList();
        List<(int Id, RigaAlloc Riga)> vpsLibere = vps.Where(kv => !allVpsPm.ContainsKey(kv.Key)).Select(kv => (kv.Key, kv.Value)).ToList();
        List<(int IdPm, int IdVps, string Impronta)> abbinate = AllocazioniSync.AbbinaPerContenuto(pmLibere, vpsLibere);
        if (abbinate.Count > 0)
        {
            using MySqlConnection c = _rdb.Open();
            foreach ((int idPm, int idVps, string impronta) in abbinate)
            {
                RisorseSyncMap.Salva(c, RisorseSyncMap.Assignment, idPm, idVps, impronta);
                mappaAll[idPm] = new RisorseSyncMap.Voce(idVps, impronta);
                _saltateNote.Remove((RisorseSyncMap.Assignment, idPm));
                _saltateNote.Remove((KindAllocazionePm, idVps));
            }
            esito.Abbinati = abbinate.Count;
        }
        var abbinatePm = abbinate.Select(a => a.IdPm).ToHashSet();
        var abbinateVps = abbinate.Select(a => a.IdVps).ToHashSet();
        creaVps.AddRange(pmLibere.Where(x => !abbinatePm.Contains(x.Id)));
        creaPm.AddRange(vpsLibere.Where(x => !abbinateVps.Contains(x.Id)));

        // D) verso il VPS: una POST con creazioni (Id null) e aggiornamenti (Id), esiti → mappa
        var righeVerso = new List<RigaDaInviare<SyncAssignmentUpsertDto>>();
        foreach ((int idPm, RigaAlloc riga) in creaVps)
            righeVerso.Add(new RigaDaInviare<SyncAssignmentUpsertDto>(idPm,
                AllocazioniSync.VersoVps(riga, null, mappaDip, mappaCom), AllocazioniSync.Impronta(riga), false));
        foreach ((int idPm, int idVps, RigaAlloc riga) in aggiornaVps)
            righeVerso.Add(new RigaDaInviare<SyncAssignmentUpsertDto>(idPm,
                AllocazioniSync.VersoVps(riga, idVps, mappaDip, mappaCom), AllocazioniSync.Impronta(riga), true));
        righeVerso = SenzaLeGiaSaltate(RisorseSyncMap.Assignment, righeVerso, esito.GiaSegnalate);
        if (righeVerso.Count > 0)
        {
            List<SyncUpsertResultDto> esiti = await client.UpsertAssignmentsAsync(righeVerso.Select(r => r.Dto).ToList(), ct);
            using MySqlConnection c = _rdb.Open();
            Conteggio n = ApplicaEsiti(c, RisorseSyncMap.Assignment, "allocazione", righeVerso, esiti, mappaAll,
                r => Etichetta(pm[r.LocalId]), esito.Errori);
            esito.CreateVps += n.Creati;
            esito.AggiornateVps += n.Aggiornati;
            esito.Riallineate += n.Invariati;   // «unchanged»: il VPS aveva già quel dato, la mappa sì è cambiata
            esito.SaltateVps += n.Saltati;
            // Conflitti vinti da PM: solo le righe che il VPS ha preso (created/updated/unchanged).
            Dictionary<int, string> presaPerIndice = esiti.GroupBy(e => e.Index)
                .ToDictionary(g => g.Key, g => (g.First().Action ?? "").ToLowerInvariant());
            for (int i = 0; i < righeVerso.Count; i++)
                if (conflittoVps.TryGetValue(righeVerso[i].LocalId, out string? frase)
                    && presaPerIndice.GetValueOrDefault(i) is "created" or "updated" or "unchanged")
                    esito.Conflitti.Add(frase);
        }

        // Cancellazioni sul VPS: l'autore è chi ha cancellato in PM (res_notify_pending, scritta
        // dalla DELETE del controller prima di perdere la riga), tradotto in id VPS; MadeBy è
        // unico per richiesta, quindi una POST per autore (di norma uno solo).
        if (cancellaVps.Count > 0)
        {
            Dictionary<int, int?> autori;
            using (MySqlConnection c = _rdb.Open())
                autori = c.Query<(int AssignmentId, int? MadeBy)>(
                        "SELECT assignment_id, made_by FROM res_notify_pending WHERE action = 'delete' AND assignment_id IN @Ids",
                        new { Ids = cancellaVps.Select(x => x.IdPm).ToList() })
                    .ToDictionary(x => x.AssignmentId, x => x.MadeBy);
            int? AutoreVps(int idPm) =>
                autori.TryGetValue(idPm, out int? madeBy) && madeBy is int m && m > 0
                && mappaDip.TryGetValue(m, out RisorseSyncMap.Voce autore) ? autore.RemoteId : null;

            foreach (IGrouping<int?, (int IdPm, int IdVps)> gruppo in cancellaVps.GroupBy(x => AutoreVps(x.IdPm)))
            {
                List<(int IdPm, int IdVps)> righe = gruppo.ToList();
                List<SyncUpsertResultDto> esiti = await client.DeleteAssignmentsAsync(
                    new SyncDeleteRequest { Ids = righe.Select(x => x.IdVps).ToList(), MadeBy = gruppo.Key }, ct);
                Dictionary<int, SyncUpsertResultDto> perIndice = esiti.GroupBy(e => e.Index).ToDictionary(g => g.Key, g => g.First());
                using MySqlConnection c = _rdb.Open();
                for (int i = 0; i < righe.Count; i++)
                {
                    SyncUpsertResultDto? e = perIndice.GetValueOrDefault(i);
                    string azione = (e?.Action ?? "").ToLowerInvariant();
                    if (azione is "deleted" or "missing")
                    {
                        // «missing»: sul VPS non c'era già più — la mappa se ne va lo stesso.
                        RisorseSyncMap.Rimuovi(c, RisorseSyncMap.Assignment, righe[i].IdPm);
                        mappaAll.Remove(righe[i].IdPm);
                        if (azione == "deleted") esito.CancellateVps++; else esito.Riallineate++;
                        if (conflittoVps.TryGetValue(righe[i].IdPm, out string? frase)) esito.Conflitti.Add(frase);
                    }
                    else
                    {
                        esito.SaltateVps++;
                        esito.Errori.Add($"cancellazione sul VPS (id {righe[i].IdVps}): {(string.IsNullOrWhiteSpace(e?.Error) ? "nessun esito dal VPS" : e!.Error)}");
                    }
                }
            }
        }

        // E) in PM, tutto in una transazione: righe + mappa insieme. Ogni riga sta in un SAVEPOINT:
        // se MySQL la rifiuta (FK, dato fuori misura) si torna indietro di quella sola, si conta
        // fra le saltate e si prosegue — una riga storta non deve bloccare per sempre tutto il
        // verso VPS → PM. UPDATE e DELETE portano la guardia di concorrenza sull'updated_at letto
        // al passo A: se nel frattempo un utente ha salvato dal planner, la riga si lascia stare
        // (0 righe toccate → niente mappa) e il giro dopo la ridecide con le regole di §4.3.
        var creati = new List<int>();
        var aggiornati = new List<int>();
        var cancellati = new List<int>();
        if (creaPm.Count > 0 || aggiornaPm.Count > 0 || cancellaPm.Count > 0 || soloImpronta.Count > 0 || mappeDaTogliere.Count > 0)
        {
            using MySqlConnection c = _rdb.Open();
            using MySqlTransaction tx = c.BeginTransaction();

            // Torna true se la riga è stata scritta (e solo allora il conflitto deciso al passo C, se
            // c'era, entra nel registro con la sua frase «vince …»); false = rifiutata da MySQL
            // (contata, col messaggio, ricordata in _saltateNote con la sua impronta) o rimandata (la
            // scrittura ha risposto false: guardia di concorrenza — nessun conflitto contato, resta la
            // sola voce «si ridecide al prossimo»). In entrambi i casi si torna al SAVEPOINT.
            bool Scrivi(string etichetta, string? kindNota, int? idNota, string? impronta, string? conflitto, Func<bool> scrittura)
            {
                c.Execute("SAVEPOINT riga", transaction: tx);
                try
                {
                    if (scrittura())
                    {
                        c.Execute("RELEASE SAVEPOINT riga", transaction: tx);
                        if (kindNota != null && idNota is int ok) _saltateNote.Remove((kindNota, ok));
                        if (conflitto != null) esito.Conflitti.Add(conflitto);
                        return true;
                    }
                    c.Execute("ROLLBACK TO SAVEPOINT riga", transaction: tx);
                    esito.Rimandate.Add($"modificata in PM durante il giro, si ridecide al prossimo: {etichetta}");
                    return false;
                }
                catch (MySqlException ex)
                {
                    c.Execute("ROLLBACK TO SAVEPOINT riga", transaction: tx);
                    esito.SaltatePm++;
                    string messaggio = $"{etichetta}: non scrivibile in PM ({ex.Message})";
                    esito.Errori.Add(messaggio);
                    if (kindNota != null && idNota is int ko) _saltateNote[(kindNota, ko)] = (impronta ?? "", messaggio);
                    _logger.LogWarning(ex, "[RisorseSync] {Msg}", messaggio);
                    return false;
                }
            }

            // Una riga già rifiutata da MySQL con lo stesso dato non si riprova a ogni giro (come le «skipped» del VPS).
            bool GiaRifiutata(int idVps, string impronta)
            {
                if (!_saltateNote.TryGetValue((KindAllocazionePm, idVps), out (string Impronta, string Messaggio) nota) || nota.Impronta != impronta)
                    return false;
                esito.GiaSegnalate.Add(nota.Messaggio);
                return true;
            }

            foreach ((int idVps, RigaAlloc r) in creaPm)
            {
                string impronta = AllocazioniSync.Impronta(r);
                if (GiaRifiutata(idVps, impronta)) continue;
                Scrivi(Etichetta(r), KindAllocazionePm, idVps, impronta, null, () =>
                {
                    c.Execute(@"
                        INSERT INTO res_assignments
                            (employee_id, tipo, data_inizio, data_fine, project_id, service_id, other_activity_id,
                             descrizione, created_at, updated_by, updated_at)
                        VALUES (@EmployeeId, @Tipo, @DataInizio, @DataFine, @ProjectId, NULL, NULL,
                                @Descrizione, NOW(), @UpdatedBy, @UpdatedAt)",
                        ParametriPm(r), tx);
                    int id = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()", transaction: tx);
                    RisorseSyncMap.Salva(c, RisorseSyncMap.Assignment, id, idVps, impronta, tx);
                    creati.Add(id);
                    return true;
                });
            }
            foreach ((int idPm, int idVps, RigaAlloc r, DateTime? visto, string? conflitto) in aggiornaPm)
            {
                string impronta = AllocazioniSync.Impronta(r);
                if (GiaRifiutata(idVps, impronta)) continue;
                Scrivi(Etichetta(r), KindAllocazionePm, idVps, impronta, conflitto, () =>
                {
                    // service_id/other_activity_id non viaggiano: in PM restano com'erano. updated_at è
                    // quello del VPS (ora locale), MAI NOW(): al giro dopo deve valere lo stesso istante.
                    int righe = c.Execute(@"
                        UPDATE res_assignments SET
                            employee_id = @EmployeeId, tipo = @Tipo, data_inizio = @DataInizio, data_fine = @DataFine,
                            project_id = @ProjectId, descrizione = @Descrizione,
                            updated_by = @UpdatedBy, updated_at = @UpdatedAt
                        WHERE id = @Id AND updated_at <=> @Visto",
                        ParametriPm(r, idPm, visto), tx);
                    if (righe == 0) return false;
                    RisorseSyncMap.Salva(c, RisorseSyncMap.Assignment, idPm, idVps, impronta, tx);
                    aggiornati.Add(idPm);
                    return true;
                });
            }
            foreach ((int idPm, _, RigaAlloc r, DateTime? visto, string? conflitto) in cancellaPm)
            {
                Scrivi(Etichetta(r), null, null, null, conflitto, () =>
                {
                    // Come DeleteAssignment del controller: chi e cosa, per il digest, prima di perdere la
                    // riga. Chi ha cancellato sul VPS non si sa (la riga di là è già sparita): made_by NULL.
                    c.Execute(@"
                        INSERT INTO res_notify_pending
                            (assignment_id, made_by, action, orig_employee_id, orig_tipo, orig_data_inizio, orig_data_fine,
                             orig_project_id, orig_service_id, orig_other_activity_id, orig_descrizione)
                        SELECT id, NULL, 'delete', employee_id, tipo, data_inizio, data_fine,
                               project_id, service_id, other_activity_id, descrizione
                        FROM res_assignments WHERE id = @Id AND updated_at <=> @Visto
                        ON DUPLICATE KEY UPDATE made_by = VALUES(made_by), touched_at = NOW()",
                        new { Id = idPm, Visto = visto }, tx);
                    int righe = c.Execute("DELETE FROM res_assignments WHERE id = @Id AND updated_at <=> @Visto",
                        new { Id = idPm, Visto = visto }, tx);
                    if (righe == 0) return false;   // il SAVEPOINT riporta indietro anche la riga del digest
                    RisorseSyncMap.Rimuovi(c, RisorseSyncMap.Assignment, idPm, tx);
                    cancellati.Add(idPm);
                    return true;
                });
            }
            foreach ((int idPm, int idVps, string impronta) in soloImpronta)
                RisorseSyncMap.Salva(c, RisorseSyncMap.Assignment, idPm, idVps, impronta, tx);
            foreach (int idPm in mappeDaTogliere)
                RisorseSyncMap.Rimuovi(c, RisorseSyncMap.Assignment, idPm, tx);
            tx.Commit();

            esito.CreatePm = creati.Count;
            esito.AggiornatePm = aggiornati.Count;
            esito.CancellatePm = cancellati.Count;
            esito.Riallineate += soloImpronta.Count + mappeDaTogliere.Count;

            // Solo DOPO il commit: chi ha il planner aperto ricarica e legge righe già scritte.
            NotificaPlannerPm("create", creati);
            NotificaPlannerPm("update", aggiornati);
            NotificaPlannerPm("delete", cancellati);
        }

        if (esito.Modifiche > 0)
            _logger.LogInformation("[RisorseSync] Allocazioni ({Innesco}): {Dettaglio}", innesco, esito.Dettaglio());
    }

    /// <summary>
    /// I parametri di INSERT/UPDATE su res_assignments: date-solo come DateTime, <c>updated_at</c>
    /// in ora locale (o NULL); <paramref name="visto"/> è l'updated_at letto al passo A, per la
    /// guardia <c>updated_at &lt;=&gt; @Visto</c> dell'UPDATE.
    /// </summary>
    private static object ParametriPm(RigaAlloc r, int? id = null, DateTime? visto = null) => new
    {
        Id = id,
        Visto = visto,
        r.EmployeeId,
        r.Tipo,
        DataInizio = r.Inizio.ToDateTime(TimeOnly.MinValue),
        DataFine = r.Fine.ToDateTime(TimeOnly.MinValue),
        r.ProjectId,
        r.Descrizione,
        r.UpdatedBy,
        UpdatedAt = r.UpdatedAtUtc is DateTime t ? AllocazioniSync.LocaleDaUtc(t) : (DateTime?)null,
    };

    /// <summary>
    /// Lo stesso <c>AssignmentsChanged</c> di <c>ResourcesController.NotifyChange</c>, a TUTTI i
    /// client (l'autore qui è il motore, nessuno da escludere). Fire-and-forget: un hub che non
    /// risponde non deve far fallire un giro le cui righe sono già scritte.
    /// </summary>
    private void NotificaPlannerPm(string azione, List<int> ids)
    {
        if (_hubPm == null || ids.Count == 0) return;
        _ = InviaAsync();

        async Task InviaAsync()
        {
            try
            {
                await _hubPm.Clients.All.SendAsync("AssignmentsChanged",
                    new ResAssignmentChange { Action = azione, Ids = ids.ToList() }, _ctHost);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RisorseSync] Hub PM: notifica «{Azione}» non inviata: {Msg}", azione, ex.Message);
            }
        }
    }

    /// <summary>Cosa ha fatto il giro delle allocazioni: contatori per res_sync_log e frase per il dettaglio.</summary>
    internal sealed class EsitoAllocazioni
    {
        /// <summary>True appena il passo è partito: un errore prima (login, stato, anagrafiche) non deve descrivere passi mai eseguiti.</summary>
        public bool Iniziato;
        public int RighePm, RigheVps;
        public int CreatePm, CreateVps, AggiornatePm, AggiornateVps, CancellatePm, CancellateVps;
        /// <summary>Coppie uguali con la mappa già allineata.</summary>
        public int Invariate;
        /// <summary>Coppie abbinate per contenuto in questo giro (solo mappa).</summary>
        public int Abbinati;
        /// <summary>Scritture sulla sola mappa (impronte riallineate, mappe tolte, «unchanged»/«missing» dal VPS).</summary>
        public int Riallineate;
        /// <summary>Righe saltate a monte perché il dipendente o la commessa (da entrambi i lati) non sono mappati.</summary>
        public int SaltateNonMappate;
        /// <summary>Righe rifiutate dal VPS in QUESTO giro (skipped, cancellazione senza esito).</summary>
        public int SaltateVps;
        /// <summary>Righe del VPS che MySQL ha rifiutato in QUESTO giro (FK, dato fuori misura): tornate indietro col SAVEPOINT.</summary>
        public int SaltatePm;
        /// <summary>UPDATE/DELETE in PM non fatti perché la riga è cambiata in PM durante il giro: si ridecidono al prossimo.</summary>
        public int RimandatePm => Rimandate.Count;
        /// <summary>I conflitti di questo giro, con chi ha vinto (in PM solo a scrittura riuscita).</summary>
        public List<string> Conflitti { get; } = new();
        /// <summary>Le righe rimandate dalla guardia di concorrenza, con l'etichetta: non sono conflitti, nessuno ha vinto.</summary>
        public List<string> Rimandate { get; } = new();
        /// <summary>Le righe rifiutate dal VPS in QUESTO giro, col loro messaggio.</summary>
        public List<string> Errori { get; } = new();
        /// <summary>I motivi delle righe saltate a monte (dipendente o commessa non mappati), una voce per oggetto.</summary>
        public List<string> Saltate { get; } = new();
        /// <summary>Le righe già saltate in un giro precedente con lo stesso dato: non rimandate.</summary>
        public List<string> GiaSegnalate { get; } = new();

        /// <summary>Saltate di questo giro: non mappate a monte + rifiutate dal VPS + rifiutate da MySQL.</summary>
        public int NumeroSaltate => SaltateNonMappate + SaltateVps + SaltatePm;
        /// <summary>Saltate già segnalate in un giro precedente e non rimandate.</summary>
        public int SaltateNote => GiaSegnalate.Count;

        /// <summary>
        /// Le scritture di questo giro (righe, mappa, righe rifiutate NUOVE, righe rimandate): se
        /// è 0 un giro del timer non lascia riga nel registro. Le righe saltate per dipendente o
        /// commessa non mappati non contano: sono stabili, e una riga al minuto per Monticone non
        /// serve a nessuno.
        /// </summary>
        public int Modifiche =>
            CreatePm + CreateVps + AggiornatePm + AggiornateVps + CancellatePm + CancellateVps
            + Abbinati + Riallineate + SaltateVps + SaltatePm + RimandatePm;

        /// <summary>Il primo messaggio di una riga rifiutata dal VPS (nuova o già segnalata), null se il VPS ha preso tutto: va in <c>LastError</c>.</summary>
        public string? PrimoErrore() => Errori.Count > 0 ? Errori[0] : GiaSegnalate.Count > 0 ? GiaSegnalate[0] : null;

        /// <summary>Es. «Allocazioni: PM +2 / ~1 / −0, VPS +1 / ~0 / −1; 1 conflitto (vince il VPS: Mario Rossi 03/09-05/09); saltate 2 (dipendente non mappato: Christian Monticone)».</summary>
        public string Dettaglio()
        {
            var sb = new StringBuilder("Allocazioni: ");
            sb.Append("PM +").Append(CreatePm).Append(" / ~").Append(AggiornatePm).Append(" / −").Append(CancellatePm);
            sb.Append(", VPS +").Append(CreateVps).Append(" / ~").Append(AggiornateVps).Append(" / −").Append(CancellateVps);
            if (Invariate > 0) sb.Append(", ").Append(Invariate).Append(" invariate");
            if (Abbinati > 0) sb.Append(", ").Append(Abbinati).Append(" abbinate per contenuto");
            if (Conflitti.Count > 0)
                sb.Append("; ").Append(Conflitti.Count).Append(Conflitti.Count == 1 ? " conflitto (" : " conflitti (")
                  .Append(string.Join("; ", Conflitti)).Append(')');
            if (Rimandate.Count > 0)
                sb.Append("; rimandate ").Append(Rimandate.Count).Append(" (").Append(string.Join("; ", Rimandate)).Append(')');
            if (NumeroSaltate > 0)
            {
                sb.Append("; saltate ").Append(NumeroSaltate);
                if (Saltate.Count > 0) sb.Append(" (").Append(string.Join(", ", Saltate)).Append(')');
            }
            if (Errori.Count > 0) sb.Append("; errori: ").Append(string.Join("; ", Errori));
            if (GiaSegnalate.Count > 0)
                sb.Append("; saltate (già segnalate): ").Append(GiaSegnalate.Count).Append(" — ").Append(string.Join("; ", GiaSegnalate));
            return sb.ToString();
        }
    }

    // ── Registro ed esito ────────────────────────────────────────

    /// <summary>
    /// Riga di registro + esito in res_settings. Un giro del <b>timer</b> (o dell'<b>hub</b>: il
    /// VPS rimanda <c>AssignmentsChanged</c> anche per le POST del motore, un'eco che non trova
    /// niente da fare) andato bene che non ha fatto niente (tutti i contatori a zero) NON scrive
    /// la riga: sarebbero 1440 righe al giorno tutte uguali, più una per ogni scrittura dal
    /// planner; <c>sync.last_*</c> si aggiornano comunque. Una volta al giorno si
    /// buttano le righe più vecchie di <see cref="GiorniRegistro"/> giorni.
    /// </summary>
    private void Registra(RisorseSyncLogEntry voce, int modifiche, ContatoriGiro contatori)
    {
        try
        {
            bool giroVuoto = voce.Innesco is "timer" or "hub" && voce.Esito == "ok" && modifiche == 0;
            using MySqlConnection c = _rdb.Open();
            if (!giroVuoto)
                c.Execute(@"
                    INSERT INTO res_sync_log (run_utc, innesco, esito, durata_ms, righe_pm, righe_vps,
                                              create_pm, create_vps, aggiornate_pm, aggiornate_vps,
                                              cancellate_pm, cancellate_vps, conflitti, saltate, dettaglio)
                    VALUES (@RunUtc, @Innesco, @Esito, @DurataMs, @RighePm, @RigheVps,
                            @CreatePm, @CreateVps, @AggiornatePm, @AggiornateVps,
                            @CancellatePm, @CancellateVps, @Conflitti, @Saltate, @Dettaglio)",
                    new
                    {
                        voce.RunUtc, voce.Innesco, voce.Esito, voce.DurataMs,
                        contatori.RighePm, contatori.RigheVps,
                        contatori.CreatePm, contatori.CreateVps, contatori.AggiornatePm, contatori.AggiornateVps,
                        contatori.CancellatePm, contatori.CancellateVps, contatori.Conflitti, contatori.Saltate,
                        voce.Dettaglio,
                    });
            // A giro «ok» resta l'eventuale riga rifiutata dal VPS (LastError), così il pannello la mostra.
            _store.ScriviEsito(voce.RunUtc, voce.Esito, voce.Esito == "ok" ? LastError : voce.Dettaglio);

            if (DateTime.UtcNow - _ultimaPulizia >= TimeSpan.FromDays(1))
            {
                int tolte = c.Execute(
                    "DELETE FROM res_sync_log WHERE run_utc < UTC_TIMESTAMP() - INTERVAL @Giorni DAY",
                    new { Giorni = GiorniRegistro });
                _ultimaPulizia = DateTime.UtcNow; // dopo il DELETE: se fallisce si ritenta al giro dopo
                if (tolte > 0)
                    _logger.LogInformation("[RisorseSync] Registro: tolte {N} righe più vecchie di {Giorni} giorni.", tolte, GiorniRegistro);
            }
        }
        catch (Exception ex)
        {
            // Il registro che non si scrive non deve sostituire l'esito vero: resta nel log.
            _logger.LogWarning(ex, "[RisorseSync] Registro non scrivibile: {Msg}", ex.Message);
        }
    }

    /// <summary>I contatori di un giro che finiscono nelle colonne di res_sync_log: anagrafiche e allocazioni sommate per lato.</summary>
    private readonly record struct ContatoriGiro(
        int RighePm = 0, int RigheVps = 0,
        int CreatePm = 0, int CreateVps = 0,
        int AggiornatePm = 0, int AggiornateVps = 0,
        int CancellatePm = 0, int CancellateVps = 0,
        int Conflitti = 0, int Saltate = 0);

    /// <summary>Messaggio per il pannello: leggibile da chi non sa cos'è un HttpRequestException.</summary>
    internal static string MessaggioLeggibile(Exception ex) => ex switch
    {
        RisorseSyncException => ex.Message,
        TaskCanceledException => "Il VPS non ha risposto entro 30 secondi.",
        HttpRequestException hre => $"VPS non raggiungibile: {hre.Message}",
        _ => ex.Message,
    };

    // ── Client HTTP ──────────────────────────────────────────────

    /// <summary>
    /// Un client per impostazioni: se cambiano indirizzo o credenziali, se ne fa uno nuovo (e
    /// l'hub si rifà). Col client vecchio muore anche il freno <c>CredenzialiRifiutate</c>: è
    /// così che, corretta la password dal pannello, il motore riparte.
    /// </summary>
    private void AssicuraClient(RisorseSyncSettings s)
    {
        if (_client != null && _client.StesseImpostazioni(s)) return;
        _client = new RisorseSyncClient(s, _logger, _http);
        _saltateNote.Clear(); // le righe rifiutate valevano per il VPS/credenziali di prima
    }

    // ── Client SignalR verso il VPS ──────────────────────────────

    /// <summary>
    /// Tiene in piedi la connessione all'hub del VPS. Degradazione elegante: se non si collega
    /// si va avanti col timer e si riprova al giro dopo. La riconnessione automatica copre le
    /// cadute DOPO il primo collegamento riuscito; il primo tentativo fallito lo ripete questo
    /// metodo, chiamato a ogni iterazione del loop.
    /// </summary>
    private async Task AssicuraHubAsync(RisorseSyncSettings s, CancellationToken ct)
    {
        string url = $"{s.BaseUrlNormalizzato}/hubs/resource-planner";
        // Indirizzo diverso O client diverso (credenziali cambiate dal pannello): l'hub si rifà,
        // così la riconnessione automatica non riparte col token dell'account vecchio.
        if (_hub != null && (_hubUrl != url || !ReferenceEquals(_hubClient, _client)))
            await ChiudiHubAsync();

        if (_hub == null)
        {
            HubConnection hub = new HubConnectionBuilder()
                .WithUrl(url, o => o.AccessTokenProvider = async () =>
                {
                    // Si legge il campo al momento della chiamata, NON si cattura il client
                    // nella closure: la riconnessione automatica deve usare quello in vigore.
                    RisorseSyncClient? client = _client;
                    if (client == null) return null;
                    try { return await client.TokenAsync(_ctHost); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[RisorseSync] Hub: token non ottenibile: {Msg}", ex.Message);
                        return null;
                    }
                })
                .WithAutomaticReconnect()
                .Build();

            hub.On<ResAssignmentChange>("AssignmentsChanged", _ => Trigger("hub"));
            hub.On("EmployeesChanged", () => Trigger("hub"));
            hub.Reconnected += _ =>
            {
                _logger.LogInformation("[RisorseSync] Hub VPS riconnesso.");
                // Quello che è cambiato mentre eravamo scollegati non arriva: un giro lo recupera.
                Trigger("hub");
                return Task.CompletedTask;
            };
            hub.Closed += ex =>
            {
                if (ex != null)
                    _logger.LogWarning(ex, "[RisorseSync] Hub VPS chiuso: {Msg}", ex.Message);
                return Task.CompletedTask;
            };

            _hub = hub;
            _hubUrl = url;
            _hubClient = _client;
        }

        if (_hub.State != HubConnectionState.Disconnected) return;
        // Credenziali rifiutate: inutile bussare all'hub (e farebbe scattare il limite di login).
        if (_client?.CredenzialiRifiutate == true) return;
        try
        {
            // Un handshake che non risponde non deve tenere fermo il loop: 15 s e si va avanti col timer.
            using var attesa = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attesa.CancelAfter(AttesaHub);
            await _hub.StartAsync(attesa.Token);
            _logger.LogInformation("[RisorseSync] Collegato all'hub del VPS ({Url}).", url);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Si prosegue col timer: il prossimo giro riprova. La scadenza dei 15 s (host non
            // annullato) arriva qui come OperationCanceledException: è un «non raggiungibile».
            string motivo = ex is OperationCanceledException
                ? $"nessuna risposta entro {AttesaHub.TotalSeconds:0} s"
                : ex.Message;
            _logger.LogWarning(ex, "[RisorseSync] Hub VPS non raggiungibile ({Url}): {Msg}", url, motivo);
        }
    }

    private async Task ChiudiHubAsync()
    {
        HubConnection? hub = _hub;
        _hub = null;
        _hubUrl = null;
        _hubClient = null;
        if (hub == null) return;
        try { await hub.DisposeAsync(); }
        catch (Exception ex) { _logger.LogDebug(ex, "[RisorseSync] Chiusura hub."); }
    }

    /// <summary>All'arresto l'hub si chiude in modo asincrono, PRIMA di fermare il loop: niente attese bloccanti in Dispose.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await ChiudiHubAsync();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _giro.Dispose();
        base.Dispose();
    }
}
