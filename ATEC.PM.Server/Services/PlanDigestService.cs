using ATEC.PM.Shared.DTOs;
using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// BackgroundService: ogni minuto controlla se è l'ora configurata per il digest email
/// automatico del piano risorse (default 07:00, salta i weekend se non abilitati) e, se sì e
/// non è già stato eseguito oggi, invoca PlanNotificationService.SendDigest("automatico").
/// Port di PlanDigestService da ATEC.Risorse.Server. Abilitabile da appsettings "Services:PlanDigest".
/// </summary>
public class PlanDigestService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<PlanDigestService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public PlanDigestService(IServiceProvider sp, ILogger<PlanDigestService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), ct);
        _logger.LogInformation("[PlanDigest] Scheduler attivo — controllo ogni {Min} min.", CheckInterval.TotalMinutes);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAndRunAsync();
                CleanupOldSnapshotsIfDue();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PlanDigest] Errore nel controllo digest");
            }

            try { await Task.Delay(CheckInterval, ct); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Una volta al giorno: elimina le istantanee del piano più vecchie di 30gg,
    /// tranne l'ultima (baseline corrente per il prossimo confronto). Senza questa pulizia
    /// res_plan_snapshots crescerebbe indefinitamente (un batch per ogni digest eseguito).</summary>
    private void CleanupOldSnapshotsIfDue()
    {
        if ((DateTime.UtcNow - _lastCleanupUtc) < TimeSpan.FromHours(24)) return;
        _lastCleanupUtc = DateTime.UtcNow;

        using IServiceScope scope = _sp.CreateScope();
        var rdb = scope.ServiceProvider.GetRequiredService<ResourcesDbService>();
        using var c = rdb.Open();

        int deleted = c.Execute(@"
            DELETE FROM res_plan_snapshot_batches
            WHERE created_utc < DATE_SUB(NOW(), INTERVAL 30 DAY)
              AND id <> (SELECT MAX(id) FROM (SELECT id FROM res_plan_snapshot_batches) x)");
        if (deleted > 0)
            _logger.LogInformation("[PlanDigest] Pulizia istantanee: {Count} batch (>30gg) rimossi.", deleted);
    }

    private async Task CheckAndRunAsync()
    {
        using IServiceScope scope = _sp.CreateScope();
        var rdb = scope.ServiceProvider.GetRequiredService<ResourcesDbService>();
        var notify = scope.ServiceProvider.GetRequiredService<PlanNotificationService>();

        PlanDigestSettingsDto settings = notify.GetSettings();
        if (!settings.DigestEnabled) return;

        DateTime nowLocal = PlanNotificationService.ToItalyTime(DateTime.UtcNow);
        string today = nowLocal.ToString("yyyy-MM-dd");
        if (settings.DigestLastRun == today) return; // già eseguito oggi

        bool isWeekend = nowLocal.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        if (isWeekend && !settings.DigestWeekends) return;

        if (!TimeSpan.TryParse(settings.DigestTime, out TimeSpan targetTime)) targetTime = new TimeSpan(7, 0, 0);
        if (nowLocal.TimeOfDay < targetTime) return; // non ancora ora

        _logger.LogInformation("[PlanDigest] Esecuzione automatica ({Time})", nowLocal);
        notify.SendDigest("automatico");

        using var c = rdb.Open();
        c.Execute(
            "INSERT INTO res_settings (`key`, `value`) VALUES ('digest_last_run', @Today) ON DUPLICATE KEY UPDATE `value` = VALUES(`value`)",
            new { Today = today });

        await Task.CompletedTask;
    }
}
