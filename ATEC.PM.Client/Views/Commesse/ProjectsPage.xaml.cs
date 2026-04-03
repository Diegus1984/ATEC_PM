using ATEC.PM.Client.UserControls;
namespace ATEC.PM.Client.Views;

using System.Windows.Forms.Integration;
using ATEC.PM.Client.Controls;


public partial class ProjectsPage : Page
{
    private List<ProjectListItem> _allProjects = new();
    private int _selectedProjectId;
    private string _selectedProjectCode = "";

    private int _pendingNavProjectId;

    private string _pendingNavSection = "";

    public ProjectsPage()
    {
        InitializeComponent();

        // Ripristina larghezza pannello tree
        double savedWidth = Services.UserPreferences.GetDouble("projects.tree_width", 250);
        colTree.Width = new GridLength(Math.Clamp(savedWidth, 180, 500));

        Loaded += async (_, _) =>
        {
            await LoadTree();
            if (_pendingNavProjectId > 0)
            {
                int pid = _pendingNavProjectId;
                string sec = _pendingNavSection;
                _pendingNavProjectId = 0;
                _pendingNavSection = "";

                // Trova e espandi il nodo della commessa nel TreeView
                foreach (TreeViewItem projNode in treeProjects.Items)
                {
                    string tag = projNode.Tag?.ToString() ?? "";
                    if (tag == $"project|{pid}")
                    {
                        projNode.IsExpanded = true;
                        projNode.BringIntoView();

                        // Trova e seleziona il sotto-nodo
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
            }
        };
    }

    public void NavigateToSection(int projectId, string section)
    {
        _pendingNavProjectId = projectId;
        _pendingNavSection = section;
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex) =>
                new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:N0} KB";
        return $"{bytes / 1024.0 / 1024.0:N1} MB";
    }

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

    private void AddInfoRow(Grid grid, int row, string label, string value)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock { Text = label.ToUpper(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 6, 0, 2) };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        var val = new TextBlock { Text = value, FontSize = 13, Foreground = Brush("#1A1D26"), Margin = new Thickness(0, 6, 0, 2), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(val, row);
        Grid.SetColumn(val, 2);
        grid.Children.Add(val);
    }

