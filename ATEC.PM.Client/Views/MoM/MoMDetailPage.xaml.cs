using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.MoM;

internal enum MomSortColumn
{
    Default,
    Id,
    Attivita,
    Descrizione,
    Azione,
    Priorita,
    Responsabile,
    DataCheck,
    DataClose
}

// Pagina DETTAGLIO di un verbale: tabella righe (azioni) con autosave inline.
// Raggiunta da MoMPage via NavigationService.Navigate; "← Riepilogo" = GoBack().
public partial class MoMDetailPage : Page
{
    private readonly MoMVerbaleNode _node;
    private readonly ObservableCollection<MoMActionItemVM> _rows = new();
    private List<LookupItem> _employees = new();
    private List<LookupItem> _wildcards = new();
    private bool _dataLoading;
    private bool _rebuildingRows;
    private bool _addingItem;
    private readonly Dictionary<MoMActionItemVM, DispatcherTimer> _textSaveTimers = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private int _rebuildDepth;
    private int _saveInFlight;
    private MomSortColumn _sortColumn = MomSortColumn.Default;
    private bool _sortAscending = true;

    public MoMDetailPage(MoMVerbaleNode node)
    {
        InitializeComponent();
        _node = node;
        DataContext = node;
        icRows.ItemsSource = _rows;
        UpdateSortHeaderLabels();
        MomDebugTrace.Info($"MoMDetailPage ctor verbaleId={node.Id} title=\"{node.Title}\"");
        Loaded += async (_, _) => await LoadAsyncSafe();
    }

    private async Task LoadAsyncSafe()
    {
        try { await LoadAsync(); }
        catch (Exception ex)
        {
            MomDebugTrace.Error(ex, "LoadAsync fallito");
            txtStatus.Text = $"Errore: {ex.Message}";
        }
    }

    // ── Caricamento ─────────────────────────────────────────────

    private async Task LoadAsync()
    {
        _dataLoading = true;
        MomDebugTrace.Event("LoadAsync", "START");
        try
        {
            _employees = await ApiClient.GetListAsync<LookupItem>("/api/mom/lookups/employees");
            _wildcards = await ApiClient.GetListAsync<LookupItem>("/api/mom/lookups/wildcards");

            ApiResponse<MoMDetailDto>? api = await ApiClient.GetApiAsync<MoMDetailDto>($"/api/mom/{_node.Id}");
            if (api?.Success == true && api.Data != null)
            {
                _node.UpdateHeader(api.Data);
                _node.SetItems(api.Data.Items);
                foreach (MoMActionItemVM vm in _node.AllItems)
                    vm.SetEmployeeSources(_employees, _wildcards);
                MomDebugTrace.Event("LoadAsync", $"OK items={api.Data.Items.Count}");
            }
            else
            {
                MomDebugTrace.Warn($"LoadAsync API fail: {api?.Message ?? "risposta vuota"}");
                txtStatus.Text = $"Errore azioni: {api?.Message ?? "risposta vuota"}";
            }

            RebuildRows("LoadAsync");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            txtStatus.Text = $"{_rows.Count} righe";
        }
        catch (Exception ex)
        {
            MomDebugTrace.Error(ex, "LoadAsync");
            throw;
        }
        finally
        {
            _dataLoading = false;
            MomDebugTrace.Event("LoadAsync", "END");
        }
    }

    // Ordinamento prototipo: Critiche(rosse) → Aperte → Stand-by(gialle) → Chiuse(verdi); dentro, per priorità.
    private static int Rank(MoMActionItemVM i) =>
        (i.IsCritical && i.Status != "CLOSED") ? 0 : i.Status == "OPEN" ? 1 : i.Status == "STANDBY" ? 2 : 3;

