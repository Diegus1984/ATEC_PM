using System.Globalization;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.UserControls;

public partial class ProjectCostingControl : UserControl
{
    private int _projectId;
    private ProjectCostingData _data = new();

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private static readonly Dictionary<string, string> GroupColors = new()
    {
        { "GESTIONE", "#2563EB" }, { "PRESCHIERAMENTO", "#7C3AED" },
        { "INSTALLAZIONE", "#D97706" }, { "OPZIONE", "#DC2626" }
    };

    public ProjectCostingControl()
    {
        InitializeComponent();
    }

    public void Load(int projectId)
    {
        _projectId = projectId;
        _ = LoadData();
    }

    private async Task LoadData()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/costing");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _data = System.Text.Json.JsonSerializer.Deserialize<ProjectCostingData>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            if (!_data.IsInitialized)
            {
                pnlInit.Visibility = Visibility.Visible;
                scrollContent.Visibility = Visibility.Collapsed;
                txtStatus.Text = "Non inizializzato";
            }
            else
            {
                pnlInit.Visibility = Visibility.Collapsed;
                scrollContent.Visibility = Visibility.Visible;
                ShowCurrentTab();
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async void BtnInit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/init", "{}");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadData();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void Tab_Changed(object sender, RoutedEventArgs e) => ShowCurrentTab();

    private void ShowCurrentTab()
    {
        if (pnlContent == null || !_data.IsInitialized) return;
        pnlContent.Children.Clear();
        if (tabRisorse.IsChecked == true) RenderRisorse();
        else if (tabMateriali.IsChecked == true) RenderMateriali();
        else if (tabRiepilogo.IsChecked == true) RenderRiepilogo();
    }

    // ══════════════════════════════════════════════════════════════
    // TAB RISORSE
    // ══════════════════════════════════════════════════════════════
    private void RenderRisorse()
    {
        string lastGroup = "";
        decimal grandTotalCost = 0;
        decimal grandTotalHours = 0;

        foreach (ProjectCostSectionDto sec in _data.CostSections.Where(s => s.IsEnabled).OrderBy(s => s.SortOrder))
        {
            // Header gruppo
            if (sec.GroupName != lastGroup)
            {
                string color = GroupColors.TryGetValue(sec.GroupName, out string? c) ? c : "#6B7280";
                pnlContent.Children.Add(MakeGroupHeader(sec.GroupName, color));
                lastGroup = sec.GroupName;
            }

            // Sezione espandibile
            pnlContent.Children.Add(MakeResourceSection(sec));
            grandTotalCost += sec.TotalCost;
            grandTotalHours += sec.TotalHours;
        }

        // Totale globale
        pnlContent.Children.Add(MakeTotalBar("TOTALE RISORSE", grandTotalHours, grandTotalCost));
        txtStatus.Text = $"{_data.CostSections.Count(s => s.IsEnabled)} sezioni — {grandTotalHours:N1} ore — {grandTotalCost:N2} €";
    }

    private Border MakeGroupHeader(string name, string color)
    {
        Border header = new()
        {
            Background = Brush(color),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 12, 0, 4)
        };
        header.Child = new TextBlock
        {
            Text = $"  {name}",
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        return header;
    }

    private Border MakeResourceSection(ProjectCostSectionDto sec)
    {
        Border card = new()
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(0)
        };

        StackPanel sp = new();

        // Header sezione
        DockPanel headerDp = new()
        {
            Background = Brush("#F9FAFB"),
            LastChildFill = true
        };

        // Bottone + aggiungi risorsa (a destra)
        Button btnAdd = new()
        {
            Content = "+ Risorsa",
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 11,
            Background = Brush("#4F6EF7"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 6, 8, 6)
        };
        DockPanel.SetDock(btnAdd, Dock.Right);
        int sectionId = sec.Id;
        string sectionType = sec.SectionType;
        btnAdd.Click += async (s, e) => await AddResource(sectionId, sectionType);
        headerDp.Children.Add(btnAdd);

        // Tipo badge
        string typeColor = sec.SectionType == "DA_CLIENTE" ? "#D97706" : "#059669";
        string typeLabel = sec.SectionType == "DA_CLIENTE" ? "CLIENTE" : "SEDE";
        Border typeBadge = new()
        {
            Background = Brush(typeColor + "20"),
            Padding = new Thickness(4, 2, 4, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        typeBadge.Child = new TextBlock { Text = typeLabel, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brush(typeColor) };
        DockPanel.SetDock(typeBadge, Dock.Right);
        headerDp.Children.Add(typeBadge);

        // Totali
        TextBlock txtTotals = new()
        {
            Text = $"{sec.TotalHours:N1} h  |  {sec.TotalCost:N2} €",
            FontSize = 11,
            Foreground = Brush("#4F6EF7"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        DockPanel.SetDock(txtTotals, Dock.Right);
        headerDp.Children.Add(txtTotals);

        // Nome sezione
        TextBlock txtName = new()
        {
            Text = sec.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1A1D26"),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 8, 0, 8)
        };
        headerDp.Children.Add(txtName);

        sp.Children.Add(headerDp);

        // Righe risorsa
        if (sec.Resources.Any())
        {
            // Header colonne
            sp.Children.Add(MakeResourceColumnHeader(sec.SectionType));

            foreach (ProjectCostResourceDto res in sec.Resources.OrderBy(r => r.SortOrder))
                sp.Children.Add(MakeResourceRow(res, sec.SectionType));
        }

        card.Child = sp;
        return card;
    }

    private Grid MakeResourceColumnHeader(string sectionType)
    {
        Grid g = new() { Margin = new Thickness(12, 4, 12, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });   // Risorsa
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });    // GG
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });    // Ore/g
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });    // Tot Ore
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });    // €/H
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });    // Totale €
        if (sectionType == "DA_CLIENTE")
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // Viaggi
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Vitto+Hotel
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Indennità
        }
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });    // Del

        string[] headers = sectionType == "DA_CLIENTE"
            ? new[] { "RISORSA", "GG", "ORE/G", "TOT ORE", "€/H", "TOT €", "VIAGGI €", "VITTO €", "INDENN. €", "" }
            : new[] { "RISORSA", "GG", "ORE/G", "TOT ORE", "€/H", "TOT €", "" };

        for (int i = 0; i < headers.Length; i++)
        {
            TextBlock tb = new() { Text = headers[i], FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brush("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb, i);
            g.Children.Add(tb);
        }
        return g;
    }

    private Grid MakeResourceRow(ProjectCostResourceDto res, string sectionType)
    {
        Grid g = new() { Margin = new Thickness(12, 1, 12, 1), Height = 28 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        if (sectionType == "DA_CLIENTE")
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        }
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        int col = 0;
        AddLabel(g, col++, res.ResourceName, 12);
        AddLabel(g, col++, res.WorkDays > 0 ? res.WorkDays.ToString("F0") : "", 11);
        AddLabel(g, col++, res.HoursPerDay > 0 ? res.HoursPerDay.ToString("F0") : "", 11);
        AddLabel(g, col++, res.TotalHours > 0 ? res.TotalHours.ToString("F1") : "", 11, FontWeights.SemiBold);
        AddLabel(g, col++, res.HourlyCost > 0 ? res.HourlyCost.ToString("F2") : "", 11);
        AddLabel(g, col++, res.TotalCost > 0 ? res.TotalCost.ToString("N2") : "", 11, FontWeights.SemiBold, "#4F6EF7");

        if (sectionType == "DA_CLIENTE")
        {
            AddLabel(g, col++, res.TravelTotal > 0 ? res.TravelTotal.ToString("N0") : "", 11);
            AddLabel(g, col++, res.AccommodationTotal > 0 ? res.AccommodationTotal.ToString("N0") : "", 11);
            AddLabel(g, col++, res.AllowanceTotal > 0 ? res.AllowanceTotal.ToString("N0") : "", 11);
        }

        // Bottone elimina
        Button btnDel = new()
        {
            Content = "✕",
            Width = 20,
            Height = 20,
            FontSize = 9,
            Background = Brush("#EF44441A"),
            Foreground = Brush("#EF4444"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        int resId = res.Id;
        btnDel.Click += async (s, e) => await DeleteResource(resId);
        Grid.SetColumn(btnDel, col);
        g.Children.Add(btnDel);

        return g;
    }

    private void AddLabel(Grid g, int col, string text, double fontSize, FontWeight? weight = null, string? color = null)
    {
        TextBlock tb = new()
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = color != null ? Brush(color) : Brush("#374151"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    // ══════════════════════════════════════════════════════════════
    // TAB MATERIALI
    // ══════════════════════════════════════════════════════════════
    private void RenderMateriali()
    {
        decimal grandTotalCost = 0;
        decimal grandTotalSale = 0;

        foreach (ProjectMaterialSectionDto sec in _data.MaterialSections.Where(s => s.IsEnabled).OrderBy(s => s.SortOrder))
        {
            pnlContent.Children.Add(MakeMaterialSection(sec));
            grandTotalCost += sec.TotalCost;
            grandTotalSale += sec.TotalSale;
        }

        // Totale
        Border bar = new()
        {
            Background = Brush("#1A1D26"),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 8, 0, 0)
        };
        StackPanel sp = new() { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = "TOTALE MATERIALI", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Width = 200 });
        sp.Children.Add(new TextBlock { Text = $"Costo: {grandTotalCost:N2} €", FontSize = 13, Foreground = Brush("#9CA3AF"), Margin = new Thickness(20, 0, 0, 0) });
        sp.Children.Add(new TextBlock { Text = $"Vendita: {grandTotalSale:N2} €", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush("#4F6EF7"), Margin = new Thickness(20, 0, 0, 0) });
        bar.Child = sp;
        pnlContent.Children.Add(bar);

        txtStatus.Text = $"{_data.MaterialSections.Count(s => s.IsEnabled)} categorie — Costo {grandTotalCost:N2} € — Vendita {grandTotalSale:N2} €";
    }

    private Border MakeMaterialSection(ProjectMaterialSectionDto sec)
    {
        Border card = new()
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 4)
        };

        StackPanel sp = new();

        // Header
        DockPanel headerDp = new() { Background = Brush("#F9FAFB") };

        Button btnAdd = new()
        {
            Content = "+ Voce",
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 11,
            Background = Brush("#4F6EF7"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 6, 8, 6)
        };
        DockPanel.SetDock(btnAdd, Dock.Right);
        int sectionId = sec.Id;
        btnAdd.Click += async (s, e) => await AddMaterialItem(sectionId);
        headerDp.Children.Add(btnAdd);

        // K badge
        Border kBadge = new()
        {
            Background = Brush("#05966920"),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        kBadge.Child = new TextBlock { Text = $"K {sec.MarkupValue:F2}", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brush("#059669") };
        DockPanel.SetDock(kBadge, Dock.Right);
        headerDp.Children.Add(kBadge);

        // Totali
        TextBlock txtTotals = new()
        {
            Text = $"Costo {sec.TotalCost:N2} €  →  Vendita {sec.TotalSale:N2} €",
            FontSize = 11,
            Foreground = Brush("#4F6EF7"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        DockPanel.SetDock(txtTotals, Dock.Right);
        headerDp.Children.Add(txtTotals);

        TextBlock txtName = new()
        {
            Text = sec.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1A1D26"),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 8, 0, 8)
        };
        headerDp.Children.Add(txtName);

        sp.Children.Add(headerDp);

        // Righe materiale
        if (sec.Items.Any())
        {
            // Header colonne
            Grid hdr = new() { Margin = new Thickness(12, 4, 12, 2) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            AddLabel(hdr, 0, "DESCRIZIONE", 9, FontWeights.SemiBold, "#9CA3AF");
            AddLabel(hdr, 1, "QTÀ", 9, FontWeights.SemiBold, "#9CA3AF");
            AddLabel(hdr, 2, "COSTO UNIT.", 9, FontWeights.SemiBold, "#9CA3AF");
            AddLabel(hdr, 3, "TOTALE", 9, FontWeights.SemiBold, "#9CA3AF");
            sp.Children.Add(hdr);

            foreach (ProjectMaterialItemDto item in sec.Items.OrderBy(i => i.SortOrder))
            {
                Grid row = new() { Margin = new Thickness(12, 1, 12, 1), Height = 26 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

                AddLabel(row, 0, item.Description, 12);
                AddLabel(row, 1, item.Quantity.ToString("F0"), 11);
                AddLabel(row, 2, item.UnitCost.ToString("N2") + " €", 11);
                AddLabel(row, 3, item.TotalCost.ToString("N2") + " €", 11, FontWeights.SemiBold, "#4F6EF7");

                Button btnDel = new()
                {
                    Content = "✕",
                    Width = 20,
                    Height = 20,
                    FontSize = 9,
                    Background = Brush("#EF44441A"),
                    Foreground = Brush("#EF4444"),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                int itemId = item.Id;
                btnDel.Click += async (s, e) => await DeleteMaterialItem(itemId);
                Grid.SetColumn(btnDel, 4);
                row.Children.Add(btnDel);

                sp.Children.Add(row);
            }
        }

        card.Child = sp;
        return card;
    }

    // ══════════════════════════════════════════════════════════════
    // TAB RIEPILOGO E PREZZI
    // ══════════════════════════════════════════════════════════════
    private void RenderRiepilogo()
    {
        // Calcoli
        decimal totalResourceCost = _data.CostSections.Where(s => s.IsEnabled).Sum(s => s.TotalCost);
        decimal totalResourceTravel = _data.CostSections.Where(s => s.IsEnabled).Sum(s => s.TotalTravel);
        decimal totalMaterialCost = _data.MaterialSections.Where(s => s.IsEnabled).Sum(s => s.TotalCost);
        decimal totalMaterialSale = _data.MaterialSections.Where(s => s.IsEnabled).Sum(s => s.TotalSale);

        // Vendita risorse: applica K dai markup locali
        decimal totalResourceSale = 0;
        foreach (var sec in _data.CostSections.Where(s => s.IsEnabled))
        {
            foreach (var res in sec.Resources)
            {
                // Trova K dalla lista markup
                decimal k = 1.45m; // default
                var mk = _data.Markups.FirstOrDefault(m => m.CoefficientType == "RESOURCE");
                if (mk != null) k = mk.MarkupValue;
                totalResourceSale += res.TotalCost * k;
            }
        }

        // Trasferte con K
        decimal kTrasferta = _data.Markups.FirstOrDefault(m => m.OriginalCode == "K9_TRASFERTA")?.MarkupValue ?? 1.1m;
        decimal totalTravelSale = totalResourceTravel * kTrasferta;

        decimal totalCost = totalResourceCost + totalResourceTravel + totalMaterialCost;
        decimal netPrice = totalResourceSale + totalTravelSale + totalMaterialSale;

        // Pricing
        ProjectPricingDto p = _data.Pricing;
        decimal structureCosts = netPrice * p.StructureCostsPct;
        decimal contingency = netPrice * p.ContingencyPct;
        decimal riskWarranty = netPrice * p.RiskWarrantyPct;
        decimal offerPrice = netPrice + structureCosts + contingency + riskWarranty;
        decimal negotiationMargin = netPrice * p.NegotiationMarginPct;
        decimal finalPrice = offerPrice + negotiationMargin;

        // Render
        AddSummaryRow("COSTO RISORSE", totalResourceCost, null);
        AddSummaryRow("COSTO TRASFERTE", totalResourceTravel, null);
        AddSummaryRow("COSTO MATERIALI", totalMaterialCost, null);
        AddSeparator();
        AddSummaryRow("TOTALE COSTI", totalCost, null, true);

        pnlContent.Children.Add(new Border { Height = 16 });

        AddSummaryRow("VENDITA RISORSE (con K)", null, totalResourceSale);
        AddSummaryRow("VENDITA TRASFERTE (con K)", null, totalTravelSale);
        AddSummaryRow("VENDITA MATERIALI (con K)", null, totalMaterialSale);
        AddSeparator();
        AddSummaryRow("NET PRICE", null, netPrice, true);

        pnlContent.Children.Add(new Border { Height = 16 });

        AddSummaryRow($"Costi fissi struttura ({p.StructureCostsPct * 100:F1}%)", null, structureCosts);
        AddSummaryRow($"Contingency ({p.ContingencyPct * 100:F1}%)", null, contingency);
        AddSummaryRow($"Rischi & Garanzie ({p.RiskWarrantyPct * 100:F1}%)", null, riskWarranty);
        AddSeparator();
        AddSummaryRow("OFFER PRICE", null, offerPrice, true);

        pnlContent.Children.Add(new Border { Height = 8 });
        AddSummaryRow($"Margine trattativa ({p.NegotiationMarginPct * 100:F1}%)", null, negotiationMargin);
        AddSeparator();
        AddSummaryRow("FINAL OFFER PRICE", null, finalPrice, true, "#4F6EF7");

        decimal margin = totalCost > 0 ? (finalPrice - totalCost) / totalCost * 100 : 0;
        txtStatus.Text = $"Costo {totalCost:N2} € → Prezzo finale {finalPrice:N2} € — Margine {margin:N1}%";
    }

    private void AddSummaryRow(string label, decimal? costValue, decimal? saleValue, bool bold = false, string? color = null)
    {
        Grid g = new() { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        FontWeight fw = bold ? FontWeights.Bold : FontWeights.Normal;
        double fs = bold ? 14 : 13;
        string fg = color ?? "#1A1D26";

        TextBlock tbLabel = new() { Text = label, FontSize = fs, FontWeight = fw, Foreground = Brush(fg), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tbLabel, 0);
        g.Children.Add(tbLabel);

        if (costValue.HasValue)
        {
            TextBlock tbCost = new() { Text = $"{costValue.Value:N2} €", FontSize = fs, FontWeight = fw, Foreground = Brush("#DC2626"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tbCost, 1);
            g.Children.Add(tbCost);
        }

        if (saleValue.HasValue)
        {
            TextBlock tbSale = new() { Text = $"{saleValue.Value:N2} €", FontSize = fs, FontWeight = fw, Foreground = Brush(color ?? "#059669"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tbSale, 2);
            g.Children.Add(tbSale);
        }

        pnlContent.Children.Add(g);
    }

    private void AddSeparator()
    {
        pnlContent.Children.Add(new Border { Height = 1, Background = Brush("#E4E7EC"), Margin = new Thickness(0, 4, 0, 4) });
    }

    private Border MakeTotalBar(string label, decimal hours, decimal cost)
    {
        Border bar = new()
        {
            Background = Brush("#1A1D26"),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 8, 0, 0)
        };
        StackPanel sp = new() { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Width = 200 });
        sp.Children.Add(new TextBlock { Text = $"{hours:N1} ore", FontSize = 13, Foreground = Brush("#9CA3AF"), Margin = new Thickness(20, 0, 0, 0) });
        sp.Children.Add(new TextBlock { Text = $"{cost:N2} €", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush("#4F6EF7"), Margin = new Thickness(20, 0, 0, 0) });
        bar.Child = sp;
        return bar;
    }

    // ══════════════════════════════════════════════════════════════
    // CRUD HELPERS
    // ══════════════════════════════════════════════════════════════

    private async Task AddResource(int sectionId, string sectionType)
    {
        // Dialog semplice per aggiungere risorsa
        var dlg = new CostResourceDialog(_projectId, sectionId, sectionType) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) await LoadData();
    }

    private async Task DeleteResource(int resourceId)
    {
        if (MessageBox.Show("Eliminare questa risorsa?", "Conferma", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            await ApiClient.DeleteAsync($"/api/projects/{_projectId}/costing/resources/{resourceId}");
            await LoadData();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async Task AddMaterialItem(int sectionId)
    {
        var dlg = new CostMaterialItemDialog(_projectId, sectionId) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) await LoadData();
    }

    private async Task DeleteMaterialItem(int itemId)
    {
        if (MessageBox.Show("Eliminare questa voce?", "Conferma", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            await ApiClient.DeleteAsync($"/api/projects/{_projectId}/costing/material-items/{itemId}");
            await LoadData();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }
}
