namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Una riga della pagina «Ore Commessa» (segnalazione #39): una imputazione di ore del
/// timesheet, vista dal PM con tutto quello che serve per decidere se è lavoro di commessa
/// o «Extra Lavoro».
/// </summary>
public class ProjectHourRowDto
{
    public int EntryId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTime WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string EntryType { get; set; } = "";
    public string Notes { get; set; } = "";

    public int ProjectPhaseId { get; set; }
    public string PhaseName { get; set; } = "";
    public string CostSectionName { get; set; } = "";

    /// <summary>IN_SEDE / DA_CLIENTE: è il tag che decide anche la trasferta (#37/#52).</summary>
    public string CostSectionType { get; set; } = "";

    /// <summary>Costo orario del reparto principale della persona, congelato dalla vista.</summary>
    public decimal HourlyCost { get; set; }

    /// <summary>Costo della riga: ore × costo orario.</summary>
    public decimal Cost => Hours * HourlyCost;

    /// <summary>Il PM ha spostato questa riga sulla causale «Extra Lavoro».</summary>
    public bool IsExtra { get; set; }

    /// <summary>
    /// La riga pesa ancora sui costi della commessa. Le righe normali sono sempre true;
    /// quelle spostate su Extra Lavoro nascono false e il PM può rimetterle dentro.
    /// </summary>
    public bool CountsInProject { get; set; }

    public DateTime? MovedAt { get; set; }
    public string MovedByName { get; set; } = "";
}

/// <summary>Righe da spostare su «Extra Lavoro» (o da riportare in commessa).</summary>
public class ExtraWorkMoveRequest
{
    public List<int> EntryIds { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>
/// Card della pagina «Ore Commessa» (segnalazione #109): una commessa su cui i colleghi
/// hanno scaricato ore, con quanto è arrivato e se il PM l'ha già guardato.
/// Stessa forma della card della Trasferta, che è il modello chiesto da Paolo.
/// </summary>
public class ProjectHoursSummaryDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PmName { get; set; } = "";
    public string Status { get; set; } = "";

    /// <summary>Ore scaricate in tutto, Extra Lavoro compreso: è «quanto è arrivato».</summary>
    public decimal TotalHours { get; set; }

    /// <summary>Costo di quelle ore al costo orario del reparto principale di ciascuno.</summary>
    public decimal TotalCost { get; set; }

    /// <summary>Persone distinte che hanno scaricato su questa commessa.</summary>
    public int PeopleCount { get; set; }

    /// <summary>Primo giorno scaricato.</summary>
    public DateTime? FirstWorkDate { get; set; }

    /// <summary>Ultimo giorno scaricato.</summary>
    public DateTime? LastWorkDate { get; set; }

    // ── Scarico da verificare (#109) ─────────────────────────────

    public int PendingPeople { get; set; }
    public decimal PendingHours { get; set; }
    public DateTime? PendingFrom { get; set; }
    public DateTime? PendingTo { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string VerifiedByName { get; set; } = "";

    /// <summary>La card va in rosso: ore che nessuno ha ancora guardato.</summary>
    public bool NeedsVerification => PendingPeople > 0;
}
