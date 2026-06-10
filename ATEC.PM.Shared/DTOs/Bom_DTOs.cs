using System;

namespace ATEC.PM.Shared.DTOs;

public class BomItemListItem : System.ComponentModel.INotifyPropertyChanged
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? CatalogItemId { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public int RowNumber { get; set; }

    private decimal _quantity;
    public decimal Quantity
    {
        get => _quantity;
        set { _quantity = value; OnPropertyChanged(nameof(Quantity)); OnPropertyChanged(nameof(TotalCost)); }
    }

    private decimal _unitCost;
    public decimal UnitCost
    {
        get => _unitCost;
        set { _unitCost = value; OnPropertyChanged(nameof(UnitCost)); OnPropertyChanged(nameof(TotalCost)); }
    }

    public decimal TotalCost => Quantity * UnitCost;

    public string SupplierName { get; set; } = "";
    public string Manufacturer { get; set; } = "";

    private string _itemStatus = "DO";
    public string ItemStatus
    {
        get => _itemStatus;
        set { _itemStatus = value; OnPropertyChanged(nameof(ItemStatus)); }
    }

    public string RequestedBy { get; set; } = "";
    public string DaneaRef { get; set; } = "";
    public DateTime? DateNeeded { get; set; }
    public string Destination { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";
    public DateTime? CreatedAt { get; set; }

    // Concorrenza ottimistica: versione vista al caricamento (rispedita nel PUT come ExpectedUpdatedAt).
    public DateTime? UpdatedAt { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class BomItemSaveRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? CatalogItemId { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "PZ";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public int? SupplierId { get; set; }
    public string Manufacturer { get; set; } = "";
    public string ItemStatus { get; set; } = "DO";
    public string RequestedBy { get; set; } = "";
    public string DaneaRef { get; set; } = "";
    public DateTime? DateNeeded { get; set; }
    public string Destination { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";

    // Concorrenza ottimistica: se valorizzata e diversa da quella sul server → 409 (modificata da altri). Null = nessun controllo.
    public DateTime? ExpectedUpdatedAt { get; set; }
}

// Notifica real-time (SignalR) di modifica della distinta (DDP) di una commessa.
// Inviata dal server ai client nel gruppo "project-{ProjectId}"; il client la usa come segnale per ricaricare.
public class DdpChange
{
    public int ProjectId { get; set; }
    public string Action { get; set; } = "";   // "create" | "update" | "delete"
    public int ItemId { get; set; }
}

public static class DdpStatusMap
{
    // Fallback per le notifiche cambio-stato. Gli stati "veri" stanno in ddp_statuses (editabili);
    // qui solo per un'etichetta leggibile se la mappa DB non è a portata. ToLabel ritorna la chiave se assente.
    public static readonly Dictionary<string, string> Labels = new()
    {
        ["ANN"] = "ANNULLATO",
        ["SOSP"] = "SOSPESO",
        ["RAM"] = "RIMESSO A MAGAZZINO",
        ["SOST"] = "SOSTITUITO",
        ["CON"] = "CONSEGNATO",
        ["COS"] = "COSTRUITO",
        ["DISP"] = "DISPONIBILE",
        ["DC"] = "DA COSTRUIRE",
        ["DO"] = "DA ORDINARE",
        ["ASS"] = "ASSEGNATO AL MONTATORE",
        ["CHEK"] = "MAT. CHE NECESSITA CONTROLLO TECNICO/COMMERCIALE",
        ["IO"] = "IN ORDINE",
        ["PAR"] = "PARZIALMENTE CONSEGNATO o COSTRUITO",
        ["RO"] = "RICHIESTA OFFERTA",
        ["VER"] = "VERIFICARE SE DISPONIBILE A MAG",
        ["SPED"] = "SPEDITO AL CLIENTE O AL FORNITORE DI SERVIZI",
        ["MOD"] = "INVIATO A MODULA - MAG"
    };

    public static string ToLabel(string status) =>
        Labels.TryGetValue(status, out string? label) ? label : status;
}
