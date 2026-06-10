using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.MoM;

// Pagina MoM: griglia card-verbale con crea/modifica inline. Dettaglio righe in MoMDetailPage.
public partial class MoMPage : Page
{
    private ObservableCollection<MoMVerbaleNode> _verbali = new();   // sorgente di verità (tutti i verbali)
    private readonly ObservableCollection<object> _display = new();  // vista: cartelle (commesse 2+) + card singole
    private List<MoMProjectLookupDto> _projects = new();
    private bool _dataLoading;
    private string _searchText = "";
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    // Cartella attualmente espansa (hover) + timer di chiusura ritardata.
    private MoMFolderNode? _openFolder;
    private DispatcherTimer? _folderCloseTimer;

    public IReadOnlyList<MoMProjectLookupDto> Projects => _projects;

    public MoMPage()
    {
        InitializeComponent();
        DataContext = this;
        icCards.ItemsSource = _display;
        Loaded += MoMPage_Loaded;
    }

    private async void MoMPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadLookups();
        await LoadAll();
    }

    public Task RefreshFromServerAsync() => LoadAll();

    public Task FlushSavesAsync() => Task.CompletedTask;

    // ── Caricamento ─────────────────────────────────────────────

    private async Task LoadLookups()
    {
        try
        {
            _projects = await ApiClient.GetListAsync<MoMProjectLookupDto>("/api/mom/lookups/projects");
        }
        catch (Exception ex) { txtStatus.Text = $"Errore lookup: {ex.Message}"; }
    }

    private async Task LoadAll()
    {
        try
        {
            _dataLoading = true;
            List<MoMListDto> list = await ApiClient.GetListAsync<MoMListDto>("/api/mom/list");
            _verbali.Clear();
            foreach (MoMListDto v in list)
                _verbali.Add(new MoMVerbaleNode(v));
            RebuildDisplay();
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
        finally { _dataLoading = false; }
    }

    // Ricostruisce la vista: commesse con 2+ verbali → cartella; resto → card singola.
    private void RebuildDisplay()
    {
        _openFolder = null;
        _folderCloseTimer?.Stop();

        string q = (_searchText ?? "").Trim().ToLowerInvariant();
        bool Match(MoMVerbaleNode n) =>
            string.IsNullOrEmpty(q) || n.IsNewDraft ||
            (n.HeaderTitle?.ToLowerInvariant().Contains(q) ?? false) ||
            (n.Title?.ToLowerInvariant().Contains(q) ?? false) ||
            (n.ProjectCode?.ToLowerInvariant().Contains(q) ?? false);

        List<MoMVerbaleNode> visible = _verbali.Where(Match).ToList();

        // Commesse (no bozze) con almeno 2 verbali = cartelle.
        HashSet<int> folderKeys = visible
            .Where(n => !n.IsNewDraft && n.Tipo == "COMMESSA" && n.ProjectId.HasValue)
            .GroupBy(n => n.ProjectId!.Value)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToHashSet();

        _display.Clear();
        Dictionary<int, MoMFolderNode> folders = new();
        foreach (MoMVerbaleNode n in visible)
        {
            if (!n.IsNewDraft && n.Tipo == "COMMESSA" && n.ProjectId.HasValue && folderKeys.Contains(n.ProjectId.Value))
            {
                int pid = n.ProjectId.Value;
                if (!folders.TryGetValue(pid, out MoMFolderNode? folder))
                {
                    folder = new MoMFolderNode(n.ProjectCode ?? "", ResolveProjectTitle(pid));
                    folders[pid] = folder;
                    _display.Add(folder);   // posizione = prima apparizione della commessa
                }
                folder.Add(n);
            }
            else
            {
                _display.Add(n);
            }
        }
        foreach (MoMFolderNode f in folders.Values) f.Finish();

        txtStatus.Text = visible.Count == 1 ? "1 verbale" : $"{visible.Count} verbali";
    }

    private string ResolveProjectTitle(int projectId)
        => _projects.FirstOrDefault(p => p.Id == projectId)?.Title ?? "";

    private async Task ReloadNode(MoMVerbaleNode? node)
    {
        if (node == null) return;
        try
        {
            _dataLoading = true;
            ApiResponse<MoMDetailDto>? api = await ApiClient.GetApiAsync<MoMDetailDto>($"/api/mom/{node.Id}");
            if (api?.Success == true && api.Data != null)
                node.UpdateHeader(api.Data);
            else ShowError(api?.Message ?? "Errore ricaricamento verbale");
        }
        finally { _dataLoading = false; }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_dataLoading) return;
        _searchText = txtSearch.Text;
        RebuildDisplay();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadAll();

    // ── Azioni sul verbale ──────────────────────────────────────

    private void BtnNewVerbale_Click(object sender, RoutedEventArgs e)
    {
        CloseOtherInlineEdits();
        MoMVerbaleNode draft = MoMVerbaleNode.CreateDraft();
        _verbali.Insert(0, draft);
        _display.Insert(0, draft);   // la bozza è sempre una card singola in cima (mai dentro una cartella)
        txtStatus.Text = "Compila la nuova card e premi Salva";
        gridScroll.ScrollToVerticalOffset(0);
    }

    private void BtnEditVerbale_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMVerbaleNode node) return;
        CloseOtherInlineEdits(node);
        node.BeginEdit();
    }

    private async void BtnInlineSave_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMVerbaleNode node) return;

        if (string.IsNullOrWhiteSpace(node.Title))
        {
            ShowError("Il titolo è obbligatorio.");
            return;
        }
        if (node.Tipo == "COMMESSA" && node.ProjectId == null)
        {
            ShowError("Seleziona una commessa.");
            return;
        }

        MoMSaveRequest req = node.ToSaveRequest();

        if (node.IsNewDraft)
        {
            string resp = await ApiClient.PostAsync("/api/mom", JsonSerializer.Serialize(req));
            if (ApiClient.TryGetApiData(resp, out int _, out string msg))
            {
                _verbali.Remove(node);
                await LoadAll();
                txtStatus.Text = "Verbale creato";
            }
            else ShowError(msg);
            return;
        }

        string putResp = await ApiClient.PutAsync($"/api/mom/{node.Id}", JsonSerializer.Serialize(req));
        if (ApiClient.IsApiSuccess(putResp, out string putMsg))
        {
            node.EndEdit();
            ResolveProjectCode(node);
            await ReloadNode(node);
            RebuildDisplay();   // la commessa può essere cambiata → rivaluta i raggruppamenti
            txtStatus.Text = "Salvato";
        }
        else ShowError(putMsg);
    }

    private void BtnInlineCancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMVerbaleNode node) return;
        if (node.IsNewDraft)
        {
            _verbali.Remove(node);
            _display.Remove(node);
            txtStatus.Text = "Creazione annullata";
            return;
        }
        node.CancelEdit();
        ResolveProjectCode(node);
        txtStatus.Text = "Modifica annullata";
    }

    private void InlineProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_dataLoading || e.AddedItems.Count == 0) return;
        if (sender is ComboBox cb && cb.DataContext is MoMVerbaleNode node && cb.SelectedItem is MoMProjectLookupDto project)
            node.SetProject(project);
    }

    private void CloseOtherInlineEdits(MoMVerbaleNode? keep = null)
    {
        foreach (MoMVerbaleNode node in _verbali.ToList())
        {
            if (node == keep) continue;
            if (node.IsNewDraft) { _verbali.Remove(node); _display.Remove(node); }
            else if (node.IsEditing) node.CancelEdit();
        }
    }

    private void ResolveProjectCode(MoMVerbaleNode node)
    {
        if (node.ProjectId == null)
        {
            node.SetProject(null);
            return;
        }
        MoMProjectLookupDto? project = _projects.FirstOrDefault(p => p.Id == node.ProjectId.Value);
        node.SetProject(project);
    }

    private async void DashboardCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.DataContext is not MoMVerbaleNode node) return;
        if (node.IsNewDraft || node.Id == 0) return;
        MoMSaveRequest req = new MoMSaveRequest
        {
            Tipo = node.Tipo,
            ProjectId = node.ProjectId,
            Title = node.Title,
            MeetingDate = node.MeetingDate,
            InDashboard = node.InDashboard
        };
        string resp = await ApiClient.PutAsync($"/api/mom/{node.Id}", JsonSerializer.Serialize(req));
        if (ApiClient.IsApiSuccess(resp, out string msg)) txtStatus.Text = "Salvato";
        else { node.InDashboard = !node.InDashboard; ShowError(msg); }
    }

    private async void BtnDeleteVerbale_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMVerbaleNode node) return;

        if (node.IsNewDraft)
        {
            _verbali.Remove(node);
            _display.Remove(node);
            txtStatus.Text = "Creazione annullata";
            return;
        }

        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
                $"Eliminare il verbale \"{node.Title}\" e tutte le sue azioni?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            _dataLoading = true;
            await _saveGate.WaitAsync();
            try
            {
                string resp = await ApiClient.DeleteAsync($"/api/mom/{node.Id}");
                if (ApiClient.IsApiSuccess(resp, out string msg))
                {
                    _verbali.Remove(node);
                    RebuildDisplay();   // se la cartella scende a 1 verbale si dissolve
                    txtStatus.Text = "Verbale eliminato";
                }
                else ShowError(msg);
            }
            finally
            {
                _saveGate.Release();
                _dataLoading = false;
            }
        }
    }

    private void Card_Open(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveSource(e.OriginalSource as DependencyObject)) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMVerbaleNode node) return;
        if (node.IsNewDraft || node.IsEditing) return;
        NavigationService?.Navigate(new MoMDetailPage(node));
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button or CheckBox or TextBoxBase or ComboBox or DatePicker)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    // ── Cartelle commessa: espansione hover (effetto "apri cartella") ──

    private void Folder_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MoMFolderNode folder) OpenFolder(folder);
    }

    private void Folder_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MoMFolderNode folder) OpenFolder(folder);
    }

    private void Folder_MouseLeave(object sender, MouseEventArgs e) => ScheduleFolderClose();

    private void FolderPopup_MouseEnter(object sender, MouseEventArgs e) => StopFolderTimer();

    private void FolderPopup_MouseLeave(object sender, MouseEventArgs e) => ScheduleFolderClose();

    private void OpenFolder(MoMFolderNode folder)
    {
        StopFolderTimer();
        if (_openFolder != null && !ReferenceEquals(_openFolder, folder)) _openFolder.IsOpen = false;
        _openFolder = folder;
        folder.IsOpen = true;
    }

    private void ScheduleFolderClose()
    {
        _folderCloseTimer ??= CreateFolderCloseTimer();
        _folderCloseTimer.Stop();
        _folderCloseTimer.Start();
    }

    private void StopFolderTimer() => _folderCloseTimer?.Stop();

    private DispatcherTimer CreateFolderCloseTimer()
    {
        DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (_openFolder == null) return;
            // Non chiudere mentre un verbale dentro è in modifica/bozza
            // (es. il calendario del DatePicker apre un suo popup esterno alla cartella).
            if (_openFolder.Verbali.Any(v => v.IsEditing || v.IsNewDraft)) return;
            _openFolder.IsOpen = false;
            _openFolder = null;
        };
        return t;
    }

    private static void ShowError(string msg)
        => ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
            string.IsNullOrWhiteSpace(msg) ? "Operazione non riuscita." : msg, "MoM",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}

