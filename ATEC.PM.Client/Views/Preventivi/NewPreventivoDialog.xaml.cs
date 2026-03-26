using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.Views; // CustomerDialog è in questo namespace

namespace ATEC.PM.Client.Views.Preventivi;

public partial class NewPreventivoDialog : Window
{
    public int CreatedQuoteId { get; private set; }

    public NewPreventivoDialog()
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

    private async void BtnNewCustomer_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CustomerDialog { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            // Ricarica la lista clienti e seleziona il nuovo
            await LoadCustomers();
            if (dlg.CreatedCustomerId > 0)
                cmbCustomer.SelectedValue = dlg.CreatedCustomerId;
        }
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
            string quoteType = rbImpianto.IsChecked == true ? "IMPIANTO" : "SERVICE";

            string body = JsonSerializer.Serialize(new
            {
                CustomerId = customerId,
                Title = txtTitle.Text.Trim(),
                QuoteType = quoteType
            });

            var json = await ApiClient.PostAsync("/api/preventivi", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                if (doc.RootElement.TryGetProperty("data", out var dataEl))
                    CreatedQuoteId = dataEl.GetInt32();
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
