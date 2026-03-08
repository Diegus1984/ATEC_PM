using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ATEC.PM.Client.Views.Costing.ViewModels;

public class MaterialSectionVM : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int? CategoryId { get; set; }

    private string _name = "";
    public string Name { get => _name; set { _name = value; Notify(); } }

    /// <summary>K default per nuove righe MATERIAL</summary>
    public decimal DefaultMarkup { get; set; } = 1.300m;

    /// <summary>K default per nuove righe COMMISSION</summary>
    public decimal DefaultCommissionMarkup { get; set; } = 1.100m;

    private bool _isDetailExpanded;
    public bool IsDetailExpanded { get => _isDetailExpanded; set { _isDetailExpanded = value; Notify(); } }

    public ObservableCollection<MaterialItemVM> Items { get; set; } = new();

    // Totali calcolati
    private decimal _totalCost;
    public decimal TotalCost { get => _totalCost; private set { _totalCost = value; Notify(); } }

    private decimal _totalSale;
    public decimal TotalSale { get => _totalSale; private set { _totalSale = value; Notify(); } }

    private int _itemCount;
    public int ItemCount { get => _itemCount; private set { _itemCount = value; Notify(); } }

    public void RecalcTotals()
    {
        TotalCost = Items.Sum(i => i.TotalCost);
        TotalSale = Items.Sum(i => i.TotalSale);
        ItemCount = Items.Count;
    }

    public void WireItemChanges()
    {
        foreach (var item in Items)
            item.PropertyChanged += (s, e) => RecalcTotals();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
