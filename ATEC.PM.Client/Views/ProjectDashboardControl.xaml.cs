using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;
using OxyPlot.Wpf;

namespace ATEC.PM.Client.UserControls;

public partial class ProjectDashboardControl : UserControl
{
    private int _projectId;

    private static readonly Dictionary<string, string> DeptColors = new()
    {
        { "PM", "#4F6EF7" }, { "UTM", "#059669" }, { "UTE", "#2563EB" },
        { "MEC", "#D97706" }, { "INS", "#DC2626" }, { "PLC", "#7C3AED" },
        { "ROB", "#BE185D" }, { "ACQ", "#0891B2" }, { "AMM", "#6B7280" },
        { "TRASV", "#6B7280" }, { "", "#6B7280" }
    };

    private static SolidColorBrush B(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private static OxyPlot.OxyColor OC(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return OxyPlot.OxyColor.FromRgb(c.R, c.G, c.B);
    }

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

        RenderHeader(d);
        RenderKpiRow1(d);
        if (isPm) RenderKpiRow2(d);

        // ═══ NOTE ═══
        if (!string.IsNullOrWhiteSpace(d.Notes))
        {
            pnlContent.Children.Add(SectionTitle("Note Commessa"));
            Border noteCard = MakeCard();
            noteCard.Child = new TextBlock
            {
                Text = d.Notes,
                FontSize = 13,
                Foreground = B("#374151"),
                TextWrapping = TextWrapping.Wrap
            };
            pnlContent.Children.Add(noteCard);
        }

        // ═══ GRAFICI AFFIANCATI ═══
        if (d.DepartmentSummaries.Any())
        {
            pnlContent.Children.Add(SectionTitle("Analisi per Reparto"));

            Grid chartRow = new() { Margin = new Thickness(0, 0, 0, 12) };
            chartRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chartRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            chartRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pieCard = MakeCard();
            pieCard.Child = RenderPieChart(d);
            Grid.SetColumn(pieCard, 0);
            chartRow.Children.Add(pieCard);

            var barCard = MakeCard();
            barCard.Child = RenderBudgetVsActualChart(d);
            Grid.SetColumn(barCard, 2);
            chartRow.Children.Add(barCard);

            pnlContent.Children.Add(chartRow);
        }

        // ═══ ORE SETTIMANALI ═══
        if (d.WeeklyHours.Any())
        {
            pnlContent.Children.Add(SectionTitle("Andamento Ore Settimanali"));
            Border lineCard = MakeCard();
            lineCard.Child = RenderWeeklyChart(d);
            pnlContent.Children.Add(lineCard);
        }

        // ═══ GANTT ═══
        if (d.PhaseGantt.Any())
        {
            pnlContent.Children.Add(SectionTitle("Timeline Fasi"));
            Border ganttCard = MakeCard();
            ganttCard.Child = RenderGanttChart(d);
            pnlContent.Children.Add(ganttCard);
        }

        // ═══ SCADENZE ═══
        if (d.Deadlines.Any())
        {
            pnlContent.Children.Add(SectionTitle("Scadenze Prossime"));
            RenderDeadlines(d);
        }

        // ═══ ORE PER REPARTO ═══
        if (d.DepartmentSummaries.Any())
        {
            pnlContent.Children.Add(SectionTitle("Ore per Reparto"));
            RenderDeptBars(d, isPm);
        }

        if (d.ActiveTechnicians.Any())
            RenderTechnicianTable(d);

        if (d.RecentEntries.Any())
            RenderRecentEntries(d);
    }

    // ══════════════════════════════════════════════════════════
    // HEADER
    // ══════════════════════════════════════════════════════════

