using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;


/// <summary>
/// Quello che i controller della commessa (<c>api/projects/…</c>) hanno in comune: i
/// servizi, l'identità di chi chiama, la firma sulle scritture, le notifiche real-time delle
/// distinte. Nato il 04/09/2026 spaccando <c>ProjectsController</c> (3.212 righe, cinque
/// domini) in un controller per dominio; le rotte non sono cambiate.
/// </summary>
public abstract class ProjectsControllerBase : ControllerBase
{
    protected readonly DbService _db;
    protected readonly IHubContext<ProjectHub> _hub;
    protected readonly NotificationService _notif;
    protected readonly FeatureAccessService _access;
    protected readonly AnagraficheCache _cache;

    protected ProjectsControllerBase(DbService db, IHubContext<ProjectHub> hub, NotificationService notif, FeatureAccessService access, AnagraficheCache cache)
    {
        _db = db;
        _hub = hub;
        _notif = notif;
        _access = access;
        _cache = cache;
    }

    // Notifica real-time: chi guarda QUESTA commessa (gruppo "project-{id}") + il Gestore DDP (gruppo globale
    // "ddp-all"), escludendo chi ha fatto la modifica (conn) per non auto-ricaricarsi.
    // ddpType ("COMMERCIAL" | "OFFICINA") permette ai client di ignorare la distinta che non li riguarda.
    protected void NotifyDdpChange(int projectId, string? conn, string action, int itemId, string ddpType = "COMMERCIAL")
    {
        var payload = new DdpChange { ProjectId = projectId, Action = action, ItemId = itemId, DdpType = ddpType };
        foreach (string group in new[] { $"project-{projectId}", ProjectHub.AllGroup })
        {
            IClientProxy target = string.IsNullOrEmpty(conn)
                ? _hub.Clients.Group(group)
                : _hub.Clients.GroupExcept(group, conn);
            target.SendAsync("DdpChanged", payload).SenzaAttesa("DdpChanged");
        }
    }

    // Il sync DDP Officina → lavorazioni tocca project_work_requests: avvisa il Pannello
    // Lavorazioni (gruppo globale) e la griglia nel dettaglio commessa (stesso contratto
    // del WorkRequestsController).
    protected void NotifyWorkRequestsChanged(string action, int projectId)
    {
        _hub.Clients.Group(ProjectHub.WorkRequestsGroup)
            .SendAsync("WorkRequestsChanged", new { action, projectId = (int?)projectId }).SenzaAttesa("WorkRequestsChanged");
        _hub.Clients.Group(ProjectHub.ProjectGroup(projectId))
            .SendAsync("WorkRequestsChanged", new { action, projectId = (int?)projectId }).SenzaAttesa("WorkRequestsChanged");
    }

    protected int GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    /// <summary>
    /// Chi ha <c>action.ddp_status_override</c> non è vincolato alla matrice degli avanzamenti
    /// (segnalazione #140): amministratori e PM devono poter riportare indietro una riga che un
    /// collega ha mandato nello stato sbagliato, altrimenti l'errore è definitivo. Il client
    /// mostra la finestra completa alle stesse persone, leggendo la stessa chiave da
    /// <c>/features/my</c>; qui il controllo si rifà comunque, perché la finestra ristretta del
    /// menu è un aiuto e non un cancello.
    /// </summary>
    protected bool PuoScavalcareMatriceDdp() =>
        _access.CanAccessUser(
            GetCurrentEmployeeId(),
            User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
            DdpTransitionService.FeatureScavalcaMatrice);

    /// <summary>
    /// I parametri di una UPDATE più la firma di chi sta scrivendo (<c>@UpdatedBy</c>, #114).
    /// L'oggetto della richiesta non ha un campo per l'autore — e non deve averlo: lo decide il
    /// token, non il client. Token senza dipendente → NULL, cioè modifica senza firma.
    /// </summary>
    protected DynamicParameters ConFirma(object req)
    {
        var parametri = new DynamicParameters(req);
        int me = GetCurrentEmployeeId();
        parametri.Add("UpdatedBy", me > 0 ? me : (int?)null);
        return parametri;
    }
}
