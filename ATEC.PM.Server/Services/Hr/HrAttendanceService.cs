using System.Globalization;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Il servizio del modulo HR (PIANO-HR-PRESENZE.md): importa timbrature e richieste assenza da Ecos,
/// calcola <c>hr_days</c> col <see cref="TimesheetEngine"/>, gestisce il workflow delle richieste ferie/permessi,
/// le rettifiche, la mappatura e la matrice mensile presenze.
/// </summary>
public partial class HrAttendanceService
{
    private const string CursoreKey = "hr_sync_punches_from";

    /// <summary>
    /// L'ultima lettura riuscita dell'anagrafica badge (voce 11 del port): sta in
    /// <c>app_config</c> perché è un'informazione che deve sopravvivere al riavvio.
    /// 🪤 Nell'originale il pulsante «Solo Badge» NON la scriveva, e il ciclo dei 7 giorni
    /// non se ne accorgeva: qui si scrive nell'unico punto in cui i badge si leggono.
    /// </summary>
    private const string BadgeKey = "hr_last_badge_read";

    private static readonly TimeSpan MargineCursore = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MargineCursoreOrologioNostro = TimeSpan.FromHours(1);
    private const int MaxDaysToRepair = 5000;

    private readonly DbService _db;
    private readonly EcosClient _ecos;
    private readonly NotificationService _notif;
    private readonly ILogger<HrAttendanceService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Real-time: null nei test (niente hub), pieno in produzione via DI.
    private readonly HrChangeNotifier? _realtime;

    public HrAttendanceService(
        DbService db, EcosClient ecos, NotificationService notif, ILogger<HrAttendanceService> logger,
        HrChangeNotifier? realtime = null)
    {
        _db = db;
        _ecos = ecos;
        _notif = notif;
        _logger = logger;
        _realtime = realtime;
    }

    public HrAttendanceService(
        DbService db, EcosClient ecos, ILogger<HrAttendanceService> logger)
        : this(db, ecos, new NotificationService(db, new AnagraficheCache(Microsoft.Extensions.Logging.Abstractions.NullLogger<AnagraficheCache>.Instance)), logger)
    {
    }

    // Stato dell'ultimo import, per la pagina (il servizio è singleton).
    public bool ImportInProgress { get; private set; }
    public DateTime? LastImport { get; private set; }
    public string LastResult { get; private set; } = "";

    // ── AVANZAMENTO A VIDEO (PIANO-HR-PORT-ORIGINALE.md, B3) ──────────────────
    //
    // Port della barra + txtLog di SyncEcosPage. Il servizio è singleton, quindi lo stato
    // sta qui in memoria e la pagina lo legge da GET /api/hr/status mentre l'import gira.
    // 🪤 In memoria vuol dire che un riavvio del servizio a metà import lo azzera: la
    // pagina lo riconosce (StartedAt nullo) e lo dice, invece di restare a girare.

    private readonly object _progressoLock = new();
    private HrImportProgressDto _progresso = new();

    /// <summary>Copia dello stato dell'import: si legge da un altro thread mentre l'import scrive.</summary>
    public HrImportProgressDto SnapshotProgresso()
    {
        lock (_progressoLock)
        {
            return new HrImportProgressDto
            {
                Running = _progresso.Running,
                Title = _progresso.Title,
                Phase = _progresso.Phase,
                Percent = _progresso.Percent,
                Downloaded = _progresso.Downloaded,
                Added = _progresso.Added,
                Updated = _progresso.Updated,
                Removed = _progresso.Removed,
                DaysRecalculated = _progresso.DaysRecalculated,
                StartedAt = _progresso.StartedAt,
                EndedAt = _progresso.EndedAt,
                Log = new List<string>(_progresso.Log),
            };
        }
    }

    private void ProgressoInizio(string titolo)
    {
        lock (_progressoLock)
        {
            _progresso = new HrImportProgressDto
            {
                Running = true,
                Title = titolo,
                StartedAt = DateTime.Now,
            };
            _progresso.Log.Add($"=== {titolo.ToUpperInvariant()} ===");
        }
    }

    private void ProgressoFase(string fase, int percento)
    {
        lock (_progressoLock)
        {
            _progresso.Phase = fase;
            _progresso.Percent = percento;
            AggiungiRiga(fase);
        }
        // Chi ha aperto «Aggiorna da Ecos» vede la barra muoversi senza aspettare il polling.
        _realtime?.Notify("import-progress");
    }

    private void ProgressoLog(string riga)
    {
        lock (_progressoLock) AggiungiRiga(riga);
    }

    private void ProgressoFine(string riga, HrImportResultDto? esito)
    {
        lock (_progressoLock)
        {
            _progresso.Running = false;
            _progresso.Percent = 100;
            _progresso.Phase = riga;
            _progresso.EndedAt = DateTime.Now;
            if (esito != null)
            {
                _progresso.Added = esito.PunchesAdded;
                _progresso.Updated = esito.PunchesUpdated;
                _progresso.DaysRecalculated = esito.DaysRecalculated;
            }
            AggiungiRiga(riga);
        }
        // Fine import (riuscito o no, anche quello automatico delle 12 ore): le pagine
        // presenze aperte rileggono cartellino, calendario, quadratura e stato.
        _realtime?.Notify("import");
    }

    /// <summary>Da chiamare già dentro il lock: tiene le ultime 200 righe, come il txtLog.</summary>
    private void AggiungiRiga(string riga)
    {
        _progresso.Log.Add(riga);
        if (_progresso.Log.Count > 200) _progresso.Log.RemoveAt(0);
    }
}
