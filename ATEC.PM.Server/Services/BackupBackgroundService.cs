namespace ATEC.PM.Server.Services;

/// <summary>
/// Backup automatico notturno (ore <c>Backup:AutoHour</c>, default 02:00).
///
/// <para>Dal 26/08/2026 il notturno crea il <b>pacchetto completo</b> (database + documenti
/// + foto), non più il solo dump .sql: il dump era ridondante — il pacchetto lo contiene —
/// e lasciava fuori proprio la parte insostituibile, i file. Con la destinazione sul NAS
/// la copia notturna nasce già fuori dal server. Se il pacchetto fallisce (NAS spento,
/// share giù) si RIPIEGA sul dump .sql locale: una notte senza NAS non deve essere una
/// notte senza backup. <c>Backup:AutoCompleto = false</c> torna al solo dump.</para>
///
/// <para>Dopo il backup gira la pulizia a tempo (<c>Backup:GiorniConservazione</c>,
/// default 60 giorni) su pacchetti e dump, con una scorta minima sempre conservata.</para>
/// </summary>
public class BackupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _config;
    private readonly ILogger<BackupBackgroundService> _log;
    private readonly int _hourOfDay;

    public BackupBackgroundService(IServiceProvider sp, IConfiguration config, ILogger<BackupBackgroundService> log)
    {
        _sp = sp;
        _config = config;
        _log = log;
        _hourOfDay = int.TryParse(config["Backup:AutoHour"], out int h) ? h : 2; // Default: 02:00
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("[BackupAuto] Backup automatico attivo — ogni notte alle {Hour}:00", _hourOfDay);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Calcola prossima esecuzione
                DateTime now = DateTime.Now;
                DateTime next = now.Date.AddHours(_hourOfDay);
                if (next <= now) next = next.AddDays(1);

                TimeSpan delay = next - now;
                _log.LogInformation("[BackupAuto] Prossimo backup tra {Hours}h {Minutes}m",
                    (int)delay.TotalHours, delay.Minutes);

                await Task.Delay(delay, ct);

                bool completoRiuscito = false;
                if (_config.GetValue("Backup:AutoCompleto", true))
                    completoRiuscito = await EseguiPacchettoCompleto(ct);

                if (!completoRiuscito)
                {
                    // Ripiego (o scelta esplicita): il dump .sql locale di sempre.
                    using var scope = _sp.CreateScope();
                    var backupController = scope.ServiceProvider.GetRequiredService<ATEC.PM.Server.Controllers.BackupController>();
                    string path = backupController.ExecuteBackup("auto");
                    _log.LogInformation("[BackupAuto] Dump .sql completato: {Path}", path);
                }

                // La pulizia gira anche dopo un ripiego: le scorte minime la rendono
                // innocua, e i vecchi non devono accumularsi solo perché il NAS ha
                // fatto una notte storta.
                _sp.GetRequiredService<FullBackupService>().PulisciBackupVecchi();
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[BackupAuto] Errore durante backup automatico");
                // Riprova tra 1 ora in caso di errore
                await Task.Delay(TimeSpan.FromHours(1), ct);
            }
        }
    }

    /// <summary>
    /// Crea il pacchetto completo e ASPETTA che finisca (stesso motore a job della
    /// pagina Backup: se un admin nottambulo ha la pagina aperta, vede la console).
    /// true = pacchetto creato; false = fallito, tocca al ripiego .sql.
    /// </summary>
    private async Task<bool> EseguiPacchettoCompleto(CancellationToken ct)
    {
        var full = _sp.GetRequiredService<FullBackupService>();
        FullBackupService.BackupJob job;
        try
        {
            job = full.AvviaBackup();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[BackupAuto] Pacchetto completo non avviabile");
            return false;
        }

        // AvviaBackup restituisce il job GIÀ ATTIVO se c'è un'operazione in corso (per
        // esempio un ripristino lanciato a mano): in quel caso non è il nostro backup —
        // si aspetta comunque la fine e poi si ripiega sul dump, senza accodare niente.
        bool nostro = job.Tipo == "backup";

        var limite = DateTime.UtcNow.AddHours(3);   // con GB di documenti può volerci
        while (job.Stato == "in_corso" && DateTime.UtcNow < limite)
            await Task.Delay(TimeSpan.FromSeconds(10), ct);

        if (nostro && job.Stato == "completato")
        {
            _log.LogInformation("[BackupAuto] Pacchetto completo creato: {File} ({MB} MB)",
                job.FileName, job.DimensioneMB);
            return true;
        }

        if (!nostro)
            _log.LogWarning("[BackupAuto] All'ora del backup era in corso un'operazione di tipo " +
                "{Tipo} (esito {Stato}): il pacchetto notturno non è partito — ripiego sul dump .sql",
                job.Tipo, job.Stato);
        else
            _log.LogError("[BackupAuto] Pacchetto completo NON riuscito (stato {Stato}: {Msg}) — ripiego sul dump .sql",
                job.Stato, job.Messaggio);
        return false;
    }
}
