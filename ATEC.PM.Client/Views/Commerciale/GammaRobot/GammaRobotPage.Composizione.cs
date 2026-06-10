using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.Views;            // QuantityDialog
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Commerciale.GammaRobot;

// Editor "Composizione" (3ª tab) — distinta editabile drag&drop + CRUD anagrafica robot/quadro.
// Scrittura solo ADMIN (gate client + server [Authorize(Roles=ADMIN)]). Stile dell'editor Codex.
public partial class GammaRobotPage
{
    // Le 7 sezioni macro-gruppo (stesso ordine FIELD del server). Sempre mostrate, anche vuote.
    private static readonly string[] Sezioni =
        { "Schede", "Azionamenti", "Kit Cavi", "Motori", "Componenti meccanici", "Tastierino", "Ventole" };

    private List<GammaRobotDto> _editorRobots = new();
    private List<GammaDistintaItemDto> _distintaEditor = new();
    private GammaRobotDto? _selectedRobotEditor;
    private GammaQuadroDto? _selectedQuadroEditor;
    private bool _editorLoaded;
    private bool _editorCompLoaded;

    private Point _dragStartPointEditor;
    private TreeViewItem? _lastHighlightedEditor;

    // ── INIT / TAB ──────────────────────────────────────────

    private void InitComposizioneTab()
    {
        // La toolbar CRUD (e tutto il D&D) è riservata agli ADMIN; gli altri consultano in sola lettura.
        pnlEditorToolbar.Visibility = App.CurrentUser.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void TabComposizione_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(GammaTabKind.Composizione);
        if (_editorLoaded) return;
        _editorLoaded = true;
        await LoadEditorTree();
        await LoadEditorComponents();
    }

    // ── ALBERO ROBOT → QUADRO (sx-alto) ─────────────────────

    private async Task LoadEditorTree()
    {
        try
        {
            _editorRobots = await ApiClient.GetListAsync<GammaRobotDto>("/api/gamma-robot/robots");
            BuildEditorTree(txtEditorSearch.Text);
            txtEditorTreeStatus.Text = $"{_editorRobots.Count} robot";
        }
        catch (Exception ex)
        {
            txtEditorTreeStatus.Text = "Errore caricamento";
            ShowWarn($"Errore nel caricamento robot:\n{ex.Message}");
        }
    }

    private void BuildEditorTree(string? filter)
    {
        treeQuadriEditor.Items.Clear();
        IEnumerable<GammaRobotDto> robots = _editorRobots;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            string f = filter.Trim().ToLowerInvariant();
            robots = robots.Where(r => r.Modello.ToLowerInvariant().Contains(f)
                                       || (r.Serie ?? "").ToLowerInvariant().Contains(f));
        }

