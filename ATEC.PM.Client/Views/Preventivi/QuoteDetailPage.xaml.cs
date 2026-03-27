using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.Views.Preventivi.Models;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Preventivi;

public partial class QuoteDetailPage : Page
{
    private int _selectedQuoteId;
    private int _initialQuoteId;
    private QuoteDto? _selectedQuote;
    private ObservableCollection<QuoteProductGroup> _serviceProducts = new();
    private ObservableCollection<QuoteProductGroup> _autoIncludes = new();
    private bool _suppressServiceToggle;
    private bool _suppressInfoSave;

    /// <summary>DependencyProperty per binding XAML: nasconde bottoni azione quando true.</summary>
    public static readonly DependencyProperty IsReadOnlyModeProperty =
        DependencyProperty.Register(nameof(IsReadOnlyMode), typeof(bool), typeof(QuoteDetailPage),
            new PropertyMetadata(false));

    public bool IsReadOnlyMode
    {
        get => (bool)GetValue(IsReadOnlyModeProperty);
        set => SetValue(IsReadOnlyModeProperty, value);
    }

    // Alias per i guard nei handler
    private bool _readOnly => IsReadOnlyMode;

    public QuoteDetailPage() : this(0) { }

    public QuoteDetailPage(int quoteId, bool readOnly = false)
    {
        _initialQuoteId = quoteId;
        InitializeComponent();
        IsReadOnlyMode = readOnly;
        icServiceProducts.ItemsSource = _serviceProducts;
        icImpAutoIncludes.ItemsSource = _autoIncludes;
    }

