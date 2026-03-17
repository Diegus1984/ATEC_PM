using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ATEC.PM.Client.Views.Costing.ViewModels;

public class CostResourceVM : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public int? EmployeeId { get; set; }

    private string _resourceName = "";
    public string ResourceName { get => _resourceName; set { _resourceName = value; Notify(); } }

    private decimal _workDays;
    public decimal WorkDays
    {
        get => _workDays;
        set { _workDays = value; Notify(); Notify(nameof(TotalHours)); Notify(nameof(TotalCost)); Notify(nameof(TotalSale)); Notify(nameof(AccommodationTotal)); }
    }

    private decimal _hoursPerDay;
    public decimal HoursPerDay
    {
        get => _hoursPerDay;
        set { _hoursPerDay = value; Notify(); Notify(nameof(TotalHours)); Notify(nameof(TotalCost)); Notify(nameof(TotalSale)); }
    }

    private decimal _hourlyCost;
    public decimal HourlyCost
    {
        get => _hourlyCost;
        set { _hourlyCost = value; Notify(); Notify(nameof(TotalCost)); Notify(nameof(TotalSale)); }
    }

    private decimal _markupValue = 1.450m;
    public decimal MarkupValue
    {
        get => _markupValue;
        set { _markupValue = value; Notify(); Notify(nameof(TotalSale)); }
    }
    // In CostResourceVM
    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set { _isDirty = value; OnPropertyChanged(); }
    }
    public decimal TotalHours => WorkDays * HoursPerDay;
    public decimal TotalCost => TotalHours * HourlyCost;
    public decimal TotalSale => TotalCost * MarkupValue;

    // Trasferta
    private int _numTrips;
    public int NumTrips
    {
        get => _numTrips;
        set { _numTrips = value; Notify(); Notify(nameof(TravelTotal)); }
    }

    private decimal _kmPerTrip;
    public decimal KmPerTrip
    {
        get => _kmPerTrip;
        set { _kmPerTrip = value; Notify(); Notify(nameof(TravelTotal)); }
    }

    private decimal _costPerKm = 0.90m;
    public decimal CostPerKm
    {
        get => _costPerKm;
        set { _costPerKm = value; Notify(); Notify(nameof(TravelTotal)); }
    }

    private decimal _dailyFood;
    public decimal DailyFood
    {
        get => _dailyFood;
        set { _dailyFood = value; Notify(); Notify(nameof(AccommodationTotal)); }
    }

    private decimal _dailyHotel;
    public decimal DailyHotel
    {
        get => _dailyHotel;
        set { _dailyHotel = value; Notify(); Notify(nameof(AccommodationTotal)); }
    }

    private decimal _allowanceDays;
    public decimal AllowanceDays
    {
        get => _allowanceDays;
        set { _allowanceDays = value; Notify(); Notify(nameof(AllowanceTotal)); }
    }

    private decimal _dailyAllowance;
    public decimal DailyAllowance
    {
        get => _dailyAllowance;
        set { _dailyAllowance = value; Notify(); Notify(nameof(AllowanceTotal)); }
    }

    public decimal TravelTotal => NumTrips * KmPerTrip * CostPerKm;
    public decimal AccommodationTotal => WorkDays * (DailyFood + DailyHotel);
    public decimal AllowanceTotal => AllowanceDays * DailyAllowance;

    public static decimal[] AllowanceOptions => new[] { 0m, 20m, 40m, 60m };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