    // === ACTIONS ===
    private async void BtnAction_Click(object sender, RoutedEventArgs e)
    {
        var tag = btnAction.Tag?.ToString() ?? "";
        var parts = tag.Split('|');
        if (parts[0] == "edit" && parts.Length > 1 && int.TryParse(parts[1], out var editId))
        {
            var dlg = new ProjectDialog(editId) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) await LoadTree();
        }
        else if (parts[0] == "createfolder" && parts.Length > 1 && int.TryParse(parts[1], out var cfId))
        {
            try
            {
                var json = await ApiClient.PostAsync($"/api/projects/{cfId}/create-folder", "{}");
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                {
                    RefreshDocNode(cfId);
                    ShowDocuments(cfId, "");
                }
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        }
        else if (parts[0] == "openfolder" && parts.Length > 1 && int.TryParse(parts[1], out var ofId))
        {
            var projJson = await ApiClient.GetAsync($"/api/projects/{ofId}");
            var projDoc = JsonDocument.Parse(projJson);
            var sp = projDoc.RootElement.GetProperty("data").GetProperty("serverPath").GetString() ?? "";
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
                    MessageBox.Show("Impossibile scaricare il file.");
                    return;
                }
                var tempDir = Path.Combine(Path.GetTempPath(), "ATEC_PM");
                Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, fileName);
                File.WriteAllBytes(tempFile, bytes);
                OpenFile(tempFile);
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        }
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProjectDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = LoadTree();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadTree();

    private void BuildTree(List<ProjectListItem> projects)
    {
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
            projNode.Items.Add(new TreeViewItem { Header = "📋 DDP Commerciali", Tag = $"ddp_commercial|{p.Id}" });

            var docNode = new TreeViewItem { Header = "📁 Documenti", Tag = $"documents|{p.Id}" };
            // Placeholder per lazy-load al primo expand
            docNode.Items.Add(new TreeViewItem { Header = "Caricamento...", IsEnabled = false });
            docNode.Expanded += DocNode_Expanded;
            projNode.Items.Add(docNode);

            treeProjects.Items.Add(projNode);
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
            var json = await ApiClient.GetAsync($"/api/projects/{projectId}/file-tree");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean())
            {
                parentNode.Items.Add(new TreeViewItem { Header = "Cartella non creata", IsEnabled = false, FontStyle = FontStyles.Italic });
                return;
            }

            var items = JsonSerializer.Deserialize<List<FileTreeItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            if (items.Count == 0)
            {
                parentNode.Items.Add(new TreeViewItem { Header = "Cartella non creata", IsEnabled = false, FontStyle = FontStyles.Italic });
                return;
            }

            AddFileTreeNodes(parentNode, items, projectId);
        }
        catch
        {
            parentNode.Items.Add(new TreeViewItem { Header = "Errore caricamento", IsEnabled = false });
        }
    }

    // === LOAD TREE ===
    private async Task LoadTree()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            var json = await ApiClient.GetAsync("/api/projects");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _allProjects = JsonSerializer.Deserialize<List<ProjectListItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                BuildTree(_allProjects);
                txtStatus.Text = $"{_allProjects.Count} commesse";
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }
    private void OpenFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Impossibile aprire: {ex.Message}"); }
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
        var ctrl = new BudgetVsCosting.BudgetVsActualControl();
        SectionContent.Content = ctrl;
        ctrl.Load(projectId);
    }

    private void ShowCashFlow(int projectId)
    {
        txtSectionTitle.Text = "Flusso di Cassa";
        btnAction.Visibility = Visibility.Collapsed;
        var ctrl = new CashFlow.CashFlowControl();
        SectionContent.Content = ctrl;
        ctrl.Load(projectId);
    }

    // === CHAT ===
    private void ShowChat(int projectId)
    {
        txtSectionTitle.Text = "Chat";
        btnAction.Visibility = Visibility.Collapsed;
        var ctrl = new ProjectChatControl();
        SectionContent.Content = ctrl;
        ctrl.Load(projectId);
    }

    private void ShowDdpCommercial(int projectId)
    {
        txtSectionTitle.Text = "DDP Commerciali";
        btnAction.Visibility = Visibility.Collapsed;
        var ddpControl = new DdpCommercialControl();
        SectionContent.Content = ddpControl;
        ddpControl.Load(projectId);
    }

    // === DETAILS ===
    private void ShowDetails(int projectId)
    {
        txtSectionTitle.Text = "Dashboard Commessa";
        btnAction.Content = "Modifica";
        btnAction.Visibility = Visibility.Visible;
        btnAction.Tag = $"edit|{projectId}";

        var dashboard = new ProjectDashboardControl();
        SectionContent.Content = dashboard;
        dashboard.Load(projectId);
    }

    // === DOCUMENTS ===
    private void ShowDocuments(int projectId, string subPath)
    {
        txtSectionTitle.Text = string.IsNullOrEmpty(subPath) ? "Documenti" : subPath;
        btnAction.Visibility = Visibility.Collapsed;
        var ctrl = new DocumentManagerControl();
        ctrl.FilesChanged += () => RefreshDocNode(projectId);
        SectionContent.Content = ctrl;
        ctrl.Load(projectId, subPath);
    }
    // === FILE INFO ===
    private async void ShowFileInfo(int projectId, string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        var ext = Path.GetExtension(fileName).ToLower();
        txtSectionTitle.Text = fileName;
        btnAction.Content = "Scarica";
        btnAction.Visibility = Visibility.Visible;
        btnAction.Tag = $"download|{projectId}|{relativePath}";

        try
        {
            // Anteprima PDF
            if (ext == ".pdf")
            {
                var encoded = Uri.EscapeDataString(relativePath);
                var bytes = await ApiClient.DownloadAsync($"/api/projects/{projectId}/download?path={encoded}");

                if (bytes == null || bytes.Length == 0)
                {
                    SectionContent.Content = new TextBlock { Text = "Impossibile scaricare il file.", FontSize = 14, Foreground = System.Windows.Media.Brushes.Gray };
                    return;
                }

                var tempDir = Path.Combine(Path.GetTempPath(), "ATEC_PM");
                Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, fileName);
                File.WriteAllBytes(tempFile, bytes);

                var panel = new DockPanel();
                var infoBar = new Border
                {
                    Background = Brush("#F7F8FA"),
                    BorderBrush = Brush("#E4E7EC"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                infoBar.Child = new TextBlock
                {
                    Text = $"📕  {fileName} (Anteprima)",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(infoBar, Dock.Top);
                panel.Children.Add(infoBar);

                var webView = new Microsoft.Web.WebView2.Wpf.WebView2
                {
                    Height = Math.Max(500, SectionContent.ActualHeight - 60)
                };
                panel.Children.Add(webView);
                SectionContent.Content = panel;

                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Navigate(new Uri(tempFile).AbsoluteUri);
                return;
            }
            // Anteprima Word
            else if (ext is ".doc" or ".docx")
            {
                if (ext == ".doc")
                {
                    // .doc binario: non supportato in anteprima, apri esterno
                    var infoPanel = new StackPanel();
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $"📘  {fileName}",
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 16)
                    });
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = "Il formato .doc (Word 97-2003) non supporta anteprima integrata.\nUsa il pulsante 'Scarica' per aprirlo con Word.",
                        FontSize = 14,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        TextWrapping = TextWrapping.Wrap
                    });
                    SectionContent.Content = infoPanel;
                    return;
                }

                // .docx: anteprima con Mammoth
                try
                {
                    var encoded = Uri.EscapeDataString(relativePath);
                    var html = await ApiClient.GetRawAsync($"/api/projects/{projectId}/preview?path={encoded}");

                    if (string.IsNullOrEmpty(html))
                    {
                        SectionContent.Content = new TextBlock { Text = "HTML vuoto dal server", Foreground = System.Windows.Media.Brushes.Red };
                        return;
                    }

                    var tempDir = Path.Combine(Path.GetTempPath(), "ATEC_PM");
                    Directory.CreateDirectory(tempDir);
                    var tempHtml = Path.Combine(tempDir, $"preview_doc_{projectId}.html");
                    File.WriteAllText(tempHtml, html, System.Text.Encoding.UTF8);

                    var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
                    SectionContent.Content = webView;

                    await webView.EnsureCoreWebView2Async();
                    webView.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri);
                }
                catch (Exception ex)
                {
                    SectionContent.Content = new TextBlock
                    {
                        Text = $"Errore preview: {ex.GetType().Name}: {ex.Message}",
                        FontSize = 12,
                        Foreground = System.Windows.Media.Brushes.Red,
                        TextWrapping = TextWrapping.Wrap
                    };
                }
                return;
            }
            // Anteprima Excel / CSV
            else if (ext is ".xlsx" or ".xls" or ".csv")
            {
                try
                {
                    var encoded = Uri.EscapeDataString(relativePath);
                    var html = await ApiClient.GetRawAsync($"/api/projects/{projectId}/preview?path={encoded}");

                    if (string.IsNullOrEmpty(html))
                    {
                        SectionContent.Content = new TextBlock { Text = "HTML vuoto dal server", Foreground = System.Windows.Media.Brushes.Red };
                        return;
                    }

                    var tempDir = Path.Combine(Path.GetTempPath(), "ATEC_PM");
                    Directory.CreateDirectory(tempDir);
                    var tempHtml = Path.Combine(tempDir, $"preview_{projectId}.html");
                    File.WriteAllText(tempHtml, html, System.Text.Encoding.UTF8);

                    var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
                    SectionContent.Content = webView;

                    await webView.EnsureCoreWebView2Async();
                    webView.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri);
                }
                catch (Exception ex)
                {
                    SectionContent.Content = new TextBlock
                    {
                        Text = $"Errore preview: {ex.GetType().Name}: {ex.Message}",
                        FontSize = 12,
                        Foreground = System.Windows.Media.Brushes.Red,
                        TextWrapping = TextWrapping.Wrap
                    };
                }
                return;
            }
            // Anteprima immagini
            else if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp")
            {
                var encoded = Uri.EscapeDataString(relativePath);
                var bytes = await ApiClient.DownloadAsync($"/api/projects/{projectId}/download?path={encoded}");

                if (bytes == null || bytes.Length == 0)
                {
                    SectionContent.Content = new TextBlock { Text = "Impossibile scaricare l'immagine.", FontSize = 14, Foreground = System.Windows.Media.Brushes.Gray };
                    return;
                }

                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();

                var imgPanel = new StackPanel();
                imgPanel.Children.Add(new TextBlock { Text = $"🖼  {fileName}", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
                imgPanel.Children.Add(new System.Windows.Controls.Image { Source = bmp, MaxHeight = 500, Stretch = System.Windows.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });

                SectionContent.Content = imgPanel;
            }
            // ── Anteprima DWG / DXF (ACadSharp nativo WPF) ──────────────
            else if (ext is ".dwg" or ".dxf")
            {
                var encoded = Uri.EscapeDataString(relativePath);
                var bytes = await ApiClient.DownloadAsync($"/api/projects/{projectId}/download?path={encoded}");

                if (bytes == null || bytes.Length == 0)
                {
                    SectionContent.Content = new TextBlock { Text = "Impossibile scaricare il file.", FontSize = 14, Foreground = System.Windows.Media.Brushes.Gray };
                    return;
                }

                var tempDir = Path.Combine(Path.GetTempPath(), "ATEC_PM", "cad");
                Directory.CreateDirectory(tempDir);

                // Pulisci file vecchi (ignora quelli bloccati)
                try { foreach (var old in Directory.GetFiles(tempDir)) { try { File.Delete(old); } catch { } } } catch { }

                var tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{fileName}");
                File.WriteAllBytes(tempFile, bytes);

                var panel = new DockPanel();
                var infoBar = new Border
                {
                    Background = Brush("#F7F8FA"),
                    BorderBrush = Brush("#E4E7EC"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                infoBar.Child = new TextBlock
                {
                    Text = $"📐  {fileName} (Vista CAD — scroll per zoom, trascina per muovere)",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(infoBar, Dock.Top);
                panel.Children.Add(infoBar);

                var cadViewer = new CadViewerControl
                {
                    Height = Math.Max(500, SectionContent.ActualHeight - 60)
                };
                panel.Children.Add(cadViewer);
                SectionContent.Content = panel;

                // Carica il file dopo che il controllo è stato renderizzato
                cadViewer.Loaded += (_, _) => cadViewer.LoadFile(tempFile);
            }
            // ── Anteprima SolidWorks nativo (eDrawings) ─────────────────
            else if (ext is ".sldprt" or ".sldasm" or ".slddrw" or ".easm" or ".eprt" or ".edrw" or ".iges" or ".igs")
            {
                // Scarica il file in temp
                var encoded = Uri.EscapeDataString(relativePath);
                var bytes = await ApiClient.DownloadAsync($"/api/projects/{projectId}/download?path={encoded}");

                if (bytes == null || bytes.Length == 0)
                {
                    SectionContent.Content = new TextBlock { Text = "Impossibile scaricare il file.", FontSize = 14, Foreground = System.Windows.Media.Brushes.Gray };
                    return;
                }

                var tempDir = Path.Combine(Path.GetTempPath(), "ATEC_PM", "cad");
                Directory.CreateDirectory(tempDir);

                // Pulisci file vecchi (ignora quelli ancora bloccati)
                try
                {
                    foreach (var old in Directory.GetFiles(tempDir))
                    {
                        try { File.Delete(old); } catch { }
                    }
                }
                catch { }

                var tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{fileName}");
                File.WriteAllBytes(tempFile, bytes);

                // Verifica se eDrawings è installato
                bool eDrawingsInstalled = false;
                try
                {
                    using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"EModelView.EModelNonVersionSpecificViewControl\CLSID");
                    eDrawingsInstalled = key != null;
                }
                catch { }

                if (!eDrawingsInstalled)
                {
                    var infoPanel = new StackPanel { Margin = new Thickness(20) };
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = $"🔧  {fileName}",
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 16)
                    });
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = "eDrawings Viewer non è installato su questo PC.\n\n" +
                               "Per visualizzare file SolidWorks, DXF, DWG e IGES è necessario installare\n" +
                               "eDrawings Viewer (gratuito) da: https://www.edrawingsviewer.com/download-edrawings\n\n" +
                               "Usa il pulsante 'Scarica' per aprire il file con un'applicazione esterna.",
                        FontSize = 13,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        TextWrapping = TextWrapping.Wrap
                    });
                    SectionContent.Content = infoPanel;
                    return;
                }

                // Crea il controllo eDrawings via WindowsFormsHost
                var panel = new DockPanel();

                var infoBar = new Border
                {
                    Background = Brush("#F7F8FA"),
                    BorderBrush = Brush("#E4E7EC"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                infoBar.Child = new TextBlock
                {
                    Text = $"🔧  {fileName} (eDrawings — trascina per ruotare, scroll per zoom)",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(infoBar, Dock.Top);
                panel.Children.Add(infoBar);

                try
                {
                    var host = new WindowsFormsHost
                    {
                        Height = Math.Max(500, SectionContent.ActualHeight - 60)
                    };

                    var eDrawCtrl = new EDrawingHost();
                    string fileToOpen = tempFile; // cattura per la closure
                    eDrawCtrl.ControlLoaded += (ctrl) =>
                    {
                        try
                        {
                            dynamic eView = ctrl;
                            eView.OpenDoc(fileToOpen, false, false, false, "");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[eDrawings] Errore apertura: {ex.Message}");
                        }
                    };

                    host.Child = eDrawCtrl;
                    panel.Children.Add(host);
                    SectionContent.Content = panel;
                }
                catch (Exception ex)
                {
                    SectionContent.Content = new TextBlock
                    {
                        Text = $"Errore caricamento eDrawings: {ex.Message}\n\nUsa 'Scarica' per aprire il file esternamente.",
                        FontSize = 13,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(20)
                    };
                }
            }
            // ── Anteprima STEP / STL / OBJ (3D viewer three.js offline) ─
            else if (ext is ".step" or ".stp" or ".stl" or ".obj")
            {
                var encoded = Uri.EscapeDataString(relativePath);
                var bytes = await ApiClient.DownloadAsync($"/api/projects/{projectId}/download?path={encoded}");

                if (bytes == null || bytes.Length == 0)
                {
                    SectionContent.Content = new TextBlock { Text = "Impossibile scaricare il file.", FontSize = 14, Foreground = System.Windows.Media.Brushes.Gray };
                    return;
                }

                var tempDir = Path.Combine(Path.GetTempPath(), "ATEC_PM", "3d");
                Directory.CreateDirectory(tempDir);
                var tempModel = Path.Combine(tempDir, fileName);
                File.WriteAllBytes(tempModel, bytes);

                string viewerBase = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ATEC_PM", "3DViewer");

                if (!Directory.Exists(viewerBase) || !File.Exists(Path.Combine(viewerBase, "three.module.js")))
                {
                    SectionContent.Content = new TextBlock
                    {
                        Text = "⚠ Librerie 3D non trovate.\n\nEseguire lo script Download_3DViewer.ps1 per scaricare le dipendenze.\nCartella attesa: " + viewerBase,
                        FontSize = 13,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(20)
                    };
                    return;
                }

                string isStep = (ext is ".step" or ".stp") ? "true" : "false";
                string modelFileName = fileName.Replace("'", "\\'");

                var modelsDir = Path.Combine(tempDir, "models");
                Directory.CreateDirectory(modelsDir);
                File.Copy(tempModel, Path.Combine(modelsDir, fileName), true);

                string viewerHtml = $@"<!DOCTYPE html>
                    <html><head>
                    <meta charset='utf-8'/>
                    <style>
                      body {{ margin:0; overflow:hidden; background:#1a1d26; }}
                      canvas {{ display:block; }}
                      #info {{ position:absolute; top:10px; left:12px; color:#999; font:13px sans-serif; z-index:10; }}
                      #info.ok {{ color:#12B76A; }}
                      #info.err {{ color:#F04438; }}
                    </style>
                    </head><body>
                    <div id='info'>🔄 Caricamento modello...</div>
                    <script type='importmap'>
                    {{
                      ""imports"": {{
                        ""three"": ""https://atec.libs/three.module.js"",
                        ""three/addons/"": ""https://atec.libs/""
                      }}
                    }}
                    </script>
                    <script type='module'>
                    import * as THREE from 'three';
                    import {{ OrbitControls }} from 'three/addons/OrbitControls.js';
                    import {{ STLLoader }} from 'three/addons/STLLoader.js';
                    import {{ OBJLoader }} from 'three/addons/OBJLoader.js';

                    const info = document.getElementById('info');
                    const scene = new THREE.Scene();
                    scene.background = new THREE.Color(0x1a1d26);

                    const camera = new THREE.PerspectiveCamera(45, window.innerWidth/window.innerHeight, 0.1, 100000);
                    const renderer = new THREE.WebGLRenderer({{ antialias:true }});
                    renderer.setSize(window.innerWidth, window.innerHeight);
                    renderer.setPixelRatio(window.devicePixelRatio);
                    renderer.toneMapping = THREE.NoToneMapping;
                    document.body.appendChild(renderer.domElement);

                    const controls = new OrbitControls(camera, renderer.domElement);
                    controls.enableDamping = true;
                    controls.dampingFactor = 0.08;

                    scene.add(new THREE.AmbientLight(0xffffff, 1.0));
                    const dir1 = new THREE.DirectionalLight(0xffffff, 1.5);
                    dir1.position.set(5, 10, 7);
                    scene.add(dir1);
                    const dir2 = new THREE.DirectionalLight(0xffffff, 0.8);
                    dir2.position.set(-5, -3, -5);
                    scene.add(dir2);
                    const dir3 = new THREE.DirectionalLight(0xffffff, 0.6);
                    dir3.position.set(0, -8, 3);
                    scene.add(dir3);
                    const dir4 = new THREE.DirectionalLight(0xffffff, 0.4);
                    dir4.position.set(-3, 5, -8);
                    scene.add(dir4);

                    const grid = new THREE.GridHelper(1000, 20, 0x2a2d36, 0x2a2d36);
                    scene.add(grid);

                    const material = new THREE.MeshStandardMaterial({{color: 0xB0B8C8, metalness: 0.3, roughness: 0.5}});

                    function fitCamera(object) {{
                        const box = new THREE.Box3().setFromObject(object);
                        const center = box.getCenter(new THREE.Vector3());
                        const size = box.getSize(new THREE.Vector3()).length();
                        const dist = size * 1.5;
                        camera.position.copy(center.clone().add(new THREE.Vector3(dist*0.6, dist*0.4, dist*0.6)));
                        camera.near = size / 1000;
                        camera.far = size * 100;
                        camera.updateProjectionMatrix();
                        controls.target.copy(center);
                        controls.update();
                        grid.scale.setScalar(size / 500);
                        grid.position.y = box.min.y;
                    }}

                    const isStep = {isStep};
                    const modelUrl = 'https://atec.models/{fileName}';

                    try {{
                        if (isStep) {{
                            const resp = await fetch('https://atec.libs/occt-import-js.js');
                            const scriptText = await resp.text();
                            const blob = new Blob([scriptText], {{ type: 'application/javascript' }});
                            const scriptUrl = URL.createObjectURL(blob);

                            await new Promise((resolve, reject) => {{
                                const s = document.createElement('script');
                                s.src = scriptUrl;
                                s.onload = resolve;
                                s.onerror = reject;
                                document.head.appendChild(s);
                            }});

                            const occt = await occtimportjs({{
                                locateFile: () => 'https://atec.libs/occt-import-js.wasm'
                            }});

                            const response = await fetch(modelUrl);
                            const buffer = new Uint8Array(await response.arrayBuffer());
                            const result = occt.ReadStepFile(buffer, null);

                            if (!result.meshes || result.meshes.length === 0) {{
                                info.textContent = '✗ Nessuna geometria trovata nel file STEP';
                                info.className = 'err';
                            }} else {{
                                const group = new THREE.Group();
                                for (const meshData of result.meshes) {{
                                    const geom = new THREE.BufferGeometry();
                                    geom.setAttribute('position', new THREE.Float32BufferAttribute(meshData.attributes.position.array, 3));
                                    if (meshData.attributes.normal)
                                        geom.setAttribute('normal', new THREE.Float32BufferAttribute(meshData.attributes.normal.array, 3));
                                    if (meshData.index)
                                        geom.setIndex(new THREE.BufferAttribute(new Uint32Array(meshData.index.array), 1));
                                    geom.computeVertexNormals();

                                    const mat = material.clone();
                                    if (meshData.color)
                                        mat.color.setRGB(meshData.color[0]/255, meshData.color[1]/255, meshData.color[2]/255);
                                    group.add(new THREE.Mesh(geom, mat));
                                }}
                                scene.add(group);
                                fitCamera(group);
                                info.textContent = '✓ {modelFileName} (' + result.meshes.length + ' mesh)';
                                info.className = 'ok';
                            }}
                        }} else if (modelUrl.endsWith('.stl')) {{
                            const loader = new STLLoader();
                            const response = await fetch(modelUrl);
                            const buffer = await response.arrayBuffer();
                            const geometry = loader.parse(buffer);
                            geometry.computeVertexNormals();
                            const mesh = new THREE.Mesh(geometry, material);
                            scene.add(mesh);
                            fitCamera(mesh);
                            info.textContent = '✓ {modelFileName}';
                            info.className = 'ok';
                        }} else {{
                            const loader = new OBJLoader();
                            const response = await fetch(modelUrl);
                            const text = await response.text();
                            const obj = loader.parse(text);
                            obj.traverse(c => {{ if (c.isMesh) c.material = material; }});
                            scene.add(obj);
                            fitCamera(obj);
                            info.textContent = '✓ {modelFileName}';
                            info.className = 'ok';
                        }}
                    }} catch(err) {{
                        info.textContent = '✗ Errore: ' + err.message;
                        info.className = 'err';
                        console.error(err);
                    }}

                    function animate() {{
                        requestAnimationFrame(animate);
                        controls.update();
                        renderer.render(scene, camera);
                    }}
                    animate();

                    window.addEventListener('resize', () => {{
                        camera.aspect = window.innerWidth/window.innerHeight;
                        camera.updateProjectionMatrix();
                        renderer.setSize(window.innerWidth, window.innerHeight);
                    }});
                    </script></body></html>";

                var tempHtmlPath = Path.Combine(tempDir, $"viewer3d_{projectId}.html");
                File.WriteAllText(tempHtmlPath, viewerHtml, System.Text.Encoding.UTF8);

                var panel = new DockPanel();
                var infoBar = new Border
                {
                    Background = Brush("#F7F8FA"),
                    BorderBrush = Brush("#E4E7EC"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                infoBar.Child = new TextBlock
                {
                    Text = $"🔧  {fileName} (Vista 3D — trascina per ruotare, scroll per zoom)",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(infoBar, Dock.Top);
                panel.Children.Add(infoBar);

                var webView = new Microsoft.Web.WebView2.Wpf.WebView2
                {
                    Height = Math.Max(500, SectionContent.ActualHeight - 60)
                };
                panel.Children.Add(webView);
                SectionContent.Content = panel;

                await webView.EnsureCoreWebView2Async();

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "atec.libs", viewerBase,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "atec.models", Path.Combine(tempDir, "models"),
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Navigate(new Uri(tempHtmlPath).AbsoluteUri);
            }
            // Info generico
            else
            {
                var infoPanel = new StackPanel();
                infoPanel.Children.Add(new TextBlock { Text = $"{GetFileIcon(fileName)}  {fileName}", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });

                var infoGrid = new Grid();
                infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                AddInfoRow(infoGrid, 0, "Percorso", relativePath);
                AddInfoRow(infoGrid, 1, "Tipo", ext.TrimStart('.').ToUpper());
                AddInfoRow(infoGrid, 2, "Azione", "File non visualizzabile in anteprima. Usa 'Scarica'.");

                infoPanel.Children.Add(infoGrid);
                SectionContent.Content = infoPanel;
            }
        }
        catch (Exception ex)
        {
            SectionContent.Content = new TextBlock { Text = $"Errore: {ex.Message}", Foreground = System.Windows.Media.Brushes.Red };
        }
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

    // === SEARCH ===
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        var filter = txtSearch.Text.Trim().ToLower();
        if (string.IsNullOrEmpty(filter))
            BuildTree(_allProjects);
        else
            BuildTree(_allProjects.Where(p =>
                p.Code.ToLower().Contains(filter) ||
                p.Title.ToLower().Contains(filter) ||
                p.CustomerName.ToLower().Contains(filter)
            ).ToList());
    }

    private async void BtnHardDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProjectId <= 0) return;
        string code = _selectedProjectCode;

        if (MessageBox.Show(
            $"Eliminare DEFINITIVAMENTE la commessa {code}?\n\n" +
            "Verranno cancellati:\n" +
            "• Tutti i dati (fasi, timesheet, DDP, costing, documenti)\n" +
            "• Le cartelle su disco\n" +
            "• Se derivata da offerta, l'offerta tornerà ACCETTATA\n\n" +
            "Questa operazione è IRREVERSIBILE.",
            "Conferma eliminazione definitiva",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (MessageBox.Show(
            $"ULTIMA CONFERMA: cancellare {code}?",
            "Sei sicuro?",
            MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes)
            return;

        try
        {
            string json = await ApiClient.DeleteAsync($"/api/projects/{_selectedProjectId}/hard");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                MessageBox.Show($"Commessa {code} eliminata.", "Fatto", MessageBoxButton.OK, MessageBoxImage.Information);
                _selectedProjectId = 0;
                _selectedProjectCode = "";
                btnHardDelete.Visibility = Visibility.Collapsed;
                SectionContent.Content = null;
                txtSectionTitle.Text = "Seleziona una commessa";
                await LoadTree();
            }
            else
            {
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore", "Errore");
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }
}
