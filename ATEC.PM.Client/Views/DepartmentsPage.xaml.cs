using System.Windows.Media;

namespace ATEC.PM.Client.Views;

public partial class DepartmentsPage : Page
{
    private List<DepartmentDto> _departments = new();
    private List<MarkupCoefficientDto> _markups = new();

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public DepartmentsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadData();
    }

    private async Task LoadData()
    {
        try
        {
            // Carica markup (solo RESOURCE per la combo)
            string mkJson = await ApiClient.GetAsync("/api/markup");
            JsonDocument mkDoc = JsonDocument.Parse(mkJson);
            if (mkDoc.RootElement.GetProperty("success").GetBoolean())
                _markups = JsonSerializer.Deserialize<List<MarkupCoefficientDto>>(
                    mkDoc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Carica departments
            string json = await ApiClient.GetAsync("/api/departments");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _departments = JsonSerializer.Deserialize<List<DepartmentDto>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            RenderList();
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void RenderList()
    {
        pnlDepartments.Children.Clear();
        int activeCount = _departments.Count(d => d.IsActive);
        txtCount.Text = $"{_departments.Count} reparti — {activeCount} attivi";

        foreach (DepartmentDto dept in _departments.OrderBy(d => d.SortOrder))
            pnlDepartments.Children.Add(BuildRow(dept));

        txtStatus.Text = $"{_departments.Count} reparti caricati";
    }

    private Border BuildRow(DepartmentDto dept)
    {
        Border row = new()
        {
            Background = dept.IsActive ? Brushes.White : Brush("#F9FAFB"),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 0, 2)
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        // Col 0: Codice (badge)
        Border codeBadge = new()
        {
            Background = Brush("#4F6EF720"),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        codeBadge.Child = new TextBlock
        {
            Text = dept.Code,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#4F6EF7")
        };
        Grid.SetColumn(codeBadge, 0);
        grid.Children.Add(codeBadge);

        // Col 1: Nome
        TextBlock txtName = new()
        {
            Text = dept.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = dept.IsActive ? Brush("#1A1D26") : Brushes.Gray
        };
        Grid.SetColumn(txtName, 1);
        grid.Children.Add(txtName);

        // Col 2: Costo orario (editabile)
        TextBox txtCost = new()
        {
            Text = dept.HourlyCost.ToString("F2"),
            FontSize = 12, Width = 80, Height = 24,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        txtCost.LostFocus += async (s, e) =>
        {
            if (!decimal.TryParse(txtCost.Text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal newCost)) return;
            if (newCost == dept.HourlyCost) return;
            await UpdateField(dept.Id, "hourly_cost", newCost.ToString(System.Globalization.CultureInfo.InvariantCulture));
            dept.HourlyCost = newCost;
        };
        Grid.SetColumn(txtCost, 2);
        grid.Children.Add(txtCost);

        // Col 3: Markup code (ComboBox)
        ComboBox cmbMarkup = new()
        {
            Height = 24, Width = 110,
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"),
            VerticalAlignment = VerticalAlignment.Center
        };
        cmbMarkup.Items.Add(new ComboBoxItem { Content = "— nessuno —", Tag = "" });
        int selectedIdx = 0;
        int idx = 1;
        foreach (MarkupCoefficientDto mk in _markups.Where(m => m.CoefficientType == "RESOURCE").OrderBy(m => m.SortOrder))
        {
            cmbMarkup.Items.Add(new ComboBoxItem { Content = mk.Code, Tag = mk.Code });
            if (mk.Code == dept.MarkupCode) selectedIdx = idx;
            idx++;
        }
        cmbMarkup.SelectedIndex = selectedIdx;
        cmbMarkup.SelectionChanged += async (s, e) =>
        {
            string newCode = (cmbMarkup.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            if (newCode == dept.MarkupCode) return;
            await UpdateField(dept.Id, "markup_code", newCode);
            dept.MarkupCode = newCode;
        };
        Grid.SetColumn(cmbMarkup, 3);
        grid.Children.Add(cmbMarkup);

        // Col 4: Sort order (editabile)
        TextBox txtSort = new()
        {
            Text = dept.SortOrder.ToString(),
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
            if (newSort == dept.SortOrder) return;
            await UpdateField(dept.Id, "sort_order", newSort.ToString());
            dept.SortOrder = newSort;
        };
        Grid.SetColumn(txtSort, 4);
        grid.Children.Add(txtSort);

        // Col 5: Checkbox attivo
        CheckBox chkActive = new()
        {
            IsChecked = dept.IsActive,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        chkActive.Click += async (s, e) =>
        {
            bool newVal = chkActive.IsChecked == true;
            await UpdateField(dept.Id, "is_active", newVal ? "1" : "0");
            dept.IsActive = newVal;
            RenderList();
        };
        Grid.SetColumn(chkActive, 5);
        grid.Children.Add(chkActive);

        // Col 6: Bottoni modifica + elimina
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button btnEdit = new()
        {
            Content = "✏", Width = 24, Height = 24, FontSize = 11,
            Background = Brush("#4F6EF71A"), Foreground = Brush("#4F6EF7"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Modifica reparto"
        };
        btnEdit.Click += async (s, e) =>
        {
            DepartmentDialog dlg = new(dept, _markups) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) await LoadData();
        };

        Button btnDel = new()
        {
            Content = "✕", Width = 24, Height = 24, FontSize = 11,
            Background = Brush("#EF44441A"), Foreground = Brush("#EF4444"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Elimina reparto"
        };
        btnDel.Click += async (s, e) =>
        {
            if (MessageBox.Show($"Eliminare il reparto \"{dept.Code} - {dept.Name}\"?",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                string result = await ApiClient.DeleteAsync($"/api/departments/{dept.Id}");
                JsonDocument doc = JsonDocument.Parse(result);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                    await LoadData();
                else
                    MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        };

        actions.Children.Add(btnEdit);
        actions.Children.Add(btnDel);
        Grid.SetColumn(actions, 6);
        grid.Children.Add(actions);

        row.Child = grid;
        return row;
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        DepartmentDialog dlg = new(_markups) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) await LoadData();
    }

    private async Task UpdateField(int deptId, string field, string value)
    {
        try
        {
            string jsonBody = JsonSerializer.Serialize(new { field, value });
            await ApiClient.PatchAsync($"/api/departments/{deptId}/field", jsonBody);
        }
        catch { }
    }
}
