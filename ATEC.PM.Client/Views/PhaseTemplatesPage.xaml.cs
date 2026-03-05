using System.Globalization;
using System.Windows.Media;

namespace ATEC.PM.Client.Views;

public partial class PhaseTemplatesPage : Page
{
    private List<PhaseTemplateDto> _templates = new();
    private List<DepartmentDto> _departments = new();

    private static readonly Dictionary<string, string> DeptColors = new()
    {
        { "ELE", "#2563EB" }, { "MEC", "#059669" }, { "PLC", "#7C3AED" },
        { "ROB", "#DC2626" }, { "UTC", "#D97706" }, { "ACQ", "#0891B2" },
        { "AMM", "#BE185D" }, { "",    "#6B7280" }
    };

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public PhaseTemplatesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadData();
    }

    private async Task LoadData()
    {
        await LoadDepartments();
        await LoadTemplates();
    }

    private async Task LoadDepartments()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/departments");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                _departments = JsonSerializer.Deserialize<List<DepartmentDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { }
    }

    private async Task LoadTemplates()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/phases/templates");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _templates = JsonSerializer.Deserialize<List<PhaseTemplateDto>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            RenderList();
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void RenderList()
    {
        pnlTemplates.Children.Clear();
        int defaultCount = _templates.Count(t => t.IsDefault);
        txtCount.Text = $"{_templates.Count} fasi totali — {defaultCount} default";

        string lastCategory = "";
        foreach (PhaseTemplateDto t in _templates.OrderBy(t => t.SortOrder))
        {
            string cat = string.IsNullOrEmpty(t.DepartmentCode) ? "TRASVERSALE" : t.DepartmentCode;

            // Header reparto
            if (cat != lastCategory)
            {
                string deptColor = DeptColors.TryGetValue(t.DepartmentCode ?? "", out string? c) ? c : "#6B7280";
                Border header = new()
                {
                    Background = Brush(deptColor),
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(0, 10, 0, 4)
                };
                header.Child = new TextBlock
                {
                    Text = $"  {cat}",
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                };
                pnlTemplates.Children.Add(header);
                lastCategory = cat;
            }

            pnlTemplates.Children.Add(BuildRow(t));
        }

        txtStatus.Text = $"{_templates.Count} template caricati";
    }

    private Border BuildRow(PhaseTemplateDto t)
    {
        Border row = new()
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 0, 2)
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        // Checkbox default
        CheckBox chkDefault = new()
        {
            IsChecked = t.IsDefault,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = t
        };
        chkDefault.Click += async (s, e) =>
        {
            bool newVal = chkDefault.IsChecked == true;
            await UpdateTemplateField(t.Id, "is_default", newVal ? "1" : "0");
            t.IsDefault = newVal;
            // Aggiorna conteggio
            int defaultCount = _templates.Count(x => x.IsDefault);
            txtCount.Text = $"{_templates.Count} fasi totali — {defaultCount} default";
        };
        Grid.SetColumn(chkDefault, 0);
        grid.Children.Add(chkDefault);

        // Nome
        TextBlock txtName = new()
        {
            Text = t.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#1A1D26")
        };
        Grid.SetColumn(txtName, 1);
        grid.Children.Add(txtName);

        // Reparto
        string deptColor = DeptColors.TryGetValue(t.DepartmentCode ?? "", out string? dc) ? dc : "#6B7280";
        string deptLabel = string.IsNullOrEmpty(t.DepartmentCode) ? "TRASV." : t.DepartmentCode;
        Border deptBadge = new()
        {
            Background = Brush(deptColor + "20"),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        deptBadge.Child = new TextBlock
        {
            Text = deptLabel,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(deptColor)
        };
        Grid.SetColumn(deptBadge, 2);
        grid.Children.Add(deptBadge);

        // Categoria
        TextBlock txtCat = new()
        {
            Text = t.Category,
            FontSize = 11,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(txtCat, 3);
        grid.Children.Add(txtCat);

        // Sort order (editabile)
        TextBox txtSort = new()
        {
            Text = t.SortOrder.ToString(),
            FontSize = 12,
            Width = 50,
            Height = 24,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = t
        };
        txtSort.LostFocus += async (s, e) =>
        {
            if (!int.TryParse(txtSort.Text, out int newSort)) return;
            if (newSort == t.SortOrder) return;
            await UpdateTemplateField(t.Id, "sort_order", newSort.ToString());
            t.SortOrder = newSort;
        };
        Grid.SetColumn(txtSort, 4);
        grid.Children.Add(txtSort);

        // Bottone elimina
        Button btnDel = new()
        {
            Content = "✕",
            Width = 24,
            Height = 24,
            FontSize = 11,
            Background = Brush("#EF44441A"),
            Foreground = Brush("#EF4444"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Elimina template"
        };
        btnDel.Click += async (s, e) =>
        {
            if (MessageBox.Show($"Eliminare il template \"{t.Name}\"?\nLe fasi già create nelle commesse non verranno toccate.",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            try
            {
                string result = await ApiClient.DeleteAsync($"/api/phases/templates/{t.Id}");
                JsonDocument doc = JsonDocument.Parse(result);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                    await LoadTemplates();
                else
                    MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        };
        Grid.SetColumn(btnDel, 5);
        grid.Children.Add(btnDel);

        row.Child = grid;
        return row;
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PhaseTemplateDialog(_departments) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await LoadTemplates();
    }

    private async Task UpdateTemplateField(int templateId, string field, string value)
    {
        try
        {
            string jsonBody = JsonSerializer.Serialize(new { field, value });
            await ApiClient.PatchAsync($"/api/phases/templates/{templateId}/field", jsonBody);
        }
        catch { }
    }
}
