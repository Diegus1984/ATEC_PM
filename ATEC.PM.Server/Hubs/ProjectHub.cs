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

    // Gruppo globale Segnalazioni: la pagina bug/migliorie riceve BugReportsChanged per
    // ogni segnalazione — non sono legate a una commessa.
    public const string BugReportsGroup = "bugs-all";

    // Gruppo globale ANAGRAFICA commesse: chi ha davanti un elenco di commesse (albero,
    // Milestones, SAL, Lavorazioni, Check list, MoM) riceve ProjectsChanged quando una
    // commessa viene creata, modificata o eliminata, e ricarica la lista.
    public const string ProjectsGroup = "projects-all";

    // Gruppo globale CONFIGURAZIONE SEZIONI DI COSTO: gruppi, sezioni, reparti, anagrafica
    // delle fasi e tariffe. È una pagina che si lavora in due (uno assegna le fasi, l'altro
    // sistema i reparti) e ogni modifica cambia l'albero sotto gli occhi dell'altro: senza
    // avviso il secondo lavora su un albero vecchio e se ne accorge solo al refresh.
    public const string CostSectionsGroup = "cost-sections-all";

    // Inbox chat globale (campanella messaggi in header): ChatChanged di qualsiasi
    // commessa, così il badge si aggiorna anche se non sei dentro quella commessa.
    public const string ChatInboxGroup = "chat-inbox-all";

    // Gruppo globale PRESENZE (modulo HR): cartellino, calendario, quadratura, cronologia email
    // e richieste di assenza ricevono HrChanged per ogni modifica — import da Ecos (anche
    // quello automatico), rettifiche, solleciti, causali, richieste, mappatura, credenziali.
    public const string HrGroup = "hr-all";

    public static string ProjectGroup(int projectId) => $"project-{projectId}";

    // Gruppo PERSONALE dei permessi: uno per dipendente. Quando i permessi di quella persona
    // cambiano il server le manda "PermissionsChanged" e il client rilegge /features/my da
    // solo. Senza, il menu resta com'era fino al primo F5: i permessi arrivano al client solo
    // al login e il token dura 8 ore (PIANO-PERMESSI.md §8).
    public static string PermissionsGroup(int employeeId) => $"permessi-{employeeId}";

    /// <summary>
    /// Gruppo PERSONALE, uno per dipendente: ci passano gli avvisi destinati a lui soltanto —
    /// oggi il messaggio di chat appena arrivato (<c>ChatMessageReceived</c>, #78), che porta con sé
    /// l'anteprima del testo e quindi non può viaggiare sui gruppi che ascoltano tutti.
    /// Come per i permessi, l'iscrizione la fa il server con l'id del token: non esiste un
    /// <c>JoinUser(id)</c>, o si potrebbero leggere le anteprime di un collega.
    /// </summary>
    public static string UserGroup(int employeeId) => $"user-{employeeId}";

    /// <summary>
    /// Iscrizione ai propri gruppi personali (permessi e avvisi), con l'id preso dal token. Non è un
    /// metodo chiamabile dal client apposta: un <c>JoinPermessi(id)</c> lascerebbe ascoltare il
    /// gruppo di un collega, e i cambi di permesso sono un dato di chi li subisce.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        if (int.TryParse(
                Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                out int employeeId) && employeeId > 0)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, PermissionsGroup(employeeId));
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(employeeId));
        }
        await base.OnConnectedAsync();
    }

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

    public Task JoinBugReports() => Groups.AddToGroupAsync(Context.ConnectionId, BugReportsGroup);

    public Task LeaveBugReports() => Groups.RemoveFromGroupAsync(Context.ConnectionId, BugReportsGroup);

    public Task JoinProjects() => Groups.AddToGroupAsync(Context.ConnectionId, ProjectsGroup);

    public Task LeaveProjects() => Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectsGroup);

    public Task JoinCostSections() => Groups.AddToGroupAsync(Context.ConnectionId, CostSectionsGroup);

    public Task LeaveCostSections() => Groups.RemoveFromGroupAsync(Context.ConnectionId, CostSectionsGroup);

    public Task JoinHr() => Groups.AddToGroupAsync(Context.ConnectionId, HrGroup);

    public Task LeaveHr() => Groups.RemoveFromGroupAsync(Context.ConnectionId, HrGroup);

    public Task JoinChatInbox() => Groups.AddToGroupAsync(Context.ConnectionId, ChatInboxGroup);

    public Task LeaveChatInbox() => Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatInboxGroup);

    /// <summary>Broadcast «sta scrivendo» agli altri della commessa, senza toccare il DB.</summary>
    public Task ChatTyping(int projectId, int chatId)
    {
        _ = int.TryParse(Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int empId);
        string name = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
        var payload = new ATEC.PM.Shared.DTOs.ChatTyping
        {
            ProjectId = projectId,
            ChatId = chatId,
            EmployeeId = empId,
            EmployeeName = name,
        };
        return Clients.OthersInGroup(ProjectGroup(projectId)).SendAsync("ChatTyping", payload);
    }
}
