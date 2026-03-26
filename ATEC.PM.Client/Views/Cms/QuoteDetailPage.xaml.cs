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
using System.Windows.Input;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Quotes;

public partial class QuoteDetailPage : Page
{
    private int _quoteId;
    private string _snapshotJson = "";
    private QuoteDto? _quote;
    private ObservableCollection<QuoteProductGroup> _productGroups = new();
    private ObservableCollection<QuoteProductGroup> _autoIncludes = new();
    private bool _suppressToggle;

    // Drag & drop state
    private Point _dragStartPoint;
    private bool _isDragging;

    public QuoteDetailPage(int quoteId)
    {
        InitializeComponent();
        _quoteId = quoteId;
        icProducts.ItemsSource = _productGroups;
        lbAutoIncludes.ItemsSource = _autoIncludes;
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

        _suppressToggle = true;
        BuildProductGroups();
        _suppressToggle = false;

        UpdateTotalsUI();

        txtDiscountPct.Text = _quote.DiscountPct.ToString("N2");

        chkShowItemPrices.IsChecked = _quote.ShowItemPrices;
        chkShowSummary.IsChecked = _quote.ShowSummary;
        chkShowSummaryPrices.IsChecked = _quote.ShowSummaryPrices;
        chkHideQuantities.IsChecked = _quote.HideQuantities;

        txtNotesInternal.Text = _quote.NotesInternal;
        txtNotesQuote.Text = _quote.NotesQuote;
    }

    private void BuildProductGroups()
    {
        _productGroups.Clear();
        _autoIncludes.Clear();

        if (_quote == null) return;

        var items = _quote.Items.OrderBy(i => i.SortOrder).ToList();

        // Identifica parent items (senza parent_item_id)
        var parents = items.Where(i => i.ParentItemId == null).ToList();

        foreach (var parent in parents)
        {
            var variants = items
                .Where(i => i.ParentItemId == parent.Id)
                .Select(v => new QuoteVariantRow(v))
                .ToList();

            var group = new QuoteProductGroup(parent, variants);

            if (parent.IsAutoInclude)
                _autoIncludes.Add(group);
            else
                _productGroups.Add(group);
        }

        txtNoProducts.Visibility = _productGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        txtNoAutoIncludes.Visibility = _autoIncludes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        txtAutoCount.Text = _autoIncludes.Count > 0 ? $"({_autoIncludes.Count})" : "";
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

    private void TakeSnapshot() => _snapshotJson = BuildSnapshotJson();

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
            HideQuantities = chkHideQuantities.IsChecked,
            NotesInternal = txtNotesInternal.Text,
            NotesQuote = txtNotesQuote.Text
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
            HideQuantities = chkHideQuantities.IsChecked == true,
            NotesInternal = txtNotesInternal.Text,
            NotesQuote = txtNotesQuote.Text,
            AssignedTo = _quote?.AssignedTo
        };
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
            await LoadQuote();
            MessageBox.Show("Preventivo salvato.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════
    // ITEMS — Prodotti
    // ═══════════════════════════════════════════════

    private void BtnAddItem_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddQuoteItemDialog(_quoteId) { Owner = Window.GetWindow(this) };
        dlg.ItemAdded += async () => await LoadQuote(false);
        dlg.ShowDialog();
    }

    private void BtnToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        // Trova il parent Border che contiene l'ItemsControl delle varianti
        var parentBorder = FindParent<Border>(btn);
        if (parentBorder == null) return;

        // Risali fino al StackPanel del prodotto
        var productStack = FindParent<StackPanel>(parentBorder);
        if (productStack == null) return;

