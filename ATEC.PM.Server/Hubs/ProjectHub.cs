using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ATEC.PM.Server.Hubs;

/// <summary>
/// Hub real-time delle commesse: notifica ai client che stanno guardando una commessa le modifiche
/// ai suoi dati condivisi (distinta DDP, documenti, chat) così le viste aperte si aggiornano in ~1s.
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

    // Gruppo globale MoM: le pagine del modulo Verbali (lista, dettaglio, note) ricevono
    // MoMChanged per ogni verbale — le MoM di tipo RIUNIONE non hanno una commessa.
    public const string MoMGroup = "mom-all";

    // Gruppo globale Check list: la pagina PM aggregata riceve ChecklistChanged per ogni
    // attività (di commessa o di gruppo generico). Gli item di commessa vengono notificati
    // anche al gruppo "project-{id}" per il tab nel dettaglio commessa.
    public const string CheckListGroup = "checklist-all";

    // Gruppo globale Lavorazioni: il Pannello Lavorazioni (bozze/priorità/consegne/trattamenti)
    // riceve WorkRequestsChanged per ogni lavorazione; la griglia nel dettaglio commessa
    // ascolta invece il gruppo "project-{id}".
    public const string WorkRequestsGroup = "workrequests-all";

    public static string ProjectGroup(int projectId) => $"project-{projectId}";

    public Task JoinProject(int projectId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, ProjectGroup(projectId));

    public Task LeaveProject(int projectId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectGroup(projectId));

    public Task JoinAll() => Groups.AddToGroupAsync(Context.ConnectionId, AllGroup);

    public Task LeaveAll() => Groups.RemoveFromGroupAsync(Context.ConnectionId, AllGroup);

    public Task JoinMoM() => Groups.AddToGroupAsync(Context.ConnectionId, MoMGroup);

    public Task LeaveMoM() => Groups.RemoveFromGroupAsync(Context.ConnectionId, MoMGroup);

    public Task JoinCheckList() => Groups.AddToGroupAsync(Context.ConnectionId, CheckListGroup);

    public Task LeaveCheckList() => Groups.RemoveFromGroupAsync(Context.ConnectionId, CheckListGroup);

    public Task JoinWorkRequests() => Groups.AddToGroupAsync(Context.ConnectionId, WorkRequestsGroup);

    public Task LeaveWorkRequests() => Groups.RemoveFromGroupAsync(Context.ConnectionId, WorkRequestsGroup);
}
