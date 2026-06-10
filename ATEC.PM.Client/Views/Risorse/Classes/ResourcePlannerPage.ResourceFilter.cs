using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Selettore manuale delle risorse da mostrare nel Gantt (pulsante «Risorse» + checklist).
public partial class ResourcePlannerPage
{
    // Filtro attivo = mostra solo gli id in _selectedResourceIds. Spento = tutte le risorse.
    private bool _resourceFilterActive;
    private HashSet<int> _selectedResourceIds = new();

    private readonly ObservableCollection<ResourcePick> _resourcePicks = new();
    private bool _syncingResourcePicks;

    private sealed class ResourcePick : INotifyPropertyChanged
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private void BtnResourceFilter_Click(object sender, RoutedEventArgs e)
    {
        if (!resourceFilterPopup.IsOpen)
        {
            lstResourcePicks.ItemsSource ??= _resourcePicks;
            RebuildResourcePicks();
        }
        resourceFilterPopup.IsOpen = !resourceFilterPopup.IsOpen;
    }

    /// <summary>Ricostruisce la checklist dallo stato corrente delle risorse caricate.</summary>
    private void RebuildResourcePicks()
    {
        _syncingResourcePicks = true;
        try
        {
            _resourcePicks.Clear();
            foreach (LookupItem r in _resources.OrderBy(r => r.Name))
            {
                bool isChecked = !_resourceFilterActive || _selectedResourceIds.Contains(r.Id);
                _resourcePicks.Add(new ResourcePick { Id = r.Id, Name = r.Name ?? "", IsChecked = isChecked });
            }
        }
        finally
        {
            _syncingResourcePicks = false;
        }
    }

    private void ResourcePick_Toggled(object sender, RoutedEventArgs e) => RecomputeResourceSelection();

    private void BtnSelectAllResources_Click(object sender, RoutedEventArgs e) => SetAllResourcePicks(true);

    private void BtnSelectNoneResources_Click(object sender, RoutedEventArgs e) => SetAllResourcePicks(false);

    private void SetAllResourcePicks(bool value)
    {
        _syncingResourcePicks = true;
        try
        {
            foreach (ResourcePick p in _resourcePicks)
                p.IsChecked = value;
        }
        finally
        {
            _syncingResourcePicks = false;
        }
        RecomputeResourceSelection();
    }

    /// <summary>
    /// Deriva stato e selezione dalle spunte correnti.
    /// Tutte spuntate → filtro spento (mostra tutte). Altrimenti mostra solo le spuntate
    /// (zero spuntate = nessuna risorsa visibile, scelta esplicita dell'utente).
    /// </summary>
    private void RecomputeResourceSelection()
    {
        if (_syncingResourcePicks)
            return;

        HashSet<int> checkedIds = _resourcePicks.Where(p => p.IsChecked).Select(p => p.Id).ToHashSet();
        bool allChecked = _resourcePicks.Count > 0 && checkedIds.Count == _resourcePicks.Count;

        if (allChecked)
        {
            _resourceFilterActive = false;
            _selectedResourceIds.Clear();
        }
        else
        {
            _resourceFilterActive = true;
            _selectedResourceIds = checkedIds;
        }

        UpdateResourceFilterButtonLabel();

        if (!IsLoaded)
            return;

        RenderGantt();
        ScheduleSavePlannerSettings();
    }

    private void UpdateResourceFilterButtonLabel()
    {
        if (!_resourceFilterActive)
        {
            btnResourceFilter.Content = "tutte  ▾";
            return;
        }

        // Conta solo le risorse selezionate ancora esistenti (un id orfano, es. risorsa
        // cessata, non viene contato). Prima del caricamento (_resources vuoto) usa il
        // totale salvato per non mostrare uno 0 fuorviante all'avvio.
        // L'id resta comunque in _selectedResourceIds: se la risorsa viene riattivata, ricompare.
        int count = _resources.Count > 0
            ? _resources.Count(r => _selectedResourceIds.Contains(r.Id))
            : _selectedResourceIds.Count;

        btnResourceFilter.Content = $"{count} selezionate  ▾";
    }
}
