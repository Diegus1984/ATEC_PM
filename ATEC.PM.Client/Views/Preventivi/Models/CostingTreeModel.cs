using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ATEC.PM.Client.Views.Preventivi.Models;

// ── Converters (only ones not already in AppConverters.cs) ──

public class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NodeTypeToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && (s == "GROUP" || s == "SECTION") ? FontWeights.SemiBold : FontWeights.Normal;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Unified row model for costing view.
/// Represents Groups, Sections, and Resources in a nested structure.
/// </summary>
public class CostingTreeRow : INotifyPropertyChanged
{
    // ── Tree structure ──
    public int NodeId { get; set; }
    public int ParentNodeId { get; set; }

    // Nested collection for children
    public ObservableCollection<CostingTreeRow> Children { get; set; } = new();

    // ── Expand/Collapse state ──
    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    // ── Aggregate properties for GROUP/SECTION rows ──
    private decimal _sumWorkDays;
    public decimal SumWorkDays
    {
        get => _sumWorkDays;
        set { _sumWorkDays = value; OnPropertyChanged(); }
    }

    private decimal _sumTotalHours;
    public decimal SumTotalHours
    {
        get => _sumTotalHours;
        set { _sumTotalHours = value; OnPropertyChanged(); }
    }

    /// <summary>GROUP, SECTION, RESOURCE</summary>
    public string NodeType { get; set; } = "";

    // ── DB reference ──
    public int DbId { get; set; }
    public int? EmployeeId { get; set; }
    public int? SectionDbId { get; set; }

    // ── Display ──
    private string _displayName = "";
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    private string _groupColor = "#3B82F6";
    public string GroupColor
    {
        get => _groupColor;
        set { _groupColor = value; OnPropertyChanged(); }
    }

    // ── Section fields ──
    private string _sectionType = "";
    public string SectionType
    {
        get => _sectionType;
        set { _sectionType = value; OnPropertyChanged(); OnPropertyChanged(nameof(TypeLabel)); }
    }
    public string TypeLabel => SectionType == "DA_CLIENTE" ? "CLIENTE" : SectionType == "IN_SEDE" ? "SEDE" : "";

    private int _resourceCount;
    public int ResourceCount
    {
        get => _resourceCount;
        set { _resourceCount = value; OnPropertyChanged(); }
    }

    // ── Resource fields (editable) ──
    private string _departmentCode = "";
    public string DepartmentCode
    {
        get => _departmentCode;
        set { _departmentCode = value; OnPropertyChanged(); }
    }

