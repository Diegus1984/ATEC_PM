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
