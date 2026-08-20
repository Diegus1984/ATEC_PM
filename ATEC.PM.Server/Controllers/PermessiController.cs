using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ATEC.PM.Server.Authorization;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// La scheda permessi delle persone (PIANO-PERMESSI.md §5, Fase B). È l'unica porta da cui
/// <c>employee_feature_access</c> si scrive: fino a qui i permessi si cambiavano solo dal
/// database, perché la vecchia pagina «Permessi» scrive su tabelle che il motore non legge più.
///
/// <para>Tutto sta dietro <c>nav.permessi</c>. Le GET chiedono l'accesso, le scritture — per
/// come funziona <see cref="RequireFeatureAttribute"/> — chiedono anche la concessione piena:
/// chi ha la pagina in sola lettura guarda chi vede cosa senza poterlo cambiare, che è una cosa
/// sensata da poter concedere.</para>
/// </summary>
[ApiController]
[Route("api/permessi")]
[Authorize]
[RequireFeature("nav.permessi")]
public class PermessiController : ControllerBase
{
    private readonly PermissionAdminService _permessi;

    public PermessiController(PermissionAdminService permessi) => _permessi = permessi;

    /// <summary>Chi sta facendo la modifica: finisce nel registro. È l'id, non il nome.</summary>
    private int? UtenteCorrente =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : null;

    /// <summary>
    /// Esegue un gesto e traduce il rifiuto dell'invariante in un **409 leggibile**, non in un
    /// 500: «non ci si chiude fuori» è una risposta prevista del sistema, e chi la riceve deve
    /// capire cosa ha sbagliato senza aprire i log (§5.1 — «rifiuto con messaggio leggibile»).
    /// </summary>
    private IActionResult Esegui<T>(Func<T> gesto, string messaggioOk)
    {
        try
        {
            T esito = gesto();
            return Ok(ApiResponse<T>.Ok(esito, messaggioOk));
        }
        catch (PermissionAdminService.UltimoAmministratoreException ex)
        {
            return Conflict(ApiResponse<string>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<string>.Fail(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<string>.Fail(ex.Message));
        }
    }

    /// <summary>Elenco delle persone attive, con quante funzioni hanno e quante sono diverse dalla classe.</summary>
    [HttpGet]
    public IActionResult Elenco() =>
        Ok(ApiResponse<List<RigaPermessiDto>>.Ok(_permessi.Elenco()));

    /// <summary>La scheda di una persona: le 9 aree, il catalogo intero e le ultime 20 modifiche.</summary>
    [HttpGet("{employeeId:int}")]
    public IActionResult Scheda(int employeeId) =>
        Esegui(() => _permessi.Scheda(employeeId), "");

    /// <summary>I profili della pagina Master: classi con pacchetto riassunto (§5.4 rebuild).</summary>
    [HttpGet("classi")]
    public IActionResult Classi() =>
        Ok(ApiResponse<List<ClasseDto>>.Ok(_permessi.Classi()));

    /// <summary>Il pacchetto di una classe (lo legge la pagina Master).</summary>
    [HttpGet("classe/{classe}")]
    public IActionResult Pacchetto(string classe) =>
        Ok(ApiResponse<List<AuthRoleFeatureDto>>.Ok(_permessi.Pacchetto(classe)));

    /// <summary>
    /// Scrive una voce del template (pagina Master). Salvare il master aggiorna SOLO il
    /// template: nessun utente cambia finché non si preme «Applica template» (§3.5).
    /// </summary>
    [HttpPut("pacchetto")]
    public IActionResult ImpostaPacchetto([FromBody] ImpostaPacchettoRequest req) =>
        Esegui<object?>(() => { _permessi.ImpostaPacchetto(req); return null; },
            "Template aggiornato: nessun utente cambia finché non lo applichi");

    /// <summary>
    /// Cambia una combo — una delle 9 aree o una singola funzione avanzata. Quello che si tocca
    /// diventa <c>MANO</c> e «Applica classe» non lo sovrascrive più.
    /// </summary>
    [HttpPut("combo")]
    public IActionResult Imposta([FromBody] ImpostaPermessoRequest req) =>
        Esegui<object?>(() => { _permessi.Imposta(req, UtenteCorrente); return null; }, "Permesso aggiornato");

    /// <summary>
    /// Applica il pacchetto della classe. <b>Con <c>anteprima = true</c> non scrive niente</b> e
    /// torna l'elenco esatto di cosa cambierebbe: è quello che si conferma, non il pulsante.
    /// Le combo decise a mano restano dove sono e vengono contate a parte.
    /// </summary>
    [HttpPost("applica-classe")]
    public IActionResult ApplicaClasse([FromBody] ApplicaClasseRequest req) =>
        Esegui(() => _permessi.ApplicaClasse(req, UtenteCorrente),
            req.Anteprima ? "" : "Classe applicata");

    /// <summary>
    /// Copia la scheda di un collega — un CLONE, origin compresi (§3.6). Con
    /// <c>anteprima = true</c> non scrive niente e torna l'elenco esatto dei cambi.
    /// </summary>
    [HttpPost("copia")]
    public IActionResult Copia([FromBody] CopiaPermessiRequest req) =>
        Esegui(() => _permessi.CopiaDa(req, UtenteCorrente),
            req.Anteprima ? "" : "Scheda copiata");

    /// <summary>Riporta una funzione (o tutte) al valore della classe.</summary>
    [HttpPost("riallinea")]
    public IActionResult Riallinea([FromBody] RiallineaPermessoRequest req) =>
        Esegui(() => _permessi.Riallinea(req, UtenteCorrente), "Riallineato alla classe");
}
