using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.GestoreDdp;

// Gestore DDP (sezione PM): DDP Commerciali raggruppate per commessa.
// Pezzo 1: intestazione commessa (codice+cliente) + card con KPI (Tot. Acquisti / Inserimenti /
// Mat. Consegna / Mat. Ritardo) e finestra consegne, sul modello del prototipo Gestore_DDP_V4.
// L'apertura della sintesi di una commessa arriverà nel pezzo 2.
public partial class GestoreDdpPage : Page
{
    private readonly ObservableCollection<DdpProjectCardVM> _cards = new();
    private readonly List<DdpProjectCardVM> _all = new();
    private string _search = "";

    // Real-time: aggiornamento live quando un altro utente modifica una DDP di qualsiasi commessa.
    private readonly ProjectHubClient _hub = new();
    private DispatcherTimer? _rtTimer;
    private bool _reloading;

    public GestoreDdpPage()
    {
        InitializeComponent();
        icCards.ItemsSource = _cards;
        _hub.DdpChanged += OnRealtimeChange;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
        await _hub.StartAsync();
        await _hub.JoinAllAsync();   // riceve gli eventi DDP di TUTTE le commesse
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _rtTimer?.Stop();
        _ = _hub.DisposeAsync();
    }

    // Evento da thread di background → marshalo sul Dispatcher e debounce (coalesce burst di modifiche).
    private void OnRealtimeChange(DdpChange c) => Dispatcher.Invoke(ScheduleReload);

    private void ScheduleReload()
    {
        if (_rtTimer == null)
        {
            _rtTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _rtTimer.Tick += async (_, _) => { _rtTimer!.Stop(); await LoadAsync(); };
        }
        _rtTimer.Stop();
        _rtTimer.Start();
    }

    private async Task LoadAsync()
    {
        if (_reloading) return;
        _reloading = true;
        txtStatus.Text = "Caricamento...";
        try
        {
            List<DdpProjectSummary> summaries = await ApiClient.GetListAsync<DdpProjectSummary>("/api/ddp-manager/summary");

            _all.Clear();
            foreach (DdpProjectSummary s in summaries)
                _all.Add(new DdpProjectCardVM(s));

            ApplyFilter();
            txtStatus.Text = _all.Count == 1 ? "1 commessa con DDP" : $"{_all.Count} commesse con DDP";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
        finally { _reloading = false; }
    }

    private void ApplyFilter()
    {
        string q = _search.Trim().ToLowerInvariant();
        _cards.Clear();
        foreach (DdpProjectCardVM c in _all)
        {
            if (q.Length == 0
                || (c.Code?.ToLowerInvariant().Contains(q) ?? false)
                || (c.CustomerName?.ToLowerInvariant().Contains(q) ?? false))
                _cards.Add(c);
        }
        txtEmpty.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = txtSearch.Text;
        ApplyFilter();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    // Apre la sintesi della commessa (KPI + ripartizione per stato).
    private void BtnOpenSintesi_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DdpProjectCardVM vm) return;
        NavigationService?.Navigate(new DdpSintesiPage(vm.ProjectId, vm.Code, vm.CustomerName));
    }
}

// ── VM card commessa ───────────────────────────────────────────
public class DdpProjectCardVM
{
    public int ProjectId { get; }
    public string Code { get; }
    public string CustomerName { get; }
    public int TotalRows { get; }
    public int DatedCount { get; }
    public int OverdueCount { get; }
    public string TotAcquistiLabel { get; }
    public string InsertedLabel { get; }
    public string DeliveryLabel { get; }
    public Brush OverdueBrush { get; }

    public DdpProjectCardVM(DdpProjectSummary s)
    {
        ProjectId = s.ProjectId;
        Code = s.Code;
        CustomerName = s.CustomerName;
        TotalRows = s.TotalRows;
        DatedCount = s.DatedCount;
        OverdueCount = s.OverdueCount;
        TotAcquistiLabel = $"€ {s.TotalValue:N2}";

        InsertedLabel = s.LastInsertedAt.HasValue
            ? $"Inserita il {s.LastInsertedAt.Value:dd/MM/yyyy} alle {s.LastInsertedAt.Value:HH:mm}"
            : "—";

        DeliveryLabel = (s.DeliveryStart.HasValue && s.DeliveryEnd.HasValue)
            ? $"Consegne dal {s.DeliveryStart.Value:dd/MM/yyyy} al {s.DeliveryEnd.Value:dd/MM/yyyy}"
            : "Consegne: n/d";

        // Arancione quando ci sono materiali in ritardo (come .overdue del prototipo).
        OverdueBrush = s.OverdueCount > 0
            ? new SolidColorBrush(Color.FromRgb(0xB0, 0x6B, 0x1F))
            : (Brush)new BrushConverter().ConvertFromString("#26323F")!;
    }
}
