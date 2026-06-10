using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Services;

// Client SignalR dell'hub commesse (/hubs/project). Riusabile dalle viste in sola lettura
// (Gestore DDP lista → JoinAll; sintesi → JoinProject) per ricevere gli eventi "DdpChanged".
// L'evento DdpChanged viene sollevato su un thread di background: il chiamante deve marshalare sul Dispatcher.
public sealed class ProjectHubClient : IAsyncDisposable
{
    private HubConnection? _hub;
    private int? _joinedProject;
    private bool _joinedAll;

    public event Action<DdpChange>? DdpChanged;

    private bool Connected => _hub is { State: HubConnectionState.Connected };

    public async Task StartAsync()
    {
        if (_hub != null) return;
        try
        {
            HubConnection hub = new HubConnectionBuilder()
                .WithUrl($"{App.ApiBaseUrl}/hubs/project", o =>
                    o.AccessTokenProvider = () => Task.FromResult<string?>(App.Token))
                .WithAutomaticReconnect()
                .Build();

            hub.On<DdpChange>("DdpChanged", c => DdpChanged?.Invoke(c));
            hub.Reconnected += async _ => await RejoinAsync();

            _hub = hub;
            await hub.StartAsync();
            await RejoinAsync();
        }
        catch (Exception)
        {
            // Real-time non disponibile (hub spento/rete): la pagina resta usabile con l'Aggiorna manuale.
            _hub = null;
        }
    }

    public async Task JoinAllAsync()
    {
        _joinedAll = true;
        if (Connected) { try { await _hub!.InvokeAsync("JoinAll"); } catch { } }
    }

    public async Task JoinProjectAsync(int projectId)
    {
        _joinedProject = projectId;
        if (Connected) { try { await _hub!.InvokeAsync("JoinProject", projectId); } catch { } }
    }

    private async Task RejoinAsync()
    {
        if (!Connected) return;
        try
        {
            if (_joinedAll) await _hub!.InvokeAsync("JoinAll");
            if (_joinedProject.HasValue) await _hub!.InvokeAsync("JoinProject", _joinedProject.Value);
        }
        catch { /* best-effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        HubConnection? hub = _hub;
        _hub = null;
        if (hub == null) return;
        try { await hub.DisposeAsync(); } catch { /* chiusura best-effort */ }
    }
}
