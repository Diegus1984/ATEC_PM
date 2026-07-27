using System;

namespace ATEC.PM.Shared.DTOs;

// Riga della DDP Officina (particolari meccanici, tabella dedicata ddp_officina_items).
// I nomi proprietà ricalcano BomItemListItem così le viste condivise (es. Sintesi DDP)
// possono deserializzare le righe officina nello stesso shape senza adattatori.
public class OfficinaItemListItem : System.ComponentModel.INotifyPropertyChanged
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string PartNumber { get; set; } = "";   // Codice 101 (Codex), denormalizzato
    public string Description { get; set; } = "";
    public int RowNumber { get; set; }

    private decimal _quantity;
    public decimal Quantity
    {
        get => _quantity;
        set { _quantity = value; OnPropertyChanged(nameof(Quantity)); OnPropertyChanged(nameof(TotalCost)); }
    }

    /// <summary>Pezzi già prodotti / costruiti (0 … Quantity).</summary>
    public decimal QuantityProduced { get; set; }

    private decimal _unitCost;
    public decimal UnitCost
    {
        get => _unitCost;
        set { _unitCost = value; OnPropertyChanged(nameof(UnitCost)); OnPropertyChanged(nameof(TotalCost)); }
    }

    public decimal TotalCost => Quantity * UnitCost;

    public string Material { get; set; } = "";     // Materiale (es. ALLUMINIO, C40, INOX...)
    public string Treatment { get; set; } = "";    // Trattamento (es. ANODIZZATO, BRUNITO...)
    public int? SupplierId { get; set; }
    public string SupplierName { get; set; } = ""; // testo libero (officina esterna), non FK

    private string _itemStatus = "DO";
    public string ItemStatus
    {
        get => _itemStatus;
        set { _itemStatus = value; OnPropertyChanged(nameof(ItemStatus)); }
    }

    public string RequestedBy { get; set; } = "";
    public string DaneaRef { get; set; } = "";
    public DateTime? DateNeeded { get; set; }
    /// <summary>Data in cui la riga è passata a In Ordine (IO); auto-compilata al primo IO.</summary>
    public DateTime? OrderDate { get; set; }
    public string Destination { get; set; } = "";
    public string DestinationSpec { get; set; } = "";
    public string Notes { get; set; } = "";

    // «Comanda il padre»: se la riga è stata importata dalla composizione Codex, id della
    // riga padre in distinta + quantità unitaria di composizione (per 1 padre). Al cambio
    // Qtà del padre il server riallinea i figli con delta = CompositionQty × ΔQtà.
    public int? ParentOfficinaItemId { get; set; }
    public decimal? CompositionQty { get; set; }

    public DateTime? CreatedAt { get; set; }

    // Concorrenza ottimistica: versione vista al caricamento (rispedita nel PUT come ExpectedUpdatedAt).
    public DateTime? UpdatedAt { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class OfficinaItemSaveRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    /// <summary>Pezzi già prodotti / costruiti (0 … Quantity).</summary>
    public decimal QuantityProduced { get; set; }
    public decimal UnitCost { get; set; }
    public string Material { get; set; } = "";
    public string Treatment { get; set; } = "";
    public int? SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string ItemStatus { get; set; } = "DO";   // DA ORDINARE (default officina; la commerciale parte da VER)
    public string RequestedBy { get; set; } = "";
    public string DaneaRef { get; set; } = "";
    public DateTime? DateNeeded { get; set; }
    public DateTime? OrderDate { get; set; }
    public string Destination { get; set; } = "";
    public string DestinationSpec { get; set; } = "";
    public string Notes { get; set; } = "";

    // Concorrenza ottimistica: se valorizzata e diversa da quella sul server → 409 (modificata da altri). Null = nessun controllo.
    public DateTime? ExpectedUpdatedAt { get; set; }
    public bool? UpdateCodexPrice { get; set; }
}

/// <summary>Import in distinta officina della composizione Codex di un codice padre (figli diretti).</summary>
public class OfficinaImportCompositionRequest
{
    public int CodexParentId { get; set; }
    public string RequestedBy { get; set; } = "";
}

    public class OfficinaImportCompositionResult
{
    public int Added { get; set; }      // nuove righe inserite
    public int Updated { get; set; }    // righe esistenti con quantità sommata
    public int Skipped { get; set; }    // figli non importabili (articoli da Catalogo)
    public decimal ParentQuantity { get; set; } = 1;  // moltiplicatore applicato (Qtà del padre in distinta)
}

/// <summary>
/// Riga inbox Officina (cross-commessa) per il Responsabile: stessa shape della distinta
/// + contesto commessa e collegamento lavorazione (se codice 101).
/// </summary>
public class OfficinaInboxItem : OfficinaItemListItem
{
    public string ProjectCode { get; set; } = "";
    public string ProjectTitle { get; set; } = "";
    public string CustomerName { get; set; } = "";
    /// <summary>Lavorazione collegata (project_work_requests), se esiste.</summary>
    public int? WorkRequestId { get; set; }
    /// <summary>Priorità WR (0=P0 … 2=P2); null se senza lavorazione o senza priorità.</summary>
    public int? WrPriority { get; set; }
    /// <summary>Flag ultra-critico della lavorazione collegata.</summary>
    public bool IsUltraCritical { get; set; }
    /// <summary>Giorni di ritardo (negativo = in anticipo); null se senza date_needed.</summary>
    public int? DaysLate { get; set; }
}
