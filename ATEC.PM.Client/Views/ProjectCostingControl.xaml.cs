using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

// ViewModel per riga risorsa editabile nella DataGrid
public class CostResourceRow : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public int? EmployeeId { get; set; }
    public string ResourceName { get; set; } = "";

    private decimal _workDays;
    public decimal WorkDays { get => _workDays; set { _workDays = value; Notify(); Notify(nameof(TotalHours)); Notify(nameof(TotalCost)); } }

    private decimal _hoursPerDay;
    public decimal HoursPerDay { get => _hoursPerDay; set { _hoursPerDay = value; Notify(); Notify(nameof(TotalHours)); Notify(nameof(TotalCost)); } }

    private decimal _hourlyCost;
    public decimal HourlyCost { get => _hourlyCost; set { _hourlyCost = value; Notify(); Notify(nameof(TotalCost)); } }

    public decimal TotalHours => WorkDays * HoursPerDay;
    public decimal TotalCost => TotalHours * HourlyCost;

    // Trasferta
    public int NumTrips { get; set; }
    public decimal KmPerTrip { get; set; }
    public decimal CostPerKm { get; set; } = 0.90m;
    public decimal DailyFood { get; set; }
    public decimal DailyHotel { get; set; }
    public decimal AllowanceDays { get; set; }
    public decimal DailyAllowance { get; set; }

    public decimal TravelTotal => NumTrips * KmPerTrip * CostPerKm;
    public decimal AccommodationTotal => WorkDays * (DailyFood + DailyHotel);
    public decimal AllowanceTotal => AllowanceDays * DailyAllowance;

    public bool IsDirty { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class ProjectCostingControl : UserControl
{
    private int _projectId;
    private ProjectCostingData _data = new();
    private Dictionary<int, List<EmployeeCostLookup>> _sectionEmployeesCache = new();
    private Dictionary<string, bool> _expanderStates = new();    // gruppi
    private Dictionary<int, bool> _sectionExpanderStates = new(); // sezioni dettaglio

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private static readonly Dictionary<string, string> GroupColors = new()
    {
        { "GESTIONE", "#2563EB" }, { "PRESCHIERAMENTO", "#7C3AED" },
        { "INSTALLAZIONE", "#D97706" }, { "OPZIONE", "#DC2626" }
    };

    public ProjectCostingControl()
    {
        InitializeComponent();
    }

    private async Task LoadData()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/costing");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _data = System.Text.Json.JsonSerializer.Deserialize<ProjectCostingData>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            if (!_data.IsInitialized)
            {
                pnlInit.Visibility = Visibility.Visible;
                scrollContent.Visibility = Visibility.Collapsed;
                txtStatus.Text = "Non inizializzato";
            }
            else
            {
                pnlInit.Visibility = Visibility.Collapsed;
                scrollContent.Visibility = Visibility.Visible;
                ShowCurrentTab();
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async void BtnInit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/init", "{}");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadData();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private string _currentTab = "risorse";

    public void Load(int projectId, string tab = "risorse")
    {
        if (_projectId != projectId)
        {
            _expanderStates.Clear();
            _sectionExpanderStates.Clear();
            _sectionEmployeesCache.Clear();
        }
        _projectId = projectId;
        _currentTab = tab;
        _ = LoadData();
    }

    private void ShowCurrentTab()
    {
        if (pnlContent == null || !_data.IsInitialized) return;
        pnlContent.Children.Clear();
        switch (_currentTab)
        {
            case "risorse": RenderRisorse(); break;
            case "materiali": RenderMateriali(); break;
            case "riepilogo": RenderRiepilogo(); break;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // TAB RISORSE — Vista unificata con K per sezione
    // ══════════════════════════════════════════════════════════════
    private async void RenderRisorse()
    {
        // Precarica dipendenti per tutte le sezioni abilitate
        _sectionEmployeesCache.Clear();
        foreach (var sec in _data.CostSections.Where(s => s.IsEnabled))
        {
            var emps = await LoadEmployeesForSection(sec.Id);
            _sectionEmployeesCache[sec.Id] = emps;
        }

        // Legenda colonne
        pnlContent.Children.Add(MakeColumnLegend());

        decimal grandTotalCost = 0;
        decimal grandTotalSale = 0;

        var groups = _data.CostSections
            .Where(s => s.IsEnabled)
            .GroupBy(s => s.GroupName)
            .OrderBy(g => _data.CostSections.Where(s => s.GroupName == g.Key).Min(s => s.SortOrder));

        foreach (var group in groups)
        {
            string color = GroupColors.TryGetValue(group.Key, out string? c) ? c : "#6B7280";
            List<ProjectCostSectionDto> groupSections = group.ToList();

            pnlContent.Children.Add(MakeGroupExpander(group.Key, color, groupSections));

            grandTotalCost += groupSections.Sum(s => s.TotalCost);
            grandTotalSale += groupSections.Sum(s => s.TotalSale);
        }

        pnlContent.Children.Add(MakeTotalBarUnified("TOTALE RISORSE", grandTotalCost, grandTotalSale));
        txtStatus.Text = $"{_data.CostSections.Count(s => s.IsEnabled)} sezioni — Netto {grandTotalCost:N2} € — Vendita {grandTotalSale:N2} €";
    }

    private Border MakeColumnLegend()
    {
        Border bar = new()
        {
            Background = Brush("#F9FAFB"),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 0, 4)
        };

        Grid g = new();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Sezione
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // QTÀ
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // Costo Netto
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // K Ricarico
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // Prezzo Vendita
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Tipo

        string[] headers = { "SEZIONE", "QTÀ", "COSTO NETTO", "K RICARICO", "PREZZO VENDITA", "TIPO" };
        for (int i = 0; i < headers.Length; i++)
        {
            TextBlock tb = new()
            {
                Text = headers[i],
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#6B7280"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = i >= 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = i >= 1 ? new Thickness(0, 0, 8, 0) : new Thickness(0)
            };
            Grid.SetColumn(tb, i);
            g.Children.Add(tb);
        }

        bar.Child = g;
        return bar;
    }

    private Expander MakeGroupExpander(string name, string color, List<ProjectCostSectionDto> groupSections)
    {
        decimal groupCost = groupSections.Sum(s => s.TotalCost);
        decimal groupSale = groupSections.Sum(s => s.TotalSale);

        bool isExpanded = _expanderStates.TryGetValue(name, out bool saved) ? saved : true;

        Expander exp = new()
        {
            IsExpanded = isExpanded,
            Margin = new Thickness(0, 8, 0, 0)
        };
        if (Application.Current.TryFindResource("SmoothExpander") is Style smoothStyle)
            exp.Style = smoothStyle;

        string groupKey = name;
        exp.Expanded += (s, e) => _expanderStates[groupKey] = true;
        exp.Collapsed += (s, e) => _expanderStates[groupKey] = false;

        // Header gruppo
        Border headerBorder = new()
        {
            Background = Brush(color),
            Padding = new Thickness(12, 6, 12, 6)
        };
        DockPanel dp = new();

        TextBlock txtTotals = new()
        {
            Text = $"Netto {groupCost:N2} €  →  Vendita {groupSale:N2} €",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(txtTotals, Dock.Right);
        dp.Children.Add(txtTotals);

        dp.Children.Add(new TextBlock
        {
            Text = $"  {name}",
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        headerBorder.Child = dp;
        exp.Header = headerBorder;

        // Content: le sezioni di questo gruppo come righe sommario
        StackPanel content = new();
        foreach (var sec in groupSections.OrderBy(s => s.SortOrder))
            content.Children.Add(MakeSectionRow(sec));

        exp.Content = content;
        return exp;
    }

    // ── Riga sommario sezione con Expander dettaglio ──────────
    private Border MakeSectionRow(ProjectCostSectionDto sec)
    {
        Border card = new()
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 2)
        };

        StackPanel wrapper = new();

        // ── RIGA SOMMARIO (click per espandere) ──
        Grid summaryGrid = new()
        {
            Background = Brush("#FAFBFC"),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        // Col 0: Nome sezione + freccia
        StackPanel namePanel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 0, 8) };
        TextBlock arrow = new()
        {
            Text = "▶",
            FontSize = 9,
            Foreground = Brush("#9CA3AF"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(0)
        };
        namePanel.Children.Add(arrow);
        namePanel.Children.Add(new TextBlock
        {
            Text = sec.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1A1D26"),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(namePanel, 0);
        summaryGrid.Children.Add(namePanel);

        // Col 1: QTÀ
        TextBlock txtQty = new()
        {
            Text = sec.Resources.Count.ToString(),
            FontSize = 12,
            Foreground = Brush("#6B7280"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(txtQty, 1);
        summaryGrid.Children.Add(txtQty);

        // Col 2: Costo Netto
        TextBlock txtCost = new()
        {
            Text = $"{sec.TotalCost:N2} €",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1A1D26"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(txtCost, 2);
        summaryGrid.Children.Add(txtCost);

        // Col 3: K Ricarico (editabile)
        TextBox txtK = new()
        {
            Text = sec.MarkupValue.ToString("F3", CultureInfo.InvariantCulture),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#059669"),
            Background = Brush("#05966910"),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("#05966940"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Width = 60,
            Height = 24,
            Padding = new Thickness(2, 0, 2, 0),
            Margin = new Thickness(0, 0, 8, 0)
        };
        int sectionId = sec.Id;
        txtK.LostFocus += async (s, ev) =>
        {
            if (decimal.TryParse(txtK.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != sec.MarkupValue)
            {
                await SaveSectionMarkup(sectionId, newK);
                await LoadData();
            }
        };
        txtK.KeyDown += async (s, ev) =>
        {
            if (ev.Key == System.Windows.Input.Key.Enter)
            {
                ev.Handled = true;
                if (decimal.TryParse(txtK.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != sec.MarkupValue)
                {
                    await SaveSectionMarkup(sectionId, newK);
                    await LoadData();
                }
                Keyboard.ClearFocus();
            }
        };
        txtK.GotFocus += (s, ev) => txtK.SelectAll();
        Grid.SetColumn(txtK, 3);
        summaryGrid.Children.Add(txtK);

        // Col 4: Prezzo Vendita
        TextBlock txtSale = new()
        {
            Text = $"{sec.TotalSale:N2} €",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#4F6EF7"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(txtSale, 4);
        summaryGrid.Children.Add(txtSale);

        // Col 5: Badge tipo
        string typeColor = sec.SectionType == "DA_CLIENTE" ? "#D97706" : "#059669";
        string typeLabel = sec.SectionType == "DA_CLIENTE" ? "CLIENTE" : "SEDE";
        Border typeBadge = new()
        {
            Background = Brush(typeColor + "20"),
            Padding = new Thickness(4, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        typeBadge.Child = new TextBlock { Text = typeLabel, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brush(typeColor) };
        Grid.SetColumn(typeBadge, 5);
        summaryGrid.Children.Add(typeBadge);

        wrapper.Children.Add(summaryGrid);

        // ── DETTAGLIO RISORSE (collassabile) ──
        Border detailBorder = new()
        {
            Background = Brushes.White,
            Padding = new Thickness(8, 4, 8, 8),
            Visibility = Visibility.Collapsed
        };
        StackPanel detailPanel = new();

        DataGrid dg = MakeResourceDataGrid(sec);
        detailPanel.Children.Add(dg);

        // Subtotali DA_CLIENTE (dichiarati fuori per accesso nel CellEditEnding)
        TextBlock? txtSubOre = null;
        TextBlock? txtSubViaggi = null;
        TextBlock? txtSubAlloggio = null;
        TextBlock? txtSubIndennita = null;

        if (sec.SectionType == "DA_CLIENTE")
        {
            Border subtotalBar = new()
            {
                Background = Brush("#F9FAFB"),
                BorderBrush = Brush("#E4E7EC"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 4, 0, 0)
            };

            // Riga labels + valori
            Grid subGrid = new();
            subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            TextBlock lblTot = new()
            {
                Text = "Subtotali",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#1A1D26")
            };
            Grid.SetColumn(lblTot, 0);
            subGrid.Children.Add(lblTot);

            decimal sumOre = sec.Resources.Sum(r => r.TotalCost);
            decimal sumViaggi = sec.Resources.Sum(r => r.TravelTotal);
            decimal sumAlloggio = sec.Resources.Sum(r => r.AccommodationTotal);
            decimal sumIndennita = sec.Resources.Sum(r => r.AllowanceTotal);

            txtSubOre = MakeSubtotalCell($"Ore: {sumOre:N2} €");
            txtSubViaggi = MakeSubtotalCell($"Viaggi: {sumViaggi:N2} €");
            txtSubAlloggio = MakeSubtotalCell($"Vitto/Alloggio: {sumAlloggio:N2} €");
            txtSubIndennita = MakeSubtotalCell($"Indennità: {sumIndennita:N2} €");

            Grid.SetColumn(txtSubOre, 1);
            Grid.SetColumn(txtSubViaggi, 2);
            Grid.SetColumn(txtSubAlloggio, 3);
            Grid.SetColumn(txtSubIndennita, 4);

            subGrid.Children.Add(txtSubOre);
            subGrid.Children.Add(txtSubViaggi);
            subGrid.Children.Add(txtSubAlloggio);
            subGrid.Children.Add(txtSubIndennita);

            subtotalBar.Child = subGrid;
            detailPanel.Children.Add(subtotalBar);
        }

        // Aggiorna totali sezione su edit
        dg.CellEditEnding += async (s, ev) =>
        {
            await Task.Delay(100);
            if (ev.Row.Item is CostResourceRow row && row.Id > 0)
                await SaveResource(row);

            await Task.Delay(50);
            if (dg.ItemsSource is ObservableCollection<CostResourceRow> allRows)
            {
                decimal newCost = allRows.Sum(r => r.TotalCost + r.TravelTotal + r.AccommodationTotal + r.AllowanceTotal);
                decimal newSale = newCost * sec.MarkupValue;
                txtCost.Text = $"{newCost:N2} €";
                txtSale.Text = $"{newSale:N2} €";

                // Aggiorna subtotali DA_CLIENTE
                if (sec.SectionType == "DA_CLIENTE")
                {
                    if (txtSubOre != null) txtSubOre.Text = $"Ore: {allRows.Sum(r => r.TotalCost):N2} €";
                    if (txtSubViaggi != null) txtSubViaggi.Text = $"Viaggi: {allRows.Sum(r => r.TravelTotal):N2} €";
                    if (txtSubAlloggio != null) txtSubAlloggio.Text = $"Vitto/Alloggio: {allRows.Sum(r => r.AccommodationTotal):N2} €";
                    if (txtSubIndennita != null) txtSubIndennita.Text = $"Indennità: {allRows.Sum(r => r.AllowanceTotal):N2} €";
                }
            }
        };

        // Bottone + Risorsa
        Button btnAdd = new()
        {
            Content = "+ Risorsa",
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 11,
            Background = Brush("#4F6EF7"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        int secId = sec.Id;
        btnAdd.Click += async (s, e) =>
        {
            if (!_sectionEmployeesCache.ContainsKey(secId))
            {
                var emps = await LoadEmployeesForSection(secId);
                _sectionEmployeesCache[secId] = emps;
            }

            var allEmps = _sectionEmployeesCache[secId];
            var usedIds = sec.Resources.Where(r => r.EmployeeId.HasValue).Select(r => r.EmployeeId!.Value).ToHashSet();
            var available = allEmps.Where(e2 => !usedIds.Contains(e2.Id)).ToList();

            if (available.Count == 0)
            {
                MessageBox.Show("Tutti i dipendenti disponibili sono già assegnati a questa sezione.", "Attenzione");
                return;
            }

            var first = available.First();
            var req = new ProjectCostResourceSaveRequest
            {
                SectionId = secId,
                EmployeeId = first.Id,
                ResourceName = first.FullName,
                HourlyCost = first.HourlyCost,
                HoursPerDay = 8,
                CostPerKm = 0.90m
            };

            string json = System.Text.Json.JsonSerializer.Serialize(req,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/resources", json);
            await LoadData();
        };
        detailPanel.Children.Add(btnAdd);

        detailBorder.Child = detailPanel;
        wrapper.Children.Add(detailBorder);

        // Stato espansione sezione
        bool isDetailOpen = _sectionExpanderStates.TryGetValue(sec.Id, out bool detailSaved) && detailSaved;
        if (isDetailOpen)
        {
            detailBorder.Visibility = Visibility.Visible;
            if (arrow.RenderTransform is RotateTransform rt) rt.Angle = 90;
        }

        // Click sulla riga sommario → toggle dettaglio (ignora click su TextBox K)
        summaryGrid.MouseLeftButtonDown += (s, ev) =>
        {
            if (ev.OriginalSource is TextBox || (ev.OriginalSource as FrameworkElement)?.TemplatedParent is TextBox)
                return;

            bool nowVisible = detailBorder.Visibility == Visibility.Visible;
            detailBorder.Visibility = nowVisible ? Visibility.Collapsed : Visibility.Visible;
            _sectionExpanderStates[sec.Id] = !nowVisible;

            if (arrow.RenderTransform is RotateTransform rot)
            {
                var anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = nowVisible ? 0 : 90,
                    Duration = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase()
                };
                rot.BeginAnimation(RotateTransform.AngleProperty, anim);
            }
        };

        card.Child = wrapper;
        return card;
    }

    // Helper per cella subtotale
    private TextBlock MakeSubtotalCell(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1A1D26"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
    }

    private DataGrid MakeResourceDataGrid(ProjectCostSectionDto sec)
    {
        DataGrid dg = new()
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Brush("#F3F4F6"),
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            RowHeight = 32,
            ColumnHeaderHeight = 28,
            FontSize = 12,
            SelectionMode = DataGridSelectionMode.Single,
            Margin = new Thickness(0)
        };

        Style headerStyle = new(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#F9FAFB")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#6B7280")));
        headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 10.0));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 0, 6, 0)));
        dg.ColumnHeaderStyle = headerStyle;

        Style cellStyle = new(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(DataGridCell.VerticalAlignmentProperty, VerticalAlignment.Center));
        dg.CellStyle = cellStyle;

        // Col 0: RISORSA — ComboBox con dipendenti
        DataGridTemplateColumn empCol = new() { Header = "RISORSA", Width = new DataGridLength(1, DataGridLengthUnitType.Star) };

        FrameworkElementFactory displayFactory = new(typeof(TextBlock));
        displayFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("ResourceName"));
        displayFactory.SetValue(TextBlock.PaddingProperty, new Thickness(6, 0, 0, 0));
        displayFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        empCol.CellTemplate = new DataTemplate { VisualTree = displayFactory };

        FrameworkElementFactory comboFactory = new(typeof(ComboBox));
        comboFactory.SetValue(ComboBox.DisplayMemberPathProperty, "FullName");
        comboFactory.SetValue(ComboBox.FontSizeProperty, 12.0);
        comboFactory.SetValue(ComboBox.BorderThicknessProperty, new Thickness(0));
        comboFactory.AddHandler(ComboBox.LoadedEvent, new RoutedEventHandler((s, ev) =>
        {
            if (s is not ComboBox combo) return;
            if (combo.DataContext is not CostResourceRow row) return;

            int sectionId = row.SectionId;
            if (!_sectionEmployeesCache.TryGetValue(sectionId, out var allEmployees))
                return;

            var usedIds = sec.Resources.Where(r => r.EmployeeId.HasValue && r.Id != row.Id).Select(r => r.EmployeeId!.Value).ToHashSet();
            var available = allEmployees.Where(e => !usedIds.Contains(e.Id)).ToList();

            combo.ItemsSource = available;

            if (row.EmployeeId.HasValue)
                combo.SelectedItem = available.FirstOrDefault(e => e.Id == row.EmployeeId);
        }));
        comboFactory.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(async (s, ev) =>
        {
            if (s is not ComboBox combo) return;
            if (combo.DataContext is not CostResourceRow row) return;
            if (combo.SelectedItem is not EmployeeCostLookup emp) return;

            row.EmployeeId = emp.Id;
            row.ResourceName = emp.FullName;
            row.HourlyCost = emp.HourlyCost;

            if (row.Id > 0)
                await SaveResource(row);
        }));
        empCol.CellEditingTemplate = new DataTemplate { VisualTree = comboFactory };
        dg.Columns.Add(empCol);

        // Altre colonne
        dg.Columns.Add(new DataGridTextColumn { Header = "GG", Binding = new System.Windows.Data.Binding("WorkDays") { StringFormat = "F0" }, Width = 50 });
        dg.Columns.Add(new DataGridTextColumn { Header = "ORE/G", Binding = new System.Windows.Data.Binding("HoursPerDay") { StringFormat = "F0" }, Width = 55 });
        dg.Columns.Add(new DataGridTextColumn { Header = "TOT ORE", Binding = new System.Windows.Data.Binding("TotalHours") { StringFormat = "F1" }, Width = 65, IsReadOnly = true });
        dg.Columns.Add(new DataGridTextColumn { Header = "€/H", Binding = new System.Windows.Data.Binding("HourlyCost") { StringFormat = "F2" }, Width = 60, IsReadOnly = true });
        dg.Columns.Add(new DataGridTextColumn { Header = "TOT €", Binding = new System.Windows.Data.Binding("TotalCost") { StringFormat = "N2" }, Width = 80, IsReadOnly = true });

        if (sec.SectionType == "DA_CLIENTE")
        {
            dg.Columns.Add(new DataGridTextColumn { Header = "VIAGGI", Binding = new System.Windows.Data.Binding("NumTrips"), Width = 50 });
            dg.Columns.Add(new DataGridTextColumn { Header = "KM", Binding = new System.Windows.Data.Binding("KmPerTrip") { StringFormat = "F0" }, Width = 50 });
            dg.Columns.Add(new DataGridTextColumn { Header = "€/KM", Binding = new System.Windows.Data.Binding("CostPerKm") { StringFormat = "F2" }, Width = 50 });
            dg.Columns.Add(new DataGridTextColumn { Header = "VITTO/G", Binding = new System.Windows.Data.Binding("DailyFood") { StringFormat = "F0" }, Width = 55 });
            dg.Columns.Add(new DataGridTextColumn { Header = "HOTEL/G", Binding = new System.Windows.Data.Binding("DailyHotel") { StringFormat = "F0" }, Width = 55 });
            dg.Columns.Add(new DataGridTextColumn { Header = "GG IND.", Binding = new System.Windows.Data.Binding("AllowanceDays") { StringFormat = "F0" }, Width = 55 });

            // ComboBox indennità
            DataGridTemplateColumn allowCol = new() { Header = "€/G IND.", Width = 70 };

            FrameworkElementFactory allowDisplay = new(typeof(TextBlock));
            allowDisplay.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DailyAllowance") { StringFormat = "F0" });
            allowDisplay.SetValue(TextBlock.PaddingProperty, new Thickness(6, 0, 0, 0));
            allowDisplay.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            allowCol.CellTemplate = new DataTemplate { VisualTree = allowDisplay };

            FrameworkElementFactory allowCombo = new(typeof(ComboBox));
            allowCombo.SetValue(ComboBox.FontSizeProperty, 12.0);
            allowCombo.SetValue(ComboBox.BorderThicknessProperty, new Thickness(0));
            allowCombo.SetValue(ComboBox.VerticalAlignmentProperty, VerticalAlignment.Center);
            allowCombo.SetValue(ComboBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            allowCombo.SetValue(ComboBox.PaddingProperty, new Thickness(4, 0, 4, 0));
            allowCombo.AddHandler(ComboBox.LoadedEvent, new RoutedEventHandler((s, ev) =>
            {
                if (s is not ComboBox combo) return;
                combo.ItemsSource = new[] { 0m, 20m, 40m, 60m };
                if (combo.DataContext is CostResourceRow row)
                    combo.SelectedItem = row.DailyAllowance;
            }));
            allowCombo.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(async (s, ev) =>
            {
                if (s is not ComboBox combo) return;
                if (combo.DataContext is not CostResourceRow row) return;
                if (combo.SelectedItem is decimal val)
                {
                    row.DailyAllowance = val;
                    if (row.Id > 0)
                        await SaveResource(row);
                }
            }));
            allowCol.CellEditingTemplate = new DataTemplate { VisualTree = allowCombo };
            dg.Columns.Add(allowCol);
        }

        // Bottone elimina
        DataGridTemplateColumn delCol = new() { Header = "", Width = 30 };
        FrameworkElementFactory btnFactory = new(typeof(Button));
        btnFactory.SetValue(Button.ContentProperty, "✕");
        btnFactory.SetValue(Button.WidthProperty, 20.0);
        btnFactory.SetValue(Button.HeightProperty, 20.0);
        btnFactory.SetValue(Button.FontSizeProperty, 9.0);
        btnFactory.SetValue(Button.BackgroundProperty, Brush("#EF44441A"));
        btnFactory.SetValue(Button.ForegroundProperty, Brush("#EF4444"));
        btnFactory.SetValue(Button.BorderThicknessProperty, new Thickness(0));
        btnFactory.SetValue(Button.CursorProperty, System.Windows.Input.Cursors.Hand);
        btnFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(async (s, ev) =>
        {
            if (s is Button btn && btn.DataContext is CostResourceRow row && row.Id > 0)
            {
                await ApiClient.DeleteAsync($"/api/projects/{_projectId}/costing/resources/{row.Id}");
                await LoadData();
            }
        }));
        delCol.CellTemplate = new DataTemplate { VisualTree = btnFactory };
        dg.Columns.Add(delCol);

        // Popola dati
        ObservableCollection<CostResourceRow> rows = new();
        foreach (var res in sec.Resources.OrderBy(r => r.SortOrder))
        {
            rows.Add(new CostResourceRow
            {
                Id = res.Id,
                SectionId = res.SectionId,
                EmployeeId = res.EmployeeId,
                ResourceName = res.ResourceName,
                WorkDays = res.WorkDays,
                HoursPerDay = res.HoursPerDay,
                HourlyCost = res.HourlyCost,
                NumTrips = res.NumTrips,
                KmPerTrip = res.KmPerTrip,
                CostPerKm = res.CostPerKm,
                DailyFood = res.DailyFood,
                DailyHotel = res.DailyHotel,
                AllowanceDays = res.AllowanceDays,
                DailyAllowance = res.DailyAllowance
            });
        }
        dg.ItemsSource = rows;
        return dg;
    }

    // ══════════════════════════════════════════════════════════════
    // SAVE HELPERS
    // ══════════════════════════════════════════════════════════════

    private async Task SaveResource(CostResourceRow row)
    {
        try
        {
            var req = new ProjectCostResourceSaveRequest
            {
                Id = row.Id,
                SectionId = row.SectionId,
                EmployeeId = row.EmployeeId,
                ResourceName = row.ResourceName,
                WorkDays = row.WorkDays,
                HoursPerDay = row.HoursPerDay,
                HourlyCost = row.HourlyCost,
                NumTrips = row.NumTrips,
                KmPerTrip = row.KmPerTrip,
                CostPerKm = row.CostPerKm,
                DailyFood = row.DailyFood,
                DailyHotel = row.DailyHotel,
                AllowanceDays = row.AllowanceDays,
                DailyAllowance = row.DailyAllowance
            };
            string json = System.Text.Json.JsonSerializer.Serialize(req,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"/api/projects/{_projectId}/costing/resources/{row.Id}", json);
        }
        catch { }
    }

    private async Task SaveSectionMarkup(int sectionId, decimal markupValue)
    {
        try
        {
            var req = new { Field = "markup_value", Value = markupValue.ToString(CultureInfo.InvariantCulture) };
            string json = System.Text.Json.JsonSerializer.Serialize(req,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            await ApiClient.PatchAsync($"/api/projects/{_projectId}/costing/sections/{sectionId}/field", json);
        }
        catch { }
    }

    private async Task<List<EmployeeCostLookup>> LoadEmployeesForSection(int sectionId)
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/costing/sections/{sectionId}/employees");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                return System.Text.Json.JsonSerializer.Deserialize<List<EmployeeCostLookup>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { }
        return new();
    }

    // ══════════════════════════════════════════════════════════════
    // TAB MATERIALI (invariato — placeholder per ora)
    // ══════════════════════════════════════════════════════════════
    private void RenderMateriali()
    {
        decimal grandTotalCost = 0;
        decimal grandTotalSale = 0;

        foreach (var sec in _data.MaterialSections.Where(s => s.IsEnabled).OrderBy(s => s.SortOrder))
        {
            pnlContent.Children.Add(MakeMaterialSectionWithGrid(sec));
            grandTotalCost += sec.TotalCost;
            grandTotalSale += sec.TotalSale;
        }

        pnlContent.Children.Add(MakeTotalBarUnified("TOTALE MATERIALI", grandTotalCost, grandTotalSale));
        txtStatus.Text = $"{_data.MaterialSections.Count(s => s.IsEnabled)} categorie — Costo {grandTotalCost:N2} € — Vendita {grandTotalSale:N2} €";
    }

    private Border MakeMaterialSectionWithGrid(ProjectMaterialSectionDto sec)
    {
        Border card = new()
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 4)
        };

        StackPanel sp = new();

        // Header
        DockPanel headerDp = new() { Background = Brush("#F9FAFB") };

        Button btnAdd = new()
        {
            Content = "+ Voce",
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 11,
            Background = Brush("#4F6EF7"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 6, 8, 6)
        };
        DockPanel.SetDock(btnAdd, Dock.Right);

        Border kBadge = new()
        {
            Background = Brush("#05966920"),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        kBadge.Child = new TextBlock { Text = $"K {sec.MarkupValue:F2}", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brush("#059669") };
        DockPanel.SetDock(kBadge, Dock.Right);

        TextBlock txtTotals = new()
        {
            Text = $"Costo {sec.TotalCost:N2} €  →  Vendita {sec.TotalSale:N2} €",
            FontSize = 11,
            Foreground = Brush("#4F6EF7"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        DockPanel.SetDock(txtTotals, Dock.Right);

        headerDp.Children.Add(btnAdd);
        headerDp.Children.Add(kBadge);
        headerDp.Children.Add(txtTotals);

        TextBlock txtName = new()
        {
            Text = sec.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1A1D26"),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 8, 0, 8)
        };
        headerDp.Children.Add(txtName);
        sp.Children.Add(headerDp);

        // DataGrid materiali
        DataGrid dg = new()
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Brush("#F3F4F6"),
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            RowHeight = 32,
            ColumnHeaderHeight = 28,
            FontSize = 12,
            SelectionMode = DataGridSelectionMode.Single,
            Margin = new Thickness(8, 0, 8, 8)
        };

        Style headerStyle = new(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#F9FAFB")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#6B7280")));
        headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 10.0));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 0, 6, 0)));
        dg.ColumnHeaderStyle = headerStyle;

        dg.Columns.Add(new DataGridTextColumn { Header = "DESCRIZIONE", Binding = new System.Windows.Data.Binding("Description"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { Header = "QTÀ", Binding = new System.Windows.Data.Binding("Quantity") { StringFormat = "F0" }, Width = 60 });
        dg.Columns.Add(new DataGridTextColumn { Header = "COSTO UNIT.", Binding = new System.Windows.Data.Binding("UnitCost") { StringFormat = "N2" }, Width = 90 });
        dg.Columns.Add(new DataGridTextColumn { Header = "TOTALE", Binding = new System.Windows.Data.Binding("TotalCost") { StringFormat = "N2" }, Width = 90, IsReadOnly = true });

        DataGridTemplateColumn delCol = new() { Header = "", Width = 30 };
        FrameworkElementFactory btnFactory = new(typeof(Button));
        btnFactory.SetValue(Button.ContentProperty, "✕");
        btnFactory.SetValue(Button.WidthProperty, 20.0);
        btnFactory.SetValue(Button.HeightProperty, 20.0);
        btnFactory.SetValue(Button.FontSizeProperty, 9.0);
        btnFactory.SetValue(Button.BackgroundProperty, Brush("#EF44441A"));
        btnFactory.SetValue(Button.ForegroundProperty, Brush("#EF4444"));
        btnFactory.SetValue(Button.BorderThicknessProperty, new Thickness(0));
        btnFactory.SetValue(Button.CursorProperty, System.Windows.Input.Cursors.Hand);
        btnFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(async (s, ev) =>
        {
            if (s is Button btn && btn.DataContext is MaterialItemRow row && row.Id > 0)
            {
                await ApiClient.DeleteAsync($"/api/projects/{_projectId}/costing/material-items/{row.Id}");
                await LoadData();
            }
        }));
        delCol.CellTemplate = new DataTemplate { VisualTree = btnFactory };
        dg.Columns.Add(delCol);

        ObservableCollection<MaterialItemRow> rows = new();
        foreach (var item in sec.Items.OrderBy(i => i.SortOrder))
            rows.Add(new MaterialItemRow { Id = item.Id, SectionId = item.SectionId, Description = item.Description, Quantity = item.Quantity, UnitCost = item.UnitCost });
        dg.ItemsSource = rows;

        dg.CellEditEnding += async (s, ev) =>
        {
            await Task.Delay(100);
            if (ev.Row.Item is MaterialItemRow row && row.Id > 0)
                await SaveMaterialItem(row);
        };

        int secId = sec.Id;
        btnAdd.Click += async (s, e) =>
        {
            var req = new ProjectMaterialItemSaveRequest { SectionId = secId, Description = "Nuova voce", Quantity = 1, UnitCost = 0 };
            string json = System.Text.Json.JsonSerializer.Serialize(req,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/material-items", json);
            await LoadData();
        };

        sp.Children.Add(dg);
        card.Child = sp;
        return card;
    }

    private async Task SaveMaterialItem(MaterialItemRow row)
    {
        try
        {
            var req = new ProjectMaterialItemSaveRequest
            {
                Id = row.Id,
                SectionId = row.SectionId,
                Description = row.Description,
                Quantity = row.Quantity,
                UnitCost = row.UnitCost
            };
            string json = System.Text.Json.JsonSerializer.Serialize(req,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"/api/projects/{_projectId}/costing/material-items/{row.Id}", json);
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════
    // TAB RIEPILOGO — usa TotalSale per sezione
    // ══════════════════════════════════════════════════════════════
    private void RenderRiepilogo()
    {
        decimal totalResourceCost = _data.CostSections.Where(s => s.IsEnabled).Sum(s => s.TotalCost);
        decimal totalResourceSale = _data.CostSections.Where(s => s.IsEnabled).Sum(s => s.TotalSale);

        decimal totalMaterialCost = _data.MaterialSections.Where(s => s.IsEnabled).Sum(s => s.TotalCost);
        decimal totalMaterialSale = _data.MaterialSections.Where(s => s.IsEnabled).Sum(s => s.TotalSale);

        decimal totalCost = totalResourceCost + totalMaterialCost;
        decimal netPrice = totalResourceSale + totalMaterialSale;

        var p = _data.Pricing;
        decimal structureCosts = netPrice * p.StructureCostsPct;
        decimal contingency = netPrice * p.ContingencyPct;
        decimal riskWarranty = netPrice * p.RiskWarrantyPct;
        decimal offerPrice = netPrice + structureCosts + contingency + riskWarranty;
        decimal negotiationMargin = netPrice * p.NegotiationMarginPct;
        decimal finalPrice = offerPrice + negotiationMargin;

        AddSummaryRow("COSTO RISORSE (ore + trasferte)", totalResourceCost, null);
        AddSummaryRow("COSTO MATERIALI", totalMaterialCost, null);
        AddSeparator();
        AddSummaryRow("TOTALE COSTI", totalCost, null, true);
        pnlContent.Children.Add(new Border { Height = 16 });
        AddSummaryRow("VENDITA RISORSE (con K per sezione)", null, totalResourceSale);
        AddSummaryRow("VENDITA MATERIALI (con K)", null, totalMaterialSale);
        AddSeparator();
        AddSummaryRow("NET PRICE", null, netPrice, true);
        pnlContent.Children.Add(new Border { Height = 16 });
        AddSummaryRow($"Costi fissi struttura ({p.StructureCostsPct * 100:F1}%)", null, structureCosts);
        AddSummaryRow($"Contingency ({p.ContingencyPct * 100:F1}%)", null, contingency);
        AddSummaryRow($"Rischi & Garanzie ({p.RiskWarrantyPct * 100:F1}%)", null, riskWarranty);
        AddSeparator();
        AddSummaryRow("OFFER PRICE", null, offerPrice, true);
        pnlContent.Children.Add(new Border { Height = 8 });
        AddSummaryRow($"Margine trattativa ({p.NegotiationMarginPct * 100:F1}%)", null, negotiationMargin);
        AddSeparator();
        AddSummaryRow("FINAL OFFER PRICE", null, finalPrice, true, "#4F6EF7");

        decimal margin = totalCost > 0 ? (finalPrice - totalCost) / totalCost * 100 : 0;
        txtStatus.Text = $"Costo {totalCost:N2} € → Prezzo finale {finalPrice:N2} € — Margine {margin:N1}%";
    }

    // ══════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════

    private void AddSummaryRow(string label, decimal? costValue, decimal? saleValue, bool bold = false, string? color = null)
    {
        Grid g = new() { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        FontWeight fw = bold ? FontWeights.Bold : FontWeights.Normal;
        double fs = bold ? 14 : 13;
        string fg = color ?? "#1A1D26";

        TextBlock tbLabel = new() { Text = label, FontSize = fs, FontWeight = fw, Foreground = Brush(fg) };
        Grid.SetColumn(tbLabel, 0); g.Children.Add(tbLabel);

        if (costValue.HasValue)
        {
            TextBlock tbCost = new() { Text = $"{costValue.Value:N2} €", FontSize = fs, FontWeight = fw, Foreground = Brush("#DC2626"), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(tbCost, 1); g.Children.Add(tbCost);
        }
        if (saleValue.HasValue)
        {
            TextBlock tbSale = new() { Text = $"{saleValue.Value:N2} €", FontSize = fs, FontWeight = fw, Foreground = Brush(color ?? "#059669"), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(tbSale, 2); g.Children.Add(tbSale);
        }
        pnlContent.Children.Add(g);
    }

    private void AddSeparator() => pnlContent.Children.Add(new Border { Height = 1, Background = Brush("#E4E7EC"), Margin = new Thickness(0, 4, 0, 4) });

    private Border MakeTotalBarUnified(string label, decimal cost, decimal sale)
    {
        Border bar = new() { Background = Brush("#1A1D26"), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 8, 0, 0) };
        StackPanel sp = new() { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Width = 200 });
        sp.Children.Add(new TextBlock { Text = $"Netto: {cost:N2} €", FontSize = 13, Foreground = Brush("#9CA3AF"), Margin = new Thickness(20, 0, 0, 0) });
        sp.Children.Add(new TextBlock { Text = $"Vendita: {sale:N2} €", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush("#4F6EF7"), Margin = new Thickness(20, 0, 0, 0) });
        bar.Child = sp; return bar;
    }
}

// ViewModel per riga materiale
public class MaterialItemRow : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int SectionId { get; set; }

    private string _description = "";
    public string Description { get => _description; set { _description = value; Notify(); } }

    private decimal _quantity = 1;
    public decimal Quantity { get => _quantity; set { _quantity = value; Notify(); Notify(nameof(TotalCost)); } }

    private decimal _unitCost;
    public decimal UnitCost { get => _unitCost; set { _unitCost = value; Notify(); Notify(nameof(TotalCost)); } }

    public decimal TotalCost => Quantity * UnitCost;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
