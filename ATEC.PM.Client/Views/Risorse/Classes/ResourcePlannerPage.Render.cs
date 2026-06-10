using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Rendering Gantt, filtri risorse e pannello conflitti.
public partial class ResourcePlannerPage
{
    private double TimelineMinWidth()
    {
        double viewport = bodyHScroll.ActualWidth;
        if (viewport < 1 || double.IsNaN(viewport))
            viewport = 800;

        double dayWidth = DayColumnWidth(viewport);
        return Math.Max(viewport, _windowDays * dayWidth);
    }

    private double DayColumnWidth(double viewport)
    {
        if (viewport < 1)
            viewport = 800;

        double fitted = viewport / _windowDays;
        return Math.Clamp(fitted, AbsoluteMinDayColumnWidth, MinDayColumnWidth);
    }

    private void RenderGantt()
    {
        if (!IsLoaded || _isRenderingGantt)
            return;

        _isRenderingGantt = true;
        _syncingScroll = true;
        try
        {
            RenderGanttCore();
            double offset = Math.Min(bodyHScroll.HorizontalOffset, bodyHScroll.ScrollableWidth);
            if (offset < 0) offset = 0;
            bodyHScroll.ScrollToHorizontalOffset(offset);
            headerHScroll.ScrollToHorizontalOffset(offset);
        }
        finally
        {
            _syncingScroll = false;
            _isRenderingGantt = false;
        }
    }

    private void ScheduleRenderGantt()
    {
        if (_renderDebounceTimer == null)
        {
            _renderDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _renderDebounceTimer.Tick += (_, _) =>
            {
                _renderDebounceTimer.Stop();
                RenderGantt();
            };
        }

        if (_renderDebounceTimer.IsEnabled)
            return;

        _renderDebounceTimer.Start();
    }

