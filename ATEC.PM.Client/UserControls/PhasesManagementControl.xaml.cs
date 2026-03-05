using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.UserControls;

public partial class PhasesManagementControl : UserControl
{
    private int _projectId;
    private List<PhaseListItem> _allPhases = new();
    private List<PhaseTemplateDto> _templates = new();
    private string _searchFilter = "";

    private static readonly Dictionary<string, string> DeptColors = new()
    {
        { "ELE", "#2563EB" }, { "MEC", "#059669" }, { "PLC", "#7C3AED" },
        { "ROB", "#DC2626" }, { "UTC", "#D97706" }, { "ACQ", "#0891B2" },
        { "AMM", "#BE185D" }, { "",    "#6B7280" }
    };

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public PhasesManagementControl()
    {
        InitializeComponent();
    }

    public async void Load(int projectId)
    {
        _projectId = projectId;
        await LoadTemplates();
        await LoadPhases();
    }

    private async Task LoadTemplates()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/phases/templates");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                _templates = JsonSerializer.Deserialize<List<PhaseTemplateDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { }
    }

    private async Task LoadPhases()
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/phases/project/{_projectId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _allPhases = JsonSerializer.Deserialize<List<PhaseListItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            UpdateSummary();
            RenderPhases();
        }
        catch (Exception ex)
        {
            pnlPhases.Children.Clear();
            pnlPhases.Children.Add(new TextBlock { Text = $"Errore: {ex.Message}", Foreground = Brushes.Red });
        }
    }

    private void UpdateSummary()
    {
        txtTotalPhases.Text = _allPhases.Count.ToString();
        txtTotalBudget.Text = $"{_allPhases.Sum(p => p.BudgetHours):N0} h";
        txtTotalWorked.Text = $"{_allPhases.Sum(p => p.HoursWorked):N1} h";
        txtTotalCost.Text = $"{_allPhases.Sum(p => p.BudgetCost):N0} €";
    }

    private void RenderPhases()
    {
        pnlPhases.Children.Clear();

        IEnumerable<PhaseListItem> filtered = _allPhases;
        if (!string.IsNullOrWhiteSpace(_searchFilter))
            filtered = filtered.Where(p =>
                p.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                (p.DepartmentCode ?? "").Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));

        var groups = filtered
            .GroupBy(p => string.IsNullOrEmpty(p.DepartmentCode) ? "" : p.DepartmentCode)
            .OrderBy(g => g.Key == "" ? "ZZZ" : g.Key);

        foreach (var group in groups)
        {
            string deptCode = group.Key;
            string deptColor = DeptColors.TryGetValue(deptCode, out string? col) ? col : "#6B7280";
            string deptLabel = string.IsNullOrEmpty(deptCode) ? "TRASVERSALE" : deptCode;

            Border groupHeader = new()
            {
                Background = Brush(deptColor),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 12, 0, 4)
            };
            decimal grpBudget = group.Sum(p => p.BudgetHours);
            decimal grpWorked = group.Sum(p => p.HoursWorked);
            groupHeader.Child = new TextBlock
            {
                Text = $"  {deptLabel}  —  {group.Count()} fasi  |  {grpWorked:N1} / {grpBudget:N0} h",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            pnlPhases.Children.Add(groupHeader);

            foreach (PhaseListItem phase in group.OrderBy(p => p.SortOrder))
            {
                PhaseRowControl row = new();
                row.Bind(phase, deptColor);
                row.PhaseChanged += async () => await LoadPhases();
                row.SummaryChanged += () => UpdateSummary();
                pnlPhases.Children.Add(row);
            }
        }

        if (!filtered.Any())
            pnlPhases.Children.Add(new TextBlock
            {
                Text = "Nessuna fase trovata.",
                FontSize = 13,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 20, 0, 0)
            });
    }

    private async void BtnAddPhase_Click(object sender, RoutedEventArgs e)
    {
        var existingTemplateIds = _allPhases.Select(p => p.PhaseTemplateId).ToHashSet();
        var available = _templates.Where(t => !existingTemplateIds.Contains(t.Id)).ToList();

        if (!available.Any())
        {
            MessageBox.Show("Tutte le fasi sono già state inserite.", "Info");
            return;
        }

        var dlg = new AddPhasesWindow(available) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.SelectedTemplates.Any())
        {
            try
            {
                string jsonBody = JsonSerializer.Serialize(new
                {
                    projectId = _projectId,
                    templateIds = dlg.SelectedTemplates.Select(t => t.Id).ToList()
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                string result = await ApiClient.PostAsync("/api/phases/bulk", jsonBody);
                JsonDocument doc = JsonDocument.Parse(result);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                    await LoadPhases();
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        _searchFilter = txtSearch.Text.Trim();
        RenderPhases();
    }
}
