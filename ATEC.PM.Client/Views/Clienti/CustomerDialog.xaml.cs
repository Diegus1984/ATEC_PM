using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

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
            CustomerSaveRequest? d = await ApiClient.GetDataAsync<CustomerSaveRequest>($"/api/customers/{_id}");
            if (d == null)
                return;

            txtCompanyName.Text = d.CompanyName;
            txtContactName.Text = d.ContactName;
            txtEmail.Text = d.Email;
            txtPhone.Text = d.Phone;
            txtAddress.Text = d.Address;
            txtVatNumber.Text = d.VatNumber;
            txtNotes.Text = d.Notes;
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

            if (ApiClient.IsApiSuccess(result, out string msg))
            {
                if (_id == 0 && ApiClient.TryGetApiData<int>(result, out int newId, out _))
                    CreatedCustomerId = newId;
                else
                    CreatedCustomerId = _id;
                DialogResult = true;
                Close();
            }
            else
                txtError.Text = msg;
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