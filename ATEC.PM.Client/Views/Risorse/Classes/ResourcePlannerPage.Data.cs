using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ATEC.PM.Client.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Caricamento dati, CRUD allocazioni e dialoghi.
public partial class ResourcePlannerPage
{
    private async Task LoadAll()
    {
        try
        {
            _resources = await ApiClient.GetListAsync<LookupItem>("/api/employees/real");
            _projects = await ApiClient.GetListAsync<LookupItem>("/api/resource-planner/lookups/projects");
            _services = await ApiClient.GetListAsync<ResServiceDto>("/api/resource-planner/services");
            _others = await ApiClient.GetListAsync<ResOtherActivityDto>("/api/resource-planner/others");
            UpdateResourceFilterButtonLabel(); // riallinea il conteggio alle risorse realmente esistenti
            BindProjectFilter();
            ApplyPendingProjectFilter();
            await LoadAssignments();
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async Task LoadAssignments()
    {
        _assignments = await ApiClient.GetListAsync<ResAssignmentDto>("/api/resource-planner/assignments");
        ResourcePlannerHelpers.RecalculateConflicts(_assignments);
        if (!_initialWindowFocused && _assignments.Count > 0)
        {
            FocusWindowOnAssignments();
            _initialWindowFocused = true;
        }
        RenderGantt();
    }

    private void FocusWindowOnAssignments()
    {
        DateTime winEnd = _windowStart.AddDays(_windowDays - 1);
        if (_assignments.Any(a => a.DataFine.Date >= _windowStart && a.DataInizio.Date <= winEnd))
            return;

        DateTime anchor = _assignments.Min(a => a.DataInizio.Date);
        _windowStart = ResourcePlannerHelpers.MondayOf(anchor);
    }

    private void BindProjectFilter()
    {
        List<LookupItem> opts = new() { new LookupItem { Id = 0, Name = "Tutte" } };
        opts.AddRange(_projects);
        _syncingFilters = true;
        cmbProjectFilter.ItemsSource = opts;
        cmbProjectFilter.SelectedValue = 0;
        _syncingFilters = false;
    }

    private async Task<bool> PostAssignmentAsync(ResAssignmentCreateRequest req, string? successToast = null)
    {
        _busy = true;
        try
        {
            string resp = await ApiClient.PostAsync(WithConn("/api/resource-planner/assignments"), JsonSerializer.Serialize(req));
            if (!ApiClient.IsApiSuccess(resp, out string msg))
            {
                ShowError(msg);
                return false;
            }

            await LoadAssignments();
            if (!string.IsNullOrEmpty(successToast))
                ShowToast(successToast);
            return true;
        }
        catch (Exception ex)
        {
            ShowError($"Salvataggio non riuscito: {ex.Message}");
            return false;
        }
        finally { _busy = false; }
    }

    private async Task CreateAssignmentDirectAsync(int employeeId, DateTime start, DateTime end, string tipo)
    {
        if (!await ConfirmConflictIfNeeded(employeeId, start, end, tipo, excludeId: 0))
        {
            RenderGantt();
            return;
        }

        ResAssignmentCreateRequest req = new()
        {
            EmployeeIds = new List<int> { employeeId },
            Tipo = tipo,
            DataInizio = start,
            DataFine = end
        };

        txtStatus.Text = $"Salvataggio · {ResourcePlannerHelpers.TipoLabel(tipo)} · {start:dd/MM/yyyy} → {end:dd/MM/yyyy}...";
        SetDragStatusActive(false);

        if (await PostAssignmentAsync(req, "Allocazione creata"))
            return;

        RenderGantt();
    }

    private async Task SaveAssignmentAsync(ResAssignmentDto a, DateTime start, DateTime end, int employeeId)
    {
        if (!await ConfirmConflictIfNeeded(employeeId, start, end, a.Tipo, a.Id))
        {
            RenderGantt();
            return;
        }

        ResAssignmentUpdateRequest req = BuildUpdateRequest(a, start, end, employeeId);
        txtStatus.Text = $"Salvataggio · {start:dd/MM/yyyy} → {end:dd/MM/yyyy}...";
        SetDragStatusActive(false);
        _busy = true;
        try
        {
            string resp = await ApiClient.PutAsync(WithConn($"/api/resource-planner/assignments/{a.Id}"), JsonSerializer.Serialize(req));
            if (ApiClient.IsApiSuccess(resp, out string msg))
                ApplyLocalUpdate(a.Id, req);
            else
            {
                ShowError(msg);
                RenderGantt();
            }
        }
        catch (Exception ex)
        {
            ShowError($"Salvataggio non riuscito: {ex.Message}");
            RenderGantt();
        }
        finally { _busy = false; }
    }

    private async Task DuplicateAssignmentAsync(ResAssignmentDto a, DateTime start, DateTime end, int employeeId)
    {
        if (!await ConfirmConflictIfNeeded(employeeId, start, end, a.Tipo, excludeId: 0))
        {
            RenderGantt();
            return;
        }

        ResAssignmentCreateRequest req = new()
        {
            EmployeeIds = new List<int> { employeeId },
            Tipo = a.Tipo,
            DataInizio = start,
            DataFine = end,
            ProjectId = a.ProjectId,
            ServiceId = a.ServiceId,
            OtherActivityId = a.OtherActivityId,
            Descrizione = a.Descrizione
        };
        txtStatus.Text = "Copia in corso...";
        if (!await PostAssignmentAsync(req))
            RenderGantt();
    }

    private async Task<bool> ConfirmConflictIfNeeded(int employeeId, DateTime start, DateTime end, string tipo, int excludeId)
    {
        if (!ResourcePlannerHelpers.WouldConflict(_assignments, employeeId, start, end, tipo, excludeId))
            return true;
        return ShadcnMessageBox.Show(
            "L'allocazione genera un conflitto con un'altra attività della stessa risorsa. Salvare comunque?",
            "Conflitto", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static ResAssignmentUpdateRequest BuildUpdateRequest(ResAssignmentDto a, DateTime start, DateTime end, int employeeId) => new()
    {
        EmployeeId = employeeId,
        Tipo = a.Tipo,
        DataInizio = start,
        DataFine = end,
        ProjectId = a.ProjectId,
        ServiceId = a.ServiceId,
        OtherActivityId = a.OtherActivityId,
        Descrizione = a.Descrizione
    };

    private void ApplyLocalUpdate(int id, ResAssignmentUpdateRequest req)
    {
        ResAssignmentDto? item = _assignments.FirstOrDefault(a => a.Id == id);
        if (item == null) { RenderGantt(); return; }

        LookupItem? emp = _resources.FirstOrDefault(r => r.Id == req.EmployeeId);
        item.EmployeeId = req.EmployeeId;
        item.EmployeeName = emp?.Name ?? item.EmployeeName;
        item.Tipo = req.Tipo;
        item.DataInizio = req.DataInizio;
        item.DataFine = req.DataFine;
        item.ProjectId = req.ProjectId;
        item.ServiceId = req.ServiceId;
        item.OtherActivityId = req.OtherActivityId;
        item.Descrizione = req.Descrizione;
        ResourcePlannerHelpers.RecalculateConflicts(_assignments);
        RenderGantt();
        ShowToast("Salvato");
    }

    private async void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        AssignmentDialog dlg = new(_resources, _projects, _services, _others) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        await PostAssignmentAsync(dlg.CreateRequest);
    }

    private async void BtnFeriePlan_Click(object sender, RoutedEventArgs e)
    {
        FerieDashboardWindow dlg = new(_resources, _assignments)
        { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
        if (dlg.DataChanged)
            await LoadAssignments();
    }

    private async void EditAssignment(ResAssignmentDto a)
    {
        AssignmentDialog dlg = new(_resources, _projects, _services, _others, existing: a) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        _busy = true;
        try
        {
            string resp = await ApiClient.PutAsync(WithConn($"/api/resource-planner/assignments/{a.Id}"), JsonSerializer.Serialize(dlg.UpdateRequest));
            if (ApiClient.IsApiSuccess(resp, out _))
                ApplyLocalUpdate(a.Id, dlg.UpdateRequest);
            else
                ShowError("Modifica non riuscita.");
        }
        catch (Exception ex)
        {
            ShowError($"Modifica non riuscita: {ex.Message}");
        }
        finally { _busy = false; }
    }

    private void DuplicateAssignment(ResAssignmentDto a, int dayOffset)
    {
        AssignmentDialog dlg = new(_resources, _projects, _services, _others, copyFrom: a, duplicateDayOffset: dayOffset)
        { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        _ = PostAssignmentAsync(dlg.CreateRequest);
    }

    private async Task DeleteAssignment(ResAssignmentDto a)
    {
        if (ShadcnMessageBox.Show(
                $"Eliminare l'allocazione di {a.EmployeeName} ({a.DataInizio:dd/MM} → {a.DataFine:dd/MM})?",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _busy = true;
        try
        {
            string resp = await ApiClient.DeleteAsync(WithConn($"/api/resource-planner/assignments/{a.Id}"));
            if (ApiClient.IsApiSuccess(resp, out _))
            {
                _assignments.RemoveAll(x => x.Id == a.Id);
                if (_selectedAssignment?.Id == a.Id) _selectedAssignment = null;
                ResourcePlannerHelpers.RecalculateConflicts(_assignments);
                RenderGantt();
                ShowToast("Eliminato");
            }
            else
                ShowError("Eliminazione non riuscita.");
        }
        catch (Exception ex)
        {
            ShowError($"Eliminazione non riuscita: {ex.Message}");
        }
        finally { _busy = false; }
    }
}
