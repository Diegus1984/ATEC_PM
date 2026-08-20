namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Una riga della pagina «Lavorazioni Officine» (#83). Le righe hanno due provenienze e la
/// pagina le mostra insieme:
/// <list type="bullet">
/// <item><b>DDP</b> — la riga vive in <c>ddp_officina_items</c> e qui si <i>guarda</i>: tutti i
/// campi seguono la distinta e non sono modificabili da questa pagina. Le uniche tre eccezioni
/// sono <see cref="RequestDate"/>, <see cref="Notes"/> e <see cref="IsUltraCritical"/>, che da
/// qui si scrivono e che nella DDP sono di sola lettura.</item>
/// <item><b>MANUAL</b> — riga battuta a mano in <c>project_work_requests</c>, non collegata a
/// nessuna distinta e con tutti i campi scrivibili. Può non avere commessa.</item>
/// </list>
/// <see cref="Source"/> dice quale delle due è: da lì dipendono l'endpoint di scrittura e il
/// token di concorrenza da rimandare indietro (<see cref="UpdatedAt"/> per le DDP,
/// <see cref="RowVersion"/> per le manuali).
/// </summary>
public class WorkshopRowDto
{
    public const string SourceDdp = "DDP";
    public const string SourceManual = "MANUAL";

    /// <summary>"DDP" oppure "MANUAL": vedi <see cref="WorkshopRowDto"/>.</summary>
    public string Source { get; set; } = SourceDdp;
    /// <summary>Id nella tabella d'origine. Unico solo a parità di <see cref="Source"/>.</summary>
    public int Id { get; set; }

    /// <summary>Null solo sulle righe manuali senza commessa.</summary>
    public int? ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public string ProjectTitle { get; set; } = "";
    public string CustomerName { get; set; } = "";

    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    /// <summary>Pezzi già prodotti (Prodotti).</summary>
    public decimal QuantityProduced { get; set; }
    public string Material { get; set; } = "";
    public string Treatment { get; set; } = "";
    public string Destination { get; set; } = "";
    public string DestinationSpec { get; set; } = "";

    /// <summary>'Internal' (Interna) o 'External' (Esterna): decide in quale vista sta la riga.</summary>
    public string WorkType { get; set; } = "";
    /// <summary>Stato DDP (DC, DO, PAR, MIT…). Vuoto sulle righe manuali, che non ne hanno uno.</summary>
    public string ItemStatus { get; set; } = "";

    /// <summary>«Data Richiesta» (<c>date_needed</c>): si scrive QUI e si riporta sulla DDP.</summary>
    public string RequestDate { get; set; } = "";
    /// <summary>Giorni di ritardo sulla data richiesta; null se la data non c'è.</summary>
    public int? DaysLate { get; set; }

    // ── Solo lavorazioni esterne ─────────────────────────────────────────────
    public string SupplierName { get; set; } = "";
    /// <summary>Rif. Danea della riga DDP.</summary>
    public string DaneaRef { get; set; } = "";
    public string OrderDate { get; set; } = "";
    /// <summary>«Consegnato il»: la data su cui le esterne si ordinano e si filtrano.</summary>
    public string DeliveredAt { get; set; } = "";

    /// <summary>Note di gestione lavorazione. NON sono le note della DDP.</summary>
    public string Notes { get; set; } = "";
    /// <summary>Segnala ultra critica → la riga compare anche in Urgenze (solo interne).</summary>
    public bool IsUltraCritical { get; set; }

    /// <summary>Concorrenza delle righe DDP (<c>updated_at</c> della riga officina).</summary>
    public System.DateTime? UpdatedAt { get; set; }
    /// <summary>Concorrenza delle righe manuali.</summary>
    public int RowVersion { get; set; }
}

/// <summary>
/// Scrittura di uno dei tre campi che «Lavorazioni Officine» possiede su una riga DDP.
/// Gli altri campi si cambiano nella distinta, che ne resta l'unica padrona.
/// </summary>
public class WorkshopFieldUpdateRequest
{
    /// <summary>request_date | notes | is_ultra_critical.</summary>
    public string Field { get; set; } = "";
    public string? Value { get; set; }
    /// <summary>Se valorizzato, la scrittura fallisce con 409 quando la riga è cambiata.</summary>
    public System.DateTime? ExpectedUpdatedAt { get; set; }
}
