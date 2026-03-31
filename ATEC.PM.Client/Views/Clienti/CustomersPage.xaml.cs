using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class CustomersPage : Page
{
    private List<CustomerListItem> _allItems = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;
    private CustomerListItem? _selectedCustomer;
    private bool _isEditing;

    private const string PREF_SPLITTER = "customers.splitter_ratio";

    public CustomersPage()
    {
        InitializeComponent();
        syncBar.SyncCompleted += async () => await Load();
        Loaded += async (_, _) =>
        {
            LoadSplitterPosition();
            await Load();
        };
    }

    // === Splitter persistenza ===

    private void LoadSplitterPosition()
    {
        double ratio = UserPreferences.GetDouble(PREF_SPLITTER, 0);
        if (ratio > 0.1 && ratio < 0.9)
        {
            colList.Width = new GridLength(ratio, GridUnitType.Star);
            colDetail.Width = new GridLength(1 - ratio, GridUnitType.Star);
        }
    }

    private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        double total = colList.ActualWidth + colDetail.ActualWidth;
        if (total > 0)
        {
            double ratio = colList.ActualWidth / total;
            UserPreferences.Set(PREF_SPLITTER, Math.Round(ratio, 4));
        }
    }

    private async Task Load()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync("/api/customers");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<ApiResponse<List<CustomerListItem>>>(json, options);

            if (response != null && response.Success)
            {
                _allItems = response.Data ?? new();
                ApplyFilter();
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    // === Filtri ===

    private void Filter_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag != null)
            _filterBoxes[tb.Tag.ToString()!] = tb;
    }

    private async void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, _filterCts.Token);
            ApplyFilter();
        }
        catch (TaskCanceledException) { }
    }

    private static bool Match(string? value, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        string v = value?.ToLower() ?? "";

        bool startsWild = filter.StartsWith('*');
        bool endsWild = filter.EndsWith('*');

        if (startsWild && endsWild)
            return v.Contains(filter.Trim('*'));
        if (endsWild)
            return v.StartsWith(filter.TrimEnd('*'));
        if (startsWild)
            return v.EndsWith(filter.TrimStart('*'));

        return v.Contains(filter);
    }

    private void ApplyFilter()
    {
        if (_allItems == null) return;

        string fName = _filterBoxes.GetValueOrDefault("Name")?.Text.Trim().ToLower() ?? "";
        string fVat = _filterBoxes.GetValueOrDefault("Vat")?.Text.Trim().ToLower() ?? "";

        List<CustomerListItem> filtered = _allItems.Where(c =>
            Match(c.CompanyName, fName) &&
            Match(c.VatNumber, fVat)
        ).ToList();

        dgCustomers.ItemsSource = filtered;
        txtStatus.Text = $"{filtered.Count} clienti trovati su {_allItems.Count}";
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (TextBox tb in _filterBoxes.Values) tb.Clear();
        ApplyFilter();
    }

    // === Selezione e dettagli ===

    private void Dg_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgCustomers.SelectedItem is CustomerListItem c)
            ShowDetail(c);
        else
            ClearDetail();
    }

    private void ShowDetail(CustomerListItem c)
    {
        _selectedCustomer = c;
        SetEditMode(false);
        HideError();

        // Popola campi readonly
        txtDetailTitle.Text = c.CompanyName;
        lblCompanyName.Text = c.CompanyName;
        lblVatNumber.Text = c.VatNumber;
        lblFiscalCode.Text = c.FiscalCode;
        lblSdiCode.Text = c.SdiCode;
        lblAddress.Text = c.Address;
        lblContactName.Text = c.ContactName;
        lblPhone.Text = c.Phone;
        lblCell.Text = c.Cell;
        lblEmail.Text = c.Email;
        lblPec.Text = c.Pec;
        lblPaymentTerms.Text = c.PaymentTerms;
        lblNotes.Text = c.Notes;

        panelPlaceholder.Visibility = Visibility.Collapsed;
        panelDetail.Visibility = Visibility.Visible;
    }

    private void ClearDetail()
    {
        _selectedCustomer = null;
        _isEditing = false;
        panelDetail.Visibility = Visibility.Collapsed;
        panelPlaceholder.Visibility = Visibility.Visible;
    }

    // === Edit inline ===

    private void BtnEditInline_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCustomer == null) return;
        PopulateEditFields(_selectedCustomer);
        SetEditMode(true);
    }

    private void PopulateEditFields(CustomerListItem c)
    {
        txtCompanyName.Text = c.CompanyName;
        txtVatNumber.Text = c.VatNumber;
        txtFiscalCode.Text = c.FiscalCode;
        txtSdiCode.Text = c.SdiCode;
        txtAddress.Text = c.Address;
        txtContactName.Text = c.ContactName;
        txtPhone.Text = c.Phone;
        txtCell.Text = c.Cell;
        txtEmail.Text = c.Email;
        txtPec.Text = c.Pec;
        txtPaymentTerms.Text = c.PaymentTerms;
        txtNotes.Text = c.Notes;
    }

    private void SetEditMode(bool editing)
    {
        _isEditing = editing;
        Visibility show = editing ? Visibility.Visible : Visibility.Collapsed;
        Visibility hide = editing ? Visibility.Collapsed : Visibility.Visible;

        // Toggle visibilita label/input
        lblCompanyName.Visibility = hide;
        txtCompanyName.Visibility = show;
        lblVatNumber.Visibility = hide;
        txtVatNumber.Visibility = show;
        lblFiscalCode.Visibility = hide;
        txtFiscalCode.Visibility = show;
        lblSdiCode.Visibility = hide;
        txtSdiCode.Visibility = show;
        lblAddress.Visibility = hide;
        txtAddress.Visibility = show;
        lblContactName.Visibility = hide;
        txtContactName.Visibility = show;
        lblPhone.Visibility = hide;
        txtPhone.Visibility = show;
        lblCell.Visibility = hide;
        txtCell.Visibility = show;
        lblEmail.Visibility = hide;
        txtEmail.Visibility = show;
        lblPec.Visibility = hide;
        txtPec.Visibility = show;
        lblPaymentTerms.Visibility = hide;
        txtPaymentTerms.Visibility = show;
        lblNotes.Visibility = hide;
        txtNotes.Visibility = show;

        // Toggle pulsanti
        panelReadActions.Visibility = hide;
        panelEditActions.Visibility = show;
    }

    private async void BtnSaveInline_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCustomer == null) return;

        if (string.IsNullOrWhiteSpace(txtCompanyName.Text))
        {
            ShowError("Ragione sociale obbligatoria.");
            return;
        }

        HideError();
        try
        {
            var obj = new
            {
                companyName = txtCompanyName.Text.Trim(),
                contactName = txtContactName.Text.Trim(),
                email = txtEmail.Text.Trim(),
                pec = txtPec.Text.Trim(),
                phone = txtPhone.Text.Trim(),
                cell = txtCell.Text.Trim(),
                address = txtAddress.Text.Trim(),
                vatNumber = txtVatNumber.Text.Trim(),
                fiscalCode = txtFiscalCode.Text.Trim(),
                paymentTerms = txtPaymentTerms.Text.Trim(),
                sdiCode = txtSdiCode.Text.Trim(),
                notes = txtNotes.Text.Trim(),
                isActive = true
            };
            string jsonBody = JsonSerializer.Serialize(obj);
            string result = await ApiClient.PutAsync($"/api/customers/{_selectedCustomer.Id}", jsonBody);

            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                int savedId = _selectedCustomer.Id;
                await Load();
                // Riseleziona il cliente dopo il reload
                ReselectCustomer(savedId);
            }
            else
            {
                ShowError(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
            }
        }
        catch (Exception ex) { ShowError($"Errore: {ex.Message}"); }
    }

    private void BtnCancelInline_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCustomer != null)
            ShowDetail(_selectedCustomer);
    }

    private void ReselectCustomer(int id)
    {
        if (dgCustomers.ItemsSource is List<CustomerListItem> items)
        {
            CustomerListItem? match = items.FirstOrDefault(c => c.Id == id);
            if (match != null)
            {
                dgCustomers.SelectedItem = match;
                dgCustomers.ScrollIntoView(match);
            }
        }
    }

    private void ShowError(string msg)
    {
        txtDetailError.Text = msg;
        panelError.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        txtDetailError.Text = "";
        panelError.Visibility = Visibility.Collapsed;
    }

    // === Azioni toolbar ===

    private void Dg_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Double-click apre direttamente in modalita edit
        if (dgCustomers.SelectedItem is CustomerListItem c)
        {
            ShowDetail(c);
            PopulateEditFields(c);
            SetEditMode(true);
        }
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CustomerDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CustomerListItem c &&
            MessageBox.Show($"Disattivare {c.CompanyName}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await ApiClient.DeleteAsync($"/api/customers/{c.Id}");
            ClearDetail();
            await Load();
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await Load();

    private void BtnImportEasyfatt_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new EasyfattCustomersImportDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }
}
