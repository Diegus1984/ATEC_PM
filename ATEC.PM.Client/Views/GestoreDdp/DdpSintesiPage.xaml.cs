using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.GestoreDdp;

// Pezzo 2+3 — Sintesi DDP di una commessa: KPI + viste segmentate
// (Ripartizione per stato · Consegne · Top 10 Costi · Destinazioni · Dati Mancanti), sul modello del prototipo.
public partial class DdpSintesiPage : Page
{
    private const double BarTrack = 200.0;
    // Default (fallback se le aggregazioni A2/A8 non sono configurate). I set effettivi arrivano da "Aggregazioni DDP".
    private static readonly string[] DefaultDelivered = { "CON", "COS", "DISP", "ASS", "MOD" };
    private static readonly string[] DefaultExclMissing = { "ANN", "SOSP", "SOST", "RAM" };

    private HashSet<string> _delivered = new(DefaultDelivered);   // A2 — Materiale Consegnato
    private HashSet<string> _exclMissing = new(DefaultExclMissing); // A8 — Esclusione Dati Mancanti

    private readonly int _projectId;
    private readonly string _code;
    private readonly string _customer;
    private List<BomItemListItem> _rows = new();
    private Dictionary<string, DdpStatusItem> _statusDefs = new();

    private readonly ObservableCollection<BarRowVM> _rip = new();
    // Tabelle-elenco: stesse colonne complete della Dati Distinta (nessun campo omesso).
    private readonly ObservableCollection<BomItemListItem> _consegne = new();
    private readonly ObservableCollection<BomItemListItem> _consegnato = new();
    private readonly ObservableCollection<Top10RowVM> _top10 = new();
    private readonly ObservableCollection<BarRowVM> _dest = new();
    private readonly ObservableCollection<MissingRowVM> _mancanti = new();
    private readonly ObservableCollection<AvanzCardVM> _avanz = new();
    private readonly ObservableCollection<BarRowVM> _acq = new();
    private readonly ObservableCollection<BarRowVM> _mag = new();

    // Set di stati per ogni aggregazione (code → stati), da "Aggregazioni DDP".
    private Dictionary<string, HashSet<string>> _aggSets = new();

    private bool _accordionGuard;
    private bool _multiOpen;   // false = una sezione per volta (accordion); true = più sezioni aperte
    private const string PrefAccordion = "DdpSintesi.MultiOpen";

    // Real-time: refresh live quando un altro utente modifica la DDP di QUESTA commessa.
    private readonly ProjectHubClient _hub = new();
    private DispatcherTimer? _rtTimer;
    private bool _reloading;

    // Stili base della tabella Dati Distinta (catturati una volta) per ricolorare le righe senza accumulare trigger.
    private Style? _baseRowStyle;
    private Style? _baseCellStyle;

    public DdpSintesiPage(int projectId, string code, string customerName)
    {
        InitializeComponent();
        _projectId = projectId;
        _code = code ?? "";
        _customer = customerName ?? "";
        txtTitle.Text = string.IsNullOrWhiteSpace(customerName)
            ? $"Sintesi DDP · {code}"
            : $"Sintesi DDP · {code} — {customerName}";

        icRip.ItemsSource = _rip;
        icDest.ItemsSource = _dest;
        dgConsegne.ItemsSource = _consegne;
        dgConsegnato.ItemsSource = _consegnato;
        dgTop10.ItemsSource = _top10;
        dgMancanti.ItemsSource = _mancanti;
        icAvanz.ItemsSource = _avanz;
        icAcq.ItemsSource = _acq;
        icMag.ItemsSource = _mag;

        // Opzione locale per utente (come QuotesHomePage.ViewMode).
        _multiOpen = UserPreferences.GetBool(PrefAccordion);
        chkMultiOpen.IsChecked = _multiOpen;

        _hub.DdpChanged += OnRealtimeChange;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
        await _hub.StartAsync();
        await _hub.JoinProjectAsync(_projectId);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _rtTimer?.Stop();
        _ = _hub.DisposeAsync();
    }

    private void OnRealtimeChange(DdpChange c)
    {
        if (c.ProjectId != _projectId) return;
        Dispatcher.Invoke(ScheduleReload);
    }

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
            List<DdpStatusItem> statuses = await ApiClient.GetListAsync<DdpStatusItem>("/api/ddp-statuses");
            _statusDefs = statuses
                .GroupBy(s => s.StatusKey).Select(g => g.First())
                .ToDictionary(s => s.StatusKey, s => s);

