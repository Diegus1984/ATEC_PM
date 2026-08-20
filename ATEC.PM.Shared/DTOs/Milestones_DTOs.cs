namespace ATEC.PM.Shared.DTOs;

// Milestone = riga di pianificazione di una commessa (project_milestones). È una COPIA PER VALORE
// delle voci scelte dal catalogo al precarico (nessun legame residuo). W.Inizio/W.Fine/W.Tot e lo
// stato/colore NON sono qui: derivati lato client dalle date (settimana ISO + confronto con oggi).
public class MilestoneDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Descrizione { get; set; } = "";
    public DateTime? DataInizio { get; set; }
    public DateTime? DataFine { get; set; }
    public int? Avanzamento { get; set; }        // 0..100 oppure null
    public string Note { get; set; } = "";
    public bool Evidenza { get; set; }           // hl (evidenza urgenza)
    public bool Spento { get; set; }             // riga esclusa da tabella/Gantt/avanzamento medio
    public int SortOrder { get; set; }
    public int RowVersion { get; set; }          // concorrenza ottimistica
    public int? SourceCatalogId { get; set; }    // traccia INERTE della voce di catalogo di origine (no propagazione)
}

// Riepilogo per-commessa delle milestone ATTIVE (spento=0) per la sidebar PM globale.
// Gli stati replicano la logica client (milestone-utils.msStatus): done=avanzamento 100,
// late=scaduta e non completata, current=in corso oggi. Calcolo server-side con CURDATE().
public class MilestoneSummaryDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Active { get; set; }     // righe non spente = conteggio del contenitore in sidebar
    public int Late { get; set; }
    public int Current { get; set; }
    public int Done { get; set; }
    public int? AvgAvanz { get; set; }          // media avanzamento (0 le righe senza valore), arrotondata — come avgAvanz client
    public DateTime? PeriodStart { get; set; }  // min fondendo data_inizio e data_fine — come periodo() client
    public DateTime? PeriodEnd { get; set; }    // max fondendo data_inizio e data_fine
}

public class MilestoneSaveRequest
{
    public string Descrizione { get; set; } = "";
    public DateTime? DataInizio { get; set; }
    public DateTime? DataFine { get; set; }
    public int? Avanzamento { get; set; }
    public string Note { get; set; } = "";
    public bool Evidenza { get; set; }
    public bool Spento { get; set; }
    public int? RowVersion { get; set; }         // null = nessun controllo di concorrenza (client legacy)
}

// Riordino: elenco ordinato di id (la posizione diventa il nuovo sort_order 0..N).
public class MilestoneReorderRequest
{
    public List<int> Ids { get; set; } = new();
}

// Precarico alla creazione commessa: copia (snapshot) le voci di catalogo scelte in milestone.
// Lista vuota = tutte le voci attive del catalogo.
public class MilestoneSeedRequest
{
    public List<int> CatalogIds { get; set; } = new();
}

// ══════════════════════════════════════════════════════════
// VISTE SALVATE del Gantt («Vista Interna» / «Vista Cliente»)
// ══════════════════════════════════════════════════════════
// La composizione di un Gantt (colonne spente, righe spente, intervallo date, timeline
// on/off) sta LATO SERVER e non in localStorage: il Gantt ridotto che si manda al cliente
// deve essere lo stesso per tutti e non deve morire col browser di chi l'ha composto.
// `Payload` è opaco per il server — lo interpreta solo il client — così aggiungere una
// voce alla composizione non richiede una migrazione.

public class MilestoneGanttViewDto
{
    public string Name { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public string UpdatedByName { get; set; } = "";
}

public class MilestoneGanttViewSaveRequest
{
    public string Payload { get; set; } = "";
}
