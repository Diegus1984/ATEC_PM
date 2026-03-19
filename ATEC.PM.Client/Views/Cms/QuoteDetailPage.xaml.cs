using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Quotes;

public partial class QuoteDetailPage : Page
{
    private int _quoteId;
    private string _snapshotJson = "";
    private QuoteDto? _quote;
    private ObservableCollection<QuoteItemRow> _items = new();

    public QuoteDetailPage(int quoteId)
    {
        InitializeComponent();
        _quoteId = quoteId;
        dgItems.ItemsSource = _items;
        Loaded += async (_, _) =>
        {
            await LoadQuote();
            if (NavigationService != null)
                NavigationService.Navigating += NavigationService_Navigating;
        };
        Unloaded += (_, _) =>
        {
            if (NavigationService != null)
                NavigationService.Navigating -= NavigationService_Navigating;
        };
    }

    // ═══════════════════════════════════════════════
    // LOAD
    // ═══════════════════════════════════════════════

    private async Task LoadQuote(bool updateSnapshot = true)
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/quotes/{_quoteId}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _quote = JsonSerializer.Deserialize<QuoteDto>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (_quote == null) return;
                PopulateUI();
                if (updateSnapshot)
                    TakeSnapshot();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void PopulateUI()
    {
        if (_quote == null) return;

        txtQuoteNumber.Text = _quote.QuoteNumber;
        txtTitle.Text = _quote.Title;
        txtCustomer.Text = _quote.CustomerName;
        txtGroupName.Text = _quote.GroupName;
        txtContact1.Text = _quote.ContactName1;
        txtContact2.Text = _quote.ContactName2;
        txtContact3.Text = _quote.ContactName3;
        txtDeliveryDays.Text = _quote.DeliveryDays.ToString();
        txtValidityDays.Text = _quote.ValidityDays.ToString();
        txtPaymentType.Text = _quote.PaymentType;

        SetStatusBadge(_quote.Status);

        _items.Clear();
        foreach (var item in _quote.Items)
            _items.Add(new QuoteItemRow(item));

        UpdateTotalsUI();

        txtDiscountPct.Text = _quote.DiscountPct.ToString("N2");

        chkShowItemPrices.IsChecked = _quote.ShowItemPrices;
        chkShowSummary.IsChecked = _quote.ShowSummary;
        chkShowSummaryPrices.IsChecked = _quote.ShowSummaryPrices;

        txtNotesInternal.Text = _quote.NotesInternal;
        txtNotesQuote.Text = _quote.NotesQuote;
    }

    private void SetStatusBadge(string status)
    {
        var (label, bg, fg) = status switch
        {
            "draft" => ("Bozza", "#F3F4F6", "#374151"),
            "sent" => ("Inviato", "#DBEAFE", "#1D4ED8"),
            "negotiation" => ("In trattativa", "#FEF3C7", "#D97706"),
            "accepted" => ("Accettato", "#D1FAE5", "#059669"),
            "rejected" => ("Rifiutato", "#FEE2E2", "#DC2626"),
            "expired" => ("Scaduto", "#F3F4F6", "#6B7280"),
            "converted" => ("Convertito", "#D1FAE5", "#059669"),
            _ => (status, "#F3F4F6", "#374151")
        };

        txtStatus.Text = label;
        txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
        brdStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
    }

    private void UpdateTotalsUI()
    {
        if (_quote == null) return;
        txtSubtotal.Text = $"{_quote.Subtotal:N2}€";
        txtVatTotal.Text = $"{_quote.VatTotal:N2}€";
        decimal discountAmount = _quote.Subtotal * _quote.DiscountPct / 100 + _quote.DiscountAbs;
        txtDiscount.Text = $"-{discountAmount:N2}€";
        txtTotal.Text = $"{_quote.Total:N2}€";
        txtTotalWithVat.Text = $"{_quote.TotalWithVat:N2}€";
        txtCostTotal.Text = $"{_quote.CostTotal:N2}€";
        txtProfit.Text = $"{_quote.Profit:N2}€";
    }

    // ═══════════════════════════════════════════════
    // SNAPSHOT DIRTY TRACKING
    // ═══════════════════════════════════════════════

    private void TakeSnapshot()
    {
        _snapshotJson = BuildSnapshotJson();
    }

    private bool HasChanges()
    {
        if (string.IsNullOrEmpty(_snapshotJson)) return false;
        return BuildSnapshotJson() != _snapshotJson;
    }

    private string BuildSnapshotJson()
    {
        var snapshot = new
        {
            Title = txtTitle.Text.Trim(),
            ContactName1 = txtContact1.Text.Trim(),
            ContactName2 = txtContact2.Text.Trim(),
            ContactName3 = txtContact3.Text.Trim(),
            DeliveryDays = txtDeliveryDays.Text.Trim(),
            ValidityDays = txtValidityDays.Text.Trim(),
            PaymentType = txtPaymentType.Text.Trim(),
            DiscountPct = txtDiscountPct.Text.Trim(),
            ShowItemPrices = chkShowItemPrices.IsChecked,
            ShowSummary = chkShowSummary.IsChecked,
            ShowSummaryPrices = chkShowSummaryPrices.IsChecked,
            NotesInternal = txtNotesInternal.Text,
            NotesQuote = txtNotesQuote.Text,
            ItemIds = string.Join(",", _items.Select(i => $"{i.Id}:{i.Quantity}"))
        };
        return JsonSerializer.Serialize(snapshot);
    }

