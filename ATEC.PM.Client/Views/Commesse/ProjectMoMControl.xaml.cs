using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.Views.MoM;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.UserControls;

// Sezione MoM dentro l'albero commessa: stessa lista di verbali del modulo MoM,
// filtrata su una singola commessa (stessi record, vista diversa).
// Card → apre la MoMDetailPage esistente; "+ Nuovo verbale" è precompilato tipo=COMMESSA su questa commessa.
public partial class ProjectMoMControl : UserControl
{
    private readonly ObservableCollection<MoMVerbaleNode> _verbali = new();
    private int _projectId;
    private string _projectCode = "";
    private bool _loading;

    public ProjectMoMControl()
    {
        InitializeComponent();
        icCards.ItemsSource = _verbali;
        // Al rientro dal dettaglio (GoBack) il controllo torna visibile: ricarico per riflettere le modifiche.
        IsVisibleChanged += ProjectMoMControl_IsVisibleChanged;
    }

    // Chiamato da ProjectsPage.ShowMoM al primo accesso alla sezione.
    public void Load(int projectId, string projectCode)
    {
        _projectId = projectId;
        _projectCode = projectCode;
        _ = ReloadAsync();
    }

    private void ProjectMoMControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && _projectId > 0)
            _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_loading || _projectId <= 0) return;
        _loading = true;
        try
        {
            txtStatus.Text = "Caricamento...";
            List<MoMListDto> list = await ApiClient.GetListAsync<MoMListDto>($"/api/mom/list?projectId={_projectId}");
            _verbali.Clear();
            foreach (MoMListDto v in list)
                _verbali.Add(new MoMVerbaleNode(v));

            txtEmpty.Visibility = _verbali.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtStatus.Text = _verbali.Count == 1 ? "1 verbale" : $"{_verbali.Count} verbali";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
        finally { _loading = false; }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    // ── Azioni sul verbale ──────────────────────────────────────

    private void BtnNewVerbale_Click(object sender, RoutedEventArgs e)
    {
        CloseOtherInlineEdits();
        MoMVerbaleNode draft = MoMVerbaleNode.CreateDraft();
        draft.Tipo = "COMMESSA";
        draft.SetProject(new MoMProjectLookupDto { Id = _projectId, Code = _projectCode });
        _verbali.Insert(0, draft);
        txtEmpty.Visibility = Visibility.Collapsed;
        txtStatus.Text = "Compila la nuova card e premi Salva";
        gridScroll.ScrollToVerticalOffset(0);
    }

    private void BtnEditVerbale_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMVerbaleNode node) return;
        CloseOtherInlineEdits(node);
        node.BeginEdit();
    }

    private async void BtnInlineSave_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMVerbaleNode node) return;

        if (string.IsNullOrWhiteSpace(node.Title))
        {
            ShowError("Il titolo è obbligatorio.");
            return;
        }

        // tipo/commessa sono fissi in questo contesto: garantisco i valori sulla request.
        MoMSaveRequest req = node.ToSaveRequest();
        req.Tipo = "COMMESSA";
        req.ProjectId = _projectId;

        if (node.IsNewDraft)
        {
            string resp = await ApiClient.PostAsync("/api/mom", JsonSerializer.Serialize(req));
            if (ApiClient.TryGetApiData(resp, out int _, out string msg))
            {
                await ReloadAsync();
                txtStatus.Text = "Verbale creato";
            }
            else ShowError(msg);
            return;
        }

        string putResp = await ApiClient.PutAsync($"/api/mom/{node.Id}", JsonSerializer.Serialize(req));
        if (ApiClient.IsApiSuccess(putResp, out string putMsg))
        {
            node.EndEdit();
            txtStatus.Text = "Salvato";
        }
        else ShowError(putMsg);
    }

    private void BtnInlineCancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMVerbaleNode node) return;
        if (node.IsNewDraft)
        {
            _verbali.Remove(node);
            txtEmpty.Visibility = _verbali.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtStatus.Text = "Creazione annullata";
            return;
        }
        node.CancelEdit();
        txtStatus.Text = "Modifica annullata";
    }

    private void CloseOtherInlineEdits(MoMVerbaleNode? keep = null)
    {
        foreach (MoMVerbaleNode node in _verbali.ToList())
        {
            if (node == keep) continue;
            if (node.IsNewDraft) _verbali.Remove(node);
            else if (node.IsEditing) node.CancelEdit();
        }
    }

    private async void DashboardCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.DataContext is not MoMVerbaleNode node) return;
        if (node.IsNewDraft || node.Id == 0) return;
        MoMSaveRequest req = new MoMSaveRequest
        {
            Tipo = "COMMESSA",
            ProjectId = _projectId,
            Title = node.Title,
            MeetingDate = node.MeetingDate,
            InDashboard = node.InDashboard
        };
        string resp = await ApiClient.PutAsync($"/api/mom/{node.Id}", JsonSerializer.Serialize(req));
        if (ApiClient.IsApiSuccess(resp, out string msg)) txtStatus.Text = "Salvato";
        else { node.InDashboard = !node.InDashboard; ShowError(msg); }
    }

    private async void BtnDeleteVerbale_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMVerbaleNode node) return;

        if (node.IsNewDraft)
        {
            _verbali.Remove(node);
            txtEmpty.Visibility = _verbali.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtStatus.Text = "Creazione annullata";
            return;
        }

        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
                $"Eliminare il verbale \"{node.Title}\" e tutte le sue azioni?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        string resp = await ApiClient.DeleteAsync($"/api/mom/{node.Id}");
        if (ApiClient.IsApiSuccess(resp, out string msg))
        {
            _verbali.Remove(node);
            txtEmpty.Visibility = _verbali.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtStatus.Text = "Verbale eliminato";
        }
        else ShowError(msg);
    }

    // ── Apertura dettaglio (riusa MoMDetailPage via il Frame della pagina) ──

    private void Card_Open(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveSource(e.OriginalSource as DependencyObject)) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not MoMVerbaleNode node) return;
        if (node.IsNewDraft || node.IsEditing) return;

        NavigationService? nav = NavigationService.GetNavigationService(this);
        nav?.Navigate(new MoMDetailPage(node));
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button or CheckBox or TextBoxBase or ComboBox or DatePicker)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static void ShowError(string msg)
        => ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
            string.IsNullOrWhiteSpace(msg) ? "Operazione non riuscita." : msg, "MoM",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
