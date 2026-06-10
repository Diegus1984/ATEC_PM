using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ATEC.PM.Client.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Gestione Risorse — planner Gantt (shell + navigazione + stato UI).
public partial class ResourcePlannerPage : Page
{
    private sealed class LaneInfo
    {
        public int EmployeeId { get; init; }
        public string EmployeeName { get; init; } = "";
        public Grid Lane { get; init; } = null!;
        public Border NameCell { get; init; } = null!;
    }

    private sealed class GanttBarHost
    {
        public ResAssignmentDto Assignment { get; init; } = null!;
        public Border Inner { get; init; } = null!;
        public DropShadowEffect GlowEffect { get; init; } = null!;
    }

    private sealed class ConflictListItem
    {
        public int AssignmentId { get; init; }
        public int EmployeeId { get; init; }
        public string Label { get; init; } = "";
    }

    private Border? _hoveredBar;

    private enum GanttDragMode { None, Move, ResizeStart, ResizeEnd, Create }

    // ── Dati ────────────────────────────────────────────────────
    private List<LookupItem> _resources = new();
    private List<LookupItem> _projects = new();
    private List<ResServiceDto> _services = new();
    private List<ResOtherActivityDto> _others = new();
    private List<ResAssignmentDto> _assignments = new();
    private readonly List<LaneInfo> _lanes = new();
    private readonly Dictionary<int, int> _employeeRowIndex = new();

    // ── Finestra temporale ──────────────────────────────────────
    private DateTime _windowStart;
    private int _windowDays = 28;
    private string _search = "";
    private bool _syncingWindowCombo;
    private bool _syncingScroll;
    private bool _syncingFilters;
    private bool _initialWindowFocused;
    private bool _isRenderingGantt;
    private double _lastTimelineWidth;

    private const int MinWindowDays = 7;
    private const int MaxWindowDays = 84;
    private const int WindowZoomStepDays = 7;
    private const int PanStepDays = 7;
    private const double MinDayColumnWidth = 32;
    private const double AbsoluteMinDayColumnWidth = 8;
    private const int BarDoubleClickMs = 500;
    private const double LaneHeight = 26;
    private const double BarGripWidth = 4;
    private const double DragPixelThreshold = 4;
    private const double VerticalDragThreshold = 12;

    // ── Drag ────────────────────────────────────────────────────
    private GanttDragMode _dragMode = GanttDragMode.None;
    private ResAssignmentDto? _dragItem;
    private DateTime _dragOrigStart;
    private DateTime _dragOrigEnd;
    private DateTime _dragPreviewStart;
    private DateTime _dragPreviewEnd;
    private int _dragOrigEmployeeId;
    private Point _dragStartPoint;
    private Grid? _dragLane;
    private Border? _dragBar;
    private bool _dragMoved;
    private bool _dragCopyMode;
    private bool _barDragPending;
    private GanttDragMode _pendingDragMode;
    private DateTime _lastBarClickUtc = DateTime.MinValue;
    private int _lastBarClickId;

    // Salvataggio/caricamento in corso: blocca l'avvio di nuovi drag finché la
    // chiamata di rete non torna (evita render sovrapposti / doppie operazioni).
    private bool _busy;

    // Auto-pan: durante un Move, se il puntatore resta sul bordo del viewport la
    // finestra avanza da sola di ±7 gg/tick e la barra la segue agganciata al bordo
    // a dimensione piena → si sposta un'attività oltre il periodo visibile.
    private DispatcherTimer? _autoPanTimer;
    private int _autoPanDir;
    private const double AutoPanEdgeMargin = 28;
    private const int AutoPanIntervalMs = 180;

    // True mentre ri-aggancio la barra dopo il RenderGantt dell'auto-pan: sopprime
    // l'abort in Bar_LostMouseCapture quando la vecchia barra esce dall'albero.
    private bool _reacquiring;

    // Override di rendering della barra trascinata: durante l'auto-pan la sua data
    // reale è fuori finestra, ma va disegnata alle date di preview (sul bordo, piena)
    // senza toccare il DTO. Null = nessun auto-pan in corso.
    private (int Id, int EmployeeId, DateTime Start, DateTime End)? _dragRender;

    private ResAssignmentDto? _selectedAssignment;
    private Border? _selectedBar;

    private int _createEmployeeId;
    private int _createStartCol;
    private string? _legendDragTipo;
    private Border? _createPreviewBar;
    private Grid? _createLane;

    // ── UI ──────────────────────────────────────────────────────
    private Brush? _statusDefaultForeground;
    private DispatcherTimer? _toastTimer;
    private DispatcherTimer? _renderDebounceTimer;

    // Permesso di scrittura (feature resources.edit): PM/RESP_REPARTO/ADMIN. Gli altri sono in sola
    // lettura → drag/resize/create/menu/dialog disabilitati lato client (il server fa da backstop).
    private bool _canEdit = true;

    public ResourcePlannerPage()
    {
        InitializeComponent();
        _windowStart = ResourcePlannerHelpers.MondayOf(DateTime.Today);
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Focus();
        _canEdit = ATEC.PM.Shared.PermissionEngine.CanAccess("resources.edit");
        ApplyEditPermissions();
        BindProjectFilter();
        ApplyPlannerSettings(PlannerUiSettingsStore.Load());
        await LoadAll();
        await StartRealtimeAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        SavePlannerSettingsNow();
        StopRealtime();
    }

    // Nasconde i comandi di scrittura quando l'utente è in sola lettura.
    private void ApplyEditPermissions()
    {
        Visibility v = _canEdit ? Visibility.Visible : Visibility.Collapsed;
        btnNew.Visibility = v;
        legendChips.Visibility = v;
    }

