using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;
using OxyPlot;
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
        { "TRASV", "#6B7280" }, { "DEFAULT", "#6B7280" }
    };

    public ProjectDashboardControl() => InitializeComponent();

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
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var data = JsonSerializer.Deserialize<ProjectDashboardData>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data != null) Render(data);
        }
        catch (Exception ex)
        {
            pnlContent.Children.Clear();
            pnlContent.Children.Add(new TextBlock { Text = ex.Message, Foreground = Brushes.Red, Margin = new Thickness(16) });
        }
    }

    private void Render(ProjectDashboardData d)
    {
        pnlContent.Children.Clear();
        bool isPm = App.CurrentUser.IsPm;

        RenderHeader(d);
        RenderKpiRow(d, isPm);

        if (!string.IsNullOrWhiteSpace(d.Notes))
        {
            pnlContent.Children.Add(Title("Note Commessa"));
            pnlContent.Children.Add(Card(new TextBlock
            {
                Text = d.Notes,
                FontSize = 13,
                Foreground = B("#374151"),
                TextWrapping = TextWrapping.Wrap
            }));
        }

        if (d.DepartmentSummaries.Any())
        {
            pnlContent.Children.Add(Title("Analisi per Reparto"));
            RenderCharts(d);
        }

        if (d.WeeklyHours != null && d.WeeklyHours.Any())
        {
            pnlContent.Children.Add(Title("Andamento Ore Settimanali"));
            pnlContent.Children.Add(Card(CreateWeeklyChart(d)));
        }

        if (d.PhaseGantt != null && d.PhaseGantt.Any())
        {
            pnlContent.Children.Add(Title("Timeline Fasi"));
            pnlContent.Children.Add(Card(CreateGanttChart(d)));
        }

        if (d.Deadlines != null && d.Deadlines.Any())
        {
            pnlContent.Children.Add(Title("Scadenze Prossime"));
            pnlContent.Children.Add(Card(new ItemsControl
            {
                ItemTemplate = (DataTemplate)Resources["DeadlineRowTemplate"],
                ItemsSource = d.Deadlines.Select(dl => new
                {
                    dl.PhaseName,
                    dl.DepartmentCode,
                    DeptBrush = GetBrush(dl.DepartmentCode),
                    Icon = dl.DaysRemaining < 0 ? "🔴" : dl.DaysRemaining <= 3 ? "🟡" : dl.DaysRemaining <= 7 ? "🔵" : "🟢",
                    DeadlineText = dl.Deadline.ToString("dd/MM/yyyy"),
                    RemainingText = dl.DaysRemaining < 0 ? $"{Math.Abs(dl.DaysRemaining)}gg RITARDO"
                                  : dl.DaysRemaining == 0 ? "OGGI"
                                  : $"{dl.DaysRemaining}gg",
                    UrgencyBrush = dl.DaysRemaining < 0 ? B("#EF4444")
                                 : dl.DaysRemaining <= 3 ? B("#F59E0B")
                                 : dl.DaysRemaining <= 7 ? B("#3B82F6")
                                 : B("#059669")
                })
            }));
        }

        if (d.DepartmentSummaries.Any())
        {
            pnlContent.Children.Add(Title("Ore per Reparto"));
            decimal maxH = Math.Max(d.DepartmentSummaries.Max(s => Math.Max(s.BudgetHours, s.HoursWorked)), 1);
            pnlContent.Children.Add(Card(new ItemsControl
            {
                ItemTemplate = (DataTemplate)Resources["DeptBarTemplate"],
                ItemsSource = d.DepartmentSummaries.Select(ds =>
                {
                    string c = DeptColors.GetValueOrDefault(ds.DepartmentCode, DeptColors["DEFAULT"]);
                    decimal pct = ds.BudgetHours > 0 ? Math.Round(ds.HoursWorked / ds.BudgetHours * 100, 0) : 0;
                    return new
                    {
                        ds.DepartmentCode,
                        DeptBrush = B(c),
                        BudgetBarBrush = B(c + "40"),
                        Summary = $"{ds.HoursWorked:N1} / {ds.BudgetHours:N0} h  ({pct}%)  —  {ds.CompletedPhases}/{ds.TotalPhases} fasi",
                        BudgetBarWidth = Math.Max(0, (double)(ds.BudgetHours / maxH) * 500),
                        WorkedBarWidth = Math.Max(0, (double)(ds.HoursWorked / maxH) * 500)
                    };
                })
            }));
        }

        if (d.ActiveTechnicians.Any())
        {
            pnlContent.Children.Add(Title("Personale su Commessa"));
            pnlContent.Children.Add(Card(new ItemsControl
            {
                ItemTemplate = (DataTemplate)Resources["TechnicianRowTemplate"],
                ItemsSource = d.ActiveTechnicians.Select(t => new
                {
                    t.EmployeeName,
                    t.DepartmentCode,
                    t.PhaseCount,
                    t.TotalHours,
                    DeptBrush = GetBrush(t.DepartmentCode)
                })
            }));
        }

        if (d.RecentEntries.Any())
        {
            pnlContent.Children.Add(Title("Ultime Attività"));
            pnlContent.Children.Add(Card(new ItemsControl
            {
                ItemTemplate = (DataTemplate)Resources["TimesheetRowTemplate"],
                ItemsSource = d.RecentEntries
            }));
        }
    }

    // ══════════════════════════════════════════════════════════
    // HEADER
    // ══════════════════════════════════════════════════════════

    private void RenderHeader(ProjectDashboardData d)
    {
        var header = new Border
        {
            Background = B("#1A1D26"),
            Padding = new Thickness(20, 15, 20, 15),
            Margin = new Thickness(-16, 0, -16, 16)
        };
        var sp = new StackPanel();

        var row1 = new DockPanel();
        row1.Children.Add(new TextBlock { Text = d.Code, FontSize = 22, FontWeight = System.Windows.FontWeights.Bold, Foreground = B("#4F6EF7") });
        row1.Children.Add(Badge(d.Status, GetStatusColor(d.Status)));
        row1.Children.Add(Badge(d.Priority, GetPriorityColor(d.Priority)));
        sp.Children.Add(row1);

        sp.Children.Add(new TextBlock { Text = d.Title, FontSize = 14, Foreground = B("#E5E7EB"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });

        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Chip(chips, "🏢", d.CustomerName);
        if (!string.IsNullOrEmpty(d.PmName)) Chip(chips, "👤", d.PmName);
        if (d.StartDate.HasValue) Chip(chips, "📅", $"Inizio: {d.StartDate.Value:dd/MM/yyyy}");
        if (d.EndDatePlanned.HasValue) Chip(chips, "🏁", $"Fine prev.: {d.EndDatePlanned.Value:dd/MM/yyyy}");
        sp.Children.Add(chips);

        header.Child = sp;
        pnlContent.Children.Add(header);
    }

    // ══════════════════════════════════════════════════════════
    // KPI
    // ══════════════════════════════════════════════════════════

    private void RenderKpiRow(ProjectDashboardData d, bool isPm)
    {
        decimal phasePct = d.TotalPhases > 0 ? Math.Round((decimal)d.CompletedPhases / d.TotalPhases * 100) : 0;
        decimal hoursPct = d.BudgetHoursTotal > 0 ? Math.Round(d.HoursWorked / d.BudgetHoursTotal * 100) : 0;

        var kpis = new List<object>
        {
            new { Label = "AVANZAMENTO", Value = $"{phasePct}%", Subtitle = $"{d.CompletedPhases}/{d.TotalPhases} Fasi", AccentColor = B("#4F6EF7") },
            new { Label = "ORE TOTALI", Value = $"{d.HoursWorked:N1}", Subtitle = $"Budget: {d.BudgetHoursTotal:N0} h ({hoursPct}%)", AccentColor = hoursPct > 100 ? B("#EF4444") : B("#059669") },
            new { Label = "TECNICI", Value = d.ActiveTechnicians.Count.ToString(), Subtitle = "Attivi", AccentColor = B("#7C3AED") },
            new { Label = "COSTO MAT.", Value = $"{d.MaterialCost:N0} €", Subtitle = "Da acquisti", AccentColor = B("#0891B2") }
        };

        if (isPm)
        {
            decimal margin = d.Revenue - d.TotalCost;
            kpis.AddRange(new object[]
            {
                new { Label = "COSTO ORE", Value = $"{d.CostWorked:N0} €", Subtitle = "Manodopera", AccentColor = B("#D97706") },
                new { Label = "COSTO TOTALE", Value = $"{d.TotalCost:N0} €", Subtitle = $"Budget: {d.BudgetTotal:N0} €", AccentColor = d.TotalCost > d.BudgetTotal ? B("#EF4444") : B("#059669") },
                new { Label = "RICAVO", Value = $"{d.Revenue:N0} €", Subtitle = "Commessa", AccentColor = B("#2563EB") },
                new { Label = "MARGINE", Value = $"{margin:N0} €", Subtitle = $"{(d.Revenue > 0 ? Math.Round(margin / d.Revenue * 100) : 0)}%", AccentColor = margin >= 0 ? B("#059669") : B("#EF4444") }
            });
        }

        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -8, 12) };
        foreach (var kpi in kpis)
        {
            var visual = (FrameworkElement)((DataTemplate)Resources["KpiTemplate"]).LoadContent();
            visual.DataContext = kpi;
            grid.Children.Add(visual);
        }
        pnlContent.Children.Add(grid);
    }

    // ══════════════════════════════════════════════════════════
    // GRAFICI OxyPlot
    // ══════════════════════════════════════════════════════════

    private void RenderCharts(ProjectDashboardData d)
    {
        Grid grid = new() { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var pieCard = Card(CreatePie(d));
        pieCard.Height = 260;
        Grid.SetColumn(pieCard, 0);

        var barCard = Card(CreateBar(d));
        barCard.Height = 260;
        Grid.SetColumn(barCard, 2);

        grid.Children.Add(pieCard);
        grid.Children.Add(barCard);
        pnlContent.Children.Add(grid);
    }

    private PlotView CreatePie(ProjectDashboardData d)
    {
        var model = new PlotModel { Title = "Ore per Reparto", TitleFontSize = 12, TitleFontWeight = OxyPlot.FontWeights.Bold };
        var ps = new OxyPlot.Series.PieSeries
        {
            InnerDiameter = 0.4,
            StrokeThickness = 2,
            Stroke = OxyColors.White,
            InsideLabelPosition = 0.6,
            InsideLabelFormat = "{1}",
            FontSize = 10
        };
        foreach (var s in d.DepartmentSummaries.Where(x => x.HoursWorked > 0))
            ps.Slices.Add(new OxyPlot.Series.PieSlice(s.DepartmentCode, (double)s.HoursWorked) { Fill = GetOxyColor(s.DepartmentCode) });
        model.Series.Add(ps);
        return new PlotView { Model = model };
    }

    private PlotView CreateBar(ProjectDashboardData d)
    {
        var model = new PlotModel { Title = "Budget vs Consuntivo", TitleFontSize = 12, TitleFontWeight = OxyPlot.FontWeights.Bold };
        var b1 = new OxyPlot.Series.BarSeries { Title = "Budget", FillColor = OxyColor.FromRgb(0x4F, 0x6E, 0xF7), StrokeThickness = 0 };
        var b2 = new OxyPlot.Series.BarSeries { Title = "Consuntivo", FillColor = OxyColor.FromRgb(0x05, 0x96, 0x69), StrokeThickness = 0 };
        var ax = new OxyPlot.Axes.CategoryAxis { Position = OxyPlot.Axes.AxisPosition.Left, FontSize = 10 };
        var valAx = new OxyPlot.Axes.LinearAxis { Position = OxyPlot.Axes.AxisPosition.Bottom, MinimumPadding = 0, AbsoluteMinimum = 0, FontSize = 9, Title = "Ore" };

        foreach (var s in d.DepartmentSummaries)
        {
            ax.Labels.Add(s.DepartmentCode);
            b1.Items.Add(new OxyPlot.Series.BarItem((double)s.BudgetHours));
            b2.Items.Add(new OxyPlot.Series.BarItem((double)s.HoursWorked));
        }
        model.Axes.Add(ax);
        model.Axes.Add(valAx);
        model.Series.Add(b1);
        model.Series.Add(b2);
        model.Legends.Add(new OxyPlot.Legends.Legend { LegendPosition = OxyPlot.Legends.LegendPosition.TopRight, FontSize = 9 });
        return new PlotView { Model = model };
    }

    private PlotView CreateWeeklyChart(ProjectDashboardData d)
    {
        var model = new PlotModel { Title = "Ore per Settimana", TitleFontSize = 12, TitleFontWeight = OxyPlot.FontWeights.Bold };
        var catAxis = new OxyPlot.Axes.CategoryAxis { Position = OxyPlot.Axes.AxisPosition.Bottom, FontSize = 9 };
        var valAxis = new OxyPlot.Axes.LinearAxis { Position = OxyPlot.Axes.AxisPosition.Left, MinimumPadding = 0, AbsoluteMinimum = 0, FontSize = 9 };

        var series = new OxyPlot.Series.AreaSeries
        {
            Color = OxyColor.FromRgb(0x4F, 0x6E, 0xF7),
            Fill = OxyColor.FromArgb(60, 0x4F, 0x6E, 0xF7),
            MarkerType = OxyPlot.MarkerType.Circle,
            MarkerSize = 4,
            MarkerFill = OxyColor.FromRgb(0x4F, 0x6E, 0xF7),
            StrokeThickness = 2
        };

        for (int i = 0; i < d.WeeklyHours.Count; i++)
        {
            catAxis.Labels.Add(d.WeeklyHours[i].WeekLabel);
            series.Points.Add(new OxyPlot.DataPoint(i, (double)d.WeeklyHours[i].Hours));
        }

        model.Axes.Add(catAxis);
        model.Axes.Add(valAxis);
        model.Series.Add(series);
        return new PlotView { Model = model, Height = 200 };
    }

    private UIElement CreateGanttChart(ProjectDashboardData d)
    {
        var phasesWithDates = d.PhaseGantt.Where(p => p.StartDate.HasValue || p.EndDate.HasValue).ToList();
        if (!phasesWithDates.Any())
            return new TextBlock { Text = "Assegna date inizio/fine alle fasi per visualizzare il Gantt.", FontSize = 12, Foreground = B("#9CA3AF"), Margin = new Thickness(4) };

        var model = new PlotModel { Title = "Timeline Fasi", TitleFontSize = 12, TitleFontWeight = OxyPlot.FontWeights.Bold };
        var catAxis = new OxyPlot.Axes.CategoryAxis { Position = OxyPlot.Axes.AxisPosition.Left, FontSize = 9, GapWidth = 0.2 };
        var dateAxis = new OxyPlot.Axes.DateTimeAxis { Position = OxyPlot.Axes.AxisPosition.Bottom, FontSize = 8, StringFormat = "dd/MM" };

        var barSeries = new OxyPlot.Series.IntervalBarSeries { StrokeThickness = 0 };

        foreach (var phase in phasesWithDates.OrderBy(p => p.SortOrder))
        {
            DateTime start = phase.StartDate ?? DateTime.Today;
            DateTime end = phase.EndDate ?? start.AddDays(7);
            if (end <= start) end = start.AddDays(1);

            var oxyCol = GetOxyColor(phase.DepartmentCode);

            barSeries.Items.Add(new OxyPlot.Series.IntervalBarItem
            {
                Start = OxyPlot.Axes.DateTimeAxis.ToDouble(start),
                End = OxyPlot.Axes.DateTimeAxis.ToDouble(end),
                Color = OxyColor.FromArgb(160, oxyCol.R, oxyCol.G, oxyCol.B)
            });
            catAxis.Labels.Add($"{phase.PhaseName} [{phase.DepartmentCode}]");
        }

        model.Axes.Add(catAxis);
        model.Axes.Add(dateAxis);
        model.Series.Add(barSeries);

        int height = Math.Max(180, phasesWithDates.Count * 26 + 60);
        return new PlotView { Model = model, Height = height };
    }

    // ══════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════

    private TextBlock Title(string text) => new() { Text = text, Style = (Style)Resources["SectionTitleStyle"] };

    private Border Card(UIElement child) => new() { Style = (Style)Resources["CardStyle"], Child = child };

    private static Border Badge(string text, string color) => new()
    {
        Background = B(color),
        Padding = new Thickness(10, 3, 10, 3),
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = System.Windows.VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 10, FontWeight = System.Windows.FontWeights.SemiBold, Foreground = Brushes.White }
    };

    private static void Chip(WrapPanel panel, string icon, string text)
    {
        panel.Children.Add(new Border
        {
            Background = B("#2A2D36"),
            Padding = new Thickness(8, 3, 10, 3),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock { Text = $"{icon} {text}", FontSize = 11, Foreground = B("#D1D5DB") }
        });
    }

    private SolidColorBrush GetBrush(string code) => B(DeptColors.GetValueOrDefault(code ?? "", DeptColors["DEFAULT"]));
    private OxyColor GetOxyColor(string code) => OxyColor.Parse(DeptColors.GetValueOrDefault(code ?? "", DeptColors["DEFAULT"]));
    private static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private static string GetStatusColor(string s) => s switch
    {
        "ACTIVE" => "#059669", "COMPLETED" => "#2563EB", "ON_HOLD" => "#D97706", _ => "#6B7280"
    };

    private static string GetPriorityColor(string p) => p switch
    {
        "HIGH" => "#EF4444", "MEDIUM" => "#F59E0B", "LOW" => "#6B7280", _ => "#9CA3AF"
    };
}
