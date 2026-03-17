using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ATEC.PM.Client.Views.Costing.ViewModels;

public class CostSectionVM : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int? TemplateId { get; set; }

    private string _name = "";
    public string Name { get => _name; set { _name = value; Notify(); } }

    private string _sectionType = "IN_SEDE";
    public string SectionType { get => _sectionType; set { _sectionType = value; Notify(); Notify(nameof(IsDaCliente)); Notify(nameof(TypeLabel)); Notify(nameof(TypeColor)); } }

    public bool IsDaCliente => SectionType == "DA_CLIENTE";
    public string TypeLabel => IsDaCliente ? "CLIENTE" : "SEDE";
    public string TypeColor => IsDaCliente ? "#D97706" : "#059669";

    private bool _isDetailExpanded;
    public bool IsDetailExpanded
    {
        get => _isDetailExpanded;
        set { _isDetailExpanded = value; Notify(); }
    }

    // Distribuzione prezzi (solo offerta)
    private decimal _contingencyPct;
    public decimal ContingencyPct { get => _contingencyPct; set { _contingencyPct = value; Notify(); } }

    private decimal _marginPct;
    public decimal MarginPct { get => _marginPct; set { _marginPct = value; Notify(); } }

    private bool _isContingencyPinned;
    public bool IsContingencyPinned { get => _isContingencyPinned; set { _isContingencyPinned = value; Notify(); } }

    private bool _isMarginPinned;
    public bool IsMarginPinned { get => _isMarginPinned; set { _isMarginPinned = value; Notify(); } }

    public ObservableCollection<CostResourceVM> Resources { get; set; } = new();
    public List<int> DepartmentIds { get; set; } = new();

    // Totali calcolati
    private decimal _totalHours;
    public decimal TotalHours { get => _totalHours; private set { _totalHours = value; Notify(); } }

    private decimal _totalCostOre;
    public decimal TotalCostOre { get => _totalCostOre; private set { _totalCostOre = value; Notify(); } }

    private decimal _totalViaggi;
    public decimal TotalViaggi { get => _totalViaggi; private set { _totalViaggi = value; Notify(); } }

    private decimal _totalAlloggio;
    public decimal TotalAlloggio { get => _totalAlloggio; private set { _totalAlloggio = value; Notify(); } }

    private decimal _totalIndennita;
    public decimal TotalIndennita { get => _totalIndennita; private set { _totalIndennita = value; Notify(); } }

    // TotalCost = solo ore
    private decimal _totalCost;
    public decimal TotalCost { get => _totalCost; private set { _totalCost = value; Notify(); } }

    // TotalSale = somma dei singoli TotalSale per riga (ogni risorsa ha il suo K)
    private decimal _totalSale;
    public decimal TotalSale { get => _totalSale; private set { _totalSale = value; Notify(); } }

    // Trasferte (per CostingViewModel)
    public decimal TotalTravelExpenses => TotalViaggi + TotalAlloggio;
    public decimal TotalAllowanceExpenses => TotalIndennita;

    private int _resourceCount;
    public int ResourceCount { get => _resourceCount; private set { _resourceCount = value; Notify(); } }

    public void RecalcTotals()
    {
        TotalHours = Resources.Sum(r => r.TotalHours);
        TotalCostOre = Resources.Sum(r => r.TotalCost);
        TotalViaggi = Resources.Sum(r => r.TravelTotal);
        TotalAlloggio = Resources.Sum(r => r.AccommodationTotal);
        TotalIndennita = Resources.Sum(r => r.AllowanceTotal);
        TotalCost = TotalCostOre;
        TotalSale = Resources.Sum(r => r.TotalSale);
        ResourceCount = Resources.Count;
        Notify(nameof(TotalTravelExpenses));
        Notify(nameof(TotalAllowanceExpenses));
    }

    public void WireResourceChanges()
    {
        foreach (var r in Resources)
            r.PropertyChanged += (s, e) => RecalcTotals();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}