// ── VM nodo verbale (card griglia + header dettaglio) ────────────
public class MoMVerbaleNode : System.ComponentModel.INotifyPropertyChanged
{
    public int Id { get; }
    public bool IsNewDraft { get; private set; }

    private string _tipo = "RIUNIONE";
    public string Tipo
    {
        get => _tipo;
        set
        {
            if (_tipo == value) return;
            _tipo = value;
            if (value != "COMMESSA") SetProject(null);
            OnPropertyChanged(nameof(Tipo));
            OnPropertyChanged(nameof(IsCommessa));
            OnPropertyChanged(nameof(TipoBadge));
            OnPropertyChanged(nameof(HeaderTitle));
        }
    }

    public int? ProjectId
    {
        get => _projectId;
        set
        {
            if (_projectId == value) return;
            _projectId = value;
            OnPropertyChanged(nameof(ProjectId));
        }
    }
    private int? _projectId;

    public string? ProjectCode { get; private set; }

    private bool _inDashboard;
    public bool InDashboard { get => _inDashboard; set { _inDashboard = value; OnPropertyChanged(nameof(InDashboard)); } }

    private string _title = "";
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? "";
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(HeaderTitle));
        }
    }

    private DateTime? _meetingDate;
    public DateTime? MeetingDate
    {
        get => _meetingDate;
        set
        {
            _meetingDate = value;
            OnPropertyChanged(nameof(MeetingDate));
            OnPropertyChanged(nameof(MeetingLabel));
        }
    }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); OnPropertyChanged(nameof(InlineEditTitle)); }
    }

    public string InlineEditTitle => IsNewDraft ? "Nuovo verbale" : "Modifica verbale";

    private MoMSaveRequest? _editSnapshot;
    private string? _editSnapshotProjectCode;

    public int ItemsCount { get; private set; }
    public int P1Count { get; private set; }
    public int P2Count { get; private set; }
    public int P3Count { get; private set; }
    public DateTime? PeriodStart { get; private set; }
    public DateTime? PeriodEnd { get; private set; }

    public bool IsCommessa => Tipo == "COMMESSA";
    public string TipoBadge => IsCommessa ? "COMMESSA" : "RIUNIONE";
    public string HeaderTitle =>
        IsCommessa && !string.IsNullOrWhiteSpace(ProjectCode) ? $"{ProjectCode} — {Title}" : Title;
    public string PeriodLabel =>
        (PeriodStart.HasValue || PeriodEnd.HasValue)
            ? $"{(PeriodStart.HasValue ? PeriodStart.Value.ToString("dd/MM/yyyy") : "—")} → {(PeriodEnd.HasValue ? PeriodEnd.Value.ToString("dd/MM/yyyy") : "—")}"
            : "—";
    public string MeetingLabel => MeetingDate.HasValue ? MeetingDate.Value.ToString("dd/MM/yyyy") : "—";

    public List<MoMActionItemVM> AllItems { get; private set; } = new();

    public MoMVerbaleNode(MoMListDto v)
    {
        Id = v.Id;
        _tipo = v.Tipo;
        ProjectId = v.ProjectId;
        ProjectCode = v.ProjectCode;
        _inDashboard = v.InDashboard;
        _title = v.Title;
        _meetingDate = v.MeetingDate;
        ItemsCount = v.ItemsCount;
        P1Count = v.P1Count;
        P2Count = v.P2Count;
        P3Count = v.P3Count;
        PeriodStart = v.PeriodStart;
        PeriodEnd = v.PeriodEnd;
    }

    public static MoMVerbaleNode CreateDraft()
    {
        return new MoMVerbaleNode
        {
            IsNewDraft = true,
            IsEditing = true,
            _tipo = "RIUNIONE",
            _title = "",
            _meetingDate = DateTime.Today,
            _inDashboard = true
        };
    }

    private MoMVerbaleNode() { Id = 0; }

    public void SetProject(MoMProjectLookupDto? project)
    {
        ProjectId = project?.Id;
        ProjectCode = project?.Code;
        OnPropertyChanged(nameof(ProjectCode));
        OnPropertyChanged(nameof(HeaderTitle));
    }

    public void BeginEdit()
    {
        if (IsEditing || IsNewDraft) return;
        _editSnapshot = ToSaveRequest();
        _editSnapshotProjectCode = ProjectCode;
        IsEditing = true;
    }

    public void CancelEdit()
    {
        if (_editSnapshot != null)
        {
            Tipo = _editSnapshot.Tipo;
            ProjectId = _editSnapshot.ProjectId;
            ProjectCode = _editSnapshotProjectCode;
            OnPropertyChanged(nameof(ProjectId));
            OnPropertyChanged(nameof(ProjectCode));
            Title = _editSnapshot.Title;
            MeetingDate = _editSnapshot.MeetingDate;
            InDashboard = _editSnapshot.InDashboard;
            _editSnapshot = null;
            _editSnapshotProjectCode = null;
        }
        IsEditing = false;
    }

    public void EndEdit()
    {
        _editSnapshot = null;
        _editSnapshotProjectCode = null;
        IsEditing = false;
    }

    public MoMSaveRequest ToSaveRequest() => new MoMSaveRequest
    {
        Tipo = Tipo,
        ProjectId = Tipo == "COMMESSA" ? ProjectId : null,
        Title = Title.Trim(),
        MeetingDate = MeetingDate,
        InDashboard = InDashboard
    };

    public void UpdateHeader(MoMDetailDto d)
    {
        Tipo = d.Tipo;
        ProjectId = d.ProjectId;
        ProjectCode = d.ProjectCode;
        OnPropertyChanged(nameof(ProjectId));
        InDashboard = d.InDashboard;
        Title = d.Title;
        MeetingDate = d.MeetingDate;
        OnPropertyChanged(nameof(TipoBadge));
        OnPropertyChanged(nameof(IsCommessa));
        OnPropertyChanged(nameof(HeaderTitle));
    }

    public void SetItems(List<MoMActionItemDto> items, List<LookupItem>? employees = null, List<LookupItem>? wildcards = null)
    {
        AllItems = items.Select(i => new MoMActionItemVM(i)).ToList();
        if (employees != null)
        {
            foreach (MoMActionItemVM vm in AllItems)
                vm.SetEmployeeSources(employees, wildcards ?? new List<LookupItem>());
        }
        RefreshCounts();
    }

    public void RefreshCounts()
    {
        ItemsCount = AllItems.Count;
        P1Count = AllItems.Count(i => i.Priorita == 1);
        P2Count = AllItems.Count(i => i.Priorita == 2);
        P3Count = AllItems.Count(i => i.Priorita == 3);

        List<DateTime> dates = new List<DateTime>();
        foreach (MoMActionItemVM item in AllItems)
        {
            if (item.DataCheck.HasValue) dates.Add(item.DataCheck.Value);
            if (item.DataClose.HasValue) dates.Add(item.DataClose.Value);
        }
        PeriodStart = dates.Count > 0 ? dates.Min() : null;
        PeriodEnd = dates.Count > 0 ? dates.Max() : null;

        OnPropertyChanged(nameof(ItemsCount));
        OnPropertyChanged(nameof(P1Count));
        OnPropertyChanged(nameof(P2Count));
        OnPropertyChanged(nameof(P3Count));
        OnPropertyChanged(nameof(PeriodStart));
        OnPropertyChanged(nameof(PeriodEnd));
        OnPropertyChanged(nameof(PeriodLabel));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

// VM singola action item (tabella dettaglio).
public class MoMActionItemVM : System.ComponentModel.INotifyPropertyChanged
{
    private readonly MoMActionItemDto _model;
    private List<LookupItem> _employees = new();
    private List<LookupItem> _wildcards = new();
    private List<LookupItem> _responsiblePool = new();
    private bool _syncingResponsibleIds;

    public MoMActionItemDto Model => _model;

    public List<LookupItem> ResponsiblePool => _responsiblePool;

    public MoMActionItemVM(MoMActionItemDto model) { _model = model; }

    public bool SuppressAutoSave { get; set; }

    public void SetEmployeeSources(List<LookupItem> employees, List<LookupItem> wildcards)
    {
        _employees = employees;
        _wildcards = wildcards;
        RefreshResponsiblePool();
    }

    public string SelectedResponsibleIds
    {
        get
        {
            List<int> ids = GetSelectedIdsFromModel();
            return ids.Count == 0 ? string.Empty : string.Join(",", ids);
        }
        set
        {
            if (_syncingResponsibleIds) return;
            ApplySelectedIdsToModel(ParseIds(value));
        }
    }

    private List<int> GetSelectedIdsFromModel()
    {
        if (_model.ResponsibleIds.Count > 0)
            return new List<int>(_model.ResponsibleIds);

        List<int> ids = new List<int>();
        if (_model.Resp1Id.HasValue) ids.Add(_model.Resp1Id.Value);
        if (_model.Resp2Id.HasValue && _model.Resp2Id != _model.Resp1Id) ids.Add(_model.Resp2Id.Value);
        if (_model.Resp3Id.HasValue && _model.Resp3Id != _model.Resp1Id && _model.Resp3Id != _model.Resp2Id)
            ids.Add(_model.Resp3Id.Value);
        return ids;
    }

    private void ApplySelectedIdsToModel(List<int> ids)
    {
        _syncingResponsibleIds = true;
        try
        {
            _model.ResponsibleIds = ids.ToList();
            _model.ResponsibleNames = ids
                .Select(id => ResolveName(id))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Cast<string>()
                .ToList();
            _model.Resp1Id = ids.Count > 0 ? ids[0] : null;
            _model.Resp1Name = ResolveName(_model.Resp1Id);
            _model.Resp2Id = ids.Count > 1 ? ids[1] : null;
            _model.Resp2Name = ResolveName(_model.Resp2Id);
            _model.Resp3Id = ids.Count > 2 ? ids[2] : null;
            _model.Resp3Name = ResolveName(_model.Resp3Id);
            EnsurePoolContainsSelected(ids);
            OnPropertyChanged(nameof(SelectedResponsibleIds));
        }
        finally { _syncingResponsibleIds = false; }
    }

    private void EnsurePoolContainsSelected(IReadOnlyList<int> ids)
    {
        bool changed = false;
        foreach (int id in ids)
        {
            if (_responsiblePool.Any(x => x.Id == id)) continue;
            string? name = ResolveName(id);
            if (string.IsNullOrWhiteSpace(name)) continue;
            _responsiblePool.Add(new LookupItem { Id = id, Name = name });
            changed = true;
        }
        if (changed)
            OnPropertyChanged(nameof(ResponsiblePool));
    }

    private string? ResolveName(int? id)
    {
        if (!id.HasValue) return null;
        LookupItem? found = _responsiblePool.FirstOrDefault(x => x.Id == id.Value)
            ?? _employees.FirstOrDefault(x => x.Id == id.Value)
            ?? _wildcards.FirstOrDefault(x => x.Id == id.Value);
        return found?.Name;
    }

    private List<LookupItem> BuildResponsiblePool()
    {
        List<LookupItem> pool = new List<LookupItem>(_employees);
        if (_model.ResponsibleIds.Count > 0)
        {
            for (int i = 0; i < _model.ResponsibleIds.Count; i++)
            {
                int id = _model.ResponsibleIds[i];
                string? name = i < _model.ResponsibleNames.Count ? _model.ResponsibleNames[i] : ResolveName(id);
                AddToPoolIfMissing(pool, id, name);
            }
        }
        else
        {
            AddToPoolIfMissing(pool, _model.Resp1Id, _model.Resp1Name);
            AddToPoolIfMissing(pool, _model.Resp2Id, _model.Resp2Name);
            AddToPoolIfMissing(pool, _model.Resp3Id, _model.Resp3Name);
        }
        return pool;
    }

    private static void AddToPoolIfMissing(List<LookupItem> pool, int? id, string? name)
    {
        if (!id.HasValue || string.IsNullOrWhiteSpace(name)) return;
        if (pool.Any(x => x.Id == id.Value)) return;
        pool.Add(new LookupItem { Id = id.Value, Name = name });
    }

    private void RefreshResponsiblePool()
    {
        List<LookupItem> next = BuildResponsiblePool();
        if (!PoolSameItems(_responsiblePool, next))
        {
            _responsiblePool = next;
            OnPropertyChanged(nameof(ResponsiblePool));
        }
        OnPropertyChanged(nameof(SelectedResponsibleIds));
    }

    private static bool PoolSameItems(List<LookupItem> current, List<LookupItem> next)
    {
        if (current.Count != next.Count) return false;
        for (int i = 0; i < current.Count; i++)
        {
            if (current[i].Id != next[i].Id || current[i].Name != next[i].Name)
                return false;
        }
        return true;
    }

    private static List<int> ParseIds(string? raw)
    {
        List<int> ids = new List<int>();
        if (string.IsNullOrWhiteSpace(raw)) return ids;
        foreach (string part in raw.Split(','))
            if (int.TryParse(part.Trim(), out int id) && !ids.Contains(id)) ids.Add(id);
        return ids;
    }

    public int Id => _model.Id;
    public int MomId => _model.MomId;

    public string Attivita
    {
        get => _model.Attivita;
        set { if (_model.Attivita != value) { _model.Attivita = value; OnPropertyChanged(nameof(Attivita)); } }
    }
    public string? Descrizione
    {
        get => _model.Descrizione;
        set { if (_model.Descrizione != value) { _model.Descrizione = value; OnPropertyChanged(nameof(Descrizione)); } }
    }
    public string? Azione
    {
        get => _model.Azione;
        set { if (_model.Azione != value) { _model.Azione = value; OnPropertyChanged(nameof(Azione)); } }
    }
    public int Priorita
    {
        get => _model.Priorita;
        set { if (_model.Priorita != value) { _model.Priorita = value; OnPropertyChanged(nameof(Priorita)); } }
    }
    public string Status
    {
        get => _model.Status;
        set { if (_model.Status != value) { _model.Status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(ConfermaLabel)); OnPropertyChanged(nameof(StateGlyph)); } }
    }
    public bool IsCritical
    {
        get => _model.IsCritical;
        set { if (_model.IsCritical != value) { _model.IsCritical = value; OnPropertyChanged(nameof(IsCritical)); OnPropertyChanged(nameof(StateGlyph)); } }
    }

    // Etichetta dinamica voce menu (#1): "Riapri" se la riga è chiusa, altrimenti "Conferma".
    public string ConfermaLabel => Status == "CLOSED" ? "Riapri" : "Conferma";

    // Indicatore di stato a FORMA (non solo colore) per accessibilità (#7): 🚩 critica · ⏸ stand-by · ✅ chiusa · vuoto = aperta.
    public string StateGlyph => Status == "CLOSED" ? "✅" : IsCritical ? "🚩" : Status == "STANDBY" ? "⏸" : "";
    public int? Resp1Id
    {
        get => _model.Resp1Id;
        set { if (_model.Resp1Id != value) { _model.Resp1Id = value; OnPropertyChanged(nameof(Resp1Id)); OnPropertyChanged(nameof(SelectedResponsibleIds)); } }
    }
    public string? Resp1Name { get => _model.Resp1Name; set { _model.Resp1Name = value; } }
    public int? Resp2Id
    {
        get => _model.Resp2Id;
        set { if (_model.Resp2Id != value) { _model.Resp2Id = value; OnPropertyChanged(nameof(Resp2Id)); OnPropertyChanged(nameof(SelectedResponsibleIds)); } }
    }
    public string? Resp2Name { get => _model.Resp2Name; set { _model.Resp2Name = value; } }
    public int? Resp3Id
    {
        get => _model.Resp3Id;
        set { if (_model.Resp3Id != value) { _model.Resp3Id = value; OnPropertyChanged(nameof(Resp3Id)); OnPropertyChanged(nameof(SelectedResponsibleIds)); } }
    }
    public string? Resp3Name { get => _model.Resp3Name; set { _model.Resp3Name = value; } }

    public IEnumerable<string> AllResponsibleNames =>
        _model.ResponsibleNames.Count > 0
            ? _model.ResponsibleNames.Where(s => !string.IsNullOrWhiteSpace(s))
            : new[] { Resp1Name, Resp2Name, Resp3Name }.Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>();

    public DateTime? DataCheck
    {
        get => _model.DataCheck;
        set { if (_model.DataCheck != value) { _model.DataCheck = value; OnPropertyChanged(nameof(DataCheck)); } }
    }
    public DateTime? DataClose
    {
        get => _model.DataClose;
        set { if (_model.DataClose != value) { _model.DataClose = value; OnPropertyChanged(nameof(DataClose)); } }
    }

    private int _rowNo;
    public int RowNo { get => _rowNo; set { if (_rowNo != value) { _rowNo = value; OnPropertyChanged(nameof(RowNo)); } } }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

// ── VM cartella: una commessa con 2+ verbali (raccoglie le card e aggrega i conteggi) ──
public class MoMFolderNode : System.ComponentModel.INotifyPropertyChanged
{
    public string Code { get; }
    public string ProjectTitle { get; }
    public ObservableCollection<MoMVerbaleNode> Verbali { get; } = new();

    public MoMFolderNode(string code, string projectTitle)
    {
        Code = code;
        ProjectTitle = projectTitle ?? "";
    }

    public void Add(MoMVerbaleNode n) => Verbali.Add(n);

    public string HeaderTitle => string.IsNullOrWhiteSpace(ProjectTitle) ? Code : $"{Code} — {ProjectTitle}";
    public int Count => Verbali.Count;
    public IEnumerable<string> PreviewTitles => Verbali.Take(3).Select(v => v.Title);

    public int ItemsCount { get; private set; }
    public int P1Count { get; private set; }
    public int P2Count { get; private set; }
    public int P3Count { get; private set; }

    // Aggrega i conteggi dopo aver riempito la cartella.
    public void Finish()
    {
        ItemsCount = Verbali.Sum(v => v.ItemsCount);
        P1Count = Verbali.Sum(v => v.P1Count);
        P2Count = Verbali.Sum(v => v.P2Count);
        P3Count = Verbali.Sum(v => v.P3Count);
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(ItemsCount));
        OnPropertyChanged(nameof(P1Count));
        OnPropertyChanged(nameof(P2Count));
        OnPropertyChanged(nameof(P3Count));
        OnPropertyChanged(nameof(PreviewTitles));
        OnPropertyChanged(nameof(HeaderTitle));
    }

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set { if (_isOpen != value) { _isOpen = value; OnPropertyChanged(nameof(IsOpen)); } }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

// Espone il DataContext della pagina (Projects) dentro i Popup, che vivono fuori dall'albero visuale.
public class BindingProxy : System.Windows.Freezable
{
    protected override System.Windows.Freezable CreateInstanceCore() => new BindingProxy();

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly System.Windows.DependencyProperty DataProperty =
        System.Windows.DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy),
            new System.Windows.UIPropertyMetadata(null));
}
