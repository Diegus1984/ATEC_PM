using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class EmployeesPage : Page
{
    private ObservableCollection<EmployeeListItem> _employees = new();
    private List<EmployeeListItem> _allEmployees = new();

    public EmployeesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadEmployees();
    }

    private async Task LoadEmployees()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            var json = await ApiClient.GetAsync("/api/employees");
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetProperty("success").GetBoolean())
            {
                var data = root.GetProperty("data");
                _allEmployees = JsonSerializer.Deserialize<List<EmployeeListItem>>(data.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                ApplyFilter();
                txtStatus.Text = $"{_allEmployees.Count} dipendenti";
            }
            else
            {
                txtStatus.Text = "Errore: " + root.GetProperty("message").GetString();
            }
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var filter = txtSearch.Text.Trim().ToLower();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allEmployees
            : _allEmployees.Where(e =>
                (e.FullName?.ToLower().Contains(filter) ?? false) ||
                (e.Email?.ToLower().Contains(filter) ?? false) ||
                (e.BadgeNumber?.ToLower().Contains(filter) ?? false) ||
                (e.Phone?.ToLower().Contains(filter) ?? false)
            ).ToList();

        _employees = new ObservableCollection<EmployeeListItem>(filtered);
        dgEmployees.ItemsSource = _employees;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void DgEmployees_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = dgEmployees.SelectedItem != null;
        btnEdit.IsEnabled = hasSelection;
        btnDelete.IsEnabled = hasSelection;
    }

    private void DgEmployees_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        BtnEdit_Click(sender, e);
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new EmployeeDialog();
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true)
        {
            _ = LoadEmployees();
        }
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (dgEmployees.SelectedItem is EmployeeListItem emp)
        {
            var dlg = new EmployeeDialog(emp.Id);
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true)
            {
                _ = LoadEmployees();
            }
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgEmployees.SelectedItem is EmployeeListItem emp)
        {
            var result = MessageBox.Show(
                $"Eliminare {emp.FullName}?",
                "Conferma eliminazione",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await ApiClient.DeleteAsync($"/api/employees/{emp.Id}");
                    await LoadEmployees();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Errore: {ex.Message}");
                }
            }
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadEmployees();
    }
}
