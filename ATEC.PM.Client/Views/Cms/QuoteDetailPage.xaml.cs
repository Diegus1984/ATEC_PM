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
    private QuoteDto? _quote;
    private ObservableCollection<QuoteItemRow> _items = new();

    public QuoteDetailPage(int quoteId)
    {
        InitializeComponent();
        _quoteId = quoteId;
        dgItems.ItemsSource = _items;
        Loaded += async (_, _) => await LoadQuote();
    }

    // ═══════════════════════════════════════════════
    // LOAD
    // ═══════════════════════════════════════════════

    private async Task LoadQuote()
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

        // Header
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

        // Status badge
        SetStatusBadge(_quote.Status);

        // Items
        _items.Clear();
        foreach (var item in _quote.Items)
            _items.Add(new QuoteItemRow(item));

        // Totali
        UpdateTotalsUI();

        // Sconto globale
        txtDiscountPct.Text = _quote.DiscountPct.ToString("N2");

        // Toggle PDF
        chkShowItemPrices.IsChecked = _quote.ShowItemPrices;
        chkShowSummary.IsChecked = _quote.ShowSummary;
        chkShowSummaryPrices.IsChecked = _quote.ShowSummaryPrices;

        // Note
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
    // SAVE
    // ═══════════════════════════════════════════════

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_quote == null) return;

        var dto = new QuoteSaveDto
        {
            Title = txtTitle.Text.Trim(),
            CustomerId = _quote.CustomerId,
            ContactName1 = txtContact1.Text.Trim(),
            ContactName2 = txtContact2.Text.Trim(),
            ContactName3 = txtContact3.Text.Trim(),
            DeliveryDays = int.TryParse(txtDeliveryDays.Text, out int dd) ? dd : 0,
            ValidityDays = int.TryParse(txtValidityDays.Text, out int vd) ? vd : 60,
            PaymentType = txtPaymentType.Text.Trim(),
            GroupId = _quote.GroupId,
            DiscountPct = decimal.TryParse(txtDiscountPct.Text, out decimal dp) ? dp : 0,
            ShowItemPrices = chkShowItemPrices.IsChecked == true,
            ShowSummary = chkShowSummary.IsChecked == true,
            ShowSummaryPrices = chkShowSummaryPrices.IsChecked == true,
            NotesInternal = txtNotesInternal.Text,
            NotesQuote = txtNotesQuote.Text,
            AssignedTo = _quote.AssignedTo
        };

        try
        {
            string body = JsonSerializer.Serialize(dto);
            await ApiClient.PutAsync($"/api/quotes/{_quoteId}", body);
            await LoadQuote();
            MessageBox.Show("Preventivo salvato.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════
    // ITEMS — Aggiungi dal catalogo
    // ═══════════════════════════════════════════════

    private void BtnAddItem_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddQuoteItemDialog(_quoteId) { Owner = Window.GetWindow(this) };
        dlg.ItemAdded += async () => await LoadQuote();
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
                await LoadQuote();
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

            // Reset combo
            cmbChangeStatus.SelectedIndex = 0;
        }
    }

    // ═══════════════════════════════════════════════
    // NAVIGATION
    // ═══════════════════════════════════════════════

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        NavigationService?.Navigate(new QuotesListPage());
    }
}

// ═══════════════════════════════════════════════
// QuoteItemRow — ViewModel per riga DataGrid
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
