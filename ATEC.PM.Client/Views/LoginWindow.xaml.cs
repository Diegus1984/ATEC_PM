using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        txtUsername.Focus();
    }

    // ── EYE TOGGLE ──────────────────────────────────────────────

    private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        txtPasswordVisible.Text = txtPassword.Password;
    }

    private void BtnEye_Down(object sender, MouseButtonEventArgs e)
    {
        txtPasswordVisible.Text = txtPassword.Password;
        txtPasswordVisible.Visibility = Visibility.Visible;
        txtPassword.Visibility = Visibility.Collapsed;
    }

    private void BtnEye_Up(object sender, MouseButtonEventArgs e)
    {
        txtPassword.Visibility = Visibility.Visible;
        txtPasswordVisible.Visibility = Visibility.Collapsed;
        txtPassword.Focus();
    }

    // ── LOGIN ───────────────────────────────────────────────────

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";
        btnLogin.IsEnabled = false;
        btnLogin.Content = "Accesso...";

        try
        {
            App.ApiBaseUrl = txtServer.Text.TrimEnd('/');
            string result = await ApiClient.PostLogin(txtUsername.Text, txtPassword.Password);

            if (ApiClient.TryGetApiData<LoginResponse>(result, out LoginResponse? login, out string errMsg) && login != null)
            {
                App.Token = login.Token;
                App.UserFullName = login.FullName;
                App.UserRole = login.UserRole;
                App.UserId = login.EmployeeId;

                // Carica reparti e competenze per il PermissionEngine
                await LoadUserContextAsync();

                // Carica feature e livelli per il sistema permessi a livelli
                await LoadAuthFeaturesAsync();

                // Check notifiche pendenti (fire-and-forget)
                _ = ApiClient.PostAsync("/api/notifications/check-pending", "{}");

                new MainWindow().Show();
                Close();
            }
            else
                txtError.Text = errMsg;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Errore durante il login o l'apertura di MainWindow");
            txtError.Text = $"Errore: {ex.Message} (Dettaglio: {ex.InnerException?.Message ?? "nessuno"})";
        }
        finally
        {
            btnLogin.IsEnabled = true;
            btnLogin.Content = "Accedi";
        }
    }

    private static async Task LoadUserContextAsync()
    {
        try
        {
            UserDetailDto? user = await ApiClient.GetDataAsync<UserDetailDto>($"/api/users/{App.UserId}");
            if (user == null) return;

            List<string> deptCodes = new();
            List<string> respCodes = new();
            List<string> compCodes = new();

            foreach (EmployeeDepartmentDto d in user.Departments)
            {
                deptCodes.Add(d.DepartmentCode);
                if (d.IsResponsible)
                    respCodes.Add(d.DepartmentCode);
            }

            foreach (EmployeeCompetenceDto c in user.Competences)
                compCodes.Add(c.DepartmentCode);

            App.SetCurrentUser(App.UserId, App.UserRole, deptCodes, respCodes, compCodes);
        }
        catch
        {
            App.SetCurrentUser(App.UserId, App.UserRole,
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>(),
                Enumerable.Empty<string>());
        }
    }

    private static async Task LoadAuthFeaturesAsync()
    {
        try
        {
            AuthFeaturesContextDto? ctx = await ApiClient.GetDataAsync<AuthFeaturesContextDto>(
                "/api/auth-levels/features/my");
            if (ctx == null) return;

            int userLevel = ctx.UserLevel;
            List<AuthFeatureDto> features = ctx.Features;
            List<AuthLevelDto> levels = ctx.Levels;

            PermissionEngine.LoadFeatures(features, levels, userLevel);
        }
        catch
        {
            // Se il server non supporta ancora i livelli, fallback silenzioso
        }
    }

    private void txtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (string.IsNullOrWhiteSpace(txtUsername.Text))
            {
                txtUsername.Focus();
                return;
            }
            BtnLogin_Click(sender, e);
        }
    }
}
