using ATEC.PM.Client.Views;

namespace ATEC.PM.Client.UserControls;

public partial class DdpCommercialControl : UserControl
{
    private int _projectId;
    private List<BomItemListItem> _allItems = new();
    private List<BomItemListItem> _filteredItems = new();
    private Dictionary<string, ComboBox> _filterCombos = new();

    private static readonly List<KeyValuePair<string, string>> StatusList = new()
{
    new("TO_ORDER", "Da Ordinare"),
    new("ORDERED", "In Ordine"),
    new("DELIVERED", "Consegnato"),
    new("PARTIAL", "Parziale"),
    new("TO_BUILD", "Da Costruire"),
    new("RFQ", "Rich.Offerta"),
    new("TO_CHECK", "Verificare"),
    new("CANCELLED", "Annullato"),
    new("ASSIGNED", "Assegnato"),
    new("SHIPPED", "Spedito"),
    new("TECH_CHECK", "Controllo"),
    new("TO_MODULA", "A Modula")
};

    private static readonly Dictionary<string, string> StatusKeyToDisplay =
        StatusList.ToDictionary(kv => kv.Key, kv => kv.Value);

    public DdpCommercialControl()
    {
        InitializeComponent();
        ApplyRowStyle();
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
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/ddp?type=COMMERCIAL");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean())
            {
                txtStatus.Text = "Errore caricamento";
                return;
            }

            _allItems = JsonSerializer.Deserialize<List<BomItemListItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Numera righe
            for (int i = 0; i < _allItems.Count; i++)
                _allItems[i].RowNumber = i + 1;

            // Sottoscrivi eventi
            SubscribeItemEvents();

            // Popola filtri
            PopulateFilters();

            // Applica filtri (mostra tutto)
            ApplyFilters();

            txtStatus.Text = $"{_allItems.Count} righe caricate";
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore: {ex.Message}";
        }
    }

    private void SubscribeItemEvents()
    {
        foreach (var item in _allItems)
        {
            item.PropertyChanged += (s, ev) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (ev.PropertyName is nameof(BomItemListItem.ItemStatus)
                        or nameof(BomItemListItem.TotalCost)
                        or nameof(BomItemListItem.Quantity))
                    {
                        UpdateSummary();
                    }
                });
            };
        }
    }

    // === ROW STYLE CON COLORI STATO ===
    private void ApplyRowStyle()
    {
        var rowStyle = new Style(typeof(DataGridRow), dgDdp.RowStyle);
        rowStyle.Setters.Add(new Setter(DataGridRow.FontSizeProperty, 9.0));
        rowStyle.Setters.Add(new Setter(DataGridRow.FontWeightProperty, FontWeights.Normal));
        rowStyle.Setters.Add(new Setter(DataGridRow.MinHeightProperty, 30.0));
        rowStyle.Setters.Add(new Setter(DataGridRow.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        rowStyle.Triggers.Add(CreateStatusTrigger("TO_ORDER", System.Windows.Media.Color.FromRgb(255, 0, 0), System.Windows.Media.Colors.White));
        rowStyle.Triggers.Add(CreateStatusTrigger("ORDERED", System.Windows.Media.Color.FromRgb(255, 255, 0), System.Windows.Media.Colors.Black));
        rowStyle.Triggers.Add(CreateStatusTrigger("DELIVERED", System.Windows.Media.Color.FromRgb(0, 176, 80), System.Windows.Media.Colors.White));
        rowStyle.Triggers.Add(CreateStatusTrigger("PARTIAL", System.Windows.Media.Color.FromRgb(112, 48, 160), System.Windows.Media.Colors.White));
        rowStyle.Triggers.Add(CreateStatusTrigger("TO_BUILD", System.Windows.Media.Color.FromRgb(128, 128, 128), System.Windows.Media.Colors.White));
        rowStyle.Triggers.Add(CreateStatusTrigger("RFQ", System.Windows.Media.Color.FromRgb(255, 192, 0), System.Windows.Media.Colors.Black));
        rowStyle.Triggers.Add(CreateStatusTrigger("TO_CHECK", System.Windows.Media.Color.FromRgb(0, 176, 240), System.Windows.Media.Colors.White));
        rowStyle.Triggers.Add(CreateStatusTrigger("CANCELLED", System.Windows.Media.Color.FromRgb(64, 64, 64), System.Windows.Media.Colors.White));
        rowStyle.Triggers.Add(CreateStatusTrigger("ASSIGNED", System.Windows.Media.Color.FromRgb(0, 80, 180), System.Windows.Media.Colors.White));
        rowStyle.Triggers.Add(CreateStatusTrigger("SHIPPED", System.Windows.Media.Color.FromRgb(0, 150, 150), System.Windows.Media.Colors.White));
        rowStyle.Triggers.Add(CreateStatusTrigger("TECH_CHECK", System.Windows.Media.Color.FromRgb(200, 50, 120), System.Windows.Media.Colors.White));
        rowStyle.Triggers.Add(CreateStatusTrigger("TO_MODULA", System.Windows.Media.Color.FromRgb(34, 139, 34), System.Windows.Media.Colors.White));

        rowStyle.Triggers.Add(new Trigger
        {
            Property = DataGridRow.IsSelectedProperty,
            Value = true,
            Setters = { new Setter(DataGridRow.OpacityProperty, 0.8) }
        });

        dgDdp.RowStyle = rowStyle;

        // CellStyle da zero - non eredita da ModernCell che forza il Foreground nero
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(DataGridCell.VerticalAlignmentProperty, VerticalAlignment.Center));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(DataGridCell.FocusVisualStyleProperty, null));
        cellStyle.Setters.Add(new Setter(DataGridCell.PaddingProperty, new Thickness(12, 6, 12, 6)));
        cellStyle.Setters.Add(new Setter(DataGridCell.TemplateProperty, CreateCellTemplate()));
        dgDdp.CellStyle = cellStyle;
    }

    private static ControlTemplate CreateCellTemplate()
    {
        var template = new ControlTemplate(typeof(DataGridCell));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty, new Thickness(12, 6, 12, 6));
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static DataTrigger CreateStatusTrigger(string status, System.Windows.Media.Color bgColor, System.Windows.Media.Color fgColor)
    {
        var trigger = new DataTrigger
        {
            Binding = new System.Windows.Data.Binding("ItemStatus"),
            Value = status
        };
        var bgBrush = new System.Windows.Media.SolidColorBrush(bgColor);
        bgBrush.Freeze();
        var fgBrush = new System.Windows.Media.SolidColorBrush(fgColor);
        fgBrush.Freeze();
        trigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, bgBrush));
        trigger.Setters.Add(new Setter(DataGridRow.ForegroundProperty, fgBrush));
        return trigger;
    }

    // === FILTRI ===
    private void FilterCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb && cb.Tag is string tag)
            _filterCombos[tag] = cb;
    }

    private void FilterCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void PopulateFilters()
    {
        PopulateCombo("RequestedBy", _allItems.Select(i => i.RequestedBy).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s));
        PopulateCombo("SupplierName", _allItems.Select(i => i.SupplierName).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s));
        PopulateCombo("Manufacturer", _allItems.Select(i => i.Manufacturer).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s));
        PopulateCombo("ItemStatus", StatusList.Select(kv => kv.Value));
        PopulateCombo("Destination", _allItems.Select(i => i.Destination).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s));
    }

    private void PopulateCombo(string tag, IEnumerable<string> values)
    {
        if (!_filterCombos.TryGetValue(tag, out var combo)) return;
        combo.Items.Clear();
        combo.Items.Add("(Tutti)");
        foreach (var val in values)
            combo.Items.Add(val);
        combo.SelectedIndex = 0;
    }

    private void ApplyFilters()
    {
        var filtered = _allItems.AsEnumerable();

        string fRequested = GetFilterValue("RequestedBy");
        string fSupplier = GetFilterValue("SupplierName");
        string fManufacturer = GetFilterValue("Manufacturer");
        string fStatus = GetFilterValue("ItemStatus");
        string fDestination = GetFilterValue("Destination");

        if (!string.IsNullOrEmpty(fRequested))
            filtered = filtered.Where(i => i.RequestedBy == fRequested);
        if (!string.IsNullOrEmpty(fSupplier))
            filtered = filtered.Where(i => i.SupplierName == fSupplier);
        if (!string.IsNullOrEmpty(fManufacturer))
            filtered = filtered.Where(i => i.Manufacturer == fManufacturer);
        if (!string.IsNullOrEmpty(fStatus))
        {
            var statusKey = StatusList.FirstOrDefault(kv => kv.Value == fStatus).Key;
            if (!string.IsNullOrEmpty(statusKey))
                filtered = filtered.Where(i => i.ItemStatus == statusKey);
        }
        if (!string.IsNullOrEmpty(fDestination))
            filtered = filtered.Where(i => i.Destination == fDestination);

        _filteredItems = filtered.ToList();
        dgDdp.ItemsSource = _filteredItems;
        UpdateSummary();
    }

    private string GetFilterValue(string tag)
    {
        if (_filterCombos.TryGetValue(tag, out var combo) && combo.SelectedItem is string val && val != "(Tutti)")
            return val;
        return "";
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var combo in _filterCombos.Values)
            combo.SelectedIndex = 0;
    }

    // === SUMMARY ===
    private void UpdateSummary()
    {
        var activeItems = _filteredItems.Where(i => i.ItemStatus != "CANCELLED").ToList();
        var totalCost = activeItems.Sum(i => i.TotalCost);
        txtSummary.Text = $"{_filteredItems.Count} righe ({activeItems.Count} attive)  |  Totale: {totalCost:N2} €";
    }

    // === TOOLBAR ===
    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var picker = new CatalogPickerWindow(_projectId, "COMMERCIAL", App.UserFullName)
        {
            Owner = Window.GetWindow(this)
        };
        picker.ItemAdded += async () => await LoadData();
        picker.Show();
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgDdp.SelectedItem is not BomItemListItem item) return;

        if (MessageBox.Show($"Eliminare riga {item.PartNumber} - {item.Description}?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"/api/projects/{_projectId}/ddp/{item.Id}");
            await LoadData();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadData();

    private void DgDdp_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        btnDelete.IsEnabled = dgDdp.SelectedItem != null;
    }

    // === CELL EDIT ===
    private async void DgDdp_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.Item is not BomItemListItem item) return;

        await Task.Delay(100);

        // Validazione quantità
        if (item.Quantity <= 0)
        {
            item.Quantity = 1;
            MessageBox.Show("La quantità deve essere maggiore di zero.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Validazione data
        if (item.DateNeeded.HasValue && item.DateNeeded.Value.Date < DateTime.Today)
        {
            item.DateNeeded = DateTime.Today;
            MessageBox.Show("La data prevista non può essere nel passato. Impostata a oggi.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        try
        {
            var req = new BomItemSaveRequest
            {
                Id = item.Id,
                ProjectId = _projectId,
                Quantity = item.Quantity,
                ItemStatus = item.ItemStatus,
                DaneaRef = item.DaneaRef,
                DateNeeded = item.DateNeeded,
                Destination = item.Destination,
                Notes = item.Notes
            };

            string body = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"/api/projects/{_projectId}/ddp/{item.Id}", body);
            UpdateSummary();
        }
        catch { /* silenzioso per auto-save */ }
    }

    private void StatusCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb)
            cb.ItemsSource = StatusList;
    }

    // === DATE PICKER VALIDATION ===
    private void DatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker dp && dp.SelectedDate.HasValue && dp.SelectedDate.Value.Date < DateTime.Today)
        {
            dp.SelectedDate = DateTime.Today;
            MessageBox.Show("La data prevista non può essere nel passato. Impostata a oggi.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // === STATUS DISPLAY ===
    public static string GetStatusDisplay(string key)
    {
        return StatusKeyToDisplay.TryGetValue(key, out var display) ? display : key;
    }
}
public class StatusToDisplayConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string key)
            return DdpCommercialControl.GetStatusDisplay(key);
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}