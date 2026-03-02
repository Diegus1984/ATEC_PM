using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class EasyfattImportDialog : Window
{
    private List<ImportRow> _allRows = new();
    private List<ImportRow> _filteredRows = new();

    public EasyfattImportDialog()
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

        txtStatus.Text = "Caricamento fornitori da Easyfatt...";
        btnImport.IsEnabled = false;

        try
        {
            string encoded = Uri.EscapeDataString(filePath);
            string json = await ApiClient.GetAsync($"/api/import/easyfatt/suppliers?filePath={encoded}");
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

            txtSummary.Text = $"Totale: {totalFound}  |  Nuovi: {newCount}  |  Duplicati: {dupCount}";

            _allRows.Clear();
            JsonElement suppliers = data.GetProperty("suppliers");

            foreach (JsonElement s in suppliers.EnumerateArray())
            {
                string status = s.GetProperty("status").GetString() ?? "NUOVO";
                _allRows.Add(new ImportRow
                {
                    IsSelected = status == "NUOVO",
                    CompanyName = s.GetProperty("companyName").GetString() ?? "",
                    ContactName = s.GetProperty("contactName").GetString() ?? "",
                    Email = s.GetProperty("email").GetString() ?? "",
                    Phone = s.GetProperty("phone").GetString() ?? "",
                    Address = s.GetProperty("address").GetString() ?? "",
                    VatNumber = s.GetProperty("vatNumber").GetString() ?? "",
                    FiscalCode = s.GetProperty("fiscalCode").GetString() ?? "",
                    Notes = s.GetProperty("notes").GetString() ?? "",
                    Status = status,
                    ExistingId = s.GetProperty("existingId").GetInt32(),
                    ExistingName = s.GetProperty("existingName").GetString() ?? "",
                    Action = status == "NUOVO" ? "INSERT" : "SKIP"
                });
            }

            ApplyFilter();
            btnImport.IsEnabled = true;
            txtStatus.Text = $"Caricati {totalFound} fornitori. Selezionare quelli da importare.";
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
                return r.CompanyName.ToLower().Contains(search) ||
                       r.VatNumber.ToLower().Contains(search) ||
                       r.Email.ToLower().Contains(search) ||
                       r.ContactName.ToLower().Contains(search);
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
        foreach (ImportRow row in _filteredRows) { row.IsSelected = true; row.Action = row.Status == "NUOVO" ? "INSERT" : "UPDATE"; }
        dgImport.Items.Refresh();
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (ImportRow row in _filteredRows) { row.IsSelected = false; row.Action = "SKIP"; }
        dgImport.Items.Refresh();
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        List<ImportRow> toImport = _allRows.Where(r => r.IsSelected && r.Action != "SKIP").ToList();
        if (toImport.Count == 0)
        {
            MessageBox.Show("Nessun fornitore selezionato.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"Importare {toImport.Count} fornitori?\n\n" +
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
                suppliers = toImport.Select(r => new
                {
                    companyName = r.CompanyName,
                    contactName = r.ContactName,
                    email = r.Email,
                    phone = r.Phone,
                    address = r.Address,
                    vatNumber = r.VatNumber,
                    fiscalCode = r.FiscalCode,
                    notes = r.Notes,
                    status = r.Status,
                    existingId = r.ExistingId,
                    existingName = r.ExistingName,
                    action = r.Action
                }).ToList()
            };

            string jsonBody = JsonSerializer.Serialize(payload);
            string result = await ApiClient.PostAsync("/api/import/easyfatt/suppliers", jsonBody);
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

public class ImportRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _action = "";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "";
    public int ExistingId { get; set; }
    public string ExistingName { get; set; } = "";
    public string Action
    {
        get => _action;
        set { _action = value; OnPropertyChanged(nameof(Action)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