            // Mappa codice → etichetta (Conf. DDP) per mostrare lo stato esteso nelle tabelle.
            DdpStatusLabelConverter.Map = _statusDefs.ToDictionary(kv => kv.Key, kv => kv.Value.Label);

            // Aggregazioni configurabili (Aggregazioni DDP): set per codice (A2..A8).
            List<DdpAggregation> aggs = await ApiClient.GetListAsync<DdpAggregation>("/api/ddp-aggregations");
            _aggSets = aggs.GroupBy(a => a.Code).ToDictionary(g => g.Key, g => new HashSet<string>(g.First().StatusKeys));
            if (_aggSets.TryGetValue("A2", out HashSet<string>? a2) && a2.Count > 0) _delivered = a2;
            if (_aggSets.TryGetValue("A8", out HashSet<string>? a8)) _exclMissing = a8;

            // Tutte le righe DDP commerciali della commessa (riuso l'endpoint della distinta).
            _rows = await ApiClient.GetListAsync<BomItemListItem>($"/api/projects/{_projectId}/ddp?type=COMMERCIAL");
            // RowNumber non è una colonna DB: la assegno per posizione (l'API ordina per id), come la distinta.
            for (int i = 0; i < _rows.Count; i++) _rows[i].RowNumber = i + 1;

            BuildKpis();
            BuildRipartizione();
            BuildConsegne();
            BuildConsegnato();
            BuildTop10();
            BuildDestinazioni();
            BuildMancanti();
            BuildAvanzamento();
            BuildFeedback(_acq, "A6", txtAcqSub);
            BuildFeedback(_mag, "A7", txtMagSub);
            dgDistinta.ItemsSource = _rows;

            // Colori coerenti con Conf. DDP su tutte le tabelle che elencano righe per stato.
            ApplyStatusColors(dgDistinta, nameof(BomItemListItem.ItemStatus));
            ApplyStatusColors(dgConsegne, nameof(BomItemListItem.ItemStatus));
            ApplyStatusColors(dgConsegnato, nameof(BomItemListItem.ItemStatus));
            ApplyStatusColors(dgTop10, $"{nameof(Top10RowVM.Item)}.{nameof(BomItemListItem.ItemStatus)}");
            ApplyStatusColors(dgMancanti, nameof(MissingRowVM.StatoKey));

