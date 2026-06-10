using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Drag & drop: barre Gantt, creazione su riga vuota, causali da legenda.
public partial class ResourcePlannerPage
{
    private void BeginCreateDrag(MouseButtonEventArgs e, int employeeId, Grid lane, Border cell)
    {
        if (!_canEdit || _dragMode != GanttDragMode.None || e.ClickCount >= 2 || _busy)
            return;

        int col = Grid.GetColumn(cell);
        _createStartCol = col;
        _dragMode = GanttDragMode.Create;
        _createEmployeeId = employeeId;
        _createLane = lane;
        _dragStartPoint = e.GetPosition(lane);
        _dragOrigStart = _windowStart.AddDays(col);
        _dragOrigEnd = _dragOrigStart;
        _dragPreviewStart = _dragOrigStart;
        _dragPreviewEnd = _dragOrigEnd;
        _dragMoved = false;

        _createPreviewBar = BuildCreatePreviewBar(null);
        Grid.SetColumn(_createPreviewBar, col);
        Grid.SetColumnSpan(_createPreviewBar, 1);
        Grid.SetRowSpan(_createPreviewBar, lane.RowDefinitions.Count);
        lane.Children.Add(_createPreviewBar);
        lane.CaptureMouse();
        lane.MouseMove += CreateLane_MouseMove;
        lane.MouseLeftButtonUp += CreateLane_MouseLeftButtonUp;
        SetDragStatusActive(true);
        txtStatus.Text = $"Nuova allocazione · {_dragOrigStart:dd/MM/yyyy}";
        e.Handled = true;
    }

