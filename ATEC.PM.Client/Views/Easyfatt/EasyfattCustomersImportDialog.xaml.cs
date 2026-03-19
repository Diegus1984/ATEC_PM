using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class EasyfattCustomersImportDialog : Window
{
    private List<CustomerImportRow> _allRows = new();
    private List<CustomerImportRow> _filteredRows = new();

    public EasyfattCustomersImportDialog()
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

        txtStatus.Text = "Caricamento clienti da Easyfatt...";
        btnImport.IsEnabled = false;

        try
        {
            string encoded = Uri.EscapeDataString(filePath);
            string json = await ApiClient.GetAsync($"/api/import/easyfatt/customers?filePath={encoded}");

            if (string.IsNullOrWhiteSpace(json))
            {
                txtStatus.Text = "Risposta vuota dal server.";
                return;
            }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch (JsonException)
            {
                txtStatus.Text = $"Risposta non valida: {json.Substring(0, Math.Min(json.Length, 200))}";
                return;
            }

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
            JsonElement customers = data.GetProperty("customers");

            foreach (JsonElement c in customers.EnumerateArray())
            {
                string status = c.GetProperty("status").GetString() ?? "NUOVO";
                _allRows.Add(new CustomerImportRow
                {
                    IsSelected = status == "NUOVO",
                    EasyfattId = c.GetProperty("easyfattId").GetInt32(),
                    EasyfattCode = c.GetProperty("easyfattCode").GetString() ?? "",
                    CompanyName = c.GetProperty("companyName").GetString() ?? "",
                    ContactName = c.GetProperty("contactName").GetString() ?? "",
                    Email = c.GetProperty("email").GetString() ?? "",
                    Pec = c.GetProperty("pec").GetString() ?? "",
                    Phone = c.GetProperty("phone").GetString() ?? "",
                    Cell = c.GetProperty("cell").GetString() ?? "",
                    Address = c.GetProperty("address").GetString() ?? "",
                    VatNumber = c.GetProperty("vatNumber").GetString() ?? "",
                    FiscalCode = c.GetProperty("fiscalCode").GetString() ?? "",
                    PaymentTerms = c.GetProperty("paymentTerms").GetString() ?? "",
                    SdiCode = c.GetProperty("sdiCode").GetString() ?? "",
                    Notes = c.GetProperty("notes").GetString() ?? "",
                    Status = status,
                    ExistingId = c.GetProperty("existingId").GetInt32(),
                    ExistingName = c.GetProperty("existingName").GetString() ?? "",
                    Action = c.GetProperty("action").GetString() ?? (status == "NUOVO" ? "INSERT" : "SKIP")
                });
            }

            ApplyFilter();
            btnImport.IsEnabled = true;
            txtStatus.Text = $"Caricati {totalFound} clienti. Selezionare quelli da importare.";
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
                       r.Pec.ToLower().Contains(search) ||
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
        foreach (CustomerImportRow row in _filteredRows) { row.IsSelected = true; row.Action = row.Status == "NUOVO" ? "INSERT" : "UPDATE"; }
        dgImport.Items.Refresh();
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (CustomerImportRow row in _filteredRows) { row.IsSelected = false; row.Action = "SKIP"; }
        dgImport.Items.Refresh();
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        List<CustomerImportRow> toImport = _allRows.Where(r => r.IsSelected && r.Action != "SKIP").ToList();
        if (toImport.Count == 0)
        {
            MessageBox.Show("Nessun cliente selezionato.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"Importare {toImport.Count} clienti?\n\n" +
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
                customers = toImport.Select(r => new
                {
                    easyfattId = r.EasyfattId,
                    easyfattCode = r.EasyfattCode,
                    companyName = r.CompanyName,
                    contactName = r.ContactName,
                    email = r.Email,
                    pec = r.Pec,
                    phone = r.Phone,
                    cell = r.Cell,
                    address = r.Address,
                    vatNumber = r.VatNumber,
                    fiscalCode = r.FiscalCode,
                    paymentTerms = r.PaymentTerms,
                    sdiCode = r.SdiCode,
                    notes = r.Notes,
                    status = r.Status,
                    existingId = r.ExistingId,
                    existingName = r.ExistingName,
                    action = r.Action
                }).ToList()
            };

            string jsonBody = JsonSerializer.Serialize(payload);
            string result = await ApiClient.PostAsync("/api/import/easyfatt/customers", jsonBody);
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

public class CustomerImportRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _action = "";

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }
    public int EasyfattId { get; set; }
    public string EasyfattCode { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Pec { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Cell { get; set; } = "";
    public string Address { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public string PaymentTerms { get; set; } = "";
    public string SdiCode { get; set; } = "";
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
