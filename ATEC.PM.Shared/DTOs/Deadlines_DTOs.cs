using System;

namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Rappresenta una scadenza generica proveniente da uno dei vari moduli del sistema
/// (SAL, SAL_INCASSO, PROJECT, CHECKLIST, MOM, DDP).
/// </summary>
public class DeadlineDto
{
    public string Type { get; set; } = "";        // SAL|SAL_INCASSO|PROJECT|CHECKLIST|MOM|DDP
    public string RefType { get; set; } = "";     // reference_type per navigazione: SAL_ROW (usato da SAL e SAL_INCASSO)|PROJECT|CHECKLIST|MOM_ACTION|BOM
    public int RefId { get; set; }                // ID della riga sorgente
    public int? ProjectId { get; set; }           // ID commessa (null per checklist generiche)
    public string Code { get; set; } = "";        // Codice commessa (o "")
    public string Title { get; set; } = "";       // Titolo commessa / contesto
    public string Description { get; set; } = "";  // Step SAL / attività / part_number+descr / ...
    public DateTime DueDate { get; set; }
    public int Days { get; set; }                 // DATEDIFF(DueDate, CURDATE())
}
