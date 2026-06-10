using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

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
            ApiResponse<EasyfattCustomersPreviewDto>? previewResp = await ApiClient.GetApiAsync<EasyfattCustomersPreviewDto>(
                $"/api/import/easyfatt/customers?filePath={encoded}");

            if (previewResp == null || !previewResp.Success || previewResp.Data == null)
            {
                txtStatus.Text = previewResp?.Message ?? "Errore";
                return;
            }

            EasyfattCustomersPreviewDto preview = previewResp.Data;
            txtSummary.Text = $"Totale: {preview.TotalFound}  |  Nuovi: {preview.NewCount}  |  Duplicati: {preview.DuplicateCount}";

            _allRows.Clear();
            foreach (EasyfattCustomerDto c in preview.Customers)
            {
                string status = c.Status ?? "NUOVO";
                _allRows.Add(new CustomerImportRow
                {
                    IsSelected = status == "NUOVO",
                    EasyfattId = c.EasyfattId,
                    EasyfattCode = c.EasyfattCode,
                    CompanyName = c.CompanyName,
                    ContactName = c.ContactName,
                    Email = c.Email,
                    Pec = c.Pec,
                    Phone = c.Phone,
                    Cell = c.Cell,
                    Address = c.Address,
                    VatNumber = c.VatNumber,
                    FiscalCode = c.FiscalCode,
                    PaymentTerms = c.PaymentTerms,
                    SdiCode = c.SdiCode,
                    Notes = c.Notes,
                    Status = status,
                    ExistingId = c.ExistingId,
                    ExistingName = c.ExistingName,
                    Action = string.IsNullOrEmpty(c.Action) ? (status == "NUOVO" ? "INSERT" : "SKIP") : c.Action
                });
            }

            ApplyFilter();
            btnImport.IsEnabled = true;
            txtStatus.Text = $"Caricati {preview.TotalFound} clienti. Selezionare quelli da importare.";
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
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Nessun cliente selezionato.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult confirm = ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
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

            if (ApiClient.TryGetApiData<EasyfattImportResultDto>(result, out EasyfattImportResultDto? importResult, out string errMsg)
                && importResult != null)
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Import completato!\n\nInseriti: {importResult.Imported}\nAggiornati: {importResult.Updated}\nSaltati: {importResult.Skipped}",
                    "Risultato", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            else
                txtStatus.Text = errMsg ?? "Errore import";
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
