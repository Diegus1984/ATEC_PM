using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.UserControls;

public partial class PhaseRowControl : UserControl
{
    private PhaseListItem _phase = null!;
    private bool _employeesLoaded;
    private bool _initializing = true;

    /// <summary>Evento lanciato quando serve ricaricare la lista fasi dal server.</summary>
    public event Action? PhaseChanged;

    private static SolidColorBrush HexBrush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public PhaseRowControl()
    {
        InitializeComponent();
    }

    // ═══════════════════════════════════════════════════════════════
    // INIT
    // ═══════════════════════════════════════════════════════════════

    public void Bind(PhaseListItem phase, string accentColor)
    {
        _initializing = true;
        _phase = phase;

        // Accent
        brdAccent.Background = HexBrush(accentColor);

        // Nome
        txtName.Text = phase.Name;

        // Tecnici nella riga compatta
        if (phase.Assignments != null && phase.Assignments.Any())
        {
            txtTecnici.Text = "👷 " + string.Join(", ", phase.Assignments.Select(a => a.EmployeeName));
            txtTecnici.Visibility = Visibility.Visible;
        }

        // Ore budget
        txtBudgetHours.Text = phase.BudgetHours.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        txtBudgetHours.IsEnabled = App.CurrentUser.IsPm;

        // Stato
        foreach (ComboBoxItem item in cmbStatus.Items)
            if (item.Content?.ToString() == phase.Status)
            { cmbStatus.SelectedItem = item; break; }

        // Avanzamento
        decimal pct = phase.BudgetHours > 0
            ? Math.Round(phase.HoursWorked / phase.BudgetHours * 100, 0)
            : 0;
        txtProgress.Text = $"{pct}%";
        txtProgress.Foreground = pct > 100 ? HexBrush("#EF4444")
                               : pct >= 100 ? HexBrush("#059669")
                               : HexBrush("#1A1D26");

        // Ore lavorate
        txtWorked.Text = $"{phase.HoursWorked:N1} h";

        // Costo budget
        txtBudgetCost.Text = phase.BudgetCost.ToString("F2",System.Globalization.CultureInfo.InvariantCulture);
        txtBudgetCost.IsEnabled = App.CurrentUser.IsPm;

        // Riga aggiunta tecnico: solo PM/ADMIN
        gridAddTecnico.Visibility = App.CurrentUser.IsPm ? Visibility.Visible : Visibility.Collapsed;

        // Render assegnazioni nell'expander
        RenderAssignments();

        _initializing = false;
    }

    // ═══════════════════════════════════════════════════════════════
    // ASSIGNMENTS RENDERING
    // ═══════════════════════════════════════════════════════════════

