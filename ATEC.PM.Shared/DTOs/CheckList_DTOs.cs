namespace ATEC.PM.Shared.DTOs;

// DTO del modulo Check list / Attività: tabelle di attività (item) raggruppate per
// commessa reale (project_id) oppure per gruppo generico (DIREZIONE, VARIE, custom).
// Priorità 0–3 come nel prototipo: 0=Critica, 1=Alta, 2=Media, 3=Bassa.

/// <summary>Singola attività (riga di una tabella check list).</summary>
public class ChecklistItemDto
{
    public int Id { get; set; }
    public int? ProjectId { get; set; }        // valorizzato se l'attività appartiene a una commessa
    public int? GroupId { get; set; }           // valorizzato se appartiene a un gruppo generico
    public string Description { get; set; } = "";
    public int Priority { get; set; } = 2;      // 0=Critica · 1=Alta · 2=Media · 3=Bassa
    public DateTime? DueDate { get; set; }
    public bool IsCritical { get; set; }        // flag "ultra-critico"
    public string Status { get; set; } = "OPEN"; // OPEN · STANDBY · CLOSED (allineato a MoM)
    public DateTime? DataClose { get; set; }    // data di chiusura (valorizzata quando Status=CLOSED)
    public int SortOrder { get; set; }
    public int RowVersion { get; set; }         // concorrenza ottimistica
    public DateTime CreatedAt { get; set; }     // data/ora inserimento (sola lettura)
}

/// <summary>Gruppo generico (non commessa): DIREZIONE, VARIE, o custom — con le sue attività.</summary>
public class ChecklistGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public int RowVersion { get; set; }
    public List<ChecklistItemDto> Items { get; set; } = new();
}

/// <summary>Card commessa nella pagina aggregata: la commessa reale con le sue attività.</summary>
public class ChecklistProjectDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Cliente della commessa: riga separata nella colonna "Commessa / Gruppo".</summary>
    public string CustomerName { get; set; } = "";
    public string Display => string.IsNullOrWhiteSpace(Title) ? Code : $"{Code} — {Title}";
    public List<ChecklistItemDto> Items { get; set; } = new();
}

/// <summary>Board completo della pagina PM: tutti i gruppi generici + tutte le commesse con attività.</summary>
public class ChecklistBoardDto
{
    public List<ChecklistProjectDto> Projects { get; set; } = new();
    public List<ChecklistGroupDto> Groups { get; set; } = new();
}

public class ChecklistItemSaveRequest
{
    public int? ProjectId { get; set; }
    public int? GroupId { get; set; }
    public string Description { get; set; } = "";
    public int Priority { get; set; } = 2;
    public DateTime? DueDate { get; set; }
    public bool IsCritical { get; set; }
    public string? Status { get; set; }         // null = non modificare lo stato (client legacy)
    public int? RowVersion { get; set; }        // null = nessun controllo di concorrenza (client legacy)
}

public class ChecklistGroupSaveRequest
{
    public string Name { get; set; } = "";
    public int? RowVersion { get; set; }
}

/// <summary>Nota rapida "Fissa attività" (staging personale per dipendente).</summary>
public class ChecklistInboxDto
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public int SortOrder { get; set; }
}

public class ChecklistInboxSaveRequest
{
    public string Text { get; set; } = "";
}

/// <summary>Assegnazione di una nota a una commessa (ProjectId) oppure a un gruppo generico (GroupId).</summary>
public class ChecklistAssignRequest
{
    public int? ProjectId { get; set; }
    public int? GroupId { get; set; }
}

/// <summary>Commessa selezionabile nelle combo del client.</summary>
public class ChecklistProjectLookupDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Display => string.IsNullOrWhiteSpace(Title) ? Code : $"{Code} — {Title}";
}
