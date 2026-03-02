using System.Globalization;
using System.Text.Json;
using System.Windows;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

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
            JsonDocument doc = JsonDocument.Parse(json);
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
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                JsonElement d = doc.RootElement.GetProperty("data");
                txtCode.Text = d.GetProperty("code").GetString() ?? "";
                txtDescription.Text = d.GetProperty("description").GetString() ?? "";
                txtCategory.Text = d.GetProperty("category").GetString() ?? "";
                txtSubcategory.Text = d.GetProperty("subcategory").GetString() ?? "";
                txtUnit.Text = d.GetProperty("unit").GetString() ?? "PZ";
                txtUnitCost.Text = d.GetProperty("unitCost").GetDecimal().ToString(CultureInfo.InvariantCulture);
                txtListPrice.Text = d.GetProperty("listPrice").GetDecimal().ToString(CultureInfo.InvariantCulture);
                txtSupplierCode.Text = d.GetProperty("supplierCode").GetString() ?? "";
                txtManufacturer.Text = d.GetProperty("manufacturer").GetString() ?? "";
                txtBarcode.Text = d.GetProperty("barcode").GetString() ?? "";
                txtNotes.Text = d.GetProperty("notes").GetString() ?? "";

                int suppId = d.GetProperty("supplierId").ValueKind == JsonValueKind.Null ? 0 : d.GetProperty("supplierId").GetInt32();
                foreach (LookupItem item in cmbSupplier.Items)
                {
                    if (item.Id == suppId) { cmbSupplier.SelectedItem = item; break; }
                }
            }
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtCode.Text))
        { txtError.Text = "Codice obbligatorio."; return; }

        btnSave.IsEnabled = false;
        try
        {
            int suppId = cmbSupplier.SelectedItem is LookupItem li ? li.Id : 0;
            decimal.TryParse(txtUnitCost.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal unitCost);
            decimal.TryParse(txtListPrice.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal listPrice);

            var obj = new
            {
                code = txtCode.Text,
                description = txtDescription.Text,
                category = txtCategory.Text,
                subcategory = txtSubcategory.Text,
                unit = txtUnit.Text,
                unitCost,
                listPrice,
                supplierId = suppId > 0 ? suppId : (int?)null,
                supplierCode = txtSupplierCode.Text,
                manufacturer = txtManufacturer.Text,
                barcode = txtBarcode.Text,
                notes = txtNotes.Text,
                isActive = true
            };

            string jsonBody = JsonSerializer.Serialize(obj);
            string result = _id == 0
                ? await ApiClient.PostAsync("/api/catalog", jsonBody)
                : await ApiClient.PutAsync($"/api/catalog/{_id}", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            { DialogResult = true; Close(); }
            else txtError.Text = doc.RootElement.GetProperty("message").GetString();
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
        finally { btnSave.IsEnabled = true; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
