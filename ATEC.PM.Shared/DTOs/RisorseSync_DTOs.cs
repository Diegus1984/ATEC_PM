using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

// ═══════════════════════════════════════════════════════════════
// Sincronizzazione Risorse ATEC PM ⇄ ATEC Risorse (VPS).
// PIANO-SYNC-RISORSE.md §4. Le classi Sync* sono il CONTRATTO con
// il VPS ({BaseUrl}/api/sync): nomi e proprietà devono restare
// IDENTICI da entrambe le parti. Le Risorse* sono i DTO del solo
// lato ATEC PM (pannello impostazioni + stato del motore).
//
// Date-solo (DataInizio/DataFine): DateOnly, identico al lato VPS,
// nel JSON viaggiano come 'yyyy-MM-dd' (es. "2026-09-02"), mai con
// l'ora né con il fuso. UpdatedAt/CreatedAt sono UTC (DateTimeKind.Utc,
// cioè con la Z nel JSON).
//
// Endpoint (base {BaseUrl}/api/sync, ruolo SYNC o ADMIN):
//  GET status · GET assignments · POST assignments (List<SyncAssignmentUpsertDto>)
//  · POST assignments/delete (SyncDeleteRequest) · GET employees (TUTTI i dipendenti,
//  PasswordHash sempre null) · PUT employees · GET projects · PUT projects
//  · GET departments (SyncDepartmentsRequest completa) · PUT departments.
// La copia di riferimento con le regole di scrittura è Sync_DTOs.cs sul VPS.
//
// Regole anagrafiche (PM comanda): il VPS manda EmployeesChanged
// sull'hub SOLO se almeno una riga è created/updated — un giro che
// non cambia niente non riceve alcun eco dall'hub. L'account di
// servizio sul VPS ha ruolo SYNC (non ADMIN): /api/sync accetta
// SYNC o ADMIN.
// ═══════════════════════════════════════════════════════════════

/// <summary>GET /api/sync/status — contatori del VPS e ora del suo orologio.</summary>
public class SyncStatusDto
{
    public DateTime ServerUtc { get; set; }
    public int Employees { get; set; }
    public int Projects { get; set; }
    public int Departments { get; set; }
    public int Assignments { get; set; }
    public string Version { get; set; } = "";
}

/// <summary>GET /api/sync/assignments — una riga grezza di res_assignments del VPS.</summary>
public class SyncAssignmentDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Tipo { get; set; } = "OP";            // OP | FLEX | FERIE
    public DateOnly DataInizio { get; set; }
    public DateOnly DataFine { get; set; }
    public int? ProjectId { get; set; }
    public int? ServiceId { get; set; }
    public int? OtherActivityId { get; set; }
    public string? Descrizione { get; set; }
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}

/// <summary>POST /api/sync/assignments — upsert per id VPS (null = crea).</summary>
public class SyncAssignmentUpsertDto
{
    public int? Id { get; set; }
    public int EmployeeId { get; set; }                  // id VPS
    public string Tipo { get; set; } = "OP";
    public DateOnly DataInizio { get; set; }
    public DateOnly DataFine { get; set; }
    public int? ProjectId { get; set; }
    public int? ServiceId { get; set; }
    public int? OtherActivityId { get; set; }
    public string? Descrizione { get; set; }
    public int? UpdatedBy { get; set; }                  // id VPS dell'autore, può essere null
    public DateTime? UpdatedAt { get; set; }             // UTC; null = adesso
}

/// <summary>Esito riga per riga delle scritture sul VPS (assignments, employees, projects).</summary>
public class SyncUpsertResultDto
{
    public int Index { get; set; }
    public int? Id { get; set; }
    public string Action { get; set; } = "";             // created|updated|unchanged|skipped|deleted|missing
                                                         // (POST assignments risponde anche "unchanged": riga identica, niente scritto)
    public string? Error { get; set; }
}

/// <summary>POST /api/sync/assignments/delete.</summary>
public class SyncDeleteRequest
{
    public List<int> Ids { get; set; } = new();
    public int? MadeBy { get; set; }                     // id VPS di chi cancella, può essere null
}

/// <summary>PUT /api/sync/employees — anagrafica dipendente (PM comanda).</summary>
public class SyncEmployeeDto
{
    public int? Id { get; set; }                         // id VPS; null = crea nuovo
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public string EmpType { get; set; } = "INTERNAL";
    public string Status { get; set; } = "ACTIVE";
    public string? UserRole { get; set; }
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
}

/// <summary>PUT /api/sync/departments — reparti (per codice) e legami dipendente↔reparto.</summary>
public class SyncDepartmentsRequest
{
    public List<SyncDepartmentDto> Departments { get; set; } = new();
    public List<SyncEmployeeDepartmentDto> Links { get; set; } = new();
}

public class SyncDepartmentDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SyncEmployeeDepartmentDto
{
    public int EmployeeId { get; set; }                  // id VPS
    public string DepartmentCode { get; set; } = "";
    public bool IsResponsible { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>Esito di PUT /api/sync/departments.</summary>
public class SyncCountsDto
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Links { get; set; }
}

/// <summary>PUT /api/sync/projects — commesse (per id VPS, altrimenti per codice).</summary>
public class SyncProjectDto
{
    public int? Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "ACTIVE";
}

// ── Lato ATEC PM: impostazioni e stato del motore ─────────────

/// <summary>
/// Impostazioni del motore di sincronizzazione (pannello admin), lette/scritte in
/// res_settings con chiavi sync.*. La password è write-only: si scrive per cambiarla,
/// non torna MAI indietro (esce solo HasPassword).
/// </summary>
public class RisorseSyncSettingsDto
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public bool HasPassword { get; set; }
    public string? LastRun { get; set; }
    public string? LastEsito { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Stato del motore per il pannello: configurazione, hub, ultimo giro, registro.</summary>
public class RisorseSyncStatusDto
{
    public bool Enabled { get; set; }
    public bool Configured { get; set; }
    public bool HubConnected { get; set; }
    public bool InCorso { get; set; }
    public DateTime? LastRun { get; set; }
    public string? LastEsito { get; set; }
    public string? LastError { get; set; }
    public List<RisorseSyncLogEntry> UltimiGiri { get; set; } = new();
}

/// <summary>Una riga di res_sync_log: un giro del motore.</summary>
public class RisorseSyncLogEntry
{
    public DateTime RunUtc { get; set; }
    public string Innesco { get; set; } = "";            // hub | pm | timer | manuale | impostazioni
    public string Esito { get; set; } = "";              // ok | errore
    public int DurataMs { get; set; }
    public string? Dettaglio { get; set; }
}

/// <summary>
/// GET sync/salute — la salute del collegamento col VPS per l'avviso nel planner (#147; chiave
/// nav.risorse, cioè chiunque veda il planner): <c>VpsNonRisponde</c> è vero quando la
/// sincronizzazione è attiva ma non c'è un giro riuscito da oltre la soglia (10 minuti).
/// </summary>
public class RisorseSyncSaluteDto
{
    public bool Attiva { get; set; }
    public bool VpsNonRisponde { get; set; }
    /// <summary>Ultimo giro riuscito (UTC, con la Z); null se dall'avvio non ce n'è stato uno.</summary>
    public DateTime? UltimoGiroOkUtc { get; set; }
    /// <summary>Da quanti minuti manca un giro riuscito (0 quando tutto va bene).</summary>
    public int MinutiSenzaRisposta { get; set; }
    /// <summary>L'ultimo errore leggibile del motore, se c'è.</summary>
    public string? Errore { get; set; }
}
