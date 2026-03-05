using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.UserControls;

public partial class PhasesManagementControl : UserControl
{
    private int _projectId;
    private List<PhaseListItem> _allPhases = new();
    private List<PhaseTemplateDto> _templates = new();
    private string _searchFilter = "";

    private static readonly Dictionary<string, string> DeptColors = new()
    {
        { "ELE", "#2563EB" }, { "MEC", "#059669" }, { "PLC", "#7C3AED" },
        { "ROB", "#DC2626" }, { "UTC", "#D97706" }, { "ACQ", "#0891B2" },
        { "AMM", "#BE185D" }, { "",    "#6B7280" }
    };

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public PhasesManagementControl()
    {
        InitializeComponent();
    }

    public async void Load(int projectId)
    {
        _projectId = projectId;
        await LoadTemplates();
        await LoadPhases();
    }

    // ═══════════════════════════════════════════════════════════════
    // DATA LOADING
    // ═══════════════════════════════════════════════════════════════

    private async Task LoadTemplates()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/phases/templates");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                _templates = JsonSerializer.Deserialize<List<PhaseTemplateDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { }
    }

    private async Task LoadPhases()
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/phases/project/{_projectId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _allPhases = JsonSerializer.Deserialize<List<PhaseListItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            UpdateSummary();
            RenderPhases();
        }
        catch (Exception ex)
        {
            pnlPhases.Children.Clear();
            pnlPhases.Children.Add(new TextBlock { Text = $"Errore: {ex.Message}", Foreground = Brushes.Red });
        }
    }

    private void UpdateSummary()
    {
        txtTotalPhases.Text = _allPhases.Count.ToString();
        txtTotalBudget.Text = $"{_allPhases.Sum(p => p.BudgetHours):N0} h";
        txtTotalWorked.Text = $"{_allPhases.Sum(p => p.HoursWorked):N1} h";
        txtTotalCost.Text   = $"{_allPhases.Sum(p => p.BudgetCost):N0} €";
    }

    // ═══════════════════════════════════════════════════════════════
    // RENDERING
    // ═══════════════════════════════════════════════════════════════

    private void RenderPhases()
    {
        pnlPhases.Children.Clear();

        IEnumerable<PhaseListItem> filtered = _allPhases;
        if (!string.IsNullOrWhiteSpace(_searchFilter))
            filtered = filtered.Where(p =>
                p.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                (p.DepartmentCode ?? "").Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));

        var groups = filtered
            .GroupBy(p => string.IsNullOrEmpty(p.DepartmentCode) ? "" : p.DepartmentCode)
            .OrderBy(g => g.Key == "" ? "ZZZ" : g.Key);

        foreach (var group in groups)
        {
            string deptCode  = group.Key;
            string deptColor = DeptColors.TryGetValue(deptCode, out string? col) ? col : "#6B7280";
            string deptLabel = string.IsNullOrEmpty(deptCode) ? "TRASVERSALE" : deptCode;

            // Header reparto
            Border groupHeader = new()
            {
                Background = Brush(deptColor),
                Padding    = new Thickness(12, 6, 12, 6),
                Margin     = new Thickness(0, 12, 0, 4)
            };
            decimal grpBudget = group.Sum(p => p.BudgetHours);
            decimal grpWorked = group.Sum(p => p.HoursWorked);
            groupHeader.Child = new TextBlock
            {
                Text       = $"  {deptLabel}  —  {group.Count()} fasi  |  {grpWorked:N1} / {grpBudget:N0} h",
                Foreground = Brushes.White,
                FontSize   = 12,
                FontWeight = FontWeights.SemiBold
            };
            pnlPhases.Children.Add(groupHeader);

            // Fasi del gruppo
            foreach (PhaseListItem phase in group.OrderBy(p => p.SortOrder))
                pnlPhases.Children.Add(BuildPhaseRow(phase, deptColor));
        }

        if (!filtered.Any())
            pnlPhases.Children.Add(new TextBlock
            {
                Text = "Nessuna fase trovata.",
                FontSize = 13, Foreground = Brushes.Gray,
                Margin = new Thickness(0, 20, 0, 0)
            });
    }

    private Border BuildPhaseRow(PhaseListItem phase, string accentColor)
    {
        Border card = new()
        {
            Background      = Brushes.White,
            BorderBrush     = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(0, 0, 0, 4)
        };

        StackPanel cardContent = new();

        // ── RIGA PRINCIPALE (sempre visibile) ──
        Grid row = new() { Margin = new Thickness(0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });           // accent
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // nome
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });          // ore budget
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });         // stato
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });          // avanzamento
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });          // ore lavorate
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });          // azioni

        // Accent bar
        Border accent = new() { Background = Brush(accentColor) };
        Grid.SetColumn(accent, 0);
        row.Children.Add(accent);

        // Nome + tecnici
        StackPanel namePanel = new() { Margin = new Thickness(12, 8, 8, 8), VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(new TextBlock
        {
            Text = phase.Name, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1A1D26")
        });
        if (phase.Assignments != null && phase.Assignments.Any())
        {
            string tecnici = string.Join(", ", phase.Assignments.Select(a => a.EmployeeName));
            namePanel.Children.Add(new TextBlock
            {
                Text = $"👷 {tecnici}", FontSize = 10, Foreground = Brushes.Gray,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0)
            });
        }
        Grid.SetColumn(namePanel, 1);
        row.Children.Add(namePanel);

        // Ore budget (editabile)
        TextBox txtBudget = new()
        {
            Text = phase.BudgetHours.ToString("F1"), FontSize = 12, Height = 26,
            Padding = new Thickness(4, 2, 4, 2), BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, Width = 70,
            Tag = phase
        };
        txtBudget.LostFocus += TxtBudgetHours_LostFocus;
        Grid.SetColumn(txtBudget, 2);
        row.Children.Add(txtBudget);

        // Stato (editabile)
        ComboBox cmbStatus = new()
        {
            FontSize = 11, Height = 26, Width = 90,
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = Brush("#E4E7EC"), Tag = phase
        };
        string[] statuses = { "NOT_STARTED", "IN_PROGRESS", "COMPLETED", "ON_HOLD" };
        foreach (string s in statuses)
            cmbStatus.Items.Add(new ComboBoxItem { Content = s, IsSelected = s == phase.Status });
        cmbStatus.SelectionChanged += CmbStatus_Changed;
        Grid.SetColumn(cmbStatus, 3);
        row.Children.Add(cmbStatus);

        // Avanzamento %
        TextBlock txtPct = new()
        {
            Text = $"{phase.ProgressPct}%", FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = phase.ProgressPct >= 100 ? Brush("#059669") : Brush("#1A1D26")
        };
        Grid.SetColumn(txtPct, 4);
        row.Children.Add(txtPct);

        // Ore lavorate
        TextBlock txtWorked = new()
        {
            Text = $"{phase.HoursWorked:N1} h", FontSize = 12, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("#4F6EF7")
        };
        Grid.SetColumn(txtWorked, 5);
        row.Children.Add(txtWorked);

        // Azioni: expand + elimina
        StackPanel actionsPanel = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        Button btnExpand = new()
        {
            Content = "▼", Width = 26, Height = 26, FontSize = 10,
            Background = Brush("#F3F4F6"), Foreground = Brush("#374151"),
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Dettagli"
        };

        Button btnDel = new()
        {
            Content = "✕", Width = 26, Height = 26, FontSize = 11, Margin = new Thickness(4, 0, 0, 0),
            Background = Brush("#EF44441A"), Foreground = Brush("#EF4444"),
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Elimina fase"
        };
        btnDel.Tag = phase;
        btnDel.Click += BtnDeletePhase_Click;

        actionsPanel.Children.Add(btnExpand);
        actionsPanel.Children.Add(btnDel);
        Grid.SetColumn(actionsPanel, 6);
        row.Children.Add(actionsPanel);

        cardContent.Children.Add(row);

        // ── EXPANDER CONTENT (nascosto di default) ──
        Border expanderContent = new()
        {
            Background      = Brush("#F9FAFB"),
            BorderBrush     = Brush("#E4E7EC"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding         = new Thickness(16, 10, 16, 10),
            Visibility      = Visibility.Collapsed
        };

        StackPanel expanderPanel = new();

        // Costo budget
        DockPanel costRow = new() { Margin = new Thickness(0, 0, 0, 8) };
        costRow.Children.Add(new TextBlock
        {
            Text = "COSTO BUDGET (€)", FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Width = 120
        });
        TextBox txtCost = new()
        {
            Text = phase.BudgetCost.ToString("F2"), FontSize = 12, Height = 26, Width = 100,
            Padding = new Thickness(4, 2, 4, 2), BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1), Tag = phase
        };
        txtCost.LostFocus += TxtBudgetCost_LostFocus;
        costRow.Children.Add(txtCost);
        expanderPanel.Children.Add(costRow);

        // Note
        DockPanel notesRow = new() { Margin = new Thickness(0, 0, 0, 8) };
        notesRow.Children.Add(new TextBlock
        {
            Text = "NOTE", FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Top, Width = 120
        });
        TextBox txtNotes = new()
        {
            Text = "", FontSize = 12, Height = 40, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(4, 2, 4, 2), BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1), Tag = phase,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        DockPanel.SetDock(txtNotes, Dock.Right);
        notesRow.Children.Add(txtNotes);
        expanderPanel.Children.Add(notesRow);

        // Tecnici assegnati
        expanderPanel.Children.Add(new TextBlock
        {
            Text = "TECNICI ASSEGNATI", FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 4)
        });

        // Riga aggiunta tecnico
        Grid addRow = new() { Margin = new Thickness(0, 0, 0, 6) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        ComboBox cmbEmp = new()
        {
            Height = 28, FontSize = 12, BorderBrush = Brush("#E4E7EC"),
            DisplayMemberPath = "Name", SelectedValuePath = "Id"
        };
        Grid.SetColumn(cmbEmp, 0);
        addRow.Children.Add(cmbEmp);

        TextBox txtPlannedH = new()
        {
            Text = "0", Height = 28, Width = 70, FontSize = 12,
            Padding = new Thickness(4, 2, 4, 2), BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1), ToolTip = "Ore pianificate"
        };
        Grid.SetColumn(txtPlannedH, 2);
        addRow.Children.Add(txtPlannedH);

        Button btnAddEmp = new()
        {
            Content = "+", Width = 28, Height = 28, FontSize = 14, FontWeight = FontWeights.Bold,
            Background = Brush("#4F6EF7"), Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand
        };
        Grid.SetColumn(btnAddEmp, 4);
        addRow.Children.Add(btnAddEmp);

        expanderPanel.Children.Add(addRow);

        // Lista tecnici assegnati
        StackPanel assignmentsList = new() { Margin = new Thickness(0, 4, 0, 0) };
        if (phase.Assignments != null)
        {
            foreach (PhaseAssignmentDto a in phase.Assignments)
            {
                DockPanel aRow = new() { Margin = new Thickness(0, 2, 0, 2) };
                aRow.Children.Add(new TextBlock
                {
                    Text = a.EmployeeName, FontSize = 12, Width = 200,
                    VerticalAlignment = VerticalAlignment.Center
                });
                aRow.Children.Add(new TextBlock
                {
                    Text = $"{a.PlannedHours:N1} h", FontSize = 12, Width = 70,
                    VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray
                });
                Button btnRemove = new()
                {
                    Content = "✕", Width = 22, Height = 22, FontSize = 10,
                    Background = Brush("#EF44441A"), Foreground = Brush("#EF4444"),
                    BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = new { PhaseId = phase.Id, AssignmentId = a.Id }
                };
                btnRemove.Click += BtnRemoveAssignment_Click;
                aRow.Children.Add(btnRemove);
                assignmentsList.Children.Add(aRow);
            }
        }
        expanderPanel.Children.Add(assignmentsList);

        // Wiring del bottone aggiungi tecnico
        btnAddEmp.Tag = new { Phase = phase, CmbEmp = cmbEmp, TxtHours = txtPlannedH, List = assignmentsList };
        btnAddEmp.Click += BtnAddAssignment_Click;

        // Carica tecnici per reparto quando expander si apre
        expanderContent.Child = expanderPanel;
        expanderContent.Tag = new { Phase = phase, CmbEmp = cmbEmp, Loaded = false };

        cardContent.Children.Add(expanderContent);

        // Toggle expander
        btnExpand.Tag = expanderContent;
        btnExpand.Click += (s, e) =>
        {
            if (expanderContent.Visibility == Visibility.Collapsed)
            {
                expanderContent.Visibility = Visibility.Visible;
                btnExpand.Content = "▲";
                // Lazy load tecnici
                var tagData = expanderContent.Tag as dynamic;
                if (tagData != null && !(bool)tagData.Loaded)
                {
                    _ = LoadEmployeesForPhase(phase, cmbEmp);
                    expanderContent.Tag = new { Phase = phase, CmbEmp = cmbEmp, Loaded = true };
                }
            }
            else
            {
                expanderContent.Visibility = Visibility.Collapsed;
                btnExpand.Content = "▼";
            }
        };

        card.Child = cardContent;
        return card;
    }

    // ═══════════════════════════════════════════════════════════════
    // EMPLOYEE LOADING (filtrato per reparto)
    // ═══════════════════════════════════════════════════════════════

    private async Task LoadEmployeesForPhase(PhaseListItem phase, ComboBox cmb)
    {
        try
        {
            int deptId = phase.DepartmentId ?? 0;
            string json = await ApiClient.GetAsync($"/api/employees/by-department?departmentId={deptId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var employees = JsonSerializer.Deserialize<List<LookupItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                cmb.ItemsSource = employees;
                if (employees.Count > 0) cmb.SelectedIndex = 0;
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    // INLINE EDIT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private async void TxtBudgetHours_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox txt || txt.Tag is not PhaseListItem phase) return;
        if (!decimal.TryParse(txt.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)) return;
        if (val == phase.BudgetHours) return;

        await SavePhaseField(phase.Id, "budget_hours", val);
        phase.BudgetHours = val;
        UpdateSummary();
    }

    private async void TxtBudgetCost_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox txt || txt.Tag is not PhaseListItem phase) return;
        if (!decimal.TryParse(txt.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)) return;
        if (val == phase.BudgetCost) return;

        await SavePhaseField(phase.Id, "budget_cost", val);
        phase.BudgetCost = val;
        UpdateSummary();
    }

    private async void CmbStatus_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cmb || cmb.Tag is not PhaseListItem phase) return;
        if (cmb.SelectedItem is not ComboBoxItem ci) return;
        string newStatus = ci.Content?.ToString() ?? "";
        if (newStatus == phase.Status) return;

        await SavePhaseField(phase.Id, "status", newStatus);
        phase.Status = newStatus;
    }

    private async Task SavePhaseField(int phaseId, string field, object value)
    {
        try
        {
            string jsonBody = JsonSerializer.Serialize(new { field, value },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PatchAsync($"/api/phases/{phaseId}/field", jsonBody);
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    // ASSIGNMENT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private async void BtnAddAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        dynamic tag = btn.Tag;
        PhaseListItem phase = tag.Phase;
        ComboBox cmbEmp = tag.CmbEmp;
        TextBox txtHours = tag.TxtHours;
        StackPanel list = tag.List;

        if (cmbEmp.SelectedItem is not LookupItem emp) return;
        if (phase.Assignments.Any(a => a.EmployeeId == emp.Id)) return;

        decimal.TryParse(txtHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal hours);

        try
        {
            string jsonBody = JsonSerializer.Serialize(new
            {
                phaseId = phase.Id,
                employeeId = emp.Id,
                assignRole = "MEMBER",
                plannedHours = hours
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync($"/api/phases/{phase.Id}/assignments", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                // Ricarica tutta la lista per aggiornare
                await LoadPhases();
            }
        }
        catch { }
    }

    private async void BtnRemoveAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        dynamic tag = btn.Tag;
        int assignmentId = tag.AssignmentId;

        try
        {
            string result = await ApiClient.DeleteAsync($"/api/phases/assignments/{assignmentId}");
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadPhases();
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    // DELETE PHASE
    // ═══════════════════════════════════════════════════════════════

    private async void BtnDeletePhase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PhaseListItem phase) return;

        if (MessageBox.Show($"Eliminare la fase \"{phase.Name}\"?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            string result = await ApiClient.DeleteAsync($"/api/phases/{phase.Id}");
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadPhases();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════════
    // ADD PHASE (fasi non ancora nella commessa)
    // ═══════════════════════════════════════════════════════════════

    private async void BtnAddPhase_Click(object sender, RoutedEventArgs e)
    {
        // Mostra dialog con i template non ancora inseriti
        var existingTemplateIds = _allPhases.Select(p => p.PhaseTemplateId).ToHashSet();
        var available = _templates.Where(t => !existingTemplateIds.Contains(t.Id)).ToList();

        if (!available.Any())
        {
            MessageBox.Show("Tutte le fasi sono già state inserite.", "Info");
            return;
        }

        var dlg = new AddPhasesWindow(available) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.SelectedTemplates.Any())
        {
            try
            {
                string jsonBody = JsonSerializer.Serialize(new
                {
                    projectId = _projectId,
                    templateIds = dlg.SelectedTemplates.Select(t => t.Id).ToList()
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                string result = await ApiClient.PostAsync("/api/phases/bulk", jsonBody);
                JsonDocument doc = JsonDocument.Parse(result);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                    await LoadPhases();
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SEARCH
    // ═══════════════════════════════════════════════════════════════

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        _searchFilter = txtSearch.Text.Trim();
        RenderPhases();
    }
}