    private static Border BuildCreatePreviewBar(string? tipo)
    {
        if (string.IsNullOrEmpty(tipo))
        {
            return new Border
            {
                Background = ResourcePlannerHelpers.CreatePreviewBg,
                Opacity = 0.75,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(1, 3, 1, 3),
                Child = new TextBlock
                {
                    Text = "Nuova",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };
        }

        (Brush bg, Brush fg) = ResourcePlannerHelpers.ColorsForTipo(tipo);
        return new Border
        {
            Background = bg,
            Opacity = 0.85,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(1, 3, 1, 3),
            Child = new TextBlock
            {
                Text = ResourcePlannerHelpers.TipoLabel(tipo),
                Foreground = fg,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
    }

    private void Legend_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tipo)
            return;
        if (!_canEdit || _dragMode != GanttDragMode.None || _busy)
            return;

        _legendDragTipo = tipo;
        _dragMoved = false;
        CaptureMouse();
        PreviewMouseMove += LegendDrag_MouseMove;
        PreviewMouseLeftButtonUp += LegendDrag_MouseUp;
        SetDragStatusActive(true);
        txtStatus.Text = $"Trascina {ResourcePlannerHelpers.TipoLabel(tipo)} su un giorno nel Gantt (1 gg)";
        e.Handled = true;
    }

    private void LegendDrag_MouseMove(object sender, MouseEventArgs e)
    {
        if (_legendDragTipo == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point posTimeline = e.GetPosition(timelineBody);
        LaneInfo? laneInfo = HitTestLane(posTimeline);
        if (laneInfo == null)
        {
            RemoveCreatePreviewOnly();
            HideDragTooltip();
            txtStatus.Text = $"Trascina {ResourcePlannerHelpers.TipoLabel(_legendDragTipo)} su un giorno nel Gantt (1 gg)";
            _dragMoved = false;
            return;
        }

        Point posLane = e.GetPosition(laneInfo.Lane);
        int col = ColumnFromLaneX(posLane.X, laneInfo.Lane);

        if (_createLane != laneInfo.Lane || _createPreviewBar == null)
        {
            RemoveCreatePreviewOnly();
            EnsureCreatePreviewOnLane(laneInfo, col, _legendDragTipo);
        }
        else if (_createStartCol != col)
        {
            _createStartCol = col;
            _dragPreviewStart = _windowStart.AddDays(col);
            _dragPreviewEnd = _dragPreviewStart;
            Grid.SetColumn(_createPreviewBar, col);
            Grid.SetColumnSpan(_createPreviewBar, 1);
        }

        _dragMoved = true;
        txtStatus.Text = $"{ResourcePlannerHelpers.TipoLabel(_legendDragTipo)} · {laneInfo.EmployeeName} · {_dragPreviewStart:dd/MM/yyyy} (1 gg)";
        ShowDragTooltip(_dragPreviewStart, _dragPreviewEnd, e);
    }

    private async void LegendDrag_MouseUp(object sender, MouseButtonEventArgs e)
    {
        PreviewMouseMove -= LegendDrag_MouseMove;
        PreviewMouseLeftButtonUp -= LegendDrag_MouseUp;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        HideDragTooltip();

        string? tipo = _legendDragTipo;
        _legendDragTipo = null;

        if (_dragMode != GanttDragMode.Create || !_dragMoved || tipo == null || _createPreviewBar == null)
        {
            EndCreateDrag();
            SetDragStatusActive(false);
            UpdateDefaultStatus();
            return;
        }

        int employeeId = _createEmployeeId;
        DateTime day = _dragPreviewStart.Date;
        EndCreateDrag();
        await CreateAssignmentDirectAsync(employeeId, day, day, tipo);
    }

    private void EnsureCreatePreviewOnLane(LaneInfo laneInfo, int col, string tipo)
    {
        _dragMode = GanttDragMode.Create;
        _createEmployeeId = laneInfo.EmployeeId;
        _createLane = laneInfo.Lane;
        _createStartCol = col;
        _dragPreviewStart = _windowStart.AddDays(col);
        _dragPreviewEnd = _dragPreviewStart;

        _createPreviewBar = BuildCreatePreviewBar(tipo);
        Grid.SetColumn(_createPreviewBar, col);
        Grid.SetColumnSpan(_createPreviewBar, 1);
        Grid.SetRowSpan(_createPreviewBar, laneInfo.Lane.RowDefinitions.Count);
        laneInfo.Lane.Children.Add(_createPreviewBar);
    }

    private void RemoveCreatePreviewOnly()
    {
        if (_createPreviewBar != null && _createLane != null)
            _createLane.Children.Remove(_createPreviewBar);
        _createPreviewBar = null;
        _createLane = null;
        if (_legendDragTipo != null)
            _dragMode = GanttDragMode.None;
    }

    private LaneInfo? HitTestLane(Point posInTimelineBody)
    {
        foreach (LaneInfo info in _lanes)
        {
            Point origin = info.Lane.TranslatePoint(new Point(0, 0), timelineBody);
            if (posInTimelineBody.Y >= origin.Y
                && posInTimelineBody.Y < origin.Y + info.Lane.ActualHeight
                && posInTimelineBody.X >= origin.X
                && posInTimelineBody.X < origin.X + info.Lane.ActualWidth)
                return info;
        }
        return null;
    }

    private int ColumnFromLaneX(double x, Grid lane)
    {
        double colWidth = lane.ActualWidth / _windowDays;
        if (colWidth <= 0)
            return 0;
        return Math.Clamp((int)(x / colWidth), 0, _windowDays - 1);
    }

    private void CreateLane_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragMode != GanttDragMode.Create || _createLane == null || _createPreviewBar == null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        Point pos = e.GetPosition(_createLane);
        double colWidth = _createLane.ActualWidth / _windowDays;
        if (colWidth <= 0) return;

        int startCol = _createStartCol;
        int endCol = (int)(pos.X / colWidth);
        startCol = Math.Clamp(startCol, 0, _windowDays - 1);
        endCol = Math.Clamp(endCol, 0, _windowDays - 1);
        if (endCol < startCol) (startCol, endCol) = (endCol, startCol);

        _dragMoved = Math.Abs(endCol - _createStartCol) > 0 || Math.Abs(pos.X - _dragStartPoint.X) >= DragPixelThreshold;
        _dragPreviewStart = _windowStart.AddDays(startCol);
        _dragPreviewEnd = _windowStart.AddDays(endCol);
        Grid.SetColumn(_createPreviewBar, startCol);
        Grid.SetColumnSpan(_createPreviewBar, endCol - startCol + 1);
        txtStatus.Text = $"Nuova allocazione · {_dragPreviewStart:dd/MM/yyyy} → {_dragPreviewEnd:dd/MM/yyyy}";
        ShowDragTooltip(_dragPreviewStart, _dragPreviewEnd, e);
    }

    private async void CreateLane_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode != GanttDragMode.Create) return;

