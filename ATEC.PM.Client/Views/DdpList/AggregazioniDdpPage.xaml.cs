using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

// Configurazione delle aggregazioni di stato DDP: matrice Stati × Aggregazioni editabile (solo appartenenze).
public partial class AggregazioniDdpPage : Page
{
    private List<DdpAggregation> _aggs = new();
    private readonly ObservableCollection<StateRowVM> _rows = new();

    public AggregazioniDdpPage()
    {
        InitializeComponent();
        dgMatrix.ItemsSource = _rows;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            _aggs = await ApiClient.GetListAsync<DdpAggregation>("/api/ddp-aggregations");
            List<DdpStatusItem> statuses = await ApiClient.GetListAsync<DdpStatusItem>("/api/ddp-statuses");

            dgAgg.ItemsSource = _aggs;
            BuildMatrixColumns();

            _rows.Clear();
            foreach (DdpStatusItem s in statuses.OrderBy(x => x.SortOrder).ThenBy(x => x.StatusKey))
            {
                var row = new StateRowVM(s.StatusKey, s.Label, _aggs.Count);
                for (int i = 0; i < _aggs.Count; i++)
                    row.SetInitial(i, _aggs[i].StatusKeys.Contains(s.StatusKey));
                _rows.Add(row);
            }

            txtStatus.Text = $"{_rows.Count} stati × {_aggs.Count} aggregazioni";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    // Aggiunge una colonna checkbox per ogni aggregazione (oltre alla colonna "Stato" definita in XAML).
    private void BuildMatrixColumns()
    {
        for (int i = dgMatrix.Columns.Count - 1; i >= 1; i--)
            dgMatrix.Columns.RemoveAt(i);

        for (int i = 0; i < _aggs.Count; i++)
        {
            var col = new DataGridTemplateColumn
            {
                Width = 64,
                Header = new TextBlock { Text = _aggs[i].Code, ToolTip = _aggs[i].Name, FontWeight = FontWeights.Bold }
            };
            var dt = new DataTemplate();
            var cb = new FrameworkElementFactory(typeof(CheckBox));
            cb.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cb.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center);
            cb.SetBinding(CheckBox.IsCheckedProperty,
                new Binding($"[{i}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            dt.VisualTree = cb;
            col.CellTemplate = dt;
            dgMatrix.Columns.Add(col);
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        dgAgg.CommitEdit(DataGridEditingUnit.Row, true);   // assicura il commit di nome/descrizione in modifica
        txtStatus.Text = "Salvataggio...";
        try
        {
            for (int i = 0; i < _aggs.Count; i++)
            {
                DdpAggregation agg = _aggs[i];
                List<string> keys = _rows.Where(r => r[i]).Select(r => r.Key).ToList();
                var req = new DdpAggregationSaveRequest
                {
                    Id = agg.Id,
                    Name = agg.Name,
                    Description = agg.Description,
                    StatusKeys = keys
                };
                string resp = await ApiClient.PutAsync($"/api/ddp-aggregations/{agg.Id}", JsonSerializer.Serialize(req));
                if (!ApiClient.IsApiSuccess(resp, out string msg))
                {
                    txtStatus.Text = $"Errore su {agg.Code}: {msg}";
                    return;
                }
            }
            txtStatus.Text = "Salvato";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
}

// Riga della matrice: uno stato + appartenenza (bool) a ciascuna aggregazione, accessibile via indicizzatore [i].
public class StateRowVM : INotifyPropertyChanged
{
    private readonly bool[] _cells;
    public string Key { get; }
    public string Label { get; }
    public string Display => string.IsNullOrWhiteSpace(Label) ? Key : $"{Key} — {Label}";

    public StateRowVM(string key, string label, int count)
    {
        Key = key;
        Label = label;
        _cells = new bool[count];
    }

    public void SetInitial(int i, bool v) { if (i >= 0 && i < _cells.Length) _cells[i] = v; }

    public bool this[int i]
    {
        get => i >= 0 && i < _cells.Length && _cells[i];
        set
        {
            if (i < 0 || i >= _cells.Length || _cells[i] == value) return;
            _cells[i] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
