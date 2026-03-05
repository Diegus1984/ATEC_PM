using ATEC.PM.Client.UserControls;
namespace ATEC.PM.Client.Views;

public partial class ProjectsPage : Page
{
    private List<ProjectListItem> _allProjects = new();
    public ProjectsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadTree();
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
        else if (parts[0] == "addphase" && parts.Length > 1 && int.TryParse(parts[1], out var apId))
        {
            var dlg = new PhasesDialog(apId) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) ShowPhases(apId);
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
            projNode.Items.Add(new TreeViewItem { Header = "Fasi e Avanzamento", Tag = $"phases|{p.Id}" });
            projNode.Items.Add(new TreeViewItem { Header = "Timesheet", Tag = $"timesheet|{p.Id}" });
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
    private async void ShowDocuments(int projectId, string subPath)
    {
        txtSectionTitle.Text = string.IsNullOrEmpty(subPath) ? "Documenti" : subPath;
        btnAction.Content = "Apri Cartella";
        btnAction.Visibility = Visibility.Visible;
        btnAction.Tag = $"openfolder|{projectId}|{subPath}";

        try
        {
            // Check if server_path exists
            var projJson = await ApiClient.GetAsync($"/api/projects/{projectId}");
            var projDoc = JsonDocument.Parse(projJson);
            var serverPath = projDoc.RootElement.GetProperty("data").GetProperty("serverPath").GetString() ?? "";

            if (string.IsNullOrEmpty(serverPath))
            {
                btnAction.Content = "Crea Cartella Commessa";
                btnAction.Tag = $"createfolder|{projectId}";
                SectionContent.Content = new TextBlock
                {
                    Text = "La cartella per questa commessa non è ancora stata creata.\nClicca 'Crea Cartella Commessa' per generarla automaticamente.",
                    FontSize = 14,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap
                };
                return;
            }

            var encoded = Uri.EscapeDataString(subPath);
            var json = await ApiClient.GetAsync($"/api/projects/{projectId}/files?subPath={encoded}");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var items = JsonSerializer.Deserialize<List<FileItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Populate document sub-nodes in tree (now handled by file-tree)

            var dg = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Background = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("#E4E7EC"),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = Brush("#F3F4F6"),
                RowHeight = 36,
                ColumnHeaderHeight = 36,
                FontSize = 13
            };

            dg.Columns.Add(new DataGridTextColumn { Header = "Tipo", Binding = new System.Windows.Data.Binding("TypeIcon"), Width = 50 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Nome", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "Dimensione", Binding = new System.Windows.Data.Binding("SizeDisplay"), Width = 100 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Modificato", Binding = new System.Windows.Data.Binding("ModifiedDisplay"), Width = 140 });

            var displayItems = items.Select(i => new
            {
                TypeIcon = i.IsFolder ? "📁" : "📄",
                i.Name,
                SizeDisplay = i.IsFolder ? "" : FormatSize(i.Size),
                ModifiedDisplay = i.Modified?.ToString("dd/MM/yyyy HH:mm") ?? ""
            }).ToList();

            dg.ItemsSource = displayItems;
            dg.MouseDoubleClick += (s, ev) =>
            {
                if (dg.SelectedIndex >= 0 && dg.SelectedIndex < items.Count)
                {
                    var sel = items[dg.SelectedIndex];
                    if (sel.IsFolder)
                        ShowDocuments(projectId, sel.RelativePath);
                    else
                        OpenFile(Path.Combine(serverPath, sel.RelativePath));
                }
            };

            SectionContent.Content = dg;
        }
        catch (Exception ex) { SectionContent.Content = new TextBlock { Text = $"Errore: {ex.Message}" }; }
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
                    Height = Math.Max(500, scrollContent.ActualHeight - 60)
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

    // === PHASES ===
    private void ShowPhases(int projectId)
    {
        txtSectionTitle.Text = "Fasi e Avanzamento";
        btnAction.Visibility = Visibility.Collapsed;
        var ctrl = new PhasesManagementControl();
        SectionContent.Content = ctrl;
        ctrl.Load(projectId);
    }
    // === TIMESHEET ===
    private async void ShowTimesheet(int projectId)
    {
        txtSectionTitle.Text = "Timesheet Commessa";
        btnAction.Visibility = Visibility.Collapsed;

        try
        {
            var json = await ApiClient.GetAsync($"/api/phases/project/{projectId}");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var phases = JsonSerializer.Deserialize<List<PhaseListItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            var panel = new StackPanel();
            var totalH = phases.Sum(p => p.HoursWorked);
            panel.Children.Add(new TextBlock { Text = $"Totale ore registrate: {totalH:N1}", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });

            foreach (var p in phases.Where(p => p.HoursWorked > 0))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(new TextBlock { Text = p.Name, FontSize = 13, Width = 300 });
                row.Children.Add(new TextBlock { Text = $"{p.HoursWorked:N1} h", FontSize = 13, FontWeight = FontWeights.SemiBold });
                panel.Children.Add(row);
            }

            if (totalH == 0)
                panel.Children.Add(new TextBlock { Text = "Nessuna ora registrata su questa commessa.", FontSize = 13, Foreground = System.Windows.Media.Brushes.Gray });

            SectionContent.Content = panel;
        }
        catch (Exception ex) { SectionContent.Content = new TextBlock { Text = $"Errore: {ex.Message}" }; }
    }

    // === TREE SELECTION ===
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string tag)
        {
            var parts = tag.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var id)) return;

            switch (parts[0])
            {
                case "project":
                case "details":
                    ShowDetails(id);
                    break;
                case "phases":
                    ShowPhases(id);
                    break;
                case "timesheet":
                    ShowTimesheet(id);
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
}
