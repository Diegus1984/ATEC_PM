using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Dialog create/edit di un'allocazione risorsa. In create è multi-risorsa (batch).
public partial class AssignmentDialog : Window
{
    public bool IsEdit { get; }
    public ResAssignmentCreateRequest CreateRequest { get; private set; } = new();
    public ResAssignmentUpdateRequest UpdateRequest { get; private set; } = new();

    private readonly List<SelectableResource> _selectable = new();
    private bool _syncingSelectAll;
    private bool _singleMode;
    private int _singleEmployeeId;

    public AssignmentDialog(
        List<LookupItem> resources,
        List<LookupItem> projects,
        List<ResServiceDto> services,
        List<ResOtherActivityDto> others,
        ResAssignmentDto? existing = null,
        ResAssignmentDto? copyFrom = null,
        int? presetEmployeeId = null,
        DateTime? presetStart = null,
        DateTime? presetEnd = null,
        int duplicateDayOffset = 0,
        string? presetTipo = null)
    {
        InitializeComponent();
        IsEdit = existing != null;

        // Risorsa già nota? (modifica, duplica, o creazione su una riga specifica /
        // disegno / doppio click) → mostra SOLO il nome, non l'elenco di tutti i dipendenti.
        // L'elenco multi-checkbox resta solo per "+ Allocazione" (nessuna persona preimpostata).
        int? knownId = existing?.EmployeeId ?? copyFrom?.EmployeeId ?? presetEmployeeId;
        _singleMode = knownId.HasValue;
        if (_singleMode)
        {
            _singleEmployeeId = knownId!.Value;
            multiResourcePanel.Visibility = Visibility.Collapsed;
            singleResourcePanel.Visibility = Visibility.Visible;
            lblResources.Text = "RISORSA";
            singleResourceText.Text = resources.FirstOrDefault(x => x.Id == _singleEmployeeId)?.Name ?? "(risorsa)";
        }
        else
        {
            singleResourcePanel.Visibility = Visibility.Collapsed;
            foreach (LookupItem r in resources)
            {
                SelectableResource sr = new() { Id = r.Id, Name = r.Name ?? "" };
                sr.PropertyChanged += Resource_PropertyChanged;
                _selectable.Add(sr);
            }
            dgResources.ItemsSource = _selectable;
        }

        List<LookupItem> projOpts = new() { new LookupItem { Id = 0, Name = "— nessuna —" } };
        projOpts.AddRange(projects);
        cmbCommessa.ItemsSource = projOpts;
        cmbCommessa.SelectedValue = 0;

        List<ResServiceDto> svcOpts = new() { new ResServiceDto { Id = 0, Cod = "— nessuno —" } };
        svcOpts.AddRange(services);
        cmbService.ItemsSource = svcOpts;
        cmbService.SelectedValue = 0;

        List<ResOtherActivityDto> othOpts = new() { new ResOtherActivityDto { Id = 0, Descrizione = "— nessuna —" } };
        othOpts.AddRange(others);
        cmbAltra.ItemsSource = othOpts;
        cmbAltra.SelectedValue = 0;

        dpInizio.SelectedDate = presetStart ?? DateTime.Today;
        dpFine.SelectedDate = presetEnd ?? presetStart ?? DateTime.Today;

        if (existing != null)
        {
            txtTitle.Text = "Modifica allocazione";
            SelectByTag(cmbTipo, existing.Tipo);
            dpInizio.SelectedDate = existing.DataInizio;
            dpFine.SelectedDate = existing.DataFine;
            cmbCommessa.SelectedValue = existing.ProjectId ?? 0;
            cmbService.SelectedValue = existing.ServiceId ?? 0;
            cmbAltra.SelectedValue = existing.OtherActivityId ?? 0;
            txtDescrizione.Text = existing.Descrizione ?? "";
        }
        else if (copyFrom != null)
        {
            txtTitle.Text = "Duplica allocazione";
            SelectByTag(cmbTipo, copyFrom.Tipo);
            dpInizio.SelectedDate = copyFrom.DataInizio.Date.AddDays(duplicateDayOffset);
            dpFine.SelectedDate = copyFrom.DataFine.Date.AddDays(duplicateDayOffset);
            cmbCommessa.SelectedValue = copyFrom.ProjectId ?? 0;
            cmbService.SelectedValue = copyFrom.ServiceId ?? 0;
            cmbAltra.SelectedValue = copyFrom.OtherActivityId ?? 0;
            txtDescrizione.Text = copyFrom.Descrizione ?? "";
        }

        // Preset del tipo (es. apertura dal Piano Ferie → "FERIE") solo in creazione.
        if (existing == null && !string.IsNullOrEmpty(presetTipo))
            SelectByTag(cmbTipo, presetTipo);

        // Commessa/Service/Attività non si applicano alle FERIE: le disabilito/azzero.
        cmbTipo.SelectionChanged += (_, _) => UpdateTipoDependentFields();
        UpdateTipoDependentFields();
    }

