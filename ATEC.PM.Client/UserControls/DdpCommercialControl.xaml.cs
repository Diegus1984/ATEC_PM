using ATEC.PM.Client.Services;
using ATEC.PM.Client.Views;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.UserControls;

public partial class DdpCommercialControl : UserControl
{
    private int _projectId;
    private List<BomItemListItem> _allItems = new();
    private List<BomItemListItem> _filteredItems = new();
    private Dictionary<string, ComboBox> _filterCombos = new();
    private List<string> _destinations = new();
    private List<DdpStatusItem> _statuses = new();   // stati editabili da Conf. DDP (etichetta + colori)

    // Default CABLATI (causali DDP reali): fallback finché /api/ddp-statuses non risponde. Rimpiazzati da LoadStatuses().
    private static List<KeyValuePair<string, string>> StatusList = new()
    {
        new("ANN", "ANNULLATO"),
        new("SOSP", "SOSPESO"),
        new("RAM", "RIMESSO A MAGAZZINO"),
        new("SOST", "SOSTITUITO"),
        new("CON", "CONSEGNATO"),
        new("COS", "COSTRUITO"),
        new("DISP", "DISPONIBILE"),
        new("DC", "DA COSTRUIRE"),
        new("DO", "DA ORDINARE"),
        new("ASS", "ASSEGNATO AL MONTATORE"),
        new("CHEK", "MAT. CHE NECESSITA CONTROLLO TECNICO/COMMERCIALE"),
        new("IO", "IN ORDINE"),
        new("PAR", "PARZIALMENTE CONSEGNATO o COSTRUITO"),
        new("RO", "RICHIESTA OFFERTA"),
        new("VER", "VERIFICARE SE DISPONIBILE A MAG"),
        new("SPED", "SPEDITO AL CLIENTE O AL FORNITORE DI SERVIZI"),
        new("MOD", "INVIATO A MODULA - MAG")
    };

    private static Dictionary<string, string> StatusKeyToDisplay =
        StatusList.ToDictionary(kv => kv.Key, kv => kv.Value);

    public DdpCommercialControl()
    {
        InitializeComponent();
        ApplyRowStyle();

        // Flag "sto editando una cella" per il busy-guard del real-time (non ricaricare mentre scrivo).
        dgDdp.BeginningEdit += (_, _) => _isEditingCell = true;
        dgDdp.RowEditEnding += (_, _) => _isEditingCell = false;

        // La sezione è cachata: alla riapertura riconnetto l'hub e risincronizzo.
        Loaded += (_, _) =>
        {
            if (_projectId > 0 && _realtimeHub == null) { _ = StartRealtimeAsync(); OnRealtimeChange(); }
        };
        Unloaded += (_, _) => StopRealtime();
    }

    public void Load(int projectId)
    {
        _projectId = projectId;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await LoadStatuses();   // prima gli stati: alimentano combo, converter e colori riga
        ApplyRowStyle();        // ricostruisce i colori riga dai dati appena caricati
        _ = LoadDestinations();
        await LoadData();
        _ = StartRealtimeAsync();
    }

