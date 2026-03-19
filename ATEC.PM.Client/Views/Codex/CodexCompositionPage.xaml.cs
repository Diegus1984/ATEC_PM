using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class CodexCompositionPage : Page
{
    private List<CodexListItem> _allItems = new();
    private List<CatalogItemListItem> _catalogItems = new();
    private List<CompositionParentItem> _allParentItems = new();
    private int? _selectedParentId;
    private Point _dragStartPoint;
    private TreeViewItem? _lastHighlighted;
    private bool _suppressParentFilter;
    private bool _catalogLoaded;
    private string _currentSource = "codex";

    private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };

    // Tipi composizione disponibili
    private static readonly List<CompositionType> _types = new()
    {
        new("501", "Gruppo meccanico",   new[] { "1", "2", "3", "4" }),
        new("601", "Assieme meccanico",  new[] { "5" }),
        new("701", "Layout meccanico",   new[] { "6" }),
    };

    public CodexCompositionPage()
    {
        InitializeComponent();
        cmbType.ItemsSource = _types;

        // Ricerca wildcard nella ComboBox articolo
        cmbParent.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(CmbParent_TextChanged));

        Loaded += async (_, _) => await LoadAllItems();
    }

    // ── DATA LOADING ────────────────────────────────────────

    private async Task LoadAllItems()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/codex");
            var response = JsonSerializer.Deserialize<ApiResponse<List<CodexListItem>>>(json, _jsonOpt);
            if (response?.Success == true)
                _allItems = response.Data ?? new();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── SORGENTE COMBO ─────────────────────────────────────

    private void CmbSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbSource.SelectedItem is not ComboBoxItem sel) return;
        _currentSource = sel.Content?.ToString() == "Catalogo" ? "catalog" : "codex";

        if (_currentSource == "catalog" && !_catalogLoaded)
            _ = LoadCatalogItems();
        else
            RefreshLeftPanel();
    }

    private async Task LoadCatalogItems()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/catalog");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _catalogItems = JsonSerializer.Deserialize<List<CatalogItemListItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(), _jsonOpt) ?? new();
                _catalogLoaded = true;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento catalogo: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshLeftPanel();
    }

    private void RefreshLeftPanel()
    {
        if (cmbType.SelectedItem is CompositionType type)
        {
            if (_currentSource == "catalog")
                FilterLeftPanelCatalog();
            else
                FilterLeftPanel(type.AllowedChildPrefixes);
        }
    }

    private void FilterLeftPanelCatalog()
    {
        string searchCodice = txtSearchCodice.Text.Trim().ToLower();
        string searchDescr = txtSearchDescr.Text.Trim().ToLower();

        var filtered = _catalogItems
            .Where(i => string.IsNullOrEmpty(searchCodice) || Match(i.Code, searchCodice))
            .Where(i => string.IsNullOrEmpty(searchDescr) || Match(i.Description, searchDescr))
            .Select(i => new AvailableItem { Id = i.Id, Codice = i.Code, Descr = i.Description, Source = "catalog" })
            .OrderBy(i => i.Codice)
            .ToList();

        dgAvailable.ItemsSource = filtered;
    }

    // ── COMBO BOX HANDLERS ──────────────────────────────────

    private void CmbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbType.SelectedItem is not CompositionType type) return;

        // Popola combo articoli con items del tipo selezionato
        _allParentItems = _allItems
            .Where(i => i.Codice.StartsWith(type.Prefix))
            .Select(i => new CompositionParentItem(i.Id, i.Codice, i.Descr))
            .OrderBy(i => i.Codice)
            .ToList();

        _suppressParentFilter = true;
        cmbParent.ItemsSource = _allParentItems;
        cmbParent.SelectedIndex = -1;
        cmbParent.Text = "";
        _suppressParentFilter = false;

        // 601/701: solo sorgente Codex, no catalogo
        bool allowCatalog = type.Prefix == "501";
        cmbSource.SelectedIndex = 0; // forza "Codex"
        cmbSource.IsEnabled = allowCatalog;
        _currentSource = "codex";
        _selectedParentId = null;

        // Filtra lista sinistra per tipo ammesso
        FilterLeftPanel(type.AllowedChildPrefixes);

        tvComposition.Items.Clear();
        txtTreeHeader.Text = "COMPOSIZIONE";
        txtStatus.Text = $"{_allParentItems.Count} articoli {type.Prefix} trovati";
    }

    private async void CmbParent_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbParent.SelectedItem is not CompositionParentItem parent) return;

        _selectedParentId = parent.Id;
        txtTreeHeader.Text = $"COMPOSIZIONE — {parent.Codice}";
        await LoadTree(parent.Id);
    }

    private void CmbParent_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressParentFilter) return;
        if (cmbParent.SelectedItem != null) return; // selezione in corso, non filtrare

        string search = cmbParent.Text?.Trim().ToLower() ?? "";
        if (string.IsNullOrEmpty(search))
        {
            cmbParent.ItemsSource = _allParentItems;
        }
        else
        {
            var filtered = _allParentItems
                .Where(i => Match(i.Codice, search) || Match(i.Descr, search))
                .ToList();
            _suppressParentFilter = true;
            cmbParent.ItemsSource = filtered;
            _suppressParentFilter = false;
        }

        cmbParent.IsDropDownOpen = true;
    }

    // ── LEFT PANEL FILTERING ────────────────────────────────

    private void FilterLeftPanel(string[] allowedPrefixes)
    {
        string searchCodice = txtSearchCodice.Text.Trim().ToLower();
        string searchDescr = txtSearchDescr.Text.Trim().ToLower();

        var filtered = _allItems
            .Where(i => allowedPrefixes.Any(p => i.Codice.StartsWith(p)))
            .Where(i => string.IsNullOrEmpty(searchCodice) || Match(i.Codice, searchCodice))
            .Where(i => string.IsNullOrEmpty(searchDescr) || Match(i.Descr, searchDescr))
            .Select(i => new AvailableItem { Id = i.Id, Codice = i.Codice, Descr = i.Descr, Source = "codex" })
            .OrderBy(i => i.Codice)
            .ToList();

        dgAvailable.ItemsSource = filtered;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshLeftPanel();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshAll();
    }

    private async Task RefreshAll()
    {
        await LoadAllItems();
        if (cmbType.SelectedItem is CompositionType type)
        {
            CmbType_SelectionChanged(cmbType, null!);
            if (_selectedParentId.HasValue)
                await LoadTree(_selectedParentId.Value);
        }
    }

    // ── TREE LOADING ────────────────────────────────────────

    private async Task LoadTree(int parentId)
    {
        tvComposition.Items.Clear();

        try
        {
            string json = await ApiClient.GetAsync($"/api/codex/compositions/tree/{parentId}");
            var response = JsonSerializer.Deserialize<ApiResponse<CompositionTreeNode>>(json, _jsonOpt);

            if (response?.Success == true && response.Data != null)
            {
                var rootNode = BuildTreeViewItem(response.Data, isRoot: true);
                rootNode.IsExpanded = true;
                tvComposition.Items.Add(rootNode);

                int count = CountNodes(response.Data) - 1; // escludi root
                txtStatus.Text = $"{count} componenti nella composizione";
            }
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore: {ex.Message}";
        }
    }

    // Colori sfondo per tipo codice (tenui, leggibili con testo nero)
    private static readonly Dictionary<string, Color> _nodeColors = new()
    {
        { "1", Color.FromRgb(0xDB, 0xED, 0xF8) }, // 1xx - azzurro chiaro
        { "2", Color.FromRgb(0xE8, 0xF5, 0xE9) }, // 2xx - verde chiaro
        { "3", Color.FromRgb(0xFF, 0xF3, 0xE0) }, // 3xx - arancio chiaro
        { "4", Color.FromRgb(0xF3, 0xE5, 0xF5) }, // 4xx - viola chiaro
        { "5", Color.FromRgb(0xFD, 0xF6, 0xD6) }, // 5xx - giallo chiaro
        { "6", Color.FromRgb(0xE0, 0xF2, 0xF1) }, // 6xx - teal chiaro
        { "7", Color.FromRgb(0xFC, 0xE4, 0xEC) }, // 7xx - rosa chiaro
    };

    private static Color GetNodeColor(string codice)
    {
        string prefix = codice.Length > 0 ? codice.Substring(0, 1) : "";
        return _nodeColors.TryGetValue(prefix, out var color) ? color : Color.FromRgb(0xF5, 0xF5, 0xF5);
    }

    private TreeViewItem BuildTreeViewItem(CompositionTreeNode node, bool isRoot = false, bool isEditable = true)
    {
        var bgColor = GetNodeColor(node.Codice);
        double fontSize = isRoot ? 16 : 13;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        // Icona tipo
        string icon = node.Source == "catalog" ? "🛒" :
                       node.Codice.StartsWith("7") ? "📦" :
                       node.Codice.StartsWith("6") ? "🔧" :
                       node.Codice.StartsWith("5") ? "⚙" : "🔩";

        panel.Children.Add(new TextBlock
        {
            Text = icon + " ",
            FontSize = fontSize,
            VerticalAlignment = VerticalAlignment.Center
        });

        // Codice + Descrizione
        panel.Children.Add(new TextBlock
        {
            Text = node.Codice,
            FontWeight = isRoot ? FontWeights.Bold : FontWeights.SemiBold,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x26)),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $" — {node.Descr}",
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Pulsante elimina (solo admin, non root, solo se editabile)
        if (!isRoot && isEditable && App.CurrentUser.IsAdmin)
        {
            var btnDelete = new Button
            {
                Content = "✕",
                Width = 22,
                Height = 22,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 10,
                Tag = node.CompositionId,
                ToolTip = "Rimuovi dalla composizione"
            };
            btnDelete.Click += BtnRemoveNode_Click;
            panel.Children.Add(btnDelete);
        }

        // Contenitore con sfondo colorato e bordo arrotondato
        var border = new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 1, 0, 1),
            Child = panel
        };

        var item = new TreeViewItem
        {
            Header = border,
            IsExpanded = true,
            Tag = node,
            AllowDrop = isEditable
        };

        // Drop su sotto-nodo (solo se editabile)
        if (isEditable)
        {
            item.DragOver += TreeViewItem_DragOver;
            item.Drop += TreeViewItem_Drop;
        }

        // Figli — se il nodo corrente è di un livello diverso dal root,
        // i suoi figli sono in sola lettura (es. figli di un 501 dentro un 601)
        string? rootPrefix = (cmbType.SelectedItem as CompositionType)?.Prefix;
        foreach (var child in node.Children)
        {
            // Un figlio è editabile solo se il suo parent è il root type
            // Es: in un 601, i 501 sono editabili (possono essere rimossi dal 601),
            // ma i figli dei 501 (101, 201...) NON sono editabili
            bool childEditable = isRoot; // solo i figli diretti del root sono editabili
            item.Items.Add(BuildTreeViewItem(child, isEditable: childEditable));
        }

        return item;
    }

    private int CountNodes(CompositionTreeNode node)
    {
        return 1 + node.Children.Sum(c => CountNodes(c));
    }

    // ── DRAG & DROP ─────────────────────────────────────────

    private void DgAvailable_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!App.CurrentUser.IsAdmin) return;
        if (_selectedParentId == null) return;
        if (dgAvailable.SelectedItem is not AvailableItem item) return;

        _ = HandleQuickAdd(_selectedParentId.Value, item);
    }

    private async Task HandleQuickAdd(int parentId, AvailableItem child)
    {
        try
        {
            var req = new AddCompositionRequest
            {
                ParentCodexId = parentId,
                ChildCodexId = child.Source == "codex" ? child.Id : null,
                ChildCatalogId = child.Source == "catalog" ? child.Id : null,
                Quantity = 1
            };
            string body = JsonSerializer.Serialize(req);
            string json = await ApiClient.PostAsync("/api/codex/compositions", body);
            var response = JsonSerializer.Deserialize<ApiResponse<int>>(json, _jsonOpt);

            if (response?.Success == true)
                await LoadTree(_selectedParentId!.Value);
            else
                MessageBox.Show(response?.Message ?? "Errore", "Attenzione",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DgAvailable_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void DgAvailable_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (!App.CurrentUser.IsAdmin) return;

        Point pos = e.GetPosition(null);
        Vector diff = _dragStartPoint - pos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (dgAvailable.SelectedItem is AvailableItem item)
            {
                var data = new DataObject(typeof(AvailableItem), item);
                DragDrop.DoDragDrop(dgAvailable, data, DragDropEffects.Copy);
            }
        }
    }

    private void TvComposition_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (!App.CurrentUser.IsAdmin || _selectedParentId == null) return;
        if (!e.Data.GetDataPresent(typeof(AvailableItem))) return;

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void TvComposition_Drop(object sender, DragEventArgs e)
    {
        if (!App.CurrentUser.IsAdmin || _selectedParentId == null) return;
        if (e.Data.GetData(typeof(AvailableItem)) is not AvailableItem droppedItem) return;

        // Drop sulla TreeView stessa = drop sul parent root
        _ = HandleDrop(_selectedParentId.Value, droppedItem);
        e.Handled = true;
    }

    private void TreeViewItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (!App.CurrentUser.IsAdmin) return;
        if (!e.Data.GetDataPresent(typeof(AvailableItem))) return;
        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CompositionTreeNode targetNode) return;

        // Valida se il target può ricevere il child
        if (e.Data.GetData(typeof(AvailableItem)) is AvailableItem item)
        {
            // Articoli catalogo accettati ovunque, codex validati con gerarchia
            string? error = item.Source == "catalog" ? null : ValidateDropLocal(targetNode.Codice, item.Codice);
            if (error == null)
            {
                e.Effects = DragDropEffects.Copy;

                // Highlight
                ClearHighlight();
                tvi.Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE7, 0xFF));
                _lastHighlighted = tvi;
            }
        }
    }

    private void TreeViewItem_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearHighlight();

        if (!App.CurrentUser.IsAdmin) return;
        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CompositionTreeNode targetNode) return;
        if (e.Data.GetData(typeof(AvailableItem)) is not AvailableItem droppedItem) return;

        _ = HandleDrop(targetNode.CodexId, droppedItem);
    }

    private void ClearHighlight()
    {
        if (_lastHighlighted != null)
        {
            _lastHighlighted.Background = Brushes.Transparent;
            _lastHighlighted = null;
        }
    }

    private async Task HandleDrop(int parentId, AvailableItem child)
    {
        // Chiedi quantità
        var dialog = new QuantityDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var req = new AddCompositionRequest
            {
                ParentCodexId = parentId,
                ChildCodexId = child.Source == "codex" ? child.Id : null,
                ChildCatalogId = child.Source == "catalog" ? child.Id : null,
                Quantity = dialog.Quantity
            };
            string body = JsonSerializer.Serialize(req);
            string json = await ApiClient.PostAsync("/api/codex/compositions", body);
            var response = JsonSerializer.Deserialize<ApiResponse<int>>(json, _jsonOpt);

            if (response?.Success == true)
            {
                await LoadTree(_selectedParentId!.Value);
            }
            else
            {
                MessageBox.Show(response?.Message ?? "Errore", "Attenzione",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── REMOVE NODE ─────────────────────────────────────────

    private async void BtnRemoveNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (_selectedParentId == null) return;

        int compositionId = (int)btn.Tag;

        var result = MessageBox.Show(
            "Rimuovere questo elemento dalla composizione?",
            "Conferma rimozione", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            string delJson = await ApiClient.DeleteAsync($"/api/codex/compositions/{compositionId}");
            var delResponse = JsonSerializer.Deserialize<ApiResponse<bool>>(delJson, _jsonOpt);

            if (delResponse?.Success == true)
                await LoadTree(_selectedParentId.Value);
            else
                MessageBox.Show(delResponse?.Message ?? "Errore", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── WILDCARD MATCH ─────────────────────────────────────

    private static bool Match(string? value, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        var v = value?.ToLower() ?? "";

        bool startsWild = filter.StartsWith('*');
        bool endsWild = filter.EndsWith('*');

        if (startsWild && endsWild)
            return v.Contains(filter.Trim('*'));
        if (endsWild)
            return v.StartsWith(filter.TrimEnd('*'));
        if (startsWild)
            return v.EndsWith(filter.TrimStart('*'));

        return v.Contains(filter);
    }

    // ── VALIDATION ──────────────────────────────────────────

    private static string? ValidateDropLocal(string targetCodice, string childCodice)
    {
        string targetPrefix = targetCodice.Substring(0, 1);
        string childPrefix = childCodice.Substring(0, 1);

        return targetPrefix switch
        {
            "5" => childPrefix is "1" or "2" or "3" or "4" ? null : "5xx accetta solo 1xx-4xx",
            "6" => childPrefix is "5" ? null : "6xx accetta solo 5xx",
            "7" => childPrefix is "6" ? null : "7xx accetta solo 6xx",
            _ => "Questo nodo non può contenere figli"
        };
    }

    // ── HELPER CLASSES ──────────────────────────────────────

    private class CompositionType
    {
        public string Prefix { get; }
        public string Description { get; }
        public string[] AllowedChildPrefixes { get; }
        public string Display => $"{Prefix} — {Description}";

        public CompositionType(string prefix, string description, string[] allowedChildPrefixes)
        {
            Prefix = prefix;
            Description = description;
            AllowedChildPrefixes = allowedChildPrefixes;
        }
    }

    private class CompositionParentItem
    {
        public int Id { get; }
        public string Codice { get; }
        public string Descr { get; }
        public string Display => $"{Codice} — {Descr}";

        public CompositionParentItem(int id, string codice, string descr)
        {
            Id = id;
            Codice = codice;
            Descr = descr;
        }
    }

    /// <summary>Wrapper unificato per articoli Codex e Catalogo nel DataGrid sinistro.</summary>
    public class AvailableItem
    {
        public int Id { get; set; }
        public string Codice { get; set; } = "";
        public string Descr { get; set; } = "";
        public string Source { get; set; } = "codex"; // "codex" o "catalog"
    }
}