    // Le ferie non hanno commessa/service/altra attività: quando il tipo è FERIE
    // azzera e disabilita quei campi (così non restano agganciate a una commessa).
    private void UpdateTipoDependentFields()
    {
        bool ferie = TipoTag(cmbTipo) == "FERIE";
        if (ferie)
        {
            cmbCommessa.SelectedValue = 0;
            cmbService.SelectedValue = 0;
            cmbAltra.SelectedValue = 0;
        }
        cmbCommessa.IsEnabled = !ferie;
        cmbService.IsEnabled = !ferie;
        cmbAltra.IsEnabled = !ferie;
    }

    private void Resource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableResource.IsSelected) && !_syncingSelectAll)
            UpdateSelectAllState();
    }

    private void ChkSelectAll_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingSelectAll) return;
        bool on = (sender as CheckBox)?.IsChecked == true;
        _syncingSelectAll = true;
        try
        {
            foreach (SelectableResource r in _selectable) r.IsSelected = on;
        }
        finally { _syncingSelectAll = false; }
    }

    // Riflette lo stato del check "tutti" in intestazione: tutto / niente / parziale.
    private void UpdateSelectAllState()
    {
        if (chkSelectAll == null) return;
        int sel = _selectable.Count(r => r.IsSelected);
        _syncingSelectAll = true;
        try
        {
            chkSelectAll.IsChecked = sel == 0 ? false : sel == _selectable.Count ? true : (bool?)null;
        }
        finally { _syncingSelectAll = false; }
    }

    private static void SelectByTag(ComboBox cmb, string tag)
    {
        foreach (object item in cmb.Items)
            if (item is ComboBoxItem ci && (ci.Tag as string) == tag) { cmb.SelectedItem = ci; return; }
    }

    private static string TipoTag(ComboBox cmb) =>
        (cmb.SelectedItem as ComboBoxItem)?.Tag as string ?? "OP";

    private static int? Nullable0(object? selectedValue)
    {
        if (selectedValue is int id && id > 0) return id;
        return null;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        List<int> ids = _singleMode
            ? new List<int> { _singleEmployeeId }
            : _selectable.Where(r => r.IsSelected).Select(r => r.Id).ToList();
        if (ids.Count == 0)
        {
            Warn("Seleziona almeno una risorsa.");
            return;
        }
        if (dpInizio.SelectedDate == null || dpFine.SelectedDate == null)
        {
            Warn("Indica data inizio e data fine.");
            return;
        }
        if (dpFine.SelectedDate.Value.Date < dpInizio.SelectedDate.Value.Date)
        {
            Warn("La data fine non può precedere la data inizio.");
            return;
        }

        string tipo = TipoTag(cmbTipo);
        int? projectId = Nullable0(cmbCommessa.SelectedValue);
        int? serviceId = Nullable0(cmbService.SelectedValue);
        int? otherId = Nullable0(cmbAltra.SelectedValue);
        string? descr = string.IsNullOrWhiteSpace(txtDescrizione.Text) ? null : txtDescrizione.Text.Trim();

        // Le ferie non hanno commessa/service/attività: forza null (anche se i campi
        // fossero stati valorizzati prima di cambiare tipo in FERIE).
        if (tipo == "FERIE")
        {
            projectId = null;
            serviceId = null;
            otherId = null;
        }

        if (IsEdit)
        {
            UpdateRequest = new ResAssignmentUpdateRequest
            {
                EmployeeId = ids[0],
                Tipo = tipo,
                DataInizio = dpInizio.SelectedDate.Value,
                DataFine = dpFine.SelectedDate.Value,
                ProjectId = projectId,
                ServiceId = serviceId,
                OtherActivityId = otherId,
                Descrizione = descr
            };
        }
        else
        {
            CreateRequest = new ResAssignmentCreateRequest
            {
                EmployeeIds = ids,
                Tipo = tipo,
                DataInizio = dpInizio.SelectedDate.Value,
                DataFine = dpFine.SelectedDate.Value,
                ProjectId = projectId,
                ServiceId = serviceId,
                OtherActivityId = otherId,
                Descrizione = descr
            };
        }
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static void Warn(string msg) =>
        ATEC.PM.Client.Controls.ShadcnMessageBox.Show(msg, "Allocazione", MessageBoxButton.OK, MessageBoxImage.Warning);
}

// Riga selezionabile della DataGrid risorse (checkbox per assegnazione multipla).
public sealed class SelectableResource : INotifyPropertyChanged
{
    public int Id { get; init; }
    public string Name { get; init; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
