using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks; // Necessario per Task
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs; // Assicurati che AllDTOs.cs abbia questo namespace

namespace ATEC.PM.Client.Views;

public partial class CatalogItemDialog : Window
{
    private readonly int _id;

    public CatalogItemDialog(int id = 0)
    {
        InitializeComponent();
        _id = id;
        Title = id == 0 ? "Nuovo Articolo" : "Modifica Articolo";
        Loaded += async (_, _) => await Init();
    }

    private async Task Init()
    {
        await LoadSuppliers();
        if (_id > 0) await LoadItem();
    }

    private async Task LoadSuppliers()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/suppliers");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // 1. Leggiamo usando il tipo che il controller invia effettivamente
            var response = JsonSerializer.Deserialize<ApiResponse<List<SupplierListItem>>>(json, options);

            if (response != null && response.Success && response.Data != null)
            {
                // 2. Trasformiamo i SupplierListItem in LookupItem per la ComboBox
                var items = new List<LookupItem> { new() { Id = 0, Name = "(nessuno)" } };

                items.AddRange(response.Data.Select(s => new LookupItem
                {
                    Id = s.Id,
                    Name = s.CompanyName // Qui CompanyName viene mappato su Name
                }));

                cmbSupplier.ItemsSource = items;
                cmbSupplier.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Errore caricamento fornitori: " + ex.Message);
        }
    }

    private async Task LoadItem()
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/catalog/{_id}");

            // CONTROLLO FONDAMENTALE: Se la stringa è vuota, il server ha fallito
            if (string.IsNullOrWhiteSpace(json))
            {
                txtError.Text = $"Errore: Il server ha restituito una risposta vuota per l'ID {_id}.";
                return;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Proviamo a deserializzare solo se abbiamo del contenuto
            var response = JsonSerializer.Deserialize<ApiResponse<CatalogItem>>(json, options);

            if (response != null && response.Success && response.Data != null)
            {
                var item = response.Data;

                txtCode.Text = item.Code ?? "";
                txtDescription.Text = item.Description ?? "";
                txtCategory.Text = item.Category ?? "";
                txtSubcategory.Text = item.Subcategory ?? "";
                txtUnit.Text = string.IsNullOrEmpty(item.Unit) ? "PZ" : item.Unit;

                txtUnitCost.Text = item.UnitCost.ToString("N4");
                txtListPrice.Text = item.ListPrice.ToString("N4");

                txtSupplierCode.Text = item.SupplierCode ?? "";
                txtManufacturer.Text = item.Manufacturer ?? "";
                txtBarcode.Text = item.Barcode ?? "";
                txtNotes.Text = item.Notes ?? "";

                if (item.SupplierId.HasValue)
                {
                    var suppliers = cmbSupplier.ItemsSource as List<LookupItem>;
                    var found = suppliers?.FirstOrDefault(s => s.Id == item.SupplierId.Value);
                    if (found != null) cmbSupplier.SelectedItem = found;
                }
            }
            else
            {
                txtError.Text = response?.Message ?? "Articolo non trovato nel database.";
            }
        }
        catch (JsonException jex)
        {
            txtError.Text = "Errore formato dati: Il server non ha inviato un JSON valido.";
            System.Diagnostics.Debug.WriteLine($"JSON Errato: {jex.Message}");
        }
        catch (Exception ex)
        {
            txtError.Text = "Errore tecnico: " + ex.Message;
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtCode.Text) || string.IsNullOrWhiteSpace(txtDescription.Text))
        {
            txtError.Text = "Codice e Descrizione sono obbligatori.";
            return;
        }

        btnSave.IsEnabled = false;
        try
        {
            int? suppId = (cmbSupplier.SelectedItem as LookupItem)?.Id;
            if (suppId == 0) suppId = null;

            // Parsing robusto dei decimali
            decimal.TryParse(txtUnitCost.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal unitCost);
            decimal.TryParse(txtListPrice.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal listPrice);

            // Prepariamo l'oggetto di salvataggio
            // Usa CatalogItem invece dell'oggetto anonimo o del Dto inesistente
            var saveReq = new CatalogItem
            {
                Id = _id,
                Code = txtCode.Text.Trim(),
                Description = txtDescription.Text.Trim(),
                Category = txtCategory.Text.Trim(),
                Subcategory = txtSubcategory.Text.Trim(),
                Unit = txtUnit.Text.Trim(),
                UnitCost = unitCost,
                ListPrice = listPrice,
                SupplierId = suppId,
                SupplierCode = txtSupplierCode.Text.Trim(),
                Manufacturer = txtManufacturer.Text.Trim(),
                Barcode = txtBarcode.Text.Trim(),
                Notes = txtNotes.Text.Trim(),
                IsActive = true
            };

            string jsonBody = JsonSerializer.Serialize(saveReq);
            string result = _id == 0
                ? await ApiClient.PostAsync("/api/catalog", jsonBody)
                : await ApiClient.PutAsync($"/api/catalog/{_id}", jsonBody);

            var response = JsonSerializer.Deserialize<ApiResponse<object>>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (response != null && response.Success)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                txtError.Text = response?.Message ?? "Errore durante il salvataggio.";
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