        // Trova l'ItemsControl delle varianti (secondo figlio dello StackPanel)
        foreach (var child in productStack.Children)
        {
            if (child is ItemsControl ic && ic.Name != "icProducts")
            {
                ic.Visibility = ic.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                btn.Content = ic.Visibility == Visibility.Visible ? "▲" : "▼";
                return;
            }
        }
    }

    private async void BtnRemoveProduct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int parentId)
        {
            var group = _productGroups.FirstOrDefault(g => g.ParentId == parentId)
                     ?? _autoIncludes.FirstOrDefault(g => g.ParentId == parentId);
            string name = group?.ParentName ?? $"#{parentId}";

            if (MessageBox.Show($"Rimuovere '{name}' e tutte le sue varianti?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // Cancella parent (cascade cancella anche i figli nel server)
                await ApiClient.DeleteAsync($"/api/quotes/{_quoteId}/items/{parentId}");
                // Cancella anche le varianti figlie esplicitamente
                if (group != null)
                {
                    foreach (var v in group.Variants)
                        await ApiClient.DeleteAsync($"/api/quotes/{_quoteId}/items/{v.Id}");
                }
                await LoadQuote(false);
            }
        }
    }

    // ═══════════════════════════════════════════════
    // VARIANTI — Toggle, Edit, Add locale, Remove
    // ═══════════════════════════════════════════════

    private async void VariantToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        if (sender is not CheckBox cb || cb.DataContext is not QuoteVariantRow row) return;

        try
        {
            var dto = BuildVariantDto(row);
            string body = JsonSerializer.Serialize(dto);
            await ApiClient.PutAsync($"/api/quotes/{_quoteId}/items/{row.Id}", body);
            await LoadQuote(false);
        }
        catch { }
    }

    private async void VariantField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        if (sender is not TextBox tb || tb.DataContext is not QuoteVariantRow row) return;

        try
        {
            row.ParseTexts();
            var dto = BuildVariantDto(row);
            string body = JsonSerializer.Serialize(dto);
            await ApiClient.PutAsync($"/api/quotes/{_quoteId}/items/{row.Id}", body);
            await LoadQuote(false);
        }
        catch { }
    }

    private QuoteItemSaveDto BuildVariantDto(QuoteVariantRow row)
    {
        return new QuoteItemSaveDto
        {
            ProductId = row.ProductId,
            VariantId = row.VariantId,
            ItemType = "product",
            Code = row.Code,
            Name = row.Name,
            DescriptionRtf = row.DescriptionRtf,
            Unit = row.Unit,
            Quantity = row.Quantity,
            CostPrice = row.CostPrice,
            SellPrice = row.SellPrice,
            DiscountPct = row.DiscountPct,
            VatPct = row.VatPct,
            SortOrder = row.SortOrder,
            IsActive = row.IsActive,
            IsConfirmed = row.IsConfirmed,
            ParentItemId = row.ParentItemId
        };
    }

    private async void BtnAddLocalVariant_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int parentId) return;

        var group = _productGroups.FirstOrDefault(g => g.ParentId == parentId)
                 ?? _autoIncludes.FirstOrDefault(g => g.ParentId == parentId);
        if (group == null) return;

        // Dialog semplice per nome e prezzo
        var dlg = new AddLocalVariantDialog(group.ParentName) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            var dto = new QuoteItemSaveDto
            {
                Code = dlg.VariantCode,
                Name = dlg.VariantName,
                Unit = dlg.VariantUnit,
                Quantity = dlg.VariantQty,
                SellPrice = dlg.VariantPrice,
                CostPrice = dlg.VariantCost,
                VatPct = 22,
                IsActive = true
            };
            string body = JsonSerializer.Serialize(dto);
            await ApiClient.PostAsync($"/api/quotes/{_quoteId}/items/{parentId}/variant", body);
            await LoadQuote(false);
        }
    }

    private async void BtnRemoveVariant_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int variantId)
        {
            await ApiClient.DeleteAsync($"/api/quotes/{_quoteId}/items/{variantId}");
            await LoadQuote(false);
        }
    }

    // ═══════════════════════════════════════════════
    // AUTO-INCLUDE — Remove + Drag & Drop
    // ═══════════════════════════════════════════════

    private async void BtnRemoveAutoInclude_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int parentId)
        {
            var group = _autoIncludes.FirstOrDefault(g => g.ParentId == parentId);
            if (group != null)
            {
                // Cancella parent + varianti
                foreach (var v in group.Variants)
                    await ApiClient.DeleteAsync($"/api/quotes/{_quoteId}/items/{v.Id}");
                await ApiClient.DeleteAsync($"/api/quotes/{_quoteId}/items/{parentId}");
                await LoadQuote(false);
            }
        }
    }

    private void AutoInclude_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    private void AutoInclude_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        Point pos = e.GetPosition(null);
        Vector diff = _dragStartPoint - pos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (lbAutoIncludes.SelectedItem is QuoteProductGroup dragItem && !_isDragging)
            {
                _isDragging = true;
                DragDrop.DoDragDrop(lbAutoIncludes, dragItem, DragDropEffects.Move);
                _isDragging = false;
            }
        }
    }

    private async void AutoInclude_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(QuoteProductGroup))) return;

        var droppedItem = (QuoteProductGroup)e.Data.GetData(typeof(QuoteProductGroup));

        // Trova posizione di drop
        var targetItem = GetAutoIncludeItemAtPoint(e.GetPosition(lbAutoIncludes));

        if (targetItem == null || targetItem == droppedItem) return;

        int oldIndex = _autoIncludes.IndexOf(droppedItem);
        int newIndex = _autoIncludes.IndexOf(targetItem);

        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;

        _autoIncludes.Move(oldIndex, newIndex);

        // Salva nuovo ordine sul server
        var allIds = new List<int>();
        // Prima i prodotti normali
        foreach (var g in _productGroups)
        {
            allIds.Add(g.ParentId);
            foreach (var v in g.Variants) allIds.Add(v.Id);
        }
        // Poi gli auto-include nel nuovo ordine
        foreach (var g in _autoIncludes)
        {
            allIds.Add(g.ParentId);
            foreach (var v in g.Variants) allIds.Add(v.Id);
        }

        string body = JsonSerializer.Serialize(allIds);
        await ApiClient.PutAsync($"/api/quotes/{_quoteId}/items/reorder", body);
    }

    private QuoteProductGroup? GetAutoIncludeItemAtPoint(Point pt)
    {
        var hit = VisualTreeHelper.HitTest(lbAutoIncludes, pt);
        if (hit?.VisualHit == null) return null;

        var item = FindParent<ListBoxItem>(hit.VisualHit);
        return item?.DataContext as QuoteProductGroup;
    }

    // ═══════════════════════════════════════════════
    // PDF
    // ═══════════════════════════════════════════════

    private async void BtnPdfPreview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Auto-save opzioni prima di generare il PDF
            string body = JsonSerializer.Serialize(BuildCurrentDto());
            await ApiClient.PutAsync($"/api/quotes/{_quoteId}", body);

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
            // Auto-save opzioni prima di generare il PDF
            string saveBody = JsonSerializer.Serialize(BuildCurrentDto());
            await ApiClient.PutAsync($"/api/quotes/{_quoteId}", saveBody);

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

    private void NavigationService_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (HasChanges())
        {
            if (!ConfirmLeave())
                e.Cancel = true;
            else
                _snapshotJson = "";
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmLeave())
        {
            _snapshotJson = "";
            NavigationService?.Navigate(new QuotesListPage());
        }
    }

    // ═══════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}