    // Carica gli stati (etichetta + colori) da Conf. DDP; in caso di errore resta sui default cablati.
    private async Task LoadStatuses()
    {
        try
        {
            // TUTTE le causali: servono per risolvere etichetta/colore di QUALSIASI valore già salvato sulle righe
            // (anche di una causale disattivata). La combo per la scelta, invece, offre solo le ATTIVE.
            List<DdpStatusItem> statuses = await ApiClient.GetListAsync<DdpStatusItem>("/api/ddp-statuses");
            if (statuses.Count > 0)
            {
                _statuses = statuses;
                StatusKeyToDisplay = statuses
                    .GroupBy(s => s.StatusKey).Select(g => g.First())
                    .ToDictionary(s => s.StatusKey, s => s.Label);
                StatusList = statuses
                    .Where(s => s.IsActive)
                    .Select(s => new KeyValuePair<string, string>(s.StatusKey, s.Label))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[DdpCommercialControl] Errore caricamento stati: {ex.Message}");
        }
    }

    private async Task LoadDestinations()
    {
        try
        {
            List<DdpDestinationItem> items = await ApiClient.GetListAsync<DdpDestinationItem>(
                "/api/ddp-destinations/active");
            _destinations = items.Select(d => d.Name).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[DdpCommercialControl] Errore caricamento destinazioni: {ex.Message}");
        }
    }

    private async Task LoadData()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            _allItems = await ApiClient.GetListAsync<BomItemListItem>(
                $"/api/projects/{_projectId}/ddp?type=COMMERCIAL");

            for (int i = 0; i < _allItems.Count; i++)
                _allItems[i].RowNumber = i + 1;

            SubscribeItemEvents();
            PopulateFilters();
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

        foreach ((string key, System.Windows.Media.Color bg, System.Windows.Media.Color fg) in GetStatusColors())
            rowStyle.Triggers.Add(CreateStatusTrigger(key, bg, fg));

        rowStyle.Triggers.Add(new Trigger
        {
            Property = DataGridRow.IsSelectedProperty,
            Value = true,
            Setters = { new Setter(DataGridRow.OpacityProperty, 0.8) }
        });

        dgDdp.RowStyle = rowStyle;

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

    // Colori riga per stato: dai dati (Conf. DDP) se caricati, altrimenti i default cablati di fallback.
    private IEnumerable<(string Key, System.Windows.Media.Color Bg, System.Windows.Media.Color Fg)> GetStatusColors()
    {
        if (_statuses.Count > 0)
        {
            foreach (DdpStatusItem s in _statuses)
                yield return (s.StatusKey,
                              ParseColor(s.ColorBg, System.Windows.Media.Colors.LightGray),
                              ParseColor(s.ColorFg, System.Windows.Media.Colors.Black));
            yield break;
        }

        yield return ("ANN", System.Windows.Media.Colors.Black, System.Windows.Media.Colors.White);
        yield return ("SOSP", System.Windows.Media.Colors.Black, System.Windows.Media.Colors.White);
        yield return ("RAM", System.Windows.Media.Colors.Black, System.Windows.Media.Colors.White);
        yield return ("SOST", System.Windows.Media.Colors.Black, System.Windows.Media.Colors.White);
        yield return ("CON", System.Windows.Media.Color.FromRgb(0, 176, 80), System.Windows.Media.Colors.White);
        yield return ("COS", System.Windows.Media.Color.FromRgb(0, 176, 80), System.Windows.Media.Colors.White);
        yield return ("DISP", System.Windows.Media.Color.FromRgb(0, 176, 80), System.Windows.Media.Colors.White);
        yield return ("DC", System.Windows.Media.Color.FromRgb(0, 100, 0), System.Windows.Media.Colors.White);
        yield return ("DO", System.Windows.Media.Color.FromRgb(255, 0, 0), System.Windows.Media.Colors.White);
        yield return ("ASS", System.Windows.Media.Color.FromRgb(180, 180, 180), System.Windows.Media.Colors.Black);
        yield return ("CHEK", System.Windows.Media.Color.FromRgb(139, 0, 139), System.Windows.Media.Colors.White);
        yield return ("IO", System.Windows.Media.Color.FromRgb(255, 255, 0), System.Windows.Media.Colors.Black);
        yield return ("PAR", System.Windows.Media.Color.FromRgb(112, 48, 160), System.Windows.Media.Colors.White);
        yield return ("RO", System.Windows.Media.Color.FromRgb(255, 192, 0), System.Windows.Media.Colors.Black);
        yield return ("VER", System.Windows.Media.Color.FromRgb(0, 176, 240), System.Windows.Media.Colors.White);
        yield return ("SPED", System.Windows.Media.Color.FromRgb(217, 217, 217), System.Windows.Media.Colors.Black);
        yield return ("MOD", System.Windows.Media.Color.FromRgb(173, 216, 230), System.Windows.Media.Colors.Black);
    }

    private static System.Windows.Media.Color ParseColor(string? hex, System.Windows.Media.Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
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
        PopulateCombo("Destination", _destinations);
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
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        picker.ItemAdded += async () => await LoadData();
        picker.Show();
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgDdp.SelectedItem is not BomItemListItem item) return;

        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Eliminare riga {item.PartNumber} - {item.Description}?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync(WithConn($"/api/projects/{_projectId}/ddp/{item.Id}"));
            await LoadData();
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadStatuses();
        ApplyRowStyle();
        await LoadData();
    }

    private void DgDdp_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        btnDelete.IsEnabled = dgDdp.SelectedItem != null;
    }

    // === CELL EDIT ===
    private async void DgDdp_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        _isEditingCell = false;
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.Item is not BomItemListItem item) return;

        await Task.Delay(100);

        if (item.Quantity <= 0)
        {
            item.Quantity = 1;
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("La quantità deve essere maggiore di zero.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (item.DateNeeded.HasValue && item.DateNeeded.Value.Date < DateTime.Today)
        {
            item.DateNeeded = DateTime.Today;
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("La data prevista non può essere nel passato. Impostata a oggi.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                Notes = item.Notes,
                ExpectedUpdatedAt = item.UpdatedAt   // concorrenza ottimistica
            };

            string body = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string resp = await ApiClient.PutAsync(WithConn($"/api/projects/{_projectId}/ddp/{item.Id}"), body);
            if (ApiClient.TryGetApiData<DateTime?>(resp, out DateTime? newTs, out string msg))
            {
                if (newTs.HasValue) item.UpdatedAt = newTs;   // riallinea il token per il prossimo salvataggio
                UpdateSummary();
            }
            else
            {
                // Conflitto (409) o errore: avviso e ricarico per riallinearmi al server.
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
                    string.IsNullOrWhiteSpace(msg) ? "Salvataggio non riuscito." : msg,
                    "Distinta", MessageBoxButton.OK, MessageBoxImage.Warning);
                await LoadData();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[DdpCommercialControl] Errore salvataggio cella per riga {item.Id}: {ex.Message}");
        }
    }

    private void StatusCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb)
            cb.ItemsSource = StatusList;
    }

    private void DestinationCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox cb) return;

        string? current = (cb.DataContext as BomItemListItem)?.Destination;
        List<string> options = new(_destinations);
        // Mantieni la destinazione corrente anche se non è più tra le attive (rinominata/disattivata):
        // così in modifica non sparisce e non rischi di azzerarla per sbaglio.
        if (!string.IsNullOrWhiteSpace(current) && !options.Contains(current))
            options.Insert(0, current);

        cb.ItemsSource = options;
        cb.SelectedItem = current;
    }

    // === DATE PICKER VALIDATION ===
    private void DatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker dp && dp.SelectedDate.HasValue && dp.SelectedDate.Value.Date < DateTime.Today)
        {
            dp.SelectedDate = DateTime.Today;
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("La data prevista non può essere nel passato. Impostata a oggi.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // === STATUS DISPLAY ===
    public static string GetStatusDisplay(string key)
    {
        return StatusKeyToDisplay.TryGetValue(key, out var display) ? display : key;
    }

    private void DestinationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem != null)
        {
            dgDdp.CommitEdit(DataGridEditingUnit.Row, true);
        }
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