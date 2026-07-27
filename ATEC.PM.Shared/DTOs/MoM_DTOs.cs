namespace ATEC.PM.Shared.DTOs;

// ═══════════════════════════════════════════════════════════════
// MoM — Minutes of Meeting (verbali di riunione)
// Verbale → action item (attività con responsabili, scadenze, stato).
// Agganciato alle anagrafiche esistenti: projects (commesse) + employees (responsabili).
// ═══════════════════════════════════════════════════════════════

// Riga della lista verbali (master)
public class MoMListDto
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "RIUNIONE";   // COMMESSA | RIUNIONE
    public int? ProjectId { get; set; }
    public string? ProjectCode { get; set; }
    public string? ProjectTitle { get; set; }
    public string Title { get; set; } = "";
    public DateTime? MeetingDate { get; set; }
    public bool InDashboard { get; set; }
    public int Rev { get; set; }                     // revisione corrente del verbale
    public int ItemsCount { get; set; }
    public int OpenCount { get; set; }               // azioni non chiuse
    public int P1Count { get; set; }                 // ripartizione priorità (Alta)
    public int P2Count { get; set; }                 // Media
    public int P3Count { get; set; }                 // Bassa
    public DateTime? PeriodStart { get; set; }        // min tra data_check/data_close delle azioni
    public DateTime? PeriodEnd { get; set; }          // max tra data_check/data_close delle azioni
}

// Verbale completo con le sue action item
public class MoMDetailDto
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "RIUNIONE";
    public int? ProjectId { get; set; }
    public string? ProjectCode { get; set; }
    public string? ProjectTitle { get; set; }
    public string Title { get; set; } = "";
    public DateTime? MeetingDate { get; set; }
    public bool InDashboard { get; set; }
    public int Rev { get; set; }
    public List<MoMActionItemDto> Items { get; set; } = new();
    public List<MoMRevisionDto> Revisions { get; set; } = new();
}

// Riga dello storico revisioni (cambio data riunione confermato)
public class MoMRevisionDto
{
    public int Rev { get; set; }
    public DateTime? MeetingDate { get; set; }       // nuova data registrata
    public DateTime? PrevDate { get; set; }          // data precedente
    public DateTime CreatedAt { get; set; }
}

// Singola action item (riga della tabella verbale)
public class MoMActionItemDto
{
    public int Id { get; set; }
    public int MomId { get; set; }
    public string Attivita { get; set; } = "";
    public string? Descrizione { get; set; }
    public string? Azione { get; set; }
    public int Priorita { get; set; } = 2;           // 1=Alta, 2=Media, 3=Bassa
    public string Status { get; set; } = "OPEN";     // OPEN | STANDBY | CLOSED
    public bool IsCritical { get; set; }
    public int? Resp1Id { get; set; }
    public string? Resp1Name { get; set; }
    public int? Resp2Id { get; set; }
    public string? Resp2Name { get; set; }
    public int? Resp3Id { get; set; }
    public string? Resp3Name { get; set; }
    public DateTime? DataCheck { get; set; }
    public DateTime? DataClose { get; set; }
    public int SortOrder { get; set; }               // ordine manuale (drag&drop foglio)
    public int RowVersion { get; set; }              // token concorrenza ottimistica

    public List<string> ResponsibleNames { get; set; } = new();
    public List<int> ResponsibleIds { get; set; } = new();

    // Responsabili concatenati (tutti, ordine di selezione)
    public string RespNames =>
        ResponsibleNames.Count > 0
            ? string.Join(", ", ResponsibleNames.Where(s => !string.IsNullOrWhiteSpace(s)))
            : string.Join(", ", new[] { Resp1Name, Resp2Name, Resp3Name }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

// ── Request di scrittura ───────────────────────────────────────

public class MoMSaveRequest
{
    public string Tipo { get; set; } = "RIUNIONE";
    public int? ProjectId { get; set; }
    public string Title { get; set; } = "";
    public DateTime? MeetingDate { get; set; }
    public bool InDashboard { get; set; } = true;
}

// Usata sia per create che per update (overwrite completo della riga)
public class MoMActionItemSaveRequest
{
    public string Attivita { get; set; } = "";
    public string? Descrizione { get; set; }
    public string? Azione { get; set; }
    public int Priorita { get; set; } = 2;
    public string Status { get; set; } = "OPEN";
    public bool IsCritical { get; set; }
    public int? Resp1Id { get; set; }
    public int? Resp2Id { get; set; }
    public int? Resp3Id { get; set; }
    public List<int> ResponsibleIds { get; set; } = new();
    public DateTime? DataCheck { get; set; }
    public DateTime? DataClose { get; set; }

    // Concorrenza ottimistica: se valorizzato, l'update fallisce quando la riga
    // è stata modificata da un altro utente (i client legacy lo lasciano null).
    public int? RowVersion { get; set; }
}

// ── Gestione v9: riordino / note rapide ────────────────────────

// Ordine manuale delle righe del foglio: elenco completo degli id nell'ordine voluto.
public class MoMReorderRequest
{
    public List<int> ItemIds { get; set; } = new();
}

// Nota di acquisizione rapida (staging personale, vista «Note MoM»)
public class MoMNoteDto
{
    public int Id { get; set; }
    public string Note { get; set; } = "";
    public int? TargetMomId { get; set; }
}

public class MoMNoteSaveRequest
{
    public string Note { get; set; } = "";
    public int? TargetMomId { get; set; }
}

// ── Lookup per le combo (commesse + dipendenti) ────────────────

public class MoMProjectLookupDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";

    public string Display => string.IsNullOrWhiteSpace(Title) ? Code : $"{Code} — {Title}";
}
