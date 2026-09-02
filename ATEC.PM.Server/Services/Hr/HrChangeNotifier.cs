using ATEC.PM.Server.Hubs;
using ATEC.PM.Shared.DTOs;
using Microsoft.AspNetCore.SignalR;

namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Manda «HrChanged» a chi ha davanti una pagina del modulo presenze (gruppo
/// <see cref="ProjectHub.HrGroup"/> su /hubs/project). Un solo punto per controller e servizio
/// di import: così anche l'import automatico delle 12 ore, che non passa da un controller,
/// fa aggiornare le pagine aperte. Niente self-exclusion: chi ha fatto la modifica rilegge già
/// da sé, e un doppio refetch di react-query non disturba nessuno.
/// </summary>
public sealed class HrChangeNotifier
{
    private readonly IHubContext<ProjectHub> _hub;

    public HrChangeNotifier(IHubContext<ProjectHub> hub)
    {
        _hub = hub;
    }

    public void Notify(string action, int? employeeId = null, DateTime? date = null)
    {
        // Fire-and-forget: un hub giù non deve mai far fallire un'operazione riuscita.
        _ = _hub.Clients.Group(ProjectHub.HrGroup)
            .SendAsync("HrChanged", new HrChange { Action = action, EmployeeId = employeeId, Date = date });
    }
}