    private decimal _workDays;
    public decimal WorkDays
    {
        get => _workDays;
        set
        {
            if (_workDays == value) return;
            _workDays = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalHours));
            RecalcCosts();
        }
    }

    private decimal _hoursPerDay;
    public decimal HoursPerDay
    {
        get => _hoursPerDay;
        set
        {
            if (_hoursPerDay == value) return;
            _hoursPerDay = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalHours));
            RecalcCosts();
        }
    }

    public decimal TotalHours => WorkDays * HoursPerDay;

    private decimal _hourlyCost;
    public decimal HourlyCost
    {
        get => _hourlyCost;
        set { if (_hourlyCost == value) return; _hourlyCost = value; OnPropertyChanged(); RecalcCosts(); }
    }

    private decimal _markupValue;
    public decimal MarkupValue
    {
        get => _markupValue;
        set
        {
            if (_markupValue == value) return;
            _markupValue = value;
            OnPropertyChanged();
            RecalcCosts();
        }
    }

    // ── Travel fields (DA_CLIENTE resources) ──
    private int _numTrips;
    public int NumTrips
    {
        get => _numTrips;
        set { if (_numTrips == value) return; _numTrips = value; OnPropertyChanged(); OnPropertyChanged(nameof(TravelTotal)); }
    }

    private decimal _kmPerTrip;
    public decimal KmPerTrip
    {
        get => _kmPerTrip;
        set { if (_kmPerTrip == value) return; _kmPerTrip = value; OnPropertyChanged(); OnPropertyChanged(nameof(TravelTotal)); }
    }

    private decimal _costPerKm = 0.90m;
    public decimal CostPerKm
    {
        get => _costPerKm;
        set { _costPerKm = value; OnPropertyChanged(); OnPropertyChanged(nameof(TravelTotal)); }
    }

    public decimal TravelTotal => NumTrips * KmPerTrip * CostPerKm;

    private decimal _dailyFood;
    public decimal DailyFood
    {
        get => _dailyFood;
        set { _dailyFood = value; OnPropertyChanged(); OnPropertyChanged(nameof(AccommodationTotal)); }
    }

    private decimal _dailyHotel;
    public decimal DailyHotel
    {
        get => _dailyHotel;
        set { _dailyHotel = value; OnPropertyChanged(); OnPropertyChanged(nameof(AccommodationTotal)); }
    }

    public decimal AccommodationTotal => WorkDays * (DailyFood + DailyHotel);

    private decimal _allowanceDays;
    public decimal AllowanceDays
    {
        get => _allowanceDays;
        set { _allowanceDays = value; OnPropertyChanged(); OnPropertyChanged(nameof(AllowanceTotal)); }
    }

    private decimal _dailyAllowance;
    public decimal DailyAllowance
    {
        get => _dailyAllowance;
        set { _dailyAllowance = value; OnPropertyChanged(); OnPropertyChanged(nameof(AllowanceTotal)); }
    }

    public decimal AllowanceTotal => AllowanceDays * DailyAllowance;

    // ── Totals (used at all levels) ──
    private decimal _totalCost;
    public decimal TotalCost
    {
        get => _totalCost;
        set { _totalCost = value; OnPropertyChanged(); }
    }

    private decimal _totalSale;
    public decimal TotalSale
    {
        get => _totalSale;
        set { _totalSale = value; OnPropertyChanged(); }
    }

    // ── Dirty flag ──
    public bool IsDirty { get; set; }

    // ── Helpers ──
    public bool IsGroup => NodeType == "GROUP";
    public bool IsSection => NodeType == "SECTION";
    public bool IsResource => NodeType == "RESOURCE";
    public bool IsDaCliente => SectionType == "DA_CLIENTE";

    private void RecalcCosts()
    {
        if (NodeType != "RESOURCE") return;
        TotalCost = TotalHours * HourlyCost;
        TotalSale = TotalCost * MarkupValue;
        IsDirty = true;
    }

    // ── INotifyPropertyChanged ──
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Material row for the materials DataGrid (flat, no hierarchy needed).
/// </summary>
public class MaterialTreeRow : INotifyPropertyChanged
{
    public int DbId { get; set; }
    public int SectionId { get; set; }
    public int? ParentItemId { get; set; }
    public int? ProductId { get; set; }
    public int? VariantId { get; set; }
    public string Code { get; set; } = "";
    public string? DescriptionRtf { get; set; }

