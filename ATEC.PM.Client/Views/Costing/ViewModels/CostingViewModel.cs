using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ATEC.PM.Client.Views.Costing.ViewModels;

public class CostingViewModel : INotifyPropertyChanged
{
    private bool _isInitialized;
    public bool IsInitialized { get => _isInitialized; set { _isInitialized = value; Notify(); Notify(nameof(ShowInit)); Notify(nameof(ShowContent)); } }

    public bool ShowInit => !IsInitialized;
    public bool ShowContent => IsInitialized;

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; Notify(); } }

    public ObservableCollection<CostGroupVM> Groups { get; set; } = new();

    // Totali generali
    private decimal _grandTotalCost;
    public decimal GrandTotalCost { get => _grandTotalCost; private set { _grandTotalCost = value; Notify(); } }

    private decimal _grandTotalSale;
    public decimal GrandTotalSale { get => _grandTotalSale; private set { _grandTotalSale = value; Notify(); } }

    public void RecalcGrandTotals()
    {
        GrandTotalCost = Groups.Sum(g => g.TotalCost);
        GrandTotalSale = Groups.Sum(g => g.TotalSale);
        StatusText = $"{Groups.Sum(g => g.Sections.Count)} sezioni — Netto {GrandTotalCost:N2} € — Vendita {GrandTotalSale:N2} €";
    }

    /// <summary>
    /// Connette tutti i PropertyChanged a cascata: risorsa → sezione → gruppo → totali
    /// </summary>
    public void WireAllChanges()
    {
        foreach (var group in Groups)
        {
            foreach (var sec in group.Sections)
            {
                sec.WireResourceChanges();
                sec.RecalcTotals();
            }
            group.WireSectionChanges();
            group.RecalcTotals();

            group.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(CostGroupVM.TotalCost) or nameof(CostGroupVM.TotalSale))
                    RecalcGrandTotals();
            };
        }
        RecalcGrandTotals();
    }

    /// <summary>
    /// Popola i VM dai DTO ricevuti dal server
    /// </summary>
    public static CostingViewModel FromData(ProjectCostingData data)
    {
        var vm = new CostingViewModel { IsInitialized = data.IsInitialized };
        if (!data.IsInitialized) return vm;

        var colorMap = new Dictionary<string, string>
        {
            { "GESTIONE", "#2563EB" }, { "PRESCHIERAMENTO", "#7C3AED" },
            { "INSTALLAZIONE", "#D97706" }, { "OPZIONE", "#DC2626" }
        };

        var groups = data.CostSections
            .Where(s => s.IsEnabled)
            .GroupBy(s => s.GroupName)
            .OrderBy(g => data.CostSections.Where(s => s.GroupName == g.Key).Min(s => s.SortOrder));

        foreach (var grp in groups)
        {
            var groupVM = new CostGroupVM
            {
                Name = grp.Key,
                Color = colorMap.TryGetValue(grp.Key, out string? c) ? c : "#6B7280"
            };

            foreach (var sec in grp.OrderBy(s => s.SortOrder))
            {
                var secVM = new CostSectionVM
                {
                    Id = sec.Id,
                    TemplateId = sec.TemplateId,
                    Name = sec.Name,
                    SectionType = sec.SectionType,
                    MarkupValue = sec.MarkupValue,
                    DepartmentIds = sec.DepartmentIds
                };

                foreach (var res in sec.Resources.OrderBy(r => r.SortOrder))
                {
                    secVM.Resources.Add(new CostResourceVM
                    {
                        Id = res.Id,
                        SectionId = res.SectionId,
                        EmployeeId = res.EmployeeId,
                        ResourceName = res.ResourceName,
                        WorkDays = res.WorkDays,
                        HoursPerDay = res.HoursPerDay,
                        HourlyCost = res.HourlyCost,
                        NumTrips = res.NumTrips,
                        KmPerTrip = res.KmPerTrip,
                        CostPerKm = res.CostPerKm,
                        DailyFood = res.DailyFood,
                        DailyHotel = res.DailyHotel,
                        AllowanceDays = res.AllowanceDays,
                        DailyAllowance = res.DailyAllowance
                    });
                }

                groupVM.Sections.Add(secVM);
            }

            vm.Groups.Add(groupVM);
        }

        vm.WireAllChanges();
        return vm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
