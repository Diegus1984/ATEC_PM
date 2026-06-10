using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.ViewModels;
using ATEC.PM.Shared.DTOs;

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
            string url = chkShowTerminated?.IsChecked == true
                ? "/api/users?includeTerminated=true"
                : "/api/users";
            _allUsers = await ApiClient.GetListAsync<UserRow>(url);
            ApplyFilter();
            txtStatus.Text = $"{_allUsers.Count} utenti";
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore: {ex.Message}";
        }
    }

    private async void ChkShowTerminated_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadUsers();
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

        if (ShadcnMessageBox.Show(
                $"Eliminare {row.FullName}?\nL'utente verrà cessato (reversibile: «Mostra cessati» → «Riattiva»).",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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

    private async void MenuItemResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not UserRow row)
            return;

        if (ShadcnMessageBox.Show(
                $"Reimpostare le credenziali di {row.FullName}?\n\n" +
                "Username e password torneranno alla forma iniziale (iniziale.cognome) " +
                "e al prossimo accesso l'utente dovrà scegliere una nuova password.",
                "Reset password", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        string body = JsonSerializer.Serialize(new ResetPasswordRequest { EmployeeId = row.Id });
        string json = await ApiClient.PostAsync("/api/users/reset-password", body);
        if (ApiClient.TryGetApiData(json, out string? initialLogin, out string msg))
        {
            ShadcnMessageBox.Show(
                $"Credenziali reimpostate.\nUsername e password: {initialLogin}",
                "Reset password", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadUsers();
        }
        else
        {
            ShadcnMessageBox.Show(string.IsNullOrEmpty(msg) ? "Reset non riuscito." : msg,
                "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MenuItemReactivate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not UserRow row)
            return;

        if (string.Equals(row.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            ShadcnMessageBox.Show($"{row.FullName} è già attivo.", "Riattiva", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (ShadcnMessageBox.Show(
                $"Riattivare {row.FullName}?\nTornerà attivo e potrà accedere di nuovo.",
                "Riattiva", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        string body = JsonSerializer.Serialize(new SaveUserStatusRequest
        {
            EmployeeId = row.Id,
            IsActive = true
        });
        string json = await ApiClient.PutAsync("/api/users/status", body);
        if (ApiClient.IsApiSuccess(json, out string msg))
            await LoadUsers();
        else
            ShadcnMessageBox.Show(string.IsNullOrEmpty(msg) ? "Riattivazione non riuscita." : msg,
                "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }
}
