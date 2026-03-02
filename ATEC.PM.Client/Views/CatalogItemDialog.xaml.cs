using System.Globalization;
using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;
using System.Linq;

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
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var suppliers = JsonSerializer.Deserialize<List<LookupItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                var items = new List<LookupItem> { new() { Id = 0, Name = "(nessuno)" } };
                items.AddRange(suppliers.Select(s => new LookupItem { Id = s.Id, Name = s.Name }));
                cmbSupplier.ItemsSource = items;
                cmbSupplier.SelectedIndex = 0;
            }
        }
        catch { }
    }

    private async Task LoadItem()
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/catalog/{_id}");
            using var doc = JsonDocument.Parse(json);

            // Verifichiamo il successo della risposta API 
            if (doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean())
            {
                // Il contenuto reale è dentro la proprietà "data" 
                var data = doc.RootElement.GetProperty("data");

                // Mappatura campi con i nomi corretti del DB 
                txtCode.Text = data.TryGetProperty("code", out var c) ? c.GetString() : "";
                txtDescription.Text = data.TryGetProperty("description", out var d) ? d.GetString() : "";
                txtCategory.Text = data.TryGetProperty("category", out var cat) ? cat.GetString() : "";
                txtSubcategory.Text = data.TryGetProperty("subcategory", out var sub) ? sub.GetString() : "";
                txtUnit.Text = data.TryGetProperty("unit", out var u) ? u.GetString() : "PZ";

                // Gestione dei decimali 
                if (data.TryGetProperty("unitCost", out var uc))
                    txtUnitCost.Text = uc.GetDecimal().ToString("N4");

                if (data.TryGetProperty("listPrice", out var lp))
                    txtListPrice.Text = lp.GetDecimal().ToString("N4");

                txtSupplierCode.Text = data.TryGetProperty("supplierCode", out var sc) ? sc.GetString() : "";
                txtManufacturer.Text = data.TryGetProperty("manufacturer", out var m) ? m.GetString() : "";
                txtBarcode.Text = data.TryGetProperty("barcode", out var b) ? b.GetString() : "";
                txtNotes.Text = data.TryGetProperty("notes", out var n) ? n.GetString() : "";

                // Selezione del fornitore 
                if (data.TryGetProperty("supplierId", out var sid) && sid.ValueKind != JsonValueKind.Null)
                {
                    int targetId = sid.GetInt32();
                    var items = cmbSupplier.ItemsSource as List<LookupItem>;
                    cmbSupplier.SelectedItem = items?.FirstOrDefault(i => i.Id == targetId);
                }
            }
        }
        catch (Exception ex)
        {
            txtError.Text = "Errore nel caricamento: " + ex.Message;
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
            int suppId = (cmbSupplier.SelectedItem as LookupItem)?.Id ?? 0;

            // Parsing numeri sicuro
            decimal.TryParse(txtUnitCost.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal unitCost);
            decimal.TryParse(txtListPrice.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal listPrice);

            var obj = new
            {
                code = txtCode.Text.Trim(),
                description = txtDescription.Text.Trim(),
                category = txtCategory.Text.Trim(),
                subcategory = txtSubcategory.Text.Trim(),
                unit = txtUnit.Text.Trim(),
                unitCost,
                listPrice,
                supplierId = suppId > 0 ? suppId : (int?)null,
                supplierCode = txtSupplierCode.Text.Trim(),
                manufacturer = txtManufacturer.Text.Trim(),
                barcode = txtBarcode.Text.Trim(),
                notes = txtNotes.Text.Trim(),
                isActive = true
            };

            string jsonBody = JsonSerializer.Serialize(obj);
            string result = _id == 0
                ? await ApiClient.PostAsync("/api/catalog", jsonBody)
                : await ApiClient.PutAsync($"/api/catalog/{_id}", jsonBody);

            using var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
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