        int employeeId = _createEmployeeId;
        DateTime start = _dragPreviewStart;
        DateTime end = _dragPreviewEnd;
        bool moved = _dragMoved;
        EndCreateDrag();

        if (!moved) return;

        AssignmentDialog dlg = new(_resources, _projects, _services, _others,
            presetEmployeeId: employeeId, presetStart: start, presetEnd: end)
        { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        await PostAssignmentAsync(dlg.CreateRequest);
    }

    private void EndCreateDrag()
    {
        if (_createLane != null)
        {
            _createLane.MouseMove -= CreateLane_MouseMove;
            _createLane.MouseLeftButtonUp -= CreateLane_MouseLeftButtonUp;
            if (_createLane.IsMouseCaptured) _createLane.ReleaseMouseCapture();
            if (_createPreviewBar != null) _createLane.Children.Remove(_createPreviewBar);
        }
        _dragMode = GanttDragMode.None;
        _createLane = null;
        _createPreviewBar = null;
        _dragMoved = false;
        SetDragStatusActive(false);
        HideDragTooltip();
    }

    private void PrepareBarDrag(MouseButtonEventArgs e, ResAssignmentDto a, Grid lane, Border bar, GanttDragMode mode)
    {
        // Sola lettura: niente spostamento né doppio-click→modifica (la selezione resta consentita).
        if (!_canEdit || _dragMode != GanttDragMode.None || _barDragPending || _busy)
            return;

        _barDragPending = true;
        _pendingDragMode = mode;
        _dragItem = a;
        _dragOrigStart = a.DataInizio.Date;
        _dragOrigEnd = a.DataFine.Date;
        _dragPreviewStart = _dragOrigStart;
        _dragPreviewEnd = _dragOrigEnd;
        _dragOrigEmployeeId = a.EmployeeId;
        _dragLane = lane;
        _dragBar = bar;
        _dragMoved = false;
        _dragCopyMode = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        _dragStartPoint = e.GetPosition(lane);

        ClearBarHover(_hoveredBar);
        bar.PreviewMouseMove += Bar_PendingMouseMove;
        bar.PreviewMouseLeftButtonUp += Bar_PendingMouseUp;
        // Cattura SUBITO il mouse: senza cattura i Preview* della barra arrivano solo
        // finché il puntatore è sopra la barra (stretta). Una passata laterale veloce
        // esce dalla barra prima del primo MouseMove → niente commit e niente MouseUp →
        // _barDragPending resta true per sempre e drag/hover si bloccano. La cattura
        // garantisce che move/up arrivino comunque, anche fuori dalla barra.
        bar.CaptureMouse();
    }

    private void Bar_PendingMouseMove(object sender, MouseEventArgs e)
    {
        if (!_barDragPending || _dragLane == null || _dragBar == null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        Point pos = e.GetPosition(_dragLane);
        if (Math.Abs(pos.X - _dragStartPoint.X) < DragPixelThreshold
            && Math.Abs(pos.Y - _dragStartPoint.Y) < VerticalDragThreshold)
            return;

        CommitBarDragStart();
        DragLane_MouseMove(sender, e);
    }

    private void Bar_PendingMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_barDragPending || _dragItem == null)
            return;

        ResAssignmentDto item = _dragItem;
        ResetPendingDrag();

        double elapsedMs = (DateTime.UtcNow - _lastBarClickUtc).TotalMilliseconds;
        if (elapsedMs <= BarDoubleClickMs && _lastBarClickId == item.Id)
        {
            _lastBarClickUtc = DateTime.MinValue;
            _lastBarClickId = 0;
            EditAssignment(item);
            e.Handled = true;
            return;
        }

