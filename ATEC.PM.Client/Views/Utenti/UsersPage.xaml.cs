using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Controls;
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
            _allUsers = await ApiClient.GetListAsync<UserRow>("/api/users");
            ApplyFilter();
            txtStatus.Text = $"{_allUsers.Count} utenti";
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

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        EmployeeDialog dlg = new(0) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            _ = LoadUsers();
    }

    private void OpenEdit()
    {
        if (dgUsers.SelectedItem is not UserRow row) return;
        EmployeeDialog dlg = new(row.Id) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            _ = LoadUsers();
    }

    private async Task ExecuteDelete(UserRow row)
    {
        if (row.Id == App.UserId)
        {
            ShadcnMessageBox.Show("Non puoi eliminare il tuo stesso account.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ShadcnMessageBox.Show($"Eliminare {row.FullName}?\nL'utente verrà disattivato.", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            await ApiClient.DeleteAsync($"/api/employees/{row.Id}");
            await LoadUsers();
        }
        catch (Exception ex)
        {
            ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnRowOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            if (btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }
    }

    private void MenuItemEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            var rowItem = menuItem.DataContext as UserRow;
            if (rowItem != null)
            {
                dgUsers.SelectedItem = rowItem;
                OpenEdit();
            }
        }
    }

    private async void MenuItemDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            var rowItem = menuItem.DataContext as UserRow;
            if (rowItem != null)
            {
                dgUsers.SelectedItem = rowItem;
                await ExecuteDelete(rowItem);
            }
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }
}
