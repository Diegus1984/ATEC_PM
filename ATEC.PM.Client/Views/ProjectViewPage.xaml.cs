using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class ProjectViewPage : Page
{
    private readonly int _projectId;
    private string _serverPath = "";
    private string _currentSection = "details";

    public ProjectViewPage(int projectId)
    {
        InitializeComponent();
        _projectId = projectId;
        Loaded += async (_, _) => await LoadProject();
    }

    private async Task LoadProject()
    {
        try
        {
            var json = await ApiClient.GetAsync($"/api/projects/{_projectId}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var d = doc.RootElement.GetProperty("data");
                txtCode.Text = d.GetProperty("code").GetString() ?? "";
                txtTitle.Text = d.GetProperty("title").GetString() ?? "";
                txtStatus.Text = d.GetProperty("status").GetString() ?? "";
                _serverPath = d.GetProperty("serverPath").GetString() ?? "";

                // Carica nome cliente
                var custId = d.GetProperty("customerId").GetInt32();
                var custJson = await ApiClient.GetAsync($"/api/customers/{custId}");
                var custDoc = JsonDocument.Parse(custJson);
                if (custDoc.RootElement.GetProperty("success").GetBoolean())
                    txtCustomer.Text = custDoc.RootElement.GetProperty("data").GetProperty("companyName").GetString() ?? "";

                // Popola sottocartelle nel TreeView documenti
                await LoadDocumentTree();

                // Mostra dettagli
                ShowDetails(d);
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async Task LoadDocumentTree()
    {
        // Trova il nodo Documenti nel TreeView
        TreeViewItem? docNode = null;
        foreach (TreeViewItem item in treeNav.Items)
        {
            if (item.Tag?.ToString() == "documents") { docNode = item; break; }
        }
        if (docNode == null) return;

        docNode.Items.Clear();

        if (string.IsNullOrEmpty(_serverPath))
        {
            docNode.Items.Add(new TreeViewItem { Header = "(cartella non creata)", Tag = "doc_nocreated", FontStyle = FontStyles.Italic, Foreground = System.Windows.Media.Brushes.Gray });
            return;
        }

        try
        {
            var json = await ApiClient.GetAsync($"/api/projects/{_projectId}/files?subPath=");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var items = JsonSerializer.Deserialize<List<FileItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                foreach (var f in items.Where(i => i.IsFolder))
                {
                    docNode.Items.Add(new TreeViewItem
                    {
                        Header = f.Name,
                        Tag = $"docfolder|{f.RelativePath}",
                        FontSize = 12
                    });
                }
            }
        }
        catch { }
    }

    private void TreeNav_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag != null)
        {
            var tag = item.Tag.ToString() ?? "";
            if (tag == "details") { _currentSection = "details"; ShowDetailsFromServer(); }
            else if (tag == "phases") { _currentSection = "phases"; ShowPhases(); }
            else if (tag == "timesheet") { _currentSection = "timesheet"; ShowTimesheet(); }
            else if (tag == "documents") { _currentSection = "documents"; ShowDocuments(""); }
            else if (tag == "doc_nocreated") { _currentSection = "doc_nocreated"; ShowCreateFolder(); }
            else if (tag.StartsWith("docfolder|")) { _currentSection = tag; ShowDocuments(tag.Substring(10)); }
        }
    }

    // === DETAILS ===
    private async void ShowDetailsFromServer()
    {
        var json = await ApiClient.GetAsync($"/api/projects/{_projectId}");
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetProperty("success").GetBoolean())
            ShowDetails(doc.RootElement.GetProperty("data"));
    }

    private void ShowDetails(JsonElement d)
    {
        txtSectionTitle.Text = "Dettagli Commessa";
        btnSectionAction.Content = "Modifica";
        btnSectionAction.Visibility = Visibility.Visible;
        btnSectionAction.Tag = "edit_project";

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftPanel = new StackPanel();
        var rightPanel = new StackPanel();

        AddField(leftPanel, "Codice", d.GetProperty("code").GetString());
        AddField(leftPanel, "Titolo", d.GetProperty("title").GetString());
        AddField(leftPanel, "Stato", d.GetProperty("status").GetString());
        AddField(leftPanel, "Priorità", d.GetProperty("priority").GetString());

        if (d.TryGetProperty("startDate", out var sd) && sd.ValueKind != JsonValueKind.Null)
            AddField(leftPanel, "Data Inizio", sd.GetDateTime().ToString("dd/MM/yyyy"));
        if (d.TryGetProperty("endDatePlanned", out var ed) && ed.ValueKind != JsonValueKind.Null)
            AddField(leftPanel, "Data Fine Prevista", ed.GetDateTime().ToString("dd/MM/yyyy"));

        AddField(rightPanel, "Ricavo", d.GetProperty("revenue").GetDecimal().ToString("N0") + " €");
        AddField(rightPanel, "Budget", d.GetProperty("budgetTotal").GetDecimal().ToString("N0") + " €");
        AddField(rightPanel, "Ore Previste", d.GetProperty("budgetHoursTotal").GetDecimal().ToString("N0"));
        AddField(rightPanel, "Path Server", _serverPath == "" ? "(non creata)" : _serverPath);

        if (d.TryGetProperty("notes", out var notes) && notes.ValueKind != JsonValueKind.Null)
            AddField(rightPanel, "Note", notes.GetString());

        Grid.SetColumn(leftPanel, 0);
        Grid.SetColumn(rightPanel, 2);
        grid.Children.Add(leftPanel);
        grid.Children.Add(rightPanel);

        SectionContent.Content = grid;
    }

    private void AddField(StackPanel panel, string label, string? value)
    {
        panel.Children.Add(new TextBlock { Text = label.ToUpper(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 12, 0, 2) });
        panel.Children.Add(new TextBlock { Text = value ?? "-", FontSize = 14, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1A1D26")), TextWrapping = TextWrapping.Wrap });
    }

    // === PHASES ===
    private async void ShowPhases()
    {
        txtSectionTitle.Text = "Fasi e Avanzamento";
        btnSectionAction.Visibility = Visibility.Collapsed;

        try
        {
            var json = await ApiClient.GetAsync($"/api/projects/{_projectId}/phases");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var phases = JsonSerializer.Deserialize<List<PhaseListItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                var panel = new StackPanel();
                var totalBudget = phases.Sum(p => p.BudgetHours);
                var totalWorked = phases.Sum(p => p.HoursWorked);

                panel.Children.Add(new TextBlock { Text = $"Ore previste: {totalBudget:N0} | Ore lavorate: {totalWorked:N1}", FontSize = 13, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 16) });

                foreach (var phase in phases)
                {
                    var card = new Border
                    {
                        Background = System.Windows.Media.Brushes.White,
                        BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E4E7EC")),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 0, 0, 8)
                    };

                    var cardGrid = new Grid();
                    cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var left = new StackPanel();
                    left.Children.Add(new TextBlock { Text = phase.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
                    left.Children.Add(new TextBlock { Text = $"Stato: {phase.Status} | Progresso: {phase.ProgressPct}%", FontSize = 12, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });

                    // Progress bar
                    var progressBg = new Border { Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F3F4F6")), Height = 6, Margin = new Thickness(0, 6, 0, 0) };
                    var progressGrid = new Grid();
                    progressGrid.Children.Add(progressBg);
                    var progressBar = new Border
                    {
                        Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4F6EF7")),
                        Height = 6,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Width = Math.Max(0, Math.Min(300, 300.0 * phase.ProgressPct / 100.0))
                    };
                    progressGrid.Children.Add(progressBar);
                    left.Children.Add(progressGrid);

                    var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    right.Children.Add(new TextBlock { Text = $"{phase.HoursWorked:N1} / {phase.BudgetHours:N0} h", FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });

                    Grid.SetColumn(left, 0);
                    Grid.SetColumn(right, 1);
                    cardGrid.Children.Add(left);
                    cardGrid.Children.Add(right);

                    card.Child = cardGrid;
                    panel.Children.Add(card);
                }

                var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                SectionContent.Content = scroll;
            }
        }
        catch (Exception ex) { SectionContent.Content = new TextBlock { Text = $"Errore: {ex.Message}" }; }
    }

    // === TIMESHEET ===
    private async void ShowTimesheet()
    {
        txtSectionTitle.Text = "Timesheet Commessa";
        btnSectionAction.Visibility = Visibility.Collapsed;

        try
        {
            var json = await ApiClient.GetAsync($"/api/projects/{_projectId}/phases");
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

            SectionContent.Content = new ScrollViewer { Content = panel };
        }
        catch (Exception ex) { SectionContent.Content = new TextBlock { Text = $"Errore: {ex.Message}" }; }
    }

    // === DOCUMENTS ===
    private async void ShowDocuments(string subPath)
    {
        txtSectionTitle.Text = string.IsNullOrEmpty(subPath) ? "Documenti" : subPath;
        btnSectionAction.Content = "Apri Cartella";
        btnSectionAction.Visibility = Visibility.Visible;
        btnSectionAction.Tag = $"open_folder|{subPath}";

        if (string.IsNullOrEmpty(_serverPath))
        {
            ShowCreateFolder();
            return;
        }

        try
        {
            var encoded = Uri.EscapeDataString(subPath);
            var json = await ApiClient.GetAsync($"/api/projects/{_projectId}/files?subPath={encoded}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var items = JsonSerializer.Deserialize<List<FileItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                var dg = new DataGrid
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = true,
                    Background = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E4E7EC")),
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    RowHeight = 36,
                    ColumnHeaderHeight = 36,
                    FontSize = 13
                };

                dg.Columns.Add(new DataGridTextColumn { Header = "Tipo", Binding = new System.Windows.Data.Binding("TypeIcon"), Width = 40 });
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
                dg.MouseDoubleClick += (s, e) =>
                {
                    if (dg.SelectedIndex >= 0 && dg.SelectedIndex < items.Count)
                    {
                        var selected = items[dg.SelectedIndex];
                        if (selected.IsFolder)
                            ShowDocuments(selected.RelativePath);
                        else
                            OpenFile(Path.Combine(_serverPath, selected.RelativePath));
                    }
                };

                SectionContent.Content = dg;
            }
        }
        catch (Exception ex) { SectionContent.Content = new TextBlock { Text = $"Errore: {ex.Message}" }; }
    }

    private void ShowCreateFolder()
    {
        txtSectionTitle.Text = "Documenti";
        btnSectionAction.Content = "Crea Cartella Commessa";
        btnSectionAction.Visibility = Visibility.Visible;
        btnSectionAction.Tag = "create_folder";

        SectionContent.Content = new TextBlock
        {
            Text = "La cartella per questa commessa non è ancora stata creata.\nClicca 'Crea Cartella Commessa' per generarla automaticamente.",
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 20, 0, 0)
        };
    }

    // === ACTIONS ===
    private async void BtnSectionAction_Click(object sender, RoutedEventArgs e)
    {
        var tag = btnSectionAction.Tag?.ToString() ?? "";

        if (tag == "edit_project")
        {
            var dlg = new ProjectDialog(_projectId) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) await LoadProject();
        }
        else if (tag == "create_folder")
        {
            try
            {
                var json = await ApiClient.PostAsync($"/api/projects/{_projectId}/create-folder", "{}");
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                {
                    _serverPath = doc.RootElement.GetProperty("data").GetString() ?? "";
                    await LoadDocumentTree();
                    ShowDocuments("");
                }
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        }
        else if (tag.StartsWith("open_folder|"))
        {
            var sub = tag.Substring(12);
            var fullPath = string.IsNullOrEmpty(sub) ? _serverPath : Path.Combine(_serverPath, sub);
            if (Directory.Exists(fullPath))
                System.Diagnostics.Process.Start("explorer.exe", fullPath);
            else
                MessageBox.Show("Cartella non trovata.");
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true)
            NavigationService.GoBack();
    }

    private void OpenFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Impossibile aprire: {ex.Message}"); }
    }

    private string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:N0} KB";
        return $"{bytes / 1024.0 / 1024.0:N1} MB";
    }
}