            txtStatus.Text = $"{_rows.Count} righe DDP";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
        finally { _reloading = false; }
    }

    // ── KPI ──
    private void BuildKpis()
    {
        decimal totVal = _rows.Sum(r => r.Quantity * r.UnitCost);
        List<BomItemListItem> dated = _rows
            .Where(r => r.DateNeeded.HasValue && !_delivered.Contains(r.ItemStatus))
            .ToList();
        int overdue = dated.Count(r => r.DateNeeded!.Value.Date < DateTime.Today);

        kpiTot.Text = $"€ {totVal:N2}";
        kpiIns.Text = _rows.Count.ToString();
        kpiCons.Text = dated.Count.ToString();
        kpiRit.Text = overdue.ToString();

        // A2 = Materiale Consegnato (set configurabile) · A3 = Parziali.
        kpiConsegnato.Text = _rows.Count(r => _delivered.Contains(r.ItemStatus)).ToString();
        HashSet<string> a3 = _aggSets.TryGetValue("A3", out HashSet<string>? s3) ? s3 : new HashSet<string> { "PAR" };
        kpiParziali.Text = _rows.Count(r => a3.Contains(r.ItemStatus)).ToString();
        kpiRit.Foreground = overdue > 0
            ? new SolidColorBrush(Color.FromRgb(0xB0, 0x6B, 0x1F))
            : (Brush)new BrushConverter().ConvertFromString("#26323F")!;

        if (dated.Count > 0)
        {
            DateTime mn = dated.Min(r => r.DateNeeded!.Value);
            DateTime mx = dated.Max(r => r.DateNeeded!.Value);
            int gg = (int)(mx.Date - mn.Date).TotalDays + 1;
            kpiFin.Text = $"dal {mn:dd/MM/yyyy}\nal {mx:dd/MM/yyyy} · {gg} gg";
        }
        else kpiFin.Text = "n/d";
    }

    // ── Ripartizione per stato ──
    private void BuildRipartizione()
    {
        _rip.Clear();
        var groups = _rows.GroupBy(r => r.ItemStatus ?? "")
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Key)
            .ToList();
        int total = _rows.Count;
        txtRipSub.Text = $"{total} righe · {groups.Count} stati presenti";

        foreach (var g in groups)
        {
            _statusDefs.TryGetValue(g.Key, out DdpStatusItem? def);
            double frac = total > 0 ? (double)g.Count / total : 0;
            _rip.Add(new BarRowVM
            {
                Key = g.Key,
                Label = def?.Label ?? g.Key,
                Count = g.Count,
                Pct = total > 0 ? $"{frac * 100:0.#}%" : "—",
                BarWidth = g.Count > 0 ? Math.Max(6, BarTrack * frac) : 0,
                Background = Brush(def?.ColorBg, "#CCCCCC"),
                Foreground = Brush(def?.ColorFg, "#000000")
            });
        }
    }

    // ── Materiale in Consegna (con data, non consegnate; ⚠ in ritardo sulla data prev.) ──
    private void BuildConsegne()
    {
        _consegne.Clear();
        foreach (BomItemListItem r in _rows
            .Where(r => r.DateNeeded.HasValue && !_delivered.Contains(r.ItemStatus))
            .OrderBy(r => r.DateNeeded!.Value))
            _consegne.Add(r);

        txtConsegneSub.Text = "Orizzonte temporale delle consegne ancora da evadere. "
            + $"Escluse le righe già consegnate o gestite ({string.Join(", ", _delivered.OrderBy(s => s))}).";
    }

    // ── Materiale Consegnato (righe già consegnate o gestite: set A2) ──
    private void BuildConsegnato()
    {
        _consegnato.Clear();
        foreach (BomItemListItem r in _rows
            .Where(r => _delivered.Contains(r.ItemStatus))
            .OrderBy(r => r.RowNumber))
            _consegnato.Add(r);

        txtConsegnatoSub.Text = $"{_consegnato.Count} righe di materiale consegnato o gestito "
            + $"({string.Join(", ", _delivered.OrderBy(s => s))}).";
    }

    // ── Top 10 costi (riga completa + rank e % sul totale) ──
    private void BuildTop10()
    {
        _top10.Clear();
        decimal total = _rows.Sum(r => r.Quantity * r.UnitCost);
        int rank = 1;
        foreach (BomItemListItem r in _rows.OrderByDescending(x => x.Quantity * x.UnitCost).Take(10))
        {
            decimal imp = r.Quantity * r.UnitCost;
            _top10.Add(new Top10RowVM
            {
                Item = r,
                Rank = rank++,
                PctLabel = total > 0 ? $"{imp / total * 100:0.#}%" : "—"
            });
        }
    }

    // ── Destinazioni ──
    private void BuildDestinazioni()
    {
        _dest.Clear();
        int total = _rows.Count;
        var groups = _rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Destination) ? "NON DEFINITA" : r.Destination.Trim())
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Name)
            .ToList();
        txtDestSub.Text = $"{total} righe · {groups.Count} destinazioni";

        Brush bg = (Brush)new BrushConverter().ConvertFromString("#2563EB")!;
        foreach (var g in groups)
        {
            double frac = total > 0 ? (double)g.Count / total : 0;
            bool nd = g.Name == "NON DEFINITA";
            _dest.Add(new BarRowVM
            {
                Key = "",
                Label = g.Name,
                Count = g.Count,
                Pct = total > 0 ? $"{frac * 100:0.#}%" : "—",
                BarWidth = g.Count > 0 ? Math.Max(6, BarTrack * frac) : 0,
                Background = nd ? new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)) : bg,
                Foreground = Brushes.White
            });
        }
    }

    // ── Dati mancanti ──
    private void BuildMancanti()
    {
        _mancanti.Clear();
        int analyzed = 0;
        foreach (BomItemListItem r in _rows)
        {
            string st = r.ItemStatus ?? "";
            if (_exclMissing.Contains(st)) continue;  // stati chiusi/parcheggiati: missing atteso (A8)
            analyzed++;

            bool mStato = string.IsNullOrWhiteSpace(st) || st == "ND";
            bool mRif = string.IsNullOrWhiteSpace(r.DaneaRef);
            bool mData = !r.DateNeeded.HasValue;
            bool mDest = string.IsNullOrWhiteSpace(r.Destination);
            bool mCosto = r.UnitCost == 0;
            if (!(mStato || mRif || mData || mDest || mCosto)) continue;

            // Colore del flag adattato allo sfondo di stato della riga (rosso o bianco per contrasto).
            _statusDefs.TryGetValue(st, out DdpStatusItem? mdef);
            Brush flag = MissingFlagBrush(mdef?.ColorBg);

            _mancanti.Add(new MissingRowVM
            {
                RowNo = r.RowNumber,
                StatoKey = st,
                Desc = r.Description,
                Stato = MissCell.Make(mStato, "Stato", flag),
                Rif = MissCell.Make(mRif, "Rif. Danea", flag),
                Data = MissCell.Make(mData, "Data prev.", flag),
                Dest = MissCell.Make(mDest, "Destinazione", flag),
                Costo = MissCell.Make(mCosto, "Costo", flag)
            });
        }
        int excluded = _rows.Count - analyzed;
        txtMancantiSub.Text = $"{_mancanti.Count} righe con almeno un dato mancante su {analyzed} analizzate ({excluded} escluse per stato)";
    }

    // ── A5 — Stati Avanzamento (8 card; DDP Stop/Sped-Mod sono unioni strutturali) ──
    private void BuildAvanzamento()
    {
        _avanz.Clear();
        int total = _rows.Count;
        (string Label, string[] States)[] buckets =
        {
            ("VERIFICARE", new[] { "VER" }),
            ("CHECK", new[] { "CHEK" }),
            ("DA ORDINARE", new[] { "DO" }),
            ("RICH. OFF.", new[] { "RO" }),
            ("IN ORDINE", new[] { "IO" }),
            ("DDP STOP", new[] { "ANN", "SOSP", "RAM", "SOST" }),
            ("SPED-MOD", new[] { "SPED", "MOD" }),
            ("ASSEGNATO", new[] { "ASS" })
        };
        txtAvanzSub.Text = $"{buckets.Length} stati di avanzamento · {total} righe";

        foreach ((string Label, string[] States) b in buckets)
        {
            int count = _rows.Count(r => b.States.Contains(r.ItemStatus));
            double frac = total > 0 ? (double)count / total : 0;
            _statusDefs.TryGetValue(b.States[0], out DdpStatusItem? def);
            Color baseC = ColorOf(def?.ColorBg, "#94A3B8");
            _avanz.Add(new AvanzCardVM
            {
                Label = b.Label,
                Count = count,
                PctLabel = $"{frac * 100:0.#}% su Tot.",
                CardBg = new SolidColorBrush(Lighten(baseC, 0.86)),
                CardBorder = new SolidColorBrush(Lighten(baseC, 0.5))
            });
        }
    }

    private static Color ColorOf(string? hex, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? fallback : hex); }
        catch { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    private static Color Lighten(Color c, double f) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

    // Colore del flag "dato mancante": rosso, oppure bianco quando sullo sfondo di stato il rosso
    // sarebbe poco leggibile (es. rosso su rosso/scuro). Sceglie per contrasto WCAG tra bianco e rosso.
    private static readonly Color MissRed = (Color)ColorConverter.ConvertFromString("#C0392B");
    private static Brush MissingFlagBrush(string? bgHex)
    {
        Color bg = ColorOf(bgHex, "#FFFFFF");
        if (Contrast(Colors.White, bg) > Contrast(MissRed, bg)) return Brushes.White;
        var b = new SolidColorBrush(MissRed);
        b.Freeze();
        return b;
    }

    private static double Contrast(Color a, Color b)
    {
        double la = RelLum(a) + 0.05, lb = RelLum(b) + 0.05;
        return la > lb ? la / lb : lb / la;
    }

    private static double RelLum(Color c)
        => 0.2126 * LinChan(c.R) + 0.7152 * LinChan(c.G) + 0.0722 * LinChan(c.B);

    private static double LinChan(byte v)
    {
        double s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    // ── A6/A7 — Feedback: conteggio per stato membro dell'aggregazione (config-driven) ──
    private void BuildFeedback(ObservableCollection<BarRowVM> target, string code, System.Windows.Controls.TextBlock subTb)
    {
        target.Clear();
        HashSet<string> set = _aggSets.TryGetValue(code, out HashSet<string>? s) ? s : new HashSet<string>();
        int total = _rows.Count;
        int sum = 0;

        foreach (string key in set
            .OrderBy(k => _statusDefs.TryGetValue(k, out DdpStatusItem? d) ? d.SortOrder : int.MaxValue)
            .ThenBy(k => k))
        {
            int count = _rows.Count(r => r.ItemStatus == key);
            sum += count;
            double frac = total > 0 ? (double)count / total : 0;
            _statusDefs.TryGetValue(key, out DdpStatusItem? def);
            target.Add(new BarRowVM
            {
                Key = key,
                Label = def?.Label ?? key,
                Count = count,
                Pct = total > 0 ? $"{frac * 100:0.#}%" : "—",
                BarWidth = count > 0 ? Math.Max(6, BarTrack * frac) : 0,
                Background = Brush(def?.ColorBg, "#CCCCCC"),
                Foreground = Brush(def?.ColorFg, "#000000")
            });
        }
        subTb.Text = $"{set.Count} stati · {sum} righe";
    }

    // ── Tab ──
    // Accordion: aprendo una sezione, chiude le altre — SOLO se è attiva la modalità "una per volta".
    private void Section_Expanded(object sender, RoutedEventArgs e)
    {
        if (_multiOpen || _accordionGuard) return;
        _accordionGuard = true;
        foreach (Expander ex in accordionPanel.Children.OfType<Expander>())
            if (!ReferenceEquals(ex, sender)) ex.IsExpanded = false;
        _accordionGuard = false;
    }

    // Toggle opzione (salvata per utente): più sezioni aperte vs una per volta.
    private void ChkMultiOpen_Click(object sender, RoutedEventArgs e)
    {
        _multiOpen = chkMultiOpen.IsChecked == true;
        UserPreferences.Set(PrefAccordion, _multiOpen);
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
    }

    // ── Stampa report (KPI + tutte le viste) ──
    private void BtnPrint_Click(object sender, RoutedEventArgs e)
        => PrintDoc(BuildReport(), $"Sintesi DDP {_code}");

    // Stampa di una singola sezione (PDF via "Microsoft Print to PDF" dal PrintDialog).
    private void PrintSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string key)
            PrintDoc(BuildSectionDoc(key), $"DDP {_code} - {key}");
    }

    // Apre direttamente il dialogo di stampa di Windows (niente finestra di anteprima custom).
    private void PrintDoc(FlowDocument doc, string job)
    {
        try
        {
            var pd = new PrintDialog();
            if (pd.ShowDialog() != true) return;
            ApplyPageLayout(doc, pd);
            pd.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, job);
            txtStatus.Text = "Inviato in stampa";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore stampa: {ex.Message}"; }
    }

    // A4 orizzontale di default (tabelle ampie); usa l'area stampabile della stampante scelta.
    private static void ApplyPageLayout(FlowDocument doc, PrintDialog? pd)
    {
        double w = pd?.PrintableAreaWidth ?? 96 * 11.69;
        double h = pd?.PrintableAreaHeight ?? 96 * 8.27;
        doc.PageWidth = w;
        doc.PageHeight = h;
        doc.ColumnWidth = w - doc.PagePadding.Left - doc.PagePadding.Right;
    }

    private FlowDocument NewDoc() => new FlowDocument
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 10,
        PagePadding = new Thickness(40)
    };

    private Paragraph TitlePara(string section)
    {
        string header = string.IsNullOrWhiteSpace(_customer) ? _code : $"{_code} — {_customer}";
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        p.Inlines.Add(new Run($"{section}  ") { FontSize = 16, FontWeight = FontWeights.Bold });
        p.Inlines.Add(new Run(header) { FontSize = 12, Foreground = Brushes.Gray });
        return p;
    }

    // Set completo di colonne (come Dati Distinta / DDP sotto commessa): riusato da tutte le tabelle-elenco in stampa.
    private static readonly string[] FullHeaders =
        { "#", "Data", "Rich.", "Codice", "Descrizione", "Qtà", "UM", "Fornitore", "Produttore", "Stato", "Rif. Danea", "Data prev.", "Destinazione", "Note", "Costo un.", "Totale" };

    private string[] FullRow(BomItemListItem r) => new[]
    {
        r.RowNumber.ToString(), r.CreatedAt?.ToString("dd/MM/yyyy") ?? "", r.RequestedBy, r.PartNumber,
        r.Description, $"{r.Quantity:0.##}", r.Unit, r.SupplierName, r.Manufacturer, StatoLabel(r.ItemStatus),
        r.DaneaRef, r.DateNeeded?.ToString("dd/MM/yyyy") ?? "", r.Destination, r.Notes,
        $"€ {r.UnitCost:N2}", $"€ {r.TotalCost:N2}"
    };

    // Documento di stampa per la singola sezione dell'accordion (+ Stati Avanzamento).
    private FlowDocument BuildSectionDoc(string key)
    {
        FlowDocument doc = NewDoc();
        switch (key)
        {
            case "avanz":
                doc.Blocks.Add(TitlePara("Stati Avanzamento"));
                doc.Blocks.Add(ReportTable("Stati Avanzamento", new[] { "Stato", "N", "%" },
                    _avanz.Select(x => new[] { x.Label, x.Count.ToString(), x.PctLabel })));
                break;
            case "rip":
                doc.Blocks.Add(TitlePara("Ripartizione per stato"));
                doc.Blocks.Add(ReportTable("Ripartizione per stato", new[] { "Stato", "Descrizione", "N", "%" },
                    _rip.Select(x => new[] { x.Key, x.Label, x.Count.ToString(), x.Pct })));
                break;
            case "consegne":
                doc.Blocks.Add(TitlePara("Materiale in Consegna"));
                doc.Blocks.Add(ReportTable("Materiale in Consegna", FullHeaders, _consegne.Select(FullRow)));
                break;
            case "consegnato":
                doc.Blocks.Add(TitlePara("Materiale Consegnato"));
                doc.Blocks.Add(ReportTable("Materiale Consegnato", FullHeaders, _consegnato.Select(FullRow)));
                break;
            case "top10":
                doc.Blocks.Add(TitlePara("Top 10 Costi"));
                doc.Blocks.Add(ReportTable("Top 10 Costi",
                    new[] { "Pos." }.Concat(FullHeaders).Append("% tot.").ToArray(),
                    _top10.Select(x => new[] { x.Rank.ToString() }.Concat(FullRow(x.Item)).Append(x.PctLabel).ToArray())));
                break;
            case "dest":
                doc.Blocks.Add(TitlePara("Destinazioni"));
                doc.Blocks.Add(ReportTable("Destinazioni", new[] { "Destinazione", "N", "%" },
                    _dest.Select(x => new[] { x.Label, x.Count.ToString(), x.Pct })));
                break;
            case "mancanti":
                doc.Blocks.Add(TitlePara("Dati Mancanti"));
                doc.Blocks.Add(ReportTable("Dati Mancanti",
                    new[] { "Riga", "Stato", "Descrizione", "Stato", "Rif. Danea", "Data prev.", "Destinazione", "Costo" },
                    _mancanti.Select(x => new[] { x.RowNo.ToString(), x.StatoKey, x.Desc, x.Stato.Text, x.Rif.Text, x.Data.Text, x.Dest.Text, x.Costo.Text })));
                break;
            case "distinta":
                doc.Blocks.Add(TitlePara("Dati Distinta"));
                doc.Blocks.Add(ReportTable("Dati Distinta", FullHeaders, _rows.Select(FullRow)));
                break;
            case "acq":
                doc.Blocks.Add(TitlePara("Feedback Acquisti"));
                doc.Blocks.Add(ReportTable("Feedback Acquisti", new[] { "Stato", "Descrizione", "N", "%" },
                    _acq.Select(x => new[] { x.Key, x.Label, x.Count.ToString(), x.Pct })));
                break;
            case "mag":
                doc.Blocks.Add(TitlePara("Feedback Magazzino"));
                doc.Blocks.Add(ReportTable("Feedback Magazzino", new[] { "Stato", "Descrizione", "N", "%" },
                    _mag.Select(x => new[] { x.Key, x.Label, x.Count.ToString(), x.Pct })));
                break;
        }
        return doc;
    }

    private FlowDocument BuildReport()
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            PagePadding = new Thickness(40)
        };
        string header = string.IsNullOrWhiteSpace(_customer) ? _code : $"{_code} — {_customer}";
        doc.Blocks.Add(new Paragraph(new Run($"Sintesi DDP — {header}")) { FontSize = 16, FontWeight = FontWeights.Bold });
        doc.Blocks.Add(new Paragraph(new Run(
            $"Tot. Acquisti {kpiTot.Text} · Inserimenti {kpiIns.Text} · Mat. in consegna {kpiCons.Text} · Mat. in ritardo {kpiRit.Text}"))
        { Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 6) });

        doc.Blocks.Add(ReportTable("Ripartizione per stato", new[] { "Stato", "Descrizione", "N", "%" },
            _rip.Select(x => new[] { x.Key, x.Label, x.Count.ToString(), x.Pct })));
        doc.Blocks.Add(ReportTable("Top 10 costi",
            new[] { "Pos." }.Concat(FullHeaders).Append("% tot.").ToArray(),
            _top10.Select(x => new[] { x.Rank.ToString() }.Concat(FullRow(x.Item)).Append(x.PctLabel).ToArray())));
        doc.Blocks.Add(ReportTable("Materiale in Consegna", FullHeaders, _consegne.Select(FullRow)));
        doc.Blocks.Add(ReportTable("Materiale Consegnato", FullHeaders, _consegnato.Select(FullRow)));
        doc.Blocks.Add(ReportTable("Destinazioni", new[] { "Destinazione", "N", "%" },
            _dest.Select(x => new[] { x.Label, x.Count.ToString(), x.Pct })));
        doc.Blocks.Add(ReportTable("Dati mancanti", new[] { "Riga", "Stato", "Descrizione", "Campi mancanti" },
            _mancanti.Select(x => new[] { x.RowNo.ToString(), x.StatoKey, x.Desc, x.MissingFieldsLabel })));
        return doc;
    }

    private static Section ReportTable(string title, string[] headers, IEnumerable<string[]> rows)
    {
        var sec = new Section();
        sec.Blocks.Add(new Paragraph(new Run(title)) { FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) });

        var t = new Table { CellSpacing = 0, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0.5) };
        for (int i = 0; i < headers.Length; i++) t.Columns.Add(new TableColumn());

        var grp = new TableRowGroup();
        var hr = new TableRow { Background = Brushes.Gainsboro };
        foreach (string h in headers) hr.Cells.Add(ReportCell(h, true));
        grp.Rows.Add(hr);
        foreach (string[] r in rows)
        {
            var tr = new TableRow();
            foreach (string c in r) tr.Cells.Add(ReportCell(c, false));
            grp.Rows.Add(tr);
        }
        t.RowGroups.Add(grp);
        sec.Blocks.Add(t);
        return sec;
    }

    private static TableCell ReportCell(string text, bool head) => new(
        new Paragraph(new Run(text ?? "")) { FontWeight = head ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0) })
    {
        Padding = new Thickness(4, 2, 4, 2),
        BorderBrush = Brushes.LightGray,
        BorderThickness = new Thickness(0.5)
    };

    // ── Esporta Excel (Dati Distinta completa) ──
    private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "File Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            FileName = $"DDP_{SafeFileName(_code)}.xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();
            ClosedXML.Excel.IXLWorksheet ws = wb.Worksheets.Add("DDP");
            string[] heads = { "#", "Data", "Rich.", "Codice", "Descrizione", "Qtà", "UM", "Fornitore", "Produttore", "Stato", "Rif. Danea", "Data prev.", "Destinazione", "Note", "Costo un.", "Totale" };
            for (int c = 0; c < heads.Length; c++)
            {
                ClosedXML.Excel.IXLCell cell = ws.Cell(1, c + 1);
                cell.Value = heads[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#CFE3F6");
            }

            int r = 2;
            foreach (BomItemListItem it in _rows)
            {
                ws.Cell(r, 1).Value = it.RowNumber;
                ws.Cell(r, 2).Value = it.CreatedAt?.ToString("dd/MM/yyyy") ?? "";
                ws.Cell(r, 3).Value = it.RequestedBy;
                ws.Cell(r, 4).Value = it.PartNumber;
                ws.Cell(r, 5).Value = it.Description;
                ws.Cell(r, 6).Value = (double)it.Quantity;
                ws.Cell(r, 7).Value = it.Unit;
                ws.Cell(r, 8).Value = it.SupplierName;
                ws.Cell(r, 9).Value = it.Manufacturer;
                ws.Cell(r, 10).Value = StatoLabel(it.ItemStatus);
                ws.Cell(r, 11).Value = it.DaneaRef;
                ws.Cell(r, 12).Value = it.DateNeeded?.ToString("dd/MM/yyyy") ?? "";
                ws.Cell(r, 13).Value = it.Destination;
                ws.Cell(r, 14).Value = it.Notes;
                ws.Cell(r, 15).Value = (double)it.UnitCost;
                ws.Cell(r, 16).Value = (double)it.TotalCost;
                r++;
            }

            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(1);
            wb.SaveAs(dlg.FileName);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            txtStatus.Text = "Excel esportato";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore esportazione: {ex.Message}"; }
    }

    private string StatoLabel(string? key)
        => !string.IsNullOrEmpty(key) && _statusDefs.TryGetValue(key, out DdpStatusItem? d) ? d.Label : (key ?? "");

    private static string SafeFileName(string s)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "export" : s;
    }

    private static Brush Brush(string? hex, string fallback)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(string.IsNullOrWhiteSpace(hex) ? fallback : hex)!; }
        catch { return (Brush)new BrushConverter().ConvertFromString(fallback)!; }
    }

    // Colora le righe di una griglia per causale/stato DDP (sfondo riga = ColorBg, testo = ColorFg),
    // secondo le regole configurate in "Gestione avanzata → Conf. DDP". statusPath = proprietà del VM
    // che contiene la chiave di stato (es. ItemStatus / StatoKey). Riusa il base ModernRow/ModernCell.
    private void ApplyStatusColors(DataGrid dg, string statusPath)
    {
        _baseRowStyle ??= (TryFindResource("ModernRow") as Style) ?? dg.RowStyle;
        _baseCellStyle ??= (TryFindResource("ModernCell") as Style) ?? dg.CellStyle;

        // Sfondo: sulla RIGA (il colore copre tutta l'estensione della riga).
        var rowStyle = new Style(typeof(DataGridRow), _baseRowStyle);
        foreach (DdpStatusItem def in _statusDefs.Values)
        {
            var trig = new DataTrigger { Binding = new Binding(statusPath), Value = def.StatusKey };
            trig.Setters.Add(new Setter(DataGridRow.BackgroundProperty, FrozenBrush(def.ColorBg, "#FFFFFF")));
            rowStyle.Triggers.Add(trig);
        }
        dg.RowStyle = rowStyle;

        // Testo: va impostato sulla CELLA (il Foreground della riga NON si propaga al testo nel DataGrid).
        // Celle trasparenti per lasciar vedere il colore della riga.
        var cellStyle = new Style(typeof(DataGridCell), _baseCellStyle);
        cellStyle.Setters.Add(new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        foreach (DdpStatusItem def in _statusDefs.Values)
        {
            var trig = new DataTrigger { Binding = new Binding(statusPath), Value = def.StatusKey };
            trig.Setters.Add(new Setter(DataGridCell.ForegroundProperty, FrozenBrush(def.ColorFg, "#000000")));
            cellStyle.Triggers.Add(trig);
        }
        dg.CellStyle = cellStyle;
    }

    private static Brush FrozenBrush(string? hex, string fallback)
    {
        Brush b = Brush(hex, fallback);
        if (b is Freezable f && f.CanFreeze) f.Freeze();
        return b;
    }
}

