using System.Globalization;
using System.Windows.Media;

namespace ATEC.PM.Client.UserControls;

public partial class ProjectDashboardControl : UserControl
{
    private int _projectId;

    private static readonly Dictionary<string, string> DeptColors = new()
    {
        { "ELE", "#2563EB" }, { "MEC", "#059669" }, { "PLC", "#7C3AED" },
        { "ROB", "#DC2626" }, { "UTC", "#D97706" }, { "ACQ", "#0891B2" },
        { "AMM", "#BE185D" }, { "TRASV", "#6B7280" }, { "", "#6B7280" }
    };

    private static SolidColorBrush B(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public ProjectDashboardControl()
    {
        InitializeComponent();
    }

    public async void Load(int projectId)
    {
        _projectId = projectId;
        await LoadDashboard();
    }

    private async Task LoadDashboard()
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/dashboard");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            ProjectDashboardData data = JsonSerializer.Deserialize<ProjectDashboardData>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            Render(data);
        }
        catch (Exception ex)
        {
            pnlContent.Children.Clear();
            pnlContent.Children.Add(new TextBlock { Text = $"Errore: {ex.Message}", Foreground = Brushes.Red, Margin = new Thickness(16) });
        }
    }

    private void Render(ProjectDashboardData d)
    {
        pnlContent.Children.Clear();
        bool isPm = App.CurrentUser.IsPm;

        // ═══ HEADER COMMESSA ═══
        Border header = new()
        {
            Background = B("#1A1D26"), Padding = new Thickness(24, 16, 24, 16),
            Margin = new Thickness(0, 0, 0, 16)
        };
        StackPanel headerContent = new();

        // Riga 1: codice + stato
        DockPanel row1 = new();
        row1.Children.Add(new TextBlock { Text = d.Code, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = B("#4F6EF7") });

        Border statusBadge = new()
        {
            Background = GetStatusBackground(d.Status),
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        statusBadge.Child = new TextBlock { Text = d.Status, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
        row1.Children.Add(statusBadge);

        // Priorità badge
        Border prioBadge = new()
        {
            Background = GetPriorityBackground(d.Priority),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        prioBadge.Child = new TextBlock { Text = d.Priority, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
        row1.Children.Add(prioBadge);

        headerContent.Children.Add(row1);

        // Riga 2: titolo
        headerContent.Children.Add(new TextBlock { Text = d.Title, FontSize = 15, Foreground = B("#E5E7EB"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });

        // Riga 3: cliente + PM + date
        WrapPanel infoRow = new() { Margin = new Thickness(0, 8, 0, 0) };
        AddInfoChip(infoRow, "🏢", d.CustomerName);
        if (!string.IsNullOrEmpty(d.PmName)) AddInfoChip(infoRow, "👤", d.PmName);
        if (d.StartDate.HasValue) AddInfoChip(infoRow, "📅", $"Inizio: {d.StartDate.Value:dd/MM/yyyy}");
        if (d.EndDatePlanned.HasValue) AddInfoChip(infoRow, "🏁", $"Fine prev.: {d.EndDatePlanned.Value:dd/MM/yyyy}");
        headerContent.Children.Add(infoRow);

        header.Child = headerContent;
        pnlContent.Children.Add(header);

        // ═══ KPI CARDS ═══
        Grid kpiGrid = new() { Margin = new Thickness(0, 0, 0, 16) };

        int cols = isPm ? 5 : 3;
        for (int i = 0; i < cols; i++)
        {
            kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (i < cols - 1)
                kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); // spacer
        }

        int colIdx = 0;

        // Avanzamento globale
        decimal globalPct = d.TotalPhases > 0 ? Math.Round((decimal)d.CompletedPhases / d.TotalPhases * 100, 0) : 0;
        AddKpiCard(kpiGrid, colIdx, "AVANZAMENTO", $"{globalPct}%", $"{d.CompletedPhases}/{d.TotalPhases} fasi", "#4F6EF7");
        colIdx += 2;

        // Ore
        decimal hoursPct = d.BudgetHoursTotal > 0 ? Math.Round(d.HoursWorked / d.BudgetHoursTotal * 100, 0) : 0;
        string hoursColor = hoursPct > 100 ? "#EF4444" : "#059669";
        AddKpiCard(kpiGrid, colIdx, "ORE", $"{d.HoursWorked:N1} h", $"su {d.BudgetHoursTotal:N0} h budget ({hoursPct}%)", hoursColor);
        colIdx += 2;

        // Tecnici attivi
        AddKpiCard(kpiGrid, colIdx, "TECNICI ATTIVI", d.ActiveTechnicians.Count.ToString(), "su questa commessa", "#7C3AED");
        colIdx += 2;

        if (isPm)
        {
            // Budget
            AddKpiCard(kpiGrid, colIdx, "COSTO CONSUNTIVO", $"{d.CostWorked:N0} €", $"su {d.BudgetTotal:N0} € budget", d.CostWorked > d.BudgetTotal ? "#EF4444" : "#059669");
            colIdx += 2;

            // Ricavo
            decimal margin = d.Revenue - d.CostWorked;
            AddKpiCard(kpiGrid, colIdx, "RICAVO", $"{d.Revenue:N0} €", $"Margine: {margin:N0} €", margin >= 0 ? "#059669" : "#EF4444");
        }

        pnlContent.Children.Add(kpiGrid);

        // ═══ ORE PER REPARTO (barre orizzontali) ═══
        if (d.DepartmentSummaries.Any())
        {
            pnlContent.Children.Add(SectionTitle("Ore per Reparto"));

            Border deptCard = MakeCard();
            StackPanel deptPanel = new();

            decimal maxHours = Math.Max(d.DepartmentSummaries.Max(ds => Math.Max(ds.BudgetHours, ds.HoursWorked)), 1);

            foreach (DeptSummary ds in d.DepartmentSummaries)
            {
                string color = DeptColors.TryGetValue(ds.DepartmentCode, out string? c) ? c : "#6B7280";
                decimal pct = ds.BudgetHours > 0 ? Math.Round(ds.HoursWorked / ds.BudgetHours * 100, 0) : 0;

                StackPanel row = new() { Margin = new Thickness(0, 6, 0, 6) };

                // Label riga
                DockPanel labelRow = new();
                Border deptBadge = new()
                {
                    Background = B(color), Padding = new Thickness(8, 2, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                deptBadge.Child = new TextBlock { Text = ds.DepartmentCode, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
                labelRow.Children.Add(deptBadge);
                labelRow.Children.Add(new TextBlock
                {
                    Text = $"  {ds.HoursWorked:N1} / {ds.BudgetHours:N0} h  ({pct}%)  —  {ds.CompletedPhases}/{ds.TotalPhases} fasi",
                    FontSize = 12, Foreground = B("#374151"), VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(labelRow);

                // Barra
                Grid barGrid = new() { Height = 8, Margin = new Thickness(0, 4, 0, 0) };
                barGrid.Children.Add(new Border { Background = B("#F3F4F6") }); // sfondo

                double budgetWidth = (double)(ds.BudgetHours / maxHours);
                barGrid.Children.Add(new Border
                {
                    Background = B(color + "40"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = Math.Max(0, budgetWidth * 500)
                });

                double workedWidth = (double)(ds.HoursWorked / maxHours);
                barGrid.Children.Add(new Border
                {
                    Background = B(color),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = Math.Max(0, workedWidth * 500)
                });

                row.Children.Add(barGrid);
                deptPanel.Children.Add(row);
            }

            deptCard.Child = deptPanel;
            pnlContent.Children.Add(deptCard);
        }

        // ═══ TECNICI ATTIVI ═══
        if (d.ActiveTechnicians.Any())
        {
            pnlContent.Children.Add(SectionTitle("Tecnici Attivi"));

            Border techCard = MakeCard();
            StackPanel techPanel = new();

            // Header
            Grid techHeader = new() { Margin = new Thickness(0, 0, 0, 6) };
            techHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            techHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            techHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            techHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            AddHdrCell(techHeader, 0, "TECNICO");
            AddHdrCell(techHeader, 1, "REPARTO");
            AddHdrCell(techHeader, 2, "FASI");
            AddHdrCell(techHeader, 3, "ORE");
            techPanel.Children.Add(techHeader);

            foreach (ActiveTechSummary tech in d.ActiveTechnicians)
            {
                Grid techRow = new()
                {
                    Margin = new Thickness(0, 0, 0, 2),
                    Background = d.ActiveTechnicians.IndexOf(tech) % 2 == 0 ? Brushes.White : B("#F9FAFB")
                };
                techRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                techRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                techRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                techRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                AddCell(techRow, 0, tech.EmployeeName, FontWeights.SemiBold);

                string deptCol = DeptColors.TryGetValue(tech.DepartmentCode, out string? dc) ? dc : "#6B7280";
                TextBlock deptTxt = new() { Text = tech.DepartmentCode, FontSize = 12, Foreground = B(deptCol), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
                Grid.SetColumn(deptTxt, 1);
                techRow.Children.Add(deptTxt);

                AddCell(techRow, 2, tech.PhaseCount.ToString());
                AddCell(techRow, 3, $"{tech.TotalHours:N1} h", FontWeights.SemiBold);
                techPanel.Children.Add(techRow);
            }

            techCard.Child = techPanel;
            pnlContent.Children.Add(techCard);
        }

        // ═══ ULTIMI TIMESHEET ═══
        if (d.RecentEntries.Any())
        {
            pnlContent.Children.Add(SectionTitle("Ultime Registrazioni"));

            Border tsCard = MakeCard();
            StackPanel tsPanel = new();

            // Header
            Grid tsHeader = new() { Margin = new Thickness(0, 0, 0, 6) };
            tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            AddHdrCell(tsHeader, 0, "DATA");
            AddHdrCell(tsHeader, 1, "TECNICO");
            AddHdrCell(tsHeader, 2, "FASE");
            AddHdrCell(tsHeader, 3, "ORE");
            AddHdrCell(tsHeader, 4, "TIPO");
            tsPanel.Children.Add(tsHeader);

            foreach (RecentTimesheetEntry entry in d.RecentEntries)
            {
                Grid tsRow = new()
                {
                    Margin = new Thickness(0, 0, 0, 1),
                    Background = d.RecentEntries.IndexOf(entry) % 2 == 0 ? Brushes.White : B("#F9FAFB")
                };
                tsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                tsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                tsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                tsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                tsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                AddCell(tsRow, 0, entry.WorkDate.ToString("dd/MM"));
                AddCell(tsRow, 1, entry.EmployeeName);
                AddCell(tsRow, 2, entry.PhaseName);
                AddCell(tsRow, 3, $"{entry.Hours:N1}", FontWeights.SemiBold);
                AddCell(tsRow, 4, entry.EntryType, FontWeights.Normal, "#6B7280");
                tsPanel.Children.Add(tsRow);
            }

            tsCard.Child = tsPanel;
            pnlContent.Children.Add(tsCard);
        }
    }

    // ═══ HELPERS ═══

    private void AddKpiCard(Grid grid, int col, string label, string value, string sub, string accentColor)
    {
        Border card = new()
        {
            Background = Brushes.White,
            BorderBrush = B("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12, 16, 12)
        };

        StackPanel sp = new();
        sp.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = B("#6B7280") });
        sp.Children.Add(new TextBlock { Text = value, FontSize = 28, FontWeight = FontWeights.Bold, Foreground = B(accentColor), Margin = new Thickness(0, 4, 0, 2) });
        sp.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = B("#9CA3AF") });

        card.Child = sp;
        Grid.SetColumn(card, col);
        grid.Children.Add(card);
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text, FontSize = 14, FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1D26")),
        Margin = new Thickness(0, 8, 0, 6)
    };

    private static Border MakeCard() => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4E7EC")),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(16, 12, 16, 12),
        Margin = new Thickness(0, 0, 0, 12)
    };

    private static void AddHdrCell(Grid grid, int col, string text)
    {
        TextBlock tb = new()
        {
            Text = text, FontSize = 9, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF")),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private static void AddCell(Grid grid, int col, string text, FontWeight? weight = null, string color = "#1A1D26")
    {
        TextBlock tb = new()
        {
            Text = text, FontSize = 12, FontWeight = weight ?? FontWeights.Normal,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4)
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private void AddInfoChip(WrapPanel panel, string icon, string text)
    {
        Border chip = new()
        {
            Background = B("#2A2D36"),
            Padding = new Thickness(8, 3, 10, 3),
            Margin = new Thickness(0, 0, 8, 0)
        };
        chip.Child = new TextBlock { Text = $"{icon} {text}", FontSize = 11, Foreground = B("#D1D5DB") };
        panel.Children.Add(chip);
    }

    private static SolidColorBrush GetStatusBackground(string status) => status switch
    {
        "ACTIVE" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669")),
        "COMPLETED" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
        "ON_HOLD" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"))
    };

    private static SolidColorBrush GetPriorityBackground(string prio) => prio switch
    {
        "HIGH" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
        "MEDIUM" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
        "LOW" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"))
    };
}
