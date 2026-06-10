using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

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
        _allRows.Clear();

        try
        {
            string encoded = Uri.EscapeDataString(filePath);
            ApiResponse<EasyfattArticlesPreviewDto>? previewResp = await ApiClient.GetApiAsync<EasyfattArticlesPreviewDto>(
                $"/api/import/easyfatt/articles?filePath={encoded}");

            if (previewResp == null || !previewResp.Success || previewResp.Data == null)
            {
                txtStatus.Text = previewResp?.Message ?? "Errore server";
                return;
            }

            EasyfattArticlesPreviewDto preview = previewResp.Data;
            txtSummary.Text = $"Totale: {preview.TotalFound} | Nuovi: {preview.NewCount} | Esistenti: {preview.DuplicateCount} | Con fornitore: {preview.WithSupplier}";

            foreach (EasyfattArticleDto a in preview.Articles)
            {
                string status = a.Status ?? "NUOVO";
                bool isNew = status == "NUOVO";
                _allRows.Add(new ArticleImportRow
                {
                    IsSelected = isNew,
                    EasyfattId = a.EasyfattId,
                    Code = a.Code,
                    Description = a.Description,
                    Category = a.Category,
                    Subcategory = a.Subcategory,
                    Unit = a.Unit,
                    UnitCost = a.UnitCost,
                    ListPrice = a.ListPrice,
                    SupplierCode = a.SupplierCode,
                    Manufacturer = a.Manufacturer,
                    Barcode = a.Barcode,
                    Notes = a.Notes,
                    Status = status,
                    ExistingId = a.ExistingId,
                    ResolvedSupplierId = a.ResolvedSupplierId,
                    ResolvedSupplierName = a.ResolvedSupplierName,
                    Action = string.IsNullOrEmpty(a.Action) ? (isNew ? "INSERT" : "SKIP") : a.Action
                });
            }

            ApplyFilter();
            btnImport.IsEnabled = true;
            txtStatus.Text = $"Caricati {preview.TotalFound} articoli. Scegliere le azioni e cliccare Importa.";
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore caricamento: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        if (txtSearch == null) return;

        string search = txtSearch.Text?.Trim().ToLower() ?? "";

        _filteredRows = _allRows.Where(r =>
        {
            // Filtro Radio Buttons
            if (rbNew.IsChecked == true && r.Status != "NUOVO") return false;
            if (rbDup.IsChecked == true && r.Status != "DUPLICATO") return false;

            // Filtro Ricerca Testuale
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
        foreach (ArticleImportRow row in _filteredRows)
        {
            row.IsSelected = true;
            row.Action = (row.Status == "NUOVO") ? "INSERT" : "UPDATE";
        }
        dgImport.Items.Refresh();
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (ArticleImportRow row in _filteredRows)
        {
            row.IsSelected = false;
            row.Action = "SKIP";
        }
        dgImport.Items.Refresh();
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        List<ArticleImportRow> toImport = _allRows.Where(r => r.IsSelected && r.Action != "SKIP").ToList();
        if (toImport.Count == 0)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Nessun articolo selezionato per l'importazione.", "Avviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
            $"Confermi l'importazione di {toImport.Count} articoli?\n\n" +
            $"Nuovi (INSERT): {toImport.Count(r => r.Action == "INSERT")}\n" +
            $"Esistenti (UPDATE): {toImport.Count(r => r.Action == "UPDATE")}",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        btnImport.IsEnabled = false;
        txtStatus.Text = "Importazione in corso...";

        try
        {
            var payload = new { articles = toImport };
            string jsonBody = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync("/api/import/easyfatt/articles", jsonBody);

            if (ApiClient.TryGetApiData<EasyfattImportResultDto>(result, out EasyfattImportResultDto? importResult, out string errMsg)
                && importResult != null)
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Operazione completata!\n\nArticoli creati: {importResult.Imported}\nArticoli aggiornati: {importResult.Updated}",
                    "Successo", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            else
                txtStatus.Text = "Errore: " + (errMsg ?? "import");
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore durante l'import: {ex.Message}";
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
    private string _action = "SKIP";

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
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}