    // ── Navigazione timeline ────────────────────────────────────

    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        _windowStart = _windowStart.AddDays(-PanStepDays);
        RenderGantt();
        ScheduleSavePlannerSettings();
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        _windowStart = _windowStart.AddDays(PanStepDays);
        RenderGantt();
        ScheduleSavePlannerSettings();
    }

    private void BtnToday_Click(object sender, RoutedEventArgs e)
    {
        _windowStart = ResourcePlannerHelpers.MondayOf(DateTime.Today);
        RenderGantt();
        ScheduleSavePlannerSettings();
    }

    private void BtnHelp_Click(object sender, RoutedEventArgs e) => helpPopup.IsOpen = !helpPopup.IsOpen;

    private void CmbWindow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingWindowCombo) return;
        if (cmbWindow.SelectedItem is ComboBoxItem ci && int.TryParse(ci.Tag as string, out int d))
        {
            SetWindowDays(d, syncCombo: false);
            ScheduleSavePlannerSettings();
        }
    }

    private void Gantt_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            int deltaDays = e.Delta > 0 ? -WindowZoomStepDays : WindowZoomStepDays;
            int clamped = Math.Clamp(_windowDays + deltaDays, MinWindowDays, MaxWindowDays);
            if (clamped == _windowDays)
                return;

            _windowDays = clamped;
            SyncWindowComboSelection();
            ScheduleRenderGantt();
            ScheduleSavePlannerSettings();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            _windowStart = _windowStart.AddDays(e.Delta > 0 ? -PanStepDays : PanStepDays);
            ScheduleRenderGantt();
            ScheduleSavePlannerSettings();
        }
    }

    private void TimelineHScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll)
            return;

        ScrollViewer src = (ScrollViewer)sender;
        ScrollViewer dst = ReferenceEquals(src, headerHScroll) ? bodyHScroll : headerHScroll;
        double target = src.HorizontalOffset;
        if (Math.Abs(dst.HorizontalOffset - target) < 1)
            return;

        _syncingScroll = true;
        try { dst.ScrollToHorizontalOffset(target); }
        finally { _syncingScroll = false; }

        ScheduleSavePlannerSettings();
    }

    private void BodyVScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0)
            ScheduleSavePlannerSettings();
    }

    private void SetWindowDays(int days, bool syncCombo)
    {
        int clamped = Math.Clamp(days, MinWindowDays, MaxWindowDays);
        if (clamped == _windowDays)
            return;

        _windowDays = clamped;
        if (syncCombo)
            SyncWindowComboSelection();
        if (IsLoaded)
        {
            RenderGantt();
            ScheduleSavePlannerSettings();
        }
    }

    private void SyncWindowComboSelection()
    {
        _syncingWindowCombo = true;
        try
        {
            foreach (object item in cmbWindow.Items)
            {
                if (item is ComboBoxItem ci && int.TryParse(ci.Tag as string, out int d) && d == _windowDays)
                {
                    cmbWindow.SelectedItem = ci;
                    return;
                }
            }
            cmbWindow.SelectedIndex = -1;
        }
        finally { _syncingWindowCombo = false; }
    }

    // ── Filtri ──────────────────────────────────────────────────

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = txtSearch.Text.Trim().ToLower();
        if (IsLoaded)
        {
            ScheduleRenderGantt();
            ScheduleSavePlannerSettings();
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingFilters || !IsLoaded) return;
        RenderGantt();
        ScheduleSavePlannerSettings();
    }

    private void LstConflicts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstConflicts.SelectedItem is not ConflictListItem item) return;
        _selectedAssignment = _assignments.FirstOrDefault(a => a.Id == item.AssignmentId);
        RenderGantt();
        ScrollToEmployee(item.EmployeeId);
        lstConflicts.SelectedItem = null;
    }

    // ── Tastiera ────────────────────────────────────────────────

    private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelBarDrag();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _canEdit && _selectedAssignment != null && _dragMode == GanttDragMode.None)
        {
            ResAssignmentDto sel = _selectedAssignment;
            _ = DeleteAssignment(sel);
            e.Handled = true;
        }
    }

    // ── Status bar ──────────────────────────────────────────────

    private void UpdateDefaultStatus(int resourceCount, DateTime winEnd)
    {
        if (_toastTimer?.IsEnabled == true) return;
        int conflictCount = _assignments.Count(a => a.HasConflict && a.DataFine.Date >= _windowStart && a.DataInizio.Date <= winEnd);
        txtStatus.Text = $"{resourceCount} risorse · {_assignments.Count} allocazioni"
            + (conflictCount > 0 ? $"  ·  ⚠ {conflictCount} in conflitto" : "");
        SetDragStatusActive(false);
    }

    private void UpdateDefaultStatus()
    {
        DateTime winEnd = _windowStart.AddDays(_windowDays - 1);
        UpdateDefaultStatus(GetFilteredResources(winEnd).Count, winEnd);
    }

    private void SetDragStatusActive(bool active)
    {
        if (_statusDefaultForeground == null)
            _statusDefaultForeground = txtStatus.Foreground;
        txtStatus.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        txtStatus.Foreground = active
            ? (Brush)FindResource("ShadcnPrimaryBrush")
            : _statusDefaultForeground ?? (Brush)FindResource("ShadcnMutedForegroundBrush");
    }

    private void ShowToast(string message)
    {
        _toastTimer?.Stop();
        txtStatus.Text = message;
        SetDragStatusActive(false);
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            UpdateDefaultStatus();
        };
        _toastTimer.Start();
    }

    private static void ShowError(string msg) =>
        ShadcnMessageBox.Show(
            string.IsNullOrWhiteSpace(msg) ? "Operazione non riuscita." : msg, "Risorse",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
