using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class NewOfferDialog : Window
{
    public NewOfferDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadCustomers();
    }

    private async Task LoadCustomers()
    {
        try
        {
            var json = await ApiClient.GetAsync("/api/customers");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var items = doc.RootElement.GetProperty("data").EnumerateArray()
                    .Select(e => new { Id = e.GetProperty("id").GetInt32(), Name = e.GetProperty("companyName").GetString() ?? "" })
                    .OrderBy(x => x.Name)
                    .ToList();
                cmbCustomer.ItemsSource = items;
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore caricamento clienti: {ex.Message}"); }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (cmbCustomer.SelectedValue is not int customerId)
        {
            MessageBox.Show("Seleziona un cliente", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(txtTitle.Text))
        {
            MessageBox.Show("Inserisci un titolo", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            string body = JsonSerializer.Serialize(new
            {
                CustomerId = customerId,
                Title = txtTitle.Text.Trim(),
                Description = txtDescription.Text.Trim()
            });

            var json = await ApiClient.PostAsync("/api/offers", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore",
                    "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
