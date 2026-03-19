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
        _allRows.Clear();

        try
        {
            string encoded = Uri.EscapeDataString(filePath);
            string json = await ApiClient.GetAsync($"/api/import/easyfatt/articles?filePath={encoded}");

            if (string.IsNullOrWhiteSpace(json))
            {
                txtStatus.Text = "Risposta vuota dal server.";
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Controllo successo risposta
            if (!root.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
            {
                txtStatus.Text = root.TryGetProperty("message", out var msg) ? msg.GetString() : "Errore server";
                return;
            }

            // Accesso ai dati (Summary)
            if (!root.TryGetProperty("data", out var data)) return;

            // Estrazione conteggi con valori di default se mancano (evita l'errore "key not present")
            int totalFound = data.TryGetProperty("totalFound", out var tf) ? tf.GetInt32() : 0;
            int newCount = data.TryGetProperty("newCount", out var nc) ? nc.GetInt32() : 0;
            int dupCount = data.TryGetProperty("duplicateCount", out var dc) ? dc.GetInt32() : 0;
            int withSupplier = data.TryGetProperty("withSupplier", out var ws) ? ws.GetInt32() : 0;

            txtSummary.Text = $"Totale: {totalFound} | Nuovi: {newCount} | Esistenti: {dupCount} | Con fornitore: {withSupplier}";

            if (data.TryGetProperty("articles", out var articles) && articles.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement a in articles.EnumerateArray())
                {
                    // Determiniamo lo stato e l'azione predefinita
                    string status = a.TryGetProperty("status", out var st) ? (st.GetString() ?? "NUOVO") : "NUOVO";
                    bool isNew = status == "NUOVO";

                    _allRows.Add(new ArticleImportRow
                    {
                        IsSelected = isNew, // Seleziona automaticamente solo i nuovi
                        EasyfattId = a.GetProperty("easyfattId").GetInt32(),
                        Code = a.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                        Description = a.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                        Category = a.TryGetProperty("category", out var cat) ? cat.GetString() ?? "" : "",
                        Subcategory = a.TryGetProperty("subcategory", out var sub) ? sub.GetString() ?? "" : "",
                        Unit = a.TryGetProperty("unit", out var u) ? u.GetString() ?? "" : "",
                        UnitCost = a.TryGetProperty("unitCost", out var uc) ? uc.GetDecimal() : 0,
                        ListPrice = a.TryGetProperty("listPrice", out var lp) ? lp.GetDecimal() : 0,
                        SupplierCode = a.TryGetProperty("supplierCode", out var sc) ? sc.GetString() ?? "" : "",
                        Manufacturer = a.TryGetProperty("manufacturer", out var m) ? m.GetString() ?? "" : "",
                        Barcode = a.TryGetProperty("barcode", out var b) ? b.GetString() ?? "" : "",
                        Notes = a.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "",
                        Status = status,
                        ExistingId = a.TryGetProperty("existingId", out var exId) ? exId.GetInt32() : 0,
                        ResolvedSupplierId = a.TryGetProperty("resolvedSupplierId", out var rsId) && rsId.ValueKind != JsonValueKind.Null ? rsId.GetInt32() : null,
                        ResolvedSupplierName = a.TryGetProperty("resolvedSupplierName", out var rsName) ? rsName.GetString() ?? "" : "",
                        Action = isNew ? "INSERT" : "SKIP"
                    });
                }
            }

            ApplyFilter();
            btnImport.IsEnabled = true;
            txtStatus.Text = $"Caricati {totalFound} articoli. Scegliere le azioni e cliccare Importa.";
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
            MessageBox.Show("Nessun articolo selezionato per l'importazione.", "Avviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
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
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            if (root.GetProperty("success").GetBoolean())
            {
                var d = root.GetProperty("data");
                int imp = d.GetProperty("imported").GetInt32();
                int upd = d.GetProperty("updated").GetInt32();

                MessageBox.Show($"Operazione completata!\n\nArticoli creati: {imp}\nArticoli aggiornati: {upd}",
                    "Successo", MessageBoxButton.OK, MessageBoxImage.Information);

                this.DialogResult = true;
                this.Close();
            }
            else
            {
                txtStatus.Text = "Errore: " + root.GetProperty("message").GetString();
            }
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