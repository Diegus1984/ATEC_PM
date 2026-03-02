using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class EasyfattArticlesImportDialog : Window
{
    private List<ArticleImportRow> _allRows = new();
    private List<ArticleImportRow> _filteredRows = new();

    public EasyfattArticlesImportDialog()
    {
        InitializeComponent();
    }

    private async void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        string filePath = txtFilePath.Text.Trim();
        if (string.IsNullOrEmpty(filePath))
        {
            txtStatus.Text = "Inserire il percorso del file .eft";
            return;
        }

        txtStatus.Text = "Caricamento articoli da Easyfatt...";
        btnImport.IsEnabled = false;

        try
        {
            string encoded = Uri.EscapeDataString(filePath);
            string json = await ApiClient.GetAsync($"/api/import/easyfatt/articles?filePath={encoded}");
            JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.GetProperty("success").GetBoolean())
            {
                txtStatus.Text = doc.RootElement.GetProperty("message").GetString() ?? "Errore";
                return;
            }

            JsonElement data = doc.RootElement.GetProperty("data");
            int totalFound = data.GetProperty("totalFound").GetInt32();
            int newCount = data.GetProperty("newCount").GetInt32();
            int dupCount = data.GetProperty("duplicateCount").GetInt32();
            int withSupplier = data.GetProperty("withSupplier").GetInt32();

            txtSummary.Text = $"Totale: {totalFound}  |  Nuovi: {newCount}  |  Duplicati: {dupCount}  |  Con fornitore: {withSupplier}";

            _allRows.Clear();
            JsonElement articles = data.GetProperty("articles");

            foreach (JsonElement a in articles.EnumerateArray())
            {
                string status = a.GetProperty("status").GetString() ?? "NUOVO";
                _allRows.Add(new ArticleImportRow
                {
                    IsSelected = status == "NUOVO",
                    EasyfattId = a.GetProperty("easyfattId").GetInt32(),
                    Code = a.GetProperty("code").GetString() ?? "",
                    Description = a.GetProperty("description").GetString() ?? "",
                    Category = a.GetProperty("category").GetString() ?? "",
                    Subcategory = a.GetProperty("subcategory").GetString() ?? "",
                    Unit = a.GetProperty("unit").GetString() ?? "",
                    UnitCost = a.GetProperty("unitCost").GetDecimal(),
                    ListPrice = a.GetProperty("listPrice").GetDecimal(),
                    SupplierCode = a.GetProperty("supplierCode").GetString() ?? "",
                    Manufacturer = a.GetProperty("manufacturer").GetString() ?? "",
                    Barcode = a.GetProperty("barcode").GetString() ?? "",
                    Notes = a.GetProperty("notes").GetString() ?? "",
                    Status = status,
                    ExistingId = a.GetProperty("existingId").GetInt32(),
                    ResolvedSupplierId = a.GetProperty("resolvedSupplierId").ValueKind == JsonValueKind.Null ? null : a.GetProperty("resolvedSupplierId").GetInt32(),
                    ResolvedSupplierName = a.GetProperty("resolvedSupplierName").GetString() ?? "",
                    Action = status == "NUOVO" ? "INSERT" : "SKIP"
                });
            }

            ApplyFilter();
            btnImport.IsEnabled = true;
            txtStatus.Text = $"Caricati {totalFound} articoli. Selezionare quelli da importare.";
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        if (txtSearch == null || _allRows.Count == 0) return;

        string search = txtSearch.Text?.Trim().ToLower() ?? "";

        _filteredRows = _allRows.Where(r =>
        {
            if (rbNew.IsChecked == true && r.Status != "NUOVO") return false;
            if (rbDup.IsChecked == true && r.Status != "DUPLICATO") return false;

            if (!string.IsNullOrEmpty(search))
            {
                return r.Code.ToLower().Contains(search) ||
                       r.Description.ToLower().Contains(search) ||
                       r.Category.ToLower().Contains(search) ||
                       r.Manufacturer.ToLower().Contains(search) ||
                       r.ResolvedSupplierName.ToLower().Contains(search);
            }
            return true;
        }).ToList();

        dgImport.ItemsSource = null;
        dgImport.ItemsSource = _filteredRows;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();
    private void TxtSearch_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (ArticleImportRow row in _filteredRows) { row.IsSelected = true; row.Action = row.Status == "NUOVO" ? "INSERT" : "UPDATE"; }
        dgImport.Items.Refresh();
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (ArticleImportRow row in _filteredRows) { row.IsSelected = false; row.Action = "SKIP"; }
        dgImport.Items.Refresh();
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        List<ArticleImportRow> toImport = _allRows.Where(r => r.IsSelected && r.Action != "SKIP").ToList();
        if (toImport.Count == 0)
        {
            MessageBox.Show("Nessun articolo selezionato.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"Importare {toImport.Count} articoli?\n\n" +
            $"INSERT: {toImport.Count(r => r.Action == "INSERT")}\n" +
            $"UPDATE: {toImport.Count(r => r.Action == "UPDATE")}",
            "Conferma Import", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        btnImport.IsEnabled = false;
        txtStatus.Text = "Importazione in corso...";

        try
        {
            var payload = new
            {
                articles = toImport.Select(r => new
                {
                    easyfattId = r.EasyfattId,
                    code = r.Code,
                    description = r.Description,
                    category = r.Category,
                    subcategory = r.Subcategory,
                    unit = r.Unit,
                    unitCost = r.UnitCost,
                    listPrice = r.ListPrice,
                    supplierCode = r.SupplierCode,
                    manufacturer = r.Manufacturer,
                    barcode = r.Barcode,
                    notes = r.Notes,
                    status = r.Status,
                    existingId = r.ExistingId,
                    resolvedSupplierId = r.ResolvedSupplierId,
                    resolvedSupplierName = r.ResolvedSupplierName,
                    action = r.Action
                }).ToList()
            };

            string jsonBody = JsonSerializer.Serialize(payload);
            string result = await ApiClient.PostAsync("/api/import/easyfatt/articles", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);

            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                JsonElement d = doc.RootElement.GetProperty("data");
                int imported = d.GetProperty("imported").GetInt32();
                int updated = d.GetProperty("updated").GetInt32();
                int skipped = d.GetProperty("skipped").GetInt32();

                MessageBox.Show($"Import completato!\n\nInseriti: {imported}\nAggiornati: {updated}\nSaltati: {skipped}",
                    "Risultato", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            else
            {
                txtStatus.Text = doc.RootElement.GetProperty("message").GetString() ?? "Errore import";
            }
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore: {ex.Message}";
        }
        finally
        {
            btnImport.IsEnabled = true;
        }
    }
}

public class ArticleImportRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _action = "";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }
    public int EasyfattId { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subcategory { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal UnitCost { get; set; }
    public decimal ListPrice { get; set; }
    public string SupplierCode { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "";
    public int ExistingId { get; set; }
    public int? ResolvedSupplierId { get; set; }
    public string ResolvedSupplierName { get; set; } = "";
    public string Action
    {
        get => _action;
        set { _action = value; OnPropertyChanged(nameof(Action)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
