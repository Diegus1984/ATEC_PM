using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.CashFlow;

public partial class CashFlowControl : UserControl
{
    private bool _paymentSaving;
    private int _projectId;
    private CashFlowViewModel _vm = new();
    private OxyPlotChartHost? _chartHost;
    private FrameworkElement? _chartMarginTarget;

    public CashFlowControl()
    {
        InitializeComponent();
        Unloaded += (_, _) => TeardownChart();
    }

    public async void Load(int projectId)
    {
        _projectId = projectId;
        try
        {
            CashFlowData data = await ApiClient.GetDataAsync<CashFlowData>(
                $"/api/projects/{_projectId}/cashflow") ?? new();

            _vm = CashFlowViewModel.FromData(data);
            DataContext = _vm;

            if (_vm.IsInitialized)
                BuildMonthColumns();

            txtLoading.Visibility = Visibility.Collapsed;

            // Grafico OxyPlot dopo griglia visibile (non blocca il primo paint)
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(EnsureChart));
        }
        catch (Exception ex) { txtLoading.Text = $"Errore: {ex.Message}"; }
    }

    private void EnsureChart()
    {
        if (!_vm.IsInitialized || _chartHost != null)
            return;

        _chartHost = new OxyPlotChartHost(chartHost);
        _chartMarginTarget = _chartHost.EnsurePlotView(_vm.PlotModel, height: 220);
        _vm.ApplyChart();
        _chartHost.UpdateModel(_vm.PlotModel);
    }

    private void TeardownChart()
    {
        _chartHost?.Teardown();
        _chartHost = null;
        _chartMarginTarget = null;
    }

    private void RefreshChartIfReady()
    {
        _vm.Recalculate();
        if (_chartHost == null)
            return;

        _vm.ApplyChart();
        _chartHost.UpdateModel(_vm.PlotModel);
    }

    private void AmountTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && tb.DataContext is CfGridRow row && row.IsAmountEditable)
        {
            e.Handled = true;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            SaveCategoryAmount(row);
            tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
        }
    }

    private void AmountTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is CfGridRow row && row.IsAmountEditable)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            SaveCategoryAmount(row);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // CATEGORIE
    // ═══════════════════════════════════════════════════════════════
    private async void BtnAddCategory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var req = new { name = "Nuova categoria", totalAmount = 0m, notes = "" };
            await ApiClient.PostAsync($"/api/projects/{_projectId}/cashflow/categories",
                JsonSerializer.Serialize(req));
            Load(_projectId);
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════════
    // INIT
    // ═══════════════════════════════════════════════════════════════
    private async void BtnInit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            decimal defaultAmt = _vm.ProjectRevenue;
            string json = JsonSerializer.Serialize(new { paymentAmount = defaultAmt, monthCount = 13 });
            await ApiClient.PostAsync($"/api/projects/{_projectId}/cashflow/init", json);
            TeardownChart();
            Load(_projectId);
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnDeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not CfGridRow row) return;
        string displayName = row.Label.StartsWith("[M] ") ? row.Label[4..] : row.Label;
        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Eliminare '{displayName}'?", "Conferma", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            await ApiClient.DeleteAsync($"/api/projects/{_projectId}/cashflow/categories/{row.RefId}");
            Load(_projectId);
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    private DataTemplate BuildCellTemplate(string bindingPath)
    {
        DataTemplate template = new DataTemplate();

        FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetBinding(Border.BackgroundProperty, new Binding("CellColor")
        {
            Converter = (IValueConverter)Resources["RowTypeToBg"]
        });

        FrameworkElementFactory tbFactory = new FrameworkElementFactory(typeof(TextBox));
        tbFactory.SetBinding(TextBox.TextProperty, new Binding(bindingPath)
        {
            StringFormat = "N0",
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
        });
        tbFactory.SetValue(TextBox.FontSizeProperty, 11.0);
        tbFactory.SetValue(TextBox.PaddingProperty, new Thickness(2, 0, 2, 0));
        tbFactory.SetValue(TextBox.HorizontalContentAlignmentProperty, HorizontalAlignment.Right);
        tbFactory.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        tbFactory.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
        tbFactory.SetBinding(TextBox.BackgroundProperty, new Binding("CellColor")
        {
            Converter = (IValueConverter)Resources["RowTypeToBg"]
        });
        tbFactory.SetBinding(TextBox.IsReadOnlyProperty, new Binding("IsEditable")
        {
            Converter = new InvertBoolConverter()
        });
        tbFactory.SetBinding(TextBox.ForegroundProperty, new Binding(bindingPath)
        {
            Converter = (IValueConverter)Resources["CellForegroundConverter"]
        });
        tbFactory.AddHandler(TextBox.LostFocusEvent, new RoutedEventHandler(CellTextBox_LostFocus));
        tbFactory.AddHandler(TextBox.KeyDownEvent, new KeyEventHandler(CellTextBox_KeyDown));

        borderFactory.AppendChild(tbFactory);
        template.VisualTree = borderFactory;
        return template;
    }

    // ═══════════════════════════════════════════════════════════════
    // GENERA COLONNE MESE — TextBox sempre visibili
    // ═══════════════════════════════════════════════════════════════
    private void BuildMonthColumns()
    {
        while (dgMain.Columns.Count > 2)
            dgMain.Columns.RemoveAt(dgMain.Columns.Count - 1);

        for (int m = 0; m < _vm.MonthCount; m++)
        {
            string header = $"{m + 1}\n{_vm.MonthLabels[m]}";

            DataGridTemplateColumn col = new DataGridTemplateColumn
            {
                Header = header,
                Width = 80,
                CellTemplate = BuildCellTemplate($"Values[{m}]")
            };
            dgMain.Columns.Add(col);
        }

        dgMain.LayoutUpdated -= DgMain_LayoutUpdated;
        dgMain.LayoutUpdated += DgMain_LayoutUpdated;
    }

    private void DgMain_LayoutUpdated(object? sender, EventArgs e)
    {
        try
        {
            if (_chartMarginTarget == null || dgMain.Columns.Count < 3) return;

            double colA = dgMain.Columns[0].ActualWidth;

            double monthsWidth = 0;
            for (int i = 2; i < dgMain.Columns.Count; i++)
                monthsWidth += dgMain.Columns[i].ActualWidth;

            double colB = dgMain.Columns[1].ActualWidth;
            double dgVisible = dgMain.ActualWidth;
            double rightMargin = Math.Max(0, dgVisible - colA - colB - monthsWidth);

            _chartMarginTarget.Margin = new Thickness(colA, 8, rightMargin, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error in DgMain_LayoutUpdated: {ex}");
        }
    }

    private void CellTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && tb.DataContext is CfGridRow row && row.IsEditable)
        {
            e.Handled = true;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            SaveRowData(row, tb);
            tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // CELLE — salvataggio su LostFocus e Enter
    // ═══════════════════════════════════════════════════════════════
    private void CellTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is CfGridRow row && row.IsEditable)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            SaveRowData(row, tb);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PAGAMENTO CLIENTE
    // ═══════════════════════════════════════════════════════════════
    private void PaymentAmount_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            _paymentSaving = true;
            _ = SavePaymentAndRefresh();
        }
    }

    private async void PaymentAmount_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_paymentSaving) { _paymentSaving = false; return; }
        if (sender is TextBox tb)
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        await SavePaymentAndRefresh();
    }

    private void LabelTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && tb.DataContext is CfGridRow row && row.IsLabelEditable)
        {
            e.Handled = true;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            SaveCategoryLabel(row);
            tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
        }
    }

    private void LabelTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is CfGridRow row && row.IsLabelEditable)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            SaveCategoryLabel(row);
        }
    }

    private void SaveCategoryLabel(CfGridRow row)
    {
        if (row.RefId <= 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var req = new { name = row.Label, totalAmount = row.TotalAmount, notes = "" };
                await ApiClient.PutAsync($"/api/projects/{_projectId}/cashflow/categories/{row.RefId}",
                    JsonSerializer.Serialize(req));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Error saving category label: {ex}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore durante il salvataggio dell'etichetta della categoria: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        });
    }

    private void SaveCategoryAmount(CfGridRow row)
    {
        if (row.RefId <= 0 || row.IsLinked) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var req = new { name = row.Label, totalAmount = row.TotalAmount, notes = "" };
                await ApiClient.PutAsync($"/api/projects/{_projectId}/cashflow/categories/{row.RefId}",
                    JsonSerializer.Serialize(req));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Error saving category amount: {ex}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore durante il salvataggio dell'importo della categoria: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        });
        RefreshChartIfReady();
    }

    private async Task SaveData(string dataType, int refId, int monthNumber, decimal numValue)
    {
        try
        {
            var req = new { dataType, refId, monthNumber, numValue, dateValue = (DateTime?)null };
            await ApiClient.PutAsync($"/api/projects/{_projectId}/cashflow/data",
                JsonSerializer.Serialize(req));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error in SaveData: {ex}");
            Application.Current.Dispatcher.Invoke(() =>
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore durante il salvataggio dei dati del flusso di cassa: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    private bool _startDateSaving;

    private void DpStartDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_startDateSaving) return;
        _startDateSaving = true;
        _ = SaveHeaderAndReload();
    }

    private async Task SaveHeaderAndReload()
    {
        try
        {
            await SavePaymentAndRefresh();
            TeardownChart();
            Load(_projectId);
        }
        finally
        {
            _startDateSaving = false;
        }
    }

    private async Task SavePaymentAndRefresh()
    {
        try
        {
            var req = new { paymentAmount = _vm.PaymentAmount, monthCount = _vm.MonthCount, startDate = _vm.StartDate };
            await ApiClient.PutAsync($"/api/projects/{_projectId}/cashflow/header",
                JsonSerializer.Serialize(req));
            RefreshChartIfReady();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error saving payment/header: {ex}");
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore durante il salvataggio dell'importo o dell'intestazione: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveRowData(CfGridRow row, TextBox tb)
    {
        BindingExpression? be = tb.GetBindingExpression(TextBox.TextProperty);
        string path = be?.ParentBinding?.Path?.Path ?? "";
        Match match = Regex.Match(path, @"Values\[(\d+)\]");
        if (!match.Success) return;

        int monthIndex = int.Parse(match.Groups[1].Value);
        int monthNumber = monthIndex + 1;

        string dataType = row.RowType switch
        {
            CfRowType.IncomePct => "INCOME_PCT",
            CfRowType.Adjustment => "ADJUSTMENT",
            CfRowType.CategoryPct => "CAT_PCT",
            CfRowType.Bank => "BANK",
            _ => ""
        };
        if (string.IsNullOrEmpty(dataType)) return;

        int refId = row.RowType == CfRowType.CategoryPct ? row.RefId : 0;
        _ = SaveData(dataType, refId, monthNumber, row.Values[monthIndex]);
        RefreshChartIfReady();
    }
}
