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
    private List<CompositionParentItem> _allParentItems = new();
    private int? _selectedParentId;
    private Point _dragStartPoint;
    private TreeViewItem? _lastHighlighted;
    private bool _suppressParentFilter;

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
        string search = txtSearch.Text.Trim().ToLower();

        var filtered = _allItems
            .Where(i => allowedPrefixes.Any(p => i.Codice.StartsWith(p)))
            .Where(i => string.IsNullOrEmpty(search) ||
                        Match(i.Codice, search) ||
                        Match(i.Descr, search))
            .OrderBy(i => i.Codice)
            .ToList();

        dgAvailable.ItemsSource = filtered;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (cmbType.SelectedItem is CompositionType type)
            FilterLeftPanel(type.AllowedChildPrefixes);
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

    private TreeViewItem BuildTreeViewItem(CompositionTreeNode node, bool isRoot = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        // Icona tipo
        string icon = node.Codice.StartsWith("7") ? "📦" :
                       node.Codice.StartsWith("6") ? "🔧" :
                       node.Codice.StartsWith("5") ? "⚙" : "🔩";

        panel.Children.Add(new TextBlock
        {
            Text = icon + " ",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });

        // Codice + Descrizione
        panel.Children.Add(new TextBlock
        {
            Text = node.Codice,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x26)),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $" — {node.Descr}",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x85)),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Quantità (non per root)
        if (!isRoot && node.Quantity > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"  (x{node.Quantity})",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0x6E, 0xF7)),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // Pulsante elimina (solo admin, non root)
        if (!isRoot && App.CurrentUser.IsAdmin)
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
                Tag = node.CodexId,
                ToolTip = "Rimuovi dalla composizione"
            };
            btnDelete.Click += BtnRemoveNode_Click;
            panel.Children.Add(btnDelete);
        }

        var item = new TreeViewItem
        {
            Header = panel,
            IsExpanded = true,
            Tag = node,
            AllowDrop = true
        };

        // Drop su sotto-nodo
        item.DragOver += TreeViewItem_DragOver;
        item.Drop += TreeViewItem_Drop;

        // Figli
        foreach (var child in node.Children)
            item.Items.Add(BuildTreeViewItem(child));

        return item;
    }

    private int CountNodes(CompositionTreeNode node)
    {
        return 1 + node.Children.Sum(c => CountNodes(c));
    }

    // ── DRAG & DROP ─────────────────────────────────────────

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
            if (dgAvailable.SelectedItem is CodexListItem item)
            {
                var data = new DataObject(typeof(CodexListItem), item);
                DragDrop.DoDragDrop(dgAvailable, data, DragDropEffects.Copy);
            }
        }
    }

    private void TvComposition_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (!App.CurrentUser.IsAdmin || _selectedParentId == null) return;
        if (!e.Data.GetDataPresent(typeof(CodexListItem))) return;

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void TvComposition_Drop(object sender, DragEventArgs e)
    {
        if (!App.CurrentUser.IsAdmin || _selectedParentId == null) return;
        if (e.Data.GetData(typeof(CodexListItem)) is not CodexListItem droppedItem) return;

        // Drop sulla TreeView stessa = drop sul parent root
        _ = HandleDrop(_selectedParentId.Value, droppedItem);
        e.Handled = true;
    }

    private void TreeViewItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (!App.CurrentUser.IsAdmin) return;
        if (!e.Data.GetDataPresent(typeof(CodexListItem))) return;
        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CompositionTreeNode targetNode) return;

        // Valida se il target può ricevere il child
        if (e.Data.GetData(typeof(CodexListItem)) is CodexListItem item)
        {
            string? error = ValidateDropLocal(targetNode.Codice, item.Codice);
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
        if (e.Data.GetData(typeof(CodexListItem)) is not CodexListItem droppedItem) return;

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

    private async Task HandleDrop(int parentId, CodexListItem child)
    {
        // Chiedi quantità
        var dialog = new QuantityDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var req = new AddCompositionRequest
            {
                ParentCodexId = parentId,
                ChildCodexId = child.Id,
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

        // Trova il composition id dalla relazione
        int childCodexId = (int)btn.Tag;

        // Cerca il composition id
        try
        {
            string json = await ApiClient.GetAsync($"/api/codex/compositions/{_selectedParentId.Value}");
            var response = JsonSerializer.Deserialize<ApiResponse<List<CompositionChildDto>>>(json, _jsonOpt);

            if (response?.Success != true) return;

            // Trova il nodo target nel parent attuale. Devo risalire trovando il parent
            // del nodo cliccato nell'albero
            var targetParentId = FindParentOfChild(childCodexId);
            if (targetParentId == null) return;

            string jsonChildren = await ApiClient.GetAsync($"/api/codex/compositions/{targetParentId.Value}");
            var responseChildren = JsonSerializer.Deserialize<ApiResponse<List<CompositionChildDto>>>(jsonChildren, _jsonOpt);
            if (responseChildren?.Success != true) return;

            var comp = responseChildren.Data?.FirstOrDefault(c => c.ChildCodexId == childCodexId);
            if (comp == null)
            {
                MessageBox.Show("Relazione non trovata", "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Rimuovere {comp.ChildCodice} dalla composizione?\n\n\"{comp.ChildDescr}\"",
                "Conferma rimozione", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            string delJson = await ApiClient.DeleteAsync($"/api/codex/compositions/{comp.Id}");
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

    private int? FindParentOfChild(int childCodexId)
    {
        // Cerca ricorsivamente nel tree il parent del nodo con CodexId == childCodexId
        if (tvComposition.Items.Count == 0) return null;
        if (tvComposition.Items[0] is not TreeViewItem rootTvi) return null;
        if (rootTvi.Tag is not CompositionTreeNode rootNode) return null;

        return FindParentInNode(rootNode, childCodexId);
    }

    private int? FindParentInNode(CompositionTreeNode node, int childCodexId)
    {
        foreach (var child in node.Children)
        {
            if (child.CodexId == childCodexId)
                return node.CodexId;
            var found = FindParentInNode(child, childCodexId);
            if (found.HasValue) return found;
        }
        return null;
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
}
