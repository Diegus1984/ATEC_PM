using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views.Preventivi;

public partial class NewPreventivoDialog : Window
{
    public int CreatedQuoteId { get; private set; }

    public NewPreventivoDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await OnLoaded();
    }

    private async Task OnLoaded()
    {
        await LoadCustomers();
        await LoadPriceLists();
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

    private async Task LoadPriceLists()
    {
        try
        {
            var json = await ApiClient.GetAsync("/api/quote-catalog/price-lists");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var items = doc.RootElement.GetProperty("data").EnumerateArray()
                    .Select(e => new { Id = e.GetProperty("id").GetInt32(), Name = e.GetProperty("name").GetString() ?? "" })
                    .OrderBy(x => x.Name)
                    .ToList();
                cmbPriceList.ItemsSource = items;
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore caricamento listini: {ex.Message}"); }
    }

    private async void cmbPriceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        cmbGroup.ItemsSource = null;
        if (cmbPriceList.SelectedValue is not int priceListId) return;

        try
        {
            var json = await ApiClient.GetAsync($"/api/quote-catalog/groups?priceListId={priceListId}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var items = doc.RootElement.GetProperty("data").EnumerateArray()
                    .Select(e2 => new { Id = e2.GetProperty("id").GetInt32(), Name = e2.GetProperty("name").GetString() ?? "" })
                    .OrderBy(x => x.Name)
                    .ToList();
                cmbGroup.ItemsSource = items;
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore caricamento gruppi: {ex.Message}"); }
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
            int? priceListId = cmbPriceList.SelectedValue as int?;
            int? groupId = cmbGroup.SelectedValue as int?;

            string body = JsonSerializer.Serialize(new
            {
                CustomerId = customerId,
                Title = txtTitle.Text.Trim(),
                QuoteType = quoteType,
                PriceListId = priceListId,
                GroupId = groupId
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
