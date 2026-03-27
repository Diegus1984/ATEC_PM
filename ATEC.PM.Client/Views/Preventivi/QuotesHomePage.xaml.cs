using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Preventivi;

/// <summary>Riga unificata per la DataGrid: può essere master o sotto-riga revisione.</summary>
public class QuoteDisplayRow : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string QuoteNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string Status { get; set; } = "draft";
    public string QuoteType { get; set; } = "SERVICE";
    public int Revision { get; set; }
    public int? ParentQuoteId { get; set; }
    public decimal Total { get; set; }
    public decimal Profit { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    // ── Display flags ──
    public bool IsRevisionSubRow { get; set; }
    public bool IsSuperseded => Status == "superseded";
    public int RevisionCount { get; set; }
    public bool HasRevisions => RevisionCount > 0;
    public bool CanConvert => QuoteType == "IMPIANTO" && Status == "accepted" && !IsRevisionSubRow;
    public bool IsConverted => Status == "converted";

    // ID del master per questa catena di revisioni
    public int MasterId { get; set; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; PropertyChanged?.Invoke(this, new(nameof(IsExpanded))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static QuoteDisplayRow FromDto(QuoteDto q, bool isSubRow = false, int revCount = 0, int masterId = 0)
    {
        return new QuoteDisplayRow
        {
            Id = q.Id,
            QuoteNumber = q.QuoteNumber,
            Title = q.Title,
            CustomerName = q.CustomerName,
            Status = q.Status,
            QuoteType = q.QuoteType ?? "SERVICE",
            Revision = q.Revision,
            ParentQuoteId = q.ParentQuoteId,
            Total = q.Total,
            Profit = q.Profit,
            CreatedByName = q.CreatedByName,
            CreatedAt = q.CreatedAt,
            IsRevisionSubRow = isSubRow,
            RevisionCount = revCount,
            MasterId = masterId
        };
    }
}

public partial class QuotesHomePage : Page
{
    private List<QuoteDto> _allQuotes = new();
    private ObservableCollection<QuoteDisplayRow> _displayRows = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;
    private bool _isGroupedView;

    private const string PrefKeyQuotesView = "QuotesHomePage.ViewMode";

    // Cache delle revisioni per master ID
    private Dictionary<int, List<QuoteDto>> _revisionsByMaster = new();

    public QuotesHomePage()
    {
        InitializeComponent();
        _isGroupedView = UserPreferences.GetString(PrefKeyQuotesView) == "grouped";
        UpdateViewToggleButtons();
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

                // Costruisci la cache delle revisioni per master
                _revisionsByMaster = _allQuotes
                    .Where(q => q.ParentQuoteId != null)
                    .GroupBy(q => q.ParentQuoteId!.Value)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Revision).ToList());

                ApplyFilter();
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    // ── Costruzione lista piatta ──

    private void BuildDisplayList(List<QuoteDto> filteredQuotes)
    {
        // Salva stato expand corrente
        HashSet<int> expandedMasters = new(_displayRows
            .Where(r => !r.IsRevisionSubRow && r.IsExpanded)
            .Select(r => r.MasterId));

        _displayRows.Clear();

        // Raggruppa: trova i master (parent_quote_id == null)
        List<QuoteDto> masters = filteredQuotes.Where(q => q.ParentQuoteId == null).ToList();

        foreach (QuoteDto master in masters)
        {
            List<QuoteDto> revisions = _revisionsByMaster.GetValueOrDefault(master.Id) ?? new();
            int revCount = revisions.Count;

            // Quale quote mostrare come riga principale?
            QuoteDto displayQuote = master;
            List<QuoteDto> subRowQuotes = new();

            if (master.Status == "superseded" && revisions.Count > 0)
            {
                // Mostra l'ultima revisione attiva come principale
                QuoteDto? activeRev = revisions.FirstOrDefault(r => r.Status != "superseded")
                                      ?? revisions.First();
                displayQuote = activeRev;

                // Sub-rows: master originale + altre revisioni (non quella mostrata)
                subRowQuotes.Add(master);
                subRowQuotes.AddRange(revisions.Where(r => r.Id != displayQuote.Id));
            }
            else if (revisions.Count > 0)
            {
                // Master non superato ma ha revisioni → tutte le revisioni come sub-rows
                subRowQuotes.AddRange(revisions);
            }

            subRowQuotes = subRowQuotes.OrderByDescending(r => r.Revision).ToList();
            int masterId = master.Id;

            // Riga master
            QuoteDisplayRow masterRow = QuoteDisplayRow.FromDto(displayQuote, false, revCount, masterId);
            bool wasExpanded = expandedMasters.Contains(masterId);
            masterRow.IsExpanded = wasExpanded;
            _displayRows.Add(masterRow);

            // Se espanso, aggiungi sotto-righe
            if (wasExpanded)
            {
                foreach (QuoteDto sub in subRowQuotes)
                {
                    _displayRows.Add(QuoteDisplayRow.FromDto(sub, true, 0, masterId));
                }
            }
        }

        // Aggiungi anche le revisioni non-superseded che non hanno un master nella lista filtrata
        // (caso: filtro per titolo che matcha solo la revisione)
        HashSet<int> shownMasterIds = new(masters.Select(m => m.Id));
        List<QuoteDto> orphanRevisions = filteredQuotes
            .Where(q => q.ParentQuoteId != null && !shownMasterIds.Contains(q.ParentQuoteId.Value))
            .ToList();
        foreach (QuoteDto orphan in orphanRevisions)
        {
            _displayRows.Add(QuoteDisplayRow.FromDto(orphan, false, 0, orphan.ParentQuoteId ?? orphan.Id));
        }
    }

    // ── Toggle expand/collapse ──

    private void ToggleExpand(QuoteDisplayRow masterRow)
    {
        if (!masterRow.HasRevisions) return;

        int idx = _displayRows.IndexOf(masterRow);
        if (idx < 0) return;

        if (masterRow.IsExpanded)
        {
            // Collapse: rimuovi sotto-righe
            masterRow.IsExpanded = false;
            while (idx + 1 < _displayRows.Count && _displayRows[idx + 1].IsRevisionSubRow)
            {
                _displayRows.RemoveAt(idx + 1);
            }
        }
        else
        {
            // Expand: inserisci sotto-righe
            masterRow.IsExpanded = true;

            // Recupera le revisioni per questo master
            int masterId = masterRow.MasterId;
            List<QuoteDto> revisions = _revisionsByMaster.GetValueOrDefault(masterId) ?? new();
            QuoteDto? originalMaster = _allQuotes.FirstOrDefault(q => q.Id == masterId);

            List<QuoteDto> subRowQuotes = new();
            if (originalMaster != null && originalMaster.Id != masterRow.Id)
                subRowQuotes.Add(originalMaster);
            subRowQuotes.AddRange(revisions.Where(r => r.Id != masterRow.Id));
            subRowQuotes = subRowQuotes.OrderByDescending(r => r.Revision).ToList();

            int insertIdx = idx + 1;
            foreach (QuoteDto sub in subRowQuotes)
            {
                _displayRows.Insert(insertIdx++, QuoteDisplayRow.FromDto(sub, true, 0, masterId));
            }
        }
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

    private void StatusFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void TypeFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private string F(string tag) =>
        _filterBoxes.GetValueOrDefault(tag)?.Text.Trim().ToLower() ?? "";

    private static bool Match(string? value, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        string v = value?.ToLower() ?? "";
        bool startsWild = filter.StartsWith('*');
        bool endsWild = filter.EndsWith('*');
        if (startsWild && endsWild) return v.Contains(filter.Trim('*'));
        if (endsWild) return v.StartsWith(filter.TrimEnd('*'));
        if (startsWild) return v.EndsWith(filter.TrimStart('*'));
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

        // Filtra solo quote non-superseded per le righe principali
        // Le superseded appaiono solo come sotto-righe espanse
        List<QuoteDto> filtered = _allQuotes.Where(q =>
        {
            if (q.Status == "superseded") return false;
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

        // Assicurati che i master delle revisioni filtrate siano inclusi
        HashSet<int> filteredIds = new(filtered.Select(q => q.Id));
        foreach (QuoteDto q in filtered.ToList())
        {
            if (q.ParentQuoteId != null && !filteredIds.Contains(q.ParentQuoteId.Value))
            {
                QuoteDto? master = _allQuotes.FirstOrDefault(m => m.Id == q.ParentQuoteId.Value);
                if (master != null && !filteredIds.Contains(master.Id))
                {
                    filtered.Add(master);
                    filteredIds.Add(master.Id);
                }
            }
        }

        BuildDisplayList(filtered);

        if (_isGroupedView)
        {
            // Vista raggruppata: ordina per cliente, poi applica GroupDescription
            var sorted = _displayRows.OrderBy(r => r.CustomerName).ThenByDescending(r => r.CreatedAt).ToList();
            _displayRows.Clear();
            foreach (QuoteDisplayRow row in sorted) _displayRows.Add(row);

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_displayRows);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(QuoteDisplayRow.CustomerName)));
            dgQuotes.ItemsSource = view;
        }
        else
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_displayRows);
            view.GroupDescriptions.Clear();
            dgQuotes.ItemsSource = _displayRows;
        }

        int activeCount = _allQuotes.Count(q => q.Status != "superseded");
        decimal totalValue = _displayRows.Where(r => !r.IsRevisionSubRow).Sum(r => r.Total);
        decimal totalProfit = _displayRows.Where(r => !r.IsRevisionSubRow).Sum(r => r.Profit);
        txtStatus.Text = $"{_displayRows.Count(r => !r.IsRevisionSubRow)} preventivi su {activeCount}  |  Valore: {totalValue:N2}€  |  Utile: {totalProfit:N2}€";
    }

    // ── Selezione ──

    private void DgQuotes_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void DgQuotes_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgQuotes.SelectedItem is QuoteDisplayRow row)
        {
            bool readOnly = row.IsSuperseded || row.IsConverted;
            NavigationService?.Navigate(new QuoteDetailPage(row.Id, readOnly));
        }
    }

    // ── Expand/collapse ──

    private void RowBtnToggleRevisions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is QuoteDisplayRow row && !row.IsRevisionSubRow)
            ToggleExpand(row);
    }

    // ── Azioni ──

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewQuoteDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.CreatedQuoteId > 0)
            NavigationService?.Navigate(new QuoteDetailPage(dlg.CreatedQuoteId));
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await Load();

    // ── Toggle vista ──

    private void BtnViewGrid_Click(object sender, RoutedEventArgs e)
    {
        if (!_isGroupedView) return;
        _isGroupedView = false;
        UserPreferences.Set(PrefKeyQuotesView, "grid");
        UpdateViewToggleButtons();
        ApplyFilter();
    }

    private void BtnViewGrouped_Click(object sender, RoutedEventArgs e)
    {
        if (_isGroupedView) return;
        _isGroupedView = true;
        UserPreferences.Set(PrefKeyQuotesView, "grouped");
        UpdateViewToggleButtons();
        ApplyFilter();
    }

    private void UpdateViewToggleButtons()
    {
        if (_isGroupedView)
        {
            btnViewGrid.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00000000"));
            btnViewGrid.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#374151"));
            btnViewGrouped.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2563EB"));
            btnViewGrouped.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"));
        }
        else
        {
            btnViewGrid.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2563EB"));
            btnViewGrid.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"));
            btnViewGrouped.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00000000"));
            btnViewGrouped.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#374151"));
        }
    }

    // ── Cambio stato inline ──

    private bool _suppressStatusChange;

    private async void StatusCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStatusChange) return;
        if (sender is not ComboBox cmb) return;
        if (cmb.SelectedItem is not ComboBoxItem selected) return;
        string newStatus = selected.Tag?.ToString() ?? "";
        if (string.IsNullOrEmpty(newStatus)) return;

        QuoteDisplayRow? row = cmb.DataContext as QuoteDisplayRow;
        if (row == null || row.Status == newStatus) return;

        try
        {
            string body = JsonSerializer.Serialize(new { NewStatus = newStatus });
            string json = await ApiClient.PutAsync($"/api/quotes/{row.Id}/status", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await Load();
            }
            else
            {
                string msg = doc.RootElement.GetProperty("message").GetString() ?? "Errore";
                MessageBox.Show(msg, "Transizione non consentita", MessageBoxButton.OK, MessageBoxImage.Warning);
                _suppressStatusChange = true;
                cmb.SelectedValue = row.Status;
                _suppressStatusChange = false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
            _suppressStatusChange = true;
            cmb.SelectedValue = row.Status;
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
                FileName = tempPath, UseShellExecute = true
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
            QuoteDto? quote = _allQuotes.FirstOrDefault(q => q.Id == id);
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
                    FileName = dlg.FileName, UseShellExecute = true
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

    private async void RowBtnRevision_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;

        QuoteDto? quote = _allQuotes.FirstOrDefault(q => q.Id == id);
        string label = quote?.QuoteNumber ?? id.ToString();

        if (MessageBox.Show($"Creare una nuova revisione di {label}?\n\nLa versione attuale diventerà SUPERATA.",
            "Crea Revisione", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            string json = await ApiClient.PostAsync($"/api/preventivi/{id}/revision", "{}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await Load();
            }
            else
            {
                string msg = doc.RootElement.GetProperty("message").GetString() ?? "Errore";
                MessageBox.Show(msg, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void RowBtnDuplicate_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;
        try
        {
            string json = await ApiClient.PostAsync($"/api/preventivi/{id}/duplicate", "{}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await Load();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void RowBtnConvert_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;

        QuoteDto? quote = _allQuotes.FirstOrDefault(q => q.Id == id);
        string label = quote?.QuoteNumber ?? id.ToString();

        var dlg = new ConvertQuoteDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string body = JsonSerializer.Serialize(new { PmId = dlg.SelectedPmId });
            string json = await ApiClient.PostAsync($"/api/preventivi/{id}/convert", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                string msg = doc.RootElement.GetProperty("message").GetString() ?? "Commessa creata";
                MessageBox.Show(msg, "Conversione completata", MessageBoxButton.OK, MessageBoxImage.Information);
                await Load();
            }
            else
            {
                string msg = doc.RootElement.GetProperty("message").GetString() ?? "Errore";
                MessageBox.Show(msg, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void RowBtnEdit_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;

        // Se superseded o convertito, apri in sola lettura
        if (sender is Button btn && btn.DataContext is QuoteDisplayRow row && (row.IsSuperseded || row.IsConverted))
        {
            NavigationService?.Navigate(new QuoteDetailPage(id, readOnly: true));
            return;
        }

        NavigationService?.Navigate(new QuoteDetailPage(id));
    }

    private async void RowBtnDelete_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;
        QuoteDisplayRow? row = sender is Button btn ? btn.DataContext as QuoteDisplayRow : null;
        if (row == null) return;

        if (row.IsRevisionSubRow)
        {
            // Eliminazione revisione
            await DeleteRevision(row);
            return;
        }

        if (row.Status != "draft")
        {
            MessageBox.Show("Solo i preventivi in bozza possono essere eliminati.", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Eliminare il preventivo {row.QuoteNumber}?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await ApiClient.DeleteAsync($"/api/quotes/{id}");
            await Load();
        }
    }

    // ── Azioni revisioni ──

    private async void RowBtnReactivate_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;

        QuoteDto? rev = _allQuotes.FirstOrDefault(q => q.Id == id);
        if (rev == null || rev.Status != "superseded") return;

        if (MessageBox.Show($"Riattivare la revisione {rev.QuoteNumber}?\n\nDiventerà di nuovo BOZZA.",
            "Riattiva", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            string body = JsonSerializer.Serialize(new { NewStatus = "draft" });
            string json = await ApiClient.PutAsync($"/api/quotes/{id}/status", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await Load();
            else
            {
                string msg = doc.RootElement.GetProperty("message").GetString() ?? "Errore";
                MessageBox.Show(msg, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async Task DeleteRevision(QuoteDisplayRow rev)
    {
        if (MessageBox.Show($"Eliminare la revisione {rev.QuoteNumber}?\n\nSe è l'ultima revisione, la precedente verrà riattivata.",
            "Elimina Revisione", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            int masterId = rev.MasterId;
            List<QuoteDto> chain = _allQuotes
                .Where(q => q.Id == masterId || q.ParentQuoteId == masterId)
                .OrderByDescending(q => q.Revision)
                .ToList();

            bool isLastRev = chain.Count > 0 && chain.First().Id == rev.Id;

            await ApiClient.DeleteAsync($"/api/quotes/{rev.Id}");

            // Se era l'ultima rev, riattiva la precedente
            if (isLastRev && chain.Count > 1)
            {
                QuoteDto previous = chain[1];
                if (previous.Status == "superseded")
                {
                    string body = JsonSerializer.Serialize(new { NewStatus = "draft" });
                    await ApiClient.PutAsync($"/api/quotes/{previous.Id}/status", body);
                }
            }

            await Load();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }
}