    private void RenderHeader(ProjectDashboardData d)
    {
        Border header = new()
        {
            Background = B("#1A1D26"),
            Padding = new Thickness(24, 16, 24, 16),
            Margin = new Thickness(0, 0, 0, 16)
        };
        StackPanel headerContent = new();

        DockPanel row1 = new();
        row1.Children.Add(new TextBlock { Text = d.Code, FontSize = 22, FontWeight = System.Windows.FontWeights.Bold, Foreground = B("#4F6EF7") });

        Border statusBadge = new()
        {
            Background = GetStatusBackground(d.Status),
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        statusBadge.Child = new TextBlock { Text = d.Status, FontSize = 11, FontWeight = System.Windows.FontWeights.SemiBold, Foreground = Brushes.White };
        row1.Children.Add(statusBadge);

        Border prioBadge = new()
        {
            Background = GetPriorityBackground(d.Priority),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        prioBadge.Child = new TextBlock { Text = d.Priority, FontSize = 10, FontWeight = System.Windows.FontWeights.SemiBold, Foreground = Brushes.White };
        row1.Children.Add(prioBadge);

        headerContent.Children.Add(row1);
        headerContent.Children.Add(new TextBlock { Text = d.Title, FontSize = 15, Foreground = B("#E5E7EB"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });

        WrapPanel infoRow = new() { Margin = new Thickness(0, 8, 0, 0) };
        AddInfoChip(infoRow, "🏢", d.CustomerName);
        if (!string.IsNullOrEmpty(d.PmName)) AddInfoChip(infoRow, "👤", d.PmName);
        if (d.StartDate.HasValue) AddInfoChip(infoRow, "📅", $"Inizio: {d.StartDate.Value:dd/MM/yyyy}");
        if (d.EndDatePlanned.HasValue) AddInfoChip(infoRow, "🏁", $"Fine prev.: {d.EndDatePlanned.Value:dd/MM/yyyy}");
        headerContent.Children.Add(infoRow);

        header.Child = headerContent;
        pnlContent.Children.Add(header);
    }

    // ══════════════════════════════════════════════════════════
    // KPI
    // ══════════════════════════════════════════════════════════

    private void RenderKpiRow1(ProjectDashboardData d)
    {
        Grid kpiRow = new() { Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < 4; i++)
        {
            kpiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (i < 3) kpiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        }

        decimal globalPct = d.TotalPhases > 0 ? Math.Round((decimal)d.CompletedPhases / d.TotalPhases * 100, 0) : 0;
        AddKpiCard(kpiRow, 0, "AVANZAMENTO", $"{globalPct}%", $"{d.CompletedPhases}/{d.TotalPhases} fasi", "#4F6EF7");

        decimal hoursPct = d.BudgetHoursTotal > 0 ? Math.Round(d.HoursWorked / d.BudgetHoursTotal * 100, 0) : 0;
        string hoursColor = hoursPct > 100 ? "#EF4444" : "#059669";
        AddKpiCard(kpiRow, 2, "ORE", $"{d.HoursWorked:N1} h", $"su {d.BudgetHoursTotal:N0} h budget ({hoursPct}%)", hoursColor);

        AddKpiCard(kpiRow, 4, "TECNICI ATTIVI", d.ActiveTechnicians.Count.ToString(), "su questa commessa", "#7C3AED");
        AddKpiCard(kpiRow, 6, "FASI COMPLETATE", $"{d.CompletedPhases}/{d.TotalPhases}", $"{globalPct}% completamento", globalPct >= 100 ? "#059669" : "#4F6EF7");

        pnlContent.Children.Add(kpiRow);
    }

    private void RenderKpiRow2(ProjectDashboardData d)
    {
        Grid kpiRow = new() { Margin = new Thickness(0, 0, 0, 16) };
        for (int i = 0; i < 4; i++)
        {
            kpiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (i < 3) kpiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        }

        AddKpiCard(kpiRow, 0, "COSTO ORE", $"{d.CostWorked:N0} €", "manodopera", "#D97706");
        AddKpiCard(kpiRow, 2, "COSTO MATERIALI", $"{d.MaterialCost:N0} €", "da DDP", "#0891B2");

        string totalColor = d.TotalCost > d.BudgetTotal ? "#EF4444" : "#059669";
        AddKpiCard(kpiRow, 4, "COSTO TOTALE", $"{d.TotalCost:N0} €", $"su {d.BudgetTotal:N0} € budget", totalColor);

        decimal margin = d.Revenue - d.TotalCost;
        AddKpiCard(kpiRow, 6, "MARGINE", $"{margin:N0} €", $"Ricavo: {d.Revenue:N0} €", margin >= 0 ? "#059669" : "#EF4444");

        pnlContent.Children.Add(kpiRow);
    }

    // ══════════════════════════════════════════════════════════
    // TORTA
    // ══════════════════════════════════════════════════════════

    private UIElement RenderPieChart(ProjectDashboardData d)
    {
        var model = new OxyPlot.PlotModel
        {
            Title = "Avanzamento per Reparto",
            TitleFontSize = 13,
            TitleFontWeight = OxyPlot.FontWeights.Bold,
            PlotAreaBorderThickness = new OxyPlot.OxyThickness(0),
            Padding = new OxyPlot.OxyThickness(0)
        };

        var series = new OxyPlot.Series.PieSeries
        {
            StrokeThickness = 1,
            Stroke = OxyPlot.OxyColors.White,
            InsideLabelPosition = 0.5,
            InsideLabelFormat = "{1}: {0:N0}h",
            FontSize = 10
        };

        foreach (var ds in d.DepartmentSummaries.Where(x => x.HoursWorked > 0))
        {
            string color = DeptColors.TryGetValue(ds.DepartmentCode, out string? c) ? c : "#6B7280";
            series.Slices.Add(new OxyPlot.Series.PieSlice(ds.DepartmentCode, (double)ds.HoursWorked) { Fill = OC(color) });
        }

        model.Series.Add(series);
        return new PlotView { Model = model, Height = 280, Background = Brushes.White };
    }

    // ══════════════════════════════════════════════════════════
    // BARRE PREVENTIVO VS CONSUNTIVO
    // ══════════════════════════════════════════════════════════

    private UIElement RenderBudgetVsActualChart(ProjectDashboardData d)
    {
        var model = new OxyPlot.PlotModel
        {
            Title = "Preventivo vs Consuntivo (ore)",
            TitleFontSize = 13,
            TitleFontWeight = OxyPlot.FontWeights.Bold,
            PlotAreaBorderThickness = new OxyPlot.OxyThickness(0),
            Padding = new OxyPlot.OxyThickness(8, 8, 8, 8)
        };

        var catAxis = new OxyPlot.Axes.CategoryAxis { Position = OxyPlot.Axes.AxisPosition.Left, GapWidth = 0.3, FontSize = 11 };
        var valAxis = new OxyPlot.Axes.LinearAxis { Position = OxyPlot.Axes.AxisPosition.Bottom, MinimumPadding = 0, AbsoluteMinimum = 0, FontSize = 10, Title = "Ore" };

        var budgetSeries = new OxyPlot.Series.BarSeries { Title = "Budget", FillColor = OxyPlot.OxyColor.FromRgb(0x4F, 0x6E, 0xF7), StrokeThickness = 0 };
        var actualSeries = new OxyPlot.Series.BarSeries { Title = "Consuntivo", FillColor = OxyPlot.OxyColor.FromRgb(0x05, 0x96, 0x69), StrokeThickness = 0 };

        foreach (var ds in d.DepartmentSummaries)
        {
            catAxis.Labels.Add(ds.DepartmentCode);
            budgetSeries.Items.Add(new OxyPlot.Series.BarItem((double)ds.BudgetHours));
            actualSeries.Items.Add(new OxyPlot.Series.BarItem((double)ds.HoursWorked));
        }

        model.Axes.Add(catAxis);
        model.Axes.Add(valAxis);
        model.Series.Add(budgetSeries);
        model.Series.Add(actualSeries);
        model.Legends.Add(new OxyPlot.Legends.Legend { LegendPosition = OxyPlot.Legends.LegendPosition.TopRight, FontSize = 10 });

        return new PlotView { Model = model, Height = 280, Background = Brushes.White };
    }

    // ══════════════════════════════════════════════════════════
    // LINEA ORE SETTIMANALI
    // ══════════════════════════════════════════════════════════

    private UIElement RenderWeeklyChart(ProjectDashboardData d)
    {
        var model = new OxyPlot.PlotModel
        {
            Title = "Ore per Settimana (ultime 12)",
            TitleFontSize = 13,
            TitleFontWeight = OxyPlot.FontWeights.Bold,
            PlotAreaBorderThickness = new OxyPlot.OxyThickness(0.5),
            Padding = new OxyPlot.OxyThickness(8, 8, 16, 8)
        };

        var catAxis = new OxyPlot.Axes.CategoryAxis { Position = OxyPlot.Axes.AxisPosition.Bottom, FontSize = 10 };
        var valAxis = new OxyPlot.Axes.LinearAxis { Position = OxyPlot.Axes.AxisPosition.Left, MinimumPadding = 0, AbsoluteMinimum = 0, FontSize = 10, Title = "Ore" };

        var areaSeries = new OxyPlot.Series.AreaSeries
        {
            Color = OxyPlot.OxyColor.FromRgb(0x4F, 0x6E, 0xF7),
            Fill = OxyPlot.OxyColor.FromArgb(60, 0x4F, 0x6E, 0xF7),
            MarkerType = OxyPlot.MarkerType.Circle,
            MarkerSize = 4,
            MarkerFill = OxyPlot.OxyColor.FromRgb(0x4F, 0x6E, 0xF7),
            StrokeThickness = 2
        };

        for (int i = 0; i < d.WeeklyHours.Count; i++)
        {
            catAxis.Labels.Add(d.WeeklyHours[i].WeekLabel);
            areaSeries.Points.Add(new OxyPlot.DataPoint(i, (double)d.WeeklyHours[i].Hours));
        }

        model.Axes.Add(catAxis);
        model.Axes.Add(valAxis);
        model.Series.Add(areaSeries);

        return new PlotView { Model = model, Height = 220, Background = Brushes.White };
    }

    // ══════════════════════════════════════════════════════════
    // GANTT
    // ══════════════════════════════════════════════════════════

    private UIElement RenderGanttChart(ProjectDashboardData d)
    {
        var phasesWithDates = d.PhaseGantt.Where(p => p.StartDate.HasValue || p.EndDate.HasValue).ToList();
        if (!phasesWithDates.Any())
        {
            return new TextBlock
            {
                Text = "Nessuna fase con date impostate. Assegna date di inizio/fine alle fasi per visualizzare il Gantt.",
                FontSize = 12,
                Foreground = B("#9CA3AF"),
                Margin = new Thickness(8)
            };
        }

        var model = new OxyPlot.PlotModel
        {
            Title = "Timeline Fasi",
            TitleFontSize = 13,
            TitleFontWeight = OxyPlot.FontWeights.Bold,
            PlotAreaBorderThickness = new OxyPlot.OxyThickness(0.5),
            Padding = new OxyPlot.OxyThickness(8, 8, 16, 8)
        };

        var catAxis = new OxyPlot.Axes.CategoryAxis { Position = OxyPlot.Axes.AxisPosition.Left, FontSize = 10, GapWidth = 0.2 };
        var dateAxis = new OxyPlot.Axes.DateTimeAxis { Position = OxyPlot.Axes.AxisPosition.Bottom, FontSize = 9, StringFormat = "dd/MM" };

        var barSeries = new OxyPlot.Series.IntervalBarSeries { StrokeThickness = 0 };

        foreach (var phase in phasesWithDates.OrderBy(p => p.SortOrder))
        {
            DateTime start = phase.StartDate ?? DateTime.Today;
            DateTime end = phase.EndDate ?? start.AddDays(7);
            if (end <= start) end = start.AddDays(1);

            string color = DeptColors.TryGetValue(phase.DepartmentCode, out string? c) ? c : "#6B7280";
            var oxyCol = OC(color);

            barSeries.Items.Add(new OxyPlot.Series.IntervalBarItem
            {
                Start = OxyPlot.Axes.DateTimeAxis.ToDouble(start),
                End = OxyPlot.Axes.DateTimeAxis.ToDouble(end),
                Color = OxyPlot.OxyColor.FromArgb(100, oxyCol.R, oxyCol.G, oxyCol.B)
            });

            catAxis.Labels.Add($"{phase.PhaseName} [{phase.DepartmentCode}]");
        }

        model.Axes.Add(catAxis);
        model.Axes.Add(dateAxis);
        model.Series.Add(barSeries);

        int height = Math.Max(200, phasesWithDates.Count * 28 + 60);
        return new PlotView { Model = model, Height = height, Background = Brushes.White };
    }

    // ══════════════════════════════════════════════════════════
    // SCADENZE
    // ══════════════════════════════════════════════════════════

    private void RenderDeadlines(ProjectDashboardData d)
    {
        Border card = MakeCard();
        StackPanel sp = new();

        foreach (var dl in d.Deadlines)
        {
            string urgencyColor;
            string urgencyIcon;
            if (dl.DaysRemaining < 0) { urgencyColor = "#EF4444"; urgencyIcon = "🔴"; }
            else if (dl.DaysRemaining <= 3) { urgencyColor = "#F59E0B"; urgencyIcon = "🟡"; }
            else if (dl.DaysRemaining <= 7) { urgencyColor = "#3B82F6"; urgencyIcon = "🔵"; }
            else { urgencyColor = "#059669"; urgencyIcon = "🟢"; }

            string deptColor = DeptColors.TryGetValue(dl.DepartmentCode, out string? dc) ? dc : "#6B7280";

            Grid row = new() { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            AddCell(row, 0, urgencyIcon);

            TextBlock deptTxt = new()
            {
                Text = dl.DepartmentCode,
                FontSize = 11,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = B(deptColor),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            };
            Grid.SetColumn(deptTxt, 1);
            row.Children.Add(deptTxt);

            AddCell(row, 2, dl.PhaseName);
            AddCell(row, 3, dl.Deadline.ToString("dd/MM/yyyy"));

            string remaining = dl.DaysRemaining < 0
                ? $"{Math.Abs(dl.DaysRemaining)}gg IN RITARDO"
                : dl.DaysRemaining == 0 ? "OGGI" : $"{dl.DaysRemaining}gg";
            AddCell(row, 4, remaining, System.Windows.FontWeights.SemiBold, urgencyColor);

            sp.Children.Add(row);
        }

        card.Child = sp;
        pnlContent.Children.Add(card);
    }

    // ══════════════════════════════════════════════════════════
    // ORE PER REPARTO
    // ══════════════════════════════════════════════════════════

    private void RenderDeptBars(ProjectDashboardData d, bool isPm)
    {
        Border deptCard = MakeCard();
        StackPanel deptPanel = new();

        decimal maxHours = Math.Max(d.DepartmentSummaries.Max(ds => Math.Max(ds.BudgetHours, ds.HoursWorked)), 1);

        foreach (DeptSummary ds in d.DepartmentSummaries)
        {
            string color = DeptColors.TryGetValue(ds.DepartmentCode, out string? c) ? c : "#6B7280";
            decimal pct = ds.BudgetHours > 0 ? Math.Round(ds.HoursWorked / ds.BudgetHours * 100, 0) : 0;

            StackPanel row = new() { Margin = new Thickness(0, 6, 0, 6) };

            if (isPm && ds.MaterialCost > 0)
            {
                row.Children.Add(new TextBlock
                {
                    Text = $"    Materiali: {ds.MaterialCost:N0} €",
                    FontSize = 11,
                    Foreground = B("#0891B2"),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            DockPanel labelRow = new();
            Border deptBadge = new()
            {
                Background = B(color),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            deptBadge.Child = new TextBlock { Text = ds.DepartmentCode, FontSize = 10, FontWeight = System.Windows.FontWeights.Bold, Foreground = Brushes.White };
            labelRow.Children.Add(deptBadge);
            labelRow.Children.Add(new TextBlock
            {
                Text = $"  {ds.HoursWorked:N1} / {ds.BudgetHours:N0} h  ({pct}%)  —  {ds.CompletedPhases}/{ds.TotalPhases} fasi",
                FontSize = 12,
                Foreground = B("#374151"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            row.Children.Add(labelRow);

            Grid barGrid = new() { Height = 8, Margin = new Thickness(0, 4, 0, 0) };
            barGrid.Children.Add(new Border { Background = B("#F3F4F6") });

            double budgetWidth = (double)(ds.BudgetHours / maxHours);
            barGrid.Children.Add(new Border
            {
                Background = B(color + "40"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Width = Math.Max(0, budgetWidth * 500)
            });

            double workedWidth = (double)(ds.HoursWorked / maxHours);
            barGrid.Children.Add(new Border
            {
                Background = B(color),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Width = Math.Max(0, workedWidth * 500)
            });

            row.Children.Add(barGrid);
            deptPanel.Children.Add(row);
        }

        deptCard.Child = deptPanel;
        pnlContent.Children.Add(deptCard);
    }

    // ══════════════════════════════════════════════════════════
    // TABELLA TECNICI
    // ══════════════════════════════════════════════════════════

    private void RenderTechnicianTable(ProjectDashboardData d)
    {
        pnlContent.Children.Add(SectionTitle("Tecnici Attivi"));
        Border techCard = MakeCard();
        StackPanel techPanel = new();

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

            AddCell(techRow, 0, tech.EmployeeName, System.Windows.FontWeights.SemiBold);

            string deptCol = DeptColors.TryGetValue(tech.DepartmentCode, out string? dc2) ? dc2 : "#6B7280";
            TextBlock deptTxt = new()
            {
                Text = tech.DepartmentCode,
                FontSize = 12,
                Foreground = B(deptCol),
                FontWeight = System.Windows.FontWeights.SemiBold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            };
            Grid.SetColumn(deptTxt, 1);
            techRow.Children.Add(deptTxt);

            AddCell(techRow, 2, tech.PhaseCount.ToString());
            AddCell(techRow, 3, $"{tech.TotalHours:N1} h", System.Windows.FontWeights.SemiBold);
            techPanel.Children.Add(techRow);
        }

        techCard.Child = techPanel;
        pnlContent.Children.Add(techCard);
    }

    // ══════════════════════════════════════════════════════════
    // TABELLA ULTIMI TIMESHEET
    // ══════════════════════════════════════════════════════════

    private void RenderRecentEntries(ProjectDashboardData d)
    {
        pnlContent.Children.Add(SectionTitle("Ultime Registrazioni"));
        Border tsCard = MakeCard();
        StackPanel tsPanel = new();

        Grid tsHeader = new() { Margin = new Thickness(0, 0, 0, 6) };
        tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        tsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        AddHdrCell(tsHeader, 0, "DATA");
        AddHdrCell(tsHeader, 1, "TECNICO");
        AddHdrCell(tsHeader, 2, "NOTE");
        AddHdrCell(tsHeader, 3, "FASE");
        AddHdrCell(tsHeader, 4, "ORE");
        AddHdrCell(tsHeader, 5, "TIPO");
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
            tsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            tsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            tsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            AddCell(tsRow, 0, entry.WorkDate.ToString("dd/MM"));
            AddCell(tsRow, 1, entry.EmployeeName);
            AddCell(tsRow, 2, entry.Notes, System.Windows.FontWeights.Normal, "#9CA3AF");
            AddCell(tsRow, 3, entry.PhaseName);
            AddCell(tsRow, 4, $"{entry.Hours:N1}", System.Windows.FontWeights.SemiBold);
            AddCell(tsRow, 5, entry.EntryType, System.Windows.FontWeights.Normal, "#6B7280");
            tsPanel.Children.Add(tsRow);
        }

        tsCard.Child = tsPanel;
        pnlContent.Children.Add(tsCard);
    }

    // ══════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════

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
        sp.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = System.Windows.FontWeights.SemiBold, Foreground = B("#6B7280") });
        sp.Children.Add(new TextBlock { Text = value, FontSize = 28, FontWeight = System.Windows.FontWeights.Bold, Foreground = B(accentColor), Margin = new Thickness(0, 4, 0, 2) });
        sp.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = B("#9CA3AF") });

        card.Child = sp;
        Grid.SetColumn(card, col);
        grid.Children.Add(card);
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = System.Windows.FontWeights.Bold,
        Foreground = B("#1A1D26"),
        Margin = new Thickness(0, 8, 0, 6)
    };

    private static Border MakeCard() => new()
    {
        Background = Brushes.White,
        BorderBrush = B("#E4E7EC"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(16, 12, 16, 12),
        Margin = new Thickness(0, 0, 0, 12)
    };

    private static void AddHdrCell(Grid grid, int col, string text)
    {
        TextBlock tb = new()
        {
            Text = text,
            FontSize = 9,
            FontWeight = System.Windows.FontWeights.SemiBold,
            Foreground = B("#9CA3AF"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private static void AddCell(Grid grid, int col, string text, FontWeight? weight = null, string color = "#1A1D26")
    {
        TextBlock tb = new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = weight ?? System.Windows.FontWeights.Normal,
            Foreground = B(color),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
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
        "ACTIVE" => B("#059669"),
        "COMPLETED" => B("#2563EB"),
        "ON_HOLD" => B("#D97706"),
        _ => B("#6B7280")
    };

    private static SolidColorBrush GetPriorityBackground(string prio) => prio switch
    {
        "HIGH" => B("#EF4444"),
        "MEDIUM" => B("#F59E0B"),
        "LOW" => B("#6B7280"),
        _ => B("#9CA3AF")
    };
}