// Converte il codice stato DDP nell'etichetta estesa (es. "ANN" → "ANNULLATO"),
// usando la mappa popolata da Conf. DDP. Se la chiave non è nota, mostra il codice.
public class DdpStatusLabelConverter : IValueConverter
{
    public static Dictionary<string, string> Map = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        string key = value?.ToString() ?? "";
        return Map.TryGetValue(key, out string? label) && !string.IsNullOrWhiteSpace(label) ? label : key;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

// ── VM ─────────────────────────────────────────────────────────
public class BarRowVM
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public string Pct { get; set; } = "";
    public double BarWidth { get; set; }
    public Brush Background { get; set; } = Brushes.LightGray;
    public Brush Foreground { get; set; } = Brushes.Black;
}

// Card "Stato Avanzamento" (A5): sfondo pastello (tinta chiara del colore della causale).
public class AvanzCardVM
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public string PctLabel { get; set; } = "";
    public Brush CardBg { get; set; } = Brushes.White;
    public Brush CardBorder { get; set; } = Brushes.LightGray;
}

// Top 10 costi: incapsula la riga completa (Item, con tutti i campi) + posizione e % sul totale.
public class Top10RowVM
{
    public BomItemListItem Item { get; set; } = null!;
    public int Rank { get; set; }
    public string PctLabel { get; set; } = "";
}

