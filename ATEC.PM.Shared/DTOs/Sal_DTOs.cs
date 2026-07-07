using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

// DTO per rappresentare un singolo step/riga di pagamento SAL.
public class SalRowDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Step { get; set; } = "";
    public decimal? Perc { get; set; }
    public string Condizione { get; set; } = "";
    public DateTime? DataFatt { get; set; }
    public string Stato { get; set; } = "";
    public int SortOrder { get; set; }
    public int RowVersion { get; set; }
}

// DTO per rappresentare la testata SAL di una commessa (cliente e valore).
public class SalHeaderDto
{
    public int ProjectId { get; set; }
    public string Cliente { get; set; } = "";
    public decimal? Valore { get; set; }
    public int RowVersion { get; set; }
}

// Bundle completo che raccoglie header e righe SAL di una singola commessa.
public class SalBundleDto
{
    public SalHeaderDto Header { get; set; } = new();
    public List<SalRowDto> Rows { get; set; } = new();
}

// Richiesta di salvataggio dell'header SAL.
public class SalHeaderSaveRequest
{
    public string Cliente { get; set; } = "";
    public decimal? Valore { get; set; }
    public int? RowVersion { get; set; }
}

// Richiesta di creazione/modifica di uno step/riga SAL.
public class SalRowSaveRequest
{
    public string Step { get; set; } = "";
    public decimal? Perc { get; set; }
    public string Condizione { get; set; } = "";
    public DateTime? DataFatt { get; set; }
    public string Stato { get; set; } = "";
    public int? RowVersion { get; set; }
}

// Richiesta di riordino per gli step SAL.
public class SalReorderRequest
{
    public List<int> Ids { get; set; } = new();
}

// DTO per le condizioni di pagamento.
public class SalConditionDto
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

// Richiesta di salvataggio per le condizioni di pagamento.
public class SalConditionSaveRequest
{
    public string Label { get; set; } = "";
}

// DTO per il prospetto globale delle scadenze SAL aperte (Fase 3).
public class SalProspettoRowDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Step { get; set; } = "";
    public decimal? Perc { get; set; }
    public string Condizione { get; set; } = "";
    public DateTime? DataFatt { get; set; }
    public decimal? Importo { get; set; }
    public int Ord { get; set; }
    public string Alert { get; set; } = "";
}

// Riepilogo per-commessa dei SAL, per la sidebar PM globale (mirror di MilestoneSummaryDto).
public class SalSummaryDto
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Total { get; set; }   // righe SAL totali (conteggio del contenitore in sidebar)
    public int Open { get; set; }     // righe con data_fatt e stato='' (ipotesi aperte)
    public int Warn { get; set; }     // aperte e scadute (data_fatt <= oggi)
    public int Pre { get; set; }      // aperte e imminenti (da lunedì sett. precedente)
}

