using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATEC.PM.Client.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;
using Microsoft.Win32;

namespace ATEC.PM.Client.Views.Risorse;

// Dashboard "Piano ferie": KPI, timeline solo-ferie, filtri/selezione, editing inline, export CSV.
public partial class FerieDashboardWindow : Window
{
    private const string FerieTipo = "FERIE";
    private const double LaneHeight = 36;
    private const int MaxPeakScanDays = 1500; // guard anti-loop su intervalli ferie patologici

    private readonly List<LookupItem> _resources;
    private List<ResAssignmentDto> _ferie = new();

    private DateTime _windowStart;
    private int _windowDays = 28;
    private string _filter = "all"; // all | ferie | sel
    private readonly HashSet<int> _selected = new();
    private bool _loaded;
    private bool _busy;
    private bool _syncingCombo;

    // Segnala al planner chiamante che i dati sono cambiati (serve un reload del Gantt).
    public bool DataChanged { get; private set; }

    public FerieDashboardWindow(List<LookupItem> resources, List<ResAssignmentDto> assignments)
    {
        InitializeComponent();
        _resources = resources ?? new();
        _ferie = FilterFerie(assignments);

        _windowStart = ResourcePlannerHelpers.MondayOf(DateTime.Today);
        FocusWindowOnFerie();
        Loaded += FerieDashboardWindow_Loaded;
    }

    private static List<ResAssignmentDto> FilterFerie(IEnumerable<ResAssignmentDto>? all) =>
        (all ?? Enumerable.Empty<ResAssignmentDto>()).Where(a => a.Tipo == FerieTipo).ToList();