        foreach (GammaRobotDto robot in robots)
        {
            TreeViewItem node = new() { Header = $"{robot.Modello}  ({robot.QuadriCount})", Tag = robot };
            node.Items.Add(new TreeViewItem { Header = "..." });   // figlio fittizio → lazy load
            node.Expanded += EditorRobotNode_Expanded;
            treeQuadriEditor.Items.Add(node);
        }
    }

    private async void EditorRobotNode_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem node || node.Tag is not GammaRobotDto robot) return;
        if (node.Items.Count != 1 || node.Items[0] is not TreeViewItem { Tag: null }) return; // già caricato

        node.Items.Clear();
        List<GammaQuadroDto> quadri = await ApiClient.GetListAsync<GammaQuadroDto>(
            $"/api/gamma-robot/robots/{robot.Id}/quadri");

        foreach (GammaQuadroDto q in quadri)
            node.Items.Add(new TreeViewItem { Header = BuildQuadroLabel(q), Tag = q });   // helper nel file principale
        if (quadri.Count == 0)
            node.Items.Add(new TreeViewItem { Header = "(nessun quadro)", IsEnabled = false });
    }

    private async void TreeQuadriEditor_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item)
        {
            _selectedRobotEditor = null;
            _selectedQuadroEditor = null;
            return;
        }

        if (item.Tag is GammaRobotDto robot)
        {
            _selectedRobotEditor = robot;
            _selectedQuadroEditor = null;
            _distintaEditor = new();
            treeDistintaEditor.Items.Clear();
            txtEditorTreeHeader.Text = robot.Modello;
            txtEditorTreeSub.Text = "Seleziona un quadro per modificarne la distinta.";
            UpdateEditorFooter();
        }
        else if (item.Tag is GammaQuadroDto quadro)
        {
            _selectedRobotEditor = _editorRobots.FirstOrDefault(r => r.Id == quadro.RobotId) ?? _selectedRobotEditor;
            _selectedQuadroEditor = quadro;
            await ReloadDistintaEditor();
        }
    }

    private void TxtEditorSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtEditorSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtEditorSearch.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        if (_editorLoaded) BuildEditorTree(txtEditorSearch.Text);
    }

    // ── COMPONENTI DISPONIBILI (sx-basso, drag source) ──────

    private async Task LoadEditorComponents()
    {
        try
        {
            // _components è condiviso con la vista Magazzino: riusa se già caricato.
            if (_components.Count == 0)
                _components = await ApiClient.GetListAsync<GammaComponentDto>("/api/gamma-robot/components");
            _editorCompLoaded = true;
            ApplyEditorCompFilter(txtEditorCompSearch.Text);
        }
        catch (Exception ex)
        {
            txtEditorCompStatus.Text = "Errore caricamento";
            ShowWarn($"Errore nel caricamento componenti:\n{ex.Message}");
        }
    }

    private void ApplyEditorCompFilter(string? filter)
    {
        IEnumerable<GammaComponentDto> comps = _components;
        string f = (filter ?? "").Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(f))
            comps = comps.Where(c => WildMatch(c.Code, f) || WildMatch(c.Name, f) || WildMatch(c.Categoria, f));
        List<GammaComponentDto> list = comps.ToList();
        dgComponentiDisp.ItemsSource = list;
        txtEditorCompStatus.Text = $"{list.Count} componenti";
    }

    private void TxtEditorCompSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtEditorCompSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtEditorCompSearch.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        if (_editorCompLoaded) ApplyEditorCompFilter(txtEditorCompSearch.Text);
    }

    private void DgComponentiDisp_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStartPointEditor = e.GetPosition(null);

    private void DgComponentiDisp_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !App.CurrentUser.IsAdmin) return;
        Vector diff = _dragStartPointEditor - e.GetPosition(null);
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (dgComponentiDisp.SelectedItem is GammaComponentDto comp)
                DragDrop.DoDragDrop(dgComponentiDisp, new DataObject(typeof(GammaComponentDto), comp), DragDropEffects.Copy);
        }
    }

    // Doppio click = aggiunta rapida; Shift+doppio click = scheda prodotto catalogo.
    private void DgComponentiDisp_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (dgComponentiDisp.SelectedItem is not GammaComponentDto comp) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            OpenDescriptionFromDistintaRow(new GammaDistintaItemDto
            {
                ProductId = comp.ProductId,
                ProductCode = comp.Code,
                ProductName = comp.Name
            });
            return;
        }

        if (!App.CurrentUser.IsAdmin || _selectedQuadroEditor == null) return;
        _ = AddComponentAsync(comp, SezioneForCategoria(comp.Categoria), isAlternate: false, slot: null);
    }

    // ── DISTINTA EDITABILE (dx, drop target) ────────────────

    private async Task ReloadDistintaEditor()
    {
        if (_selectedQuadroEditor == null) return;
        _distintaEditor = await ApiClient.GetListAsync<GammaDistintaItemDto>(
            $"/api/gamma-robot/quadri/{_selectedQuadroEditor.Id}/distinta");
        txtEditorTreeHeader.Text = _selectedRobotEditor?.Modello ?? "Robot";
        txtEditorTreeSub.Text = BuildQuadroSubtitle(_selectedQuadroEditor);   // helper nel file principale
        BuildDistintaEditorTree();
        UpdateEditorFooter();
    }

    private void BuildDistintaEditorTree()
    {
        treeDistintaEditor.Items.Clear();
        if (_selectedQuadroEditor == null) return;

        foreach (string sezione in Sezioni)
        {
            List<GammaDistintaItemDto> sezItems = _distintaEditor.Where(d => (d.Sezione ?? "") == sezione).ToList();
            TreeViewItem sezNode = BuildSezioneNode(sezione, sezItems.Count);

            // Raggruppa per slot: principale + alternative annidate.
            foreach (IGrouping<string?, GammaDistintaItemDto> slotGrp in sezItems.GroupBy(i => i.Slot))
            {
                List<GammaDistintaItemDto> g = slotGrp.ToList();
                GammaDistintaItemDto principal = g.FirstOrDefault(x => !x.IsAlternate) ?? g[0];
                TreeViewItem pNode = BuildDistintaNode(principal, isPrincipal: true);
                foreach (GammaDistintaItemDto alt in g.Where(x => x != principal))
                    pNode.Items.Add(BuildDistintaNode(alt, isPrincipal: false));
                sezNode.Items.Add(pNode);
            }
            treeDistintaEditor.Items.Add(sezNode);
        }
    }

    private void UpdateEditorFooter()
    {
        if (_selectedQuadroEditor == null)
        {
            txtEditorFooterCount.Text = "";
            txtEditorFooterTotal.Text = "";
            return;
        }
        int basics = _distintaEditor.Count(d => !d.IsAlternate && !d.IsOptional);
        int opz = _distintaEditor.Count(d => !d.IsAlternate && d.IsOptional);
        int alt = _distintaEditor.Count(d => d.IsAlternate);
        decimal totBase = _distintaEditor.Where(d => !d.IsAlternate && !d.IsOptional && d.PrezzoVb.HasValue).Sum(d => d.PrezzoVb!.Value);
        decimal totOpz = _distintaEditor.Where(d => !d.IsAlternate && d.IsOptional && d.PrezzoVb.HasValue).Sum(d => d.PrezzoVb!.Value);

        txtEditorFooterCount.Text = $"{basics} componenti"
            + (alt > 0 ? $"  ·  {alt} alternative" : "")
            + (opz > 0 ? $"  ·  {opz} opzioni" : "");
        txtEditorFooterTotal.Text = opz > 0
            ? $"VB base: {totBase:N2} €    ·    +opzioni: {(totBase + totOpz):N2} €"
            : $"VB: {totBase:N2} €";
    }

    // ── COSTRUZIONE NODI ────────────────────────────────────

    private TreeViewItem BuildSezioneNode(string sezione, int count)
    {
        StackPanel panel = new() { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = sezione,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x3A, 0x5C)),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"  ({count})",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            VerticalAlignment = VerticalAlignment.Center
        });

        Border border = new()
        {
            Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xFF)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 3, 0, 1),
            Child = panel
        };
        TreeViewItem item = new() { Header = border, IsExpanded = true, Tag = sezione };
        if (App.CurrentUser.IsAdmin)
        {
            item.AllowDrop = true;
            item.DragOver += SezioneNode_DragOver;
            item.Drop += SezioneNode_Drop;
        }
        return item;
    }

    private TreeViewItem BuildDistintaNode(GammaDistintaItemDto row, bool isPrincipal)
    {
        StackPanel panel = new() { Orientation = Orientation.Horizontal };

        if (row.IsAlternate)
            panel.Children.Add(MakeBadge("ALT", Color.FromRgb(0xD9, 0x77, 0x06), Color.FromRgb(0xFE, 0xF3, 0xC7)));
        if (row.IsOptional)
            panel.Children.Add(MakeBadge("OPT", Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0xED, 0xE9, 0xFE)));

        string code = row.ProductCode ?? row.CodeRaw ?? "?";
        string name = row.ProductName ?? row.CodeRaw ?? "";
        panel.Children.Add(new TextBlock
        {
            Text = code,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x26)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = $" — {name}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 300
        });
        if (row.Qty > 1)
            panel.Children.Add(new TextBlock
            {
                Text = $"  ×{row.Qty}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                VerticalAlignment = VerticalAlignment.Center
            });

        if (App.CurrentUser.IsAdmin)
        {
            if (isPrincipal)
            {
                Button btnMenu = new()
                {
                    Content = "▾",
                    Width = 22,
                    Height = 20,
                    Margin = new Thickness(8, 0, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    FontSize = 10,
                    ToolTip = "Opzioni"
                };
                ContextMenu cm = new();
                MenuItem miOpt = new() { Header = row.IsOptional ? "Togli opzione" : "Segna come opzione", Tag = row };
                miOpt.Click += MiToggleOptional_Click;
                MenuItem miQty = new() { Header = "Modifica quantità...", Tag = row };
                miQty.Click += MiEditQty_Click;
                cm.Items.Add(miOpt);
                cm.Items.Add(miQty);
                btnMenu.ContextMenu = cm;
                btnMenu.Click += (s, _) =>
                {
                    if (s is Button b && b.ContextMenu != null)
                    {
                        b.ContextMenu.PlacementTarget = b;
                        b.ContextMenu.IsOpen = true;
                    }
                };
                panel.Children.Add(btnMenu);
            }

            Button btnDel = new()
            {
                Content = "✕",
                Width = 22,
                Height = 20,
                Margin = new Thickness(4, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 10,
                Tag = row,
                ToolTip = "Rimuovi dalla distinta"
            };
            btnDel.Click += BtnRemoveDistinta_Click;
            panel.Children.Add(btnDel);
        }

        Border border = new()
        {
            Background = new SolidColorBrush(row.IsAlternate ? Color.FromRgb(0xFB, 0xFB, 0xFD) : Color.FromRgb(0xF7, 0xF8, 0xFA)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 1, 0, 1),
            Child = panel,
            ToolTip = "Doppio click per aprire la scheda prodotto (catalogo)"
        };
        border.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ClickCount == 2)
            {
                args.Handled = true;
                OpenDescriptionFromDistintaRow(row);
            }
        };
        TreeViewItem item = new() { Header = border, IsExpanded = true, Tag = row };
        if (isPrincipal && App.CurrentUser.IsAdmin)
        {
            item.AllowDrop = true;
            item.DragOver += PrincipaleNode_DragOver;
            item.Drop += PrincipaleNode_Drop;
        }
        return item;
    }

    private static Border MakeBadge(string text, Color fg, Color bg) => new()
    {
        Background = new SolidColorBrush(bg),
        Padding = new Thickness(5, 1, 5, 1),
        Margin = new Thickness(0, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(fg) }
    };

    // ── DRAG & DROP (drop target = sezione → principale; principale → alternativa) ──

    private void TreeDistintaEditor_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (App.CurrentUser.IsAdmin && _selectedQuadroEditor != null && e.Data.GetDataPresent(typeof(GammaComponentDto)))
            e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    // Drop su spazio vuoto dell'albero → sezione = categoria del componente.
    private void TreeDistintaEditor_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearHighlightEditor();
        if (!App.CurrentUser.IsAdmin || _selectedQuadroEditor == null) return;
        if (e.Data.GetData(typeof(GammaComponentDto)) is not GammaComponentDto comp) return;
        _ = AddComponentAsync(comp, SezioneForCategoria(comp.Categoria), isAlternate: false, slot: null);
    }

    private void SezioneNode_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (!App.CurrentUser.IsAdmin || _selectedQuadroEditor == null) return;
        if (!e.Data.GetDataPresent(typeof(GammaComponentDto))) return;
        e.Effects = DragDropEffects.Copy;
        if (sender is TreeViewItem tvi) HighlightNode(tvi);
    }

    private void SezioneNode_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearHighlightEditor();
        if (!App.CurrentUser.IsAdmin || _selectedQuadroEditor == null) return;
        if (sender is not TreeViewItem tvi || tvi.Tag is not string sezione) return;
        if (e.Data.GetData(typeof(GammaComponentDto)) is not GammaComponentDto comp) return;
        _ = AddComponentAsync(comp, sezione, isAlternate: false, slot: null);
    }

    private void PrincipaleNode_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (!App.CurrentUser.IsAdmin || _selectedQuadroEditor == null) return;
        if (!e.Data.GetDataPresent(typeof(GammaComponentDto))) return;
        e.Effects = DragDropEffects.Copy;
        if (sender is TreeViewItem tvi) HighlightNode(tvi);
    }

    // Drop su un principale → alternativa: stessa sezione e slot del principale.
    private void PrincipaleNode_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearHighlightEditor();
        if (!App.CurrentUser.IsAdmin || _selectedQuadroEditor == null) return;
        if (sender is not TreeViewItem tvi || tvi.Tag is not GammaDistintaItemDto principal) return;
        if (e.Data.GetData(typeof(GammaComponentDto)) is not GammaComponentDto comp) return;
        _ = AddComponentAsync(comp, principal.Sezione ?? Sezioni[0], isAlternate: true, slot: principal.Slot);
    }

    private void HighlightNode(TreeViewItem tvi)
    {
        if (ReferenceEquals(_lastHighlightedEditor, tvi)) return;
        ClearHighlightEditor();
        if (tvi.Header is Border b)
        {
            b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0x6E, 0xF7));
            b.BorderThickness = new Thickness(1.5);
        }
        _lastHighlightedEditor = tvi;
    }

    private void ClearHighlightEditor()
    {
        if (_lastHighlightedEditor?.Header is Border b)
        {
            b.BorderBrush = System.Windows.Media.Brushes.Transparent;
            b.BorderThickness = new Thickness(0);
        }
        _lastHighlightedEditor = null;
    }

    // ── CRUD DISTINTA ───────────────────────────────────────

    private async Task AddComponentAsync(GammaComponentDto comp, string sezione, bool isAlternate, string? slot)
    {
        if (_selectedQuadroEditor == null) return;

        QuantityDialog dlg = new() { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        GammaDistintaAddRequest req = new()
        {
            QuadroId = _selectedQuadroEditor.Id,
            ProductId = comp.ProductId,
            Sezione = sezione,
            Slot = slot,
            Qty = dlg.Quantity,
            IsAlternate = isAlternate,
            IsOptional = false
        };
        string json = await ApiClient.PostAsync("/api/gamma-robot/distinta", JsonSerializer.Serialize(req));
        if (!CheckOk(json, out string msg)) { ShowWarn(msg); return; }
        await ReloadDistintaEditor();
    }

    private async void BtnRemoveDistinta_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GammaDistintaItemDto row) return;

        List<GammaDistintaItemDto> sameSlot = _distintaEditor
            .Where(d => d.Sezione == row.Sezione && d.Slot == row.Slot).ToList();
        bool isPrincipal = !row.IsAlternate;
        int altCount = isPrincipal ? sameSlot.Count(d => d.IsAlternate) : 0;

        string question = isPrincipal && altCount > 0
            ? $"Rimuovere «{row.ProductCode}» e le sue {altCount} alternative?"
            : $"Rimuovere «{row.ProductCode}» dalla distinta?";
        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show(question, "Conferma rimozione",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        // Principale → elimina tutto lo slot (principale + alternative); alternativa → solo la riga.
        List<GammaDistintaItemDto> toDelete = isPrincipal ? sameSlot : new List<GammaDistintaItemDto> { row };
        foreach (GammaDistintaItemDto d in toDelete)
            await ApiClient.DeleteAsync($"/api/gamma-robot/distinta/{d.Id}");
        await ReloadDistintaEditor();
    }

    private async void MiToggleOptional_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not GammaDistintaItemDto row) return;
        GammaDistintaUpdateRequest req = new() { IsOptional = !row.IsOptional };
        string json = await ApiClient.PutAsync($"/api/gamma-robot/distinta/{row.Id}", JsonSerializer.Serialize(req));
        if (!CheckOk(json, out string msg)) { ShowWarn(msg); return; }
        await ReloadDistintaEditor();
    }

    private async void MiEditQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not GammaDistintaItemDto row) return;
        QuantityDialog dlg = new() { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        GammaDistintaUpdateRequest req = new() { Qty = dlg.Quantity };
        string json = await ApiClient.PutAsync($"/api/gamma-robot/distinta/{row.Id}", JsonSerializer.Serialize(req));
        if (!CheckOk(json, out string msg)) { ShowWarn(msg); return; }
        await ReloadDistintaEditor();
    }

    // ── CRUD ROBOT / QUADRO (toolbar) ───────────────────────

    private async void BtnAddRobot_Click(object sender, RoutedEventArgs e)
    {
        GammaRobotDialog dlg = new() { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        string json = await ApiClient.PostAsync("/api/gamma-robot/robots", JsonSerializer.Serialize(dlg.Result));
        if (!CheckOk(json, out string msg)) { ShowWarn(msg); return; }
        await LoadEditorTree();
    }

    private async void BtnAddQuadro_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRobotEditor == null) { ShowWarn("Seleziona prima un robot nell'albero."); return; }
        GammaQuadroDialog dlg = new() { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        string json = await ApiClient.PostAsync(
            $"/api/gamma-robot/robots/{_selectedRobotEditor.Id}/quadri", JsonSerializer.Serialize(dlg.Result));
        if (!CheckOk(json, out string msg)) { ShowWarn(msg); return; }
        await LoadEditorTree();
    }

    private async void BtnEditNode_Click(object sender, RoutedEventArgs e)
    {
        if (treeQuadriEditor.SelectedItem is not TreeViewItem item)
        {
            ShowWarn("Seleziona un robot o un quadro da modificare.");
            return;
        }

        if (item.Tag is GammaRobotDto robot)
        {
            GammaRobotDialog dlg = new(new GammaRobotSaveRequest
            {
                Modello = robot.Modello, Serie = robot.Serie, Brand = robot.Brand, Note = robot.Note
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;
            string json = await ApiClient.PutAsync($"/api/gamma-robot/robots/{robot.Id}", JsonSerializer.Serialize(dlg.Result));
            if (!CheckOk(json, out string msg)) { ShowWarn(msg); return; }
            await LoadEditorTree();
        }
        else if (item.Tag is GammaQuadroDto quadro)
        {
            GammaQuadroDialog dlg = new(new GammaQuadroSaveRequest
            {
                Controllore = quadro.Controllore, Generazione = quadro.Generazione, Payload = quadro.Payload,
                AreaLavoro = quadro.AreaLavoro, OsVersion = quadro.OsVersion, SystemKey = quadro.SystemKey, Note = quadro.Note
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;
            string json = await ApiClient.PutAsync($"/api/gamma-robot/quadri/{quadro.Id}", JsonSerializer.Serialize(dlg.Result));
            if (!CheckOk(json, out string msg)) { ShowWarn(msg); return; }
            await LoadEditorTree();
            // se stavo editando il quadro aperto, ricarica anche la distinta (sottotitolo)
            if (_selectedQuadroEditor?.Id == quadro.Id)
            {
                _selectedQuadroEditor = new GammaQuadroDto
                {
                    Id = quadro.Id, RobotId = quadro.RobotId,
                    Controllore = dlg.Result.Controllore, Generazione = dlg.Result.Generazione,
                    Payload = dlg.Result.Payload, AreaLavoro = dlg.Result.AreaLavoro,
                    OsVersion = dlg.Result.OsVersion, SystemKey = dlg.Result.SystemKey, Note = dlg.Result.Note
                };
                await ReloadDistintaEditor();
            }
        }
    }

    private async void BtnDeleteNode_Click(object sender, RoutedEventArgs e)
    {
        if (treeQuadriEditor.SelectedItem is not TreeViewItem item)
        {
            ShowWarn("Seleziona un robot o un quadro da eliminare.");
            return;
        }

        if (item.Tag is GammaRobotDto robot)
        {
            if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Eliminare il robot «{robot.Modello}»?", "Conferma eliminazione",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            string json = await ApiClient.DeleteAsync($"/api/gamma-robot/robots/{robot.Id}");
            if (!CheckOk(json, out string msg)) { ShowWarn(msg); return; }
            ClearEditorSelection();
            await LoadEditorTree();
        }
        else if (item.Tag is GammaQuadroDto quadro)
        {
            if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Eliminare il quadro selezionato e tutta la sua distinta?", "Conferma eliminazione",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            string json = await ApiClient.DeleteAsync($"/api/gamma-robot/quadri/{quadro.Id}");
            if (!CheckOk(json, out string msg)) { ShowWarn(msg); return; }
            ClearEditorSelection();
            await LoadEditorTree();
        }
    }

    private void ClearEditorSelection()
    {
        _selectedQuadroEditor = null;
        _selectedRobotEditor = null;
        _distintaEditor = new();
        treeDistintaEditor.Items.Clear();
        txtEditorTreeHeader.Text = "Seleziona un quadro";
        txtEditorTreeSub.Text = "Scegli un quadro a sinistra, poi trascina i componenti nelle sezioni.";
        UpdateEditorFooter();
    }

    // ── HELPER ──────────────────────────────────────────────

    private static string SezioneForCategoria(string? categoria)
        => !string.IsNullOrWhiteSpace(categoria) && Sezioni.Contains(categoria) ? categoria! : Sezioni[0];

    private static bool CheckOk(string json, out string message) => ApiClient.IsApiSuccess(json, out message);

    private void ShowWarn(string message) => ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
        string.IsNullOrWhiteSpace(message) ? "Operazione non riuscita." : message,
        "Gamma Robot", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static bool WildMatch(string? value, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        string v = value?.ToLowerInvariant() ?? "";
        bool startsWild = filter.StartsWith('*');
        bool endsWild = filter.EndsWith('*');
        if (startsWild && endsWild) return v.Contains(filter.Trim('*'));
        if (endsWild) return v.StartsWith(filter.TrimEnd('*'));
        if (startsWild) return v.EndsWith(filter.TrimStart('*'));
        return v.Contains(filter);
    }
}
