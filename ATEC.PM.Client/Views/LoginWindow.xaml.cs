using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        txtUsername.Focus();
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";
        btnLogin.IsEnabled = false;
        btnLogin.Content = "Accesso...";

        try
        {
            App.ApiBaseUrl = txtServer.Text.TrimEnd('/');
            var result = await ApiClient.PostLogin(txtUsername.Text, txtPassword.Password);
            var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            if (root.GetProperty("success").GetBoolean())
            {
                var data = root.GetProperty("data");
                App.Token = data.GetProperty("token").GetString() ?? "";
                App.UserFullName = data.GetProperty("fullName").GetString() ?? "";
                App.UserRole = data.GetProperty("userRole").GetString() ?? "";
                App.UserId = data.GetProperty("employeeId").GetInt32();

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
}