        _lastBarClickUtc = DateTime.UtcNow;
        _lastBarClickId = item.Id;
    }

    private void CommitBarDragStart()
    {
        if (!_barDragPending || _dragBar == null)
            return;

        // ORDINE CRITICO: leggi _pendingDragMode PRIMA di ClearPendingBarDragHandlers(),
        // che lo azzera a None. Invertito, _dragMode restava None → DragLane_MouseMove
        // usciva subito (barra ferma) e al mouse-up Bar_MouseLeftButtonUp tornava senza
        // EndBarDrag → cattura mai rilasciata → UI inchiodata. (Causa del freeze del Move.)
        _dragMode = _pendingDragMode;
        ClearPendingBarDragHandlers();
        ClearBarHover(_dragBar);
        _dragBar.CaptureMouse();
        _dragBar.Opacity = 0.82;
        _dragLane!.MouseMove += DragLane_MouseMove;
        ShowDragHint(_dragMode, _dragOrigStart, _dragOrigEnd, _dragCopyMode);
    }

    private void ClearPendingBarDragHandlers()
    {
        if (_dragBar != null)
        {
            _dragBar.PreviewMouseMove -= Bar_PendingMouseMove;
            _dragBar.PreviewMouseLeftButtonUp -= Bar_PendingMouseUp;
        }
        _barDragPending = false;
        _pendingDragMode = GanttDragMode.None;
    }

    // Chiusura di un drag "pending" che NON è mai diventato attivo (click semplice,
    // cattura persa, annullamento). Stacca gli handler, rilascia la cattura e azzera
    // i riferimenti, altrimenti _dragBar resta valorizzato e SetBarHovered (che esce
    // se _dragBar != null) blocca l'hover dopo il primo click su una barra.
    private void ResetPendingDrag()
    {
        ClearPendingBarDragHandlers();
        if (_dragBar != null && _dragBar.IsMouseCaptured)
            _dragBar.ReleaseMouseCapture();
        _dragItem = null;
        _dragLane = null;
        _dragBar = null;
    }

    private void BeginBarDrag(MouseButtonEventArgs e, ResAssignmentDto a, Grid lane, Border bar, GanttDragMode mode)
    {
        if (!_canEdit || _dragMode != GanttDragMode.None || _barDragPending || _busy)
            return;

        _dragMode = mode;
        _dragItem = a;
        _dragOrigStart = a.DataInizio.Date;
        _dragOrigEnd = a.DataFine.Date;
        _dragPreviewStart = _dragOrigStart;
        _dragPreviewEnd = _dragOrigEnd;
        _dragOrigEmployeeId = a.EmployeeId;
        _dragLane = lane;
        _dragBar = bar;
        _dragMoved = false;
        _dragCopyMode = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        _dragStartPoint = e.GetPosition(lane);
        ClearBarHover(_hoveredBar);
        bar.CaptureMouse();
        bar.Opacity = 0.82;
        lane.MouseMove += DragLane_MouseMove;
        ShowDragHint(mode, _dragOrigStart, _dragOrigEnd, _dragCopyMode);
        e.Handled = true;
    }

    private void DragLane_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragMode == GanttDragMode.None || _dragLane == null || _dragItem == null || _dragBar == null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        Point pos = e.GetPosition(_dragLane);
        if (!_dragMoved && Math.Abs(pos.X - _dragStartPoint.X) < DragPixelThreshold && Math.Abs(pos.Y - _dragStartPoint.Y) < VerticalDragThreshold)
            return;

        double colWidth = _dragLane.ActualWidth / _windowDays;
        if (colWidth <= 0) return;

        _dragMoved = true;
        // Auto-pan: se il puntatore è sul bordo del viewport, fa avanzare la finestra.
        UpdateAutoPan(e);

        // SOLO asse X: la coordinata verticale del puntatore è ignorata → l'attività resta
        // sempre sulla stessa risorsa, si può spostare unicamente a destra/sinistra.
        double dxPx = pos.X - _dragStartPoint.X;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        int dayDelta = (int)Math.Round(dxPx / colWidth);
        if (shift)
            dayDelta = SnapDayDelta(_dragMode, dayDelta);
        (DateTime newStart, DateTime newEnd) = ClampDragDates(_dragMode, dayDelta);

        // VISUALE: a pixel (fluida) di default; a colonne (scatti di settimana) con Shift.
        if (shift)
            ApplyBarPreview(_dragBar, newStart, newEnd);
        else
            ApplyBarPreviewPixels(_dragBar, _dragMode, colWidth, dxPx);

        // STATO/CONFLITTO/LOG: solo quando cambia il giorno "snappato" (le date salvate
        // sono comunque arrotondate; al rilascio la barra scatta sul giorno più vicino).
        if (newStart != _dragPreviewStart || newEnd != _dragPreviewEnd)
        {
            _dragPreviewStart = newStart;
            _dragPreviewEnd = newEnd;
            SetDragStatusActive(true);
            string prefix = _dragCopyMode ? "Copia · " : "";
            ShowDragStatus(_dragMode, _dragOrigStart, _dragOrigEnd, newStart, newEnd, txtStatus, prefix, _dragItem.Tipo);

            bool wouldConflict = ResourcePlannerHelpers.WouldConflict(
                _assignments, _dragOrigEmployeeId, newStart, newEnd, _dragItem.Tipo, _dragCopyMode ? 0 : _dragItem.Id);
            if (wouldConflict)
                txtStatus.Text += "  ·  ⚠ conflitto";
        }

        ShowDragTooltip(newStart, newEnd, e, _dragItem.Tipo);
    }

    private static int SnapDayDelta(GanttDragMode mode, int dayDelta)
    {
        if (mode == GanttDragMode.Move)
            return (int)Math.Round(dayDelta / 7.0) * 7;
        return dayDelta >= 0 ? (int)Math.Ceiling(dayDelta / 7.0) * 7 : (int)Math.Floor(dayDelta / 7.0) * 7;
    }

    private async void Bar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode == GanttDragMode.None || _dragItem == null || _dragBar == null)
        {
            // Rete di sicurezza: non lasciare MAI il mouse catturato senza un drag attivo,
            // altrimenti la UI resta inchiodata (ogni click va alla barra catturata).
            if (sender is Border b && b.IsMouseCaptured)
                b.ReleaseMouseCapture();
            return;
        }

        ResAssignmentDto item = _dragItem;
        bool moved = _dragMoved;
        bool copy = _dragCopyMode;
        DateTime previewStart = _dragPreviewStart;
        DateTime previewEnd = _dragPreviewEnd;
        // Spostamento solo orizzontale: la risorsa non cambia mai.
        int targetEmployee = _dragOrigEmployeeId;
        EndBarDrag();

        if (!moved)
            return;

        if (previewStart != item.DataInizio.Date || previewEnd != item.DataFine.Date || targetEmployee != item.EmployeeId)
        {
            if (copy)
                await DuplicateAssignmentAsync(item, previewStart, previewEnd, targetEmployee);
            else
                await SaveAssignmentAsync(item, previewStart, previewEnd, targetEmployee);
        }
        else
            RenderGantt();
    }

    private void Bar_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // Durante l'auto-pan la vecchia barra esce dall'albero (RenderGantt) e perde la
        // cattura: atteso, ri-agganciamo noi sulla nuova barra. Niente abort.
        if (_reacquiring)
            return;

        if (_barDragPending)
        {
            ResetPendingDrag();
            return;
        }

        if (_dragMode == GanttDragMode.None)
            return;

        bool moved = _dragMoved;
        EndBarDrag();
        if (moved)
            RenderGantt();
    }

    private void CancelBarDrag()
    {
        if (_barDragPending)
        {
            ResetPendingDrag();
            _lastBarClickUtc = DateTime.MinValue;
            _lastBarClickId = 0;
        }

        if (_legendDragTipo != null)
        {
            PreviewMouseMove -= LegendDrag_MouseMove;
            PreviewMouseLeftButtonUp -= LegendDrag_MouseUp;
            _legendDragTipo = null;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            EndCreateDrag();
            RenderGantt();
            ShowToast("Operazione annullata");
            return;
        }

        if (_dragMode == GanttDragMode.Create)
        {
            EndCreateDrag();
            RenderGantt();
            ShowToast("Operazione annullata");
            return;
        }
        if (_dragMode == GanttDragMode.None)
            return;
        EndBarDrag();
        RenderGantt();
        ShowToast("Operazione annullata");
    }

    private void EndBarDrag()
    {
        ClearPendingBarDragHandlers();
        StopAutoPan();
        _dragRender = null;

        if (_dragLane != null)
            _dragLane.MouseMove -= DragLane_MouseMove;

        // Azzera lo stato PRIMA di rilasciare la cattura: ReleaseMouseCapture rilancia
        // LostMouseCapture in modo sincrono, e con _dragMode già None il rientro in
        // Bar_LostMouseCapture esce subito (niente EndBarDrag/RenderGantt doppi).
        Border? bar = _dragBar;
        _dragMode = GanttDragMode.None;
        _dragItem = null;
        _dragLane = null;
        _dragBar = null;
        _dragMoved = false;
        _dragCopyMode = false;
        HideDragTooltip();

        if (bar != null)
        {
            bar.Opacity = 1;
            if (bar.IsMouseCaptured) bar.ReleaseMouseCapture();
        }
    }

    // Anteprima "a colonne" (discreta): usata per lo snap-settimana (Shift) e per il
    // pin durante l'auto-pan. Resetta l'eventuale stato pixel lasciato dal drag fluido.
    private void ApplyBarPreview(Border bar, DateTime start, DateTime end)
    {
        ResetBarPixelMode(bar);

        int s = (start - _windowStart).Days;
        int eDay = (end - _windowStart).Days;
        int s2 = Math.Clamp(s, 0, _windowDays - 1);
        int e2 = Math.Clamp(eDay, 0, _windowDays - 1);
        if (e2 < s2) e2 = s2;

        bar.Visibility = Visibility.Visible;
        Grid.SetColumn(bar, s2);
        Grid.SetColumnSpan(bar, Math.Max(1, e2 - s2 + 1));
    }

    private static void ResetBarPixelMode(Border bar)
    {
        bar.HorizontalAlignment = HorizontalAlignment.Stretch;
        bar.Width = double.NaN;
        bar.Margin = new Thickness(2, 4, 2, 4);
    }

    // Anteprima FLUIDA a pixel: la barra segue il cursore in continuo (niente snap al
    // giorno durante il trascinamento). Span sull'intera corsia + HorizontalAlignment
    // Left + Width/Margin espliciti = posizionamento pixel-preciso. Lo snap al giorno
    // avviene solo al rilascio (le date in _dragPreviewStart/End restano arrotondate).
    private void ApplyBarPreviewPixels(Border bar, GanttDragMode mode, double colWidth, double dxPx)
    {
        double laneWidth = colWidth * _windowDays;
        double origLeftPx = (_dragOrigStart - _windowStart).Days * colWidth;
        double origWidthPx = ((_dragOrigEnd - _dragOrigStart).Days + 1) * colWidth;
        const double minW = 8;

        double leftPx, rightPx;
        switch (mode)
        {
            case GanttDragMode.Move:
                // Pin: la barra trasla ma resta INTERA dentro la finestra (niente troncamento).
                leftPx = Math.Clamp(origLeftPx + dxPx, 0, Math.Max(0, laneWidth - origWidthPx));
                rightPx = leftPx + origWidthPx;
                break;
            case GanttDragMode.ResizeEnd:
                leftPx = origLeftPx;
                rightPx = origLeftPx + origWidthPx + dxPx;
                if (rightPx < leftPx + minW) rightPx = leftPx + minW;
                break;
            default: // ResizeStart
                rightPx = origLeftPx + origWidthPx;
                leftPx = origLeftPx + dxPx;
                if (leftPx > rightPx - minW) leftPx = rightPx - minW;
                break;
        }

        // Clip al lato visibile della corsia (per il resize che sfora il bordo).
        double dispLeft = Math.Max(0, leftPx);
        double dispRight = Math.Min(laneWidth, rightPx);
        double dispWidth = Math.Max(minW, dispRight - dispLeft);

        Grid.SetColumn(bar, 0);
        Grid.SetColumnSpan(bar, _windowDays);
        bar.HorizontalAlignment = HorizontalAlignment.Left;
        bar.Margin = new Thickness(dispLeft + 2, 4, 0, 4);
        bar.Width = Math.Max(1, dispWidth - 4);
        bar.Visibility = Visibility.Visible;
    }

    private (DateTime Start, DateTime End) ClampDragDates(GanttDragMode mode, int dayDelta)
    {
        DateTime newStart = _dragOrigStart;
        DateTime newEnd = _dragOrigEnd;

        switch (mode)
        {
            case GanttDragMode.Move:
            {
                // Trasla mantenendo la durata, ma resta DENTRO la finestra visibile: così
                // la barra non viene mai troncata ai bordi (niente "si accorcia fino a
                // sparire"). Per spostarla oltre il periodo mostrato si pagina con ◀/▶.
                int span = (_dragOrigEnd - _dragOrigStart).Days;
                DateTime winEnd = _windowStart.AddDays(_windowDays - 1);
                DateTime maxStart = winEnd.AddDays(-span);
                DateTime candidate = _dragOrigStart.AddDays(dayDelta);
                if (candidate > maxStart) candidate = maxStart;
                if (candidate < _windowStart) candidate = _windowStart;
                newStart = candidate;
                newEnd = candidate.AddDays(span);
                break;
            }
            case GanttDragMode.ResizeStart:
                newStart = _dragOrigStart.AddDays(dayDelta);
                if (newStart > _dragOrigEnd) newStart = _dragOrigEnd;
                break;
            case GanttDragMode.ResizeEnd:
                newEnd = _dragOrigEnd.AddDays(dayDelta);
                if (newEnd < _dragOrigStart) newEnd = _dragOrigStart;
                break;
        }

        return (newStart, newEnd);
    }

    private static void ShowDragStatus(GanttDragMode mode, DateTime origStart, DateTime origEnd, DateTime newStart, DateTime newEnd, TextBlock target, string prefix = "", string tipo = "")
    {
        int giorni = ResourcePlannerHelpers.DisplayDayCount(tipo, newStart, newEnd);
        target.Text = prefix + (mode switch
        {
            GanttDragMode.Move => FormatMoveStatus(origStart, origEnd, newStart, newEnd),
            GanttDragMode.ResizeStart => $"Inizio: {origStart:dd/MM/yyyy} → {newStart:dd/MM/yyyy}  ·  {giorni} gg",
            GanttDragMode.ResizeEnd => $"Fine: {origEnd:dd/MM/yyyy} → {newEnd:dd/MM/yyyy}  ·  {giorni} gg",
            _ => target.Text
        });
    }

    private static string FormatMoveStatus(DateTime origStart, DateTime origEnd, DateTime newStart, DateTime newEnd)
    {
        int delta = (newStart - origStart).Days;
        string deltaText = delta == 0 ? "" : $"  ·  {delta:+0;-0} gg";
        return $"Spostamento: {origStart:dd/MM/yyyy}–{origEnd:dd/MM/yyyy} → {newStart:dd/MM/yyyy}–{newEnd:dd/MM/yyyy}{deltaText}";
    }

    private void ShowDragHint(GanttDragMode mode, DateTime start, DateTime end, bool copy)
    {
        SetDragStatusActive(true);
        string copyHint = copy ? " (copia) · " : " · ";
        txtStatus.Text = mode switch
        {
            GanttDragMode.Move => $"{(copy ? "Alt: copia" : "Trascina")}{copyHint}{start:dd/MM/yyyy} → {end:dd/MM/yyyy}",
            GanttDragMode.ResizeStart => $"Trascina inizio · {start:dd/MM/yyyy}",
            GanttDragMode.ResizeEnd => $"Trascina fine · {end:dd/MM/yyyy}",
            _ => txtStatus.Text
        };
    }

    private void ShowDragTooltip(DateTime start, DateTime end, MouseEventArgs e, string tipo = "")
    {
        int giorni = ResourcePlannerHelpers.DisplayDayCount(tipo, start, end);
        txtDragTooltip.Text = $"{start:dd/MM/yyyy} → {end:dd/MM/yyyy} · {giorni} gg";
        MoveDragTooltip(e);
        dragTooltipPopup.IsOpen = true;
    }

    private void MoveDragTooltip(MouseEventArgs e)
    {
        Point pos = e.GetPosition(this);
        dragTooltipPopup.PlacementTarget = this;
        dragTooltipPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
        dragTooltipPopup.HorizontalOffset = pos.X + 14;
        dragTooltipPopup.VerticalOffset = pos.Y + 16;
    }

    private void HideDragTooltip() => dragTooltipPopup.IsOpen = false;

    // ── Auto-pan ai bordi durante il Move ───────────────────────
    // Trigger sul PUNTATORE: finché resta entro AutoPanEdgeMargin dal bordo del viewport,
    // un timer fa avanzare la finestra di ±7 gg e ri-aggancia la barra al bordo a
    // dimensione piena. Solo per il Move (lo spostamento), non per il resize.

    private void UpdateAutoPan(MouseEventArgs e)
    {
        if (_dragMode != GanttDragMode.Move)
        {
            StopAutoPan();
            return;
        }

        double w = bodyHScroll.ViewportWidth;
        if (w <= 0) { StopAutoPan(); return; }

        double x = e.GetPosition(bodyHScroll).X;
        int dir = x < AutoPanEdgeMargin ? -1 : x > w - AutoPanEdgeMargin ? 1 : 0;
        if (dir == 0)
            StopAutoPan();
        else
            StartAutoPan(dir);
    }

    private void StartAutoPan(int dir)
    {
        _autoPanDir = dir;
        if (_autoPanTimer == null)
        {
            _autoPanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoPanIntervalMs) };
            _autoPanTimer.Tick += (_, _) => AutoPanStep();
        }
        if (!_autoPanTimer.IsEnabled)
            _autoPanTimer.Start();
    }

    private void StopAutoPan()
    {
        _autoPanDir = 0;
        _autoPanTimer?.Stop();
    }

    private void AutoPanStep()
    {
        if (_dragMode != GanttDragMode.Move || _autoPanDir == 0
            || _dragItem == null || _dragBar == null)
        {
            StopAutoPan();
            return;
        }

        int span = (_dragOrigEnd - _dragOrigStart).Days;
        _windowStart = _windowStart.AddDays(_autoPanDir * PanStepDays);
        DateTime winEnd = _windowStart.AddDays(_windowDays - 1);

        // Aggancia la barra al bordo verso cui scorriamo, a dimensione piena (niente
        // troncamento). La preview avanza con la finestra → al rilascio si salva qui.
        DateTime newStart, newEnd;
        if (_autoPanDir > 0) { newEnd = winEnd; newStart = winEnd.AddDays(-span); }
        else { newStart = _windowStart; newEnd = _windowStart.AddDays(span); }
        _dragPreviewStart = newStart;
        _dragPreviewEnd = newEnd;
        _dragMoved = true;

        ResAssignmentDto item = _dragItem;
        // RenderBody disegnerà la barra a queste date (override) anche se la data reale
        // del DTO è ormai fuori finestra.
        _dragRender = (item.Id, _dragOrigEmployeeId, newStart, newEnd);

        _reacquiring = true;
        try
        {
            RenderGantt();
            timelineBody.UpdateLayout();

            LaneInfo? laneInfo = _lanes.FirstOrDefault(l => l.EmployeeId == _dragOrigEmployeeId);
            Border? bar = FindBarInLane(laneInfo, item.Id);
            if (laneInfo == null || bar == null)
            {
                // Corsia/barra non ritrovata (filtro che la nasconde): chiudi in sicurezza.
                StopAutoPan();
                EndBarDrag();
                return;
            }

            if (_dragLane != null)
                _dragLane.MouseMove -= DragLane_MouseMove;
            _dragLane = laneInfo.Lane;
            _dragBar = bar;
            _dragLane.MouseMove += DragLane_MouseMove;
            bar.CaptureMouse();
            bar.Opacity = 0.82;

            // Ricalibra l'origine del gesto sul nuovo layout, così quando l'utente si
            // stacca dal bordo il movimento riparte senza salti.
            double colWidth = _dragLane.ActualWidth / _windowDays;
            if (colWidth > 0)
            {
                int appliedDelta = (newStart - _dragOrigStart).Days;
                Point m = Mouse.GetPosition(_dragLane);
                _dragStartPoint = new Point(m.X - appliedDelta * colWidth, m.Y);
            }

            ApplyBarPreview(bar, newStart, newEnd);
            SetDragStatusActive(true);
            ShowDragStatus(GanttDragMode.Move, _dragOrigStart, _dragOrigEnd, newStart, newEnd, txtStatus,
                _dragCopyMode ? "Copia · " : "");
        }
        finally
        {
            _reacquiring = false;
        }
    }

    private static Border? FindBarInLane(LaneInfo? laneInfo, int assignmentId)
    {
        if (laneInfo == null)
            return null;
        foreach (UIElement ch in laneInfo.Lane.Children)
            if (ch is Border b && b.Tag is GanttBarHost h && h.Assignment.Id == assignmentId)
                return b;
        return null;
    }
}
