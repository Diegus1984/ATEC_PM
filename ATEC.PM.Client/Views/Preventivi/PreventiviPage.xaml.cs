using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Preventivi;

public partial class PreventiviPage : Page
{
    private List<QuoteDto> _allQuotes = new();
    private int _selectedQuoteId;

    public PreventiviPage()
    {
        InitializeComponent();
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

                foreach (var quote in yearGroup.OrderByDescending(q => q.CreatedAt))
                {
                    var quoteNode = new TreeViewItem
                    {
                        Tag = $"quote|{quote.Id}",
                    };
                    quoteNode.Header = BuildQuoteHeader(quote);
                    yearNode.Items.Add(quoteNode);
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

    private static Border BuildQuoteHeader(QuoteDto q)
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
            Width = 200,
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

        mainDock.Children.Add(sp);
        cardContainer.Child = mainDock;

        return cardContainer;
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

            // Header
            txtSectionTitle.Text = $"{quote.QuoteNumber} \u2014 {quote.Title}";

            // Status badge
            txtQuoteStatus.Text = quote.Status.ToUpperInvariant();
            SetStatusBadgeColors(quote.Status);

            // Info
            txtQuoteCustomer.Text = quote.CustomerName;
            txtQuoteCreatedBy.Text = $"di {quote.CreatedByName}";
            txtQuoteDate.Text = quote.CreatedAt.ToString("dd/MM/yyyy");

            // Type badge
            bool isPlant = quote.QuoteType == "IMPIANTO";

            // Info strip badges
            brdTypeBadge.Background = Brush(isPlant ? "#FFF7ED" : "#F0FDF4");
            txtQuoteType.Text = isPlant ? "IMPIANTO" : "SERVIZIO";
            txtQuoteType.Foreground = Brush(isPlant ? "#EA580C" : "#059669");

            // Action buttons visibility
            UpdateActionButtons(quote.Status, quote.QuoteType);

            // Show IMPIANTO (costing) or SERVICE (catalogo) panel
            costingControl.Visibility = isPlant ? Visibility.Visible : Visibility.Collapsed;
            pnlServiceCatalog.Visibility = isPlant ? Visibility.Collapsed : Visibility.Visible;

            // Show detail, hide placeholder
            pnlPlaceholder.Visibility = Visibility.Collapsed;
            pnlQuoteDetail.Visibility = Visibility.Visible;
            pnlActions.Visibility = Visibility.Visible;

            // Animate
            var sb = (Storyboard)FindResource("FadeIn");
            sb.Begin(this);

            // Load costing for IMPIANTO type
            if (isPlant)
            {
                bool readOnly = quote.Status is "converted" or "rejected" or "expired";
                costingControl.LoadForPreventivo(quoteId, readOnly);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
        }
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

            var tempPath = Path.Combine(Path.GetTempPath(), $"preventivo_{_selectedQuoteId}.pdf");
            File.WriteAllBytes(tempPath, bytes);
            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
