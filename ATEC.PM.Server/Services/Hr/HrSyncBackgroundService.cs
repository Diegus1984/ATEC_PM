namespace ATEC.PM.Server.Services.Hr;

/// <summary>Periodic Ecos punch import (default every 12 hours).</summary>
public class HrSyncBackgroundService : BackgroundService
{
    private readonly HrAttendanceService _attendance;
    private readonly EcosClient _ecos;
    private readonly IConfiguration _config;
    private readonly ILogger<HrSyncBackgroundService> _logger;

    public HrSyncBackgroundService(
        HrAttendanceService attendance, EcosClient ecos, IConfiguration config,
        ILogger<HrSyncBackgroundService> logger)
    {
        _attendance = attendance;
        _ecos = ecos;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        int hours = int.TryParse(_config["Hr:ImportIntervalHours"], out int h) ? h : 12;
        if (hours <= 0)
        {
            _logger.LogInformation("[HR] Automatic import disabled (Hr:ImportIntervalHours <= 0).");
            return;
        }
        if (hours > 720) hours = 720;
        var interval = TimeSpan.FromHours(hours);

        bool warnedNotConfigured = false;

        try { await Task.Delay(TimeSpan.FromSeconds(120), ct); }
        catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_ecos.Configured)
                {
                    if (!warnedNotConfigured)
                    {
                        _logger.LogInformation(
                            "[HR] Ecos credentials not configured: automatic import waiting.");
                        warnedNotConfigured = true;
                    }
                }
                else
                {
                    warnedNotConfigured = false;
                    await _attendance.ImportAsync(full: false, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HR] Automatic import failed: {Msg}", ex.Message);
            }

            try { await Task.Delay(interval, ct); }
            catch (TaskCanceledException) { return; }
        }
    }
}
