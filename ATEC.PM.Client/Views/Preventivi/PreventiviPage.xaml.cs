using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.Views.Quotes;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Preventivi;

public partial class PreventiviPage : Page
{
    private List<QuoteDto> _allQuotes = new();
    private int _selectedQuoteId;
    private QuoteDto? _selectedQuote;
    private ObservableCollection<QuoteProductGroup> _serviceProducts = new();
    private ObservableCollection<QuoteProductGroup> _autoIncludes = new();
    private bool _suppressServiceToggle;
    private bool _suppressInfoSave;

    public PreventiviPage()
    {
        InitializeComponent();
        icServiceProducts.ItemsSource = _serviceProducts;
        icImpAutoIncludes.ItemsSource = _autoIncludes;
    }

    // ═══════════════════════════════════════════════════════
    // LOAD & TREE
    // ═══════════════════════════════════════════════════════

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadQuotes();
    }

    private async Task LoadQuotes()
    {
        try
        {
            var json = await ApiClient.GetAsync("/api/preventivi");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _allQuotes = JsonSerializer.Deserialize<List<QuoteDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                BuildTree();
                txtStatus.Text = $"{_allQuotes.Count} preventivi";
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void BuildTree()
    {
        // Salva stato espansione
        var expandedCustomers = new HashSet<string>();
        var expandedYears = new HashSet<string>();
        foreach (TreeViewItem custNode in tree.Items)
        {
            if (custNode.IsExpanded) expandedCustomers.Add(custNode.Tag?.ToString() ?? "");
            foreach (TreeViewItem yearNode in custNode.Items)
                if (yearNode.IsExpanded) expandedYears.Add($"{custNode.Tag}|{yearNode.Tag}");
        }

        tree.Items.Clear();

        // Filtra ricerca
        string search = txtSearch.Text.Trim().ToLowerInvariant();
        var filtered = _allQuotes.AsEnumerable();
        if (!string.IsNullOrEmpty(search))
            filtered = filtered.Where(q =>
                (q.QuoteNumber?.ToLowerInvariant().Contains(search) == true) ||
                (q.Title?.ToLowerInvariant().Contains(search) == true) ||
                (q.CustomerName?.ToLowerInvariant().Contains(search) == true));

        // Raggruppa: Cliente → Anno → Preventivi
        var byCustomer = filtered
            .GroupBy(q => q.CustomerName)
            .OrderBy(g => g.Key);

        foreach (var custGroup in byCustomer)
        {
            var custNode = new TreeViewItem
            {
                Tag = $"customer|{custGroup.Key}",
            };
            custNode.Header = BuildCustomerHeader(custGroup.Key, custGroup.Count());

            var byYear = custGroup
                .GroupBy(q => q.CreatedAt.Year)
                .OrderByDescending(g => g.Key);

            foreach (var yearGroup in byYear)
            {
                var yearNode = new TreeViewItem
                {
                    Tag = $"year|{yearGroup.Key}",
                };
                yearNode.Header = BuildYearHeader(yearGroup.Key, yearGroup.Count());

                // Separa master (no parent) e revisioni (con parent)
                var masters = yearGroup.Where(q => q.ParentQuoteId == null).OrderByDescending(q => q.CreatedAt).ToList();
                var revisions = yearGroup.Where(q => q.ParentQuoteId != null).ToList();
                var revByParent = revisions.ToLookup(q => q.ParentQuoteId!.Value);

                foreach (var quote in masters)
                {
                    var quoteNode = new TreeViewItem
                    {
                        Tag = $"quote|{quote.Id}",
                        ContextMenu = BuildQuoteContextMenu(quote)
                    };
                    quoteNode.Header = BuildQuoteHeader(quote);

                    // Aggiungi revisioni come figli
                    foreach (var rev in revByParent[quote.Id].OrderBy(r => r.Revision))
                    {
                        var revNode = new TreeViewItem
                        {
                            Tag = $"quote|{rev.Id}",
                            ContextMenu = BuildQuoteContextMenu(rev)
                        };
                        revNode.Header = BuildQuoteHeader(rev);
                        quoteNode.Items.Add(revNode);
                    }

                    yearNode.Items.Add(quoteNode);
                }

                // Revisioni orfane (il master non è in questo anno/filtro)
                var orphanRevs = revisions.Where(r => !masters.Any(m => m.Id == r.ParentQuoteId)).ToList();
                foreach (var rev in orphanRevs.OrderByDescending(q => q.CreatedAt))
                {
                    var revNode = new TreeViewItem
                    {
                        Tag = $"quote|{rev.Id}",
                        ContextMenu = BuildQuoteContextMenu(rev)
                    };
                    revNode.Header = BuildQuoteHeader(rev);
                    yearNode.Items.Add(revNode);
                }

                string yearKey = $"customer|{custGroup.Key}|year|{yearGroup.Key}";
                yearNode.IsExpanded = expandedYears.Contains(yearKey) || expandedYears.Count == 0;
                custNode.Items.Add(yearNode);
            }

            custNode.IsExpanded = expandedCustomers.Contains($"customer|{custGroup.Key}") || expandedCustomers.Count == 0;
            tree.Items.Add(custNode);
        }

        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ═══════════════════════════════════════════════════════
    // TREE NODE HEADERS
    // ═══════════════════════════════════════════════════════

    private static StackPanel BuildCustomerHeader(string name, int count)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        sp.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#1A1D26"),
            VerticalAlignment = VerticalAlignment.Center
        });
        sp.Children.Add(new Border
        {
            Background = Brush("#F3F4F6"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = count.ToString(),
                FontSize = 11,
                Foreground = Brush("#4B5563"),
                FontWeight = FontWeights.Bold
            }
        });
        return sp;
    }

    private static TextBlock BuildYearHeader(int year, int count)
    {
        return new TextBlock
        {
            Text = $"{year}  \u00b7  {count} preventivi",
            FontSize = 13,
            Foreground = Brush("#6B7280"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private Border BuildQuoteHeader(QuoteDto q)
    {
        (string bg, string accent, string fgTitle, string fgSub) = q.Status switch
        {
            "draft" => ("#FFFFFF", "#D1D5DB", "#4B5563", "#9CA3AF"),
            "sent" => ("#EFF6FF", "#3B82F6", "#1E40AF", "#60A5FA"),
            "negotiation" => ("#FFFBEB", "#F59E0B", "#92400E", "#FBBF24"),
            "accepted" => ("#F0FDF4", "#10B981", "#065F46", "#34D399"),
            "rejected" => ("#FEF2F2", "#EF4444", "#991B1B", "#F87171"),
            "expired" => ("#F9FAFB", "#9CA3AF", "#6B7280", "#D1D5DB"),
            "converted" => ("#F0FDF4", "#10B981", "#065F46", "#34D399"),
            "superseded" => ("#F3F4F6", "#9CA3AF", "#9CA3AF", "#D1D5DB"),
            _ => ("#FFFFFF", "#D1D5DB", "#374151", "#9CA3AF")
        };

        string label = q.QuoteNumber ?? "";
        string title = (q.Title?.Length ?? 0) > 24 ? q.Title![..24] + "\u2026" : q.Title ?? "";
        bool isPlant = q.QuoteType == "IMPIANTO";

        var cardContainer = new Border
        {
            Background = Brush(bg),
            BorderBrush = Brush("#E5E7EB"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 3, 4, 3),
            Width = 260,
            ClipToBounds = true
        };

        var mainDock = new DockPanel();

        var accentLine = new Border
        {
            Width = 4,
            Background = Brush(accent)
        };
        DockPanel.SetDock(accentLine, Dock.Left);
        mainDock.Children.Add(accentLine);

        var sp = new StackPanel
        {
            Margin = new Thickness(10, 8, 10, 8)
        };

        // Top row: QuoteNumber + Type badge
        var topRow = new DockPanel();
        topRow.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(fgTitle)
        });

        var typeBadge = new Border
        {
            Background = Brush(isPlant ? "#FFF7ED" : "#F0FDF4"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = isPlant ? "IMP" : "SRV",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(isPlant ? "#EA580C" : "#059669")
            }
        };
        DockPanel.SetDock(typeBadge, Dock.Right);
        topRow.Children.Add(typeBadge);

        sp.Children.Add(topRow);
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            Foreground = Brush(fgSub),
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        // Riga pulsanti azione rapida
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0)
        };

        Button MakeBtn(string text, string tooltip, string fg, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = text, ToolTip = tooltip,
                FontSize = 10, Cursor = System.Windows.Input.Cursors.Hand,
                Width = 22, Height = 20, Padding = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brush(fg),
                Tag = q.Id
            };
            btn.Click += handler;
            return btn;
        }

        btnRow.Children.Add(MakeBtn("PDF", "Scarica PDF", "#6B7280", CardPdf_Click));
        btnRow.Children.Add(MakeBtn("⎘", "Duplica", "#6B7280", CardDuplicate_Click));
        btnRow.Children.Add(MakeBtn("Rev", "Crea revisione", "#6B7280", CardRevision_Click));
        if (q.Status is "draft" or "rejected")
            btnRow.Children.Add(MakeBtn("✕", "Elimina", "#DC2626", CardDelete_Click));

        sp.Children.Add(btnRow);

        mainDock.Children.Add(sp);
        cardContainer.Child = mainDock;

        return cardContainer;
    }

    // ═══════════════════════════════════════════════════════
    // CONTEXT MENU (tasto destro sul preventivo)
    // ═══════════════════════════════════════════════════════

    private ContextMenu BuildQuoteContextMenu(QuoteDto quote)
    {
        var menu = new ContextMenu();

        var miRevision = new MenuItem
        {
            Header = "Crea Revisione",
            Tag = quote.Id,
            Icon = new TextBlock { Text = "\U0001F504", FontSize = 14 } // 🔄
        };
        miRevision.Click += CtxCreateRevision_Click;

        var miDuplicate = new MenuItem
        {
            Header = "Duplica Preventivo",
            Tag = quote.Id,
            Icon = new TextBlock { Text = "\U0001F4CB", FontSize = 14 } // 📋
        };
        miDuplicate.Click += CtxDuplicate_Click;

        menu.Items.Add(miRevision);
        menu.Items.Add(miDuplicate);

        // Riattiva: solo su revisione superata che è l'ultima della catena
        if (quote.Status == "superseded" && quote.ParentQuoteId.HasValue)
        {
            int masterId = quote.ParentQuoteId.Value;
            var allRevisions = _allQuotes
                .Where(q => q.ParentQuoteId == masterId || q.Id == masterId)
                .OrderBy(q => q.Revision)
                .ToList();
            var lastSuperseded = allRevisions
                .Where(q => q.Status == "superseded")
                .OrderByDescending(q => q.Revision)
                .FirstOrDefault();
            // Mostra "Riattiva" solo se non esiste una revisione attiva (draft/sent/ecc.) con Rev più alto
            bool hasActiveAfter = allRevisions.Any(q => q.Revision > quote.Revision && q.Status != "superseded");

            if (lastSuperseded?.Id == quote.Id && !hasActiveAfter)
            {
                menu.Items.Add(new Separator());
                var miReactivate = new MenuItem
                {
                    Header = "Riattiva Revisione",
                    Tag = quote.Id,
                    Icon = new TextBlock { Text = "\u2705", FontSize = 14 }, // ✅
                    Foreground = Brush("#059669")
                };
                miReactivate.Click += CtxReactivate_Click;
                menu.Items.Add(miReactivate);
            }
        }

        // Se non è converted, aggiungi elimina
        if (quote.Status != "converted")
        {
            menu.Items.Add(new Separator());
            var miDelete = new MenuItem
            {
                Header = "Elimina Preventivo",
                Tag = quote.Id,
                Icon = new TextBlock { Text = "\u274C", FontSize = 14 }, // ❌
                Foreground = Brush("#DC2626")
            };
            miDelete.Click += CtxDelete_Click;
            menu.Items.Add(miDelete);
        }

        return menu;
    }

    // ── Card quick-action handlers ──
    private async void CardPdf_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int quoteId) return;
        try
        {
            byte[]? bytes = await ApiClient.GetBytesAsync($"/api/quotes/{quoteId}/pdf");
            if (bytes == null) return;
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"preventivo_{quoteId}.pdf");
            System.IO.File.WriteAllBytes(path, bytes);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"Errore PDF: {ex.Message}"); }
    }

    private void CardDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int quoteId) return;
        // Riusa la logica del context menu
        var fakeMenuItem = new MenuItem { Tag = quoteId };
        CtxDuplicate_Click(fakeMenuItem, e);
    }

    private void CardRevision_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int quoteId) return;
        var fakeMenuItem = new MenuItem { Tag = quoteId };
        CtxCreateRevision_Click(fakeMenuItem, e);
    }

    private void CardDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int quoteId) return;
        var fakeMenuItem = new MenuItem { Tag = quoteId };
        CtxDelete_Click(fakeMenuItem, e);
    }

    private async void CtxCreateRevision_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not int quoteId) return;

        var quote = _allQuotes.FirstOrDefault(q => q.Id == quoteId);
        string label = quote?.QuoteNumber ?? $"#{quoteId}";

        var result = MessageBox.Show(
            $"Creare una revisione di {label}?\n\nLa versione attuale verrà marcata come SUPERATA.",
            "Conferma Revisione", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var json = await ApiClient.PostAsync($"/api/preventivi/{quoteId}/revision", "{}");
            if (json != null)
            {
                var doc = System.Text.Json.JsonDocument.Parse(json);
                bool success = doc.RootElement.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
                string msg = doc.RootElement.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? "" : "";

                if (success)
                {
                    MessageBox.Show(msg, "Revisione Creata", MessageBoxButton.OK, MessageBoxImage.Information);
                    int newId = doc.RootElement.GetProperty("data").GetInt32();
                    await LoadQuotes();
                    await LoadQuoteDetail(newId);
                }
                else
                {
                    MessageBox.Show(msg, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CtxDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not int quoteId) return;

        var quote = _allQuotes.FirstOrDefault(q => q.Id == quoteId);
        string label = quote?.QuoteNumber ?? $"#{quoteId}";

        var result = MessageBox.Show(
            $"Duplicare {label} come nuovo preventivo indipendente?",
            "Conferma Duplicazione", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var json = await ApiClient.PostAsync($"/api/preventivi/{quoteId}/duplicate", "{}");
            if (json != null)
            {
                var doc = System.Text.Json.JsonDocument.Parse(json);
                bool success = doc.RootElement.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
                string msg = doc.RootElement.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? "" : "";

                if (success)
                {
                    MessageBox.Show(msg, "Duplicazione Completata", MessageBoxButton.OK, MessageBoxImage.Information);
                    int newId = doc.RootElement.GetProperty("data").GetInt32();
                    await LoadQuotes();
                    await LoadQuoteDetail(newId);
                }
                else
                {
                    MessageBox.Show(msg, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CtxDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not int quoteId) return;

        var quote = _allQuotes.FirstOrDefault(q => q.Id == quoteId);
        if (quote == null) return;
        string label = quote.QuoteNumber ?? $"#{quoteId}";

        // Trova la revisione precedente (superata) da riattivare
        QuoteDto? prevRevision = null;
        if (quote.ParentQuoteId.HasValue)
        {
            int masterId = quote.ParentQuoteId.Value;
            prevRevision = _allQuotes
                .Where(q => (q.ParentQuoteId == masterId || q.Id == masterId) && q.Revision < quote.Revision)
                .OrderByDescending(q => q.Revision)
                .FirstOrDefault();
        }

        string msg = $"Eliminare definitivamente {label}?\n\nQuesta azione non può essere annullata.";
        if (prevRevision != null && prevRevision.Status == "superseded")
            msg += $"\n\nLa revisione precedente ({prevRevision.QuoteNumber}) verrà riattivata.";

        var result = MessageBox.Show(msg, "Conferma Eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"/api/quotes/{quoteId}");

            // Riattiva la revisione precedente
            if (prevRevision != null && prevRevision.Status == "superseded")
            {
                await ApiClient.PutAsync($"/api/quotes/{prevRevision.Id}/status",
                    System.Text.Json.JsonSerializer.Serialize(new { newStatus = "draft", notes = "Riattivato" }));
            }

            await LoadQuotes();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CtxReactivate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not int quoteId) return;

        var quote = _allQuotes.FirstOrDefault(q => q.Id == quoteId);
        if (quote == null) return;
        string label = quote.QuoteNumber ?? $"#{quoteId}";

        var result = MessageBox.Show(
            $"Riattivare {label}?\n\nLo stato tornerà a BOZZA.",
            "Conferma Riattivazione", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.PutAsync($"/api/quotes/{quoteId}/status",
                System.Text.Json.JsonSerializer.Serialize(new { newStatus = "draft", notes = "Riattivato" }));
            await LoadQuotes();
            await LoadQuoteDetail(quoteId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════════════════
    // TREE SELECTION → LOAD DETAIL
    // ═══════════════════════════════════════════════════════

    private async void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (tree.SelectedItem is not TreeViewItem item) return;
        string tag = item.Tag?.ToString() ?? "";

        if (tag.StartsWith("quote|") && int.TryParse(tag.Split('|')[1], out int quoteId))
        {
            await LoadQuoteDetail(quoteId);
        }
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

            // Riepilogo — populated after costing loads via CostingTreeControl.PricingUpdated event
            if (quote.QuoteType != "IMPIANTO")
            {
                // For SERVICE, show simple totals
                txtSumFinal.Text = $"{quote.Total:N2} €";
            }

            _suppressInfoSave = false;

            // Action buttons visibility
            UpdateActionButtons(quote.Status, quote.QuoteType);

            // Show IMPIANTO (full-page layout) or SERVICE (catalogo) panel
            pnlImpiantoLayout.Visibility = isPlant ? Visibility.Visible : Visibility.Collapsed;
            pnlServiceCatalog.Visibility = isPlant ? Visibility.Collapsed : Visibility.Visible;

            // Show detail, hide placeholder
            pnlPlaceholder.Visibility = Visibility.Collapsed;
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
                costingTreeControl.LoadForPreventivo(quoteId);
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
            .Select(r => new { Name = r.SectionName, TotalFormatted = $"{r.SectionTotal:N2} €" })
            .ToList();
        icRiepilogo.ItemsSource = visibleRows;

        var s = costingTreeControl.GetPricingSummary();
        txtSumFinal.Text = $"{s.Final:N2} €";
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

    // ═══════════════════════════════════════════════════════
    // ACTIONS
    // ═══════════════════════════════════════════════════════

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewPreventivoDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            _ = LoadQuotes();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadQuotes();

    private void TxtSearch_Changed(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        BuildTree();
    }

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
        if (_selectedQuoteId == 0) return;

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
                await LoadQuotes();
                await LoadQuoteDetail(_selectedQuoteId);
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedQuoteId == 0) return;

        var dlg = new ConvertPreventivoDialog { Owner = Window.GetWindow(this) };
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
                await LoadQuotes();
                await LoadQuoteDetail(_selectedQuoteId);
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedQuoteId == 0) return;

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
                pnlQuoteDetail.Visibility = Visibility.Collapsed;
                pnlPlaceholder.Visibility = Visibility.Visible;
                pnlActions.Visibility = Visibility.Collapsed;
                txtSectionTitle.Text = "Seleziona un preventivo";
                await LoadQuotes();
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

            // Sovrascrive il file precedente (se non è bloccato dal viewer)
            var tempPath = Path.Combine(Path.GetTempPath(), $"preventivo_{_selectedQuoteId}.pdf");
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* bloccato dal viewer, sovrascriviamo */ }

            File.WriteAllBytes(tempPath, bytes);
            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════
    // CONTENUTI AUTOMATICI
    // ═══════════════════════════════════════════════════════

    private async void BtnReloadAutoIncludes_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedQuoteId == 0) return;

        if (MessageBox.Show("Ricaricare i contenuti automatici dal catalogo?\nI contenuti automatici attuali verranno sostituiti.",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            var json = await ApiClient.PostAsync($"/api/quotes/{_selectedQuoteId}/reload-auto-includes", "{}");
            var doc = System.Text.Json.JsonDocument.Parse(json);
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

    // ═══════════════════════════════════════════════════════
    // EDITABLE INFO PANEL — Auto-save on LostFocus
    // ═══════════════════════════════════════════════════════

    private async void InfoField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressInfoSave || _selectedQuoteId == 0 || _selectedQuote == null) return;
        await SaveQuoteInfo();
    }

    private async void InfoCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressInfoSave || _selectedQuoteId == 0 || _selectedQuote == null) return;
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

    // ═══════════════════════════════════════════════════════
    // SERVICE CATALOG — Items management
    // ═══════════════════════════════════════════════════════

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
        if (_selectedQuoteId == 0) return;

        var dlg = new AddQuoteItemDialog(_selectedQuoteId) { Owner = Window.GetWindow(this) };
        dlg.ItemAdded += async () => await ReloadServiceItems();
        dlg.ShowDialog();
    }

    private async void BtnServiceRemoveProduct_Click(object sender, RoutedEventArgs e)
    {
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
        if (sender is Button btn && btn.Tag is int variantId)
        {
            await ApiClient.DeleteAsync($"/api/quotes/{_selectedQuoteId}/items/{variantId}");
            await ReloadServiceItems();
        }
    }

    private async void BtnServiceAddVariant_Click(object sender, RoutedEventArgs e)
    {
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
        if (_suppressServiceToggle) return;
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
        if (_suppressServiceToggle) return;
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

    // ── Sconto % ──
    private async void ServiceDiscountPct_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_selectedQuoteId == 0) return;
        if (!decimal.TryParse(txtServiceDiscountPct.Text, out decimal pct)) return;

        try
        {
            await ApiClient.PatchAsync($"/api/quotes/{_selectedQuoteId}/field",
                JsonSerializer.Serialize(new { field = "discount_pct", value = pct.ToString() }));
            await ReloadServiceItems();
        }
        catch { }
    }

    // ── Note ──
    private async void ServiceNotes_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_selectedQuoteId == 0) return;

        try
        {
            await ApiClient.PatchAsync($"/api/quotes/{_selectedQuoteId}/field",
                JsonSerializer.Serialize(new { field = "notes_internal", value = txtServiceNotesInternal.Text }));
            await ApiClient.PatchAsync($"/api/quotes/{_selectedQuoteId}/field",
                JsonSerializer.Serialize(new { field = "notes_quote", value = txtServiceNotesQuote.Text }));
        }
        catch { }
    }

    // ── Salva nome prodotto parent ──
    private async void ServiceProductName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressServiceToggle) return;
        if (sender is not TextBox tb || tb.Tag is not int parentId) return;
        if (tb.DataContext is not QuoteProductGroup group) return;

        try
        {
            await ApiClient.PatchAsync($"/api/quotes/{_selectedQuoteId}/items/{parentId}/field",
                JsonSerializer.Serialize(new { field = "name", value = group.ParentName }));
        }
        catch { }
    }

    // ── Expand/Collapse varianti ──
    private void BtnServiceToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not QuoteProductGroup group) return;
        group.IsExpanded = !group.IsExpanded;

        // Ruota il triangolo
        if (btn.Content is TextBlock tb && tb.RenderTransform is RotateTransform rt)
            rt.Angle = group.IsExpanded ? 0 : -90;
    }

    // ── Refresh prodotto da catalogo ──
    private async void BtnServiceRefreshProduct_Click(object sender, RoutedEventArgs e)
    {
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

    // ── Edit RTF descrizione prodotto ──
    private async void BtnServiceEditRtf_Click(object sender, RoutedEventArgs e)
    {
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

    // ── Clona prodotto + varianti ──
    private async void BtnServiceCloneProduct_Click(object sender, RoutedEventArgs e)
    {
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

    // ── Auto-includes: Ricarica dal catalogo ──
    private async void BtnServiceReloadAutoIncludes_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedQuoteId == 0) return;

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

    // ── Auto-includes: Rimuovi ──
    // ── Edit RTF contenuto auto-include (SERVICE) ──
    private async void BtnServiceEditAutoIncludeRtf_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not QuoteProductGroup group) return;
        await EditAutoIncludeRtf(group);
    }

    // ── Edit RTF contenuto auto-include (IMPIANTO) ──
    private async void BtnEditAutoIncludeRtf_Click(object sender, RoutedEventArgs e)
    {
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
        if (sender is not Button btn || btn.Tag is not int parentId) return;

        await ApiClient.DeleteAsync($"/api/quotes/{_selectedQuoteId}/items/{parentId}");
        await ReloadServiceItems();
    }

    // ═══════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
