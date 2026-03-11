using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class TimesheetPage : Page
{
    private DateTime _weekStart;

    public TimesheetPage()
    {
        InitializeComponent();
        SetWeek(DateTime.Today);
        Loaded += async (_, _) => await Load();
    }

    private void SetWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        _weekStart = date.AddDays(-diff).Date;
        var weekEnd = _weekStart.AddDays(6);
        txtWeekLabel.Text = $"{_weekStart:dd/MM/yyyy} - {weekEnd:dd/MM/yyyy}";
    }

    private async Task Load()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            var json = await ApiClient.GetAsync($"/api/timesheet/week?employeeId={App.UserId}&weekStart={_weekStart:yyyy-MM-dd}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var entries = JsonSerializer.Deserialize<List<TimesheetEntryDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                dgEntries.ItemsSource = entries;
                var total = entries.Sum(e => e.Hours);
                txtStatus.Text = $"{entries.Count} registrazioni | Totale: {total:N1} ore";
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void BtnPrevWeek_Click(object sender, RoutedEventArgs e) { SetWeek(_weekStart.AddDays(-7)); _ = Load(); }
    private void BtnNextWeek_Click(object sender, RoutedEventArgs e) { SetWeek(_weekStart.AddDays(7)); _ = Load(); }
    private void BtnToday_Click(object sender, RoutedEventArgs e) { SetWeek(DateTime.Today); _ = Load(); }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new TimesheetEntryDialog(_weekStart) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is TimesheetEntryDto entry)
        {
            var dlg = new TimesheetEntryDialog(_weekStart, entry) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) _ = Load();
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not TimesheetEntryDto entry) return;

        if (MessageBox.Show($"Eliminare la registrazione di {entry.Hours:N1}h del {entry.WorkDate:dd/MM/yyyy}?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var result = await ApiClient.DeleteAsync($"/api/timesheet/{entry.Id}");
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                _ = Load();
            else
                MessageBox.Show("Errore durante l'eliminazione.");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }
}
