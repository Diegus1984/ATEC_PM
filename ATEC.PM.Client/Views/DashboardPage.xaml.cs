using System.Text.Json;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        try
        {
            var json = await ApiClient.GetAsync("/api/dashboard");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var d = doc.RootElement.GetProperty("data");
                txtActiveProjects.Text = d.GetProperty("activeProjects").GetInt32().ToString();
                txtDraftProjects.Text = d.GetProperty("draftProjects").GetInt32().ToString();
                txtEmployees.Text = d.GetProperty("totalEmployees").GetInt32().ToString();
                txtCustomers.Text = d.GetProperty("totalCustomers").GetInt32().ToString();
                txtHoursWeek.Text = d.GetProperty("hoursThisWeek").GetDecimal().ToString("N1");
                txtHoursMonth.Text = d.GetProperty("hoursThisMonth").GetDecimal().ToString("N1");
                txtRevenue.Text = d.GetProperty("totalRevenue").GetDecimal().ToString("N0") + " €";

                if (d.TryGetProperty("recentProjects", out var rp))
                {
                    var projects = JsonSerializer.Deserialize<List<DashboardProjectRow>>(rp.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                    dgRecent.ItemsSource = projects;
                }
            }
        }
        catch { }
    }
}
