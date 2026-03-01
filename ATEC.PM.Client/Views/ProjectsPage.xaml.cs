using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class ProjectsPage : Page
{
    private List<ProjectListItem> _allProjects = new();

    public ProjectsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadTree();
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

            var docNode = new TreeViewItem { Header = "Documenti", Tag = $"documents|{p.Id}" };
            projNode.Items.Add(docNode);

            treeProjects.Items.Add(projNode);
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
                case "docfolder":
                    var subPath = parts.Length > 2 ? parts[2] : "";
                    ShowDocuments(id, subPath);
                    break;
            }
        }
    }

    // === DETAILS ===
    private async void ShowDetails(int projectId)
    {
        txtSectionTitle.Text = "Dettagli Commessa";
        btnAction.Content = "Modifica";
        btnAction.Visibility = Visibility.Visible;
        btnAction.Tag = $"edit|{projectId}";

        try
        {
            var json = await ApiClient.GetAsync($"/api/projects/{projectId}");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;
            var d = doc.RootElement.GetProperty("data");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            var right = new StackPanel();

            AddField(left, "Codice", d.GetProperty("code").GetString());
            AddField(left, "Titolo", d.GetProperty("title").GetString());
            AddField(left, "Stato", d.GetProperty("status").GetString());
            AddField(left, "Priorità", d.GetProperty("priority").GetString());
            if (d.TryGetProperty("startDate", out var sd) && sd.ValueKind != JsonValueKind.Null)
                AddField(left, "Data Inizio", sd.GetDateTime().ToString("dd/MM/yyyy"));
            if (d.TryGetProperty("endDatePlanned", out var ed) && ed.ValueKind != JsonValueKind.Null)
                AddField(left, "Data Fine Prevista", ed.GetDateTime().ToString("dd/MM/yyyy"));

            AddField(right, "Ricavo", d.GetProperty("revenue").GetDecimal().ToString("N0") + " €");
            AddField(right, "Budget", d.GetProperty("budgetTotal").GetDecimal().ToString("N0") + " €");
            AddField(right, "Ore Previste", d.GetProperty("budgetHoursTotal").GetDecimal().ToString("N0"));
            var sp = d.GetProperty("serverPath").GetString() ?? "";
            AddField(right, "Path Server", sp == "" ? "(non creata)" : sp);
            if (d.TryGetProperty("notes", out var notes) && notes.ValueKind != JsonValueKind.Null)
                AddField(right, "Note", notes.GetString());

            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 2);
            grid.Children.Add(left);
            grid.Children.Add(right);
            SectionContent.Content = grid;
        }
        catch (Exception ex) { SectionContent.Content = new TextBlock { Text = $"Errore: {ex.Message}" }; }
    }

    // === PHASES ===
    private async void ShowPhases(int projectId)
    {
        txtSectionTitle.Text = "Fasi e Avanzamento";
        btnAction.Visibility = Visibility.Collapsed;

        try
        {
            var json = await ApiClient.GetAsync($"/api/projects/{projectId}/phases");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var phases = JsonSerializer.Deserialize<List<PhaseListItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            var panel = new StackPanel();
            var totalBudget = phases.Sum(p => p.BudgetHours);
            var totalWorked = phases.Sum(p => p.HoursWorked);
            panel.Children.Add(new TextBlock
            {
                Text = $"Ore previste: {totalBudget:N0} | Ore lavorate: {totalWorked:N1}",
                FontSize = 13,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 16)
            });

            foreach (var phase in phases)
            {
                var card = new Border
                {
                    Background = System.Windows.Media.Brushes.White,
                    BorderBrush = Brush("#E4E7EC"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var cg = new Grid();
                cg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cl = new StackPanel();
                cl.Children.Add(new TextBlock { Text = phase.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
                cl.Children.Add(new TextBlock { Text = $"Stato: {phase.Status} | Progresso: {phase.ProgressPct}%", FontSize = 12, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });

                var pgGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                pgGrid.Children.Add(new Border { Background = Brush("#F3F4F6"), Height = 6 });
                pgGrid.Children.Add(new Border { Background = Brush("#4F6EF7"), Height = 6, HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(0, Math.Min(300, 300.0 * phase.ProgressPct / 100.0)) });
                cl.Children.Add(pgGrid);

                var cr = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                cr.Children.Add(new TextBlock { Text = $"{phase.HoursWorked:N1} / {phase.BudgetHours:N0} h", FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });

                Grid.SetColumn(cl, 0);
                Grid.SetColumn(cr, 1);
                cg.Children.Add(cl);
                cg.Children.Add(cr);
                card.Child = cg;
                panel.Children.Add(card);
            }

            SectionContent.Content = panel;
        }
        catch (Exception ex) { SectionContent.Content = new TextBlock { Text = $"Errore: {ex.Message}" }; }
    }

    // === TIMESHEET ===
    private async void ShowTimesheet(int projectId)
    {
        txtSectionTitle.Text = "Timesheet Commessa";
        btnAction.Visibility = Visibility.Collapsed;

        try
        {
            var json = await ApiClient.GetAsync($"/api/projects/{projectId}/phases");
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

            // Populate document sub-nodes in tree
            if (string.IsNullOrEmpty(subPath))
                PopulateDocSubFolders(projectId, items.Where(i => i.IsFolder).ToList());

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

    private void PopulateDocSubFolders(int projectId, List<FileItem> folders)
    {
        // Find the Documenti node for this project
        foreach (TreeViewItem projNode in treeProjects.Items)
        {
            var projTag = projNode.Tag?.ToString() ?? "";
            if (projTag == $"project|{projectId}")
            {
                foreach (TreeViewItem child in projNode.Items)
                {
                    if (child.Tag?.ToString() == $"documents|{projectId}")
                    {
                        child.Items.Clear();
                        foreach (var f in folders)
                        {
                            child.Items.Add(new TreeViewItem
                            {
                                Header = f.Name,
                                Tag = $"docfolder|{projectId}|{f.RelativePath}",
                                FontSize = 12
                            });
                        }
                        break;
                    }
                }
                break;
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
            if (dlg.ShowDialog() == true) await LoadTree();
        }
        else if (parts[0] == "createfolder" && parts.Length > 1 && int.TryParse(parts[1], out var cfId))
        {
            try
            {
                var json = await ApiClient.PostAsync($"/api/projects/{cfId}/create-folder", "{}");
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                    ShowDocuments(cfId, "");
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
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProjectDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = LoadTree();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadTree();

    // === HELPERS ===
    private void AddField(StackPanel panel, string label, string? value)
    {
        panel.Children.Add(new TextBlock { Text = label.ToUpper(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 12, 0, 2) });
        panel.Children.Add(new TextBlock { Text = value ?? "-", FontSize = 14, Foreground = Brush("#1A1D26"), TextWrapping = TextWrapping.Wrap });
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:N0} KB";
        return $"{bytes / 1024.0 / 1024.0:N1} MB";
    }

    private void OpenFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Impossibile aprire: {ex.Message}"); }
    }
}