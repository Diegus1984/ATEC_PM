using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class PhasesDialog : Window
{
    private readonly int _projectId;

    public PhasesDialog(int projectId, string projectTitle)
    {
        InitializeComponent();
        _projectId = projectId;
        Title = $"Fasi - {projectTitle}";
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        try
        {
            var json = await ApiClient.GetAsync($"/api/projects/{_projectId}/phases");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var phases = JsonSerializer.Deserialize<List<PhaseListItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                dgPhases.ItemsSource = phases;
                var totalBudget = phases.Sum(p => p.BudgetHours);
                var totalWorked = phases.Sum(p => p.HoursWorked);
                txtStatus.Text = $"{phases.Count} fasi | Ore previste: {totalBudget:N0} | Ore fatte: {totalWorked:N1}";
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
