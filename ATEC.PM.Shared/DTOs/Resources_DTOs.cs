namespace ATEC.PM.Shared.DTOs;

// ═══════════════════════════════════════════════════════════════
// Gestione Risorse — pianificazione allocazioni su dipendenti.
// Tipi allocazione: OP (operativo), FLEX (finestra flessibile), FERIE.
// Agganci: risorsa = employees, commessa = projects.
// Service (Syncorgest) e Altre attività = anagrafiche nuove dedicate.
// ═══════════════════════════════════════════════════════════════

public class ResAssignmentDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string Tipo { get; set; } = "OP";            // OP | FLEX | FERIE
    public DateTime DataInizio { get; set; }
    public DateTime DataFine { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectCode { get; set; }
    public string? ProjectTitle { get; set; }
    public int? ServiceId { get; set; }
    public string? ServiceCod { get; set; }
    public int? OtherActivityId { get; set; }
    public string? OtherActivityDesc { get; set; }
    public string? Descrizione { get; set; }
    public bool HasConflict { get; set; }

    // Audit "ultima modifica" (collaborazione multi-utente)
    public int? UpdatedBy { get; set; }
    public string? UpdatedByName { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public int Giorni => (DataFine.Date - DataInizio.Date).Days + 1;
}

// Create: una riga per ciascuna risorsa selezionata (batch).
public class ResAssignmentCreateRequest
{
    public List<int> EmployeeIds { get; set; } = new();
    public string Tipo { get; set; } = "OP";
    public DateTime DataInizio { get; set; }
    public DateTime DataFine { get; set; }
    public int? ProjectId { get; set; }
    public int? ServiceId { get; set; }
    public int? OtherActivityId { get; set; }
    public string? Descrizione { get; set; }
}

// Update: singola allocazione.
public class ResAssignmentUpdateRequest
{
    public int EmployeeId { get; set; }
    public string Tipo { get; set; } = "OP";
    public DateTime DataInizio { get; set; }
    public DateTime DataFine { get; set; }
    public int? ProjectId { get; set; }
    public int? ServiceId { get; set; }
    public int? OtherActivityId { get; set; }
    public string? Descrizione { get; set; }

    // Concorrenza ottimistica: versione (updated_at) vista dal client al momento dell'apertura.
    // Se valorizzata e diversa da quella sul server → 409 (modificata da un altro utente). Null = nessun controllo.
    public DateTime? ExpectedUpdatedAt { get; set; }
}

// Notifica real-time (SignalR) di modifica allocazioni inviata dal server ai client connessi.
// Il client la usa come segnale per ri-caricare le allocazioni (rispettando drag/dialog in corso).
public class ResAssignmentChange
{
    public string Action { get; set; } = "";       // "create" | "update" | "delete"
    public List<int> Ids { get; set; } = new();     // id allocazioni interessate (vuoto per i batch create)
}

// Notifica real-time (SignalR, evento "PresenceChanged") con l'elenco dei dipendenti che hanno
// almeno un client Gantt connesso in questo momento (pallino verde/rosso accanto al nome).
public class PresenceSnapshot
{
    public List<int> OnlineEmployeeIds { get; set; } = new();
}

// ── Anagrafica Service (Syncorgest) ────────────────────────────

public class ResServiceDto
{
    public int Id { get; set; }
    public string Cod { get; set; } = "";
    public string? Cliente { get; set; }
    public bool IsActive { get; set; } = true;

    public string Display => string.IsNullOrWhiteSpace(Cliente) ? Cod : $"{Cod} — {Cliente}";
}

public class ResServiceSaveRequest
{
    public string Cod { get; set; } = "";
    public string? Cliente { get; set; }
}

// ── Anagrafica Altre attività ──────────────────────────────────

public class ResOtherActivityDto
{
    public int Id { get; set; }
    public string Descrizione { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class ResOtherActivitySaveRequest
{
    public string Descrizione { get; set; } = "";
}

// ═══════════════════════════════════════════════════════════════
// Digest email — riepilogo giornaliero (o su richiesta) delle modifiche
// al piano risorse, inviato a dipendente/responsabile-reparto/PM.
// Confronto tra un'istantanea del piano ("snapshot") e lo stato attuale.
// ═══════════════════════════════════════════════════════════════

/// <summary>Una riga di variazione (Nuova/Modificata/Cancellata) su un'allocazione.</summary>
public class PlanChangeLine
{
    public int AssignmentId { get; set; }
    public string Kind { get; set; } = "";       // "new" | "changed" | "deleted"
    public string Attivita { get; set; } = "";   // etichetta leggibile (commessa/service/attività/ferie)
    public string Periodo { get; set; } = "";    // "dal dd/MM/yyyy al dd/MM/yyyy"
    public string? Note { get; set; }
    public string? AutoreNome { get; set; }      // chi ha fatto la modifica (null = sconosciuto)
}

/// <summary>Conteggio modifiche pendenti dall'ultima istantanea — solo informativo (badge toolbar).</summary>
public class NotifyPendingDto
{
    public int TotalChanges { get; set; }
    public bool EmailConfigurata { get; set; }
    public List<NotifyPendingEmployee> Employees { get; set; } = new();
}

public class NotifyPendingEmployee
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public int Nuove { get; set; }
    public int Modificate { get; set; }
    public int Cancellate { get; set; }
    public bool HasEmail { get; set; }
}

/// <summary>Esito di un invio digest (automatico o manuale).</summary>
public class NotifySendResultDto
{
    public int EmailInviate { get; set; }
    public int DipendentiSenzaEmail { get; set; }
    public List<string> NotifiedNames { get; set; } = new();
    public bool BaselineCreated { get; set; }
    public string Message { get; set; } = "";

    public int ResponsabiliNotificati { get; set; }
    public List<string> ResponsabiliNotificatiNomi { get; set; } = new();

    public int PmNotificati { get; set; }
    public List<string> PmNotificatiNomi { get; set; } = new();
}

/// <summary>Anteprima digest completo (nessun invio, nessuna nuova istantanea).</summary>
public class DigestPreviewDto
{
    public List<DigestPreviewPerson> Dipendenti { get; set; } = new();
}

public class DigestPreviewPerson
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public bool HasEmail { get; set; }
    public List<PlanChangeLine> Righe { get; set; } = new();
}

/// <summary>Anteprima per l'invio selettivo (dialog "Notifica subito"): una riga spuntabile per persona.</summary>
public class SelectivePreviewDto
{
    public List<SelectivePerson> Dipendenti { get; set; } = new();
}

public class SelectivePerson
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public bool HasEmail { get; set; }
    public List<PlanChangeLine> Righe { get; set; } = new();
}