    private List<MoMActionItemVM> OrderRows(IEnumerable<MoMActionItemVM> items)
    {
        List<MoMActionItemVM> source = items.ToList();
        if (_sortColumn == MomSortColumn.Default)
            return source.OrderBy(Rank).ThenBy(i => i.Priorita).ThenBy(i => i.Id).ToList();

        IOrderedEnumerable<MoMActionItemVM> ordered = _sortColumn switch
        {
            MomSortColumn.Id => _sortAscending
                ? source.OrderBy(i => i.Id)
                : source.OrderByDescending(i => i.Id),
            MomSortColumn.Attivita => _sortAscending
                ? source.OrderBy(i => i.Attivita, StringComparer.OrdinalIgnoreCase)
                : source.OrderByDescending(i => i.Attivita, StringComparer.OrdinalIgnoreCase),
            MomSortColumn.Descrizione => _sortAscending
                ? source.OrderBy(i => i.Descrizione ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : source.OrderByDescending(i => i.Descrizione ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            MomSortColumn.Azione => _sortAscending
                ? source.OrderBy(i => i.Azione ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : source.OrderByDescending(i => i.Azione ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            MomSortColumn.Priorita => _sortAscending
                ? source.OrderBy(i => i.Priorita)
                : source.OrderByDescending(i => i.Priorita),
            MomSortColumn.Responsabile => _sortAscending
                ? source.OrderBy(i => ResponsibleSortKey(i), StringComparer.OrdinalIgnoreCase)
                : source.OrderByDescending(i => ResponsibleSortKey(i), StringComparer.OrdinalIgnoreCase),
            MomSortColumn.DataCheck => _sortAscending
                ? source.OrderBy(i => i.DataCheck ?? DateTime.MaxValue)
                : source.OrderByDescending(i => i.DataCheck ?? DateTime.MinValue),
            MomSortColumn.DataClose => _sortAscending
                ? source.OrderBy(i => i.DataClose ?? DateTime.MaxValue)
                : source.OrderByDescending(i => i.DataClose ?? DateTime.MinValue),
            _ => source.OrderBy(Rank).ThenBy(i => i.Priorita).ThenBy(i => i.Id)
        };

        return ordered.ThenBy(i => i.Id).ToList();
    }

    private static string ResponsibleSortKey(MoMActionItemVM vm) =>
        string.Join(", ", vm.AllResponsibleNames);

    private void ColHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag)
            return;

        if (!Enum.TryParse(tag, out MomSortColumn column))
            return;

        if (_sortColumn == column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }

        UpdateSortHeaderLabels();
        RebuildRows(nameof(ColHeader_Click));
    }

    private void UpdateSortHeaderLabels()
    {
        SetSortHeader(btnSortId, "#", MomSortColumn.Id);
        SetSortHeader(btnSortAttivita, "ATTIVITÀ", MomSortColumn.Attivita);
        SetSortHeader(btnSortDescrizione, "DESCRIZIONE", MomSortColumn.Descrizione);
        SetSortHeader(btnSortAzione, "AZIONE", MomSortColumn.Azione);
        SetSortHeader(btnSortPriorita, "PRIORITÀ", MomSortColumn.Priorita);
        SetSortHeader(btnSortResponsabile, "RESPONSABILE", MomSortColumn.Responsabile);
        SetSortHeader(btnSortDataCheck, "DATA CHECK", MomSortColumn.DataCheck);
        SetSortHeader(btnSortDataClose, "DATA CLOSE", MomSortColumn.DataClose);
    }

    private void SetSortHeader(Button button, string label, MomSortColumn column)
    {
        string text = label;
        if (_sortColumn == column)
            text += _sortAscending ? " ▲" : " ▼";
        button.Content = text;
    }

    private void RebuildRows(string reason)
    {
        _rebuildDepth++;
        if (_rebuildDepth > 8)
            MomDebugTrace.Warn($"RebuildRows depth={_rebuildDepth} reason={reason} — possibile ricorsione");

        _rebuildingRows = true;
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            int before = _rows.Count;
            _rows.Clear();
            List<MoMActionItemVM> ordered = OrderRows(_node.AllItems);
            int n = 1;
            foreach (MoMActionItemVM vm in ordered) { vm.RowNo = n++; _rows.Add(vm); }
            MomDebugTrace.Event("RebuildRows",
                $"reason={reason} before={before} after={_rows.Count} sort={_sortColumn} asc={_sortAscending} depth={_rebuildDepth} ms={sw.ElapsedMilliseconds}");
        }
        finally
        {
            _rebuildDepth--;
            // Le ComboBox ricreate scatenano SelectionChanged dopo il return: teniamo il flag
            // fino al prossimo layout pass, altrimenti loop infinito save→rebuild→save.
            Dispatcher.BeginInvoke(() => _rebuildingRows = false, DispatcherPriority.Loaded);
        }
    }

    private static bool RowOrderChanged(IReadOnlyList<MoMActionItemVM> before, IReadOnlyList<MoMActionItemVM> after)
    {
        if (before.Count != after.Count) return true;
        for (int i = 0; i < before.Count; i++)
            if (before[i].Id != after[i].Id) return true;
        return false;
    }

    // ── Navigazione ─────────────────────────────────────────────

    private async void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await FlushPendingSavesAsync();
            _node.RefreshCounts();
            if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        }
        catch (Exception ex) { MomDebugTrace.Error(ex, "BtnBack_Click"); ShowError(ex.Message); }
    }

    private async Task FlushPendingSavesAsync()
    {
        List<MoMActionItemVM> pending = _textSaveTimers.Keys.ToList();
        foreach (DispatcherTimer t in _textSaveTimers.Values) t.Stop();
        _textSaveTimers.Clear();
        foreach (MoMActionItemVM vm in pending)
            if (!string.IsNullOrWhiteSpace(vm.Attivita)) await SaveActionItem(vm.Model, caller: nameof(FlushPendingSavesAsync));
    }

    // ── Stato / data riunione ───────────────────────────────────

    private async void BtnRowCritica_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not MoMActionItemVM vm) return;
            vm.IsCritical = !vm.IsCritical;
            await SaveActionItem(vm.Model, caller: nameof(BtnRowCritica_Click));
            RebuildRows(nameof(BtnRowCritica_Click));
        }
        catch (Exception ex) { MomDebugTrace.Error(ex, nameof(BtnRowCritica_Click)); ShowError(ex.Message); }
    }

    private async void BtnRowStandby_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not MoMActionItemVM vm) return;
            vm.Status = vm.Status == "STANDBY" ? "OPEN" : "STANDBY";
            await SaveActionItem(vm.Model, caller: nameof(BtnRowStandby_Click));
            RebuildRows(nameof(BtnRowStandby_Click));
        }
        catch (Exception ex) { MomDebugTrace.Error(ex, nameof(BtnRowStandby_Click)); ShowError(ex.Message); }
    }

    private async void RowPriority_Changed(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_dataLoading)
            {
                MomDebugTrace.Event("RowPriority_Changed", "SKIP dataLoading");
                return;
            }
            if (_rebuildingRows)
            {
                MomDebugTrace.Event("RowPriority_Changed", "SKIP rebuildingRows");
                return;
            }
            if (e.AddedItems.Count == 0)
            {
                MomDebugTrace.Event("RowPriority_Changed", $"SKIP no AddedItems removed={e.RemovedItems.Count}");
                return;
            }
            // Rebind post-RebuildRows: AddedItems sì, RemovedItems no → ignora (vedi log loop).
            if (e.RemovedItems.Count == 0)
            {
                MomDebugTrace.Event("RowPriority_Changed", "SKIP programmatic rebind (removed=0)");
                return;
            }
            if (sender is not ComboBox cb || cb.DataContext is not MoMActionItemVM vm)
            {
                MomDebugTrace.Warn("RowPriority_Changed: sender/DataContext invalido");
                return;
            }

            List<MoMActionItemVM> orderBefore = _rows.ToList();
            MomDebugTrace.Event("RowPriority_Changed",
                $"PROCESS itemId={vm.Id} priorita={vm.Priorita} added={e.AddedItems.Count} removed={e.RemovedItems.Count}");

            await SaveActionItem(vm.Model, caller: nameof(RowPriority_Changed));

            List<MoMActionItemVM> orderAfter = OrderRows(_node.AllItems);
            if (RowOrderChanged(orderBefore, orderAfter))
                RebuildRows(nameof(RowPriority_Changed));
            else
                MomDebugTrace.Event("RowPriority_Changed", "SKIP RebuildRows (ordine invariato)");
        }
        catch (Exception ex)
        {
            MomDebugTrace.Error(ex, "RowPriority_Changed");
            ShowError(ex.Message);
        }
    }

    private async void DetailMeetingDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_dataLoading) return;
            MomDebugTrace.Event("DetailMeetingDate_Changed", $"date={_node.MeetingDate:yyyy-MM-dd}");
            MoMSaveRequest req = new MoMSaveRequest
            {
                Tipo = _node.Tipo,
                ProjectId = _node.ProjectId,
                Title = _node.Title,
                MeetingDate = _node.MeetingDate,
                InDashboard = _node.InDashboard
            };
            string resp = await ApiClient.PutAsync($"/api/mom/{_node.Id}", JsonSerializer.Serialize(req));
            if (ApiClient.IsApiSuccess(resp, out string msg)) txtStatus.Text = "Salvato"; else ShowError(msg);
        }
        catch (Exception ex) { MomDebugTrace.Error(ex, nameof(DetailMeetingDate_Changed)); ShowError(ex.Message); }
    }

    // ── Righe ───────────────────────────────────────────────────

    private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+N = nuova riga
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = AddRowAsync();
            return;
        }
        // Esc = ← Riepilogo
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            BtnBack_Click(this, new RoutedEventArgs());
            return;
        }
        // Canc = elimina la riga del controllo a fuoco, MA non se si sta scrivendo in un TextBox
        // (lì Canc deve cancellare il carattere). Il dialog di conferma protegge da cancellazioni accidentali.
        if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox
            && Keyboard.FocusedElement is FrameworkElement fe && fe.DataContext is MoMActionItemVM)
        {
            e.Handled = true;
            BtnRowDelete_Click(fe, new RoutedEventArgs());
        }
    }

    private async void BtnAddRow_Click(object sender, RoutedEventArgs e) => await AddRowAsync();

    private async Task AddRowAsync()
    {
        if (_addingItem) return;
        _addingItem = true;
        _dataLoading = true;
        txtStatus.Text = "Creazione riga...";
        try
        {
            MoMActionItemDto newItem = new MoMActionItemDto
            {
                MomId = _node.Id,
                Attivita = "Nuova azione",
                Priorita = 2,
                Status = "OPEN",
                IsCritical = false
            };
            MoMActionItemSaveRequest req = BuildSaveRequest(newItem);
            string resp = await ApiClient.PostAsync($"/api/mom/{_node.Id}/items", JsonSerializer.Serialize(req));
            if (!ApiClient.TryGetApiData(resp, out int newId, out string msg))
            {
                ShowError(string.IsNullOrWhiteSpace(msg) ? "Riga non salvata dal server." : msg);
                return;
            }
            newItem.Id = newId;
            MoMActionItemVM vm = new MoMActionItemVM(newItem) { SuppressAutoSave = true };
            vm.SetEmployeeSources(_employees, _wildcards);
            _node.AllItems.Add(vm);
            _node.RefreshCounts();
            RebuildRows(nameof(AddRowAsync));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            vm.SuppressAutoSave = false;
            txtStatus.Text = "Riga creata";
            FocusRowAttivita(vm);   // #3: cursore sulla nuova riga, testo selezionato → scrivi subito
        }
        catch (Exception ex)
        {
            MomDebugTrace.Error(ex, nameof(AddRowAsync));
            ShowError(ex.Message);
        }
        finally { _dataLoading = false; _addingItem = false; }
    }

    // #3: porta il fuoco sulla casella Attività della riga indicata e ne seleziona il testo.
    private void FocusRowAttivita(MoMActionItemVM vm)
    {
        try
        {
            if (icRows.ItemContainerGenerator.ContainerFromItem(vm) is not DependencyObject container) return;
            TextBox? tb = FindDescendant<TextBox>(container, t => (t.Tag as string) == "Attivita");
            if (tb != null) { tb.Focus(); tb.SelectAll(); }
        }
        catch (Exception ex) { MomDebugTrace.Warn($"FocusRowAttivita: {ex.Message}"); }
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t && (match == null || match(t))) return t;
            T? found = FindDescendant(child, match);
            if (found != null) return found;
        }
        return null;
    }

    private async void BtnRowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMActionItemVM vm) return;
        if (vm.Id != 0 && ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
                $"Eliminare la riga \"{vm.Attivita}\"?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (vm.Id != 0)
        {
            string resp = await ApiClient.DeleteAsync($"/api/mom/items/{vm.Id}");
            if (!ApiClient.IsApiSuccess(resp, out string msg)) { ShowError(msg); return; }
        }
        if (_textSaveTimers.TryGetValue(vm, out DispatcherTimer? timer)) { timer.Stop(); _textSaveTimers.Remove(vm); }
        _node.AllItems.Remove(vm);
        _node.RefreshCounts();
        RebuildRows(nameof(BtnRowDelete_Click));
        txtStatus.Text = "Riga eliminata";
    }

    // Apre il menu ⋮ della riga (left-click; il ContextMenu eredita il DataContext della riga).
    private void BtnRowOptions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    // Voce "Conferma" ⇄ "Riapri": chiude/riapre la riga (toggle CLOSED↔OPEN).
    private async void MenuRowConferma_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not MoMActionItemVM vm) return;
            vm.Status = vm.Status == "CLOSED" ? "OPEN" : "CLOSED";
            await SaveActionItem(vm.Model, caller: nameof(MenuRowConferma_Click));
            RebuildRows(nameof(MenuRowConferma_Click));
        }
        catch (Exception ex) { MomDebugTrace.Error(ex, nameof(MenuRowConferma_Click)); ShowError(ex.Message); }
    }

    // ── Autosave campi riga ─────────────────────────────────────

    private void ActionItemTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_dataLoading) return;
        if (sender is not TextBox || ((FrameworkElement)sender).DataContext is not MoMActionItemVM vm) return;
        if (vm.SuppressAutoSave) return;
        ScheduleTextSave(vm);
    }

    private void ScheduleTextSave(MoMActionItemVM vm)
    {
        if (vm.SuppressAutoSave) return;
        if (!_textSaveTimers.TryGetValue(vm, out DispatcherTimer? timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400), Tag = vm };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _textSaveTimers.Remove(vm);
                if (!_dataLoading && timer.Tag is MoMActionItemVM itemVm && !itemVm.SuppressAutoSave)
                    _ = SaveActionItemSafe(itemVm.Model, caller: "TextSaveTimer");
            };
            _textSaveTimers[vm] = timer;
        }
        timer.Stop();
        timer.Start();
    }

    private async void ActionItemTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_dataLoading) return;
            if (sender is TextBox tb && tb.DataContext is MoMActionItemVM vm && !vm.SuppressAutoSave)
            {
                if (_textSaveTimers.TryGetValue(vm, out DispatcherTimer? timer)) { timer.Stop(); _textSaveTimers.Remove(vm); }
                await SaveActionItem(vm.Model, caller: nameof(ActionItemTextBox_LostFocus));
            }
        }
        catch (Exception ex) { MomDebugTrace.Error(ex, nameof(ActionItemTextBox_LostFocus)); }
    }

    private async void ActionItemTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Enter && sender is TextBox tb && tb.DataContext is MoMActionItemVM vm && !vm.SuppressAutoSave)
            {
                e.Handled = true;
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                if (_textSaveTimers.TryGetValue(vm, out DispatcherTimer? timer)) { timer.Stop(); _textSaveTimers.Remove(vm); }
                await SaveActionItem(vm.Model, caller: nameof(ActionItemTextBox_KeyDown));
            }
        }
        catch (Exception ex) { MomDebugTrace.Error(ex, nameof(ActionItemTextBox_KeyDown)); }
    }

    private async void ResponsibleSelection_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_dataLoading) return;
            if (sender is FrameworkElement fe && fe.DataContext is MoMActionItemVM vm && !vm.SuppressAutoSave)
                await SaveActionItem(vm.Model, caller: nameof(ResponsibleSelection_Changed));
        }
        catch (Exception ex) { MomDebugTrace.Error(ex, nameof(ResponsibleSelection_Changed)); }
    }

    private async void ActionItemDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_dataLoading || e.AddedItems.Count == 0) return;
            if (sender is DatePicker && ((FrameworkElement)sender).DataContext is MoMActionItemVM vm && !vm.SuppressAutoSave)
                await SaveActionItem(vm.Model, caller: nameof(ActionItemDatePicker_SelectedDateChanged));
        }
        catch (Exception ex) { MomDebugTrace.Error(ex, nameof(ActionItemDatePicker_SelectedDateChanged)); }
    }

    // ── Salvataggio riga ────────────────────────────────────────

    private static MoMActionItemSaveRequest BuildSaveRequest(MoMActionItemDto item) => new MoMActionItemSaveRequest
    {
        Attivita = item.Attivita.Trim(),
        Descrizione = item.Descrizione,
        Azione = item.Azione,
        Priorita = item.Priorita,
        Status = item.Status ?? "OPEN",
        IsCritical = item.IsCritical,
        ResponsibleIds = item.ResponsibleIds.Count > 0 ? item.ResponsibleIds : BuildLegacyResponsibleIds(item),
        Resp1Id = item.Resp1Id,
        Resp2Id = item.Resp2Id,
        Resp3Id = item.Resp3Id,
        DataCheck = item.DataCheck,
        DataClose = item.DataClose
    };

    private async Task SaveActionItemSafe(MoMActionItemDto item, string caller)
    {
        try { await SaveActionItem(item, caller: caller); }
        catch (Exception ex) { MomDebugTrace.Error(ex, caller); }
    }

    private async Task SaveActionItem(MoMActionItemDto item, bool rebalance = false, string caller = "")
    {
        if (string.IsNullOrWhiteSpace(item.Attivita))
        {
            MomDebugTrace.Event("SaveActionItem", $"SKIP empty attivita itemId={item.Id} caller={caller}");
            return;
        }

        int seq = Interlocked.Increment(ref _saveInFlight);
        Stopwatch sw = Stopwatch.StartNew();
        MomDebugTrace.Event("SaveActionItem", $"WAIT gate itemId={item.Id} pri={item.Priorita} seq={seq} caller={caller}");

        await _saveGate.WaitAsync();
        txtStatus.Text = "Salvataggio…";   // #5: feedback salvataggio in corso
        Stopwatch apiSw = Stopwatch.StartNew();
        try
        {
            MomDebugTrace.Event("SaveActionItem", $"API START itemId={item.Id} waitMs={sw.ElapsedMilliseconds} caller={caller}");
            MoMActionItemSaveRequest req = BuildSaveRequest(item);
            string resp = item.Id == 0
                ? await ApiClient.PostAsync($"/api/mom/{item.MomId}/items", JsonSerializer.Serialize(req))
                : await ApiClient.PutAsync($"/api/mom/items/{item.Id}", JsonSerializer.Serialize(req));

            if (ApiClient.IsApiSuccess(resp, out string msg))
            {
                if (item.Id == 0 && ApiClient.TryGetApiData(resp, out int newId, out _)) item.Id = newId;
                _node.RefreshCounts();
                if (rebalance) RebuildRows("SaveActionItem.rebalance");
                txtStatus.Text = "Salvato";
                MomDebugTrace.Event("SaveActionItem",
                    $"OK itemId={item.Id} apiMs={apiSw.ElapsedMilliseconds} totalMs={sw.ElapsedMilliseconds} caller={caller}");
            }
            else
            {
                MomDebugTrace.Warn($"SaveActionItem FAIL itemId={item.Id} msg={msg} caller={caller}");
                txtStatus.Text = $"Errore salvataggio: {msg}";
                ShowError(msg);
            }
        }
        catch (Exception ex)
        {
            MomDebugTrace.Error(ex, $"SaveActionItem itemId={item.Id} caller={caller}");
            throw;
        }
        finally
        {
            _saveGate.Release();
            Interlocked.Decrement(ref _saveInFlight);
        }
    }

    private static List<int> BuildLegacyResponsibleIds(MoMActionItemDto item)
    {
        List<int> ids = new List<int>();
        if (item.Resp1Id.HasValue) ids.Add(item.Resp1Id.Value);
        if (item.Resp2Id.HasValue && item.Resp2Id != item.Resp1Id) ids.Add(item.Resp2Id.Value);
        if (item.Resp3Id.HasValue && !ids.Contains(item.Resp3Id.Value)) ids.Add(item.Resp3Id.Value);
        return ids;
    }

    // ── Stampa / Export ─────────────────────────────────────────

    private static string StatoLabel(MoMActionItemVM v) =>
        v.Status == "CLOSED" ? "Chiusa" : v.IsCritical ? "Critica" : v.Status == "STANDBY" ? "Stand-by" : "Aperta";

    private static string? RowBgHex(MoMActionItemVM v) =>
        v.Status == "CLOSED" ? "#C6EFCE" : v.IsCritical ? "#F6C7C7" : v.Status == "STANDBY" ? "#FCF1C2" : null;

    private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.SaveFileDialog dlg = new()
        {
            Filter = "File Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            FileName = $"MoM_{SafeFileName(_node.HeaderTitle)}.xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using ClosedXML.Excel.XLWorkbook wb = new();
            ClosedXML.Excel.IXLWorksheet ws = wb.Worksheets.Add("MoM");

            string[] heads = { "#", "Attività", "Descrizione", "Azione", "Priorità", "Responsabili", "Data check", "Data close", "Stato" };
            for (int c = 0; c < heads.Length; c++)
            {
                ClosedXML.Excel.IXLCell cell = ws.Cell(1, c + 1);
                cell.Value = heads[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#CFE3F6");
            }

            int r = 2;
            foreach (MoMActionItemVM vm in _rows)
            {
                ws.Cell(r, 1).Value = vm.RowNo;
                ws.Cell(r, 2).Value = vm.Attivita;
                ws.Cell(r, 3).Value = vm.Descrizione ?? "";
                ws.Cell(r, 4).Value = vm.Azione ?? "";
                ws.Cell(r, 5).Value = vm.Priorita;
                ws.Cell(r, 6).Value = string.Join(", ", vm.AllResponsibleNames);
                ws.Cell(r, 7).Value = vm.DataCheck?.ToString("dd/MM/yyyy") ?? "";
                ws.Cell(r, 8).Value = vm.DataClose?.ToString("dd/MM/yyyy") ?? "";
                ws.Cell(r, 9).Value = StatoLabel(vm);
                string? bg = RowBgHex(vm);
                if (bg != null) ws.Range(r, 1, r, 9).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml(bg);
                r++;
            }

            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(1);
            wb.SaveAs(dlg.FileName);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            txtStatus.Text = "Excel esportato";
        }
        catch (Exception ex) { ShowError($"Errore esportazione: {ex.Message}"); }
    }

    private void BtnPrint_Click(object sender, RoutedEventArgs e) => ShowPrintPreview();

    private void ShowPrintPreview()
    {
        XpsPreviewHost? previewHost = null;
        try
        {
            FlowDocument doc = BuildPrintDocument();
            ApplyPrintPageLayout(doc);
            previewHost = new XpsPreviewHost(doc);

            Window previewWindow = new()
            {
                Title = $"Anteprima di stampa — {_node.HeaderTitle}",
                Width = 960,
                Height = 780,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = SystemColors.ControlLightBrush,
                Tag = previewHost
            };
            previewWindow.Closed += (_, _) =>
            {
                if (previewWindow.Tag is XpsPreviewHost host)
                    host.Dispose();
            };

            DockPanel root = new();

            Border toolbarBorder = new()
            {
                Padding = new Thickness(10, 8, 10, 8),
                Background = Brushes.White,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            DockPanel.SetDock(toolbarBorder, Dock.Top);

            StackPanel toolbar = new() { Orientation = Orientation.Horizontal };
            Button btnPrint = new()
            {
                Content = "Stampa...",
                MinWidth = 100,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 8, 0)
            };
            Button btnClose = new() { Content = "Chiudi", MinWidth = 100, Padding = new Thickness(10, 5, 10, 5) };

            btnPrint.Click += (_, _) =>
            {
                PrintDialog printDialog = new();
                if (printDialog.ShowDialog() != true)
                    return;

                ApplyPrintPageLayout(doc, printDialog);
                printDialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"MoM {_node.HeaderTitle}");
                txtStatus.Text = "Inviato in stampa";
            };
            btnClose.Click += (_, _) => previewWindow.Close();

            toolbar.Children.Add(btnPrint);
            toolbar.Children.Add(btnClose);
            toolbarBorder.Child = toolbar;
            root.Children.Add(toolbarBorder);
            root.Children.Add(new DocumentViewer { Document = previewHost.Document });

            previewWindow.Content = root;
            previewHost = null;
            previewWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            previewHost?.Dispose();
            ShowError($"Errore anteprima: {ex.Message}");
        }
    }

    private sealed class XpsPreviewHost : IDisposable
    {
        private readonly MemoryStream _stream;
        private readonly Package _package;
        private readonly Uri _packageUri;

        public FixedDocumentSequence Document { get; }

        public XpsPreviewHost(FlowDocument flowDocument)
        {
            _stream = new MemoryStream();
            _packageUri = new Uri($"pack://mom-print-preview-{Guid.NewGuid():N}.xps", UriKind.Absolute);
            _package = Package.Open(_stream, FileMode.Create, FileAccess.ReadWrite);
            PackageStore.AddPackage(_packageUri, _package);

            XpsDocument xpsDocument = new XpsDocument(_package, CompressionOption.Fast, _packageUri.ToString());
            XpsDocumentWriter writer = XpsDocument.CreateXpsDocumentWriter(xpsDocument);
            writer.Write(((IDocumentPaginatorSource)flowDocument).DocumentPaginator);
            Document = xpsDocument.GetFixedDocumentSequence();
        }

        public void Dispose()
        {
            PackageStore.RemovePackage(_packageUri);
            _package.Close();
            _stream.Dispose();
        }
    }

    private static void ApplyPrintPageLayout(FlowDocument doc, PrintDialog? printDialog = null)
    {
        // A4 orizzontale di default per tabella ampia; in stampa usa l'area stampabile della stampante scelta.
        double pageWidth = printDialog?.PrintableAreaWidth ?? 96 * 11.69;
        double pageHeight = printDialog?.PrintableAreaHeight ?? 96 * 8.27;
        doc.PageWidth = pageWidth;
        doc.PageHeight = pageHeight;
        doc.ColumnWidth = pageWidth - doc.PagePadding.Left - doc.PagePadding.Right;
    }

    private FlowDocument BuildPrintDocument()
    {
        FlowDocument doc = new()
        {
            PagePadding = new Thickness(30),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10
        };
        doc.Blocks.Add(new Paragraph(new Run($"{_node.HeaderTitle}  ·  {_node.TipoBadge}"))
        { FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        if (_node.MeetingDate.HasValue)
            doc.Blocks.Add(new Paragraph(new Run($"Riunione: {_node.MeetingDate:dd/MM/yyyy}"))
            { FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 10) });

        Table table = new() { CellSpacing = 0, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0.5) };
        string[] heads = { "#", "Attività", "Descrizione", "Azione", "Pri", "Responsabili", "Data check", "Data close", "Stato" };
        double[] w = { 22, 80, 110, 150, 24, 100, 60, 60, 52 };
        foreach (double width in w) table.Columns.Add(new TableColumn { Width = new GridLength(width, GridUnitType.Star) });

        TableRowGroup grp = new();
        TableRow header = new() { Background = Brushes.LightGray };
        foreach (string h in heads) header.Cells.Add(PrintCell(h, true));
        grp.Rows.Add(header);

        foreach (MoMActionItemVM vm in _rows)
        {
            TableRow row = new();
            string? bg = RowBgHex(vm);
            if (bg != null) row.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
            row.Cells.Add(PrintCell(vm.RowNo.ToString(), false));
            row.Cells.Add(PrintCell(vm.Attivita, false));
            row.Cells.Add(PrintCell(vm.Descrizione ?? "", false));
            row.Cells.Add(PrintCell(vm.Azione ?? "", false));
            row.Cells.Add(PrintCell(vm.Priorita.ToString(), false));
            row.Cells.Add(PrintCell(string.Join(", ", vm.AllResponsibleNames), false));
            row.Cells.Add(PrintCell(vm.DataCheck?.ToString("dd/MM/yyyy") ?? "", false));
            row.Cells.Add(PrintCell(vm.DataClose?.ToString("dd/MM/yyyy") ?? "", false));
            row.Cells.Add(PrintCell(StatoLabel(vm), false));
            grp.Rows.Add(row);
        }
        table.RowGroups.Add(grp);
        doc.Blocks.Add(table);
        return doc;
    }

    private static TableCell PrintCell(string text, bool bold) => new(
        new Paragraph(new Run(text)) { Margin = new Thickness(0), FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, FontSize = 9 })
    {
        BorderBrush = Brushes.LightGray,
        BorderThickness = new Thickness(0.5),
        Padding = new Thickness(3, 1, 3, 1)
    };

    private static string SafeFileName(string s)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "verbale" : s.Trim();
    }

    private static void ShowError(string msg)
        => ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
            string.IsNullOrWhiteSpace(msg) ? "Operazione non riuscita." : msg, "MoM",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
