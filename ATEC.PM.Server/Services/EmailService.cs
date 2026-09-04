using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using ATEC.PM.Shared.DTOs;
using Dapper;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace ATEC.PM.Server.Services;

/// <summary>Riepilogo variazioni per un singolo dipendente (mail personale).</summary>
public record PlanSummaryMailData(
    string ToEmail,
    string ToName,
    string? ModificheDi,
    List<PlanChangeLine> Nuove,
    List<PlanChangeLine> Modificate,
    List<PlanChangeLine> Cancellate);

/// <summary>Riepilogo per responsabile di reparto (raggruppato per tecnico) o PM (globale).</summary>
public record DepartmentSummaryMailData(
    string ToEmail,
    string ToName,
    string? RepartoNome,
    bool Globale,
    List<(string TecnicoNome, List<PlanChangeLine> Righe)> PerTecnico);

/// <summary>
/// Invio email del digest piano risorse. Coda interna (Channel) + BackgroundService che spedisce
/// una mail alla volta con una piccola pausa, per non incappare nei filtri anti-spam del provider.
/// Configurazione SMTP: letta da res_settings (chiavi email.*), con fallback su appsettings
/// "Email" per il primo avvio. La password è cifrata a riposo con DPAPI (CurrentUser), mai
/// restituita in chiaro al client (solo HasPassword).
/// </summary>
public class EmailService : BackgroundService
{
    private readonly ResourcesDbService _rdb;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly Channel<MimeMessage> _queue = Channel.CreateBounded<MimeMessage>(500);

    public EmailService(ResourcesDbService rdb, IConfiguration config, ILogger<EmailService> logger)
    {
        _rdb = rdb;
        _config = config;
        _logger = logger;
    }

    // ── Configurazione ──────────────────────────────────────────

    public EmailSettingsDto ResolveConfig()
    {
        using var c = _rdb.Open();
        var rows = c.Query<(string SettingKey, string SettingValue)>(
            "SELECT `key` AS SettingKey, `value` AS SettingValue FROM res_settings WHERE `key` LIKE 'email.%'")
            .ToDictionary(r => r.SettingKey, r => r.SettingValue);

        string Get(string key, string fallback) =>
            rows.TryGetValue(key, out string? v) && !string.IsNullOrEmpty(v) ? v : fallback;

        string? encryptedPassword = rows.TryGetValue("email.password", out string? p) && !string.IsNullOrEmpty(p)
            ? p
            : null;

        return new EmailSettingsDto
        {
            Enabled = Get("email.enabled", _config["Email:Enabled"] ?? "false") == "1"
                      || Get("email.enabled", "") == "true",
            SmtpHost = Get("email.smtphost", _config["Email:SmtpHost"] ?? ""),
            SmtpPort = int.TryParse(Get("email.smtpport", _config["Email:SmtpPort"] ?? "465"), out int port) ? port : 465,
            Security = Get("email.security", _config["Email:Security"] ?? "auto"),
            From = Get("email.from", _config["Email:From"] ?? ""),
            FromName = Get("email.fromname", _config["Email:FromName"] ?? "ATEC PM"),
            Username = Get("email.username", _config["Email:Username"] ?? ""),
            Password = encryptedPassword != null ? DecryptPassword(encryptedPassword) : _config["Email:Password"],
            HasPassword = encryptedPassword != null || !string.IsNullOrEmpty(_config["Email:Password"]),
            WebUrl = Get("email.weburl", _config["Email:WebUrl"] ?? ""),
        };
    }

    public bool Enabled
    {
        get
        {
            EmailSettingsDto cfg = ResolveConfig();
            return cfg.Enabled && !string.IsNullOrWhiteSpace(cfg.SmtpHost) && !string.IsNullOrWhiteSpace(cfg.From);
        }
    }

