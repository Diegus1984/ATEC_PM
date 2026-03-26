using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class CustomerDialog : Window
{
    private readonly int _id;
    public int CreatedCustomerId { get; private set; }

    public CustomerDialog(int id = 0)
    {
        InitializeComponent();
        _id = id;
        Title = id == 0 ? "Nuovo Cliente" : "Modifica Cliente";
        if (id > 0) Loaded += async (_, _) => await LoadCustomer();
    }

    private async Task LoadCustomer()
    {
        try
        {
            var json = await ApiClient.GetAsync($"/api/customers/{_id}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var d = doc.RootElement.GetProperty("data");
                txtCompanyName.Text = d.GetProperty("companyName").GetString() ?? "";
                txtContactName.Text = d.GetProperty("contactName").GetString() ?? "";
                txtEmail.Text = d.GetProperty("email").GetString() ?? "";
                txtPhone.Text = d.GetProperty("phone").GetString() ?? "";
                txtAddress.Text = d.GetProperty("address").GetString() ?? "";
                txtVatNumber.Text = d.GetProperty("vatNumber").GetString() ?? "";
                txtNotes.Text = d.GetProperty("notes").GetString() ?? "";
            }
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtCompanyName.Text))
        {
            txtError.Text = "Ragione sociale obbligatoria.";
            return;
        }

        btnSave.IsEnabled = false;
        try
        {
            var obj = new
            {
                companyName = txtCompanyName.Text,
                contactName = txtContactName.Text,
                email = txtEmail.Text,
                phone = txtPhone.Text,
                address = txtAddress.Text,
                vatNumber = txtVatNumber.Text,
                notes = txtNotes.Text,
                isActive = true
            };
            var jsonBody = JsonSerializer.Serialize(obj);
            var result = _id == 0
                ? await ApiClient.PostAsync("/api/customers", jsonBody)
                : await ApiClient.PutAsync($"/api/customers/{_id}", jsonBody);

            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                if (_id == 0 && doc.RootElement.TryGetProperty("data", out var dataEl))
                    CreatedCustomerId = dataEl.GetInt32();
                else
                    CreatedCustomerId = _id;
                DialogResult = true;
                Close();
            }
            else
                txtError.Text = doc.RootElement.GetProperty("message").GetString();
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
        finally { btnSave.IsEnabled = true; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}