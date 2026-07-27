using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Hubs;

/// <summary>
/// Hub real-time del planner Gestione Risorse: il server notifica ai client connessi
/// le modifiche alle allocazioni (create/update/delete) così tutti i Gantt aperti si
/// aggiornano in ~1s (stile calendario condiviso), più la presenza online (pallino
/// verde/rosso accanto al nome).
///
/// Le modifiche alle allocazioni restano unidirezionali (broadcast dal controller via
/// IHubContext&lt;ResourcePlannerHub&gt;). Per la presenza invece l'hub traccia le connessioni:
/// alla connect/disconnect aggiorna <see cref="UserPresenceService"/> e notifica tutti i client
/// con "PresenceChanged"; i client possono anche richiedere lo snapshot iniziale con
/// <see cref="GetOnlineEmployeeIds"/> (evita la corsa tra connessione e primo broadcast).
/// L'autenticazione è la stessa JWT del resto dell'API (token via query string `access_token`,
/// gestito in Program.cs con JwtBearerEvents per il path dell'hub).
/// </summary>
[Authorize]
public class ResourcePlannerHub : Hub
{
    private readonly UserPresenceService _presence;

    public ResourcePlannerHub(UserPresenceService presence) => _presence = presence;

    public override async Task OnConnectedAsync()
    {
        int employeeId = GetEmployeeId();
        if (employeeId > 0)
        {
            _presence.Register(Context.ConnectionId, employeeId);
            await NotifyPresenceChanged();
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _presence.Unregister(Context.ConnectionId);
        await NotifyPresenceChanged();
        await base.OnDisconnectedAsync(exception);
    }

    public List<int> GetOnlineEmployeeIds() => _presence.GetOnlineEmployeeIds();

    private Task NotifyPresenceChanged() =>
        Clients.All.SendAsync("PresenceChanged",
            new PresenceSnapshot { OnlineEmployeeIds = _presence.GetOnlineEmployeeIds() });

    private int GetEmployeeId() =>
        int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : 0;
}
