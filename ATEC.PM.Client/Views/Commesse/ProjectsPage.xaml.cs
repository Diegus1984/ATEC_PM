using ATEC.PM.Client.UserControls;
namespace ATEC.PM.Client.Views;

using System.Text.Json;
using System.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;


public partial class ProjectsPage : Page
{
    private const int ProjectPageSize = 50;

    private List<ProjectListItem> _allProjects = new();
    private int _selectedProjectId;
    private string _selectedProjectCode = "";

    private int _pendingNavProjectId;

    private string _pendingNavSection = "";

    private int _loadedPage;
    private int _totalProjectCount;
    private bool _hasMoreProjects;
    private bool _loadingProjects;
    private CancellationTokenSource? _searchDebounceCts;
    private InfiniteScrollHelper? _infiniteScroll;

    /// <summary>Contenuti sezione per commessa — chiave "{projectId}:{section}[:extra]".</summary>
    private readonly Dictionary<string, FrameworkElement> _sectionCache = new();

    public ProjectsPage()
    {
        InitializeComponent();
        _infiniteScroll = new InfiniteScrollHelper(
            () => _hasMoreProjects && !_loadingProjects,
            () => LoadTree(append: true));
        _infiniteScroll.Attach(treeProjects);

        // Ripristina larghezza pannello tree
        double savedWidth = Services.UserPreferences.GetDouble("projects.tree_width", 250);
        colTree.Width = new GridLength(Math.Clamp(savedWidth, 180, 500));

        Loaded += async (_, _) =>
        {
            await LoadTree();
            await ApplyPendingNavigationAsync();
        };
    }

    public void NavigateToSection(int projectId, string section)
    {
        _pendingNavProjectId = projectId;
        _pendingNavSection = section;
        if (IsLoaded)
            _ = ApplyPendingNavigationAsync();
    }

    /// <summary>Ricarica l'albero (nuova commessa) e seleziona la sezione richiesta.</summary>
    public async Task RefreshTreeAndNavigateToSection(int projectId, string section = "details")
    {
        txtSearch.Text = "";
        _pendingNavProjectId = projectId;
        _pendingNavSection = section;
        await LoadTree(append: false);
        await ApplyPendingNavigationAsync();
    }

