using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Drag della timeline ferie con PARITÀ rispetto al gantt principale: move/resize
// pixel-smooth (snap al rilascio), create per trascinamento, e AUTO-PAN ai bordi.
//
// Tecnica: la cattura del mouse è su `timelineBody` (elemento STABILE che sopravvive
// al re-render). Così l'auto-pan può fare Render() senza perdere la cattura né dover
// ri-catturare la barra (a differenza del planner). La barra è solo visuale.
public partial class FerieDashboardWindow
{
    private enum FDragMode { None, Move, ResizeStart, ResizeEnd, Create }

    private FDragMode _dragMode = FDragMode.None;
    private ResAssignmentDto? _dragItem;
    private Grid? _dragLane;
    private Border? _dragBar;
    private Border? _createBar;
    private int _createEmployeeId;
    private DateTime _createAnchor;
    private DateTime _dragOrigStart, _dragOrigEnd, _dragPreviewStart, _dragPreviewEnd;
    private Point _dragStartPoint; // in coordinate timelineBody
    private bool _dragMoved;

    // Override di rendering: durante l'auto-pan la barra trascinata va disegnata alle
    // date di preview (la data reale è ormai fuori finestra). Letto da RenderBody.
    private (int Id, int EmployeeId, DateTime Start, DateTime End)? _dragRender;

    private DispatcherTimer? _autoPanTimer;
    private int _autoPanDir;

    private const double DragPixelThreshold = 4;
    private const double BarGripWidth = 6;
    private const double AutoPanEdgeMargin = 26;
    private const int AutoPanIntervalMs = 180;

    // Date da usare in RenderBody per una barra (override se è quella in auto-pan).
    private (DateTime Start, DateTime End) DragDates(ResAssignmentDto a)
    {
        if (_dragRender is { } dr && dr.Id == a.Id)
            return (dr.Start, dr.End);
        return (a.DataInizio.Date, a.DataFine.Date);
    }

    // ── Avvio drag ──────────────────────────────────────────────

    private void BeginBarDrag(MouseButtonEventArgs e, ResAssignmentDto a, Grid lane, Border bar, FDragMode mode)
    {
        if (_dragMode != FDragMode.None || _busy) return;
        _dragMode = mode;
        _dragItem = a;
        _dragLane = lane;
        _dragBar = bar;
        _dragOrigStart = a.DataInizio.Date;
        _dragOrigEnd = a.DataFine.Date;
        _dragPreviewStart = _dragOrigStart;
        _dragPreviewEnd = _dragOrigEnd;
        _dragStartPoint = e.GetPosition(timelineBody);
        _dragMoved = false;
        bar.Opacity = 0.85;
        AttachDragCapture();
        e.Handled = true;
    }

    private void BeginCreateDrag(MouseButtonEventArgs e, int employeeId, Grid lane, int startCol)
    {
        if (_dragMode != FDragMode.None || _busy) return;
        _dragMode = FDragMode.Create;
        _createEmployeeId = employeeId;
        _dragLane = lane;
        _createAnchor = _windowStart.AddDays(startCol);
        _dragPreviewStart = _createAnchor;
        _dragPreviewEnd = _createAnchor;
        _dragStartPoint = e.GetPosition(timelineBody);
        _dragMoved = false;
        _createBar = NewCreatePreviewBorder();
        Grid.SetRowSpan(_createBar, Math.Max(1, lane.RowDefinitions.Count));
        lane.Children.Add(_createBar);
        PlaceCreateBar();
        AttachDragCapture();
        e.Handled = true;
    }

    private void AttachDragCapture()
    {
        timelineBody.MouseMove += Drag_MouseMove;
        timelineBody.MouseLeftButtonUp += Drag_MouseUp;
        timelineBody.LostMouseCapture += Drag_LostCapture;
        timelineBody.CaptureMouse();
    }

    // ── MouseMove ───────────────────────────────────────────────

