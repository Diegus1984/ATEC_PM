using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.Models;

namespace ATEC.PM.Client.Views;

public partial class OffersPage : Page
{
    private List<Offer> _allOffers = new();
    private Offer? _selectedOffer;

    public OffersPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadOffers();
    }

    // ═══════════════════════════════════════════════════════
    // LOAD & TREE
    // ═══════════════════════════════════════════════════════

    private async Task LoadOffers()
    {
        try
        {
            var json = await ApiClient.GetAsync("/api/offers");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _allOffers = JsonSerializer.Deserialize<List<Offer>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                BuildTree(_allOffers);
                txtStatus.Text = $"{_allOffers.Count} offerte";
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void BuildTree(List<Offer> offers)
    {
        // Salva stato espansione
        var expandedCustomers = new HashSet<string>();
        var expandedYears = new HashSet<string>();
        foreach (TreeViewItem custNode in treeOffers.Items)
        {
            if (custNode.IsExpanded) expandedCustomers.Add(custNode.Tag?.ToString() ?? "");
            foreach (TreeViewItem yearNode in custNode.Items)
                if (yearNode.IsExpanded) expandedYears.Add($"{custNode.Tag}|{yearNode.Tag}");
        }

        treeOffers.Items.Clear();

        // Filtra ricerca
        string search = txtSearch.Text.Trim().ToLowerInvariant();
        var filtered = offers.AsEnumerable();
        if (!string.IsNullOrEmpty(search))
            filtered = filtered.Where(o =>
                o.OfferCode.ToLowerInvariant().Contains(search) ||
                o.Title.ToLowerInvariant().Contains(search) ||
                o.CustomerName.ToLowerInvariant().Contains(search));

        // Raggruppa: Cliente → Anno → Offerte
        var byCustomer = filtered
            .GroupBy(o => o.CustomerName)
            .OrderBy(g => g.Key);

        foreach (var custGroup in byCustomer)
        {
            var custNode = new TreeViewItem
            {
                Tag = $"customer|{custGroup.Key}",
            };
            custNode.Header = BuildCustomerHeader(custGroup.Key, custGroup.Count());

            // Per anno
            var byYear = custGroup
                .GroupBy(o => o.CreatedAt.Year)
                .OrderByDescending(g => g.Key);

            foreach (var yearGroup in byYear)
            {
                var yearNode = new TreeViewItem
                {
                    Tag = $"year|{yearGroup.Key}",
                };
                yearNode.Header = BuildYearHeader(yearGroup.Key, yearGroup.Count());

                // Offerte raggruppate per codice (per avere revisioni insieme)
                var byCode = yearGroup
                    .GroupBy(o => o.OfferCode)
                    .OrderByDescending(g => g.Max(o => o.CreatedAt));

                foreach (var codeGroup in byCode)
                {
                    var sortedRevs = codeGroup.OrderByDescending(o => o.Revision).ToList();

                    if (sortedRevs.Count == 1)
                    {
                        var offer = sortedRevs[0];
                        var offerNode = new TreeViewItem
                        {
                            Tag = $"offer|{offer.Id}",
                        };
                        offerNode.Header = BuildOfferHeader(offer);
                        yearNode.Items.Add(offerNode);
                    }
                    else
                    {
                        // Nodo codice con revisioni come figli
                        var latest = sortedRevs[0];
                        var codeNode = new TreeViewItem
                        {
                            Tag = $"offergroup|{latest.OfferCode}",
                        };
                        codeNode.Header = BuildCodeGroupHeader(latest.OfferCode, sortedRevs.Count);

                        foreach (var rev in sortedRevs)
                        {
                            var revNode = new TreeViewItem
                            {
                                Tag = $"offer|{rev.Id}",
                            };
                            revNode.Header = BuildOfferHeader(rev);
                            codeNode.Items.Add(revNode);
                        }

                        codeNode.IsExpanded = true;
                        yearNode.Items.Add(codeNode);
                    }
                }

                // Ripristina espansione
                string yearKey = $"customer|{custGroup.Key}|year|{yearGroup.Key}";
                yearNode.IsExpanded = expandedYears.Contains(yearKey) || expandedYears.Count == 0;
                custNode.Items.Add(yearNode);
            }

            custNode.IsExpanded = expandedCustomers.Contains($"customer|{custGroup.Key}") || expandedCustomers.Count == 0;
            treeOffers.Items.Add(custNode);
        }

        // Aggiorna placeholder ricerca
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ═══════════════════════════════════════════════════════
    // TREE NODE HEADERS (styled)
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
            Text = $"{year}  ·  {count} offerte",
            FontSize = 13,
            Foreground = Brush("#6B7280"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private static StackPanel BuildCodeGroupHeader(string code, int revCount)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        sp.Children.Add(new TextBlock
        {
            Text = code,
            FontSize = 13,
            Foreground = Brush("#374151"),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        sp.Children.Add(new TextBlock
        {
            Text = $" ({revCount} revisioni)",
            FontSize = 12,
            Foreground = Brush("#9CA3AF"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });
        return sp;
    }

    private static Border BuildOfferHeader(Offer o)
    {
        // Colori per stato resi più morbidi
        (string bg, string accent, string fgTitle, string fgSub) = o.Status switch
        {
            "BOZZA" => ("#FFFFFF", "#D1D5DB", "#4B5563", "#9CA3AF"),
            "INVIATA" => ("#EFF6FF", "#3B82F6", "#1E40AF", "#60A5FA"),
            "ACCETTATA" => ("#F0FDF4", "#10B981", "#065F46", "#34D399"),
            "CONVERTITA" => ("#F0FDF4", "#10B981", "#065F46", "#34D399"),
            "RIFIUTATA" => ("#FEF2F2", "#EF4444", "#991B1B", "#F87171"),
            "PERSA" => ("#FEF2F2", "#EF4444", "#991B1B", "#F87171"),
            "SUPERATA" => ("#F9FAFB", "#E5E7EB", "#9CA3AF", "#D1D5DB"),
            _ => ("#FFFFFF", "#D1D5DB", "#374151", "#9CA3AF")
        };

        string label = o.Revision > 1 ? $"{o.OfferCode} R{o.Revision}" : o.OfferCode;
        string title = o.Title.Length > 24 ? o.Title[..24] + "…" : o.Title;

        var cardContainer = new Border
        {
            Background = Brush(bg),
            BorderBrush = Brush("#E5E7EB"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 3, 4, 3),
            Width = 220,
            ClipToBounds = true
        };

        var mainDock = new DockPanel();

        // Linea di accento colorata a sinistra (CORREZIONE DOCKPANEL)
        var accentLine = new Border
        {
            Width = 4,
            Background = Brush(accent)
        };
        DockPanel.SetDock(accentLine, Dock.Left);
        mainDock.Children.Add(accentLine);

        // CORREZIONE PADDING STACKPANEL (sostituito con Margin)
        var sp = new StackPanel
        {
            Margin = new Thickness(10, 8, 10, 8)
        };

        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(fgTitle)
        });
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
        if (treeOffers.SelectedItem is not TreeViewItem item) return;
        string tag = item.Tag?.ToString() ?? "";

        if (tag.StartsWith("offer|") && int.TryParse(tag.Split('|')[1], out int offerId))
        {
            await LoadOfferDetail(offerId);
        }
    }

    private async Task LoadOfferDetail(int offerId)
    {
        try
        {
            var json = await ApiClient.GetAsync($"/api/offers/{offerId}");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _selectedOffer = JsonSerializer.Deserialize<Offer>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (_selectedOffer == null) return;

            // Header
            string revStr = _selectedOffer.Revision > 1 ? $" R{_selectedOffer.Revision}" : "";
            txtSectionTitle.Text = $"{_selectedOffer.OfferCode}{revStr} — {_selectedOffer.Title}";

            // Info strip
            txtOfferStatus.Text = _selectedOffer.Status;
            SetStatusBadgeColors(_selectedOffer.Status);
            txtOfferCustomer.Text = _selectedOffer.CustomerName;
            txtOfferCreatedBy.Text = $"di {_selectedOffer.CreatedByName}";
            txtOfferDate.Text = _selectedOffer.CreatedAt.ToString("dd/MM/yyyy");

            // Converted link
            if (_selectedOffer.ConvertedProjectId.HasValue && !string.IsNullOrEmpty(_selectedOffer.ConvertedProjectCode))
            {
                txtConvertedLink.Text = $"→ Commessa {_selectedOffer.ConvertedProjectCode}";
                txtConvertedLink.Visibility = Visibility.Visible;
            }
            else
            {
                txtConvertedLink.Visibility = Visibility.Collapsed;
            }

            // Buttons
            UpdateActionButtons(_selectedOffer.Status);

            // Show detail, hide placeholder
            pnlPlaceholder.Visibility = Visibility.Collapsed;
            pnlOfferDetail.Visibility = Visibility.Visible;
            pnlActions.Visibility = Visibility.Visible;

            // Animate fade-in
            var sb = (Storyboard)FindResource("FadeIn");
            sb.Begin(this);

            // Load costing
            bool readOnly = _selectedOffer.Status is "CONVERTITA" or "SUPERATA" or "PERSA";
            costingControl.LoadForOffer(offerId, readOnly);
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
            "BOZZA" => ("#F3F4F6", "#374151"),
            "INVIATA" => ("#DBEAFE", "#1D4ED8"),
            "ACCETTATA" => ("#D1FAE5", "#059669"),
            "CONVERTITA" => ("#10B981", "#FFFFFF"),
            "RIFIUTATA" => ("#FEE2E2", "#DC2626"),
            "PERSA" => ("#FEE2E2", "#DC2626"),
            "SUPERATA" => ("#E5E7EB", "#6B7280"),
            _ => ("#F3F4F6", "#374151")
        };
        brdStatusBadge.Background = Brush(bg);
        txtOfferStatus.Foreground = Brush(fg);
    }

    private void UpdateActionButtons(string status)
    {
        bool isBozza = status == "BOZZA";
        bool isInviata = status == "INVIATA";
        bool isAccettata = status == "ACCETTATA";
        bool isEditable = isBozza || isInviata;

        btnSetInviata.Visibility = isBozza ? Visibility.Visible : Visibility.Collapsed;
        btnSetAccettata.Visibility = isInviata ? Visibility.Visible : Visibility.Collapsed;
        btnSetRifiutata.Visibility = isEditable ? Visibility.Visible : Visibility.Collapsed;
        btnSetPersa.Visibility = isEditable ? Visibility.Visible : Visibility.Collapsed;
        btnRevision.Visibility = isEditable || status == "RIFIUTATA" ? Visibility.Visible : Visibility.Collapsed;
        btnConvert.Visibility = isAccettata ? Visibility.Visible : Visibility.Collapsed;
        btnDelete.Visibility = status is "BOZZA" or "RIFIUTATA" or "PERSA" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ═══════════════════════════════════════════════════════
    // ACTIONS
    // ═══════════════════════════════════════════════════════

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewOfferDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            _ = LoadOffers();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadOffers();

    private void TxtSearch_Changed(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        BuildTree(_allOffers);
    }

    private async void BtnSetStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOffer == null || sender is not Button btn) return;
        string newStatus = btn.Tag?.ToString() ?? "";

        var confirm = MessageBox.Show($"Cambiare stato a {newStatus}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            string body = JsonSerializer.Serialize(new { _selectedOffer.Title, _selectedOffer.Description, Status = newStatus });
            var json = await ApiClient.PutAsync($"/api/offers/{_selectedOffer.Id}", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await LoadOffers();
                await LoadOfferDetail(_selectedOffer.Id);
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnRevision_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOffer == null) return;

        var confirm = MessageBox.Show(
            "Creare una nuova revisione?\nQuesta revisione verrà segnata come SUPERATA.",
            "Nuova Revisione", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var json = await ApiClient.PostAsync($"/api/offers/{_selectedOffer.Id}/revision", "{}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                int newId = doc.RootElement.GetProperty("data").GetInt32();
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Revisione creata",
                    "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadOffers();
                await LoadOfferDetail(newId);
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOffer == null) return;

        var dlg = new ConvertOfferDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string body = JsonSerializer.Serialize(new { PmId = dlg.SelectedPmId });
            var json = await ApiClient.PostAsync($"/api/offers/{_selectedOffer.Id}/convert", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Commessa creata",
                    "Conversione completata", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadOffers();
                await LoadOfferDetail(_selectedOffer.Id);
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOffer == null) return;

        var confirm = MessageBox.Show("Eliminare questa offerta?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var json = await ApiClient.DeleteAsync($"/api/offers/{_selectedOffer.Id}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _selectedOffer = null;
                pnlOfferDetail.Visibility = Visibility.Collapsed;
                pnlPlaceholder.Visibility = Visibility.Visible;
                pnlActions.Visibility = Visibility.Collapsed;
                txtSectionTitle.Text = "Seleziona un'offerta";
                await LoadOffers();
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}