    public void SaveConfig(EmailSettingsDto dto)
    {
        using var c = _rdb.Open();
        void Set(string key, string value) => c.Execute(
            "INSERT INTO res_settings (`key`, `value`) VALUES (@K, @V) " +
            "ON DUPLICATE KEY UPDATE `value` = VALUES(`value`)",
            new { K = key, V = value ?? "" });

        Set("email.enabled", dto.Enabled ? "1" : "0");
        Set("email.smtphost", dto.SmtpHost?.Trim() ?? "");
        Set("email.smtpport", dto.SmtpPort.ToString());
        Set("email.security", string.IsNullOrWhiteSpace(dto.Security) ? "auto" : dto.Security.Trim());
        Set("email.from", dto.From?.Trim() ?? "");
        Set("email.fromname", string.IsNullOrWhiteSpace(dto.FromName) ? "ATEC PM" : dto.FromName.Trim());
        Set("email.username", dto.Username?.Trim() ?? "");
        Set("email.weburl", dto.WebUrl?.Trim() ?? "");

        // Write-only: aggiorna la password SOLO se l'admin ne ha digitata una nuova (cifrata a riposo).
        if (!string.IsNullOrEmpty(dto.Password))
            Set("email.password", EncryptPassword(dto.Password));
    }

    // Stessa cifratura DPAPI dei segreti di configurazione: sul server aziendale il
    // programma gira come servizio (nessun profilo utente caricato) e ProtectedConfigHelper
    // sceglie da solo l'ambito giusto. Cifrare qui "per utente" renderebbe la password
    // SMTP illeggibile al primo riavvio del servizio.
    private static string EncryptPassword(string plain) => Segreti.Cifra(plain);

    private static string? DecryptPassword(string encryptedBase64)
    {
        try
        {
            return Segreti.Decifra(encryptedBase64);
        }
        catch
        {
            return null; // cifrata da un'altra macchina/utente: da riconfigurare
        }
    }

    // ── Accodamento mail ────────────────────────────────────────

