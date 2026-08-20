namespace ATEC.PM.Shared.DTOs;

// Segnalazioni (bug e richieste di miglioramento) sul gestionale stesso.
// Tutti vedono tutte le segnalazioni (così non si duplicano), ma ognuno modifica solo le
// proprie; lo stato lo cambia solo un ADMIN. Ogni segnalazione può avere allegati
// (screenshot o file) caricati da chi la apre.

/// <summary>Allegato di una segnalazione (screenshot o file).</summary>
public class BugAttachmentDto
{
    public int Id { get; set; }
    public int BugId { get; set; }
    public string FileName { get; set; } = "";      // nome originale, mostrato all'utente
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsImage { get; set; }               // true = si può mostrare in anteprima
    public bool IsReply { get; set; }               // true = allegato alla risposta
    /// <summary>
    /// Chi ha caricato il file (NULL se quell'utenza non esiste più). Serve al client per
    /// mostrare il cestino solo a chi può davvero togliere l'allegato: il file lo rimuove chi
    /// l'ha messo o chi gestisce le segnalazioni, non chiunque possa modificare la segnalazione
    /// — altrimenti il segnalatore cancellerebbe anche le foto allegate alla risposta.
    /// </summary>
    public int? CreatedById { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class BugReportDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = "BUG";        // BUG · IMPROVEMENT
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Area { get; set; } = "";           // pagina/modulo dove succede
    public string Severity { get; set; } = "MEDIUM"; // LOW · MEDIUM · HIGH
    public string Status { get; set; } = "OPEN";     // OPEN · IN_PROGRESS · RESOLVED · REJECTED
    public string AdminNote { get; set; } = "";      // risposta di chi gestisce
    public string? Context { get; set; }             // metadati tecnici automatici (rotta, build, browser, err)
    public string? FixedInBuild { get; set; }        // build in cui è stato risolto
    public DateTime? ArchivedAt { get; set; }        // data archiviazione
    public int? CreatedById { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int RowVersion { get; set; }              // concorrenza ottimistica
    /// <summary>True se la segnalazione è di chi sta guardando: il client abilita le azioni.</summary>
    public bool IsMine { get; set; }
    public List<BugAttachmentDto> Attachments { get; set; } = new();
}

/// <summary>Creazione/modifica del contenuto della segnalazione (autore o ADMIN).</summary>
public class BugReportSaveRequest
{
    public string Kind { get; set; } = "BUG";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Area { get; set; } = "";
    public string Severity { get; set; } = "MEDIUM";
    public string? Context { get; set; }
    public int? RowVersion { get; set; }
}

/// <summary>Cambio di stato + nota: riservato agli ADMIN.</summary>
public class BugReportStatusRequest
{
    public string Status { get; set; } = "OPEN";
    public string AdminNote { get; set; } = "";
    public int? RowVersion { get; set; }
}

/// <summary>Contatori per le viste rapide della pagina.</summary>
public class BugReportCountsDto
{
    public int All { get; set; }
    public int Mine { get; set; }
    public int Open { get; set; }
    public int InProgress { get; set; }
    public int Resolved { get; set; }
    public int Rejected { get; set; }
    public int Archived { get; set; }
}
