using ATEC.PM.Shared;

namespace ATEC.PM.Shared.DTOs;

// Riepilogo DDP aggregato per commessa (pagina "Gestore DDP" nella sezione PM): una entry
// per (commessa, tipo distinta). KPI della card modellati sul prototipo Gestore_DDP_V4.
public class DdpProjectSummary
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";   // "COMMERCIAL" | "OFFICINA"

    public int TotalRows { get; set; }          // INSERIMENTI (n° righe)
    [DatoSensibile]
    public decimal? TotalValue { get; set; }    // TOT. ACQUISTI (somma qty*costo, tutte le righe)
    public int DatedCount { get; set; }          // MAT. CONSEGNA (righe con data prevista, non ancora consegnate)
    public int OverdueCount { get; set; }        // MAT. RITARDO (di quelle, con data < oggi)

    public DateTime? DeliveryStart { get; set; } // finestra consegne (min data prevista, righe non consegnate)
    public DateTime? DeliveryEnd { get; set; }   // finestra consegne (max)
    public DateTime? LastInsertedAt { get; set; }// ultima riga inserita (max created_at)

    /// <summary>Ripartizione righe per codice stato (torta in griglia Gestore DDP).</summary>
    public List<DdpStatusCount> StatusCounts { get; set; } = new();
}

// Dettaglio/sintesi DDP di una commessa: KPI + ripartizione per stato (vista "Stato DDP" del prototipo).
public class DdpProjectDetail
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string CustomerName { get; set; } = "";

    public int TotalRows { get; set; }
    [DatoSensibile]
    public decimal? TotalValue { get; set; }
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

// Contatori dei report di controllo cross-commessa (hub "Report di Controllo"):
// una entry per report (rit|ver|ro|do|io|dc) con i conteggi Commerciali/Officina.
public class DdpControlSummaryEntry
{
    public string Report { get; set; } = "";
    public int CommercialCount { get; set; }
    public int OfficinaCount { get; set; }
}

// Riga di un report di controllo: riga DDP completa + riferimenti commessa.
// RowNumber = posizione della riga dentro la sua distinta (per id), come nel Gestore DDP.
public class DdpControlReportRow
{
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";   // "COMMERCIAL" | "OFFICINA"

    public int Id { get; set; }
    public int RowNumber { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";                // solo commerciale
    public decimal Quantity { get; set; }
    [DatoSensibile]
    public decimal? UnitCost { get; set; }
    public string SupplierName { get; set; } = "";
    public string Manufacturer { get; set; } = "";        // solo commerciale
    public string Material { get; set; } = "";            // solo officina
    public string Treatment { get; set; } = "";           // solo officina
    public string ItemStatus { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public string DaneaRef { get; set; } = "";
    public DateTime? DateNeeded { get; set; }
    public int? CreatedById { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime? CreatedAt { get; set; }
    public string Destination { get; set; } = "";
    public string DestinationSpec { get; set; } = "";
    public string Notes { get; set; } = "";
    public int? ParentOfficinaItemId { get; set; }
    public decimal? CompositionQty { get; set; }
}

// Aggregato "Analisi Consegne": consegne previste in un giorno, separate Commerciali/Officina.
// Righe considerate: con data prevista e stato non consegnato/gestito (A2) né escluso (A9).
public class DdpDeliveriesDay
{
    public DateTime Day { get; set; }
    public int CommercialCount { get; set; }
    [DatoSensibile]
    public decimal? CommercialValue { get; set; }
    public int OfficinaCount { get; set; }
    [DatoSensibile]
    public decimal? OfficinaValue { get; set; }
}

// Voce dell'elenco «DDP aggiornate da verificare» della card Gestione Controlli (#114):
// una per (commessa, tipo distinta) toccata da un collega e non ancora aperta da chi guarda.
// Title = nome della commessa (projects.title); UpdatedBy = chi ha fatto l'ultimo intervento
// ("" quando la modifica non porta la firma: righe storiche o passaggi automatici).
public class DdpUpdatedItem
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";   // "COMMERCIAL" | "OFFICINA"
    public DateTime? UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