// Antepone "⚠ " alla data prevista quando è scaduta (riga in ritardo), nel formato gg/MM/aaaa.
public class OverdueDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is DateTime d)
            return (d.Date < DateTime.Today ? "⚠ " : "") + d.ToString("dd/MM/yyyy");
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public class MissingRowVM
{
    public int RowNo { get; set; }
    public string StatoKey { get; set; } = "";
    public string Desc { get; set; } = "";
    public MissCell Stato { get; set; } = MissCell.Make(false, "Stato");
    public MissCell Rif { get; set; } = MissCell.Make(false, "Rif. Danea");
    public MissCell Data { get; set; } = MissCell.Make(false, "Data prev.");
    public MissCell Dest { get; set; } = MissCell.Make(false, "Destinazione");
    public MissCell Costo { get; set; } = MissCell.Make(false, "Costo");

    // Per la stampa: elenco compatto dei soli campi mancanti.
    public string MissingFieldsLabel =>
        string.Join(", ", new[] { Stato, Rif, Data, Dest, Costo }.Where(c => c.Text != "–").Select(c => c.Text));
}

// Cella "dato mancante": etichetta colorata se manca, "–" grigio se presente.
// missingBrush consente di adattare il colore del flag allo sfondo di stato della riga
// (rosso di default, bianco quando il rosso sarebbe illeggibile su sfondo rosso/scuro).
public class MissCell
{
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
    private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0xC7, 0xD2, 0xDC));

    public string Text { get; set; } = "–";
    public Brush Brush { get; set; } = Gray;

    public static MissCell Make(bool missing, string label, Brush? missingBrush = null) =>
        new MissCell { Text = missing ? label : "–", Brush = missing ? (missingBrush ?? Red) : Gray };
}