    private void RenderAssignments()
    {
        pnlAssignments.Children.Clear();
        if (_phase.Assignments == null) return;

        // Header
        Grid header = new() { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        if (App.CurrentUser.IsPm)
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        AddHeaderCell(header, 0, "TECNICO");
        AddHeaderCell(header, 1, "ORE PREV.");
        AddHeaderCell(header, 2, "ORE LAV.");
        AddHeaderCell(header, 3, "%");
        pnlAssignments.Children.Add(header);

        foreach (PhaseAssignmentDto a in _phase.Assignments)
        {
            Grid row = new() { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            if (App.CurrentUser.IsPm)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            // Nome
            TextBlock txtEmpName = new()
            {
                Text = a.EmployeeName,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(txtEmpName, 0);
            row.Children.Add(txtEmpName);

            // Ore pianificate (TextBlock + TextBox nascosto per edit)
            StackPanel hoursPanel = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock txtPlanned = new()
            {
                Text = $"{a.PlannedHours:N1} h",
                FontSize = 12,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBox txtEditHours = new()
            {
                Text = a.PlannedHours.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 12,
                Width = 55,
                Height = 22,
                Padding = new Thickness(2, 1, 2, 1),
                BorderBrush = HexBrush("#E4E7EC"),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed,
                Tag = a
            };
            hoursPanel.Children.Add(txtPlanned);
            hoursPanel.Children.Add(txtEditHours);
            Grid.SetColumn(hoursPanel, 1);
            row.Children.Add(hoursPanel);

            // Ore lavorate
            TextBlock txtEmpWorked = new()
            {
                Text = $"{a.HoursWorked:N1} h",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Foreground = HexBrush("#4F6EF7")
            };
            Grid.SetColumn(txtEmpWorked, 2);
            row.Children.Add(txtEmpWorked);

            // %
            decimal empPct = a.PlannedHours > 0
                ? Math.Round(a.HoursWorked / a.PlannedHours * 100, 0)
                : 0;
            TextBlock txtEmpPct = new()
            {
                Text = $"{empPct}%",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = empPct > 100 ? HexBrush("#EF4444")
                           : empPct >= 100 ? HexBrush("#059669")
                           : HexBrush("#1A1D26")
            };
            Grid.SetColumn(txtEmpPct, 3);
            row.Children.Add(txtEmpPct);

            // Bottone rimuovi
            if (App.CurrentUser.IsPm)
            {
                Button btnRem = new()
                {
                    Content = "✕",
                    Width = 22,
                    Height = 22,
                    FontSize = 10,
                    Background = HexBrush("#EF44441A"),
                    Foreground = HexBrush("#EF4444"),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = a.Id
                };
                btnRem.Click += BtnRemoveAssignment_Click;
                Grid.SetColumn(btnRem, 4);
                row.Children.Add(btnRem);

                // Bottone modifica ore
                Button btnEdit = new()
                {
                    Content = "✏",
                    Width = 22,
                    Height = 22,
                    FontSize = 10,
                    Background = HexBrush("#4F6EF71A"),
                    Foreground = HexBrush("#4F6EF7"),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Modifica ore pianificate"
                };
                btnEdit.Click += (s, ev) =>
                {
                    if (txtEditHours.Visibility == Visibility.Collapsed)
                    {
                        txtPlanned.Visibility = Visibility.Collapsed;
                        txtEditHours.Visibility = Visibility.Visible;
                        txtEditHours.Focus();
                        txtEditHours.SelectAll();
                    }
                };
                txtEditHours.LostFocus += async (s, ev) =>
                {
                    txtEditHours.Visibility = Visibility.Collapsed;
                    txtPlanned.Visibility = Visibility.Visible;
                    if (!decimal.TryParse(txtEditHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newHours)) return;
                    if (newHours == a.PlannedHours) return;
                    await UpdateAssignmentHours(a.Id, newHours);
                };
                txtEditHours.KeyDown += (s, ev) =>
                {
                    if (ev.Key == System.Windows.Input.Key.Enter)
                    {
                        // Forza LostFocus
                        Keyboard.ClearFocus();
                    }
                };
                Grid.SetColumn(btnEdit, 5);
                row.Children.Add(btnEdit);
            }

            pnlAssignments.Children.Add(row);
        }
    }

    private async Task UpdateAssignmentHours(int assignmentId, decimal newHours)
    {
        try
        {
            string jsonBody = JsonSerializer.Serialize(new { plannedHours = newHours },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PatchAsync($"/api/phases/assignments/{assignmentId}/hours", jsonBody);
            // Aggiorna solo il dato locale e ri-renderizza le assegnazioni
            var assignment = _phase.Assignments.FirstOrDefault(a => a.Id == assignmentId);
            if (assignment != null) assignment.PlannedHours = newHours;
            RenderAssignments();
        }
        catch { }
    }

    private static void AddHeaderCell(Grid grid, int col, string text)
    {
        TextBlock tb = new()
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF")),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    // ═══════════════════════════════════════════════════════════════
    // EXPANDER
    // ═══════════════════════════════════════════════════════════════

    private void BtnExpand_Click(object sender, RoutedEventArgs e)
    {
        if (brdExpander.Visibility == Visibility.Collapsed)
        {
            brdExpander.Visibility = Visibility.Visible;
            btnExpand.Content = "▲";
            if (!_employeesLoaded)
            {
                _ = LoadEmployees();
                _employeesLoaded = true;
            }
        }
        else
        {
            brdExpander.Visibility = Visibility.Collapsed;
            btnExpand.Content = "▼";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // EMPLOYEES LOADING
    // ═══════════════════════════════════════════════════════════════

    private async Task LoadEmployees()
    {
        try
        {
            int deptId = _phase.DepartmentId ?? 0;
            string json = await ApiClient.GetAsync($"/api/employees/by-department?departmentId={deptId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            List<LookupItem> employees = JsonSerializer.Deserialize<List<LookupItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Filtra via i tecnici già assegnati
            HashSet<int> assignedIds = _phase.Assignments?.Select(a => a.EmployeeId).ToHashSet() ?? new();
            List<LookupItem> available = employees.Where(e => !assignedIds.Contains(e.Id)).ToList();

            cmbEmployee.ItemsSource = available;
            if (available.Count > 0) cmbEmployee.SelectedIndex = 0;
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    // INLINE SAVE HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private async void TxtBudgetHours_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        if (!decimal.TryParse(txtBudgetHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)) return;
        if (val == _phase.BudgetHours) return;

        _phase.BudgetHours = val;
        await SaveField("budget_hours", val.ToString(CultureInfo.InvariantCulture));
    }

    private async void TxtBudgetCost_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        if (!decimal.TryParse(txtBudgetCost.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)) return;
        if (val == _phase.BudgetCost) return;

        _phase.BudgetCost = val;
        await SaveField("budget_cost", val.ToString(CultureInfo.InvariantCulture));
    }

    private async void CmbStatus_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (cmbStatus.SelectedItem is not ComboBoxItem ci) return;
        string newStatus = ci.Content?.ToString() ?? "";
        if (newStatus == _phase.Status || string.IsNullOrEmpty(newStatus)) return;

        _phase.Status = newStatus;
        await SaveField("status", newStatus);
    }

    private async Task SaveField(string field, string value)
    {
        try
        {
            string jsonBody = JsonSerializer.Serialize(new { field, value });
            await ApiClient.PatchAsync($"/api/phases/{_phase.Id}/field", jsonBody);
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    // ASSIGNMENT ACTIONS
    // ═══════════════════════════════════════════════════════════════

    private async void BtnAddAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (cmbEmployee.SelectedItem is not LookupItem emp) return;

        // Doppio check: no duplicati
        if (_phase.Assignments.Any(a => a.EmployeeId == emp.Id)) return;

        decimal.TryParse(txtPlannedHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal hours);

        try
        {
            string jsonBody = JsonSerializer.Serialize(new
            {
                employeeId = emp.Id,
                assignRole = "MEMBER",
                plannedHours = hours
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync($"/api/phases/{_phase.Id}/assignments", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                int newId = doc.RootElement.GetProperty("data").GetInt32();
                _phase.Assignments.Add(new PhaseAssignmentDto
                {
                    Id = newId,
                    EmployeeId = emp.Id,
                    EmployeeName = emp.Name,
                    AssignRole = "MEMBER",
                    PlannedHours = hours,
                    HoursWorked = 0
                });
                RenderAssignments();
                // Aggiorna dropdown (rimuovi il tecnico aggiunto)
                _employeesLoaded = false;
                await LoadEmployees();
            }
        }
        catch { }
    }

    private async void BtnRemoveAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int assignmentId) return;

        try
        {
            string result = await ApiClient.DeleteAsync($"/api/phases/assignments/{assignmentId}");
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _phase.Assignments.RemoveAll(a => a.Id == assignmentId);
                RenderAssignments();
                _employeesLoaded = false;
                await LoadEmployees();
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    // DELETE PHASE
    // ═══════════════════════════════════════════════════════════════

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show($"Eliminare la fase \"{_phase.Name}\"?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            string result = await ApiClient.DeleteAsync($"/api/phases/{_phase.Id}");
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                PhaseChanged?.Invoke();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }
}