    // ===============================================================
    // LOAD
    // ===============================================================

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialQuoteId > 0)
            await LoadQuoteDetail(_initialQuoteId);
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        NavigationService?.Navigate(new QuotesHomePage());
    }

    private async Task LoadQuoteDetail(int quoteId)
    {
        try
        {
            var json = await ApiClient.GetAsync($"/api/quotes/{quoteId}");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var quote = JsonSerializer.Deserialize<QuoteDto>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (quote == null) return;

            _selectedQuoteId = quote.Id;
            _selectedQuote = quote;

            _suppressInfoSave = true;

            // Header
            txtSectionTitle.Text = $"{quote.QuoteNumber} \u2014 {quote.Title}";

            // Status badge
            txtQuoteStatus.Text = quote.Status.ToUpperInvariant();
            SetStatusBadgeColors(quote.Status);

            // Info strip
            txtQuoteCustomer.Text = quote.CustomerName;
            txtQuoteCreatedBy.Text = $"di {quote.CreatedByName}";
            txtQuoteDate.Text = quote.CreatedAt.ToString("dd/MM/yyyy");

            // Type badge
            bool isPlant = quote.QuoteType == "IMPIANTO";
            brdTypeBadge.Background = Brush(isPlant ? "#FFF7ED" : "#F0FDF4");
            txtQuoteType.Text = isPlant ? "IMPIANTO" : "SERVIZIO";
            txtQuoteType.Foreground = Brush(isPlant ? "#EA580C" : "#059669");

            // Editable fields
            txtEditTitle.Text = quote.Title;
            txtEditContact1.Text = quote.ContactName1;
            txtEditContact2.Text = quote.ContactName2;
            txtEditContact3.Text = quote.ContactName3;
            txtEditPaymentType.Text = quote.PaymentType;
            txtEditValidityDays.Text = quote.ValidityDays.ToString();
            txtEditDeliveryDays.Text = quote.DeliveryDays.ToString();
            txtEditNotesInternal.Text = quote.NotesInternal;
            txtEditNotesQuote.Text = quote.NotesQuote;

            // Read-only info fields (horizontal header)
            txtInfoCliente.Text = quote.CustomerName;
            txtInfoTipo.Text = isPlant ? "IMPIANTO" : "SERVIZIO";

            // PDF options
            chkShowItemPrices.IsChecked = quote.ShowItemPrices;
            chkShowSummary.IsChecked = quote.ShowSummary;
            chkShowSummaryPrices.IsChecked = quote.ShowSummaryPrices;
            chkHideQuantities.IsChecked = quote.HideQuantities;

            // Riepilogo -- populated after costing loads via CostingTreeControl.PricingUpdated event
            if (quote.QuoteType != "IMPIANTO")
            {
                // For SERVICE, show simple totals
                txtSumFinal.Text = $"{quote.Total:N2} \u20ac";
            }

            _suppressInfoSave = false;

            // Read-only mode per revisioni superate
            if (_readOnly)
                ApplyReadOnlyMode();

            // Action buttons visibility
            UpdateActionButtons(quote.Status, quote.QuoteType);

            // Show IMPIANTO (full-page layout) or SERVICE (catalogo) panel
            pnlImpiantoLayout.Visibility = isPlant ? Visibility.Visible : Visibility.Collapsed;
            pnlServiceCatalog.Visibility = isPlant ? Visibility.Collapsed : Visibility.Visible;

            // Show detail
            pnlQuoteDetail.Visibility = Visibility.Visible;
            pnlActions.Visibility = Visibility.Visible;

            // Animate
            var sb = (Storyboard)FindResource("FadeIn");
            sb.Begin(this);

            // Load items (products + auto-includes) for both types
            LoadServiceItems(quote);

            // Always unsubscribe to prevent leaks when switching between quotes
            costingTreeControl.PricingUpdated -= OnPricingUpdated;

            // Load costing for IMPIANTO type
            if (isPlant)
            {
                costingTreeControl.PricingUpdated += OnPricingUpdated;
                costingTreeControl.LoadForPreventivo(quoteId, _readOnly);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
        }
    }

    private void OnPricingUpdated()
    {
        var rows = costingTreeControl.GetDistributionRows();
        var visibleRows = rows
            .Where(r => !r.IsShadowed)
            .Select(r => new { Name = r.SectionName, TotalFormatted = $"{r.SectionTotal:N2} \u20ac" })
            .ToList();
        icRiepilogo.ItemsSource = visibleRows;

        var s = costingTreeControl.GetPricingSummary();
        txtSumFinal.Text = $"{s.Final:N2} \u20ac";
    }

    private void SetStatusBadgeColors(string status)
    {
        (string bg, string fg) = status switch
        {
            "draft" => ("#F3F4F6", "#374151"),
            "sent" => ("#DBEAFE", "#1D4ED8"),
            "negotiation" => ("#FEF3C7", "#92400E"),
            "accepted" => ("#D1FAE5", "#059669"),
            "rejected" => ("#FEE2E2", "#DC2626"),
            "expired" => ("#E5E7EB", "#6B7280"),
            "converted" => ("#10B981", "#FFFFFF"),
            _ => ("#F3F4F6", "#374151")
        };
        brdStatusBadge.Background = Brush(bg);
        txtQuoteStatus.Foreground = Brush(fg);
    }

    private void UpdateActionButtons(string status, string quoteType)
    {
        bool isDraft = status == "draft";
        bool isSent = status == "sent";
        bool isAccepted = status == "accepted";
        bool isPlant = quoteType == "IMPIANTO";

        btnSetSent.Visibility = isDraft ? Visibility.Visible : Visibility.Collapsed;
        btnSetAccepted.Visibility = isSent ? Visibility.Visible : Visibility.Collapsed;
        btnSetRejected.Visibility = isDraft || isSent ? Visibility.Visible : Visibility.Collapsed;
        btnConvert.Visibility = isAccepted && isPlant ? Visibility.Visible : Visibility.Collapsed;
        btnDelete.Visibility = status is "draft" or "rejected" ? Visibility.Visible : Visibility.Collapsed;
        btnPdf.Visibility = Visibility.Visible;
    }

    private void ApplyReadOnlyMode()
    {
        // Nasconde pulsanti azione (barra superiore)
        pnlActions.Visibility = Visibility.Collapsed;

        // Badge SOLA LETTURA nell'header
        txtQuoteStatus.Text = "SUPERATA — SOLA LETTURA";
        brdStatusBadge.Background = Brush("#E5E7EB");
        txtQuoteStatus.Foreground = Brush("#6B7280");

        // Disabilita campi info (IMPIANTO)
        txtEditTitle.IsReadOnly = true;
        txtEditContact1.IsReadOnly = true;
        txtEditContact2.IsReadOnly = true;
        txtEditContact3.IsReadOnly = true;
        txtEditPaymentType.IsReadOnly = true;
        txtEditValidityDays.IsReadOnly = true;
        txtEditDeliveryDays.IsReadOnly = true;
        txtEditNotesInternal.IsReadOnly = true;
        txtEditNotesQuote.IsReadOnly = true;

        // Disabilita checkbox PDF
        chkShowItemPrices.IsEnabled = false;
        chkShowSummary.IsEnabled = false;
        chkShowSummaryPrices.IsEnabled = false;
        chkHideQuantities.IsEnabled = false;

        // Nascondi bottone aggiungi prodotto SERVICE (se esiste)
        if (FindName("btnServiceAddProduct") is Button btnAdd)
            btnAdd.Visibility = Visibility.Collapsed;

        // Disabilita campi SERVICE (se visibili)
        if (FindName("txtServiceNotesInternal") is TextBox ni) ni.IsReadOnly = true;
        if (FindName("txtServiceNotesQuote") is TextBox nq) nq.IsReadOnly = true;
        if (FindName("txtServiceDiscountPct") is TextBox sd) sd.IsReadOnly = true;
        if (FindName("chkServiceShowItemPrices") is CheckBox c1) c1.IsEnabled = false;
        if (FindName("chkServiceShowSummary") is CheckBox c2) c2.IsEnabled = false;
        if (FindName("chkServiceShowSummaryPrices") is CheckBox c3) c3.IsEnabled = false;
        if (FindName("chkServiceHideQuantities") is CheckBox c4) c4.IsEnabled = false;
    }

    // ===============================================================
    // ACTIONS
    // ===============================================================

    private async void BtnSetSent_Click(object sender, RoutedEventArgs e)
    {
        await ChangeStatus("sent");
    }

    private async void BtnSetAccepted_Click(object sender, RoutedEventArgs e)
    {
        await ChangeStatus("accepted");
    }

    private async void BtnSetRejected_Click(object sender, RoutedEventArgs e)
    {
        await ChangeStatus("rejected");
    }

    private async Task ChangeStatus(string newStatus)
    {
        if (_readOnly || _selectedQuoteId == 0) return;

        var confirm = MessageBox.Show($"Cambiare stato a {newStatus.ToUpperInvariant()}?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            string body = JsonSerializer.Serialize(new { NewStatus = newStatus });
            var json = await ApiClient.PutAsync($"/api/quotes/{_selectedQuoteId}/status", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await LoadQuoteDetail(_selectedQuoteId);
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _selectedQuoteId == 0) return;

        var dlg = new ConvertQuoteDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string body = JsonSerializer.Serialize(new { PmId = dlg.SelectedPmId });
            var json = await ApiClient.PostAsync($"/api/preventivi/{_selectedQuoteId}/convert", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Commessa creata",
                    "Conversione completata", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadQuoteDetail(_selectedQuoteId);
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _selectedQuoteId == 0) return;

        var confirm = MessageBox.Show("Eliminare questo preventivo?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var json = await ApiClient.DeleteAsync($"/api/quotes/{_selectedQuoteId}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _selectedQuoteId = 0;
                // Torna alla lista preventivi dopo eliminazione
                NavigationService?.Navigate(new QuotesHomePage());
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedQuoteId == 0) return;

        try
        {
            var bytes = await ApiClient.DownloadAsync($"/api/quotes/{_selectedQuoteId}/pdf");
            if (bytes == null || bytes.Length == 0)
            {
                MessageBox.Show("Impossibile generare il PDF", "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Sovrascrive il file precedente (se non bloccato dal viewer)
            var tempPath = Path.Combine(Path.GetTempPath(), $"preventivo_{_selectedQuoteId}.pdf");
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* bloccato dal viewer */ }

            File.WriteAllBytes(tempPath, bytes);
            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ===============================================================
    // CONTENUTI AUTOMATICI
    // ===============================================================

    private async void BtnReloadAutoIncludes_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _selectedQuoteId == 0) return;

        if (MessageBox.Show("Ricaricare i contenuti automatici dal catalogo?\nI contenuti automatici attuali verranno sostituiti.",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            var json = await ApiClient.PostAsync($"/api/quotes/{_selectedQuoteId}/reload-auto-includes", "{}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await ReloadServiceItems();
            }
            else
            {
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnRemoveImpAutoInclude_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not int parentId) return;
        if (_selectedQuoteId == 0) return;

        var group = _autoIncludes.FirstOrDefault(g => g.ParentId == parentId);
        string name = group?.ParentName ?? $"#{parentId}";

        if (MessageBox.Show($"Rimuovere '{name}'?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"/api/quotes/{_selectedQuoteId}/items/{parentId}");
            if (group != null)
            {
                foreach (var v in group.Variants)
                    await ApiClient.DeleteAsync($"/api/quotes/{_selectedQuoteId}/items/{v.Id}");
            }
            await ReloadServiceItems();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ===============================================================
    // EDITABLE INFO PANEL -- Auto-save on LostFocus
    // ===============================================================

    private async void InfoField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _suppressInfoSave || _selectedQuoteId == 0 || _selectedQuote == null) return;
        await SaveQuoteInfo();
    }

    private async void InfoCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _suppressInfoSave || _selectedQuoteId == 0 || _selectedQuote == null) return;
        await SaveQuoteInfo();
    }

    private async Task SaveQuoteInfo()
    {
        try
        {
            var dto = new QuoteSaveDto
            {
                Title = txtEditTitle.Text.Trim(),
                CustomerId = _selectedQuote!.CustomerId,
                QuoteType = _selectedQuote.QuoteType,
                ContactName1 = txtEditContact1.Text.Trim(),
                ContactName2 = txtEditContact2.Text.Trim(),
                ContactName3 = txtEditContact3.Text.Trim(),
                PaymentType = txtEditPaymentType.Text.Trim(),
                ValidityDays = int.TryParse(txtEditValidityDays.Text, out int vd) ? vd : 60,
                DeliveryDays = int.TryParse(txtEditDeliveryDays.Text, out int dd) ? dd : 0,
                NotesInternal = txtEditNotesInternal.Text,
                NotesQuote = txtEditNotesQuote.Text,
                ShowItemPrices = chkShowItemPrices.IsChecked == true,
                ShowSummary = chkShowSummary.IsChecked == true,
                ShowSummaryPrices = chkShowSummaryPrices.IsChecked == true,
                HideQuantities = chkHideQuantities.IsChecked == true,
                GroupId = _selectedQuote.GroupId,
                DiscountPct = _selectedQuote.DiscountPct,
                DiscountAbs = _selectedQuote.DiscountAbs,
                AssignedTo = _selectedQuote.AssignedTo
            };

            string body = JsonSerializer.Serialize(dto);
            await ApiClient.PutAsync($"/api/quotes/{_selectedQuoteId}", body);

            // Update header title to reflect changes
            txtSectionTitle.Text = $"{_selectedQuote.QuoteNumber} \u2014 {txtEditTitle.Text.Trim()}";
        }
        catch { /* silent save */ }
    }

    // ===============================================================
    // SERVICE CATALOG -- Items management
    // ===============================================================

    private void LoadServiceItems(QuoteDto quote)
    {
        _suppressServiceToggle = true;

        _serviceProducts.Clear();
        _autoIncludes.Clear();

        var items = quote.Items.OrderBy(i => i.SortOrder).ToList();
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
                _serviceProducts.Add(group);
        }

        icServiceAutoIncludes.ItemsSource = _autoIncludes;

        // Sconto, note, checkbox
        txtServiceDiscountPct.Text = quote.DiscountPct.ToString("N2");
        txtServiceNotesInternal.Text = quote.NotesInternal ?? "";
        txtServiceNotesQuote.Text = quote.NotesQuote ?? "";
        chkServiceShowItemPrices.IsChecked = quote.ShowItemPrices;
        chkServiceShowSummary.IsChecked = quote.ShowSummary;
        chkServiceShowSummaryPrices.IsChecked = quote.ShowSummaryPrices;
        chkServiceHideQuantities.IsChecked = quote.HideQuantities;
        pnlServiceNotes.Visibility = Visibility.Visible;

        txtServiceNoProducts.Visibility = _serviceProducts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        pnlServiceAutoIncludes.Visibility = _autoIncludes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        txtServiceAutoCount.Text = _autoIncludes.Count > 0 ? $" ({_autoIncludes.Count})" : "";
        // Aggiorna anche pannello IMPIANTO se visibile
        txtImpNoAutoIncludes.Visibility = _autoIncludes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        txtImpAutoCount.Text = _autoIncludes.Count > 0 ? $"({_autoIncludes.Count})" : "";
        UpdateServiceTotals(quote);

        _suppressServiceToggle = false;
    }

    private void UpdateServiceTotals(QuoteDto quote)
    {
        txtServiceSubtotal.Text = $"{quote.Subtotal:N2} \u20ac";
        decimal discountAmount = quote.Subtotal * quote.DiscountPct / 100 + quote.DiscountAbs;
        txtServiceDiscount.Text = discountAmount > 0 ? $"-{discountAmount:N2} \u20ac" : "0,00 \u20ac";
        txtServiceVat.Text = $"{quote.VatTotal:N2} \u20ac";
        txtServiceTotal.Text = $"{quote.Total:N2} \u20ac";
        txtServiceTotalVat.Text = $"{quote.TotalWithVat:N2} \u20ac";
        txtServiceCost.Text = $"{quote.CostTotal:N2} \u20ac";
        txtServiceProfit.Text = $"{quote.Profit:N2} \u20ac";
    }

    private void BtnServiceAddProduct_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _selectedQuoteId == 0) return;

        var dlg = new AddQuoteItemDialog(_selectedQuoteId) { Owner = Window.GetWindow(this) };
        dlg.ItemAdded += async () => await ReloadServiceItems();
        dlg.ShowDialog();
    }

    private async void BtnServiceRemoveProduct_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not int parentId) return;

        var group = _serviceProducts.FirstOrDefault(g => g.ParentId == parentId);
        string name = group?.ParentName ?? $"#{parentId}";

        if (MessageBox.Show($"Rimuovere '{name}' e tutte le sue varianti?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await ApiClient.DeleteAsync($"/api/quotes/{_selectedQuoteId}/items/{parentId}");
            if (group != null)
            {
                foreach (var v in group.Variants)
                    await ApiClient.DeleteAsync($"/api/quotes/{_selectedQuoteId}/items/{v.Id}");
            }
            await ReloadServiceItems();
        }
    }

    private async void BtnServiceRemoveVariant_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is Button btn && btn.Tag is int variantId)
        {
            await ApiClient.DeleteAsync($"/api/quotes/{_selectedQuoteId}/items/{variantId}");
            await ReloadServiceItems();
        }
    }

    private async void BtnServiceAddVariant_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not int parentId) return;

        var group = _serviceProducts.FirstOrDefault(g => g.ParentId == parentId);
        if (group == null) return;

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
            await ApiClient.PostAsync($"/api/quotes/{_selectedQuoteId}/items/{parentId}/variant", body);
            await ReloadServiceItems();
        }
    }

    private async void ServiceVariantToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _suppressServiceToggle) return;
        if (sender is not CheckBox cb || cb.DataContext is not QuoteVariantRow row) return;

        try
        {
            var dto = BuildServiceVariantDto(row);
            string body = JsonSerializer.Serialize(dto);
            await ApiClient.PutAsync($"/api/quotes/{_selectedQuoteId}/items/{row.Id}", body);
            await ReloadServiceItems();
        }
        catch { }
    }

    private async void ServiceVariantField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _suppressServiceToggle) return;
        if (sender is not TextBox tb || tb.DataContext is not QuoteVariantRow row) return;

        try
        {
            row.ParseTexts();
            var dto = BuildServiceVariantDto(row);
            string body = JsonSerializer.Serialize(dto);
            await ApiClient.PutAsync($"/api/quotes/{_selectedQuoteId}/items/{row.Id}", body);
            await ReloadServiceItems();
        }
        catch { }
    }

    private static QuoteItemSaveDto BuildServiceVariantDto(QuoteVariantRow row)
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

    private async Task ReloadServiceItems()
    {
        if (_selectedQuoteId == 0) return;

        try
        {
            var json = await ApiClient.GetAsync($"/api/quotes/{_selectedQuoteId}");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var quote = JsonSerializer.Deserialize<QuoteDto>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (quote == null) return;
            _selectedQuote = quote;
            LoadServiceItems(quote);
        }
        catch { }
    }

    // -- Sconto % --
    private async void ServiceDiscountPct_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _selectedQuoteId == 0) return;
        if (!decimal.TryParse(txtServiceDiscountPct.Text, out decimal pct)) return;

        try
        {
            await ApiClient.PatchAsync($"/api/quotes/{_selectedQuoteId}/field",
                JsonSerializer.Serialize(new { field = "discount_pct", value = pct.ToString() }));
            await ReloadServiceItems();
        }
        catch { }
    }

    // -- Note --
    private async void ServiceNotes_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _selectedQuoteId == 0) return;

        try
        {
            await ApiClient.PatchAsync($"/api/quotes/{_selectedQuoteId}/field",
                JsonSerializer.Serialize(new { field = "notes_internal", value = txtServiceNotesInternal.Text }));
            await ApiClient.PatchAsync($"/api/quotes/{_selectedQuoteId}/field",
                JsonSerializer.Serialize(new { field = "notes_quote", value = txtServiceNotesQuote.Text }));
        }
        catch { }
    }

    // -- Salva nome prodotto parent --
    private async void ServiceProductName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _suppressServiceToggle) return;
        if (sender is not TextBox tb || tb.Tag is not int parentId) return;
        if (tb.DataContext is not QuoteProductGroup group) return;

        try
        {
            await ApiClient.PatchAsync($"/api/quotes/{_selectedQuoteId}/items/{parentId}/field",
                JsonSerializer.Serialize(new { field = "name", value = group.ParentName }));
        }
        catch { }
    }

    // -- Expand/Collapse varianti --
    private void BtnServiceToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not QuoteProductGroup group) return;
        group.IsExpanded = !group.IsExpanded;

        // Ruota il triangolo
        if (btn.Content is TextBlock tb && tb.RenderTransform is RotateTransform rt)
            rt.Angle = group.IsExpanded ? 0 : -90;
    }

    // -- Refresh prodotto da catalogo --
    private async void BtnServiceRefreshProduct_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not int parentId) return;

        if (MessageBox.Show("Aggiornare il prodotto e le varianti dal catalogo? Le modifiche locali verranno sovrascritte.",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.PostAsync($"/api/quotes/{_selectedQuoteId}/items/{parentId}/refresh-from-catalog", "{}");
            await ReloadServiceItems();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -- Edit RTF descrizione prodotto --
    private async void BtnServiceEditRtf_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not QuoteProductGroup group) return;

        var dlg = new MaterialRtfDialog(group.ParentName, group.DescriptionRtf)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                group.DescriptionRtf = dlg.HtmlContent;
                var body = new { descriptionRtf = dlg.HtmlContent };
                await ApiClient.PatchAsync($"/api/quotes/{_selectedQuoteId}/items/{group.ParentId}/field",
                    JsonSerializer.Serialize(new { field = "description_rtf", value = dlg.HtmlContent }));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // -- Clona prodotto + varianti --
    private async void BtnServiceCloneProduct_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not int parentId) return;

        if (MessageBox.Show("Duplicare questo prodotto con tutte le varianti?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.PostAsync($"/api/quotes/{_selectedQuoteId}/items/{parentId}/clone", "{}");
            await ReloadServiceItems();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -- Auto-includes: Ricarica dal catalogo --
    private async void BtnServiceReloadAutoIncludes_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _selectedQuoteId == 0) return;

        try
        {
            await ApiClient.PostAsync($"/api/quotes/{_selectedQuoteId}/reload-auto-includes", "{}");
            await ReloadServiceItems();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -- Edit RTF contenuto auto-include (SERVICE) --
    private async void BtnServiceEditAutoIncludeRtf_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not QuoteProductGroup group) return;
        await EditAutoIncludeRtf(group);
    }

    // -- Edit RTF contenuto auto-include (IMPIANTO) --
    private async void BtnEditAutoIncludeRtf_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not QuoteProductGroup group) return;
        await EditAutoIncludeRtf(group);
    }

    private async Task EditAutoIncludeRtf(QuoteProductGroup group)
    {
        var dlg = new MaterialRtfDialog(group.ParentName, group.DescriptionRtf)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                group.DescriptionRtf = dlg.HtmlContent;
                await ApiClient.PatchAsync($"/api/quotes/{_selectedQuoteId}/items/{group.ParentId}/field",
                    JsonSerializer.Serialize(new { field = "description_rtf", value = dlg.HtmlContent }));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnServiceRemoveAutoInclude_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not int parentId) return;

        await ApiClient.DeleteAsync($"/api/quotes/{_selectedQuoteId}/items/{parentId}");
        await ReloadServiceItems();
    }

    // ===============================================================
    // HELPERS
    // ===============================================================

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
