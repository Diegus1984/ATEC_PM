using System;

namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Una riga dell'elenco commesse <b>della pagina Commesse</b>: ci sono dentro i soldi
/// (<see cref="Revenue"/>) e le ore a budget. Sta dietro <c>nav.commesse</c>.
/// <para>Le tendine delle altre pagine (SAL, MoM, Chat, Milestones, Lavorazioni, Dashboard)
/// NON usano questo tipo: hanno <see cref="ProjectLookupItem"/>, che di soldi non ne porta.
/// Prima erano la stessa cosa, e il valore di ogni commessa arrivava a chiunque fosse
/// autenticato dalla tendina di una pagina qualsiasi.</para>
/// </summary>
public class ProjectListItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PmName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDatePlanned { get; set; }
    public decimal Revenue { get; set; }
    public decimal BudgetHoursTotal { get; set; }
    public int LinkedQuoteId { get; set; }
}

/// <summary>
/// La commessa come la mostra una <b>tendina</b>: quel tanto che basta a riconoscerla e a
/// scegliere. Nessun importo, nessuna ora a budget, nessuna data.
///
/// <para>Serve <c>GET /api/projects/lookup</c>, aperta a tutti gli autenticati perché le
/// commesse si scelgono da mezzo gestionale (SAL, verbali, chat, milestone, lavorazioni,
/// dashboard). I campi sono esattamente quelli che quelle pagine mostrano a video: se un
/// domani ne serve un altro, si aggiunge QUI e si guarda che non sia un dato sensibile.</para>
/// </summary>
public class ProjectLookupItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PmName { get; set; } = "";
    public string Status { get; set; } = "";
}

public class ProjectTreeItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string CustomerName { get; set; } = "";
}

public class ProjectSaveRequest
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int CustomerId { get; set; }
    public int PmId { get; set; }
    public string Description { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDatePlanned { get; set; }
    public decimal BudgetTotal { get; set; }
    public decimal BudgetHoursTotal { get; set; }
    public decimal Revenue { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string Priority { get; set; } = "MEDIUM";
    public string ServerPath { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool CreateDefaultPhases { get; set; } = true;
    public int LinkedQuoteId { get; set; }
}