    private void Drag_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragMode == FDragMode.None || e.LeftButton != MouseButtonState.Pressed) return;
        double laneWidth = timelineBody.ActualWidth;
        double colWidth = laneWidth / _windowDays;
        if (colWidth <= 0) return;
        double x = e.GetPosition(timelineBody).X;

        UpdateAutoPan(x, laneWidth);

        if (_dragMode == FDragMode.Create)
        {
            if (!_dragMoved && Math.Abs(x - _dragStartPoint.X) < DragPixelThreshold) return;
            _dragMoved = true;
            int col = Math.Clamp((int)(x / colWidth), 0, _windowDays - 1);
            UpdateCreatePreview(_windowStart.AddDays(col));
            return;
        }

        if (_dragBar == null || _dragItem == null) return;
        if (!_dragMoved && Math.Abs(x - _dragStartPoint.X) < DragPixelThreshold) return;
        _dragMoved = true;

        double dxPx = x - _dragStartPoint.X;
        int dayDelta = (int)Math.Round(dxPx / colWidth);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            dayDelta = (int)Math.Round(dayDelta / 7.0) * 7; // snap a settimana
            dxPx = dayDelta * colWidth;
        }
        ApplyBarPixels(_dragBar, _dragMode, colWidth, dxPx);
        (_dragPreviewStart, _dragPreviewEnd) = SnapDates(_dragMode, dayDelta);
        ShowDragStatus();
    }

    // Esc annulla un drag in corso (parità col gantt principale).
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dragMode != FDragMode.None)
        {
            EndDrag();
            Render();
            txtStatus.Text = "Operazione annullata";
            e.Handled = true;
        }
    }

    // Rotella: Ctrl = zoom finestra (±7 gg, 7..84), Shift = pan ±7 gg. (Come il gantt.)
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_dragMode != FDragMode.None) return;

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            int step = e.Delta > 0 ? -7 : 7;
            int clamped = Math.Clamp(_windowDays + step, 7, 84);
            if (clamped == _windowDays) return;
            _windowDays = clamped;
            SyncWindowCombo();
            Render();
        }
        else if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            _windowStart = _windowStart.AddDays(e.Delta > 0 ? -7 : 7);
            Render();
        }
    }

    private void UpdateCreatePreview(DateTime cur)
    {
        _dragPreviewStart = _createAnchor <= cur ? _createAnchor : cur;
        _dragPreviewEnd = _createAnchor <= cur ? cur : _createAnchor;
        PlaceCreateBar();
        ShowCreateStatus();
    }

    // ── MouseUp / fine ──────────────────────────────────────────

    private async void Drag_MouseUp(object sender, MouseButtonEventArgs e)
    {
        FDragMode mode = _dragMode;
        if (mode == FDragMode.None) return;

        ResAssignmentDto? item = _dragItem;
        int emp = _createEmployeeId;
        bool moved = _dragMoved;
        DateTime ps = _dragPreviewStart, pe = _dragPreviewEnd;
        EndDrag();

        if (mode == FDragMode.Create)
        {
            if (moved) await CreateFerieDirectAsync(emp, ps, pe); // clic singolo = niente (usa "+")
        }
        else if (item != null && moved)
        {
            await SaveFerieDatesAsync(item, ps, pe);
        }
        // Clic singolo su una barra senza trascinamento: nessuna azione.
        // (Doppio click = form modifica date; tasto destro = menu.)
    }

    private void Drag_LostCapture(object sender, MouseEventArgs e)
    {
        if (_dragMode == FDragMode.None) return;
        EndDrag();
        Render(); // ripristina la vista (preview/pixel-mode rimossi)
    }

    private void EndDrag()
    {
        StopAutoPan();
        timelineBody.MouseMove -= Drag_MouseMove;
        timelineBody.MouseLeftButtonUp -= Drag_MouseUp;
        timelineBody.LostMouseCapture -= Drag_LostCapture;

        Border? createPrev = _createBar;
        Grid? lane = _dragLane;
        Border? bar = _dragBar;

        // Azzera lo stato PRIMA di rilasciare la cattura (il rientro in LostCapture esce subito).
        _dragMode = FDragMode.None;
        _dragItem = null;
        _dragLane = null;
        _dragBar = null;
        _createBar = null;
        _dragMoved = false;
        _dragRender = null;

        if (createPrev != null) lane?.Children.Remove(createPrev);
        if (bar != null) bar.Opacity = 1;
        if (timelineBody.IsMouseCaptured) timelineBody.ReleaseMouseCapture();
    }

    // ── Auto-pan ────────────────────────────────────────────────

    private void UpdateAutoPan(double x, double laneWidth)
    {
        if (_dragMode == FDragMode.None || laneWidth <= 0) { StopAutoPan(); return; }
        int dir = x < AutoPanEdgeMargin ? -1 : x > laneWidth - AutoPanEdgeMargin ? 1 : 0;
        if (dir == 0) StopAutoPan(); else StartAutoPan(dir);
    }

    private void StartAutoPan(int dir)
    {
        _autoPanDir = dir;
        if (_autoPanTimer == null)
        {
            _autoPanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoPanIntervalMs) };
            _autoPanTimer.Tick += (_, _) => AutoPanStep();
        }
        if (!_autoPanTimer.IsEnabled) _autoPanTimer.Start();
    }

    private void StopAutoPan()
    {
        _autoPanDir = 0;
        _autoPanTimer?.Stop();
    }

    private void AutoPanStep()
    {
        if (_dragMode == FDragMode.None || _autoPanDir == 0) { StopAutoPan(); return; }

        int step = _autoPanDir * 7;
        _windowStart = _windowStart.AddDays(step);
        DateTime winEnd = _windowStart.AddDays(_windowDays - 1);
        _dragMoved = true;

        if (_dragMode == FDragMode.Create)
        {
            DateTime cur = _autoPanDir > 0 ? winEnd : _windowStart;
            _dragPreviewStart = _createAnchor <= cur ? _createAnchor : cur;
            _dragPreviewEnd = _createAnchor <= cur ? cur : _createAnchor;
            _dragRender = null;
            Render();
            timelineBody.UpdateLayout();
            _dragLane = FindLaneForEmployee(_createEmployeeId);
            ReAddCreateBar();
            ShowCreateStatus();
            return;
        }

        if (_dragItem == null) { StopAutoPan(); return; }

        int span = (_dragOrigEnd - _dragOrigStart).Days;
        switch (_dragMode)
        {
            case FDragMode.Move:
                if (_autoPanDir > 0) { _dragPreviewEnd = winEnd; _dragPreviewStart = winEnd.AddDays(-span); }
                else { _dragPreviewStart = _windowStart; _dragPreviewEnd = _windowStart.AddDays(span); }
                break;
            case FDragMode.ResizeEnd:
                _dragPreviewEnd = _dragPreviewEnd.AddDays(step);
                if (_dragPreviewEnd < _dragPreviewStart) _dragPreviewEnd = _dragPreviewStart;
                break;
            case FDragMode.ResizeStart:
                _dragPreviewStart = _dragPreviewStart.AddDays(step);
                if (_dragPreviewStart > _dragPreviewEnd) _dragPreviewStart = _dragPreviewEnd;
                break;
        }

        _dragRender = (_dragItem.Id, _dragItem.EmployeeId, _dragPreviewStart, _dragPreviewEnd);
        Render();
        timelineBody.UpdateLayout();
        _dragLane = FindLaneForEmployee(_dragItem.EmployeeId);
        _dragBar = FindBarInLane(_dragLane, _dragItem.Id);

        // Ricalibra l'origine del gesto (coord timelineBody) per continuità quando l'utente
        // si stacca dal bordo.
        double colWidth = timelineBody.ActualWidth / _windowDays;
        if (colWidth > 0)
        {
            int applied = _dragMode == FDragMode.ResizeEnd
                ? (_dragPreviewEnd - _dragOrigEnd).Days
                : (_dragPreviewStart - _dragOrigStart).Days;
            Point m = Mouse.GetPosition(timelineBody);
            _dragStartPoint = new Point(m.X - applied * colWidth, m.Y);
        }
        ShowDragStatus();
    }

    private void ReAddCreateBar()
    {
        if (_dragLane == null) return;
        _createBar = NewCreatePreviewBorder();
        Grid.SetRowSpan(_createBar, Math.Max(1, _dragLane.RowDefinitions.Count));
        _dragLane.Children.Add(_createBar);
        PlaceCreateBar();
    }

    // ── Helpers visuali ─────────────────────────────────────────

    private static Border NewCreatePreviewBorder() => new()
    {
        Background = ResourcePlannerHelpers.FerieBg,
        Opacity = 0.55,
        CornerRadius = new CornerRadius(4),
        Margin = new Thickness(1, 4, 1, 4)
    };

    private void PlaceCreateBar()
    {
        if (_createBar == null) return;
        int s = Math.Clamp((_dragPreviewStart - _windowStart).Days, 0, _windowDays - 1);
        int en = Math.Clamp((_dragPreviewEnd - _windowStart).Days, 0, _windowDays - 1);
        if (en < s) en = s;
        Grid.SetColumn(_createBar, s);
        Grid.SetColumnSpan(_createBar, en - s + 1);
    }

    // Posizionamento FLUIDO a pixel della barra trascinata (snap al giorno al rilascio).
    private void ApplyBarPixels(Border bar, FDragMode mode, double colWidth, double dxPx)
    {
        double laneWidth = colWidth * _windowDays;
        double origLeftPx = (_dragOrigStart - _windowStart).Days * colWidth;
        double origWidthPx = ((_dragOrigEnd - _dragOrigStart).Days + 1) * colWidth;
        const double minW = 8;

        double leftPx, rightPx;
        switch (mode)
        {
            case FDragMode.Move:
                leftPx = Math.Clamp(origLeftPx + dxPx, 0, Math.Max(0, laneWidth - origWidthPx));
                rightPx = leftPx + origWidthPx;
                break;
            case FDragMode.ResizeEnd:
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

        double dispLeft = Math.Max(0, leftPx);
        double dispRight = Math.Min(laneWidth, rightPx);
        double dispWidth = Math.Max(minW, dispRight - dispLeft);

        Grid.SetColumn(bar, 0);
        Grid.SetColumnSpan(bar, _windowDays);
        bar.HorizontalAlignment = HorizontalAlignment.Left;
        bar.Margin = new Thickness(dispLeft + 2, 4, 0, 4);
        bar.Width = Math.Max(1, dispWidth - 4);
    }

    private (DateTime Start, DateTime End) SnapDates(FDragMode mode, int dayDelta)
    {
        DateTime s = _dragOrigStart, en = _dragOrigEnd;
        switch (mode)
        {
            case FDragMode.Move:
                int span = (_dragOrigEnd - _dragOrigStart).Days;
                DateTime winEnd = _windowStart.AddDays(_windowDays - 1);
                DateTime maxStart = winEnd.AddDays(-span);
                DateTime cand = _dragOrigStart.AddDays(dayDelta);
                if (cand > maxStart) cand = maxStart;
                if (cand < _windowStart) cand = _windowStart;
                s = cand;
                en = cand.AddDays(span);
                break;
            case FDragMode.ResizeEnd:
                en = _dragOrigEnd.AddDays(dayDelta);
                if (en < _dragOrigStart) en = _dragOrigStart;
                break;
            case FDragMode.ResizeStart:
                s = _dragOrigStart.AddDays(dayDelta);
                if (s > _dragOrigEnd) s = _dragOrigEnd;
                break;
        }
        return (s, en);
    }

    private void ShowDragStatus()
    {
        if (_dragItem == null) return;
        int gg = ResourcePlannerHelpers.WorkingDayCount(_dragPreviewStart, _dragPreviewEnd);
        txtStatus.Text = $"{_dragItem.EmployeeName} · {_dragPreviewStart:dd/MM} → {_dragPreviewEnd:dd/MM} ({gg} gg)";
    }

    private void ShowCreateStatus()
    {
        int gg = ResourcePlannerHelpers.WorkingDayCount(_dragPreviewStart, _dragPreviewEnd);
        txtStatus.Text = $"Nuove ferie · {_dragPreviewStart:dd/MM} → {_dragPreviewEnd:dd/MM} ({gg} gg)";
    }

    // ── Ricerca elementi dopo il re-render ──────────────────────

    private Grid? FindLaneForEmployee(int employeeId)
    {
        foreach (UIElement ch in timelineBody.Children)
            if (ch is Grid g && g.Tag is int id && id == employeeId) return g;
        return null;
    }

    private static Border? FindBarInLane(Grid? lane, int assignmentId)
    {
        if (lane == null) return null;
        foreach (UIElement ch in lane.Children)
            if (ch is Border b && b.Tag is ResAssignmentDto dto && dto.Id == assignmentId) return b;
        return null;
    }
}