/// <summary>Richiesta di invio selettivo: solo le allocazioni scelte dall'utente nel dialog.</summary>
public class SendSelectedRequest
{
    public List<int> AssignmentIds { get; set; } = new();
}

/// <summary>Impostazioni digest automatico (tab admin), lette/scritte in res_settings.</summary>
public class PlanDigestSettingsDto
{
    public bool DigestEnabled { get; set; }
    public string DigestTime { get; set; } = "07:00";
    public bool DigestWeekends { get; set; } = true;
    public string DigestLastRun { get; set; } = "";
}

/// <summary>Stato pannello admin: orario server, impostazioni, contatori, registro esecuzioni.</summary>
public class DigestStatusDto
{
    public DateTime ServerTimeLocal { get; set; }
    public DateTime ServerTimeUtc { get; set; }
    public PlanDigestSettingsDto Settings { get; set; } = new();
    public bool EmailConfigurata { get; set; }
    public int AttivitaNelPiano { get; set; }
    public int DipendentiConEmail { get; set; }
    public int DipendentiSenzaEmail { get; set; }
    public List<DigestLogEntry> UltimeEsecuzioni { get; set; } = new();
}

public class DigestLogEntry
{
    public DateTime RunUtc { get; set; }
    public string Trigger { get; set; } = "";     // "automatico" | "manuale"
    public int EmailInviate { get; set; }
    public int SenzaEmail { get; set; }
    public string Esito { get; set; } = "";
}

// ── Configurazione email (SMTP) — ADMIN only ───────────────────

public class EmailSettingsDto
{
    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 465;
    public string Security { get; set; } = "auto";  // auto | ssl | starttls | none
    public string From { get; set; } = "";
    public string FromName { get; set; } = "ATEC PM";
    public string Username { get; set; } = "";
    public string? Password { get; set; }           // write-only: valorizzata solo in scrittura
    public bool HasPassword { get; set; }           // read-only: indica se una password è già salvata
    public string WebUrl { get; set; } = "";
}

public class TestEmailRequest
{
    public string ToEmail { get; set; } = "";
}
