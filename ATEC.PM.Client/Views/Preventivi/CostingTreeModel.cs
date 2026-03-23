using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;

namespace ATEC.PM.Client.Views.Preventivi;

// ── Converters ──
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

/// <summary>Returns empty string for non-RESOURCE rows on resource-only columns.</summary>
public class ResourceOnlyValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return "";
        var nodeType = values[1] as string;
        if (nodeType != "RESOURCE") return "";
        return values[0]?.ToString() ?? "";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Unified row model for SfTreeGrid costing view.
/// Represents Groups, Sections, and Resources in a flat self-referential structure.
/// </summary>
public class CostingTreeRow : INotifyPropertyChanged
{
    // ── Tree structure ──
    public int NodeId { get; set; }
    public int ParentNodeId { get; set; }

    // Nested collection for SfTreeGrid ChildPropertyName
    public ObservableCollection<CostingTreeRow> Children { get; set; } = new();

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
        set { _hourlyCost = value; OnPropertyChanged(); RecalcCosts(); }
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
        set { _numTrips = value; OnPropertyChanged(); OnPropertyChanged(nameof(TravelTotal)); }
    }

    private decimal _kmPerTrip;
    public decimal KmPerTrip
    {
        get => _kmPerTrip;
        set { _kmPerTrip = value; OnPropertyChanged(); OnPropertyChanged(nameof(TravelTotal)); }
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
/// Material row for the materials SfTreeGrid (flat, no hierarchy needed).
/// </summary>
public class MaterialTreeRow : INotifyPropertyChanged
{
    public int DbId { get; set; }
    public int SectionId { get; set; }

    private string _description = "";
    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    private string _itemType = "MATERIAL";
    public string ItemType
    {
        get => _itemType;
        set { _itemType = value; OnPropertyChanged(); }
    }

    private decimal _quantity = 1;
    public decimal Quantity
    {
        get => _quantity;
        set { _quantity = value; OnPropertyChanged(); RecalcTotals(); }
    }

    private decimal _unitCost;
    public decimal UnitCost
    {
        get => _unitCost;
        set { _unitCost = value; OnPropertyChanged(); RecalcTotals(); }
    }

    private decimal _markupValue = 1.300m;
    public decimal MarkupValue
    {
        get => _markupValue;
        set { _markupValue = value; OnPropertyChanged(); RecalcTotals(); }
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
        TotalCost = Quantity * UnitCost;
        TotalSale = TotalCost * MarkupValue;
        IsDirty = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
