using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class ProjectViewPage : Page
{
    private readonly int _projectId;
    private string _serverPath = "";
    private string _currentSection = "details";

    // Colori reparto
    private static readonly Dictionary<string, string> DeptColors = new()
    {
        { "ELE", "#2563EB" }, { "MEC", "#059669" }, { "PLC", "#7C3AED" },
        { "ROB", "#DC2626" }, { "UTC", "#D97706" }, { "ACQ", "#0891B2" },
        { "AMM", "#BE185D" }, { "",    "#6B7280" }
    };

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
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                JsonElement d = doc.RootElement.GetProperty("data");
                txtCode.Text = d.GetProperty("code").GetString() ?? "";
                txtTitle.Text = d.GetProperty("title").GetString() ?? "";
                txtStatus.Text = d.GetProperty("status").GetString() ?? "";
                _serverPath = d.GetProperty("serverPath").GetString() ?? "";

                int custId = d.GetProperty("customerId").GetInt32();
                string custJson = await ApiClient.GetAsync($"/api/customers/{custId}");
                JsonDocument custDoc = JsonDocument.Parse(custJson);
                if (custDoc.RootElement.GetProperty("success").GetBoolean())
                    txtCustomer.Text = custDoc.RootElement.GetProperty("data").GetProperty("companyName").GetString() ?? "";

                await LoadDocumentTree();
                ShowDetails(d);
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async Task LoadDocumentTree()
    {
        TreeViewItem? docNode = null;
        foreach (TreeViewItem item in treeNav.Items)
            if (item.Tag?.ToString() == "documents") { docNode = item; break; }
        if (docNode == null) return;
        docNode.Items.Clear();

        if (string.IsNullOrEmpty(_serverPath))
        {
            docNode.Items.Add(new TreeViewItem { Header = "(cartella non creata)", Tag = "doc_nocreated", FontStyle = FontStyles.Italic, Foreground = Brushes.Gray });
            return;
        }

        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/files?subPath=");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                List<FileItem> items = JsonSerializer.Deserialize<List<FileItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                foreach (FileItem f in items.Where(i => i.IsFolder))
                    docNode.Items.Add(new TreeViewItem { Header = f.Name, Tag = $"docfolder|{f.RelativePath}", FontSize = 12 });
            }
        }
        catch { }
    }

    private void TreeNav_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag != null)
        {
            string tag = item.Tag.ToString() ?? "";
            if (tag == "details") { _currentSection = "details"; ShowDetailsFromServer(); }
            else if (tag == "phases") { _currentSection = "phases"; _ = ShowPhases(); }
            else if (tag == "documents") { _currentSection = "documents"; ShowDocuments(""); }
            else if (tag == "doc_nocreated") { _currentSection = tag; ShowCreateFolder(); }
            else if (tag.StartsWith("docfolder|")) { _currentSection = tag; ShowDocuments(tag[10..]); }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DETAILS
    // ═══════════════════════════════════════════════════════════════
    private async void ShowDetailsFromServer()
    {
        string json = await ApiClient.GetAsync($"/api/projects/{_projectId}");
        JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetProperty("success").GetBoolean())
            ShowDetails(doc.RootElement.GetProperty("data"));
    }

    private void ShowDetails(JsonElement d)
    {
        txtSectionTitle.Text = "Dettagli Commessa";
        btnSectionAction.Content = "Modifica";
        btnSectionAction.Visibility = Visibility.Visible;
        btnSectionAction.Tag = "edit_project";

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        StackPanel left = new(); StackPanel right = new();

        AddField(left, "Codice", d.GetProperty("code").GetString());
        AddField(left, "Titolo", d.GetProperty("title").GetString());
        AddField(left, "Stato", d.GetProperty("status").GetString());
        AddField(left, "Priorità", d.GetProperty("priority").GetString());
        if (d.TryGetProperty("startDate", out JsonElement sd) && sd.ValueKind != JsonValueKind.Null)
            AddField(left, "Data Inizio", sd.GetDateTime().ToString("dd/MM/yyyy"));
        if (d.TryGetProperty("endDatePlanned", out JsonElement ed) && ed.ValueKind != JsonValueKind.Null)
            AddField(left, "Data Fine Prevista", ed.GetDateTime().ToString("dd/MM/yyyy"));

        // Dati economici — solo PM/ADMIN
        if (App.CurrentUser.IsPm)
        {
            AddField(right, "Ricavo", d.GetProperty("revenue").GetDecimal().ToString("N0") + " €");
            AddField(right, "Budget", d.GetProperty("budgetTotal").GetDecimal().ToString("N0") + " €");
        }
        AddField(right, "Ore Previste", d.GetProperty("budgetHoursTotal").GetDecimal().ToString("N0"));
        AddField(right, "Path Server", _serverPath == "" ? "(non creata)" : _serverPath);
        if (d.TryGetProperty("notes", out JsonElement notes) && notes.ValueKind != JsonValueKind.Null)
            AddField(right, "Note", notes.GetString());

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        SectionContent.Content = grid;
    }

    private void AddField(StackPanel panel, string label, string? value)
    {
        panel.Children.Add(new TextBlock { Text = label.ToUpper(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 2) });
        panel.Children.Add(new TextBlock { Text = value ?? "-", FontSize = 14, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1D26")), TextWrapping = TextWrapping.Wrap });
    }

    // ═══════════════════════════════════════════════════════════════
    // PHASES — raggruppate per reparto
    // ═══════════════════════════════════════════════════════════════
    private async Task ShowPhases()
    {
        txtSectionTitle.Text = "Fasi e Avanzamento";
        btnSectionAction.Content = "+ Aggiungi Fase";
        btnSectionAction.Visibility = Visibility.Visible;
        btnSectionAction.Tag = "add_phase";

        try
        {
            string json = await ApiClient.GetAsync($"/api/phases/project/{_projectId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            List<PhaseListItem> phases = JsonSerializer.Deserialize<List<PhaseListItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            StackPanel root = new() { Margin = new Thickness(0, 0, 0, 16) };

            // Riepilogo ore totali
            decimal totalBudget = phases.Sum(p => p.BudgetHours);
            decimal totalWorked = phases.Sum(p => p.HoursWorked);
            Border summary = MakeSummaryBar(totalBudget, totalWorked, phases.Count);
            root.Children.Add(summary);

            // Raggruppa per reparto
            IEnumerable<IGrouping<string, PhaseListItem>> groups = phases
                .GroupBy(p => string.IsNullOrEmpty(p.DepartmentCode) ? "" : p.DepartmentCode)
                .OrderBy(g => g.Key == "" ? "ZZZ" : g.Key);

            foreach (IGrouping<string, PhaseListItem> group in groups)
            {
                string deptCode = group.Key;
                string deptColor = DeptColors.TryGetValue(deptCode, out string? col) ? col : "#6B7280";
                string deptLabel = string.IsNullOrEmpty(deptCode) ? "TRASVERSALE" : deptCode;

                // Header gruppo
                Border groupHeader = new()
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(deptColor)),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 16, 0, 4)
                };
                groupHeader.Child = new TextBlock
                {
                    Text = $"  {deptLabel}  —  {group.Count()} fasi  |  {group.Sum(p => p.HoursWorked):N1} / {group.Sum(p => p.BudgetHours):N0} h",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                };
                root.Children.Add(groupHeader);

                // Card per ogni fase
                foreach (PhaseListItem phase in group.OrderBy(p => p.SortOrder))
                    root.Children.Add(MakePhaseCard(phase, deptColor));
            }

            if (!phases.Any())
                root.Children.Add(new TextBlock { Text = "Nessuna fase aggiunta. Clicca '+ Aggiungi Fase'.", FontSize = 13, Foreground = Brushes.Gray, Margin = new Thickness(0, 20, 0, 0) });

            SectionContent.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        catch (Exception ex) { SectionContent.Content = new TextBlock { Text = $"Errore: {ex.Message}" }; }
    }

    private Border MakeSummaryBar(decimal totalBudget, decimal totalWorked, int phaseCount)
    {
        Border bar = new()
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F9FAFB")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4E7EC")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 4)
        };

        Grid g = new();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        decimal pct = totalBudget > 0 ? Math.Min(100, totalWorked / totalBudget * 100) : 0;

        AddSummaryCell(g, 0, "FASI TOTALI", phaseCount.ToString());
        AddSummaryCell(g, 1, "ORE LAVORATE", $"{totalWorked:N1} h");
        AddSummaryCell(g, 2, "BUDGET ORE", $"{totalBudget:N0} h  ({pct:N0}%)");

        bar.Child = g;
        return bar;
    }

    private static void AddSummaryCell(Grid g, int col, string label, string value)
    {
        StackPanel sp = new() { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1D26")), HorizontalAlignment = HorizontalAlignment.Center });
        Grid.SetColumn(sp, col);
        g.Children.Add(sp);
    }

    private Border MakePhaseCard(PhaseListItem phase, string accentColor)
    {
        Border card = new()
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4E7EC")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 6)
        };

        // Striscia colorata sinistra
        Grid outerGrid = new();
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border accent = new() { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentColor)) };
        Grid.SetColumn(accent, 0);

        // Contenuto card
        Grid content = new() { Margin = new Thickness(12, 10, 12, 10) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(content, 1);

        // Sinistra: nome + stato + tecnici + progress bar
        StackPanel left = new();

        // Riga nome + badge stato
        DockPanel nameRow = new();
        nameRow.Children.Add(new TextBlock { Text = phase.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1D26")) });
        Border statusBadge = new()
        {
            Background = GetStatusBrush(phase.Status),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        statusBadge.Child = new TextBlock { Text = phase.Status, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
        DockPanel.SetDock(statusBadge, Dock.Right);
        nameRow.Children.Add(statusBadge);
        left.Children.Add(nameRow);

        // Tecnici assegnati
        if (phase.Assignments.Any())
        {
            string tecnici = string.Join(", ", phase.Assignments.Select(a => a.EmployeeName));
            left.Children.Add(new TextBlock
            {
                Text = $"👷 {tecnici}",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        // Progress bar
        Grid progressGrid = new() { Margin = new Thickness(0, 6, 0, 0) };
        progressGrid.Children.Add(new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6")),
            Height = 5
        });
        double pctWidth = phase.BudgetHours > 0
            ? Math.Min(1.0, (double)phase.HoursWorked / (double)phase.BudgetHours)
            : (phase.ProgressPct / 100.0);
        progressGrid.Children.Add(new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentColor)),
            Height = 5,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Double.NaN // Gestita con binding in una vera app; qui usiamo il tag
        });
        // Nota: la progress bar visiva richiede un layout pass — usiamo testo %
        left.Children.Add(new TextBlock
        {
            Text = $"Avanzamento: {phase.ProgressPct}%  |  {phase.HoursWorked:N1} / {phase.BudgetHours:N0} h lavorate",
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetColumn(left, 0);

        // Destra: bottoni azione
        StackPanel right = new() { VerticalAlignment = VerticalAlignment.Center, Orientation = Orientation.Horizontal };

        Button btnEdit = MakeIconButton("✏", "#4F6EF7");
        btnEdit.ToolTip = "Modifica fase";
        btnEdit.Tag = phase;
        btnEdit.Click += async (s, e) =>
        {
            PhasesDialog dlg = new(_projectId, phase) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) await ShowPhases();
        };

        Button btnDel = MakeIconButton("✕", "#EF4444");
        btnDel.ToolTip = "Elimina fase";
        btnDel.Tag = phase;
        btnDel.Click += async (s, e) =>
        {
            if (MessageBox.Show($"Eliminare la fase \"{phase.Name}\"?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                string res = await ApiClient.DeleteAsync($"/api/phases/{phase.Id}");
                JsonDocument doc = JsonDocument.Parse(res);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                    await ShowPhases();
                else
                    MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        };

        right.Children.Add(btnEdit);
        right.Children.Add(btnDel);
        Grid.SetColumn(right, 1);

        content.Children.Add(left);
        content.Children.Add(right);
        outerGrid.Children.Add(accent);
        outerGrid.Children.Add(content);
        card.Child = outerGrid;
        return card;
    }

    private static Button MakeIconButton(string icon, string color)
    {
        return new Button
        {
            Content = icon,
            Width = 30,
            Height = 30,
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color + "1A")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }

    private static Brush GetStatusBrush(string status) => status switch
    {
        "IN_PROGRESS" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
        "COMPLETED" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669")),
        "ON_HOLD" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"))
    };

    // ═══════════════════════════════════════════════════════════════
    // TIMESHEET
    // ═══════════════════════════════════════════════════════════════
    private async void ShowTimesheet()
    {
        txtSectionTitle.Text = "Timesheet Commessa";
        btnSectionAction.Visibility = Visibility.Collapsed;

        try
        {
            string json = await ApiClient.GetAsync($"/api/phases/project/{_projectId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            List<PhaseListItem> phases = JsonSerializer.Deserialize<List<PhaseListItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            StackPanel panel = new();
            decimal totalH = phases.Sum(p => p.HoursWorked);
            panel.Children.Add(new TextBlock { Text = $"Totale ore registrate: {totalH:N1}", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });

            foreach (PhaseListItem p in phases.Where(p => p.HoursWorked > 0).OrderByDescending(p => p.HoursWorked))
            {
                DockPanel row = new() { Margin = new Thickness(0, 0, 0, 6) };
                string deptColor = DeptColors.TryGetValue(p.DepartmentCode ?? "", out string? c) ? c : "#6B7280";
                Border dot = new() { Width = 8, Height = 8, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(deptColor)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = p.Name, FontSize = 13, Width = 280 });
                row.Children.Add(new TextBlock { Text = $"{p.HoursWorked:N1} h", FontSize = 13, FontWeight = FontWeights.SemiBold });
                panel.Children.Add(row);
            }

            if (totalH == 0)
                panel.Children.Add(new TextBlock { Text = "Nessuna ora registrata su questa commessa.", FontSize = 13, Foreground = Brushes.Gray });

            SectionContent.Content = new ScrollViewer { Content = panel };
        }
        catch (Exception ex) { SectionContent.Content = new TextBlock { Text = $"Errore: {ex.Message}" }; }
    }

    // ═══════════════════════════════════════════════════════════════
    // DOCUMENTS
    // ═══════════════════════════════════════════════════════════════
    private async void ShowDocuments(string subPath)
    {
        txtSectionTitle.Text = string.IsNullOrEmpty(subPath) ? "Documenti" : subPath;
        btnSectionAction.Content = "Apri Cartella";
        btnSectionAction.Visibility = Visibility.Visible;
        btnSectionAction.Tag = $"open_folder|{subPath}";

        if (string.IsNullOrEmpty(_serverPath)) { ShowCreateFolder(); return; }

        try
        {
            string encoded = Uri.EscapeDataString(subPath);
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/files?subPath={encoded}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                List<FileItem> items = JsonSerializer.Deserialize<List<FileItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                DataGrid dg = new()
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = true,
                    Background = Brushes.White,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4E7EC")),
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    RowHeight = 36,
                    ColumnHeaderHeight = 36,
                    FontSize = 13
                };
                dg.Columns.Add(new DataGridTextColumn { Header = "Tipo", Binding = new System.Windows.Data.Binding("TypeIcon"), Width = 40 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Nome", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dg.Columns.Add(new DataGridTextColumn { Header = "Dimensione", Binding = new System.Windows.Data.Binding("SizeDisplay"), Width = 100 });
                dg.Columns.Add(new DataGridTextColumn { Header = "Modificato", Binding = new System.Windows.Data.Binding("ModifiedDisplay"), Width = 140 });

                dg.ItemsSource = items.Select(i => new
                {
                    TypeIcon = i.IsFolder ? "📁" : "📄",
                    i.Name,
                    SizeDisplay = i.IsFolder ? "" : FormatSize(i.Size),
                    ModifiedDisplay = i.Modified?.ToString("dd/MM/yyyy HH:mm") ?? ""
                }).ToList();

                dg.MouseDoubleClick += (s, e) =>
                {
                    if (dg.SelectedIndex >= 0 && dg.SelectedIndex < items.Count)
                    {
                        FileItem sel = items[dg.SelectedIndex];
                        if (sel.IsFolder) ShowDocuments(sel.RelativePath);
                        else OpenFile(Path.Combine(_serverPath, sel.RelativePath));
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
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 20, 0, 0)
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // ACTIONS
    // ═══════════════════════════════════════════════════════════════
    private async void BtnSectionAction_Click(object sender, RoutedEventArgs e)
    {
        string tag = btnSectionAction.Tag?.ToString() ?? "";

        if (tag == "edit_project")
        {
            ProjectDialog dlg = new(_projectId) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) await LoadProject();
        }
        else if (tag == "add_phase")
        {
            PhasesDialog dlg = new(_projectId) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) await ShowPhases();
        }
        else if (tag == "create_folder")
        {
            try
            {
                string json = await ApiClient.PostAsync($"/api/projects/{_projectId}/create-folder", "{}");
                JsonDocument doc = JsonDocument.Parse(json);
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
            string sub = tag[12..];
            string fullPath = string.IsNullOrEmpty(sub) ? _serverPath : Path.Combine(_serverPath, sub);
            if (Directory.Exists(fullPath))
                System.Diagnostics.Process.Start("explorer.exe", fullPath);
            else
                MessageBox.Show("Cartella non trovata.");
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
    }

    private void OpenFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Impossibile aprire: {ex.Message}"); }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:N0} KB";
        return $"{bytes / 1024.0 / 1024.0:N1} MB";
    }
}
