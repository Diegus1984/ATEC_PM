using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;
using Microsoft.Win32;

namespace ATEC.PM.Client.Views.Templates;

public class TemplateTreeNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsExpanded { get; set; }
    public ObservableCollection<TemplateTreeNode> Children { get; set; } = new();

    public string Icon => IsFolder ? "📁" : "📄";
    public string SizeLabel => IsFolder ? "" : FormatSize(Size);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
        return $"{bytes / (1024 * 1024.0):F1} MB";
    }
}

public partial class ProjectTemplatePage : Page
{
    private TemplateTreeNode? _selectedNode;

    // Bersaglio del context menu corrente (impostato in Opened, letto da MenuItem_Click)
    private TemplateTreeNode? _menuTarget;

    // Clipboard: nodo + operazione (cut o copy). Per ora solo file supportati.
    private TemplateTreeNode? _clipboardNode;
    private bool _clipboardIsCut;

    // Evita reload completo ad ogni tab-switch — la page è cacheata da MainWindow.
    private bool _loadedOnce;

    // Limite client-side per upload (deve combaciare con TemplateController.MaxFileSizeBytes)
    private const long MaxUploadBytes = 500L * 1024 * 1024; // 500 MB

    private const string UploadFileFilter =
        "Tutti i file template|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.csv;*.txt;*.rtf;" +
        "*.dwg;*.dxf;*.step;*.stp;*.stl;*.iges;*.igs;*.obj;" +
        "*.sldprt;*.sldasm;*.slddrw;*.easm;*.eprt;*.edrw;" +
        "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.zip;*.rar;*.7z|" +
        "Documenti (PDF, Word, Excel)|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.csv;*.txt;*.rtf|" +
        "CAD (DWG, STEP, SolidWorks)|*.dwg;*.dxf;*.step;*.stp;*.stl;*.iges;*.igs;*.obj;*.sldprt;*.sldasm;*.slddrw;*.easm;*.eprt;*.edrw|" +
        "Immagini|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|" +
        "Archivi|*.zip;*.rar;*.7z";

