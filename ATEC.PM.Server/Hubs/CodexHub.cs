using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ATEC.PM.Server.Hubs;

/// <summary>
/// Hub real-time del Codex: notifica i client che stanno guardando la Composizione quando un altro
/// utente aggiunge/rimuove un componente, così l'albero della distinta si aggiorna in ~1s.
///
/// Le composizioni Codex sono globali (non per-commessa) → niente gruppi: broadcast a TUTTI i client
/// connessi all'hub via IHubContext&lt;CodexHub&gt;, escludendo l'autore (parametro conn) per non
/// auto-ricaricarlo. Hub VUOTO: esiste solo per routing+auth, flusso solo server→client (come
/// ResourcePlannerHub). Auth = stessa JWT del resto dell'API (token via query string `access_token`,
/// gestito in Program.cs per tutti i path /hubs).
/// </summary>
[Authorize]
public class CodexHub : Hub
{
}
