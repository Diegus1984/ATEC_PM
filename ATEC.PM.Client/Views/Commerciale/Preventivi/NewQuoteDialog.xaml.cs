using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.Views;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Commerciale.Preventivi;

public partial class NewQuoteDialog : Window
{
    public int CreatedQuoteId { get; private set; }

    public NewQuoteDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadCustomers();
    }

    private async Task LoadCustomers()
    {
        try
        {
            List<CustomerListItem> customers = await ApiClient.GetListAsync<CustomerListItem>("/api/customers");
            cmbCustomer.ItemsSource = customers
                .OrderBy(c => c.CompanyName)
                .Select(c => new { Id = c.Id, Name = c.CompanyName })
                .ToList();
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore caricamento clienti: {ex.Message}"); }
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
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Seleziona un cliente", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(txtTitle.Text))
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Inserisci un titolo", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            string json = await ApiClient.PostAsync("/api/quotes", body);
            if (ApiClient.TryGetApiData<int>(json, out int newId, out string message))
            {
                CreatedQuoteId = newId;
                DialogResult = true;
                Close();
            }
            else
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(string.IsNullOrEmpty(message) ? "Errore" : message,
                    "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
