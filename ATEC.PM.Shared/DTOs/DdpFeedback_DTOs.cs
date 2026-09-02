using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

// Feedback Acquisti (sezione PM, aggregato su tutte le commesse): una riga per stato
// dell'aggregazione A6 (VER, CHEK, DO, RO, PAR di default, configurabile da "Aggregazioni DDP")
// per ogni DDP. Conteggio righe calcolato al volo; nota e "nascosto" persistiti per DDP+stato.
public class DdpFeedbackAcquistiGroup
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    /// <summary>Descrizione della commessa (#146): intestazione «codice · descrizione — cliente».</summary>
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";   // "COMMERCIAL" | "OFFICINA"
    public List<DdpFeedbackAcquistiRow> Rows { get; set; } = new();
}

public class DdpFeedbackAcquistiRow
{
    public string StatusKey { get; set; } = "";
    public int Count { get; set; }
    public string Note { get; set; } = "";
    public bool Hidden { get; set; }
}

public class DdpFeedbackAcquistiSaveRequest
{
    public string? Note { get; set; }
    public bool? Hidden { get; set; }
}

// Feedback Magazzino (sezione PM, aggregato su tutte le commesse): righe reali della distinta
// negli stati dell'aggregazione A7 (CON, COS, DISP, PAR, MOD di default), con flag "nascosto"
// persistito per singola riga (nessuna nota testuale, a differenza di Feedback Acquisti).
public class DdpFeedbackMagazzinoGroup
{
    public int ProjectId { get; set; }
    public string Code { get; set; } = "";
    /// <summary>Descrizione della commessa (#146): intestazione «codice · descrizione — cliente».</summary>
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";
    public List<DdpFeedbackMagazzinoRow> Rows { get; set; } = new();
}

public class DdpFeedbackMagazzinoRow
{
    public int ItemId { get; set; }
    public string RequestedBy { get; set; } = "";
    public int? CreatedById { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime? CreatedAt { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";           // COMMERCIAL
    public string Material { get; set; } = "";       // OFFICINA
    public string Treatment { get; set; } = "";      // OFFICINA
    public string SupplierName { get; set; } = "";
    public string Manufacturer { get; set; } = "";   // COMMERCIAL
    public string ItemStatus { get; set; } = "";
    public string DaneaRef { get; set; } = "";
    public string Destination { get; set; } = "";
    public string DestinationSpec { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Hidden { get; set; }
}
