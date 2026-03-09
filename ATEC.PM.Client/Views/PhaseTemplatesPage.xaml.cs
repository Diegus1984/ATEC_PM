using System.Globalization;
using System.Windows.Media;

namespace ATEC.PM.Client.Views;

public partial class PhaseTemplatesPage : Page
{
    private List<PhaseTemplateDto> _templates = new();
    private List<DepartmentDto> _departments = new();
    private List<CostSectionTemplateDto> _costSections = new();

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
        Loaded += async (_, _) => await LoadTemplates();
    }

    private async Task LoadTemplates()
    {
        try
        {
            // Carica reparti
            string deptJson = await ApiClient.GetAsync("/api/departments");
            JsonDocument deptDoc = JsonDocument.Parse(deptJson);
            if (deptDoc.RootElement.GetProperty("success").GetBoolean())
                _departments = JsonSerializer.Deserialize<List<DepartmentDto>>(
                    deptDoc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Carica sezioni costo template
            string csJson = await ApiClient.GetAsync("/api/cost-sections/templates");
            JsonDocument csDoc = JsonDocument.Parse(csJson);
            if (csDoc.RootElement.GetProperty("success").GetBoolean())
                _costSections = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                    csDoc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Carica fasi template
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

        // Raggruppa per categoria
        var groups = _templates
            .GroupBy(t => string.IsNullOrEmpty(t.Category) ? "ALTRO" : t.Category)
            .OrderBy(g => g.Min(t => t.SortOrder));

        foreach (var group in groups)
        {
            // Header categoria
            Border catHeader = new()
            {
                Background = Brush("#F3F4F6"),
                Padding = new Thickness(16, 4, 16, 4),
                Margin = new Thickness(0, 8, 0, 2)
            };
            catHeader.Child = new TextBlock
            {
                Text = group.Key,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#6B7280")
            };
            pnlTemplates.Children.Add(catHeader);

            foreach (var t in group.OrderBy(t => t.SortOrder))
                pnlTemplates.Children.Add(BuildRow(t));
        }

        txtStatus.Text = $"{_templates.Count} fasi caricate";
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // Default
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Nome
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // Reparto
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) }); // Sezione costo
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // Ordine
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });  // Elimina

        // Col 0: Checkbox default
        CheckBox chkDefault = new()
        {
            IsChecked = t.IsDefault,
            VerticalAlignment = VerticalAlignment.Center
        };
        chkDefault.Click += async (s, e) =>
        {
            bool newVal = chkDefault.IsChecked == true;
            await UpdateTemplateField(t.Id, "is_default", newVal ? "1" : "0");
            t.IsDefault = newVal;
            int defaultCount = _templates.Count(x => x.IsDefault);
            txtCount.Text = $"{_templates.Count} fasi totali — {defaultCount} default";
        };
        Grid.SetColumn(chkDefault, 0);
        grid.Children.Add(chkDefault);

        // Col 1: Nome
        TextBlock txtName = new()
        {
            Text = t.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#1A1D26")
        };
        Grid.SetColumn(txtName, 1);
        grid.Children.Add(txtName);

        // Col 2: Reparto (badge)
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

        // Col 3: Sezione costo (ComboBox)
        ComboBox cmbCostSection = new()
        {
            Height = 24, Width = 190,
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"),
            VerticalAlignment = VerticalAlignment.Center
        };
        cmbCostSection.Items.Add(new ComboBoxItem { Content = "— Nessuna —", Tag = (int?)null });
        int selectedIdx = 0;
        int idx = 1;
        foreach (var cs in _costSections.OrderBy(c => c.SortOrder))
        {
            string groupLabel = string.IsNullOrEmpty(cs.GroupName) ? "" : $"[{cs.GroupName}] ";
            cmbCostSection.Items.Add(new ComboBoxItem { Content = $"{groupLabel}{cs.Name}", Tag = (int?)cs.Id });
            if (cs.Id == t.CostSectionTemplateId) selectedIdx = idx;
            idx++;
        }
        cmbCostSection.SelectedIndex = selectedIdx;
        int templateId = t.Id;
        cmbCostSection.SelectionChanged += async (s, ev) =>
        {
            int? newCsId = (cmbCostSection.SelectedItem as ComboBoxItem)?.Tag as int?;
            if (newCsId == t.CostSectionTemplateId) return;
            string val = newCsId.HasValue ? newCsId.Value.ToString() : "";
            await UpdateTemplateField(templateId, "cost_section_template_id", val);
            t.CostSectionTemplateId = newCsId;
        };
        Grid.SetColumn(cmbCostSection, 3);
        grid.Children.Add(cmbCostSection);

        // Col 4: Sort order
        TextBox txtSort = new()
        {
            Text = t.SortOrder.ToString(),
            FontSize = 12, Width = 40, Height = 24,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
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

        // Col 5: Elimina
        Button btnDel = new()
        {
            Content = "✕", Width = 24, Height = 24, FontSize = 11,
            Background = Brush("#EF44441A"), Foreground = Brush("#EF4444"),
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
        var dlg = new PhaseTemplateDialog(_departments, _costSections) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await LoadTemplates();
    }

    private async Task UpdateTemplateField(int templateId, string field, string value)
    {
        try
        {
            string json = JsonSerializer.Serialize(new { field, value });
            await ApiClient.PatchAsync($"/api/phases/templates/{templateId}/field", json);
        }
        catch { }
    }
}