    private string _description = "";
    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); IsDirty = true; }
    }

    private string _itemType = "MATERIAL";
    public string ItemType
    {
        get => _itemType;
        set { _itemType = value; OnPropertyChanged(); }
    }

    private bool _isActive = true;
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive == value) return; _isActive = value; OnPropertyChanged(); RecalcTotals(); }
    }

    private decimal _quantity = 1;
    public decimal Quantity
    {
        get => _quantity;
        set { if (_quantity == value) return; _quantity = value; OnPropertyChanged(); RecalcTotals(); }
    }

    private decimal _unitCost;
    public decimal UnitCost
    {
        get => _unitCost;
        set { if (_unitCost == value) return; _unitCost = value; OnPropertyChanged(); RecalcTotals(); }
    }

    private decimal _markupValue = 1.300m;
    public decimal MarkupValue
    {
        get => _markupValue;
        set { if (_markupValue == value) return; _markupValue = value; OnPropertyChanged(); RecalcTotals(); }
    }

    private decimal _totalCost;
    public decimal TotalCost
    {
        get => _totalCost;
        set { _totalCost = value; OnPropertyChanged(); }
    }

    private decimal _totalSale;
    public decimal TotalSale
    {
        get => _totalSale;
        set { _totalSale = value; OnPropertyChanged(); }
    }

    public bool IsDirty { get; set; }

    private void RecalcTotals()
    {
        TotalCost = IsActive ? Quantity * UnitCost : 0;
        TotalSale = IsActive ? Quantity * UnitCost * MarkupValue : 0;
        IsDirty = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Product group for the materials section (parent + variants hierarchy).
/// </summary>
public class MaterialProductGroup : INotifyPropertyChanged
{
    public int ParentId { get; set; }
    public int SectionId { get; set; }
    public int? ProductId { get; set; }
    public string ParentName { get; set; } = "";
    public string? DescriptionRtf { get; set; }
    public ObservableCollection<MaterialTreeRow> Variants { get; set; } = new();

    /// <summary>Ha un ProductId collegato al catalogo?</summary>
    public bool HasCatalogLink => ProductId.HasValue && ProductId.Value > 0;

    public decimal TotalCost => Variants.Where(v => v.IsActive).Sum(v => v.TotalCost);
    public decimal TotalSale => Variants.Where(v => v.IsActive).Sum(v => v.TotalSale);
    public string TotalDisplay => $"{TotalSale:N2} \u20ac";

    public void NotifyTotals()
    {
        OnPropertyChanged(nameof(TotalCost));
        OnPropertyChanged(nameof(TotalSale));
        OnPropertyChanged(nameof(TotalDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Pricing ViewModel for Scheda Prezzi + Distribuzione Prezzo.
/// Mirrors the CostingViewModel pricing logic from ProjectCostingControl.
/// </summary>
public class PricingVM : INotifyPropertyChanged
{
    // ── Totali alimentati dal control ──
    private decimal _totalResourceSale;
    public decimal TotalResourceSale
    {
        get => _totalResourceSale;
        set { _totalResourceSale = value; OnPropertyChanged(); NotifyAllPricing(); }
    }

    private decimal _totalMaterialSale;
    public decimal TotalMaterialSale
    {
        get => _totalMaterialSale;
        set { _totalMaterialSale = value; OnPropertyChanged(); NotifyAllPricing(); }
    }

    private decimal _totalTravelSale;
    public decimal TotalTravelSale
    {
        get => _totalTravelSale;
        set { _totalTravelSale = value; OnPropertyChanged(); NotifyAllPricing(); }
    }

    // ── Scheda Prezzi ──
    private decimal _contingencyPct = 0.130m;
    public decimal ContingencyPct
    {
        get => _contingencyPct;
        set { _contingencyPct = value; OnPropertyChanged(); NotifyAllPricing(); }
    }

    private decimal _negotiationMarginPct = 0.050m;
    public decimal NegotiationMarginPct
    {
        get => _negotiationMarginPct;
        set { _negotiationMarginPct = value; OnPropertyChanged(); NotifyAllPricing(); }
    }

    public decimal NetPrice => TotalResourceSale + TotalMaterialSale + TotalTravelSale;
    public decimal ContingencyAmount => NetPrice * ContingencyPct;
    public decimal OfferPrice => NetPrice + ContingencyAmount;
    public decimal NegotiationMarginAmount => OfferPrice * NegotiationMarginPct;
    public decimal FinalOfferPrice => OfferPrice + NegotiationMarginAmount;

    // ── Distribuzione pesi macro-categoria ──
    public decimal ResourceDistributed => NetPrice > 0 ? FinalOfferPrice * (TotalResourceSale / NetPrice) : 0;
    public decimal MaterialDistributed => NetPrice > 0 ? FinalOfferPrice * (TotalMaterialSale / NetPrice) : 0;
    public decimal TravelDistributed => NetPrice > 0 ? FinalOfferPrice * (TotalTravelSale / NetPrice) : 0;

    // ── Distribuzione per sezione ──
    public ObservableCollection<DistributionRowVM> DistributionRows { get; set; } = new();

    public decimal TotalCombinedSale => NetPrice;

    public decimal TotalDistContingencyCheck => DistributionRows.Sum(r => r.ContingencyAmount);
    public decimal TotalDistMarginCheck => DistributionRows.Sum(r => r.MarginAmount);

    private void NotifyAllPricing()
    {
        OnPropertyChanged(nameof(NetPrice));
        OnPropertyChanged(nameof(ContingencyAmount));
        OnPropertyChanged(nameof(OfferPrice));
        OnPropertyChanged(nameof(NegotiationMarginAmount));
        OnPropertyChanged(nameof(FinalOfferPrice));
        OnPropertyChanged(nameof(ResourceDistributed));
        OnPropertyChanged(nameof(MaterialDistributed));
        OnPropertyChanged(nameof(TravelDistributed));
        OnPropertyChanged(nameof(TotalCombinedSale));
        OnPropertyChanged(nameof(TotalDistContingencyCheck));
        OnPropertyChanged(nameof(TotalDistMarginCheck));
    }

    public void NotifyDistributionTotals()
    {
        OnPropertyChanged(nameof(TotalDistContingencyCheck));
        OnPropertyChanged(nameof(TotalDistMarginCheck));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Row in the distribution table (mirrors DistributionRowVM from CostingViewModel).
/// </summary>
public class DistributionRowVM : INotifyPropertyChanged
{
    /// "R" = sezione risorse, "M" = item materiale
    public string RowType { get; set; } = "R";
    public int SectionId { get; set; }
    public int ItemId { get; set; }
    public string TypeBadge => RowType == "M" ? "M" : "R";
    public string TypeBadgeColor => RowType == "M" ? "#7C3AED" : "#2563EB";

    private string _sectionName = "";
    public string SectionName { get => _sectionName; set { _sectionName = value; OnPropertyChanged(); } }

    private decimal _saleAmount;
    public decimal SaleAmount { get => _saleAmount; set { _saleAmount = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplaySaleAmount)); } }

    private decimal _contingencyPct;
    public decimal ContingencyPct { get => _contingencyPct; set { _contingencyPct = value; OnPropertyChanged(); } }

    private decimal _contingencyAmount;
    public decimal ContingencyAmount { get => _contingencyAmount; set { _contingencyAmount = value; OnPropertyChanged(); } }

    private bool _isContingencyPinned;
    public bool IsContingencyPinned { get => _isContingencyPinned; set { _isContingencyPinned = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContingencyPinIcon)); } }
    public string ContingencyPinIcon => IsContingencyPinned ? "\U0001F512" : "";

    private decimal _marginPct;
    public decimal MarginPct { get => _marginPct; set { _marginPct = value; OnPropertyChanged(); } }

    private decimal _marginAmount;
    public decimal MarginAmount { get => _marginAmount; set { _marginAmount = value; OnPropertyChanged(); } }

    private bool _isMarginPinned;
    public bool IsMarginPinned { get => _isMarginPinned; set { _isMarginPinned = value; OnPropertyChanged(); OnPropertyChanged(nameof(MarginPinIcon)); } }
    public string MarginPinIcon => IsMarginPinned ? "\U0001F512" : "";

    private decimal _sectionTotal;
    public decimal SectionTotal { get => _sectionTotal; set { _sectionTotal = value; OnPropertyChanged(); } }

    // ── Shadow ──
    private bool _isShadowed;
    public bool IsShadowed
    {
        get => _isShadowed;
        set { _isShadowed = value; OnPropertyChanged(); OnPropertyChanged(nameof(EyeIcon)); OnPropertyChanged(nameof(DisplaySaleAmount)); OnPropertyChanged(nameof(RowBackground)); OnPropertyChanged(nameof(RowOpacity)); }
    }
    public string EyeIcon => IsShadowed ? "\U0001F441\u200D\U0001F5E8" : "\U0001F441";
    public string RowBackground => IsShadowed ? "#FEF2F2" : "Transparent";
    public double RowOpacity => IsShadowed ? 0.5 : 1.0;
    public decimal DisplaySaleAmount => IsShadowed ? 0m : SaleAmount;

    private decimal _shadowedAmount;
    public decimal ShadowedAmount { get => _shadowedAmount; set { _shadowedAmount = value; OnPropertyChanged(); } }

    private decimal _shadowedPct;
    public decimal ShadowedPct { get => _shadowedPct; set { _shadowedPct = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
