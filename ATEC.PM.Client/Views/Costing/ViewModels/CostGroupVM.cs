using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ATEC.PM.Client.Views.Costing.ViewModels;

public class CostGroupVM : INotifyPropertyChanged
{
    private string _name = "";
    public string Name { get => _name; set { _name = value; Notify(); } }

    private string _color = "#6B7280";
    public string Color { get => _color; set { _color = value; Notify(); } }

    private bool _isExpanded = true;
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; Notify(); } }

    public ObservableCollection<CostSectionVM> Sections { get; set; } = new();

    private decimal _totalCost;
    public decimal TotalCost { get => _totalCost; private set { _totalCost = value; Notify(); } }

    private decimal _totalSale;
    public decimal TotalSale { get => _totalSale; private set { _totalSale = value; Notify(); } }

    public void RecalcTotals()
    {
        TotalCost = Sections.Sum(s => s.TotalCost);
        TotalSale = Sections.Sum(s => s.TotalSale);
    }

    /// <summary>
    /// Sottoscrive PropertyChanged di ogni sezione per ricalcolo automatico
    /// </summary>
    public void WireSectionChanges()
    {
        foreach (var sec in Sections)
        {
            sec.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(CostSectionVM.TotalCost) or nameof(CostSectionVM.TotalSale))
                    RecalcTotals();
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
