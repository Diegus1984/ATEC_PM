using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ATEC.PM.Client.Views.Costing.ViewModels;

public class MaterialItemVM : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int SectionId { get; set; }

    private string _description = "";
    public string Description { get => _description; set { _description = value; Notify(); } }

    private decimal _quantity = 1;

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set { _isDirty = value; Notify(); }
    }
    public decimal Quantity { get => _quantity; set { _quantity = value; Notify(); Notify(nameof(TotalCost)); Notify(nameof(TotalSale)); } }

    private decimal _unitCost;
    public decimal UnitCost { get => _unitCost; set { _unitCost = value; Notify(); Notify(nameof(TotalCost)); Notify(nameof(TotalSale)); } }

    private decimal _markupValue = 1.300m;
    public decimal MarkupValue { get => _markupValue; set { _markupValue = value; Notify(); Notify(nameof(TotalSale)); } }

    private string _itemType = "MATERIAL";
    public string ItemType { get => _itemType; set { _itemType = value; Notify(); Notify(nameof(IsCommission)); } }

    public bool IsCommission => ItemType == "COMMISSION";

    public decimal TotalCost => Quantity * UnitCost;
    public decimal TotalSale => Quantity * UnitCost * MarkupValue;

    // Distribuzione prezzi (solo offerta)
    private decimal _contingencyPct;
    public decimal ContingencyPct { get => _contingencyPct; set { _contingencyPct = value; Notify(); } }

    private decimal _marginPct;
    public decimal MarginPct { get => _marginPct; set { _marginPct = value; Notify(); } }

    private bool _isContingencyPinned;
    public bool IsContingencyPinned { get => _isContingencyPinned; set { _isContingencyPinned = value; Notify(); } }

    private bool _isMarginPinned;
    public bool IsMarginPinned { get => _isMarginPinned; set { _isMarginPinned = value; Notify(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}