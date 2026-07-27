using System.Text;
using System.Text.RegularExpressions;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Rende in HTML l'anteprima di una mail salvata da Outlook (.msg) o in formato
/// standard (.eml): intestazione (oggetto/da/a/cc/data), elenco allegati e corpo.
/// Le immagini inline (riferimenti cid:) vengono incorporate come data URI, così
/// il documento è autosufficiente e visualizzabile nell'iframe sandbox del client.
/// </summary>
public static class EmailPreviewService
{
    // I .msg Outlook usano codepage Windows (es. 1252) che su .NET Core non sono
    // disponibili di default: senza questa registrazione MsgReader/RtfPipe falliscono
    static EmailPreviewService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private sealed class EmailData
    {
        public string Subject = "";
        public string From = "";
        public string To = "";
        public string Cc = "";
        public DateTime? Date;
        public string? BodyHtml;
        public string? BodyText;
        public List<string> Attachments = new();
    }

    public static string RenderHtml(string fullPath)
    {
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        EmailData data = ext == ".msg" ? ParseMsg(fullPath) : ParseEml(fullPath);
        return BuildDocument(Path.GetFileName(fullPath), data);
    }

    // === .EML (MIME standard) via MimeKit (già presente tramite MailKit) ===
    private static EmailData ParseEml(string fullPath)
    {
        var message = MimeKit.MimeMessage.Load(fullPath);
        var data = new EmailData
        {
            Subject = message.Subject ?? "",
            From = message.From?.ToString() ?? "",
            To = message.To?.ToString() ?? "",
            Cc = message.Cc?.ToString() ?? "",
            Date = message.Date == DateTimeOffset.MinValue ? null : message.Date.LocalDateTime,
            BodyText = message.TextBody,
        };

        var html = message.HtmlBody;
        if (html != null)
        {
            foreach (var part in message.BodyParts.OfType<MimeKit.MimePart>())
            {
                if (string.IsNullOrEmpty(part.ContentId) || part.Content == null) continue;
                using var ms = new MemoryStream();
                part.Content.DecodeTo(ms);
                var mime = part.ContentType?.MimeType ?? "application/octet-stream";
                html = ReplaceCid(html, part.ContentId, ToDataUri(mime, ms.ToArray()));
            }
        }
        data.BodyHtml = html;

        // MimeKit esclude già dagli Attachments le parti inline (immagini nel corpo)
        foreach (var att in message.Attachments)
        {
            string? name = att.ContentDisposition?.FileName ?? att.ContentType?.Name;
            if (att is MimeKit.MessagePart mp)
                name ??= (mp.Message?.Subject ?? "messaggio") + ".eml";
            data.Attachments.Add(string.IsNullOrWhiteSpace(name) ? "allegato" : name);
        }
        return data;
    }

    // === .MSG (formato binario Outlook) via MsgReader — non richiede Outlook ===
    private static EmailData ParseMsg(string fullPath)
    {
        using var message = new MsgReader.Outlook.Storage.Message(fullPath);
        var data = new EmailData
        {
            Subject = message.Subject ?? "",
            From = FormatAddress(message.Sender?.DisplayName, message.Sender?.Email),
            Date = message.SentOn?.LocalDateTime,
        };

        var to = new List<string>();
        var cc = new List<string>();
        foreach (var r in message.Recipients)
        {
            var formatted = FormatAddress(r.DisplayName, r.Email);
            if (formatted.Length == 0) continue;
            if (r.Type == MsgReader.Outlook.RecipientType.Cc) cc.Add(formatted);
            else if (r.Type == MsgReader.Outlook.RecipientType.Bcc) continue;
            else to.Add(formatted);
        }
        data.To = string.Join("; ", to);
        data.Cc = string.Join("; ", cc);

        // Corpo: HTML se presente; le mail Outlook interne hanno spesso solo RTF → RtfPipe
        var html = message.BodyHtml;
        if (string.IsNullOrWhiteSpace(html) && !string.IsNullOrWhiteSpace(message.BodyRtf))
        {
            try { html = RtfPipe.Rtf.ToHtml(message.BodyRtf); }
            catch { html = null; }
        }
        data.BodyText = message.BodyText;

        foreach (var obj in message.Attachments)
        {
            switch (obj)
            {
                case MsgReader.Outlook.Storage.Attachment att:
                {
                    var cid = att.ContentId?.Trim('<', '>');
                    var replaced = false;
                    if (html != null && !string.IsNullOrEmpty(cid) &&
                        html.Contains("cid:" + cid, StringComparison.OrdinalIgnoreCase))
                    {
                        var mime = MimeFromName(att.FileName ?? "");
                        html = ReplaceCid(html, cid, ToDataUri(mime, att.Data ?? Array.Empty<byte>()));
                        replaced = true;
                    }
                    if (!replaced)
                        data.Attachments.Add(string.IsNullOrWhiteSpace(att.FileName) ? "allegato" : att.FileName);
                    break;
                }
                case MsgReader.Outlook.Storage.Message embedded:
                    data.Attachments.Add((embedded.Subject ?? "messaggio") + ".msg");
                    break;
            }
        }
        data.BodyHtml = html;
        return data;
    }

