using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.UserControls;

// Real-time (SignalR) della distinta DDP: quando un altro utente crea/modifica/elimina una riga
// della STESSA commessa, la griglia si aggiorna in ~1s. Pattern identico al calendario Gestione Risorse
// (vedi memoria realtime-ambiente-condiviso): scope per commessa (gruppo "project-{id}"),
// busy-guard (non ricarica mentre edito una cella), self-exclusion via ?conn=, degradazione elegante.
public partial class DdpCommercialControl
{
    private HubConnection? _realtimeHub;
    private bool _pendingRealtimeReload;
    private bool _realtimeReloading;
    private bool _isEditingCell;
    private DispatcherTimer? _realtimeFlushTimer;

    private const string ProjectHubPath = "/hubs/project";

    /// <summary>ConnectionId dell'hub (se connesso): inviato sulle CRUD per non auto-notificarsi.</summary>
    private string? RealtimeConnectionId => _realtimeHub?.ConnectionId;

    /// <summary>Accoda ?conn=… all'endpoint così il server esclude QUESTO client dalla notifica.</summary>
    private string WithConn(string url)
    {
        string? id = RealtimeConnectionId;
        if (string.IsNullOrEmpty(id)) return url;
        string sep = url.Contains('?') ? "&" : "?";
        return $"{url}{sep}conn={Uri.EscapeDataString(id)}";
    }

    private async Task StartRealtimeAsync()
    {
        if (_realtimeHub != null) { await EnsureJoinedAsync(); return; }
        try
        {
            HubConnection hub = new HubConnectionBuilder()
                .WithUrl($"{App.ApiBaseUrl}{ProjectHubPath}", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(App.Token);
                })
                .WithAutomaticReconnect()
                .Build();

            hub.On<DdpChange>("DdpChanged", change =>
            {
                if (change.ProjectId != _projectId) return;   // solo la commessa che sto guardando
                Dispatcher.Invoke(OnRealtimeChange);
            });

            // Dopo una riconnessione i gruppi sono persi → rientro nel gruppo e risincronizzo.
            hub.Reconnected += async _ =>
            {
                try { await hub.InvokeAsync("JoinProject", _projectId); } catch { /* best-effort */ }
                Dispatcher.Invoke(OnRealtimeChange);
            };

            _realtimeHub = hub;
            await hub.StartAsync();
            await hub.InvokeAsync("JoinProject", _projectId);
        }
        catch (Exception)
        {
            // Real-time non disponibile (hub spento, rete): l'app resta usabile (refresh manuale).
            _realtimeHub = null;
        }
    }

    private async Task EnsureJoinedAsync()
    {
        try
        {
            if (_realtimeHub is { State: HubConnectionState.Connected })
                await _realtimeHub.InvokeAsync("JoinProject", _projectId);
        }
        catch { /* best-effort */ }
    }

    private async void StopRealtime()
    {
        _realtimeFlushTimer?.Stop();
        HubConnection? hub = _realtimeHub;
        _realtimeHub = null;
        if (hub == null) return;
        try { await hub.DisposeAsync(); } catch { /* chiusura best-effort */ }
    }

    // Notifica ricevuta: ricarica subito se idle, altrimenti rimanda (sto editando una cella / dialog modale).
    private void OnRealtimeChange()
    {
        _pendingRealtimeReload = true;
        FlushRealtimeOrSchedule();
    }

    private void FlushRealtimeOrSchedule()
    {
        if (!_pendingRealtimeReload) return;

        if (!IsRealtimeBusy())
        {
            _pendingRealtimeReload = false;
            ReloadFromRealtime();
            return;
        }

        if (_realtimeFlushTimer == null)
        {
            _realtimeFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _realtimeFlushTimer.Tick += (_, _) =>
            {
                if (!_pendingRealtimeReload) { _realtimeFlushTimer!.Stop(); return; }
                if (IsRealtimeBusy()) return;
                _realtimeFlushTimer!.Stop();
                _pendingRealtimeReload = false;
                ReloadFromRealtime();
            };
        }
        if (!_realtimeFlushTimer.IsEnabled)
            _realtimeFlushTimer.Start();
    }

    private bool IsRealtimeBusy()
    {
        if (_isEditingCell) return true;
        // Un dialog modale (es. messaggi) disabilita la finestra proprietaria: in quel caso rimanda.
        Window? w = Window.GetWindow(this);
        return w != null && !w.IsEnabled;
    }

    private async void ReloadFromRealtime()
    {
        if (_realtimeReloading) return;
        _realtimeReloading = true;
        try
        {
            await LoadData();
            txtStatus.Text = "Aggiornato";
        }
        catch (Exception)
        {
            // Una ricarica fallita non deve disturbare: si riproverà alla prossima notifica.
        }
        finally { _realtimeReloading = false; }
    }
}