    public void QueueSummaryMail(PlanSummaryMailData data)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(data.ToEmail)) return;
        EmailSettingsDto cfg = ResolveConfig();

        int nNuove = data.Nuove.Count, nMod = data.Modificate.Count, nCanc = data.Cancellate.Count;
        string subject = $"Aggiornamento piano attività: {nNuove} nuove, {nMod} modificate, {nCanc} cancellate";

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(cfg.FromName, cfg.From));
        msg.To.Add(new MailboxAddress(data.ToName, data.ToEmail));
        msg.Subject = subject;
        msg.Body = new BodyBuilder
        {
            TextBody = BuildSummaryText(data, cfg.WebUrl),
            HtmlBody = BuildSummaryHtml(data, cfg.WebUrl),
        }.ToMessageBody();

        if (!_queue.Writer.TryWrite(msg))
            _logger.LogWarning("[EmailService] Coda piena, mail di riepilogo scartata per {Email}", data.ToEmail);
    }

    public void QueueDepartmentMail(DepartmentSummaryMailData data)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(data.ToEmail)) return;
        EmailSettingsDto cfg = ResolveConfig();

        int total = data.PerTecnico.Sum(t => t.Righe.Count);
        string subject = data.Globale
            ? $"Riepilogo modifiche al piano: {total} modifiche"
            : $"Riepilogo {data.RepartoNome}: {total} modifiche";

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(cfg.FromName, cfg.From));
        msg.To.Add(new MailboxAddress(data.ToName, data.ToEmail));
        msg.Subject = subject;
        msg.Body = new BodyBuilder
        {
            TextBody = BuildDepartmentText(data, cfg.WebUrl),
            HtmlBody = BuildDepartmentHtml(data, cfg.WebUrl),
        }.ToMessageBody();

        if (!_queue.Writer.TryWrite(msg))
            _logger.LogWarning("[EmailService] Coda piena, mail di reparto scartata per {Email}", data.ToEmail);
    }

    /// <summary>Accoda una mail generica (es. RDO Acquisti). No-op se SMTP disabilitato.</summary>
    public bool QueueSimpleMail(string toEmail, string toName, string subject, string textBody, string? htmlBody = null)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(toEmail)) return false;
        EmailSettingsDto cfg = ResolveConfig();

        var msg = new MimeMessage();
        try
        {
            msg.From.Add(new MailboxAddress(cfg.FromName, cfg.From));
            msg.To.Add(new MailboxAddress(string.IsNullOrWhiteSpace(toName) ? toEmail : toName, toEmail));
        }
        catch (MimeKit.ParseException ex)
        {
            // 🪤 MimeKit valida l'indirizzo NEL COSTRUTTORE. Un'anagrafica con «Mario Rossi
            // <m@atec.srl>», due indirizzi separati da «;» o anche solo uno spazio di troppo
            // faceva saltare l'intero giro di invii del chiamante: le mail già accodate
            // partivano, ma la registrazione a fine ciclo no — e al secondo tentativo chi
            // l'aveva già ricevuta se la ritrovava due volte. Qui è un «no» a questo solo
            // destinatario, che il chiamante conta fra i falliti.
            _logger.LogWarning(ex,
                "[EmailService] Indirizzo non valido in anagrafica, mail non accodata: {Email}", toEmail);
            return false;
        }

        msg.Subject = subject;
        msg.Body = new BodyBuilder
        {
            TextBody = textBody ?? "",
            HtmlBody = htmlBody ?? textBody ?? "",
        }.ToMessageBody();

        if (!_queue.Writer.TryWrite(msg))
        {
            _logger.LogWarning("[EmailService] Coda piena, mail semplice scartata per {Email}", toEmail);
            return false;
        }
        return true;
    }

    // ── Test invio (sincrono, per il pulsante "Invia prova") ─────

    public async Task<(bool Ok, string Message)> SendTestAsync(string toEmail)
    {
        EmailSettingsDto cfg = ResolveConfig();
        if (string.IsNullOrWhiteSpace(cfg.SmtpHost) || string.IsNullOrWhiteSpace(cfg.From))
            return (false, "Configurazione SMTP incompleta (server o mittente mancanti).");
        if (string.IsNullOrWhiteSpace(toEmail))
            return (false, "Indirizzo destinatario mancante.");

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(cfg.FromName, cfg.From));
        msg.To.Add(MailboxAddress.Parse(toEmail));
        msg.Subject = "ATEC PM — email di prova";
        msg.Body = new TextPart("plain")
        {
            Text = "Questa è un'email di prova dal modulo Gestione Risorse di ATEC PM.\n" +
                   "Se la ricevi, la configurazione SMTP funziona correttamente."
        };

        try
        {
            using var client = new SmtpClient();
            await ConnectAndAuthenticateAsync(client, cfg);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            return (true, $"Email di prova inviata a {toEmail}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EmailService] Test invio fallito");
            return (false, $"Invio fallito: {ex.Message}");
        }
    }

    private static async Task ConnectAndAuthenticateAsync(SmtpClient client, EmailSettingsDto cfg)
    {
        SecureSocketOptions options = cfg.Security switch
        {
            "ssl" => SecureSocketOptions.SslOnConnect,
            "starttls" => SecureSocketOptions.StartTls,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.Auto,
        };
        await client.ConnectAsync(cfg.SmtpHost, cfg.SmtpPort, options);
        if (!string.IsNullOrEmpty(cfg.Username))
            await client.AuthenticateAsync(cfg.Username, cfg.Password ?? "");
    }

    // ── Loop di invio in background ──────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (MimeMessage msg in _queue.Reader.ReadAllAsync(ct))
        {
            if (!Enabled) continue; // disabilitato nel frattempo: scarta silenziosamente

            EmailSettingsDto cfg = ResolveConfig();
            try
            {
                using var client = new SmtpClient();
                await ConnectAndAuthenticateAsync(client, cfg);
                await client.SendAsync(msg, ct);
                await client.DisconnectAsync(true, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EmailService] Invio fallito per {To}", msg.To);
            }

            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
        }
    }

    // ── Contenuto mail: riepilogo personale ──────────────────────

    private static string BuildSummaryText(PlanSummaryMailData d, string webUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ciao {d.ToName},");
        sb.AppendLine();
        sb.AppendLine(d.ModificheDi != null
            ? $"{d.ModificheDi} ha aggiornato il tuo piano attività. Ecco cosa cambia per te:"
            : "Il tuo piano attività è stato aggiornato. Ecco cosa cambia per te:");

        void Section(string title, List<PlanChangeLine> righe)
        {
            if (righe.Count == 0) return;
            sb.AppendLine();
            sb.AppendLine($"--- {title} ---");
            foreach (PlanChangeLine r in righe)
            {
                sb.AppendLine($"* {r.Attivita} — {r.Periodo}");
                if (!string.IsNullOrWhiteSpace(r.Note)) sb.AppendLine($"  Note: {r.Note}");
            }
        }
        Section("Nuove attività", d.Nuove);
        Section("Attività modificate", d.Modificate);
        Section("Attività cancellate", d.Cancellate);

        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(webUrl)) sb.AppendLine($"Piano risorse: {webUrl}");
        sb.AppendLine();
        sb.AppendLine("Messaggio automatico di ATEC PM — non rispondere a questa mail.");
        return sb.ToString();
    }

    private static string BuildSummaryHtml(PlanSummaryMailData d, string webUrl)
    {
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;color:#111\">");
        sb.Append($"<p>Ciao {Html(d.ToName)},</p>");
        sb.Append($"<p>{(d.ModificheDi != null ? $"{Html(d.ModificheDi)} ha aggiornato il tuo piano attività. Ecco cosa cambia per te:" : "Il tuo piano attività è stato aggiornato. Ecco cosa cambia per te:")}</p>");

        void Section(string title, string color, List<PlanChangeLine> righe)
        {
            if (righe.Count == 0) return;
            sb.Append($"<p style=\"font-weight:600;color:{color};margin-bottom:4px\">{Html(title)}</p><ul>");
            foreach (PlanChangeLine r in righe)
            {
                sb.Append($"<li>{Html(r.Attivita)} — {Html(r.Periodo)}");
                if (!string.IsNullOrWhiteSpace(r.Note)) sb.Append($"<br><span style=\"color:#555\">{Html(r.Note)}</span>");
                sb.Append("</li>");
            }
            sb.Append("</ul>");
        }
        Section("Nuove attività", "#15803d", d.Nuove);
        Section("Attività modificate", "#b45309", d.Modificate);
        Section("Attività cancellate", "#b91c1c", d.Cancellate);

        if (!string.IsNullOrWhiteSpace(webUrl))
            sb.Append($"<p><a href=\"{Html(webUrl)}\">Apri il piano risorse</a></p>");
        sb.Append("<p style=\"color:#888;font-size:12px\">Messaggio automatico di ATEC PM — non rispondere a questa mail.</p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    // ── Contenuto mail: riepilogo reparto / PM ───────────────────

    private static string BuildDepartmentText(DepartmentSummaryMailData d, string webUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ciao {d.ToName},");
        sb.AppendLine();
        sb.AppendLine(d.Globale
            ? "Ecco il riepilogo delle modifiche al piano risorse:"
            : $"Ecco il riepilogo delle modifiche al piano del reparto {d.RepartoNome}:");

        foreach ((string tecnico, List<PlanChangeLine> righe) in d.PerTecnico)
        {
            if (righe.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"--- {tecnico} ---");
            foreach (PlanChangeLine r in righe)
            {
                string kind = r.Kind switch { "new" => "Nuova", "changed" => "Modificata", _ => "Cancellata" };
                string autore = r.AutoreNome != null ? $" (da {r.AutoreNome})" : "";
                sb.AppendLine($"* {kind}: {r.Attivita} — {r.Periodo}{autore}");
            }
        }

        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(webUrl)) sb.AppendLine($"Piano risorse: {webUrl}");
        sb.AppendLine();
        sb.AppendLine("Messaggio automatico di ATEC PM — non rispondere a questa mail.");
        return sb.ToString();
    }

    private static string BuildDepartmentHtml(DepartmentSummaryMailData d, string webUrl)
    {
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;color:#111\">");
        sb.Append($"<p>Ciao {Html(d.ToName)},</p>");
        sb.Append($"<p>{(d.Globale ? "Ecco il riepilogo delle modifiche al piano risorse:" : $"Ecco il riepilogo delle modifiche al piano del reparto {Html(d.RepartoNome ?? "")}:")}</p>");

        foreach ((string tecnico, List<PlanChangeLine> righe) in d.PerTecnico)
        {
            if (righe.Count == 0) continue;
            sb.Append($"<p style=\"font-weight:600;margin-bottom:2px\">{Html(tecnico)}</p><ul>");
            foreach (PlanChangeLine r in righe)
            {
                string color = r.Kind switch { "new" => "#15803d", "changed" => "#b45309", _ => "#b91c1c" };
                string kind = r.Kind switch { "new" => "Nuova", "changed" => "Modificata", _ => "Cancellata" };
                string autore = r.AutoreNome != null ? $" <span style=\"color:#555\">(da {Html(r.AutoreNome)})</span>" : "";
                sb.Append($"<li><span style=\"color:{color};font-weight:600\">{kind}</span>: {Html(r.Attivita)} — {Html(r.Periodo)}{autore}</li>");
            }
            sb.Append("</ul>");
        }

        if (!string.IsNullOrWhiteSpace(webUrl))
            sb.Append($"<p><a href=\"{Html(webUrl)}\">Apri il piano risorse</a></p>");
        sb.Append("<p style=\"color:#888;font-size:12px\">Messaggio automatico di ATEC PM — non rispondere a questa mail.</p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Html(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