    private bool ConfirmLeave()
    {
        if (!HasChanges()) return true;
        return MessageBox.Show(
            "Ci sono modifiche non salvate. Vuoi uscire senza salvare?",
            "Conferma uscita",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private QuoteSaveDto BuildCurrentDto()
    {
        return new QuoteSaveDto
        {
            Title = txtTitle.Text.Trim(),
            CustomerId = _quote?.CustomerId ?? 0,
            ContactName1 = txtContact1.Text.Trim(),
            ContactName2 = txtContact2.Text.Trim(),
            ContactName3 = txtContact3.Text.Trim(),
            DeliveryDays = int.TryParse(txtDeliveryDays.Text, out int dd) ? dd : 0,
            ValidityDays = int.TryParse(txtValidityDays.Text, out int vd) ? vd : 60,
            PaymentType = txtPaymentType.Text.Trim(),
            GroupId = _quote?.GroupId,
            DiscountPct = decimal.TryParse(txtDiscountPct.Text, out decimal dp) ? dp : 0,
            ShowItemPrices = chkShowItemPrices.IsChecked == true,
            ShowSummary = chkShowSummary.IsChecked == true,
            ShowSummaryPrices = chkShowSummaryPrices.IsChecked == true,
            NotesInternal = txtNotesInternal.Text,
            NotesQuote = txtNotesQuote.Text,
            AssignedTo = _quote?.AssignedTo
        };
    }

    private void NavigationService_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (HasChanges())
        {
            if (!ConfirmLeave())
                e.Cancel = true;
            else
                _snapshotJson = ""; // reset — non chiedere più
        }
    }

    // ═══════════════════════════════════════════════
    // SAVE
    // ═══════════════════════════════════════════════

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_quote == null) return;
        try
        {
            string body = JsonSerializer.Serialize(BuildCurrentDto());
            await ApiClient.PutAsync($"/api/quotes/{_quoteId}", body);
            await LoadQuote(); // true = aggiorna snapshot
            MessageBox.Show("Preventivo salvato.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════
    // ITEMS
    // ═══════════════════════════════════════════════

    private void BtnAddItem_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddQuoteItemDialog(_quoteId) { Owner = Window.GetWindow(this) };
        dlg.ItemAdded += async () => await LoadQuote(false);
        dlg.ShowDialog();
    }

    private async void BtnRemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int itemId)
        {
            if (MessageBox.Show("Rimuovere questa voce?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await ApiClient.DeleteAsync($"/api/quotes/{_quoteId}/items/{itemId}");
                await LoadQuote(false);
            }
        }
    }

    // ═══════════════════════════════════════════════
    // STATUS
    // ═══════════════════════════════════════════════

    private async void CmbChangeStatus_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (cmbChangeStatus.SelectedItem is ComboBoxItem cbi && cbi.Tag is string newStatus
            && !string.IsNullOrEmpty(newStatus) && _quote != null)
        {
            if (MessageBox.Show($"Cambiare stato a '{cbi.Content}'?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    var dto = new QuoteStatusChangeDto { NewStatus = newStatus, Notes = "" };
                    string body = JsonSerializer.Serialize(dto);
                    string json = await ApiClient.PutAsync($"/api/quotes/{_quoteId}/status", body);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.GetProperty("success").GetBoolean())
                    {
                        await LoadQuote();
                    }
                    else
                    {
                        string msg = doc.RootElement.GetProperty("message").GetString() ?? "Errore";
                        MessageBox.Show(msg, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
            }

            cmbChangeStatus.SelectedIndex = 0;
        }
    }

    // ═══════════════════════════════════════════════
    // PDF
    // ═══════════════════════════════════════════════

    private async void BtnPdfPreview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            byte[] pdfBytes = await ApiClient.GetBytesAsync($"/api/quotes/{_quoteId}/pdf");

            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ATEC_Preventivo_{_quote?.QuoteNumber?.Replace("/", "-") ?? _quoteId.ToString()}.pdf");
            System.IO.File.WriteAllBytes(tempPath, pdfBytes);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore generazione PDF: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnPdfDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"{_quote?.QuoteNumber?.Replace("/", "-") ?? "Preventivo"}.pdf",
                Filter = "PDF|*.pdf",
                Title = "Salva preventivo PDF"
            };

            if (dlg.ShowDialog() == true)
            {
                byte[] pdfBytes = await ApiClient.GetBytesAsync($"/api/quotes/{_quoteId}/pdf");
                System.IO.File.WriteAllBytes(dlg.FileName, pdfBytes);
                MessageBox.Show("PDF salvato.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════════
    // NAVIGATION
    // ═══════════════════════════════════════════════

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmLeave())
        {
            _snapshotJson = "";
            NavigationService?.Navigate(new QuotesListPage());
        }
    }
}

// ═══════════════════════════════════════════════
// QuoteItemRow
// ═══════════════════════════════════════════════

public class QuoteItemRow : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public int? VariantId { get; set; }
    public string ItemType { get; set; } = "product";
    public string ItemTypeLabel => ItemType == "content" ? "Cont." : "Prod.";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "nr.";
    public decimal Quantity { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal VatPct { get; set; }
    public decimal LineTotal { get; set; }
    public decimal LineProfit { get; set; }
    public int SortOrder { get; set; }
    public string DescriptionRtf { get; set; } = "";

    public QuoteItemRow(QuoteItemDto dto)
    {
        Id = dto.Id;
        ProductId = dto.ProductId;
        VariantId = dto.VariantId;
        ItemType = dto.ItemType;
        Code = dto.Code;
        Name = dto.Name;
        Unit = dto.Unit;
        Quantity = dto.Quantity;
        CostPrice = dto.CostPrice;
        SellPrice = dto.SellPrice;
        DiscountPct = dto.DiscountPct;
        VatPct = dto.VatPct;
        LineTotal = dto.LineTotal;
        LineProfit = dto.LineProfit;
        SortOrder = dto.SortOrder;
        DescriptionRtf = dto.DescriptionRtf;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}