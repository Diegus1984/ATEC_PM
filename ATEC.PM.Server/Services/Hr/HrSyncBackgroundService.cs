namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Import periodico delle timbrature da EcosAgile (default ogni 12 ore, come il
/// ripescaggio Danea). Senza credenziali Ecos non fa nulla e lo dice una volta sola:
/// in sviluppo la sezione <c>Ecos</c> è vuota apposta.
///
/// <para>Si spegne con <c>Services:HrSync = false</c> o con
/// <c>Hr:ImportIntervalHours = 0</c>; l'import a mano dalla pagina resta possibile
/// in ogni caso (passa dallo stesso <see cref="HrPresenzeService"/>, che serializza
/// con il suo semaforo).</para>
/// </summary>
public class HrSyncBackgroundService : BackgroundService
{
    private readonly HrPresenzeService _presenze;
    private readonly EcosClient _ecos;
    private readonly IConfiguration _config;
    private readonly ILogger<HrSyncBackgroundService> _logger;

    public HrSyncBackgroundService(
        HrPresenzeService presenze, EcosClient ecos, IConfiguration config,
        ILogger<HrSyncBackgroundService> logger)
    {
        _presenze = presenze;
        _ecos = ecos;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        int ore = int.TryParse(_config["Hr:ImportIntervalHours"], out int h) ? h : 12;
        if (ore <= 0)
        {
            _logger.LogInformation("[HR] Import automatico spento (Hr:ImportIntervalHours <= 0).");
            return;
        }
        // Oltre ~49 giorni Task.Delay esplode (stesso clamp di DaneaOldPullService).
        if (ore > 720) ore = 720;
        var intervallo = TimeSpan.FromHours(ore);

        bool avvisatoNonConfigurato = false;

        // Due minuti di respiro all'avvio: migrazioni e cache prima dell'HTTP verso fuori.
        try { await Task.Delay(TimeSpan.FromSeconds(120), ct); }
        catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_ecos.Configurato)
                {
                    if (!avvisatoNonConfigurato)
                    {
                        _logger.LogInformation(
                            "[HR] Credenziali Ecos non configurate: import automatico in attesa (si riprova a ogni giro).");
                        avvisatoNonConfigurato = true;
                    }
                }
                else
                {
                    avvisatoNonConfigurato = false;
                    await _presenze.ImportaAsync(completo: false, ct);
                }
            }
            catch (Exception ex)
            {
                // Il loop non muore mai per un giro storto: al prossimo si riprova.
                _logger.LogError(ex, "[HR] Import automatico fallito: {Msg}", ex.Message);
            }

            try { await Task.Delay(intervallo, ct); }
            catch (TaskCanceledException) { return; }
        }
    }
}
