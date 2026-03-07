using System.Windows.Media;

namespace ATEC.PM.Client.Views;

public partial class CostSectionsPage : Page
{
    private List<CostSectionGroupDto> _groups = new();
    private List<CostSectionTemplateDto> _templates = new();

    private static readonly Dictionary<string, string> GroupColors = new()
    {
        { "GESTIONE", "#2563EB" }, { "PRESCHIERAMENTO", "#7C3AED" },
        { "INSTALLAZIONE", "#D97706" }, { "OPZIONE", "#DC2626" }
    };

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public CostSectionsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadData();
    }

    private async Task LoadData()
    {
        try
        {
            // Carica gruppi
            string gJson = await ApiClient.GetAsync("/api/cost-sections/groups");
            JsonDocument gDoc = JsonDocument.Parse(gJson);
            if (gDoc.RootElement.GetProperty("success").GetBoolean())
                _groups = JsonSerializer.Deserialize<List<CostSectionGroupDto>>(
                    gDoc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Carica template
            string tJson = await ApiClient.GetAsync("/api/cost-sections/templates");
            JsonDocument tDoc = JsonDocument.Parse(tJson);
            if (tDoc.RootElement.GetProperty("success").GetBoolean())
                _templates = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                    tDoc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            RenderList();
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void RenderList()
    {
        pnlSections.Children.Clear();
        int defaultCount = _templates.Count(t => t.IsDefault);
        txtCount.Text = $"{_groups.Count} gruppi — {_templates.Count} sezioni ({defaultCount} default)";

        foreach (CostSectionGroupDto group in _groups.OrderBy(g => g.SortOrder))
        {
            List<CostSectionTemplateDto> groupTemplates = _templates
                .Where(t => t.GroupId == group.Id)
                .OrderBy(t => t.SortOrder).ToList();

            // Header gruppo (editabile)
            pnlSections.Children.Add(BuildGroupHeader(group, groupTemplates.Count));

            // Template di questo gruppo
            foreach (CostSectionTemplateDto tmpl in groupTemplates)
                pnlSections.Children.Add(BuildTemplateRow(tmpl));
        }

        txtStatus.Text = $"{_templates.Count} sezioni template caricate";
    }

    private Border BuildGroupHeader(CostSectionGroupDto group, int count)
    {
        string color = GroupColors.TryGetValue(group.Name.ToUpper(), out string? c) ? c : "#6B7280";

        Border header = new()
        {
            Background = Brush(color),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 12, 0, 4)
        };

        DockPanel dp = new();

        // Nome gruppo + conteggio
        TextBlock txt = new()
        {
            Text = $"  {group.Name}  —  {count} sezioni",
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        dp.Children.Add(txt);

        // Bottoni edit/delete gruppo
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(actions, Dock.Right);

        // Ordine editabile
        TextBox txtSort = new()
        {
            Text = group.SortOrder.ToString(),
            FontSize = 11, Width = 35, Height = 22,
            Padding = new Thickness(2), Margin = new Thickness(0, 0, 6, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.White
        };
        txtSort.LostFocus += async (s, e) =>
        {
            if (!int.TryParse(txtSort.Text, out int val) || val == group.SortOrder) return;
            await UpdateGroupField(group.Id, "sort_order", val.ToString());
            group.SortOrder = val;
        };
        actions.Children.Add(txtSort);

        // Rinomina
        Button btnEdit = new()
        {
            Content = "✏", Width = 22, Height = 22, FontSize = 10,
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Rinomina gruppo"
        };
        btnEdit.Click += async (s, e) =>
        {
            string? newName = PromptInput("Rinomina Gruppo", "Nome:", group.Name);
            if (newName == null || newName == group.Name) return;
            await UpdateGroupField(group.Id, "name", newName);
            await LoadData();
        };
        actions.Children.Add(btnEdit);

        // Elimina
        Button btnDel = new()
        {
            Content = "✕", Width = 22, Height = 22, FontSize = 10,
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Elimina gruppo"
        };
        btnDel.Click += async (s, e) =>
        {
            if (MessageBox.Show($"Eliminare il gruppo \"{group.Name}\"?\nTutte le sezioni associate verranno eliminate.",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                string result = await ApiClient.DeleteAsync($"/api/cost-sections/groups/{group.Id}");
                JsonDocument doc = JsonDocument.Parse(result);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                    await LoadData();
                else
                    MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        };
        actions.Children.Add(btnDel);

        dp.Children.Add(actions);
        header.Child = dp;
        return header;
    }

    private Border BuildTemplateRow(CostSectionTemplateDto tmpl)
    {
        Border row = new()
        {
            Background = tmpl.IsActive ? Brushes.White : Brush("#F9FAFB"),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 0, 2)
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

        // Col 0: Checkbox default
        CheckBox chkDefault = new()
        {
            IsChecked = tmpl.IsDefault,
            VerticalAlignment = VerticalAlignment.Center
        };
        chkDefault.Click += async (s, e) =>
        {
            bool val = chkDefault.IsChecked == true;
            await UpdateTemplateField(tmpl.Id, "is_default", val ? "1" : "0");
            tmpl.IsDefault = val;
            int defaultCount = _templates.Count(t => t.IsDefault);
            txtCount.Text = $"{_groups.Count} gruppi — {_templates.Count} sezioni ({defaultCount} default)";
        };
        Grid.SetColumn(chkDefault, 0);
        grid.Children.Add(chkDefault);

        // Col 1: Nome
        TextBlock txtName = new()
        {
            Text = tmpl.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = tmpl.IsActive ? Brush("#1A1D26") : Brushes.Gray
        };
        Grid.SetColumn(txtName, 1);
        grid.Children.Add(txtName);

        // Col 2: Tipo (badge)
        string typeColor = tmpl.SectionType == "DA_CLIENTE" ? "#D97706" : "#059669";
        string typeLabel = tmpl.SectionType == "DA_CLIENTE" ? "DA CLIENTE" : "IN SEDE";
        Border typeBadge = new()
        {
            Background = Brush(typeColor + "20"),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        typeBadge.Child = new TextBlock
        {
            Text = typeLabel, FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = Brush(typeColor)
        };
        Grid.SetColumn(typeBadge, 2);
        grid.Children.Add(typeBadge);

        // Col 3: Gruppo
        TextBlock txtGroup = new()
        {
            Text = tmpl.GroupName,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Gray
        };
        Grid.SetColumn(txtGroup, 3);
        grid.Children.Add(txtGroup);

        // Col 4: Sort order
        TextBox txtSort = new()
        {
            Text = tmpl.SortOrder.ToString(),
            FontSize = 12, Width = 40, Height = 24,
            Padding = new Thickness(4, 2, 4, 2),
            BorderBrush = Brush("#E4E7EC"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        txtSort.LostFocus += async (s, e) =>
        {
            if (!int.TryParse(txtSort.Text, out int val) || val == tmpl.SortOrder) return;
            await UpdateTemplateField(tmpl.Id, "sort_order", val.ToString());
            tmpl.SortOrder = val;
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
            ToolTip = "Elimina sezione"
        };
        btnDel.Click += async (s, e) =>
        {
            if (MessageBox.Show($"Eliminare \"{tmpl.Name}\"?",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                string result = await ApiClient.DeleteAsync($"/api/cost-sections/templates/{tmpl.Id}");
                JsonDocument doc = JsonDocument.Parse(result);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                    await LoadData();
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

    // ══════════════════════════════════════════════════════════════
    // ADD
    // ══════════════════════════════════════════════════════════════

    private async void BtnAddGroup_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptInput("Nuovo Gruppo", "Nome gruppo:", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        int maxSort = _groups.Any() ? _groups.Max(g => g.SortOrder) + 1 : 1;
        try
        {
            string json = JsonSerializer.Serialize(new { name, sortOrder = maxSort, isActive = true },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PostAsync("/api/cost-sections/groups", json);
            await LoadData();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnAddTemplate_Click(object sender, RoutedEventArgs e)
    {
        CostSectionTemplateDialog dlg = new(_groups) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) await LoadData();
    }

    // ══════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════

    private string? PromptInput(string title, string label, string defaultValue)
    {
        Window dlg = new()
        {
            Title = title, Width = 360, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = Brush("#F7F8FA")
        };

        StackPanel sp = new() { Margin = new Thickness(20, 16, 20, 16) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("#6B7280") });
        TextBox txt = new() { Text = defaultValue, Height = 32, Padding = new Thickness(8, 5, 8, 5), FontSize = 13, BorderBrush = Brush("#E4E7EC"), Margin = new Thickness(0, 6, 0, 12) };
        sp.Children.Add(txt);

        StackPanel btns = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button btnOk = new() { Content = "OK", Width = 80, Height = 30, Background = Brush("#4F6EF7"), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand };
        Button btnCancel = new() { Content = "Annulla", Width = 80, Height = 30, Background = Brush("#F3F4F6"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand };
        btnOk.Click += (s, ev) => { dlg.DialogResult = true; dlg.Close(); };
        btnCancel.Click += (s, ev) => { dlg.DialogResult = false; dlg.Close(); };
        btns.Children.Add(btnCancel);
        btns.Children.Add(btnOk);
        sp.Children.Add(btns);

        dlg.Content = sp;
        txt.Focus();
        txt.SelectAll();

        if (dlg.ShowDialog() == true)
            return txt.Text.Trim();
        return null;
    }

    private async Task UpdateGroupField(int id, string field, string value)
    {
        try
        {
            string json = JsonSerializer.Serialize(new { field, value });
            await ApiClient.PatchAsync($"/api/cost-sections/groups/{id}/field", json);
        }
        catch { }
    }

    private async Task UpdateTemplateField(int id, string field, string value)
    {
        try
        {
            string json = JsonSerializer.Serialize(new { field, value });
            await ApiClient.PatchAsync($"/api/cost-sections/templates/{id}/field", json);
        }
        catch { }
    }
}
