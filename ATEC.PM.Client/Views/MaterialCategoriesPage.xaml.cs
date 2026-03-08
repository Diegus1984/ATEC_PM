using System.Globalization;
using System.Windows.Media;

namespace ATEC.PM.Client.Views;

public partial class MaterialCategoriesPage : Page
{
    private List<MaterialCategoryDto> _categories = new();

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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
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

        // Col 1: K Materiale (editabile)
        TextBox txtKMat = new()
        {
            Text = cat.DefaultMarkup.ToString("F3", CultureInfo.InvariantCulture),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#4F6EF7"),
            Width = 60, Height = 24,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        int catId = cat.Id;
        txtKMat.LostFocus += async (s, e) =>
        {
            if (decimal.TryParse(txtKMat.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val) && val != cat.DefaultMarkup)
            {
                await UpdateField(catId, "default_markup", val.ToString(CultureInfo.InvariantCulture));
                cat.DefaultMarkup = val;
            }
        };
        txtKMat.GotFocus += (s, e) => txtKMat.SelectAll();
        Grid.SetColumn(txtKMat, 1);
        grid.Children.Add(txtKMat);

        // Col 2: K Provvigione (editabile)
        TextBox txtKComm = new()
        {
            Text = cat.DefaultCommissionMarkup.ToString("F3", CultureInfo.InvariantCulture),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#059669"),
            Width = 60, Height = 24,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        txtKComm.LostFocus += async (s, e) =>
        {
            if (decimal.TryParse(txtKComm.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val) && val != cat.DefaultCommissionMarkup)
            {
                await UpdateField(catId, "default_commission_markup", val.ToString(CultureInfo.InvariantCulture));
                cat.DefaultCommissionMarkup = val;
            }
        };
        txtKComm.GotFocus += (s, e) => txtKComm.SelectAll();
        Grid.SetColumn(txtKComm, 2);
        grid.Children.Add(txtKComm);

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
            await UpdateField(catId, "sort_order", val.ToString());
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

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        MaterialCategoryDialog dlg = new() { Owner = Window.GetWindow(this) };
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
