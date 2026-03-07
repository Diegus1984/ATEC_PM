using System.Globalization;
using System.Windows.Media;

namespace ATEC.PM.Client.Views;

public partial class MaterialCategoriesPage : Page
{
    private List<MaterialCategoryDto> _categories = new();
    private List<MarkupCoefficientDto> _markups = new();

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public MaterialCategoriesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadData();
    }

    private async Task LoadData()
    {
        try
        {
            // Carica markup (tutti, per la combo)
            string mkJson = await ApiClient.GetAsync("/api/markup");
            JsonDocument mkDoc = JsonDocument.Parse(mkJson);
            if (mkDoc.RootElement.GetProperty("success").GetBoolean())
                _markups = JsonSerializer.Deserialize<List<MarkupCoefficientDto>>(
                    mkDoc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Carica categorie
            string json = await ApiClient.GetAsync("/api/material-categories");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _categories = JsonSerializer.Deserialize<List<MaterialCategoryDto>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            RenderList();
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void RenderList()
    {
        pnlCategories.Children.Clear();
        txtCount.Text = $"{_categories.Count} categorie";

        foreach (MaterialCategoryDto cat in _categories.OrderBy(c => c.SortOrder))
            pnlCategories.Children.Add(BuildRow(cat));

        txtStatus.Text = $"{_categories.Count} categorie caricate";
    }

    private Border BuildRow(MaterialCategoryDto cat)
    {
        Border row = new()
        {
            Background = cat.IsActive ? Brushes.White : Brush("#F9FAFB"),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 0, 2)
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

        // Col 0: Nome
        TextBlock txtName = new()
        {
            Text = cat.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = cat.IsActive ? Brush("#1A1D26") : Brushes.Gray
        };
        Grid.SetColumn(txtName, 0);
        grid.Children.Add(txtName);

        // Col 1: Markup ComboBox
        ComboBox cmbMarkup = new()
        {
            Height = 24, Width = 130,
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"),
            VerticalAlignment = VerticalAlignment.Center
        };
        int selectedIdx = 0;
        int idx = 0;
        foreach (MarkupCoefficientDto mk in _markups.Where(m => m.CoefficientType == "MATERIAL").OrderBy(m => m.SortOrder))
        {
            cmbMarkup.Items.Add(new ComboBoxItem { Content = mk.Code, Tag = mk.Code });
            if (mk.Code == cat.MarkupCode) selectedIdx = idx;
            idx++;
        }
        cmbMarkup.SelectedIndex = selectedIdx;
        cmbMarkup.SelectionChanged += async (s, e) =>
        {
            string newCode = (cmbMarkup.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            if (newCode == cat.MarkupCode) return;
            await UpdateField(cat.Id, "markup_code", newCode);
            cat.MarkupCode = newCode;
            // Aggiorna K value display
            MarkupCoefficientDto? mk = _markups.FirstOrDefault(m => m.Code == newCode);
            if (mk != null) cat.MarkupValue = mk.MarkupValue;
            UpdateKDisplay(grid, cat);
        };
        Grid.SetColumn(cmbMarkup, 1);
        grid.Children.Add(cmbMarkup);

        // Col 2: K valore (read-only, calcolato)
        TextBlock txtK = new()
        {
            Text = cat.MarkupValue.ToString("F3", CultureInfo.InvariantCulture),
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("#4F6EF7"),
            Tag = "kvalue"
        };
        Grid.SetColumn(txtK, 2);
        grid.Children.Add(txtK);

        // Col 3: Sort order
        TextBox txtSort = new()
        {
            Text = cat.SortOrder.ToString(),
            FontSize = 12, Width = 40, Height = 24,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        txtSort.LostFocus += async (s, e) =>
        {
            if (!int.TryParse(txtSort.Text, out int val) || val == cat.SortOrder) return;
            await UpdateField(cat.Id, "sort_order", val.ToString());
            cat.SortOrder = val;
        };
        Grid.SetColumn(txtSort, 3);
        grid.Children.Add(txtSort);

        // Col 4: Elimina
        Button btnDel = new()
        {
            Content = "✕", Width = 24, Height = 24, FontSize = 11,
            Background = Brush("#EF44441A"), Foreground = Brush("#EF4444"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Elimina categoria"
        };
        btnDel.Click += async (s, e) =>
        {
            if (MessageBox.Show($"Eliminare \"{cat.Name}\"?",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                string result = await ApiClient.DeleteAsync($"/api/material-categories/{cat.Id}");
                JsonDocument doc = JsonDocument.Parse(result);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                    await LoadData();
                else
                    MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        };
        Grid.SetColumn(btnDel, 4);
        grid.Children.Add(btnDel);

        row.Child = grid;
        return row;
    }

    private void UpdateKDisplay(Grid grid, MaterialCategoryDto cat)
    {
        foreach (var child in grid.Children)
        {
            if (child is TextBlock tb && tb.Tag?.ToString() == "kvalue")
            {
                tb.Text = cat.MarkupValue.ToString("F3", CultureInfo.InvariantCulture);
                break;
            }
        }
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        MaterialCategoryDialog dlg = new(_markups) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) await LoadData();
    }

    private async Task UpdateField(int id, string field, string value)
    {
        try
        {
            string json = JsonSerializer.Serialize(new { field, value });
            await ApiClient.PatchAsync($"/api/material-categories/{id}/field", json);
        }
        catch { }
    }
}
