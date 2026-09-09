using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;
using Xunit;

namespace ATEC.PM.Tests.Email;

/// <summary>
/// 09/09/2026 sera, Diego: «come faccio a sapere se la mail di sollecito è effettivamente
/// stata inviata?». Non poteva: la mail si accodava e a video si diceva «inviato» a scatola
/// chiusa, mentre in produzione Aruba rispondeva «501 invalid LOGIN encoding» perché la
/// password SMTP salvata non era più decifrabile e il server ne mandava una VUOTA. Qui la
/// regola che ferma l'invio PRIMA di toccare il server di posta, con il motivo in chiaro.
/// </summary>
public class EsitoInvioMailTests
{
    private static EmailSettingsDto Buona() => new()
    {
        Enabled = true, SmtpHost = "smtps.aruba.it", SmtpPort = 465, Security = "ssl",
        From = "BOT_Atec@atec.srl", FromName = "ATEC - Gestione Risorse",
        Username = "BOT_Atec@atec.srl", Password = "segreta",
    };

    [Fact]
    public void Con_tutto_a_posto_la_mail_puo_partire() =>
        Assert.Null(EmailService.MotivoBlocco(Buona(), passwordIllegibile: false));

    [Fact]
    public void La_password_salvata_ma_illeggibile_si_dice_prima_di_toccare_il_server_di_posta()
    {
        EmailSettingsDto cfg = Buona();
        cfg.Password = null; // DecryptPassword ha restituito null: blob DPAPI di un altro ambito
        string? motivo = EmailService.MotivoBlocco(cfg, passwordIllegibile: true);
        Assert.NotNull(motivo);
        Assert.Contains("reinserirla", motivo);
    }

    [Fact]
    public void Utente_senza_password_non_parte()
    {
        EmailSettingsDto cfg = Buona();
        cfg.Password = "";
        Assert.Contains("Password SMTP mancante", EmailService.MotivoBlocco(cfg, passwordIllegibile: false));
    }

    [Fact]
    public void Senza_utente_la_password_non_serve()
    {
        EmailSettingsDto cfg = Buona();
        cfg.Username = "";
        cfg.Password = null;
        Assert.Null(EmailService.MotivoBlocco(cfg, passwordIllegibile: false));
    }

    [Fact]
    public void Spento_o_incompleto_lo_dice_ma_la_prova_si_fa_anche_da_spento()
    {
        EmailSettingsDto spento = Buona();
        spento.Enabled = false;
        Assert.Contains("spento", EmailService.MotivoBlocco(spento, passwordIllegibile: false));
        Assert.Null(EmailService.MotivoBlocco(spento, passwordIllegibile: false, richiedeAttivo: false));

        EmailSettingsDto senzaMittente = Buona();
        senzaMittente.From = "";
        Assert.Contains("incompleta", EmailService.MotivoBlocco(senzaMittente, passwordIllegibile: false));
    }

    [Fact]
    public void L_errore_del_server_di_posta_si_legge_in_parole()
    {
        var rifiuto = new MailKit.Security.AuthenticationException("535: 5.7.0 authentication failed");
        Assert.Contains("rifiutato utente o password", EmailService.TestoErroreSmtp(rifiuto));
        Assert.Contains("535", EmailService.TestoErroreSmtp(rifiuto));
        var rete = new System.Net.Sockets.SocketException(10061);
        Assert.Contains("non raggiungibile", EmailService.TestoErroreSmtp(rete));
    }
}