    public ProjectTemplatePage()
    {
        InitializeComponent();
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true && !_loadedOnce)
                await LoadTreeAsync();
        };
    }

    // ══════════════════════════════════════════════════════════════
    // LOAD
    // ══════════════════════════════════════════════════════════════

    private async Task LoadTreeAsync()
    {
        ShowOverlay("📂", "Caricamento...", "");

        // Salva stato di espansione e selezione PRIMA del reload (via TreeViewStateHelper)
        // NB: solo le cartelle sono espandibili → predicato filtra anche su IsFolder
        HashSet<int> expandedFolderIds = TreeViewStateHelper.CollectExpandedIds(
            tvTemplates.ItemsSource as IEnumerable<TemplateTreeNode>,
            isExpanded: n => n.IsFolder && n.IsExpanded,
            idOf: n => n.Id,
            childrenOf: n => n.Children);

        (int Id, bool IsFolder)? selectionKey = _selectedNode is null
            ? null
            : (_selectedNode.Id, _selectedNode.IsFolder);

        try
        {
            List<TemplateFolderNode> roots = await ApiClient.GetListAsync<TemplateFolderNode>(
                "/api/project-templates/tree");

            ObservableCollection<TemplateTreeNode> tree = new();
            foreach (TemplateFolderNode root in roots)
                tree.Add(ConvertNode(root));

            // Ripristina IsExpanded sui nodi cartella che lo erano prima
            TreeViewStateHelper.ApplyExpandedState(
                tree, expandedFolderIds,
                idOf: n => n.Id,
                setExpanded: (n, v) => { if (n.IsFolder) n.IsExpanded = v; },
                childrenOf: n => n.Children);

            tvTemplates.ItemsSource = tree;

            // Riallinea _selectedNode al nuovo oggetto corrispondente (toolbar/menu coerenti)
            if (selectionKey.HasValue)
            {
                _selectedNode = TreeViewStateHelper.FindNode(
                    tree,
                    predicate: n => n.Id == selectionKey.Value.Id && n.IsFolder == selectionKey.Value.IsFolder,
                    childrenOf: n => n.Children);
            }

            if (tree.Count == 0)
                ShowOverlay("📭", "Nessun template configurato",
                    "Usa “+ Nuova Cartella” o tasto destro per iniziare a creare la struttura.");
            else
                HideOverlay();

            _loadedOnce = true;
        }
        catch (HttpRequestException ex)
        {
            ShowOverlay("⚠️", "Server non raggiungibile",
                $"Verifica che ATEC.PM.Server sia avviato.\n({ex.Message})");
        }
        catch (Exception ex)
        {
            ShowOverlay("⚠️", "Errore di caricamento", ex.Message);
        }
    }

    private static TemplateTreeNode ConvertNode(TemplateFolderNode src)
    {
        TemplateTreeNode node = new()
        {
            Id = src.Id,
            Name = src.Name,
            IsFolder = true,
            ParentId = src.ParentId,
            SortOrder = src.SortOrder
        };

        foreach (TemplateFolderNode child in src.Children)
            node.Children.Add(ConvertNode(child));

        foreach (TemplateFileItem file in src.Files)
        {
            node.Children.Add(new TemplateTreeNode
            {
                Id = file.Id,
                Name = file.FileName,
                IsFolder = false,
                Size = file.FileSize,
                ParentId = src.Id
            });
        }
        return node;
    }

    private void ShowOverlay(string icon, string title, string detail)
    {
        txtOverlayIcon.Text = icon;
        txtOverlayTitle.Text = title;
        txtOverlayDetail.Text = detail;
        overlayMessage.Visibility = Visibility.Visible;
    }

    private void HideOverlay() => overlayMessage.Visibility = Visibility.Collapsed;

    // ══════════════════════════════════════════════════════════════
    // SELEZIONE + TOOLBAR
    // ══════════════════════════════════════════════════════════════

    private void TvTemplates_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedNode = e.NewValue as TemplateTreeNode;
        bool hasSelection = _selectedNode != null;
        btnRename.IsEnabled = hasSelection;
        btnDelete.IsEnabled = hasSelection;
        btnUpload.IsEnabled = _selectedNode is { IsFolder: true };
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _loadedOnce = false;
        await LoadTreeAsync();
    }

    private async void BtnNewFolder_Click(object sender, RoutedEventArgs e) =>
        await CreateFolderAsync(_selectedNode is { IsFolder: true } ? _selectedNode : null);

    private async void BtnUploadFile_Click(object sender, RoutedEventArgs e) =>
        await UploadFileAsync(_selectedNode is { IsFolder: true } ? _selectedNode : null);

    private async void BtnRename_Click(object sender, RoutedEventArgs e) =>
        await RenameAsync(_selectedNode);

    private async void BtnDelete_Click(object sender, RoutedEventArgs e) =>
        await DeleteAsync(_selectedNode);

    // ══════════════════════════════════════════════════════════════
    // CONTEXT MENU
    // ══════════════════════════════════════════════════════════════

    private void NodeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu cm) return;
        _menuTarget = (cm.PlacementTarget as FrameworkElement)?.DataContext as TemplateTreeNode;
        if (_menuTarget == null) { cm.IsOpen = false; return; }

        bool isFolder = _menuTarget.IsFolder;
        bool hasClipboard = _clipboardNode != null;

        foreach (object item in cm.Items)
        {
            if (item is MenuItem mi)
            {
                string tag = mi.Tag as string ?? "";
                mi.Visibility = tag switch
                {
                    "newfolder" => isFolder ? Visibility.Visible : Visibility.Collapsed,
                    "upload"    => isFolder ? Visibility.Visible : Visibility.Collapsed,
                    "paste"     => isFolder && hasClipboard ? Visibility.Visible : Visibility.Collapsed,
                    _           => Visibility.Visible
                };
            }
            else if (item is Separator sep && sep.Tag as string == "sep_folder")
            {
                sep.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private async void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || _menuTarget == null) return;
        TemplateTreeNode target = _menuTarget;

        switch (mi.Tag as string)
        {
            case "newfolder": await CreateFolderAsync(target); break;
            case "upload":    await UploadFileAsync(target); break;
            case "rename":    await RenameAsync(target); break;
            case "cut":       SetClipboard(target, isCut: true); break;
            case "copy":      SetClipboard(target, isCut: false); break;
            case "paste":     await PasteAsync(target); break;
            case "delete":    await DeleteAsync(target); break;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // SCORCIATOIE DA TASTIERA (F2, Canc, Ctrl+X/C/V)
    // ══════════════════════════════════════════════════════════════

    private async void TvTemplates_KeyDown(object sender, KeyEventArgs e)
    {
        if (_selectedNode == null) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (e.Key == Key.F2)
        {
            e.Handled = true;
            await RenameAsync(_selectedNode);
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            await DeleteAsync(_selectedNode);
        }
        else if (ctrl && e.Key == Key.X)
        {
            e.Handled = true;
            SetClipboard(_selectedNode, isCut: true);
        }
        else if (ctrl && e.Key == Key.C)
        {
            e.Handled = true;
            SetClipboard(_selectedNode, isCut: false);
        }
        else if (ctrl && e.Key == Key.V)
        {
            e.Handled = true;
            if (_selectedNode.IsFolder && _clipboardNode != null)
                await PasteAsync(_selectedNode);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // OPERAZIONI (riusate da toolbar, context menu e shortcut)
    // ══════════════════════════════════════════════════════════════

    private async Task CreateFolderAsync(TemplateTreeNode? parent)
    {
        InputDialog dlg = new("Nuova Cartella", "Nome cartella:") { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;

        int? parentId = parent is { IsFolder: true } ? parent.Id : null;
        string body = JsonSerializer.Serialize(new { parentId, name = dlg.InputText, sortOrder = 0 });
        string json = await ApiClient.PostAsync("/api/project-templates/folders", body);

        if (ApiClient.IsApiSuccess(json, out string _))
        {
            if (parent is { IsFolder: true }) parent.IsExpanded = true;
            _loadedOnce = false;
            await LoadTreeAsync();
        }
        else
            Warn("Errore nella creazione della cartella.");
    }

    private async Task UploadFileAsync(TemplateTreeNode? folder)
    {
        if (folder is not { IsFolder: true })
        {
            Warn("Seleziona prima una cartella di destinazione.");
            return;
        }

        OpenFileDialog ofd = new()
        {
            Multiselect = false,
            Title = "Seleziona file da caricare",
            Filter = UploadFileFilter
        };
        if (ofd.ShowDialog() != true) return;

        // Check dimensione client-side (evita upload lunghissimi che il server rifiuta)
        try
        {
            long size = new System.IO.FileInfo(ofd.FileName).Length;
            if (size > MaxUploadBytes)
            {
                Warn($"File troppo grande ({size / (1024 * 1024)} MB). " +
                     $"Massimo consentito: {MaxUploadBytes / (1024 * 1024)} MB.");
                return;
            }
        }
        catch (Exception ex)
        {
            Warn($"Impossibile leggere il file: {ex.Message}");
            return;
        }

        string endpoint = $"/api/project-templates/folders/{folder.Id}/upload";
        string json = await ApiClient.UploadFileAsync(endpoint, ofd.FileName);

        if (ApiClient.IsApiSuccess(json, out string _))
        {
            folder.IsExpanded = true;
            _loadedOnce = false; // forza refresh dopo modifica
            await LoadTreeAsync();
        }
        else
            Warn("Errore nel caricamento del file.");
    }

    private async Task RenameAsync(TemplateTreeNode? node)
    {
        if (node == null) return;

        InputDialog dlg = new("Rinomina", "Nuovo nome:", node.Name) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;
        if (dlg.InputText == node.Name) return;

        string json;
        if (node.IsFolder)
        {
            string body = JsonSerializer.Serialize(new
            {
                name = dlg.InputText,
                parentId = node.ParentId,
                sortOrder = node.SortOrder
            });
            json = await ApiClient.PutAsync($"/api/project-templates/folders/{node.Id}", body);
        }
        else
        {
            string body = JsonSerializer.Serialize(new { name = dlg.InputText });
            json = await ApiClient.PutAsync($"/api/project-templates/files/{node.Id}", body);
        }

        if (ApiClient.IsApiSuccess(json, out string _))
        {
            _loadedOnce = false;
            await LoadTreeAsync();
        }
        else
            Warn("Errore nella rinomina.");
    }

    private async Task DeleteAsync(TemplateTreeNode? node)
    {
        if (node == null) return;

        string message;
        if (node.IsFolder)
        {
            (int folders, int files) = CountDescendants(node);
            string detail = (folders == 0 && files == 0)
                ? "(cartella vuota)"
                : $"(contiene {folders} cartelle e {files} file — verranno eliminati anch'essi)";
            message = $"Eliminare la cartella “{node.Name}”?\n{detail}";
        }
        else
        {
            message = $"Eliminare il file “{node.Name}”?";
        }

        if (ShadcnMessageBox.Show(message, "Conferma eliminazione",
                MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;

        string type = node.IsFolder ? "folders" : "files";
        string json = await ApiClient.DeleteAsync($"/api/project-templates/{type}/{node.Id}");

        if (ApiClient.IsApiSuccess(json, out string _))
        {
            // Se ho eliminato qualcosa che era nel clipboard, lo svuoto
            if (_clipboardNode?.Id == node.Id && _clipboardNode.IsFolder == node.IsFolder)
                ClearClipboard();
            _loadedOnce = false;
            await LoadTreeAsync();
        }
        else
            Warn("Errore nell'eliminazione.");
    }

    /// <summary>Conta ricorsivamente sottocartelle e file in un nodo cartella.</summary>
    private static (int folders, int files) CountDescendants(TemplateTreeNode root)
    {
        int folders = 0, files = 0;
        foreach (TemplateTreeNode child in root.Children)
        {
            if (child.IsFolder)
            {
                folders++;
                (int f, int x) = CountDescendants(child);
                folders += f;
                files += x;
            }
            else
            {
                files++;
            }
        }
        return (folders, files);
    }

    // ══════════════════════════════════════════════════════════════
    // CLIPBOARD (cut / copy / paste)
    // ══════════════════════════════════════════════════════════════

    private void SetClipboard(TemplateTreeNode node, bool isCut)
    {
        _clipboardNode = node;
        _clipboardIsCut = isCut;
    }

    private void ClearClipboard()
    {
        _clipboardNode = null;
        _clipboardIsCut = false;
    }

    private async Task PasteAsync(TemplateTreeNode targetFolder)
    {
        if (_clipboardNode == null || !targetFolder.IsFolder) return;

        // Non si incolla dentro se stessi né dentro un proprio discendente (proteggo i cicli per le cartelle)
        if (_clipboardNode.Id == targetFolder.Id && _clipboardNode.IsFolder)
        {
            Warn("Impossibile incollare una cartella dentro se stessa.");
            return;
        }
        if (_clipboardNode.IsFolder && TreeViewStateHelper.IsDescendant(
                _clipboardNode, targetFolder.Id, n => n.Id, n => n.Children))
        {
            Warn("Impossibile spostare o copiare una cartella dentro un suo discendente.");
            return;
        }

        string json;
        if (_clipboardNode.IsFolder)
        {
            if (_clipboardIsCut)
            {
                // Spostamento (CUT): cambio parent_id via UpdateFolder
                string body = JsonSerializer.Serialize(new
                {
                    name = _clipboardNode.Name,
                    parentId = (int?)targetFolder.Id,
                    sortOrder = _clipboardNode.SortOrder
                });
                json = await ApiClient.PutAsync($"/api/project-templates/folders/{_clipboardNode.Id}", body);
            }
            else
            {
                // Copia ricorsiva (COPY): nuovo endpoint server-side
                string body = JsonSerializer.Serialize(new { folderId = targetFolder.Id });
                json = await ApiClient.PostAsync(
                    $"/api/project-templates/folders/{_clipboardNode.Id}/copy", body);
            }
        }
        else
        {
            string endpoint = _clipboardIsCut ? "move" : "copy";
            string body = JsonSerializer.Serialize(new { folderId = targetFolder.Id });
            json = await ApiClient.PostAsync(
                $"/api/project-templates/files/{_clipboardNode.Id}/{endpoint}", body);
        }

        if (ApiClient.IsApiSuccess(json, out string _))
        {
            targetFolder.IsExpanded = true;
            if (_clipboardIsCut) ClearClipboard();
            _loadedOnce = false;
            await LoadTreeAsync();
        }
        else
            Warn("Errore durante l'incolla.");
    }

    private static void Warn(string msg) =>
        ShadcnMessageBox.Show(msg, "Avviso", MessageBoxButton.OK, MessageBoxImage.Information);
}
