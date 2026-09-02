using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using ATEC.PM.Shared.DTOs;
using Dapper;
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
/// vedi <see cref="SyncAnagraficheAsync"/>. Le allocazioni (Fase 2) hanno il loro metodo già
/// chiamato nell'ordine giusto, per ora vuoto.</para>
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

    private readonly ResourcesDbService _rdb;
    private readonly ILogger<RisorseSyncService> _logger;
    private readonly RisorseSyncSettingsStore _store;
    /// <summary>Solo nei test: un HttpClient con handler finto al posto di quello condiviso del client.</summary>
    private readonly HttpClient? _http;

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

    public RisorseSyncService(ResourcesDbService rdb, IConfiguration config, ILogger<RisorseSyncService> logger)
        : this(rdb, config, logger, null)
    {
    }

    /// <summary>Per i test: stesso motore, ma le chiamate HTTP passano dall'<paramref name="http"/> dato (handler finto).</summary>
    internal RisorseSyncService(ResourcesDbService rdb, IConfiguration config, ILogger<RisorseSyncService> logger, HttpClient? http)
    {
        _rdb = rdb;
        _logger = logger;
        _http = http;
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
        // L'esito delle anagrafiche vive FUORI dal try e si riempie passo dopo passo: se il giro
        // cade a metà (es. la PUT projects) il registro racconta lo stesso quello che la PUT
        // employees ha già creato e mappato.
        var anagrafiche = new EsitoAnagrafiche();

        IsSyncing = true;
        try
        {
            RisorseSyncClient client = _client
                ?? throw new InvalidOperationException("Client non inizializzato.");

            SyncStatusDto stato = await client.GetStatusAsync(ct);
            righeVps = stato.Assignments;

            await SyncAnagraficheAsync(client, anagrafiche, innesco, ct);

            await SyncAllocazioniAsync(client, ct);

            voce.Esito = "ok";
            voce.Dettaglio = $"VPS: {stato.Employees} dipendenti, {stato.Assignments} allocazioni, " +
                             $"{stato.Projects} commesse, {stato.Departments} reparti (v{stato.Version}); " +
                             anagrafiche.Dettaglio();
            // Giro buono ma con righe rifiutate dal VPS: il pannello deve continuare a mostrarlo,
            // anche quando il timer non lascia più righe nel registro (già segnalate).
            LastError = anagrafiche.PrimoErrore();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            voce.Esito = "errore";
            voce.Dettaglio = "Interrotto prima di finire (arresto del servizio o richiesta annullata).";
        }
        catch (Exception ex)
        {
            voce.Esito = "errore";
            voce.Dettaglio = MessaggioLeggibile(ex) + (anagrafiche.Iniziato ? " — " + anagrafiche.Dettaglio() : "");
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
                CreateVps: anagrafiche.CreateVps,
                AggiornateVps: anagrafiche.AggiornateVps,
                Saltate: anagrafiche.Saltate + anagrafiche.SaltateNote);
            // «Modifiche» = scritture sul VPS o sulla mappa in questo giro: righe create/aggiornate,
            // abbinamenti del seme, la PUT dei reparti (il VPS risponde 0/0 se cambiano solo i
            // legami) e le righe saltate NUOVE. Quelle già segnalate no: se resta 0 un giro del
            // timer non lascia riga nel registro. Vedi Registra.
            int modifiche = anagrafiche.CreateVps + anagrafiche.AggiornateVps + anagrafiche.Saltate
                            + anagrafiche.Abbinati + (anagrafiche.RepartiInviati ? 1 : 0);
            Registra(voce, righeVps, modifiche, contatori);
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
        righeDip = SenzaLeGiaSaltate(RisorseSyncMap.Employee, righeDip, esito);
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
        righeCom = SenzaLeGiaSaltate(RisorseSyncMap.Project, righeCom, esito);
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
    private List<RigaDaInviare<T>> SenzaLeGiaSaltate<T>(string kind, List<RigaDaInviare<T>> righe, EsitoAnagrafiche esito)
    {
        if (_saltateNote.Count == 0) return righe;
        var tenute = new List<RigaDaInviare<T>>(righe.Count);
        foreach (RigaDaInviare<T> r in righe)
        {
            if (_saltateNote.TryGetValue((kind, r.LocalId), out (string Impronta, string Messaggio) nota) && nota.Impronta == r.Impronta)
                esito.GiaSegnalate.Add(nota.Messaggio);
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

    /// <summary>
    /// Fase 2 — allocazioni nei due versi: scarica tutte le righe del VPS, legge quelle di
    /// MySQL, confronta con la mappa (impronta in <c>synced_hash</c>) e applica le differenze
    /// con le regole di merge di §4.3 (funzioni pure con test). Per ora non fa niente.
    /// </summary>
    private Task SyncAllocazioniAsync(RisorseSyncClient client, CancellationToken ct)
    {
        // TODO Fase 2: GET /api/sync/assignments ↔ res_assignments; POST assignments / delete;
        // scritture locali via NotifyChange dell'hub PM; conteggi nelle colonne di res_sync_log.
        return Task.CompletedTask;
    }

    // ── Registro ed esito ────────────────────────────────────────

    /// <summary>
    /// Riga di registro + esito in res_settings. Un giro del <b>timer</b> andato bene che non ha
    /// fatto niente (tutti i contatori a zero) NON scrive la riga: sarebbero 1440 righe al
    /// giorno tutte uguali; <c>sync.last_*</c> si aggiornano comunque. Una volta al giorno si
    /// buttano le righe più vecchie di <see cref="GiorniRegistro"/> giorni.
    /// </summary>
    private void Registra(RisorseSyncLogEntry voce, int righeVps, int modifiche, ContatoriGiro contatori)
    {
        try
        {
            bool giroVuoto = voce.Innesco == "timer" && voce.Esito == "ok" && modifiche == 0;
            using MySqlConnection c = _rdb.Open();
            if (!giroVuoto)
                c.Execute(@"
                    INSERT INTO res_sync_log (run_utc, innesco, esito, durata_ms, righe_vps,
                                              create_vps, aggiornate_vps, saltate, dettaglio)
                    VALUES (@RunUtc, @Innesco, @Esito, @DurataMs, @RigheVps,
                            @CreateVps, @AggiornateVps, @Saltate, @Dettaglio)",
                    new
                    {
                        voce.RunUtc, voce.Innesco, voce.Esito, voce.DurataMs, RigheVps = righeVps,
                        contatori.CreateVps, contatori.AggiornateVps, contatori.Saltate, voce.Dettaglio,
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

    /// <summary>I contatori di un giro che finiscono nelle colonne di res_sync_log (la Fase 2 aggiungerà i suoi).</summary>
    private readonly record struct ContatoriGiro(int CreateVps = 0, int AggiornateVps = 0, int Saltate = 0);

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
