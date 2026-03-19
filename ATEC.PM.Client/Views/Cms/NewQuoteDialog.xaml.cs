using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Quotes;

public partial class NewQuoteDialog : Window
{
    public int CreatedQuoteId { get; private set; }

    public NewQuoteDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadData();
    }

    private List<QuoteGroupDto> _allGroups = new();
    private static readonly JsonSerializerOptions _jopt = new() { PropertyNameCaseInsensitive = true };

    private async System.Threading.Tasks.Task LoadData()
    {
        // Carica clienti
        try
        {
            string json = await ApiClient.GetAsync("/api/customers");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var customers = JsonSerializer.Deserialize<List<CustomerListItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(), _jopt) ?? new();
                cmbCustomer.ItemsSource = customers;
            }
        }
        catch { }

        // Carica listini
        try
        {
            string json = await ApiClient.GetAsync("/api/quote-catalog/price-lists");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var priceLists = JsonSerializer.Deserialize<List<QuotePriceListDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(), _jopt) ?? new();
                cmbPriceList.ItemsSource = priceLists;
                if (priceLists.Count > 0) cmbPriceList.SelectedIndex = 0;
            }
        }
        catch { }

        // Carica gruppi (template)
        try
        {
            string json = await ApiClient.GetAsync("/api/quote-catalog/groups");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _allGroups = JsonSerializer.Deserialize<List<QuoteGroupDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(), _jopt) ?? new();
                FilterGroupsByPriceList();
            }
        }
        catch { }
    }

    private void CmbPriceList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        FilterGroupsByPriceList();
    }

    private void FilterGroupsByPriceList()
    {
        int? plId = cmbPriceList.SelectedValue as int?;
        if (plId.HasValue)
            cmbGroup.ItemsSource = _allGroups.Where(g => g.PriceListId == plId.Value).ToList();
        else
            cmbGroup.ItemsSource = _allGroups;
        cmbGroup.SelectedIndex = -1;
    }

    private async void BtnCreate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtTitle.Text))
        {
            MessageBox.Show("Il titolo è obbligatorio.", "Validazione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (cmbCustomer.SelectedValue is not int customerId)
        {
            MessageBox.Show("Seleziona un cliente.", "Validazione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int? groupId = cmbGroup.SelectedValue as int?;
        int? priceListId = cmbPriceList.SelectedValue as int?;
        string payment = cmbPayment.Text?.Trim() ?? "";

        var dto = new QuoteSaveDto
        {
            PriceListId = priceListId,
            Title = txtTitle.Text.Trim(),
            CustomerId = customerId,
            GroupId = groupId,
            DeliveryDays = int.TryParse(txtDeliveryDays.Text, out int dd) ? dd : 90,
            ValidityDays = int.TryParse(txtValidityDays.Text, out int vd) ? vd : 30,
            PaymentType = payment
        };

        try
        {
            string body = JsonSerializer.Serialize(dto);
            string json = await ApiClient.PostAsync("/api/quotes", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                CreatedQuoteId = doc.RootElement.GetProperty("data").GetInt32();
                DialogResult = true;
                Close();
            }
            else
            {
                string msg = doc.RootElement.GetProperty("message").GetString() ?? "Errore";
                MessageBox.Show(msg, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
