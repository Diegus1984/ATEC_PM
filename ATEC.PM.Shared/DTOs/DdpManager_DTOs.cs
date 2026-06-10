namespace ATEC.PM.Shared.DTOs;

// Riepilogo DDP Commerciali aggregato per commessa (pagina "Gestore DDP" nella sezione PM).
// KPI della card modellati sul prototipo Gestore_DDP_V4 (Tot. Acquisti / Inserimenti / Mat. Consegna / Mat. Ritardo).
public class DdpProjectSummary
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string CustomerName { get; set; } = "";

    public int TotalRows { get; set; }          // INSERIMENTI (n° righe)
    public decimal TotalValue { get; set; }     // TOT. ACQUISTI (somma qty*costo, tutte le righe)
    public int DatedCount { get; set; }          // MAT. CONSEGNA (righe con data prevista, non ancora consegnate)
    public int OverdueCount { get; set; }        // MAT. RITARDO (di quelle, con data < oggi)

    public DateTime? DeliveryStart { get; set; } // finestra consegne (min data prevista, righe non consegnate)
    public DateTime? DeliveryEnd { get; set; }   // finestra consegne (max)
    public DateTime? LastInsertedAt { get; set; }// ultima riga inserita (max created_at)
}

// Dettaglio/sintesi DDP di una commessa: KPI + ripartizione per stato (vista "Stato DDP" del prototipo).
public class DdpProjectDetail
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string CustomerName { get; set; } = "";

    public int TotalRows { get; set; }
    public decimal TotalValue { get; set; }
    public int DatedCount { get; set; }
    public int OverdueCount { get; set; }
    public DateTime? DeliveryStart { get; set; }
    public DateTime? DeliveryEnd { get; set; }

    public List<DdpStatusCount> StatusCounts { get; set; } = new();
}

// Conteggio righe per stato (causale) dentro una commessa.
public class DdpStatusCount
{
    public string StatusKey { get; set; } = "";
    public int Count { get; set; }
}