    private void FerieDashboardWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Compensa la larghezza della scrollbar verticale per allineare header e corpo.
        headerHostWrap.Margin = new Thickness(0, 0, SystemParameters.VerticalScrollBarWidth, 0);
        PreviewKeyDown += OnPreviewKeyDown; // Esc = annulla drag
        PreviewMouseWheel += OnPreviewMouseWheel; // Ctrl=zoom, Shift=pan
        _loaded = true;
        Render();
    }

    private void FocusWindowOnFerie()
    {
        DateTime winEnd = _windowStart.AddDays(_windowDays - 1);
        if (_ferie.Count == 0) return;
        if (_ferie.Any(a => a.DataFine.Date >= _windowStart && a.DataInizio.Date <= winEnd)) return;
        DateTime anchor = _ferie.Min(a => a.DataInizio.Date);
        _windowStart = ResourcePlannerHelpers.MondayOf(anchor);
    }

    // ── Rendering ───────────────────────────────────────────────

    private void Render()
    {
        if (!_loaded) return;
        DateTime winEnd = _windowStart.AddDays(_windowDays - 1);
        txtPeriod.Text = $"{_windowStart:dd MMM yyyy} – {winEnd:dd MMM yyyy}";

        ComputeKpis();
        RenderHeader(winEnd);
        RenderBody(winEnd);
        UpdateStatus();
    }

    private void ComputeKpis()
    {
        HashSet<int> withFerie = _ferie.Select(a => a.EmployeeId).ToHashSet();
        txtKpiColleghi.Text = withFerie.Count.ToString();
        txtKpiColleghiSub.Text = $"su {_resources.Count} risorse";

        int totDays = _ferie.Sum(a => ResourcePlannerHelpers.WorkingDayCount(a.DataInizio.Date, a.DataFine.Date));
        txtKpiGiorni.Text = totDays.ToString();

        (int peak, DateTime? peakDate) = ComputePeak();
        txtKpiPicco.Text = peak.ToString();
        txtKpiPiccoSub.Text = peakDate.HasValue ? $"il {peakDate.Value:dd/MM/yyyy}" : "—";
    }

    // Picco di colleghi contemporaneamente in ferie su tutto il piano.
    private (int Peak, DateTime? Date) ComputePeak()
    {
        if (_ferie.Count == 0) return (0, null);
        DateTime minStart = _ferie.Min(a => a.DataInizio.Date);
        DateTime maxEnd = _ferie.Max(a => a.DataFine.Date);
        int span = (maxEnd - minStart).Days + 1;
        if (span > MaxPeakScanDays) span = MaxPeakScanDays;

        // Raggruppa per dipendente: conta una persona una sola volta al giorno.
        var byEmp = _ferie.GroupBy(a => a.EmployeeId).ToList();
        int peak = 0;
        DateTime? peakDate = null;
        for (int i = 0; i < span; i++)
        {
            DateTime d = minStart.AddDays(i);
            int cnt = 0;
            foreach (var grp in byEmp)
                if (grp.Any(a => a.DataInizio.Date <= d && d <= a.DataFine.Date))
                    cnt++;
            if (cnt > peak) { peak = cnt; peakDate = d; }
        }
        return (peak, peakDate);
    }

    private void RenderHeader(DateTime winEnd)
    {
        headerHost.ColumnDefinitions.Clear();
        headerHost.RowDefinitions.Clear();
        headerHost.Children.Clear();
        headerHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        headerHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < _windowDays; i++)
            headerHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Riga mesi (span)
        int gi = 0;
        while (gi < _windowDays)
        {
            DateTime d = _windowStart.AddDays(gi);
            int span = 1;
            while (gi + span < _windowDays)
            {
                DateTime d2 = _windowStart.AddDays(gi + span);
                if (d2.Month == d.Month && d2.Year == d.Year) span++;
                else break;
            }
            Border mb = new()
            {
                Background = ResourcePlannerHelpers.HeaderBg,
                BorderBrush = ResourcePlannerHelpers.Gridline,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(6, 3, 6, 3),
                Child = new TextBlock
                {
                    Text = $"{ResourcePlannerHelpers.MonthName(d.Month)} {d.Year}",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ResourcePlannerHelpers.Muted
                }
            };
            Grid.SetRow(mb, 0); Grid.SetColumn(mb, gi); Grid.SetColumnSpan(mb, span);
            headerHost.Children.Add(mb);
            gi += span;
        }

        // Riga giorni
        for (int i = 0; i < _windowDays; i++)
        {
            DateTime d = _windowStart.AddDays(i);
            bool we = ResourcePlannerHelpers.IsWeekend(d);
            bool ho = ResourcePlannerHelpers.IsHoliday(d);
            bool today = d.Date == DateTime.Today;
            StackPanel sp = new() { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = ResourcePlannerHelpers.DowLetter(d),
                FontSize = 9,
                Foreground = ResourcePlannerHelpers.Muted,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = d.Day.ToString(),
                FontSize = 11,
                FontWeight = today ? FontWeights.Bold : FontWeights.Normal,
                Foreground = (we || ho) ? ResourcePlannerHelpers.RedDay : ResourcePlannerHelpers.Ink,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Border cell = new()
            {
                Background = ResourcePlannerHelpers.DayBackground(d),
                BorderBrush = ResourcePlannerHelpers.Gridline,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(0, 2, 0, 2),
                Child = sp
            };
            Grid.SetRow(cell, 1); Grid.SetColumn(cell, i);
            headerHost.Children.Add(cell);
        }
    }

    private void RenderBody(DateTime winEnd)
    {
        namesColumn.Children.Clear();
        namesColumn.RowDefinitions.Clear();
        timelineBody.Children.Clear();
        timelineBody.RowDefinitions.Clear();

        List<LookupItem> rows = GetVisibleColleagues(winEnd);

        if (rows.Count == 0)
        {
            timelineBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) });
            TextBlock empty = new()
            {
                Text = _filter == "sel"
                    ? "Nessun nominativo selezionato. Spunta uno o più nomi, oppure scegli «Tutti i nomi»."
                    : "Nessuna risorsa da mostrare.",
                FontSize = 12,
                Foreground = ResourcePlannerHelpers.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(20)
            };
            timelineBody.Children.Add(empty);
            return;
        }

        int rowIdx = 0;
        foreach (LookupItem res in rows)
        {
            List<ResAssignmentDto> items = _ferie
                .Where(a => a.EmployeeId == res.Id && a.DataFine.Date >= _windowStart && a.DataInizio.Date <= winEnd)
                .OrderBy(a => a.DataInizio)
                .ToList();

            // Auto-pan: la barra trascinata può avere date reali ormai fuori finestra; la
            // reintroduciamo per disegnarla alle date di preview (override più sotto).
            if (_dragRender is { } drAdd && drAdd.EmployeeId == res.Id && items.All(x => x.Id != drAdd.Id))
            {
                ResAssignmentDto? dragged = _ferie.FirstOrDefault(x => x.Id == drAdd.Id);
                if (dragged != null) items.Add(dragged);
            }

            // Lane-packing: le ferie di una stessa risorsa di norma non si sovrappongono,
            // ma se capita le mettiamo su corsie diverse per non sovrapporle visivamente.
            List<DateTime> laneEnds = new();
            Dictionary<int, int> laneOf = new();
            foreach (ResAssignmentDto a in items)
            {
                (DateTime aStart, DateTime aEnd) = DragDates(a);
                int placed = -1;
                for (int li = 0; li < laneEnds.Count; li++)
                    if (laneEnds[li] < aStart) { placed = li; laneEnds[li] = aEnd; break; }
                if (placed < 0) { laneEnds.Add(aEnd); placed = laneEnds.Count - 1; }
                laneOf[a.Id] = placed;
            }
            int laneCount = Math.Max(1, laneEnds.Count);

            int totDays = _ferie.Where(a => a.EmployeeId == res.Id)
                .Sum(a => ResourcePlannerHelpers.WorkingDayCount(a.DataInizio.Date, a.DataFine.Date));

            timelineBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            namesColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            namesColumn.Children.Add(BuildNameCell(res, totDays, laneCount, rowIdx));

            Grid lane = BuildLane(res.Id, laneCount, winEnd);
            foreach (ResAssignmentDto a in items)
            {
                (DateTime aStart, DateTime aEnd) = DragDates(a);
                int s = (aStart - _windowStart).Days;
                int eDay = (aEnd - _windowStart).Days;
                int s2 = Math.Max(0, s);
                int e2 = Math.Min(_windowDays - 1, eDay);
                if (e2 < 0 || s > _windowDays - 1) continue;

                Border bar = BuildBar(a, lane);
                Grid.SetColumn(bar, s2);
                Grid.SetColumnSpan(bar, Math.Max(1, e2 - s2 + 1));
                Grid.SetRow(bar, laneOf[a.Id]);
                lane.Children.Add(bar);
            }
            Grid.SetRow(lane, rowIdx);
            timelineBody.Children.Add(lane);
            rowIdx++;
        }
    }

    private Border BuildNameCell(LookupItem res, int totDays, int laneCount, int rowIdx)
    {
        CheckBox chk = new()
        {
            IsChecked = _selected.Contains(res.Id),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Includi nell'esportazione / filtro «selezionati»"
        };
        chk.Checked += (_, _) => ToggleSelected(res.Id, true);
        chk.Unchecked += (_, _) => ToggleSelected(res.Id, false);

        StackPanel txt = new() { VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(new TextBlock
        {
            Text = res.Name,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = ResourcePlannerHelpers.Ink,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        txt.Children.Add(new TextBlock
        {
            Text = totDays > 0 ? $"{totDays} gg pianificati" : "nessuna ferie",
            FontSize = 10,
            Foreground = ResourcePlannerHelpers.Muted
        });

        Button add = new()
        {
            Content = "+",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("ShadcnButtonOutline"),
            ToolTip = $"Aggiungi ferie a {res.Name}"
        };
        add.Click += (_, _) => _ = CreateFerieDirectAsync(res.Id, DefaultFerieDay(), DefaultFerieDay());

        DockPanel dp = new();
        DockPanel.SetDock(chk, Dock.Left);
        DockPanel.SetDock(add, Dock.Right);
        dp.Children.Add(chk);
        dp.Children.Add(add);
        dp.Children.Add(txt);

        Border nameB = new()
        {
            BorderBrush = ResourcePlannerHelpers.Gridline,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(12, 0, 8, 0),
            Background = _selected.Contains(res.Id) ? ResourcePlannerHelpers.TargetRowBg : Brushes.Transparent,
            // Altezza FISSA = stessa della corsia: namesColumn e timelineBody sono Grid
            // separate, le righe devono combaciare esattamente per restare allineate.
            Height = laneCount * LaneHeight,
            Child = dp
        };
        Grid.SetRow(nameB, rowIdx);
        return nameB;
    }

    private Grid BuildLane(int employeeId, int laneCount, DateTime winEnd)
    {
        Grid lane = new() { Tag = employeeId };
        for (int i = 0; i < _windowDays; i++)
            lane.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int li = 0; li < laneCount; li++)
            lane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LaneHeight) });

        for (int i = 0; i < _windowDays; i++)
        {
            DateTime d = _windowStart.AddDays(i);
            Border bg = new()
            {
                Background = ResourcePlannerHelpers.DayBackground(d),
                BorderBrush = ResourcePlannerHelpers.Gridline,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Cursor = Cursors.Cross
            };
            int col = i;
            // Trascina su una riga vuota per creare ferie sull'intervallo (senza form).
            bg.MouseLeftButtonDown += (_, e) => BeginCreateDrag(e, employeeId, lane, col);
            Grid.SetColumn(bg, i); Grid.SetRow(bg, 0); Grid.SetRowSpan(bg, laneCount);
            lane.Children.Add(bg);
        }
        return lane;
    }

    private Border BuildBar(ResAssignmentDto a, Grid lane)
    {
        int giorni = ResourcePlannerHelpers.WorkingDayCount(a.DataInizio.Date, a.DataFine.Date);
        string label = string.IsNullOrWhiteSpace(a.Descrizione) ? $"Ferie · {giorni}gg" : a.Descrizione!;

        TextBlock tb = new()
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 8, 0)
        };
        // Maniglie di ridimensionamento ai bordi.
        Border leftGrip = new() { Width = BarGripWidth, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Cursor = Cursors.SizeWE };
        Border rightGrip = new() { Width = BarGripWidth, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, Cursor = Cursors.SizeWE };
        Grid inner = new();
        inner.Children.Add(tb);
        inner.Children.Add(leftGrip);
        inner.Children.Add(rightGrip);

        Border bar = new()
        {
            Background = ResourcePlannerHelpers.FerieBg,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(2, 4, 2, 4),
            Cursor = Cursors.SizeAll,
            Child = inner,
            Tag = a, // per ritrovare la barra dopo un re-render (auto-pan)
            ToolTip = $"{a.EmployeeName}\n{a.DataInizio:dd/MM/yyyy} → {a.DataFine:dd/MM/yyyy} ({giorni} gg)"
                + (string.IsNullOrWhiteSpace(a.Descrizione) ? "" : $"\n{a.Descrizione}")
                + "\nTrascina per spostare · bordi per ridimensionare · clic per i dettagli"
        };
        leftGrip.MouseLeftButtonDown += (_, e) => BeginBarDrag(e, a, lane, bar, FDragMode.ResizeStart);
        rightGrip.MouseLeftButtonDown += (_, e) => BeginBarDrag(e, a, lane, bar, FDragMode.ResizeEnd);
        bar.MouseLeftButtonDown += (_, e) =>
        {
            // Doppio click = modifica le date (form dedicato); singolo click+trascina = sposta.
            if (e.ClickCount == 2) { e.Handled = true; _ = EditFerieDatesAsync(a); return; }
            BeginBarDrag(e, a, lane, bar, FDragMode.Move);
        };

        ContextMenu cm = new();
        MenuItem edit = new() { Header = "Modifica date" };
        edit.Click += (_, _) => _ = EditFerieDatesAsync(a);
        MenuItem del = new() { Header = "Elimina" };
        del.Click += (_, _) => _ = DeleteFerieAsync(a);
        cm.Items.Add(edit);
        cm.Items.Add(del);
        bar.ContextMenu = ATEC.PM.Client.Helpers.ShadcnMenuHelper.ApplyDark(cm);
        return bar;
    }

    private List<LookupItem> GetVisibleColleagues(DateTime winEnd)
    {
        Dictionary<int, LookupItem> map = _resources.ToDictionary(r => r.Id);
        // Includi anche dipendenti con ferie che non sono nell'elenco risorse (coerenza col planner).
        foreach (ResAssignmentDto a in _ferie)
            if (!map.ContainsKey(a.EmployeeId))
                map[a.EmployeeId] = new LookupItem { Id = a.EmployeeId, Name = a.EmployeeName };

        IEnumerable<LookupItem> q = map.Values.OrderBy(r => r.Name);

        if (_filter == "ferie")
        {
            HashSet<int> withFerie = _ferie.Select(a => a.EmployeeId).ToHashSet();
            q = q.Where(r => withFerie.Contains(r.Id));
        }
        else if (_filter == "sel")
        {
            q = q.Where(r => _selected.Contains(r.Id));
        }
        return q.ToList();
    }

    private void ToggleSelected(int employeeId, bool on)
    {
        if (on) _selected.Add(employeeId);
        else _selected.Remove(employeeId);
        Render();
    }

    private void UpdateStatus()
    {
        int colleghi = GetVisibleColleagues(_windowStart.AddDays(_windowDays - 1)).Count;
        txtStatus.Text = $"{colleghi} risorse visualizzate · {_ferie.Count} periodi di ferie · {_selected.Count} selezionati";
    }

    // ── Navigazione / filtri ────────────────────────────────────

    private void BtnPrev_Click(object sender, RoutedEventArgs e) { _windowStart = _windowStart.AddDays(-7); Render(); }
    private void BtnNext_Click(object sender, RoutedEventArgs e) { _windowStart = _windowStart.AddDays(7); Render(); }
    private void BtnToday_Click(object sender, RoutedEventArgs e) { _windowStart = ResourcePlannerHelpers.MondayOf(DateTime.Today); Render(); }

    private void CmbWindow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombo) return;
        if (cmbWindow.SelectedItem is ComboBoxItem ci && int.TryParse(ci.Tag as string, out int d))
        {
            _windowDays = d;
            Render();
        }
    }

    // Allinea il combo finestra al valore corrente (o lo lascia vuoto se non corrisponde,
    // es. dopo uno zoom con la rotella). Non rilancia il Render.
    private void SyncWindowCombo()
    {
        _syncingCombo = true;
        try
        {
            cmbWindow.SelectedIndex = -1;
            foreach (object item in cmbWindow.Items)
                if (item is ComboBoxItem ci && int.TryParse(ci.Tag as string, out int d) && d == _windowDays)
                {
                    cmbWindow.SelectedItem = ci;
                    break;
                }
        }
        finally { _syncingCombo = false; }
    }

    private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbFilter.SelectedItem is ComboBoxItem ci)
        {
            _filter = ci.Tag as string ?? "all";
            Render();
        }
    }

    private void BtnSelAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (LookupItem r in GetAllColleagues()) _selected.Add(r.Id);
        Render();
    }

    private void BtnSelNone_Click(object sender, RoutedEventArgs e)
    {
        _selected.Clear();
        Render();
    }

    private List<LookupItem> GetAllColleagues()
    {
        Dictionary<int, LookupItem> map = _resources.ToDictionary(r => r.Id);
        foreach (ResAssignmentDto a in _ferie)
            if (!map.ContainsKey(a.EmployeeId))
                map[a.EmployeeId] = new LookupItem { Id = a.EmployeeId, Name = a.EmployeeName };
        return map.Values.OrderBy(r => r.Name).ToList();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── CRUD ferie (via AssignmentDialog + API) ─────────────────

    // Giorno di default per l'inserimento rapido: oggi se nella finestra, altrimenti inizio finestra.
    private DateTime DefaultFerieDay()
    {
        DateTime winEnd = _windowStart.AddDays(_windowDays - 1);
        DateTime today = DateTime.Today;
        return (today >= _windowStart && today <= winEnd) ? today : _windowStart;
    }

    // Inserimento DIRETTO (senza form): basta nome + giorni. Poi si regola la durata con
    // drag/resize sulla barra. Il tipo è sempre FERIE.
    private async Task CreateFerieDirectAsync(int employeeId, DateTime start, DateTime end)
    {
        if (_busy) return;
        if (end < start) (start, end) = (end, start);

        ResAssignmentCreateRequest req = new()
        {
            EmployeeIds = new List<int> { employeeId },
            Tipo = FerieTipo,
            DataInizio = start.Date,
            DataFine = end.Date
        };
        _busy = true;
        try
        {
            string resp = await ApiClient.PostAsync("/api/resource-planner/assignments", JsonSerializer.Serialize(req));
            if (!ApiClient.IsApiSuccess(resp, out string msg)) { ShowError(msg); return; }
            await ReloadAsync("Ferie inserite");
        }
        catch (Exception ex) { ShowError($"Inserimento non riuscito: {ex.Message}"); }
        finally { _busy = false; }
    }

    // Salvataggio delle nuove date dopo un drag/resize della barra.
    private async Task SaveFerieDatesAsync(ResAssignmentDto a, DateTime start, DateTime end)
    {
        if (_busy) return;
        if (end < start) (start, end) = (end, start);
        if (start.Date == a.DataInizio.Date && end.Date == a.DataFine.Date) { Render(); return; }

        ResAssignmentUpdateRequest req = new()
        {
            EmployeeId = a.EmployeeId,
            Tipo = a.Tipo,
            DataInizio = start.Date,
            DataFine = end.Date,
            ProjectId = a.ProjectId,
            ServiceId = a.ServiceId,
            OtherActivityId = a.OtherActivityId,
            Descrizione = a.Descrizione
        };
        _busy = true;
        try
        {
            string resp = await ApiClient.PutAsync($"/api/resource-planner/assignments/{a.Id}", JsonSerializer.Serialize(req));
            if (!ApiClient.IsApiSuccess(resp, out string msg)) { ShowError(msg); return; }
            await ReloadAsync("Date aggiornate");
        }
        catch (Exception ex) { ShowError($"Salvataggio non riuscito: {ex.Message}"); }
        finally { _busy = false; }
    }

    // Doppio click / menu "Modifica": form dedicato per le SOLE date (+ descrizione).
    private async Task EditFerieDatesAsync(ResAssignmentDto a)
    {
        if (_busy) return;
        FerieEditDialog dlg = new(a) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        if (dlg.DeleteRequested) { await DeleteFerieCoreAsync(a, "Ferie eliminate"); return; }
        await SaveFerieEditAsync(a, dlg.NewStart, dlg.NewEnd, dlg.NewDescription);
    }

    private async Task SaveFerieEditAsync(ResAssignmentDto a, DateTime start, DateTime end, string? descr)
    {
        if (_busy) return;
        if (end < start) (start, end) = (end, start);

        ResAssignmentUpdateRequest req = new()
        {
            EmployeeId = a.EmployeeId,
            Tipo = a.Tipo,
            DataInizio = start.Date,
            DataFine = end.Date,
            ProjectId = a.ProjectId,
            ServiceId = a.ServiceId,
            OtherActivityId = a.OtherActivityId,
            Descrizione = descr
        };
        _busy = true;
        try
        {
            string resp = await ApiClient.PutAsync($"/api/resource-planner/assignments/{a.Id}", JsonSerializer.Serialize(req));
            if (!ApiClient.IsApiSuccess(resp, out string msg)) { ShowError(msg); return; }
            await ReloadAsync("Modifica salvata");
        }
        catch (Exception ex) { ShowError($"Modifica non riuscita: {ex.Message}"); }
        finally { _busy = false; }
    }

    // Menu contestuale "Elimina": chiede conferma poi elimina.
    private async Task DeleteFerieAsync(ResAssignmentDto a)
    {
        if (ShadcnMessageBox.Show(
                $"Eliminare le ferie di {a.EmployeeName} ({a.DataInizio:dd/MM} → {a.DataFine:dd/MM})?",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await DeleteFerieCoreAsync(a, "Ferie eliminate");
    }

    private async Task DeleteFerieCoreAsync(ResAssignmentDto a, string status)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            string resp = await ApiClient.DeleteAsync($"/api/resource-planner/assignments/{a.Id}");
            if (!ApiClient.IsApiSuccess(resp, out string msg)) { ShowError(msg); return; }
            await ReloadAsync(status);
        }
        catch (Exception ex) { ShowError($"Eliminazione non riuscita: {ex.Message}"); }
        finally { _busy = false; }
    }

    private async Task ReloadAsync(string status)
    {
        DataChanged = true;
        try
        {
            List<ResAssignmentDto> all = await ApiClient.GetListAsync<ResAssignmentDto>("/api/resource-planner/assignments");
            _ferie = FilterFerie(all);
        }
        catch (Exception ex) { ShowError($"Ricarica non riuscita: {ex.Message}"); }
        Render();
        txtStatus.Text = status;
    }

    // ── Export CSV ──────────────────────────────────────────────

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        List<ResAssignmentDto> data = _ferie;
        if (_filter == "sel" || (_selected.Count > 0 && AskExportSelectedOnly()))
            data = _ferie.Where(a => _selected.Contains(a.EmployeeId)).ToList();

        if (data.Count == 0)
        {
            ShowError("Nessuna ferie da esportare con i criteri attuali.");
            return;
        }

        SaveFileDialog sfd = new()
        {
            Title = "Esporta piano ferie",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"piano_ferie_{DateTime.Today:yyyyMMdd}.csv"
        };
        if (sfd.ShowDialog(this) != true) return;

        try
        {
            StringBuilder sb = new();
            sb.AppendLine("Risorsa;Inizio;Fine;Giorni;Descrizione");
            foreach (ResAssignmentDto a in data.OrderBy(x => x.EmployeeName).ThenBy(x => x.DataInizio))
            {
                int gg = ResourcePlannerHelpers.WorkingDayCount(a.DataInizio.Date, a.DataFine.Date);
                sb.AppendLine(string.Join(";",
                    Csv(a.EmployeeName),
                    a.DataInizio.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    a.DataFine.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    gg.ToString(),
                    Csv(a.Descrizione ?? "")));
            }
            System.IO.File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));
            txtStatus.Text = $"Esportate {data.Count} ferie in {System.IO.Path.GetFileName(sfd.FileName)}";
        }
        catch (Exception ex) { ShowError($"Esportazione non riuscita: {ex.Message}"); }
    }

    private bool AskExportSelectedOnly() =>
        ShadcnMessageBox.Show(
            $"Esportare solo i {_selected.Count} nominativi selezionati? (No = tutti)",
            "Esporta", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static string Csv(string s)
    {
        s ??= "";
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static void ShowError(string msg) =>
        ShadcnMessageBox.Show(
            string.IsNullOrWhiteSpace(msg) ? "Operazione non riuscita." : msg, "Piano ferie",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
