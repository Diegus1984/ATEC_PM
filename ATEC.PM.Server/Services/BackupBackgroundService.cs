namespace ATEC.PM.Server.Services;

/// <summary>
/// Esegue backup automatico del database ogni notte alle 02:00.
/// </summary>
public class BackupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<BackupBackgroundService> _log;
    private readonly int _hourOfDay;

    public BackupBackgroundService(IServiceProvider sp, IConfiguration config, ILogger<BackupBackgroundService> log)
    {
        _sp = sp;
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

                // Esegui backup
                using var scope = _sp.CreateScope();
                var backupController = scope.ServiceProvider.GetRequiredService<ATEC.PM.Server.Controllers.BackupController>();
                string path = backupController.ExecuteBackup("auto");
                _log.LogInformation("[BackupAuto] Backup completato: {Path}", path);
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
}
