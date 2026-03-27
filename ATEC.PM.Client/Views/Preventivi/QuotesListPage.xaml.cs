using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Preventivi;

public partial class QuotesListPage : Page
{
    private List<QuoteDto> _allQuotes = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;

    public QuotesListPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync("/api/quotes");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _allQuotes = JsonSerializer.Deserialize<List<QuoteDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                ApplyFilter();
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    // ── Filtri ──

    private void Filter_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag != null)
            _filterBoxes[tb.Tag.ToString()!] = tb;
    }

    private async void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        try { await Task.Delay(300, _filterCts.Token); ApplyFilter(); }
        catch (TaskCanceledException) { }
    }

    private async void TxtSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        try { await Task.Delay(300, _filterCts.Token); ApplyFilter(); }
        catch (TaskCanceledException) { }
    }

    private void StatusFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void TypeFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private string F(string tag) =>
        _filterBoxes.GetValueOrDefault(tag)?.Text.Trim().ToLower() ?? "";

    private static bool Match(string? value, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        var v = value?.ToLower() ?? "";

        bool startsWild = filter.StartsWith('*');
        bool endsWild = filter.EndsWith('*');

        if (startsWild && endsWild)
            return v.Contains(filter.Trim('*'));
        if (endsWild)
            return v.StartsWith(filter.TrimEnd('*'));
        if (startsWild)
            return v.EndsWith(filter.TrimStart('*'));

        return v.Contains(filter);
    }

    private void ApplyFilter()
    {
        if (_allQuotes == null || !IsLoaded) return;

        string fNum = F("QuoteNumber");
        string fCust = F("CustomerName");
        string fTitle = F("Title");
        string globalSearch = txtSearch?.Text?.Trim().ToLower() ?? "";

        string statusFilter = "";
        if (cmbStatusFilter?.SelectedItem is ComboBoxItem cbi && cbi.Tag is string s)
            statusFilter = s;

        string typeFilter = "";
        if (cmbTypeFilter?.SelectedItem is ComboBoxItem tbi && tbi.Tag is string t)
            typeFilter = t;

        var filtered = _allQuotes.Where(q =>
        {
            if (!string.IsNullOrEmpty(statusFilter) && q.Status != statusFilter) return false;
            if (!string.IsNullOrEmpty(typeFilter) && (q.QuoteType ?? "SERVICE") != typeFilter) return false;
            if (!Match(q.QuoteNumber, fNum)) return false;
            if (!Match(q.CustomerName, fCust)) return false;
            if (!Match(q.Title, fTitle)) return false;
            if (!string.IsNullOrEmpty(globalSearch))
            {
                return Match(q.QuoteNumber, globalSearch)
                    || Match(q.CustomerName, globalSearch)
                    || Match(q.Title, globalSearch)
                    || Match(q.CreatedByName, globalSearch);
            }
            return true;
        }).ToList();

        dgQuotes.ItemsSource = filtered;

        decimal totalValue = filtered.Sum(q => q.Total);
        decimal totalProfit = filtered.Sum(q => q.Profit);
        txtStatus.Text = $"{filtered.Count} preventivi su {_allQuotes.Count}  |  Valore: {totalValue:N2}€  |  Utile: {totalProfit:N2}€";
    }

    // ── Selezione ──

    private void DgQuotes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void DgQuotes_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgQuotes.SelectedItem is QuoteDto quote)
        {
            NavigationService?.Navigate(new QuoteDetailPage(quote.Id));
        }
    }

    // ── Azioni ──

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewQuoteDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.CreatedQuoteId > 0)
        {
            NavigationService?.Navigate(new QuoteDetailPage(dlg.CreatedQuoteId));
        }
    }

    private async void BtnDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (dgQuotes.SelectedItem is QuoteDto quote)
        {
            try
            {
                string json = await ApiClient.PostAsync($"/api/quotes/{quote.Id}/duplicate", "{}");
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                {
                    int newId = doc.RootElement.GetProperty("data").GetInt32();
                    await Load();
                    NavigationService?.Navigate(new QuoteDetailPage(newId));
                }
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgQuotes.SelectedItem is QuoteDto quote)
        {
            if (quote.Status != "draft")
            {
                MessageBox.Show("Solo i preventivi in bozza possono essere eliminati.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"Eliminare il preventivo {quote.QuoteNumber}?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                await ApiClient.DeleteAsync($"/api/quotes/{quote.Id}");
                await Load();
            }
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await Load();

    // ── Cambio stato inline ──

    private bool _suppressStatusChange;

    private async void StatusCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStatusChange) return;
        if (sender is not ComboBox cmb) return;
        if (cmb.SelectedItem is not ComboBoxItem selected) return;
        string newStatus = selected.Tag?.ToString() ?? "";
        if (string.IsNullOrEmpty(newStatus)) return;

        // Trova il QuoteDto dalla DataGrid row
        var quote = (cmb.DataContext as QuoteDto);
        if (quote == null || quote.Status == newStatus) return;

        try
        {
            string body = JsonSerializer.Serialize(new { NewStatus = newStatus });
            string json = await ApiClient.PutAsync($"/api/quotes/{quote.Id}/status", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                // Aggiorna in memoria e refresh lista
                quote.Status = newStatus;
                await Load();
            }
            else
            {
                string msg = doc.RootElement.GetProperty("message").GetString() ?? "Errore";
                MessageBox.Show(msg, "Transizione non consentita", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Reset al vecchio valore
                _suppressStatusChange = true;
                cmb.SelectedValue = quote.Status;
                _suppressStatusChange = false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
            _suppressStatusChange = true;
            cmb.SelectedValue = quote.Status;
            _suppressStatusChange = false;
        }
    }

    // ── Azioni per riga ──

    private int GetQuoteIdFromButton(object sender)
    {
        if (sender is Button btn && btn.Tag is int id) return id;
        return 0;
    }

    private async void RowBtnPreview_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;
        try
        {
            byte[] pdfBytes = await ApiClient.GetBytesAsync($"/api/quotes/{id}/pdf");
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"ATEC_Prev_{id}.pdf");
            System.IO.File.WriteAllBytes(tempPath, pdfBytes);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void RowBtnDownload_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;
        try
        {
            var quote = _allQuotes.FirstOrDefault(q => q.Id == id);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"{quote?.QuoteNumber?.Replace("/", "-") ?? "Preventivo"}.pdf",
                Filter = "PDF|*.pdf",
                Title = "Salva PDF preventivo"
            };
            if (dlg.ShowDialog() == true)
            {
                byte[] pdfBytes = await ApiClient.GetBytesAsync($"/api/quotes/{id}/pdf");
                System.IO.File.WriteAllBytes(dlg.FileName, pdfBytes);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dlg.FileName,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void RowBtnSend_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Funzione invio email in arrivo!", "Info",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RowBtnDuplicate_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;
        try
        {
            string json = await ApiClient.PostAsync($"/api/quotes/{id}/duplicate", "{}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await Load();
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void RowBtnEdit_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;
        NavigationService?.Navigate(new QuoteDetailPage(id));
    }

    private async void RowBtnDelete_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;
        var quote = _allQuotes.FirstOrDefault(q => q.Id == id);
        if (quote == null) return;

        if (quote.Status != "draft")
        {
            MessageBox.Show("Solo i preventivi in bozza possono essere eliminati.", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Eliminare il preventivo {quote.QuoteNumber}?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await ApiClient.DeleteAsync($"/api/quotes/{id}");
            await Load();
        }
    }
}
