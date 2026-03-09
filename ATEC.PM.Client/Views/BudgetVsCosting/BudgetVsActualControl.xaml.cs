using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.BudgetVsCosting;

public partial class BudgetVsActualControl : UserControl
{
    public BudgetVsActualControl()
    {
        InitializeComponent();
    }

    public async void Load(int projectId)
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{projectId}/budget-vs-actual");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            BudgetVsActualData data = JsonSerializer.Deserialize<BudgetVsActualData>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            DataContext = BvaViewModel.FromData(data);
            txtLoading.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            txtLoading.Text = $"Errore: {ex.Message}";
        }
    }

    private void SectionRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BvaSectionVM section)
            section.IsDetailExpanded = !section.IsDetailExpanded;
    }

    private void EmployeeRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BvaActualEmployeeVM emp)
            emp.IsExpanded = !emp.IsExpanded;
    }
}