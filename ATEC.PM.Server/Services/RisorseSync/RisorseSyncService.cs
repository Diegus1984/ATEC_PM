using System.Diagnostics;
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
/// <para><b>Fase 0</b>: il giro fa solo login + lettura dello stato del VPS e scrive la riga di
/// registro. Anagrafiche (Fase 1) e allocazioni (Fase 2) hanno i loro due metodi già chiamati
/// nell'ordine giusto, per ora vuoti.</para>
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

    private readonly ResourcesDbService _rdb;
    private readonly ILogger<RisorseSyncService> _logger;
    private readonly RisorseSyncSettingsStore _store;

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

    // ── Stato esposto ────────────────────────────────────────────

    public bool IsSyncing { get; private set; }
    public bool HubConnected => _hub?.State == HubConnectionState.Connected;
    public DateTime? LastRun { get; private set; }
    public string? LastError { get; private set; }

    public RisorseSyncService(ResourcesDbService rdb, IConfiguration config, ILogger<RisorseSyncService> logger)
    {
        _rdb = rdb;
        _logger = logger;
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
        // Righe create/aggiornate/cancellate da questo giro (create_*, aggiornate_*, cancellate_*,
        // conflitti, saltate): in Fase 0 non si scrive niente, quindi resta 0. Vedi Registra.
        int modifiche = 0;

        IsSyncing = true;
        try
        {
            RisorseSyncClient client = _client
                ?? throw new InvalidOperationException("Client non inizializzato.");

            SyncStatusDto stato = await client.GetStatusAsync(ct);
            righeVps = stato.Assignments;

            await SyncAnagraficheAsync(client, ct);
            await SyncAllocazioniAsync(client, ct);

            voce.Esito = "ok";
            voce.Dettaglio = $"VPS: {stato.Employees} dipendenti, {stato.Assignments} allocazioni, " +
                             $"{stato.Projects} commesse, {stato.Departments} reparti (v{stato.Version})";
            LastError = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            voce.Esito = "errore";
            voce.Dettaglio = "Interrotto prima di finire (arresto del servizio o richiesta annullata).";
        }
        catch (Exception ex)
        {
            voce.Esito = "errore";
            voce.Dettaglio = MessaggioLeggibile(ex);
            LastError = voce.Dettaglio;
            _logger.LogWarning(ex, "[RisorseSync] Giro ({Innesco}) fallito: {Msg}", innesco, voce.Dettaglio);
        }
        finally
        {
            orologio.Stop();
            voce.DurataMs = (int)Math.Min(orologio.ElapsedMilliseconds, int.MaxValue);
            LastRun = runUtc;
            IsSyncing = false;
            Registra(voce, righeVps, modifiche);
            _giro.Release();
        }

        if (voce.Esito == "ok")
            _logger.LogInformation("[RisorseSync] Giro ({Innesco}) ok in {Ms} ms: {Dettaglio}",
                innesco, voce.DurataMs, voce.Dettaglio);
        return voce;
    }

    /// <summary>
    /// Fase 1 — anagrafiche PM → VPS (dipendenti, reparti + legami, commesse ACTIVE) e seme
    /// della mappa dipendenti per nome + cognome. Vedi PIANO-SYNC-RISORSE.md §4.1 punto 2, §5.
    /// Per ora non fa niente: arriva con la Fase 1.
    /// </summary>
    private Task SyncAnagraficheAsync(RisorseSyncClient client, CancellationToken ct)
    {
        // TODO Fase 1: employees → PUT /api/sync/employees; departments+links → PUT /api/sync/departments;
        // projects ACTIVE → PUT /api/sync/projects; aggiornare res_sync_map (EMPLOYEE, DEPARTMENT, PROJECT).
        return Task.CompletedTask;
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
    private void Registra(RisorseSyncLogEntry voce, int righeVps, int modifiche)
    {
        try
        {
            bool giroVuoto = voce.Innesco == "timer" && voce.Esito == "ok" && modifiche == 0;
            using MySqlConnection c = _rdb.Open();
            if (!giroVuoto)
                c.Execute(@"
                    INSERT INTO res_sync_log (run_utc, innesco, esito, durata_ms, righe_vps, dettaglio)
                    VALUES (@RunUtc, @Innesco, @Esito, @DurataMs, @RigheVps, @Dettaglio)",
                    new { voce.RunUtc, voce.Innesco, voce.Esito, voce.DurataMs, RigheVps = righeVps, voce.Dettaglio });
            _store.ScriviEsito(voce.RunUtc, voce.Esito, voce.Esito == "ok" ? null : voce.Dettaglio);

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
        _client = new RisorseSyncClient(s, _logger);
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
