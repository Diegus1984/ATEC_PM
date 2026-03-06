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

    private string _itemStatus = "TO_ORDER";
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
    public string ItemStatus { get; set; } = "TO_ORDER";
    public string RequestedBy { get; set; } = "";
    public string DaneaRef { get; set; } = "";
    public DateTime? DateNeeded { get; set; }
    public string Destination { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";
}
