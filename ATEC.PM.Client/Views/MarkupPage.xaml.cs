using System.Globalization;
using System.Windows.Media;

namespace ATEC.PM.Client.Views;

public partial class MarkupPage : Page
{
    private List<MarkupCoefficientDto> _items = new();

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public MarkupPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadData();
    }

    private async Task LoadData()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/markup");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _items = JsonSerializer.Deserialize<List<MarkupCoefficientDto>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            RenderList();
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void RenderList()
    {
        pnlMarkup.Children.Clear();
        int matCount = _items.Count(i => i.CoefficientType == "MATERIAL");
        int resCount = _items.Count(i => i.CoefficientType == "RESOURCE");
        txtCount.Text = $"{_items.Count} coefficienti — {matCount} materiali, {resCount} risorse";

        // Gruppo MATERIAL
        AddGroupHeader("MATERIALI", "#059669", matCount);
        foreach (MarkupCoefficientDto item in _items.Where(i => i.CoefficientType == "MATERIAL").OrderBy(i => i.SortOrder))
            pnlMarkup.Children.Add(BuildRow(item));

        // Gruppo RESOURCE
        AddGroupHeader("RISORSE", "#2563EB", resCount);
        foreach (MarkupCoefficientDto item in _items.Where(i => i.CoefficientType == "RESOURCE").OrderBy(i => i.SortOrder))
            pnlMarkup.Children.Add(BuildRow(item));

        txtStatus.Text = $"{_items.Count} coefficienti caricati";
    }

    private void AddGroupHeader(string label, string color, int count)
    {
        Border header = new()
        {
            Background = Brush(color),
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 10, 0, 4)
        };
        header.Child = new TextBlock
        {
            Text = $"  {label}  —  {count} voci",
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };
        pnlMarkup.Children.Add(header);
    }

    private Border BuildRow(MarkupCoefficientDto item)
    {
        Border row = new()
        {
            Background = item.IsActive ? Brushes.White : Brush("#F9FAFB"),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 0, 2)
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

        // Codice (badge)
        string badgeColor = item.CoefficientType == "RESOURCE" ? "#2563EB" : "#059669";
        Border codeBadge = new()
        {
            Background = Brush(badgeColor + "20"),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        codeBadge.Child = new TextBlock
        {
            Text = item.Code,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(badgeColor)
        };
        Grid.SetColumn(codeBadge, 0);
        grid.Children.Add(codeBadge);

        // Descrizione
        TextBlock txtDesc = new()
        {
            Text = item.Description,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = item.IsActive ? Brush("#1A1D26") : Brushes.Gray
        };
        Grid.SetColumn(txtDesc, 1);
        grid.Children.Add(txtDesc);

        // Tipo
        string typeLabel = item.CoefficientType == "RESOURCE" ? "Risorsa" : "Materiale";
        TextBlock txtType = new()
        {
            Text = typeLabel,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Gray
        };
        Grid.SetColumn(txtType, 2);
        grid.Children.Add(txtType);

        // K Valore (editabile)
        TextBox txtK = new()
        {
            Text = item.MarkupValue.ToString("F3", CultureInfo.InvariantCulture),
            FontSize = 12, Width = 65, Height = 24,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        txtK.LostFocus += async (s, e) =>
        {
            if (!decimal.TryParse(txtK.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)) return;
            if (val == item.MarkupValue) return;
            await UpdateField(item.Id, "markup_value", val.ToString(CultureInfo.InvariantCulture));
            item.MarkupValue = val;
            UpdateSaleDisplay(item, row);
        };
        Grid.SetColumn(txtK, 3);
        grid.Children.Add(txtK);

        // Costo/H (editabile, solo per RESOURCE)
        TextBox txtCost = new()
        {
            Text = item.HourlyCost?.ToString("F2", CultureInfo.InvariantCulture) ?? "",
            FontSize = 12, Width = 65, Height = 24,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = item.CoefficientType == "RESOURCE"
        };
        if (item.CoefficientType != "RESOURCE")
            txtCost.Background = Brush("#F9FAFB");
        txtCost.LostFocus += async (s, e) =>
        {
            if (!decimal.TryParse(txtCost.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)) return;
            if (val == item.HourlyCost) return;
            await UpdateField(item.Id, "hourly_cost", val.ToString(CultureInfo.InvariantCulture));
            item.HourlyCost = val;
            UpdateSaleDisplay(item, row);
        };
        Grid.SetColumn(txtCost, 4);
        grid.Children.Add(txtCost);

        // Vendita/H calcolata (solo RESOURCE)
        decimal salePrice = (item.HourlyCost ?? 0) * item.MarkupValue;
        TextBlock txtSale = new()
        {
            Text = item.CoefficientType == "RESOURCE" ? salePrice.ToString("F2") : "",
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("#4F6EF7"),
            Tag = item
        };
        Grid.SetColumn(txtSale, 5);
        grid.Children.Add(txtSale);

        // Sort order (editabile)
        TextBox txtSort = new()
        {
            Text = item.SortOrder.ToString(),
            FontSize = 12, Width = 40, Height = 24,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        txtSort.LostFocus += async (s, e) =>
        {
            if (!int.TryParse(txtSort.Text, out int val)) return;
            if (val == item.SortOrder) return;
            await UpdateField(item.Id, "sort_order", val.ToString());
            item.SortOrder = val;
        };
        Grid.SetColumn(txtSort, 6);
        grid.Children.Add(txtSort);

        // Bottone elimina
        Button btnDel = new()
        {
            Content = "✕", Width = 24, Height = 24, FontSize = 11,
            Background = Brush("#EF44441A"), Foreground = Brush("#EF4444"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Elimina coefficiente"
        };
        btnDel.Click += async (s, e) =>
        {
            if (MessageBox.Show($"Eliminare \"{item.Code} — {item.Description}\"?",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                string result = await ApiClient.DeleteAsync($"/api/markup/{item.Id}");
                JsonDocument doc = JsonDocument.Parse(result);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                    await LoadData();
                else
                    MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        };
        Grid.SetColumn(btnDel, 7);
        grid.Children.Add(btnDel);

        row.Child = grid;
        return row;
    }

    private void UpdateSaleDisplay(MarkupCoefficientDto item, Border row)
    {
        if (item.CoefficientType != "RESOURCE" || row.Child is not Grid grid) return;
        foreach (var child in grid.Children)
        {
            if (child is TextBlock tb && tb.Tag == item)
            {
                decimal sale = (item.HourlyCost ?? 0) * item.MarkupValue;
                tb.Text = sale.ToString("F2");
                break;
            }
        }
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        MarkupDialog dlg = new() { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) await LoadData();
    }

    private async Task UpdateField(int id, string field, string value)
    {
        try
        {
            string jsonBody = JsonSerializer.Serialize(new { field, value });
            await ApiClient.PatchAsync($"/api/markup/{id}/field", jsonBody);
        }
        catch { }
    }
}
