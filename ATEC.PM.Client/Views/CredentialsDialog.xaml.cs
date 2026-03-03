using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class CredentialsDialog : Window
{
    private readonly int _employeeId;
    private readonly string _employeeName;

    public CredentialsDialog(int employeeId, string employeeName, string currentUsername = "")
    {
        InitializeComponent();
        _employeeId = employeeId;
        _employeeName = employeeName;
        Title = $"Credenziali - {employeeName}";
        txtUsername.Text = currentUsername;
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";

        if (string.IsNullOrWhiteSpace(txtUsername.Text))
        {
            txtError.Text = "Username obbligatorio.";
            return;
        }

        if (txtPassword.Password.Length < 4)
        {
            txtError.Text = "Password minimo 4 caratteri.";
            return;
        }

        if (txtPassword.Password != txtPasswordConfirm.Password)
        {
            txtError.Text = "Le password non coincidono.";
            return;
        }

        btnSave.IsEnabled = false;

        try
        {
            var obj = new
            {
                employeeId = _employeeId,
                username = txtUsername.Text.Trim(),
                password = txtPassword.Password
            };

            string jsonBody = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync("/api/auth/set-credentials", jsonBody);

            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                MessageBox.Show($"Credenziali impostate per {_employeeName}", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                txtError.Text = doc.RootElement.GetProperty("message").GetString();
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore: {ex.Message}";
        }
        finally
        {
            btnSave.IsEnabled = true;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}