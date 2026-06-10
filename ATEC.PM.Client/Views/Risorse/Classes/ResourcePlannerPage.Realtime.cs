using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Real-time (SignalR): tutti i Gantt aperti si aggiornano in ~1s quando un altro utente
// crea/sposta/elimina un'allocazione. Il refresh NON interrompe un drag o un dialog in corso:
// se l'utente sta lavorando, la ricarica viene rimandata finché non torna idle.
public partial class ResourcePlannerPage
{
    private HubConnection? _realtimeHub;
    private bool _pendingRealtimeReload;
    private bool _realtimeReloading;
    private DispatcherTimer? _realtimeFlushTimer;

    private const string ResourceHubPath = "/hubs/resource-planner";

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
        if (_realtimeHub != null) return; // già connesso (riapertura pagina cacheata)
        try
        {
            HubConnection hub = new HubConnectionBuilder()
                .WithUrl($"{App.ApiBaseUrl}{ResourceHubPath}", options =>
                {
                    // WebSocket non porta l'header Authorization → token via access_token (lato server gestito).
                    options.AccessTokenProvider = () => Task.FromResult<string?>(App.Token);
                })
                .WithAutomaticReconnect()
                .Build();

            hub.On<ResAssignmentChange>("AssignmentsChanged", _ =>
                Dispatcher.Invoke(OnRealtimeChange));

            // Dopo una riconnessione la vista potrebbe essere disallineata → ricarica.
            hub.Reconnected += _ => { Dispatcher.Invoke(OnRealtimeChange); return Task.CompletedTask; };

            _realtimeHub = hub;
            await hub.StartAsync();
        }
        catch (Exception)
        {
            // Real-time non disponibile (hub spento, rete): l'app resta usabile in modalità classica.
            _realtimeHub = null;
        }
    }

    private async void StopRealtime()
    {
        _realtimeFlushTimer?.Stop();
        HubConnection? hub = _realtimeHub;
        _realtimeHub = null;
        if (hub == null) return;
        try { await hub.DisposeAsync(); } catch { /* chiusura best-effort */ }
    }

    // Notifica ricevuta: ricarica subito se idle, altrimenti rimanda (drag/dialog/salvataggio in corso).
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

        // Occupato: riprova a intervalli finché l'utente non finisce drag/dialog.
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

    // È in corso un'interazione che NON va interrotta da una ricarica?
    private bool IsRealtimeBusy()
    {
        if (_dragMode != GanttDragMode.None || _barDragPending || _legendDragTipo != null || _busy)
            return true;
        // Un dialog modale disabilita la finestra proprietaria: in quel caso rimanda.
        Window? w = Window.GetWindow(this);
        return w != null && !w.IsEnabled;
    }

    private async void ReloadFromRealtime()
    {
        if (_realtimeReloading) return;
        _realtimeReloading = true;
        try
        {
            await LoadAssignments();
            // L'utente potrebbe essere stato disattivato da un admin: verifica e forza il logout.
            if (!await SessionGuard.EnsureActiveOrLogoutAsync())
                return;
            ShowToast("Aggiornato");
        }
        catch (Exception)
        {
            // Una ricarica fallita non deve disturbare: si riproverà alla prossima notifica.
        }
        finally { _realtimeReloading = false; }
    }
}