    private async Task ApplyPendingNavigationAsync()
    {
        if (_pendingNavProjectId <= 0)
            return;

        if (_allProjects.Count == 0)
            await LoadTree();

        int pid = _pendingNavProjectId;
        string sec = _pendingNavSection;
        _pendingNavProjectId = 0;
        _pendingNavSection = "";

        foreach (TreeViewItem projNode in treeProjects.Items)
        {
            string tag = projNode.Tag?.ToString() ?? "";
            if (tag != $"project|{pid}")
                continue;

            projNode.IsExpanded = true;
            projNode.BringIntoView();

            foreach (TreeViewItem child in projNode.Items)
            {
                string childTag = child.Tag?.ToString() ?? "";
                if (childTag == $"{sec}|{pid}")
                {
                    child.IsSelected = true;
                    break;
                }
            }
            break;
        }
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex) =>
                new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLower();
        return ext switch
        {
            ".pdf" => "📕",
            ".doc" or ".docx" => "📘",
            ".xls" or ".xlsx" => "📗",
            ".dwg" or ".dxf" => "📐",
            ".jpg" or ".jpeg" or ".png" or ".bmp" => "🖼",
            ".zip" or ".rar" or ".7z" => "📦",
            ".txt" => "📝",
            ".csv" => "📊",
            _ => "📄"
        };
    }
    // === HELPERS ===
    private void AddField(StackPanel panel, string label, string? value)
    {
        panel.Children.Add(new TextBlock { Text = label.ToUpper(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 12, 0, 2) });
        panel.Children.Add(new TextBlock { Text = value ?? "-", FontSize = 14, Foreground = Brush("#1A1D26"), TextWrapping = TextWrapping.Wrap });
    }

    private void AddFileTreeNodes(TreeViewItem parentNode, List<FileTreeItem> items, int projectId)
    {
        foreach (var item in items)
        {
            if (item.IsFolder)
            {
                var folderNode = new TreeViewItem
                {
                    Header = $"📁 {item.Name}",
                    Tag = $"docfolder|{projectId}|{item.RelativePath}",
                    FontWeight = FontWeights.Normal,
                    FontSize = 12
                };
                AddFileTreeNodes(folderNode, item.Children, projectId);
                parentNode.Items.Add(folderNode);
            }
            else
            {
                var icon = GetFileIcon(item.Name);
                var fileNode = new TreeViewItem
                {
                    Header = $"{icon} {item.Name}",
                    Tag = $"file|{projectId}|{item.RelativePath}",
                    FontWeight = FontWeights.Normal,
                    FontSize = 12
                };
                parentNode.Items.Add(fileNode);
            }
        }
    }


    // === ACTIONS ===
    private async void BtnAction_Click(object sender, RoutedEventArgs e)
    {
        var tag = btnAction.Tag?.ToString() ?? "";
        var parts = tag.Split('|');
        if (parts[0] == "edit" && parts.Length > 1 && int.TryParse(parts[1], out var editId))
        {
            var dlg = new ProjectDialog(editId) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                ClearSectionCacheForProject(editId);
                await LoadTree();
            }
        }
        else if (parts[0] == "createfolder" && parts.Length > 1 && int.TryParse(parts[1], out var cfId))
        {
            try
            {
                string json = await ApiClient.PostAsync($"/api/projects/{cfId}/create-folder", "{}");
                if (ApiClient.IsApiSuccess(json, out _))
                {
                    RefreshDocNode(cfId);
                    ShowDocuments(cfId, "");
                }
            }
            catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
        }
        else if (parts[0] == "openfolder" && parts.Length > 1 && int.TryParse(parts[1], out var ofId))
        {
            ProjectSaveRequest? proj = await ApiClient.GetDataAsync<ProjectSaveRequest>($"/api/projects/{ofId}");
            string sp = proj?.ServerPath ?? "";
            var sub = parts.Length > 2 ? parts[2] : "";
            var fullPath = string.IsNullOrEmpty(sub) ? sp : Path.Combine(sp, sub);
            if (Directory.Exists(fullPath))
                System.Diagnostics.Process.Start("explorer.exe", fullPath);
        }
        else if (parts[0] == "download" && parts.Length > 2 && int.TryParse(parts[1], out var dlId))
        {
            var relPath = parts[2];
            var fileName = Path.GetFileName(relPath);
            try
            {
                var encoded = Uri.EscapeDataString(relPath);
                var bytes = await ApiClient.DownloadAsync($"/api/projects/{dlId}/download?path={encoded}");
                if (bytes == null || bytes.Length == 0)
                {
                    ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Impossibile scaricare il file.");
                    return;
                }
                var tempDir = Path.Combine(Path.GetTempPath(), "ATEC_PM");
                Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, fileName);
                File.WriteAllBytes(tempFile, bytes);
                OpenFile(tempFile);
            }
            catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
        }
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProjectDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = LoadTree(append: false);
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadTree(append: false);

    private void BuildTree(List<ProjectListItem> projects)
    {
        // Snapshot espansione + selezione PRIMA del rebuild (TreeViewStateHelper API "TreeViewItem")
        HashSet<object> expandedKeys = TreeViewStateHelper.CollectExpandedKeys(
            treeProjects.Items, tvi => tvi.Tag);
        object? selectedKey = (treeProjects.SelectedItem as TreeViewItem)?.Tag;

        treeProjects.Items.Clear();
        foreach (var p in projects)
        {
            var projNode = new TreeViewItem
            {
                Header = $"{p.Code} - {p.CustomerName}",
                Tag = $"project|{p.Id}",
                FontWeight = FontWeights.SemiBold
            };

            projNode.Items.Add(new TreeViewItem { Header = "Dettagli", Tag = $"details|{p.Id}" });
            projNode.Items.Add(new TreeViewItem { Header = "💰 Flusso di Cassa", Tag = $"cashflow|{p.Id}" });
            projNode.Items.Add(new TreeViewItem { Header = "📊 Preventivo vs Consuntivo", Tag = $"budget_vs_actual|{p.Id}" });
            projNode.Items.Add(new TreeViewItem { Header = "💬 Chat", Tag = $"chat|{p.Id}" });
            projNode.Items.Add(new TreeViewItem { Header = "📝 Verbali (MoM)", Tag = $"mom|{p.Id}" });
            projNode.Items.Add(new TreeViewItem { Header = "📋 DDP Commerciali", Tag = $"ddp_commercial|{p.Id}" });

            var docNode = new TreeViewItem { Header = "📁 Documenti", Tag = $"documents|{p.Id}" };
            // Placeholder per lazy-load al primo expand
            docNode.Items.Add(new TreeViewItem { Header = "Caricamento...", IsEnabled = false });
            docNode.Expanded += DocNode_Expanded;
            projNode.Items.Add(docNode);

            treeProjects.Items.Add(projNode);
        }

        // Ripristina espansione + selezione (vedi .claude/skills/wpf-xaml-guide/SKILL.md "TreeView — preservare …")
        TreeViewStateHelper.ApplyExpandedKeys(treeProjects.Items, expandedKeys, tvi => tvi.Tag);
        if (selectedKey != null)
        {
            TreeViewItem? toSel = TreeViewStateHelper.FindItem(treeProjects.Items,
                tvi => Equals(tvi.Tag, selectedKey));
            if (toSel != null) toSel.IsSelected = true;
        }
    }


    private async void DocNode_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem docNode) return;
        var tag = docNode.Tag?.ToString() ?? "";
        if (!tag.StartsWith("documents|")) return;

        // Evita ricaricamento se già popolato (controlla se c'è il placeholder)
        if (docNode.Items.Count == 1 && docNode.Items[0] is TreeViewItem first && !first.IsEnabled)
        {
            var parts = tag.Split('|');
            if (!int.TryParse(parts[1], out int projectId)) return;
            await LoadFileTree(docNode, projectId);
        }
    }


    private async Task LoadFileTree(TreeViewItem parentNode, int projectId)
    {
        parentNode.Items.Clear();
        try
        {
            List<FileTreeItem>? items = await ApiClient.GetDataAsync<List<FileTreeItem>>(
                $"/api/projects/{projectId}/file-tree");
            if (items == null)
            {
                parentNode.Items.Add(new TreeViewItem { Header = "Cartella non creata", IsEnabled = false, FontStyle = FontStyles.Italic });
                return;
            }

            if (items.Count == 0)
            {
                parentNode.Items.Add(new TreeViewItem { Header = "Cartella non creata", IsEnabled = false, FontStyle = FontStyles.Italic });
                return;
            }

            AddFileTreeNodes(parentNode, items, projectId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading file tree: {ex.Message}");
            parentNode.Items.Add(new TreeViewItem { Header = "Errore caricamento", IsEnabled = false });
        }
    }

    // === LOAD TREE ===
    private static string SectionCacheKey(int projectId, string section, string? extra = null)
        => extra == null ? $"{projectId}:{section}" : $"{projectId}:{section}:{extra}";

    private void ClearAllSectionCache()
    {
        _sectionCache.Clear();
        SectionContent.Content = null;
    }

    private void ClearSectionCacheForProject(int projectId)
    {
        List<string> keys = _sectionCache.Keys.Where(k => k.StartsWith($"{projectId}:", StringComparison.Ordinal)).ToList();
        foreach (string key in keys)
            _sectionCache.Remove(key);
    }

    private void InvalidateDocumentCache(int projectId)
    {
        List<string> keys = _sectionCache.Keys
            .Where(k => k.StartsWith($"{projectId}:documents", StringComparison.Ordinal))
            .ToList();
        foreach (string key in keys)
            _sectionCache.Remove(key);
    }

    /// <summary>Mostra un controllo sezione: creato e caricato (onFirstLoad) solo al primo accesso.</summary>
    private void ShowCachedSection<T>(int projectId, string sectionKey, Func<T> create, Action<T> onFirstLoad)
        where T : FrameworkElement
    {
        string key = SectionCacheKey(projectId, sectionKey);
        bool isNew = !_sectionCache.TryGetValue(key, out FrameworkElement? existing) || existing is not T ctrl;
        if (isNew)
        {
            ctrl = create();
            _sectionCache[key] = ctrl;
            onFirstLoad(ctrl);
        }
        else
        {
            ctrl = (T)existing!;
        }

        SectionContent.Content = ctrl;
    }

    private void ShowProjectPlaceholder(int projectId)
    {
        ProjectListItem? proj = _allProjects.FirstOrDefault(p => p.Id == projectId);
        txtSectionTitle.Text = proj != null ? $"{proj.Code} — {proj.CustomerName}" : "Commessa";
        btnAction.Visibility = Visibility.Collapsed;

        SectionContent.Content = new Border
        {
            Background = System.Windows.Media.Brushes.White,
            Padding = new Thickness(32),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Seleziona una sezione",
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brush("#1A1D26"),
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    new TextBlock
                    {
                        Text = "Apri Dettagli, Flusso di cassa, Preventivo vs consuntivo, Chat, DDP o Documenti dall'albero a sinistra.",
                        FontSize = 13,
                        Foreground = Brush("#6B7280"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }

    private async Task LoadTree(bool append = false)
    {
        if (_loadingProjects) return;
        _loadingProjects = true;
        if (!append)
        {
            ClearAllSectionCache();
            _loadedPage = 0;
            _allProjects.Clear();
        }

        int page = append ? _loadedPage + 1 : 1;
        string search = txtSearch.Text.Trim();
        string url = $"/api/projects?page={page}&pageSize={ProjectPageSize}";
        if (!string.IsNullOrEmpty(search))
            url += $"&search={Uri.EscapeDataString(search)}";

        txtStatus.Text = append ? "Caricamento altre commesse..." : "Caricamento...";
        try
        {
            PagedResult<ProjectListItem>? pageData = await PagedApiHelper.GetPageAsync<ProjectListItem>(url);
            if (pageData != null)
            {
                _loadedPage = pageData.Page;
                _totalProjectCount = pageData.TotalCount;
                _hasMoreProjects = pageData.HasMore;
                _allProjects.AddRange(pageData.Items);
                BuildTree(_allProjects);
                UpdateProjectListStatus();
                _infiniteScroll?.NotifyContentUpdated(treeProjects);
            }
            else
                txtStatus.Text = "Errore caricamento";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
        finally
        {
            _loadingProjects = false;
        }
    }

    private void UpdateProjectListStatus()
    {
        if (_totalProjectCount <= 0)
            txtStatus.Text = "Nessuna commessa";
        else if (_allProjects.Count >= _totalProjectCount)
            txtStatus.Text = $"{_totalProjectCount} commesse";
        else
            txtStatus.Text = $"{_allProjects.Count} di {_totalProjectCount} commesse";
    }
    private void OpenFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Impossibile aprire: {ex.Message}"); }
    }

    private async void RefreshDocNode(int projectId)
    {
        foreach (TreeViewItem projNode in treeProjects.Items)
        {
            if (projNode.Tag?.ToString() == $"project|{projectId}")
            {
                foreach (TreeViewItem child in projNode.Items)
                {
                    if (child.Tag?.ToString() == $"documents|{projectId}")
                    {
                        await LoadFileTree(child, projectId);
                        child.IsExpanded = true;
                        break;
                    }
                }
                break;
            }
        }
    }
    private void ShowBudgetVsActual(int projectId)
    {
        txtSectionTitle.Text = "Preventivo vs Consuntivo";
        btnAction.Visibility = Visibility.Collapsed;
        ShowCachedSection(projectId, "budget_vs_actual",
            () => new BudgetVsCosting.BudgetVsActualControl(),
            ctrl => ctrl.Load(projectId));
    }

    private void ShowCashFlow(int projectId)
    {
        txtSectionTitle.Text = "Flusso di Cassa";
        btnAction.Visibility = Visibility.Collapsed;
        ShowCachedSection(projectId, "cashflow",
            () => new CashFlow.CashFlowControl(),
            ctrl => ctrl.Load(projectId));
    }

    // === CHAT ===
    private void ShowChat(int projectId)
    {
        txtSectionTitle.Text = "Chat";
        btnAction.Visibility = Visibility.Collapsed;
        ShowCachedSection(projectId, "chat",
            () => new ProjectChatControl(),
            ctrl => ctrl.Load(projectId));
    }

    // === VERBALI (MoM) ===
    // Stessa lista di verbali del modulo MoM, filtrata su questa commessa (stessi record).
    private void ShowMoM(int projectId)
    {
        txtSectionTitle.Text = "Verbali (MoM)";
        btnAction.Visibility = Visibility.Collapsed;
        string code = _allProjects.FirstOrDefault(p => p.Id == projectId)?.Code ?? "";
        ShowCachedSection(projectId, "mom",
            () => new ProjectMoMControl(),
            ctrl => ctrl.Load(projectId, code));
    }

    private void ShowDdpCommercial(int projectId)
    {
        txtSectionTitle.Text = "DDP Commerciali";
        btnAction.Visibility = Visibility.Collapsed;
        ShowCachedSection(projectId, "ddp_commercial",
            () => new DdpCommercialControl(),
            ctrl => ctrl.Load(projectId));
    }

    // === DETAILS ===
    private void ShowDetails(int projectId)
    {
        txtSectionTitle.Text = "Dashboard Commessa";
        btnAction.Content = "Modifica";
        btnAction.Visibility = Visibility.Visible;
        btnAction.Tag = $"edit|{projectId}";

        ShowCachedSection(projectId, "details",
            () => new ProjectDashboardControl(),
            ctrl => ctrl.Load(projectId));
    }

    // === DOCUMENTS ===
    private void ShowDocuments(int projectId, string subPath)
    {
        txtSectionTitle.Text = string.IsNullOrEmpty(subPath) ? "Documenti" : subPath;
        btnAction.Visibility = Visibility.Collapsed;

        string sectionKey = string.IsNullOrEmpty(subPath) ? "documents" : $"documents:{subPath}";
        ShowCachedSection(projectId, sectionKey,
            () =>
            {
                DocumentManagerControl ctrl = new();
                ctrl.FilesChanged += () =>
                {
                    InvalidateDocumentCache(projectId);
                    RefreshDocNode(projectId);
                };
                return ctrl;
            },
            ctrl => ctrl.Load(projectId, subPath));
    }
    // === FILE INFO (delegato a FilePreviewControl) ===
    private async void ShowFileInfo(int projectId, string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        txtSectionTitle.Text = fileName;
        btnAction.Content = "Scarica";
        btnAction.Visibility = Visibility.Visible;
        btnAction.Tag = $"download|{projectId}|{relativePath}";

        FilePreviewControl preview = new();
        SectionContent.Content = preview;
        await preview.ShowAsync(projectId, relativePath, SectionContent.ActualHeight);
    }

    // === TIMESHEET ===

    // === TREE SELECTION ===
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string tag)
        {
            var parts = tag.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var id)) return;

            _selectedProjectId = id;

            // Mostra bottone elimina solo su nodi project/details e solo per ADMIN
            bool isProjectNode = parts[0] is "project" or "details";
            btnHardDelete.Visibility = isProjectNode && App.CurrentUser.IsAdmin
                ? Visibility.Visible : Visibility.Collapsed;

            // Salva codice commessa per il messaggio di conferma
            if (isProjectNode)
            {
                var proj = _allProjects.FirstOrDefault(p => p.Id == id);
                _selectedProjectCode = proj?.Code ?? "";
            }

            switch (parts[0])
            {
                case "project":
                    ShowProjectPlaceholder(id);
                    break;
                case "details":
                    ShowDetails(id);
                    break;
                case "cashflow":
                    ShowCashFlow(id);
                    break;
                case "budget_vs_actual":
                    ShowBudgetVsActual(id);
                    break;
                case "chat":
                    ShowChat(id);
                    break;
                case "mom":
                    ShowMoM(id);
                    break;
                case "documents":
                    ShowDocuments(id, "");
                    break;
                case "ddp_commercial":
                    ShowDdpCommercial(id);
                    break;
                case "docfolder":
                    var subPath = parts.Length > 2 ? parts[2] : "";
                    ShowDocuments(id, subPath);
                    break;
                case "file":
                    var filePath = parts.Length > 2 ? parts[2] : "";
                    ShowFileInfo(id, filePath);
                    break;
            }
        }
    }


    // === SPLITTER ===
    private void TreeSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        Services.UserPreferences.Set("projects.tree_width", colTree.ActualWidth);
    }

    // === SEARCH (server-side, debounce) ===
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        CancellationToken token = _searchDebounceCts.Token;
        _ = RunSearchDebouncedAsync(token);
    }

    private async Task RunSearchDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token);
            await LoadTree(append: false);
        }
        catch (TaskCanceledException) { }
    }

    private async void BtnHardDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProjectId <= 0) return;
        string code = _selectedProjectCode;

        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
            $"Eliminare DEFINITIVAMENTE la commessa {code}?\n\n" +
            "Verranno cancellati:\n" +
            "• Tutti i dati (fasi, timesheet, DDP, costing, documenti)\n" +
            "• Le cartelle su disco\n" +
            "• Se derivata da offerta, l'offerta tornerà ACCETTATA\n\n" +
            "Questa operazione è IRREVERSIBILE.",
            "Conferma eliminazione definitiva",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
            $"ULTIMA CONFERMA: cancellare {code}?",
            "Sei sicuro?",
            MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes)
            return;

        try
        {
            string json = await ApiClient.DeleteAsync($"/api/projects/{_selectedProjectId}/hard");
            if (ApiClient.IsApiSuccess(json, out string msg))
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Commessa {code} eliminata.", "Fatto", MessageBoxButton.OK, MessageBoxImage.Information);
                _selectedProjectId = 0;
                _selectedProjectCode = "";
                btnHardDelete.Visibility = Visibility.Collapsed;
                SectionContent.Content = null;
                txtSectionTitle.Text = "Seleziona una commessa";
                await LoadTree(append: false);
            }
            else
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(msg ?? "Errore", "Errore");
            }
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }
}
