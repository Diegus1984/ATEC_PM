using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.CashFlow;

public partial class CashFlowControl : UserControl
{
    private bool _paymentSaving;
    private int _projectId;
    private CashFlowViewModel _vm = new();

    public CashFlowControl()
    {
        InitializeComponent();
    }

    public async void Load(int projectId)
    {
        _projectId = projectId;
        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/cashflow");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            CashFlowData data = JsonSerializer.Deserialize<CashFlowData>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            _vm = CashFlowViewModel.FromData(data);
            DataContext = _vm;

            if (_vm.IsInitialized)
                BuildMonthColumns();

            txtLoading.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { txtLoading.Text = $"Errore: {ex.Message}"; }
    }

    // ═══════════════════════════════════════════════════════════════
    // GENERA COLONNE MESE
    // ═══════════════════════════════════════════════════════════════
    private void BuildMonthColumns()
    {
        while (dgMain.Columns.Count > 2)
            dgMain.Columns.RemoveAt(dgMain.Columns.Count - 1);

        for (int m = 0; m < _vm.MonthCount; m++)
        {
            string header = $"{m + 1}\n{_vm.MonthLabels[m]}";
            string bindingPath = $"Values[{m}]";

            var col = new DataGridTemplateColumn
            {
                Header = header,
                Width = 80
            };

            col.CellTemplate = BuildCellTemplate(bindingPath, isEditing: false);
            col.CellEditingTemplate = BuildCellTemplate(bindingPath, isEditing: true);
            dgMain.Columns.Add(col);
        }
    }

    private DataTemplate BuildCellTemplate(string bindingPath, bool isEditing)
    {
        var template = new DataTemplate();

        if (isEditing)
        {
            var tbFactory = new FrameworkElementFactory(typeof(TextBox));
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
            template.VisualTree = tbFactory;
        }
        else
        {
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetBinding(Border.BackgroundProperty, new Binding("CellColor")
            {
                Converter = (IValueConverter)Resources["RowTypeToBg"]
            });
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(4, 0, 4, 0));

            var txtFactory = new FrameworkElementFactory(typeof(TextBlock));
            var multi = new MultiBinding
            {
                Converter = (IMultiValueConverter)FindResource("SepValueConv")
            };
            multi.Bindings.Add(new Binding(bindingPath));
            multi.Bindings.Add(new Binding("IsSeparator"));
            txtFactory.SetBinding(TextBlock.TextProperty, multi);
            txtFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
            txtFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            txtFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            txtFactory.SetBinding(TextBlock.ForegroundProperty, new Binding(bindingPath)
            {
                Converter = (IValueConverter)Resources["CellForegroundConverter"]
            });

            borderFactory.AppendChild(txtFactory);
            template.VisualTree = borderFactory;
        }

        return template;
    }

    // ═══════════════════════════════════════════════════════════════
    // CELLA — click singolo, tastiera, enter
    // ═══════════════════════════════════════════════════════════════
    private void DataGridCell_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGridCell cell && !cell.IsEditing && !cell.IsReadOnly)
        {
            cell.Focus();
            dgMain.BeginEdit();
        }
    }

    private void DataGridCell_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (sender is DataGridCell cell && !cell.IsEditing && !cell.IsReadOnly)
        {
            cell.Focus();
            dgMain.BeginEdit();

            // Passa il carattere digitato alla TextBox
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var textBox = FindVisualChild<TextBox>(cell);
                if (textBox != null)
                {
                    textBox.Text = e.Text;
                    textBox.CaretIndex = textBox.Text.Length;
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void DataGridCell_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is DataGridCell cell)
        {
            // In editing + Enter → conferma
            if (cell.IsEditing && e.Key == System.Windows.Input.Key.Enter)
            {
                dgMain.CommitEdit();
                e.Handled = true;
                return;
            }

            // Non in editing → F2, Back, Delete entrano in edit
            if (!cell.IsEditing && !cell.IsReadOnly)
            {
                if (e.Key == System.Windows.Input.Key.F2 ||
                    e.Key == System.Windows.Input.Key.Back ||
                    e.Key == System.Windows.Input.Key.Delete)
                {
                    cell.Focus();
                    dgMain.BeginEdit();
                }
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    // BEGINNING EDIT — blocca celle non editabili
    // ═══════════════════════════════════════════════════════════════
    private void DgMain_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is CfGridRow row)
        {
            int colIndex = e.Column.DisplayIndex;

            if (colIndex == 1)
            {
                bool canEditAmount = row.RowType is CfRowType.CategoryAmount or CfRowType.Payment;
                if (!canEditAmount) e.Cancel = true;
                return;
            }

            if (colIndex >= 2 && !row.IsEditable)
                e.Cancel = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // CELL EDIT ENDING — salva dato
    // ═══════════════════════════════════════════════════════════════
    private async void DgMain_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        await Task.Delay(100);
        if (e.Row.Item is not CfGridRow row) return;

        int colIndex = e.Column.DisplayIndex;

        // Colonna B — importo categoria
        if (colIndex == 1 && row.RowType == CfRowType.CategoryAmount && row.RefId > 0)
        {
            try
            {
                var req = new { name = row.Label, totalAmount = row.TotalAmount, notes = "" };
                await ApiClient.PutAsync($"/api/projects/{_projectId}/cashflow/categories/{row.RefId}",
                    JsonSerializer.Serialize(req));
            }
            catch { }
            _vm.Recalculate();
            RefreshGrid();
            return;
        }

        // Colonne mese
        if (colIndex < 2 || !row.IsEditable) return;

        int monthIndex = colIndex - 2;
        int monthNumber = monthIndex + 1;
        decimal value = row.Values[monthIndex];

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
        await SaveData(dataType, refId, monthNumber, value);
        _vm.Recalculate();
        RefreshGrid();
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
            Load(_projectId);
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
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
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnRemoveCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.CategoryDtos.Count == 0) return;
        var last = _vm.CategoryDtos.Last();
        if (MessageBox.Show($"Eliminare '{last.Name}'?", "Conferma", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            await ApiClient.DeleteAsync($"/api/projects/{_projectId}/cashflow/categories/{last.Id}");
            Load(_projectId);
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════════
    // PAGAMENTO CLIENTE
    // ═══════════════════════════════════════════════════════════════
    private void PaymentAmount_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && sender is TextBox tb)
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

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════
    private void RefreshGrid()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!dgMain.IsKeyboardFocusWithin ||
                    dgMain.CommitEdit(DataGridEditingUnit.Row, true))
                {
                    dgMain.Items.Refresh();
                }
            }
            catch { }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private async Task SaveData(string dataType, int refId, int monthNumber, decimal numValue)
    {
        try
        {
            var req = new { dataType, refId, monthNumber, numValue, dateValue = (DateTime?)null };
            await ApiClient.PutAsync($"/api/projects/{_projectId}/cashflow/data",
                JsonSerializer.Serialize(req));
        }
        catch { }
    }

    private async Task SavePaymentAndRefresh()
    {
        try
        {
            var req = new { paymentAmount = _vm.PaymentAmount, monthCount = _vm.MonthCount };
            await ApiClient.PutAsync($"/api/projects/{_projectId}/cashflow/header",
                JsonSerializer.Serialize(req));
            _vm.Recalculate();
            RefreshGrid();
        }
        catch { }
    }
}