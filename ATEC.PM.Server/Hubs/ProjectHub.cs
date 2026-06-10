using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ATEC.PM.Server.Hubs;

/// <summary>
/// Hub real-time delle commesse: notifica ai client che stanno guardando una commessa le modifiche
/// ai suoi dati condivisi (oggi la distinta DDP) così le viste aperte si aggiornano in ~1s.
///
/// Scope per commessa: il client entra nel gruppo "project-{id}" (JoinProject) e riceve solo gli
/// eventi di quella commessa. Broadcast lato server via IHubContext&lt;ProjectHub&gt;.
/// Auth = stessa JWT del resto dell'API (token via query string `access_token`, gestito in Program.cs).
/// </summary>
[Authorize]
public class ProjectHub : Hub
{
    // Gruppo globale: chi (es. il Gestore DDP) vuole sapere di TUTTE le commesse, non di una sola.
    public const string AllGroup = "ddp-all";

    private static string GroupName(int projectId) => $"project-{projectId}";

    public Task JoinProject(int projectId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(projectId));

    public Task LeaveProject(int projectId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(projectId));

    public Task JoinAll() => Groups.AddToGroupAsync(Context.ConnectionId, AllGroup);

    public Task LeaveAll() => Groups.RemoveFromGroupAsync(Context.ConnectionId, AllGroup);
}
