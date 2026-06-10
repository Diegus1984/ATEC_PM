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

namespace ATEC.PM.Client.Views.Commerciale.Preventivi;

/// <summary>Riga unificata per la DataGrid: può essere master o sotto-riga revisione.</summary>
public class QuoteDisplayRow : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string QuoteNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    private string _status = "draft";
    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            NotifyStatusDependents();
        }
    }

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

    private void NotifyStatusDependents()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSuperseded)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConverted)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanConvert)));
    }

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
    private const int PageSize = 50;

    private List<QuoteDto> _allQuotes = new();
    private ObservableCollection<QuoteDisplayRow> _displayRows = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;
    private bool _isGroupedView;

    private int _loadedPage;
    private int _totalCount;
    private bool _hasMore;
    private bool _loading;
    private InfiniteScrollHelper? _infiniteScroll;

    private const string PrefKeyQuotesView = "QuotesHomePage.ViewMode";

    // Cache delle revisioni per master ID
    private Dictionary<int, List<QuoteDto>> _revisionsByMaster = new();

    /// <summary>Dopo "Crea revisione", espandi automaticamente lo storico di quella famiglia.</summary>
    private int _expandAfterLoadMasterId;

    public QuotesHomePage()
    {
        InitializeComponent();
        _infiniteScroll = new InfiniteScrollHelper(
            () => _hasMore && !_loading,
            () => Load(append: true));
        _infiniteScroll.Attach(dgQuotes);
        _isGroupedView = UserPreferences.GetString(PrefKeyQuotesView) == "grouped";
        UpdateViewToggleButtons();
        Loaded += async (_, _) => await Load(append: false);
    }

    private async Task Load(bool append = false)
    {
        if (_loading) return;
        _loading = true;
        if (!append)
        {
            _loadedPage = 0;
            _allQuotes.Clear();
        }

        int page = append ? _loadedPage + 1 : 1;
        string url = PagedApiHelper.BuildUrl("/api/quotes", page, PageSize, BuildQuotesQuery());
        txtStatus.Text = append ? "Caricamento altri preventivi..." : "Caricamento...";
        try
        {
            PagedResult<QuoteDto>? pageData = await PagedApiHelper.GetPageAsync<QuoteDto>(url);
            if (pageData != null)
            {
                _loadedPage = pageData.Page;
                _totalCount = pageData.TotalCount;
                _hasMore = pageData.HasMore;
                _allQuotes.AddRange(pageData.Items);
                await MergeRevisionChainsAsync();
                RebuildRevisionsCache();
                ApplyFilter();
                _infiniteScroll?.NotifyContentUpdated(dgQuotes);
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildRevisionsCache()
    {
        _revisionsByMaster = new Dictionary<int, List<QuoteDto>>();
        foreach (QuoteDto q in _allQuotes)
        {
            int masterId = q.ParentQuoteId ?? q.Id;
            if (!_revisionsByMaster.ContainsKey(masterId))
                _revisionsByMaster[masterId] = GetChainQuotes(masterId).Where(x => x.Id != masterId).ToList();
        }
    }

    private async Task MergeRevisionChainsAsync()
    {
        HashSet<int> masterIds = new();
        foreach (QuoteDto q in _allQuotes)
            masterIds.Add(q.ParentQuoteId ?? q.Id);
        if (masterIds.Count == 0) return;

        string idsParam = string.Join(",", masterIds);
        List<QuoteDto> chainRows = await ApiClient.GetListAsync<QuoteDto>($"/api/quotes/chains?masterIds={idsParam}");
        HashSet<int> existing = new(_allQuotes.Select(q => q.Id));
        foreach (QuoteDto q in chainRows)
        {
            if (!existing.Contains(q.Id))
            {
                _allQuotes.Add(q);
                existing.Add(q.Id);
            }
        }
    }

    private List<QuoteDto> GetChainQuotes(int masterId) =>
        _allQuotes
            .Where(q => q.Id == masterId || q.ParentQuoteId == masterId)
            .OrderByDescending(q => q.Revision)
            .ThenByDescending(q => q.CreatedAt)
            .ToList();

    private static QuoteDto PickDisplayQuote(List<QuoteDto> chain)
    {
        QuoteDto? active = chain
            .Where(q => q.Status != "superseded" && q.Status != "converted")
            .OrderByDescending(q => q.Revision)
            .ThenByDescending(q => q.CreatedAt)
            .FirstOrDefault();
        return active ?? chain.First();
    }

    private Dictionary<string, string?> BuildQuotesQuery()
    {
        Dictionary<string, string?> query = new();
        string globalSearch = txtSearch?.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(globalSearch))
            query["search"] = globalSearch;

        if (cmbStatusFilter?.SelectedItem is ComboBoxItem cbi && cbi.Tag is string status && !string.IsNullOrEmpty(status))
            query["status"] = status;

        if (cmbTypeFilter?.SelectedItem is ComboBoxItem tbi && tbi.Tag is string quoteType && !string.IsNullOrEmpty(quoteType))
            query["quoteType"] = quoteType;

        string fNum = F("QuoteNumber");
        if (!string.IsNullOrEmpty(fNum)) query["quoteNumber"] = fNum;
        string fCust = F("CustomerName");
        if (!string.IsNullOrEmpty(fCust)) query["customerName"] = fCust;
        string fTitle = F("Title");
        if (!string.IsNullOrEmpty(fTitle)) query["title"] = fTitle;
        return query;
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
            int masterId = master.Id;
            List<QuoteDto> chain = GetChainQuotes(masterId);
            List<QuoteDto> revisions = chain.Where(q => q.Id != masterId).ToList();
            int revCount = revisions.Count;

            QuoteDto displayQuote = PickDisplayQuote(chain);
            List<QuoteDto> subRowQuotes = chain
                .Where(q => q.Id != displayQuote.Id)
                .OrderByDescending(q => q.Revision)
                .ThenByDescending(q => q.CreatedAt)
                .ToList();

            QuoteDisplayRow masterRow = QuoteDisplayRow.FromDto(displayQuote, false, revCount, masterId);
            bool wasExpanded = expandedMasters.Contains(masterId)
                               || (_expandAfterLoadMasterId > 0 && _expandAfterLoadMasterId == masterId);
            if (_expandAfterLoadMasterId == masterId)
                _expandAfterLoadMasterId = 0;
            masterRow.IsExpanded = wasExpanded;
            _displayRows.Add(masterRow);

            if (wasExpanded)
            {
                foreach (QuoteDto sub in subRowQuotes)
                    _displayRows.Add(QuoteDisplayRow.FromDto(sub, true, 0, masterId));
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

            int masterId = masterRow.MasterId;
            List<QuoteDto> subRowQuotes = GetChainQuotes(masterId)
                .Where(q => q.Id != masterRow.Id)
                .OrderByDescending(q => q.Revision)
                .ThenByDescending(q => q.CreatedAt)
                .ToList();

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
        try { await Task.Delay(300, _filterCts.Token); await Load(append: false); }
        catch (TaskCanceledException) { }
    }

    private async void TxtSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        try { await Task.Delay(300, _filterCts.Token); await Load(append: false); }
        catch (TaskCanceledException) { }
    }

    private void StatusFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = Load(append: false);
    }

    private void TypeFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = Load(append: false);
    }

    private string F(string tag) =>
        _filterBoxes.GetValueOrDefault(tag)?.Text.Trim().ToLower() ?? "";

    private void ApplyFilter()
    {
        if (_allQuotes == null || !IsLoaded) return;

        _suppressStatusChange = true;

        // Filtri di testo/tipo/stato già applicati dal server sulla pagina corrente;
        // le versioni superseded arrivano via /api/quotes/chains per lo storico completo.
        List<QuoteDto> filtered = _allQuotes.ToList();

        // Assicurati che ogni famiglia di revisione includa il master root
        HashSet<int> filteredIds = new(filtered.Select(q => q.Id));
        foreach (QuoteDto q in filtered.ToList())
        {
            int masterId = q.ParentQuoteId ?? q.Id;
            if (!filteredIds.Contains(masterId))
            {
                QuoteDto? root = _allQuotes.FirstOrDefault(m => m.Id == masterId);
                if (root != null)
                {
                    filtered.Add(root);
                    filteredIds.Add(root.Id);
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

        int shown = _displayRows.Count(r => !r.IsRevisionSubRow);
        decimal totalValue = _displayRows.Where(r => !r.IsRevisionSubRow).Sum(r => r.Total);
        decimal totalProfit = _displayRows.Where(r => !r.IsRevisionSubRow).Sum(r => r.Profit);
        string countLabel = _totalCount > 0 && _allQuotes.Count < _totalCount
            ? $"{shown} visibili ({_allQuotes.Count} di {_totalCount} caricati)"
            : $"{shown} preventivi";
        txtStatus.Text = $"{countLabel}  |  Valore: {totalValue:N2}€  |  Utile: {totalProfit:N2}€";
        _infiniteScroll?.NotifyContentUpdated(dgQuotes);

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
            () => _suppressStatusChange = false);
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

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await Load(append: false);

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

    private void StatusCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox cmb) return;
        if (cmb.DataContext is not QuoteDisplayRow row) return;
        if (string.IsNullOrEmpty(row.Status)) return;
        if (Equals(cmb.SelectedValue, row.Status)) return;

        _suppressStatusChange = true;
        cmb.SelectedValue = row.Status;
        _suppressStatusChange = false;
    }

    private async void StatusCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStatusChange) return;
        if (sender is not ComboBox cmb) return;
        if (cmb.SelectedItem is not ComboBoxItem selected) return;
        string newStatus = selected.Tag?.ToString() ?? "";
        if (string.IsNullOrEmpty(newStatus)) return;
        if (newStatus == "converted")
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Lo stato «Convertito» si ottiene solo con il pulsante «Converti in Commessa».",
                "Stato non modificabile", MessageBoxButton.OK, MessageBoxImage.Information);
            _suppressStatusChange = true;
            cmb.SelectedValue = (cmb.DataContext as QuoteDisplayRow)?.Status;
            _suppressStatusChange = false;
            return;
        }

        QuoteDisplayRow? row = cmb.DataContext as QuoteDisplayRow;
        if (row == null || row.Status == newStatus) return;

        try
        {
            string body = JsonSerializer.Serialize(new { NewStatus = newStatus });
            string json = await ApiClient.PutAsync($"/api/quotes/{row.Id}/status", body);
            if (ApiClient.IsApiSuccess(json, out string msg))
            {
                row.Status = newStatus;
                QuoteDto? cached = _allQuotes.FirstOrDefault(q => q.Id == row.Id);
                if (cached != null)
                    cached.Status = newStatus;
            }
            else
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(msg, "Transizione non consentita", MessageBoxButton.OK, MessageBoxImage.Warning);
                _suppressStatusChange = true;
                cmb.SelectedValue = row.Status;
                _suppressStatusChange = false;
            }
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}");
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
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
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
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void RowBtnSend_Click(object sender, RoutedEventArgs e)
    {
        ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Funzione invio email in arrivo!", "Info",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RowBtnRevision_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;

        QuoteDto? quote = _allQuotes.FirstOrDefault(q => q.Id == id);
        string label = quote?.QuoteNumber ?? id.ToString();

        int masterId = quote?.ParentQuoteId ?? id;
        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
                $"Creare una nuova revisione partendo da {label}?\n\n" +
                "Viene copiato il contenuto di questa versione.\n" +
                "La versione scelta diventa SUPERATA; le altre restano visibili nello storico.",
                "Crea revisione", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            string json = await ApiClient.PostAsync($"/api/quotes/{id}/revision", "{}");
            if (ApiClient.IsApiSuccess(json, out string msg))
            {
                _expandAfterLoadMasterId = masterId;
                await Load(append: false);
            }
            else
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(msg, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void RowBtnDuplicate_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;
        try
        {
            string json = await ApiClient.PostAsync($"/api/quotes/{id}/duplicate", "{}");
            if (ApiClient.IsApiSuccess(json, out string msg))
                await Load(append: false);
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
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
            string json = await ApiClient.PostAsync($"/api/quotes/{id}/convert", body);
            if (ConvertQuoteDialog.TryParseConvertResponse(json, out int projectId, out string msg))
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(msg, "Conversione completata", MessageBoxButton.OK, MessageBoxImage.Information);
                await Load(append: false);
                if (Window.GetWindow(this) is MainWindow mainWindow)
                    mainWindow.NavigateToProject(projectId, reloadTree: true);
            }
            else
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(string.IsNullOrEmpty(msg) ? "Errore" : msg, "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
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
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Solo i preventivi in bozza possono essere eliminati.", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Eliminare il preventivo {row.QuoteNumber}?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await ApiClient.DeleteAsync($"/api/quotes/{id}");
            await Load(append: false);
        }
    }

    // ── Azioni revisioni ──

    private async void RowBtnReactivate_Click(object sender, RoutedEventArgs e)
    {
        int id = GetQuoteIdFromButton(sender);
        if (id == 0) return;

        QuoteDto? rev = _allQuotes.FirstOrDefault(q => q.Id == id);
        if (rev == null || rev.Status != "superseded") return;

        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Riattivare la revisione {rev.QuoteNumber}?\n\nDiventerà di nuovo BOZZA.",
            "Riattiva", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            string body = JsonSerializer.Serialize(new { NewStatus = "draft" });
            string json = await ApiClient.PutAsync($"/api/quotes/{id}/status", body);
            if (ApiClient.IsApiSuccess(json, out string msg))
                await Load(append: false);
            else
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(msg, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async Task DeleteRevision(QuoteDisplayRow rev)
    {
        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Eliminare la revisione {rev.QuoteNumber}?\n\nSe è l'ultima revisione, la precedente verrà riattivata.",
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

            await Load(append: false);
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }
}
