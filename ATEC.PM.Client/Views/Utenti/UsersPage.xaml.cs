using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.ViewModels;

namespace ATEC.PM.Client.Views;

public partial class UsersPage : Page
{
    private List<UserRow> _allUsers = new();

    public UsersPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadUsers();
    }

    private async Task LoadUsers()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync("/api/users");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _allUsers = JsonSerializer.Deserialize<List<UserRow>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                ApplyFilter();
                txtStatus.Text = $"{_allUsers.Count} utenti";
            }
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        string filter = txtSearch.Text.Trim().ToLower();
        List<UserRow> filtered = string.IsNullOrEmpty(filter)
            ? _allUsers
            : _allUsers.Where(u =>
                u.FullName.ToLower().Contains(filter) ||
                u.Username.ToLower().Contains(filter) ||
                u.UserRole.ToLower().Contains(filter) ||
                u.DepartmentCodesDisplay.ToLower().Contains(filter)).ToList();
        dgUsers.ItemsSource = filtered;
    }

    private void DgUsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = dgUsers.SelectedItem != null;
        btnEdit.IsEnabled   = hasSelection;
        btnDelete.IsEnabled = hasSelection;
    }

    private void DgUsers_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (dgUsers.SelectedItem is UserRow) OpenEdit();
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        EmployeeDialog dlg = new(0) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            _ = LoadUsers();
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e) => OpenEdit();

    private void OpenEdit()
    {
        if (dgUsers.SelectedItem is not UserRow row) return;
        EmployeeDialog dlg = new(row.Id) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            _ = LoadUsers();
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgUsers.SelectedItem is not UserRow row) return;

        if (row.Id == App.UserId)
        {
            MessageBox.Show("Non puoi eliminare il tuo stesso account.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Eliminare {row.FullName}?\nL'utente verrà disattivato.", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            await ApiClient.DeleteAsync($"/api/employees/{row.Id}");
            await LoadUsers();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }
}
