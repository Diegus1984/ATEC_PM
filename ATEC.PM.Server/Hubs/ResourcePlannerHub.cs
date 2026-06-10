using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ATEC.PM.Server.Hubs;

/// <summary>
/// Hub real-time del planner Gestione Risorse: il server notifica ai client connessi
/// le modifiche alle allocazioni (create/update/delete) così tutti i Gantt aperti si
/// aggiornano in ~1s (stile calendario condiviso).
///
/// Solo server→client: i client non invocano metodi, ascoltano l'evento "AssignmentsChanged".
/// L'autenticazione è la stessa JWT del resto dell'API (token via query string `access_token`,
/// gestito in Program.cs con JwtBearerEvents per il path dell'hub).
/// </summary>
[Authorize]
public class ResourcePlannerHub : Hub
{
    // Nessun metodo invocabile dai client: il flusso è unidirezionale (broadcast dal controller
    // via IHubContext<ResourcePlannerHub>). La classe esiste per il routing e l'auth.
}
