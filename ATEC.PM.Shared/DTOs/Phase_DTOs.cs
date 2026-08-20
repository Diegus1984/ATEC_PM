using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Una fase dell'anagrafica agganciata a UNA sezione di costo. Una fase ne ha N: «Call Cliente»
/// vive sotto Program Manager e sotto Progettazione insieme, e in commessa nasce una volta per
/// sezione. Ordine e «default» stanno qui, sul legame, non sulla fase: l'ordine dentro PM non è
/// quello dentro Progettazione, e la fase può nascere da sola in una sezione e non nell'altra.
/// Vedi PIANO-FASI-MULTISEZIONE.md.
/// </summary>
public class PhaseTemplateSectionDto
{
    public int SectionId { get; set; }
    public string SectionName { get; set; } = "";
    public string SectionType { get; set; } = "IN_SEDE";
    public string GroupName { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
}

public class PhaseTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>
    /// Sezione «principale» (la prima di <see cref="Sections"/>). **Compatibilità**: la usano i
    /// client scritti quando una fase stava sotto una sezione sola. Chi è nuovo legge Sections.
    /// </summary>
    public int? CostSectionTemplateId { get; set; }
    public string CostSectionName { get; set; } = "";
    public int SortOrder { get; set; }
    /// <summary>Vero se la fase nasce da sola in ALMENO una delle sue sezioni.</summary>
    public bool IsDefault { get; set; }
    public List<PhaseTemplateSectionDto> Sections { get; set; } = new();
}

/// <summary>Aggancio di una fase a una sezione (POST templates/{id}/sections).</summary>
public class PhaseSectionLinkRequest
{
    public int SectionId { get; set; }

    /// <summary>
    /// **Vero per scelta** (07/08/2026): una fase agganciata a una sezione deve nascere da sola
    /// sulle commesse nuove. Metterla in una sezione e poi doverla marcare a mano era il modo
    /// sicuro per ritrovarsi commesse vuote — è successo con tutte e 49 le assegnazioni.
    /// Resta l'interruttore sulla riga per spegnerla in una singola sezione.
    /// </summary>
    public bool IsDefault { get; set; } = true;
}

/// <summary>Modifica di un legame esistente (PATCH templates/{id}/sections/{sectionId}).</summary>
public class PhaseSectionLinkUpdate
{
    public int? SortOrder { get; set; }
    public bool? IsDefault { get; set; }
}

public class PhaseListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public string Status { get; set; } = "";
    public int ProgressPct { get; set; }
    public int SortOrder { get; set; }
    public decimal HoursWorked { get; set; }
    public List<PhaseAssignmentDto> Assignments { get; set; } = new();
    public int PhaseTemplateId { get; set; }
    public string CustomName { get; set; } = "";
    // Sezione costo di appartenenza (dalla fase template)
    public string CostSectionName { get; set; } = "";
    public int? CostSectionTemplateId { get; set; }
    public bool IsLocal { get; set; }

    /// <summary>
    /// Fase spenta (segnalazione #51): fuori dall'elenco del Bilancio e dalla tendina del
    /// Timesheet, ma le ore già imputate continuano a contare nei costi.
    /// </summary>
    public bool IsOff { get; set; }
}

public class PhaseAssignmentDto
{
    public int Id { get; set; }
    public int ProjectPhaseId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string AssignRole { get; set; } = "MEMBER";
    public decimal PlannedHours { get; set; }
    public decimal HoursWorked { get; set; }
}

public class PhaseSaveRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int PhaseTemplateId { get; set; }
    public string CustomName { get; set; } = "";
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public string Status { get; set; } = "NOT_STARTED";
    public int ProgressPct { get; set; }
    public int SortOrder { get; set; }
    public List<PhaseAssignmentDto> Assignments { get; set; } = new();
}

public class PhaseTemplateSaveRequest
{
    public string Name { get; set; } = "";
    public int? CostSectionTemplateId { get; set; }
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// Una fase da importare in commessa, con la sezione in cui va. La stessa fase può essere
/// importata più volte, una per sezione: sono righe diverse e le ore restano separate.
/// </summary>
public class BulkPhaseItem
{
    public int TemplateId { get; set; }
    public int? SectionId { get; set; }
}

public class BulkPhaseRequest
{
    public int ProjectId { get; set; }
    /// <summary>
    /// **Compatibilità**: una SPA rimasta in cache manda ancora solo gli id. In quel caso la
    /// sezione si prende da quella principale della fase.
    /// </summary>
    public List<int> TemplateIds { get; set; } = new();
    public List<BulkPhaseItem> Items { get; set; } = new();
}

public class PlannedHoursUpdate
{
    public decimal PlannedHours { get; set; }
}

public class LocalPhaseRequest
{
    public int ProjectId { get; set; }
    public int? CostSectionTemplateId { get; set; }
    public string Name { get; set; } = "";
    public int? DepartmentId { get; set; }
}
