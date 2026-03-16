using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ATEC.PM.Client.Services;

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
            JsonDocument doc = JsonDocument.Parse(result);
            JsonElement root = doc.RootElement;

            if (root.GetProperty("success").GetBoolean())
            {
                JsonElement data = root.GetProperty("data");
                App.Token = data.GetProperty("token").GetString() ?? "";
                App.UserFullName = data.GetProperty("fullName").GetString() ?? "";
                App.UserRole = data.GetProperty("userRole").GetString() ?? "";
                App.UserId = data.GetProperty("employeeId").GetInt32();

                // Carica reparti e competenze per il PermissionEngine
                await LoadUserContextAsync();

                new MainWindow().Show();
                Close();
            }
            else
            {
                txtError.Text = root.GetProperty("message").GetString();
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore: {ex.Message}";
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
            string json = await ApiClient.GetAsync($"/api/users/{App.UserId}");
            JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (!root.GetProperty("success").GetBoolean()) return;

            JsonElement data = root.GetProperty("data");

            List<string> deptCodes = new();
            List<string> respCodes = new();
            List<string> compCodes = new();

            foreach (JsonElement d in data.GetProperty("departments").EnumerateArray())
            {
                string code = d.GetProperty("departmentCode").GetString() ?? "";
                deptCodes.Add(code);
                if (d.GetProperty("isResponsible").GetBoolean())
                    respCodes.Add(code);
            }

            foreach (JsonElement c in data.GetProperty("competences").EnumerateArray())
                compCodes.Add(c.GetProperty("departmentCode").GetString() ?? "");

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
