using System.Globalization;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.Hr;

// Parte «Solleciti» di HrAttendanceService (classe parziale, 04/09/2026): il servizio era un
// file solo di 2.796 righe. Stesso tipo e stesso comportamento, si legge per argomento.
public partial class HrAttendanceService
{
    // ── SOLLECITI DELLE TIMBRATURE MANCANTI ───────────────────────────────────
    //
    // Port dei due pulsanti del calendario originale. Il «?» rosso del calendario è la
    // fonte: si sollecita quello che la griglia mostra, non un secondo conteggio fatto
    // per conto suo — altrimenti la mail direbbe giorni diversi da quelli che la persona
    // vede sullo schermo.

    private static readonly string[] NomiMesi =
    {
        "Gennaio", "Febbraio", "Marzo", "Aprile", "Maggio", "Giugno",
        "Luglio", "Agosto", "Settembre", "Ottobre", "Novembre", "Dicembre",
    };

    /// <summary>
    /// Chi ha giornate col «?» nel mese, con il testo del sollecito già pronto.
    /// <paramref name="employeeId"/> valorizzato = solo quella persona, come il filtro
    /// della pagina (nel VB il sollecito rispetta il filtro dipendente attivo).
    /// </summary>
    public HrRemindersDto GetReminders(int year, int month, int? departmentId, int? employeeId)
    {
        HrMonthlyCalendarDto calendario = GetMonthlyCalendar(year, month, departmentId);

        var risultato = new HrRemindersDto { Year = year, Month = month };

        // I buchi stanno sulla riga PRESENZA: sono le celle col «?».
        var buchiPerDipendente = calendario.Rows
            .Where(r => r.VoceType == "PRESENZA")
            .Where(r => employeeId == null || r.EmployeeId == employeeId.Value)
            .Select(r => (
                r.EmployeeId,
                r.EmployeeKey,
                Giorni: r.Days.Where(d => d.Value.Text == "?").Select(d => d.Key).OrderBy(g => g).ToList()))
            .Where(x => x.Giorni.Count > 0)
            .ToList();

        if (buchiPerDipendente.Count == 0) return risultato;

        int[] ids = buchiPerDipendente.Select(x => x.EmployeeId).ToArray();

        using MySqlConnection c = _db.Open();

        var recapiti = c.Query<(int Id, string? Email, string FirstName)>(@"
            SELECT id AS Id, email AS Email, COALESCE(first_name, '') AS FirstName
            FROM employees WHERE id IN @Ids", new { Ids = ids })
            .ToDictionary(x => x.Id, x => (x.Email, x.FirstName));

        var primo = new DateTime(year, month, 1);
        var ultimo = primo.AddMonths(1).AddDays(-1);
        var ultimoSollecito = c.Query<(int EmployeeId, DateTime SentAt)>(@"
            SELECT employee_id AS EmployeeId, MAX(sent_at) AS SentAt
            FROM hr_reminders
            WHERE employee_id IN @Ids AND work_date BETWEEN @Primo AND @Ultimo
            GROUP BY employee_id", new { Ids = ids, Primo = primo, Ultimo = ultimo })
            .ToDictionary(x => x.EmployeeId, x => x.SentAt);

        string mese = NomiMesi[month - 1];

        foreach (var (idDipendente, nomeCompleto, giorni) in buchiPerDipendente)
        {
            recapiti.TryGetValue(idDipendente, out (string? Email, string FirstName) recapito);
            string nome = string.IsNullOrWhiteSpace(recapito.FirstName) ? nomeCompleto : recapito.FirstName;
            string elenco = string.Join(", ", giorni.Select(g => $"{g} {mese}"));

            risultato.Targets.Add(new HrReminderTargetDto
            {
                EmployeeId = idDipendente,
                EmployeeName = nomeCompleto,
                Email = recapito.Email,
                MissingDays = giorni,
                LastReminderAt = ultimoSollecito.TryGetValue(idDipendente, out DateTime quando) ? quando : null,
                Subject = $"Timbrature mancanti - {mese} {year}",
                // I due testi dell'originale: dal client di posta si può anche inserire la
                // causale su eTime, dall'invio automatico si chiede solo di comunicarla.
                MailtoBody = TestoSollecito(nome, elenco, "Si prega di comunicare e/o inserire su eTime le relative causali di assenza."),
                Body = TestoSollecito(nome, elenco, "Si prega di comunicare le relative causali di assenza."),
            });
        }

        return risultato;
    }

    /// <summary>Il testo del sollecito, parola per parola come nel programma originale.</summary>
    private static string TestoSollecito(string nome, string giorni, string richiesta) =>
        $"""
        Gentile {nome},

        risultano mancanti le timbrature per i seguenti giorni:
        {giorni}

        {richiesta}

        Cordiali saluti,
        Ufficio Risorse Umane
        """;

    /// <summary>
    /// Segna come sollecitate le giornate indicate. Una riga per giornata (non per email):
    /// il secondo sollecito sullo stesso giorno aggiorna la data.
    ///
    /// <para>Dalla M117 si conserva anche il testo (destinatario, oggetto, corpo): è quello
    /// che la Cronologia Email rilegge. Le N giornate di uno stesso invio portano la stessa
    /// mail, come il <c>MailLog</c> dell'originale.</para>
    /// </summary>
    public void MarkReminders(
        int year, int month,
        IEnumerable<(int EmployeeId, List<int> Days, string? Email, string? Subject, string? Body)> solleciti,
        int sentBy, string channel)
    {
        var righe = solleciti
            .SelectMany(x => x.Days.Select(g => new
            {
                x.EmployeeId,
                WorkDate = new DateTime(year, month, g),
                SentBy = sentBy,
                Channel = channel,
                x.Email,
                x.Subject,
                x.Body,
            }))
            .ToList();

        if (righe.Count == 0) return;

        using MySqlConnection c = _db.Open();
        c.Execute(@"
            INSERT INTO hr_reminders (employee_id, work_date, sent_by, channel, email, subject, body)
            VALUES (@EmployeeId, @WorkDate, @SentBy, @Channel, @Email, @Subject, @Body)
            ON DUPLICATE KEY UPDATE sent_at = NOW(), sent_by = VALUES(sent_by), channel = VALUES(channel),
                                    email = VALUES(email), subject = VALUES(subject), body = VALUES(body)",
            righe);
    }
}