// ═══════════════════════════════════════════════
// QuoteProductGroup — Gruppo prodotto con varianti
// ═══════════════════════════════════════════════

public class QuoteProductGroup : INotifyPropertyChanged
{
    public int ParentId { get; set; }
    public int? ProductId { get; set; }
    public string ParentName { get; set; } = "";
    public string ParentCode { get; set; } = "";
    public string ItemType { get; set; } = "product";
    public string DescriptionRtf { get; set; } = "";
    public bool IsAutoInclude { get; set; }
    public int SortOrder { get; set; }

    public ObservableCollection<QuoteVariantRow> Variants { get; set; } = new();

    // Expand/collapse
    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; Notify(); Notify(nameof(ExpandAngle)); }
    }
    public double ExpandAngle => _isExpanded ? 0 : -90;

    // Display
    public string TypeBadgeLabel => ItemType == "content" ? "Cont." : "Prod.";
    public SolidColorBrush TypeBadgeColor => new(
        (Color)ColorConverter.ConvertFromString(ItemType == "content" ? "#7C3AED" : "#2563EB"));

    public string TotalDisplay
    {
        get
        {
            decimal total = Variants.Where(v => v.IsActive).Sum(v => v.LineTotal);
            return total > 0 ? $"{total:N2}€" : "";
        }
    }

    public QuoteProductGroup(QuoteItemDto parent, List<QuoteVariantRow> variants)
    {
        ParentId = parent.Id;
        ProductId = parent.ProductId;
        ParentName = parent.Name;
        ParentCode = parent.Code;
        ItemType = parent.ItemType;
        DescriptionRtf = parent.DescriptionRtf;
        IsAutoInclude = parent.IsAutoInclude;
        SortOrder = parent.SortOrder;
        Variants = new ObservableCollection<QuoteVariantRow>(variants);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ═══════════════════════════════════════════════
// QuoteVariantRow — Singola variante
// ═══════════════════════════════════════════════

public class QuoteVariantRow : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public int? VariantId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DescriptionRtf { get; set; } = "";
    public string Unit { get; set; } = "nr.";
    public decimal Quantity { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal VatPct { get; set; }
    public decimal LineTotal { get; set; }
    public decimal LineProfit { get; set; }
    public int SortOrder { get; set; }
    public int? ParentItemId { get; set; }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; Notify(); Notify(nameof(TotalColor)); Notify(nameof(LineTotalDisplay)); }
    }

    private bool _isConfirmed;
    public bool IsConfirmed
    {
        get => _isConfirmed;
        set { _isConfirmed = value; Notify(); }
    }

    // Display & edit helpers
    public string QuantityText { get; set; } = "1";
    public string SellPriceText { get; set; } = "0";
    public string DiscountPctText { get; set; } = "0";
    public string CostPriceText { get; set; } = "0";
    public string MarkupText { get; set; } = "1.000";
    public decimal MarkupValue { get; set; } = 1.0m;

    public string CostTotalDisplay => $"{CostPrice * Quantity:N2}";
    public string LineTotalDisplay => IsActive ? $"{LineTotal:N2}€" : "—";
    public SolidColorBrush TotalColor => new(
        (Color)ColorConverter.ConvertFromString(IsActive ? "#111827" : "#9CA3AF"));

    public QuoteVariantRow(QuoteItemDto dto)
    {
        Id = dto.Id;
        ProductId = dto.ProductId;
        VariantId = dto.VariantId;
        Code = dto.Code;
        Name = dto.Name;
        DescriptionRtf = dto.DescriptionRtf;
        Unit = dto.Unit;
        Quantity = dto.Quantity;
        CostPrice = dto.CostPrice;
        SellPrice = dto.SellPrice;
        DiscountPct = dto.DiscountPct;
        VatPct = dto.VatPct;
        LineTotal = dto.LineTotal;
        LineProfit = dto.LineProfit;
        SortOrder = dto.SortOrder;
        ParentItemId = dto.ParentItemId;
        _isActive = dto.IsActive;
        _isConfirmed = dto.IsConfirmed;

        QuantityText = dto.Quantity.ToString("G");
        SellPriceText = dto.SellPrice.ToString("N2");
        DiscountPctText = dto.DiscountPct.ToString("G");
        CostPriceText = dto.CostPrice.ToString("N2");
        MarkupValue = dto.CostPrice > 0 ? dto.SellPrice / dto.CostPrice : 1.0m;
        MarkupText = MarkupValue.ToString("N3");
    }

    public void ParseTexts()
    {
        if (decimal.TryParse(QuantityText, out decimal q)) Quantity = q;
        if (decimal.TryParse(CostPriceText, out decimal cp)) CostPrice = cp;
        if (decimal.TryParse(MarkupText, out decimal k)) { MarkupValue = k; SellPrice = CostPrice * k; }
        if (decimal.TryParse(SellPriceText, out decimal p)) SellPrice = p;
        if (decimal.TryParse(DiscountPctText, out decimal d)) DiscountPct = d;
        LineTotal = Quantity * SellPrice * (1 - DiscountPct / 100);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
