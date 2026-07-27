using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

public class RfqDto
{
    public string Supplier { get; set; } = "";
    public string Date { get; set; } = "";
    public bool Ok { get; set; }
}

public class WorkRequestDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public string RequestDate { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = ""; // 'Internal' (Interna) o 'External' (Esterna)
    public int? Priority { get; set; } // Livello di priorità: 0, 1, 2 o null
    public string AvailabilityDate { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsUltraCritical { get; set; }
    public bool IsDelivered { get; set; }
    public long? DeliveredAt { get; set; }
    public bool IsStaging { get; set; }
    
    public List<RfqDto> Rfqs { get; set; } = new();
    
    // Dettagli dell'Ordine di Acquisto (PO/ODA)
    public string PoSupplier { get; set; } = "";
    public string PoNumber { get; set; } = "";
    public string PoDate { get; set; } = "";
    
    // Trattamenti termici o superficiali
    public bool HasTreatment { get; set; }
    public string TreatmentDate { get; set; } = "";
    public string TreatmentNotes { get; set; } = "";
    public bool IsTreatmentConfirmed { get; set; }
    public long? TreatmentConfirmedAt { get; set; }

    // Concurrency token: incrementato a ogni scrittura, confrontato dalla PUT
    public int RowVersion { get; set; }

    public long CreatedAt { get; set; }

    // Riga della DDP Officina che ha generato la lavorazione (null = inserita a mano).
    // I campi derivati (descrizione, data disponibilità, trattamento) seguono la riga DDP.
    public int? DdpOfficinaItemId { get; set; }
}

public class WorkRequestSaveRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string RequestDate { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public int? Priority { get; set; }
    public string AvailabilityDate { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsUltraCritical { get; set; }
    public bool IsDelivered { get; set; }
    public long? DeliveredAt { get; set; }
    public bool IsStaging { get; set; }
    
    public List<RfqDto> Rfqs { get; set; } = new();
    
    public string PoSupplier { get; set; } = "";
    public string PoNumber { get; set; } = "";
    public string PoDate { get; set; } = "";
    
    public bool HasTreatment { get; set; }
    public string TreatmentDate { get; set; } = "";
    public string TreatmentNotes { get; set; } = "";
    public bool IsTreatmentConfirmed { get; set; }
    public long? TreatmentConfirmedAt { get; set; }

    // Concurrency token (opzionale): se valorizzato, la PUT rifiuta con CONFLITTO
    // quando la riga è stata modificata da un altro utente nel frattempo
    public int? RowVersion { get; set; }
}
