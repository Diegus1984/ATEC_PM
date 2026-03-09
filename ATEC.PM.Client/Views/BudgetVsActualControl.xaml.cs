using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Costing;

public partial class BudgetVsActualControl : UserControl
{
    private readonly int _projectId;

    private static readonly Dictionary<string, string> GroupColors = new()
    {
        { "GESTIONE", "#2563EB" }, { "PRESCHIERAMENTO", "#7C3AED" },
        { "INSTALLAZIONE", "#D97706" }, { "OPZIONE", "#DC2626" },
        { "NON ASSEGNATO", "#6B7280" }
    };

    public BudgetVsActualControl(int projectId)
    {
        InitializeComponent();
        _projectId = projectId;
        Loaded += async (_, _) => await LoadData();
    }

    private async Task LoadData()
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/budget-vs-actual");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            BudgetVsActualData data = JsonSerializer.Deserialize<BudgetVsActualData>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            if (data.Groups.Count == 0)
            {
                txtLoading.Visibility = Visibility.Collapsed;
                txtEmpty.Visibility = Visibility.Visible;
                return;
            }

            BuildUI(data);

            txtLoading.Visibility = Visibility.Collapsed;
            pnlContent.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            txtLoading.Text = $"Errore: {ex.Message}";
        }
    }

    private void BuildUI(BudgetVsActualData data)
    {
        pnlGroups.Children.Clear();

        foreach (BvaGroupDto group in data.Groups)
        {
            string color = GroupColors.TryGetValue(group.GroupName, out string? c) ? c : "#6B7280";
            Brush accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            Brush accentLight = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) { Opacity = 0.08 };

            // ── HEADER GRUPPO ──
            Border groupHeader = new()
            {
                Background = accentLight,
                Margin = new Thickness(0, 12, 0, 0)
            };

            Grid headerGrid = new();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Nome gruppo + totali preventivo
            StackPanel leftHeader = new() { Orientation = Orientation.Horizontal };
            leftHeader.Children.Add(new TextBlock
            {
                Text = group.GroupName,
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = accentBrush, VerticalAlignment = VerticalAlignment.Center
            });
            leftHeader.Children.Add(new TextBlock
            {
                Text = $"    {group.BudgetHours:F1} h  —  {group.BudgetCost:N2} €",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(leftHeader, 0);
            headerGrid.Children.Add(leftHeader);

            // Totali consuntivo + delta
            StackPanel rightHeader = new() { Orientation = Orientation.Horizontal };
            rightHeader.Children.Add(new TextBlock
            {
                Text = "CONSUNTIVO",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = accentBrush, VerticalAlignment = VerticalAlignment.Center
            });
            rightHeader.Children.Add(new TextBlock
            {
                Text = $"    {group.ActualHours:F1} h  —  {group.ActualCost:N2} €",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                VerticalAlignment = VerticalAlignment.Center
            });

            decimal deltaH = group.ActualHours - group.BudgetHours;
            if (deltaH != 0)
            {
                rightHeader.Children.Add(new TextBlock
                {
                    Text = $"    Δ {(deltaH > 0 ? "+" : "")}{deltaH:F1} h",
                    FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = deltaH > 0
                        ? new SolidColorBrush(Color.FromRgb(220, 38, 38))   // rosso = sforato
                        : new SolidColorBrush(Color.FromRgb(5, 150, 105)),   // verde = sotto budget
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            Grid.SetColumn(rightHeader, 2);
            headerGrid.Children.Add(rightHeader);
            groupHeader.Child = headerGrid;
            pnlGroups.Children.Add(groupHeader);

            // ── INTESTAZIONE COLONNE SX / DX ──
            Border colHeaders = new()
            {
                Background = new SolidColorBrush(Color.FromRgb(249, 250, 251)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            Grid colGrid = new();
            colGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            colGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            colGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lbl1 = new() { Text = "PREVENTIVO (risorse pianificate)", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)) };
            TextBlock lbl2 = new() { Text = "CONSUNTIVO (ore versate)", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)) };
            Grid.SetColumn(lbl1, 0);
            Grid.SetColumn(lbl2, 2);
            colGrid.Children.Add(lbl1);
            colGrid.Children.Add(lbl2);
            colHeaders.Child = colGrid;
            pnlGroups.Children.Add(colHeaders);

            // ── SEZIONI ──
            foreach (BvaSectionDto section in group.Sections)
            {
                pnlGroups.Children.Add(BuildSectionPanel(section, accentBrush));
            }
        }

        // ── TOTALI GENERALI ──
        txtTotalBudgetHours.Text = $"{data.TotalBudgetHours:F1}";
        txtTotalBudgetCost.Text = $"{data.TotalBudgetCost:N2}";
        txtTotalActualHours.Text = $"{data.TotalActualHours:F1}";
        txtTotalActualCost.Text = $"{data.TotalActualCost:N2}";

        decimal totalDeltaH = data.TotalActualHours - data.TotalBudgetHours;
        txtTotalDelta.Text = $"Δ {(totalDeltaH > 0 ? "+" : "")}{totalDeltaH:F1} h";
        txtTotalDelta.Foreground = totalDeltaH > 0
            ? new SolidColorBrush(Color.FromRgb(248, 113, 113))  // rosso chiaro su sfondo scuro
            : new SolidColorBrush(Color.FromRgb(52, 211, 153));   // verde chiaro
    }

    private UIElement BuildSectionPanel(BvaSectionDto section, Brush accentBrush)
    {
        Border card = new()
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 4)
        };

        StackPanel stack = new();

        // ── Titolo sezione con totali ──
        Border titleBar = new() { Background = Brushes.White };
        Grid titleGrid = new();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // SX: nome + badge tipo + ore/costo budget
        StackPanel leftTitle = new() { Orientation = Orientation.Horizontal };
        leftTitle.Children.Add(new Border
        {
            Width = 3, Height = 14, Background = accentBrush,
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        });
        leftTitle.Children.Add(new TextBlock
        {
            Text = section.SectionName,
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(26, 29, 38)),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Badge IN_SEDE / DA_CLIENTE
        string badgeText = section.SectionType == "DA_CLIENTE" ? "CLIENTE" : "SEDE";
        string badgeColor = section.SectionType == "DA_CLIENTE" ? "#D97706" : "#059669";
        leftTitle.Children.Add(new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeColor)) { Opacity = 0.12 },
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = badgeText, FontSize = 9, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeColor))
            }
        });

        leftTitle.Children.Add(new TextBlock
        {
            Text = $"    {section.BudgetHours:F1} h  —  {section.BudgetCost:N2} €",
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(leftTitle, 0);
        titleGrid.Children.Add(leftTitle);

        // DX: ore/costo actual + delta
        StackPanel rightTitle = new() { Orientation = Orientation.Horizontal };
        rightTitle.Children.Add(new TextBlock
        {
            Text = $"{section.ActualHours:F1} h  —  {section.ActualCost:N2} €",
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
            VerticalAlignment = VerticalAlignment.Center
        });

        if (section.DeltaHours != 0)
        {
            bool over = section.DeltaHours > 0;
            rightTitle.Children.Add(new TextBlock
            {
                Text = $"    Δ {(over ? "+" : "")}{section.DeltaHours:F1} h",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = over
                    ? new SolidColorBrush(Color.FromRgb(220, 38, 38))
                    : new SolidColorBrush(Color.FromRgb(5, 150, 105)),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        Grid.SetColumn(rightTitle, 2);
        titleGrid.Children.Add(rightTitle);

        titleBar.Child = titleGrid;
        stack.Children.Add(titleBar);

        // ── Griglia dati split ──
        Grid splitGrid = new() { Margin = new Thickness(12, 0, 12, 4) };
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Griglia preventivo
        if (section.BudgetResources.Count > 0)
        {
            ContentPresenter budgetPresenter = new()
            {
                Content = section,
                ContentTemplate = (DataTemplate)Resources["BudgetGrid"]
            };
            Grid.SetColumn(budgetPresenter, 0);
            splitGrid.Children.Add(budgetPresenter);
        }
        else
        {
            TextBlock noBudget = new()
            {
                Text = "Nessuna risorsa pianificata",
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetColumn(noBudget, 0);
            splitGrid.Children.Add(noBudget);
        }

        // Separatore verticale
        Border sep = new()
        {
            Width = 1, Background = new SolidColorBrush(Color.FromRgb(228, 231, 236)),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(sep, 1);
        splitGrid.Children.Add(sep);

        // Griglia consuntivo
        if (section.ActualEntries.Count > 0)
        {
            ContentPresenter actualPresenter = new()
            {
                Content = section,
                ContentTemplate = (DataTemplate)Resources["ActualGrid"]
            };
            Grid.SetColumn(actualPresenter, 2);
            splitGrid.Children.Add(actualPresenter);
        }
        else
        {
            TextBlock noActual = new()
            {
                Text = "Nessuna ora versata",
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetColumn(noActual, 2);
            splitGrid.Children.Add(noActual);
        }

        stack.Children.Add(splitGrid);

        // ── Barra avanzamento visivo ──
        if (section.BudgetHours > 0)
        {
            double pct = (double)(section.ActualHours / section.BudgetHours);
            bool over = pct > 1.0;

            Border progressBg = new()
            {
                Height = 4, Background = new SolidColorBrush(Color.FromRgb(243, 244, 246)),
                Margin = new Thickness(12, 4, 12, 4)
            };

            Border progressFill = new()
            {
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = over
                    ? new SolidColorBrush(Color.FromRgb(220, 38, 38))
                    : new SolidColorBrush(Color.FromRgb(5, 150, 105))
            };
            progressFill.Width = 0; // sarà impostato dopo il layout
            progressBg.Child = progressFill;

            progressBg.Loaded += (s, e) =>
            {
                double maxW = progressBg.ActualWidth;
                progressFill.Width = Math.Min(maxW, maxW * pct);
            };

            stack.Children.Add(progressBg);
        }

        card.Child = stack;
        return card;
    }
}
