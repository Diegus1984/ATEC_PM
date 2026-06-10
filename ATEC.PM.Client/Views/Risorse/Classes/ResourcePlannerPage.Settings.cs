using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Persistenza finestra temporale, filtri e scroll del Gantt.
public partial class ResourcePlannerPage
{
    private double? _restoreScrollH;
    private double? _restoreScrollV;
    private bool _hasPendingProjectFilter;
    private int _pendingProjectFilterId;
    private DispatcherTimer? _settingsSaveTimer;

    private void ApplyPlannerSettings(PlannerUiSettings settings)
    {
        _windowDays = Math.Clamp(settings.WindowDays, MinWindowDays, MaxWindowDays);
        if (DateTime.TryParse(settings.WindowStart, out DateTime windowStart))
        {
            _windowStart = windowStart.Date;
            _initialWindowFocused = true;
        }

        SyncWindowComboSelection();

        _syncingFilters = true;
        try
        {
            chkConflictsOnly.IsChecked = settings.ConflictsOnly;
            chkOccupiedOnly.IsChecked = settings.OccupiedOnly;
            ApplyTipoFilterSelection(settings.TipoFilter ?? "");
            string search = settings.Search ?? "";
            txtSearch.Text = search;
            _search = search.Trim().ToLower();
            _hasPendingProjectFilter = true;
            _pendingProjectFilterId = settings.ProjectFilterId;
            _resourceFilterActive = settings.ResourceFilterActive;
            _selectedResourceIds = new HashSet<int>(settings.SelectedResourceIds ?? new List<int>());
        }
        finally
        {
            _syncingFilters = false;
        }

        UpdateResourceFilterButtonLabel();

        if (settings.ScrollHorizontal > 0 || settings.ScrollVertical > 0)
        {
            _restoreScrollH = settings.ScrollHorizontal;
            _restoreScrollV = settings.ScrollVertical;
        }
    }

    private void ApplyTipoFilterSelection(string tag)
    {
        foreach (object item in cmbTipoFilter.Items)
        {
            if (item is ComboBoxItem ci && string.Equals(ci.Tag as string ?? "", tag, StringComparison.Ordinal))
            {
                cmbTipoFilter.SelectedItem = ci;
                return;
            }
        }

        cmbTipoFilter.SelectedIndex = 0;
    }

    /// <summary>Riapplica il filtro commessa salvato dopo che BindProjectFilter ha ricaricato le opzioni.</summary>
    private void ApplyPendingProjectFilter()
    {
        if (!_hasPendingProjectFilter)
            return;

        _hasPendingProjectFilter = false;
        if (_pendingProjectFilterId <= 0)
            return;

        if (cmbProjectFilter.ItemsSource is not System.Collections.IEnumerable items)
            return;

        foreach (object item in items)
        {
            if (item is LookupItem li && li.Id == _pendingProjectFilterId)
            {
                _syncingFilters = true;
                try { cmbProjectFilter.SelectedValue = _pendingProjectFilterId; }
                finally { _syncingFilters = false; }
                return;
            }
        }
    }

    private string GetTipoFilterTag()
    {
        if (cmbTipoFilter.SelectedItem is ComboBoxItem ci)
            return ci.Tag as string ?? "";
        return "";
    }

    private PlannerUiSettings CollectPlannerSettings()
    {
        int projectId = 0;
        if (cmbProjectFilter.SelectedValue is int id)
            projectId = id;

        return new PlannerUiSettings
        {
            WindowDays = _windowDays,
            WindowStart = _windowStart.ToString("yyyy-MM-dd"),
            Search = txtSearch.Text.Trim(),
            ConflictsOnly = chkConflictsOnly.IsChecked == true,
            OccupiedOnly = chkOccupiedOnly.IsChecked == true,
            TipoFilter = GetTipoFilterTag(),
            ProjectFilterId = projectId,
            ScrollHorizontal = bodyHScroll.HorizontalOffset,
            ScrollVertical = bodyVScroll.VerticalOffset,
            ResourceFilterActive = _resourceFilterActive,
            SelectedResourceIds = _selectedResourceIds.ToList()
        };
    }

    private void ScheduleSavePlannerSettings()
    {
        if (!IsLoaded)
            return;

        if (_settingsSaveTimer == null)
        {
            _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _settingsSaveTimer.Tick += (_, _) =>
            {
                _settingsSaveTimer!.Stop();
                PlannerUiSettingsStore.Save(CollectPlannerSettings());
            };
        }

        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void SavePlannerSettingsNow()
    {
        _settingsSaveTimer?.Stop();
        if (!IsLoaded)
            return;

        PlannerUiSettingsStore.Save(CollectPlannerSettings());
    }

    private void ApplyPendingScrollRestore()
    {
        if (!_restoreScrollH.HasValue && !_restoreScrollV.HasValue)
            return;

        if (_restoreScrollH.HasValue)
        {
            double h = Math.Min(_restoreScrollH.Value, bodyHScroll.ScrollableWidth);
            if (h < 0) h = 0;
            bodyHScroll.ScrollToHorizontalOffset(h);
            headerHScroll.ScrollToHorizontalOffset(h);
        }

        if (_restoreScrollV.HasValue)
        {
            double v = Math.Min(_restoreScrollV.Value, bodyVScroll.ScrollableHeight);
            if (v < 0) v = 0;
            bodyVScroll.ScrollToVerticalOffset(v);
        }

        _restoreScrollH = null;
        _restoreScrollV = null;
    }
}