    private void RenderGanttCore()
    {
        DateTime winEnd = _windowStart.AddDays(_windowDays - 1);
        txtPeriod.Text = $"{_windowStart:dd MMM yyyy} – {winEnd:dd MMM yyyy}";

        double timelineWidth = TimelineMinWidth();
        if (Math.Abs(timelineWidth - _lastTimelineWidth) > 0.5)
        {
            _lastTimelineWidth = timelineWidth;
            headerHost.MinWidth = timelineWidth;
            timelineBody.MinWidth = timelineWidth;
        }

        RenderHeader(winEnd);
        RenderBody(winEnd, timelineWidth);
        UpdateConflictPanel(winEnd);
        UpdateDefaultStatus(GetFilteredResources(winEnd).Count, winEnd);
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

    private void RenderBody(DateTime winEnd, double timelineWidth)
    {
        _lanes.Clear();
        _employeeRowIndex.Clear();
        _selectedBar = null;
        _hoveredBar = null;

        namesColumn.Children.Clear();
        namesColumn.RowDefinitions.Clear();

        timelineBody.Children.Clear();
        timelineBody.RowDefinitions.Clear();

        List<LookupItem> filtered = GetFilteredResources(winEnd);
        int rowIdx = 0;

        foreach (LookupItem res in filtered)
        {
            bool isDragLane = _dragRender is { } drLane && drLane.EmployeeId == res.Id;

            List<ResAssignmentDto> raw = GetVisibleAssignments(res.Id, winEnd);
            List<ResAssignmentDto> items = PassAssignmentFilters(raw);

            // Auto-pan: la barra trascinata ha la data reale ormai fuori finestra; la
            // reintroduciamo nella sua corsia (verrà piazzata alle date di preview).
            if (isDragLane && _dragRender is { } drAdd && items.All(x => x.Id != drAdd.Id))
            {
                ResAssignmentDto? dragged = _assignments.FirstOrDefault(x => x.Id == drAdd.Id);
                if (dragged != null) items.Add(dragged);
            }

            if (!isDragLane)
            {
                if (HasActiveAssignmentFilter() && items.Count == 0 && raw.Count > 0)
                    continue;
                if (chkOccupiedOnly.IsChecked == true && items.Count == 0)
                    continue;
            }

            timelineBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            namesColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _employeeRowIndex[res.Id] = rowIdx;

            List<DateTime> laneEnds = new();
            Dictionary<int, int> laneOf = new();
            foreach (ResAssignmentDto a in items)
            {
                DateTime aStart = a.DataInizio.Date;
                DateTime aEnd = a.DataFine.Date;
                if (_dragRender is { } dr && dr.Id == a.Id) { aStart = dr.Start.Date; aEnd = dr.End.Date; }

                int placed = -1;
                for (int li = 0; li < laneEnds.Count; li++)
                    if (laneEnds[li] < aStart) { placed = li; laneEnds[li] = aEnd; break; }
                if (placed < 0) { laneEnds.Add(aEnd); placed = laneEnds.Count - 1; }
                laneOf[a.Id] = placed;
            }
            int laneCount = Math.Max(1, laneEnds.Count);

            // La risorsa collegata all'utente loggato: riga evidenziata (grassetto + sfondo accent leggero)
            // così ognuno individua subito le attività che lo riguardano.
            bool isSelf = res.Id > 0 && res.Id == Services.AppSession.Instance.UserId;

            Border nameB = new()
            {
                BorderBrush = ResourcePlannerHelpers.Gridline,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(12, 4, 8, 4),
                Background = isSelf ? ResourcePlannerHelpers.SelfRowBg : Brushes.Transparent,
                MinHeight = laneCount * LaneHeight,
                Child = new TextBlock
                {
                    Text = res.Name,
                    FontSize = 12,
                    FontWeight = isSelf ? FontWeights.Bold : FontWeights.Medium,
                    Foreground = ResourcePlannerHelpers.Ink,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            Grid.SetRow(nameB, rowIdx);
            namesColumn.Children.Add(nameB);

            Grid lane = new() { Tag = res.Id, MinWidth = timelineWidth };
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
                bg.MouseLeftButtonDown += (_, e) => BeginCreateDrag(e, res.Id, lane, bg);
                Grid.SetColumn(bg, i); Grid.SetRow(bg, 0); Grid.SetRowSpan(bg, laneCount);
                lane.Children.Add(bg);
            }

            foreach (ResAssignmentDto a in items)
            {
                // La barra in auto-pan usa le date di preview (override), non quelle reali.
                DateTime aStart = a.DataInizio.Date;
                DateTime aEnd = a.DataFine.Date;
                if (_dragRender is { } dr && dr.Id == a.Id)
                {
                    aStart = dr.Start.Date;
                    aEnd = dr.End.Date;
                }

                int s = (aStart - _windowStart).Days;
                int eDay = (aEnd - _windowStart).Days;
                int s2 = Math.Max(0, s);
                int e2 = Math.Min(_windowDays - 1, eDay);
                if (e2 < 0 || s > _windowDays - 1)
                    continue;

                bool clipLeft = aStart < _windowStart;
                bool clipRight = aEnd > winEnd;

                Border bar = BuildBar(a, lane, clipLeft, clipRight);
                if (_selectedAssignment?.Id == a.Id)
                {
                    _selectedBar = bar;
                    ApplySelectionStyle(bar, true);
                }
                Grid.SetColumn(bar, s2);
                Grid.SetColumnSpan(bar, Math.Max(1, e2 - s2 + 1));
                Grid.SetRow(bar, laneOf[a.Id]);
                lane.Children.Add(bar);
            }

            Grid.SetRow(lane, rowIdx);
            timelineBody.Children.Add(lane);
            _lanes.Add(new LaneInfo { EmployeeId = res.Id, EmployeeName = res.Name ?? "", Lane = lane, NameCell = nameB });
            rowIdx++;
        }
    }

    private List<LookupItem> GetFilteredResources(DateTime winEnd)
    {
        Dictionary<int, LookupItem> map = _resources.ToDictionary(r => r.Id);
        foreach (ResAssignmentDto a in _assignments)
        {
            if (a.DataFine.Date < _windowStart || a.DataInizio.Date > winEnd)
                continue;
            if (!map.ContainsKey(a.EmployeeId))
                map[a.EmployeeId] = new LookupItem { Id = a.EmployeeId, Name = a.EmployeeName };
        }

        IEnumerable<LookupItem> q = map.Values.OrderBy(r => r.Name);
        if (!string.IsNullOrEmpty(_search))
            q = q.Where(r => (r.Name ?? "").ToLower().Contains(_search));

        if (chkConflictsOnly.IsChecked == true)
        {
            HashSet<int> conflictEmployees = _assignments
                .Where(a => a.HasConflict && a.DataFine.Date >= _windowStart && a.DataInizio.Date <= winEnd)
                .Select(a => a.EmployeeId)
                .ToHashSet();
            q = q.Where(r => conflictEmployees.Contains(r.Id));
        }

        return q.ToList();
    }

    private List<ResAssignmentDto> GetVisibleAssignments(int employeeId, DateTime winEnd) =>
        _assignments
            .Where(a => a.EmployeeId == employeeId && a.DataFine.Date >= _windowStart && a.DataInizio.Date <= winEnd)
            .OrderBy(a => a.DataInizio)
            .ToList();

    private bool HasActiveAssignmentFilter()
    {
        string tipo = GetFilterTipo();
        int projectId = GetFilterProjectId();
        return !string.IsNullOrEmpty(tipo) || projectId > 0;
    }

    private List<ResAssignmentDto> PassAssignmentFilters(List<ResAssignmentDto> items)
    {
        string tipo = GetFilterTipo();
        int projectId = GetFilterProjectId();
        IEnumerable<ResAssignmentDto> q = items;
        if (!string.IsNullOrEmpty(tipo))
            q = q.Where(a => a.Tipo == tipo);
        if (projectId > 0)
            q = q.Where(a => a.ProjectId == projectId);
        return q.ToList();
    }

    private string GetFilterTipo() =>
        cmbTipoFilter.SelectedItem is ComboBoxItem ci ? ci.Tag as string ?? "" : "";

    private int GetFilterProjectId() =>
        cmbProjectFilter.SelectedValue is int id ? id : 0;

    private void UpdateConflictPanel(DateTime winEnd)
    {
        List<ConflictListItem> conflicts = _assignments
            .Where(a => a.HasConflict && a.DataFine.Date >= _windowStart && a.DataInizio.Date <= winEnd)
            .OrderBy(a => a.EmployeeName)
            .ThenBy(a => a.DataInizio)
            .Select(a => new ConflictListItem
            {
                AssignmentId = a.Id,
                EmployeeId = a.EmployeeId,
                Label = $"{a.EmployeeName} · {ResourcePlannerHelpers.AssignmentLabel(a)} · {a.DataInizio:dd/MM}→{a.DataFine:dd/MM}"
            })
            .ToList();

        lstConflicts.ItemsSource = conflicts;
        conflictPanel.Visibility = conflicts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border BuildBar(ResAssignmentDto a, Grid lane, bool clipLeft, bool clipRight)
    {
        (Brush bg, Brush fg) = ResourcePlannerHelpers.ColorsForTipo(a.Tipo);
        string label = (a.HasConflict ? "⚠ " : "") + ResourcePlannerHelpers.AssignmentLabel(a);

        TextBlock labelBlock = new()
        {
            Text = label,
            Foreground = fg,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll
        };

        Border leftGrip = new() { Width = BarGripWidth, Background = Brushes.Transparent, Cursor = Cursors.SizeWE, ToolTip = "Ridimensiona inizio" };
        Border rightGrip = new() { Width = BarGripWidth, Background = Brushes.Transparent, Cursor = Cursors.SizeWE, ToolTip = "Ridimensiona fine" };

        TextBlock? leftClip = clipLeft ? new TextBlock { Text = "◀", Foreground = fg, FontSize = 9, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0) } : null;
        TextBlock? rightClip = clipRight ? new TextBlock { Text = "▶", Foreground = fg, FontSize = 9, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 2, 0) } : null;

        Grid inner = new();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarGripWidth) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarGripWidth) });
        Grid.SetColumn(leftGrip, 0);
        Grid.SetColumn(labelBlock, 1);
        Grid.SetColumn(rightGrip, 2);
        inner.Children.Add(leftGrip);
        inner.Children.Add(labelBlock);
        inner.Children.Add(rightGrip);
        if (leftClip != null) { Grid.SetColumn(leftClip, 1); inner.Children.Add(leftClip); }
        if (rightClip != null) { Grid.SetColumn(rightClip, 1); inner.Children.Add(rightClip); }

        Border innerBar = new()
        {
            Background = bg,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Child = inner
        };

        GanttBarHost host = new()
        {
            Assignment = a,
            Inner = innerBar,
            GlowEffect = ResourcePlannerHelpers.HoverGlowEffect(a)
        };

        Border bar = new()
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(2, 4, 2, 4),
            ToolTip = ResourcePlannerHelpers.BuildTooltip(a, a.DataInizio.Date, a.DataFine.Date),
            Child = innerBar,
            Tag = host
        };
        ApplyBarVisualState(bar);

        leftGrip.MouseLeftButtonDown += (_, e) => BeginBarDrag(e, a, lane, bar, GanttDragMode.ResizeStart);
        labelBlock.MouseLeftButtonDown += (_, e) =>
        {
            SelectBar(bar, a);
            PrepareBarDrag(e, a, lane, bar, GanttDragMode.Move);
            e.Handled = true;
        };
        rightGrip.MouseLeftButtonDown += (_, e) => BeginBarDrag(e, a, lane, bar, GanttDragMode.ResizeEnd);
        bar.MouseEnter += Bar_MouseEnter;
        bar.MouseLeave += Bar_MouseLeave;
        bar.MouseLeftButtonUp += Bar_MouseLeftButtonUp;
        bar.LostMouseCapture += Bar_LostMouseCapture;

        // Menu Modifica/Duplica/Elimina solo con permesso di scrittura (sola lettura → nessun menu).
        if (_canEdit)
        {
            ContextMenu cm = new();
            MenuItem edit = new() { Header = "Modifica" };
            edit.Click += (_, _) => EditAssignment(a);
            MenuItem dup = new() { Header = "Duplica (+7 gg)" };
            dup.Click += (_, _) => DuplicateAssignment(a, 7);
            MenuItem del = new() { Header = "Elimina" };
            del.Click += async (_, _) => await DeleteAssignment(a);
            cm.Items.Add(edit);
            cm.Items.Add(dup);
            cm.Items.Add(del);
            bar.ContextMenu = ATEC.PM.Client.Helpers.ShadcnMenuHelper.ApplyDark(cm);
        }
        return bar;
    }

    private void Bar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border bar)
            SetBarHovered(bar, true);
    }

    private void Bar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not Border bar || bar.IsMouseOver)
            return;
        SetBarHovered(bar, false);
    }

    private void SetBarHovered(Border bar, bool hovered)
    {
        if (_dragMode != GanttDragMode.None || _barDragPending || _dragBar != null || _busy)
            return;

        if (hovered)
        {
            if (_hoveredBar != null && _hoveredBar != bar)
                SetBarHovered(_hoveredBar, false);
            _hoveredBar = bar;
        }
        else if (_hoveredBar == bar)
        {
            _hoveredBar = null;
        }

        ApplyBarVisualState(bar);
    }

    private void ApplyBarVisualState(Border bar)
    {
        if (bar.Tag is not GanttBarHost host)
            return;

        ResAssignmentDto a = host.Assignment;
        bool hovered = _hoveredBar == bar;
        bool selected = _selectedBar == bar;

        bar.Effect = hovered && !selected ? host.GlowEffect : null;

        host.Inner.BorderThickness = new Thickness(2);
        if (selected)
            host.Inner.BorderBrush = ResourcePlannerHelpers.SelectedBorder;
        else if (hovered)
            host.Inner.BorderBrush = ResourcePlannerHelpers.HoverGlowBrush(a);
        else if (a.HasConflict)
            host.Inner.BorderBrush = ResourcePlannerHelpers.ConflictBorder;
        else
            host.Inner.BorderBrush = Brushes.Transparent;
    }

    private void ClearBarHover(Border? bar)
    {
        if (bar == null)
            return;
        if (_hoveredBar == bar)
            _hoveredBar = null;
        ApplyBarVisualState(bar);
    }

    private void SelectBar(Border bar, ResAssignmentDto a)
    {
        Border? previous = _selectedBar;
        _selectedAssignment = a;
        _selectedBar = bar;
        if (previous != null && previous != bar)
            ApplyBarVisualState(previous);
        ApplyBarVisualState(bar);
    }

    private void ApplySelectionStyle(Border bar, bool selected)
    {
        if (bar.Tag is not GanttBarHost)
            return;
        ApplyBarVisualState(bar);
    }

    private void ScrollToEmployee(int employeeId)
    {
        // Porta in vista la cella nome della risorsa: gestisce correttamente le righe
        // multi-corsia (altezza variabile), a differenza di un offset = row * altezza fissa.
        // La NameCell sta in namesColumn (solo scroller verticale) → niente scroll orizzontale.
        LaneInfo? info = _lanes.FirstOrDefault(l => l.EmployeeId == employeeId);
        info?.NameCell.BringIntoView();
    }
}