    private static string BuildDocument(string fileName, EmailData mail)
    {
        var sb = new StringBuilder();
        // CSS scopato (.mail-*): niente selettori globali su table/th/td per non
        // alterare le tabelle contenute nel corpo della mail (Outlook ne abusa)
        sb.Append(@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>
        * { box-sizing:border-box; }
        body { margin:0; font-family:Segoe UI,sans-serif; font-size:13px; background:#F7F8FA; padding:12px; }
        .info { padding:8px 12px; background:#fff; border:1px solid #E4E7EC; margin-bottom:8px; font-weight:600; }
        .mail-head { background:#fff; border:1px solid #E4E7EC; padding:12px 16px; margin-bottom:8px; }
        .mail-subject { font-size:15px; font-weight:600; margin-bottom:8px; }
        .mail-meta { border-collapse:collapse; }
        .mail-meta th { text-align:left; vertical-align:top; padding:2px 12px 2px 0; color:#6B7280; font-weight:600; white-space:nowrap; font-size:12px; }
        .mail-meta td { padding:2px 0; word-break:break-word; font-size:12px; }
        .mail-atts { margin-top:10px; display:flex; flex-wrap:wrap; gap:6px; }
        .mail-att { padding:3px 10px; background:#F7F8FA; border:1px solid #E4E7EC; font-size:12px; }
        .mail-body { background:#fff; border:1px solid #E4E7EC; padding:16px; overflow-x:auto; }
        .mail-body img { max-width:100%; height:auto; }
        .mail-text { white-space:pre-wrap; font-family:Segoe UI,sans-serif; margin:0; }
    </style></head><body>");

        sb.Append($"<div class='info'>✉️ {Html(fileName)}</div>");

        sb.Append("<div class='mail-head'>");
        sb.Append($"<div class='mail-subject'>{Html(string.IsNullOrWhiteSpace(mail.Subject) ? "(senza oggetto)" : mail.Subject)}</div>");
        sb.Append("<table class='mail-meta'>");
        AppendMeta(sb, "Da", mail.From);
        AppendMeta(sb, "A", mail.To);
        AppendMeta(sb, "Cc", mail.Cc);
        if (mail.Date.HasValue)
            AppendMeta(sb, "Data", mail.Date.Value.ToString("dd/MM/yyyy HH:mm"));
        sb.Append("</table>");
        if (mail.Attachments.Count > 0)
        {
            sb.Append("<div class='mail-atts'>");
            foreach (var name in mail.Attachments)
                sb.Append($"<span class='mail-att'>📎 {Html(name)}</span>");
            sb.Append("</div>");
        }
        sb.Append("</div>");

        sb.Append("<div class='mail-body'>");
        if (!string.IsNullOrWhiteSpace(mail.BodyHtml))
            sb.Append(CleanBodyHtml(mail.BodyHtml));
        else if (!string.IsNullOrWhiteSpace(mail.BodyText))
            sb.Append($"<pre class='mail-text'>{Html(mail.BodyText)}</pre>");
        else
            sb.Append("<p style='color:#6B7280'>(nessun contenuto)</p>");
        sb.Append("</div>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendMeta(StringBuilder sb, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append($"<tr><th>{label}</th><td>{Html(value)}</td></tr>");
    }

    /// <summary>
    /// Il corpo mail arriva come documento HTML completo: rimuove il wrapper
    /// (doctype/html/head/body) mantenendo gli style inline, e toglie script e
    /// riferimenti esterni. Gli script sarebbero comunque bloccati dall'iframe
    /// sandbox del client: qui è difesa in profondità.
    /// </summary>
    private static string CleanBodyHtml(string html)
    {
        html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<\?xml[^>]*\?>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</?(html|head|body)[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<(meta|base|link)[^>]*>", "", RegexOptions.IgnoreCase);
        return html;
    }

    private static string ReplaceCid(string html, string contentId, string dataUri)
    {
        var cid = contentId.Trim('<', '>');
        return Regex.Replace(html, "cid:" + Regex.Escape(cid), dataUri, RegexOptions.IgnoreCase);
    }

    private static string ToDataUri(string mimeType, byte[] data) =>
        $"data:{mimeType};base64,{Convert.ToBase64String(data)}";

    private static string MimeFromName(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    private static string FormatAddress(string? name, string? email)
    {
        bool hasName = !string.IsNullOrWhiteSpace(name);
        bool hasEmail = !string.IsNullOrWhiteSpace(email) &&
                        !string.Equals(name, email, StringComparison.OrdinalIgnoreCase);
        if (hasName && hasEmail) return $"{name} <{email}>";
        if (hasName) return name!;
        return email ?? "";
    }

    private static string Html(string? value) => System.Web.HttpUtility.HtmlEncode(value ?? "");
}
