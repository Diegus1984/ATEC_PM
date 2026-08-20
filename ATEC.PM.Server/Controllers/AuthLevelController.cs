using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/auth-levels")]
[Authorize]
public class AuthLevelController : ControllerBase
{
    private readonly DbService _db;
    private readonly FeatureAccessService _access;
    private readonly PermissionSeedService _seed;
    private readonly PermissionChangeService _changes;
    public AuthLevelController(
        DbService db,
        FeatureAccessService access,
        PermissionSeedService seed,
        PermissionChangeService changes)
    {
        _db = db;
        _access = access;
        _seed = seed;
        _changes = changes;
    }

    /// <summary>
    /// Chi sta facendo la modifica: finisce in <c>employee_feature_access_log.changed_by</c>.
    /// È l'id, non il nome: <c>Identity.Name</c> porta il NOME COMPLETO (vedi AuthController).
    /// </summary>
    private int? UtenteCorrente =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : null;

    // ── Passaggio al modello «permessi sulla persona» (PIANO-PERMESSI.md, Fase A) ────────
    //
    // Sono operazioni di migrazione, non di uso quotidiano: stanno dietro `nav.permessi`
    // (livello 3) e non compaiono in nessuna pagina. Servono a poter rifare seed e confronto
    // quante volte occorre SENZA un nuovo deploy — il piano chiede di verificare la fotografia
    // «col diff, non con la fiducia», e un controllo che si può ripetere è l'unico che si fa
    // davvero.

    /// <summary>
    /// Confronto fra il motore vecchio e le righe per persona. Deve tornare <b>vuoto</b> prima di
    /// invertire il fallback: ogni differenza qui è una persona che perderebbe (o guadagnerebbe)
    /// qualcosa senza che nessuno l'abbia deciso.
    /// </summary>
    [HttpGet("migrazione/diff")]
    [RequireFeature("nav.permessi")]
    public IActionResult DiffPermessi()
    {
        var esito = _seed.Diff();
        return Ok(ApiResponse<PermissionSeedService.Esito>.Ok(esito,
            esito.Differenze.Count == 0
                ? $"Nessuna differenza su {esito.Persone} persone × {esito.Funzioni} funzioni."
                : $"{esito.Differenze.Count} differenze da guardare prima di procedere."));
    }

    /// <summary>
    /// Materializza sulla persona i permessi che ha oggi. Con <c>prova=true</c> non scrive niente
    /// e dice solo cosa farebbe. Non tocca mai le righe messe a mano (<c>origin = MANO</c>).
    /// </summary>
    [HttpPost("migrazione/seed")]
    [RequireFeature("nav.permessi")]
    public IActionResult SeedPermessi([FromQuery] bool prova = false)
    {
        var esito = _seed.Seed(prova, UtenteCorrente);
        return Ok(ApiResponse<PermissionSeedService.Esito>.Ok(esito,
            prova
                ? $"Prova: scriverebbe {esito.RigheScritte} righe e ne toglierebbe {esito.RigheTolte}."
                : $"Scritte {esito.RigheScritte} righe, tolte {esito.RigheTolte}, su {esito.Persone} persone."));
    }

    private const string LevelsSelect =
        "SELECT id AS Id, level_value AS LevelValue, role_name AS RoleName, display_name AS DisplayName, " +
        "sort_order AS SortOrder, access_mode AS AccessMode FROM auth_levels ORDER BY sort_order";

    /// <summary>Lista livelli autorizzazione</summary>
    [HttpGet]
    public IActionResult GetLevels()
    {
        using var c = _db.Open();
        var levels = c.Query<AuthLevelDto>(LevelsSelect).ToList();
        return Ok(ApiResponse<List<AuthLevelDto>>.Ok(levels));
    }

    /// <summary>Lista tutte le feature con livello minimo</summary>
    [HttpGet("features")]
    public IActionResult GetFeatures()
    {
        using var c = _db.Open();
        var features = c.Query<AuthFeatureDto>(
            "SELECT id AS Id, feature_key AS FeatureKey, display_name AS DisplayName, category AS Category, min_level AS MinLevel, behavior AS Behavior FROM auth_features ORDER BY category, min_level, display_name").ToList();
        return Ok(ApiResponse<List<AuthFeatureDto>>.Ok(features));
    }

    /// <summary>Feature accessibili per l'utente corrente (chiamato al login)</summary>
    [HttpGet("features/my")]
    public IActionResult GetMyFeatures()
    {
        string role = User.FindFirst(ClaimTypes.Role)?.Value ?? "TECH";
        // Id del dipendente: `Identity.Name` è il nome completo, non lo username.
        _ = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int employeeId);

        using var c = _db.Open();
        int userLevel = c.ExecuteScalar<int>(
            "SELECT COALESCE((SELECT level_value FROM auth_levels WHERE role_name=@Role), 0)",
            new { Role = role });

        // Le chiavi ritirate (fuori catalogo, rebuild §12.8.10) non escono da qui: né nel
        // catalogo per il client, né — più sotto — nell'espansione del jolly.
        var features = c.Query<AuthFeatureDto>(
            @"SELECT id AS Id, feature_key AS FeatureKey, display_name AS DisplayName,
                     category AS Category, min_level AS MinLevel, behavior AS Behavior
              FROM auth_features WHERE retired_at IS NULL ORDER BY category, display_name").ToList();

        var levels = c.Query<AuthLevelDto>(LevelsSelect).ToList();

        // Contatore di versione dei permessi di QUESTA persona: sale a ogni modifica che la
        // riguarda (vedi PermissionChangeService). Il client se lo tiene e lo confronta con
        // quello che arriva dall'hub con "PermissionsChanged", così sa se la risposta che ha
        // appena riletto è già quella nuova — due modifiche ravvicinate mandano due avvisi.
        int permissionsVersion = c.ExecuteScalar<int>(
            "SELECT COALESCE((SELECT permissions_version FROM employees WHERE id=@Id), 0)",
            new { Id = employeeId });

        // Col motore nuovo la risposta è la stessa di sempre, ma i grant sono quelli della
        // PERSONA e la modalità è «lista bianca»: il client la conosce già (la usa per la
        // Contabilità), quindi l'inversione non gli cambia il contratto.
        //   Il jolly viene ESPANSO qui su tutto il catalogo: il client non sa cosa sia '*', e
        //   fargliela imparare vorrebbe dire due implementazioni della stessa regola da tenere
        //   allineate — la prima che diverge apre o chiude una pagina di troppo.
        Dictionary<string, string> grants;
        string accessMode;
        if (_access.IsMotoreNuovoAttivo())
        {
            var righe = _access.GetPersonGrants(employeeId);
            accessMode = "GRANTS";

            // ⚠️ Nel contratto col client «chiave presente = concessa»: le righe di DINIEGO
            // (access = 'NO') non devono uscire di qui, o il client le leggerebbe come
            // concessioni — cioè il Timesheet spento a mano all'ufficio Acquisti tornerebbe
            // visibile nel menu, e la riga che serve a togliere finirebbe per dare.
            if (righe.TryGetValue(FeatureAccessService.JollyKey, out string? jolly)
                && !FeatureAccessService.Negato(jolly))
            {
                // Jolly espanso su tutto il catalogo, tolto ciò che è negato riga per riga:
                // la decisione sulla singola funzione è più specifica del «vale tutto».
                grants = features
                    .Where(f => !righe.TryGetValue(f.FeatureKey, out string? a) || !FeatureAccessService.Negato(a))
                    .ToDictionary(
                        f => f.FeatureKey,
                        f => righe.TryGetValue(f.FeatureKey, out string? a) ? a : jolly,
                        StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                // Le righe esplicite rimaste su una chiave ritirata non devono riaffiorare:
                // la chiave è fuori catalogo, per il client non esiste (§12.8.10).
                var ritirate = c.Query<string>(
                        "SELECT feature_key FROM auth_features WHERE retired_at IS NOT NULL")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                grants = righe
                    .Where(kv => kv.Key != FeatureAccessService.JollyKey
                                 && !FeatureAccessService.Negato(kv.Value)
                                 && !ritirate.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            }
        }
        else
        {
            // Reparto Contabilità: la sua lista SOSTITUISCE i permessi del livello (non si somma),
            // quindi il client va messo in modalità «lista bianca» come per i ruoli di reparto —
            // altrimenti il menu continua a mostrargli tutto quello che il livello concede.
            bool contabilita = _access.IsRestrictedToContabilita(employeeId, role);
            grants = contabilita
                ? _access.GetContabilitaGrants(role)
                : _access.GetGrantsForRole(role);
            accessMode = contabilita || _access.IsGrantsRole(role) ? "GRANTS" : "LEVEL";
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            UserLevel = userLevel,
            Features = features,
            Levels = levels,
            AccessMode = accessMode,
            Grants = grants,
            PermissionsVersion = permissionsVersion,
            // Quale motore ha risposto. Non serve a decidere permessi — quelli sono già in
            // Grants — ma a dire la verità nella pagina «Permessi»: col motore nuovo acceso la
            // matrice funzioni × ruoli scrive in `auth_role_features` e `auth_features`, che
            // nessuno legge più, quindi un salvataggio lì non cambia niente per nessuno e
            // l'unico modo di scoprirlo era non vedere effetti.
            PermissionsEngine = _access.IsMotoreNuovoAttivo() ? "NEW" : "LEGACY",
        }));
    }

    /// <summary>Tutte le concessioni per ruolo (matrice della pagina «Permessi»).</summary>
    [HttpGet("role-features")]
    public IActionResult GetRoleFeatures()
    {
        using var c = _db.Open();
        var grants = c.Query<AuthRoleFeatureDto>(
            "SELECT role_name AS RoleName, feature_key AS FeatureKey, access AS Access FROM auth_role_features").ToList();
        return Ok(ApiResponse<List<AuthRoleFeatureDto>>.Ok(grants));
    }

    /// <summary>Assegna una concessione a un ruolo, o la revoca con Access vuoto.</summary>
    [HttpPut("role-features")]
    [Authorize]
    [RequireFeature("nav.permessi")]
    public IActionResult SetRoleFeature([FromBody] SetRoleFeatureRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RoleName) || string.IsNullOrWhiteSpace(req.FeatureKey))
            return BadRequest(ApiResponse<string>.Fail("RoleName e FeatureKey sono obbligatori"));

        string? access = string.IsNullOrWhiteSpace(req.Access) ? null : req.Access.Trim().ToUpperInvariant();
        if (access != null && access != FeatureAccessService.AccessRead && access != FeatureAccessService.AccessFull)
            return BadRequest(ApiResponse<string>.Fail("Access ammette solo READ, FULL o vuoto"));

        using var c = _db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM auth_levels WHERE role_name=@Role",
                new { Role = req.RoleName }) == 0)
            return NotFound(ApiResponse<string>.Fail($"Ruolo '{req.RoleName}' inesistente"));

        // La concessione si scrive sul RUOLO, ma a cambiare è quello che vedono le PERSONE con
        // quel ruolo: registro, contatore di versione e avviso real-time vanno su di loro —
        // e solo su chi cambia davvero (chi aveva già la funzione per livello non si accorge di
        // niente e non va svegliato). Il Reload della cache lo fa ApplicaEPropaga.
        _changes.ApplicaEPropaga(req.FeatureKey, req.RoleName, UtenteCorrente, () =>
        {
            if (access == null)
            {
                c.Execute("DELETE FROM auth_role_features WHERE role_name=@RoleName AND feature_key=@FeatureKey", req);
            }
            else
            {
                c.Execute(@"INSERT INTO auth_role_features (role_name, feature_key, access)
                            VALUES (@RoleName, @FeatureKey, @Access)
                            ON DUPLICATE KEY UPDATE access = VALUES(access)",
                    new { req.RoleName, req.FeatureKey, Access = access });
            }
        });

        return Ok(ApiResponse<string>.Ok(access == null ? "Concessione rimossa" : "Concessione aggiornata"));
    }

    /// <summary>Aggiorna livello minimo o behavior di una feature</summary>
    [HttpPut("features/{id}")]
    [Authorize]
    [RequireFeature("nav.permessi")]
    public IActionResult UpdateFeature(int id, [FromBody] UpdateAuthFeatureRequest req)
    {
        using var c = _db.Open();

        // La chiave serve prima della scrittura, per sapere su quale funzione confrontare il
        // prima e il dopo. Si guadagna anche un 404 onesto: col vecchio `affected == 0` un
        // salvataggio che non cambiava nulla (stessi valori) rispondeva «Feature non trovata».
        string? key = c.ExecuteScalar<string>("SELECT feature_key FROM auth_features WHERE id=@Id", new { Id = id });
        if (key == null) return NotFound(ApiResponse<string>.Fail("Feature non trovata"));

        // Alzare o abbassare il livello minimo cambia chi vede quella pagina: chi la perde (o la
        // guadagna) va registrato e avvisato, non lasciato con il menu di prima fino all'F5.
        _changes.ApplicaEPropaga(key, null, UtenteCorrente, () =>
            c.Execute("UPDATE auth_features SET min_level=@MinLevel, behavior=@Behavior WHERE id=@Id",
                new { req.MinLevel, req.Behavior, Id = id }));

        return Ok(ApiResponse<string>.Ok("Feature aggiornata"));
    }

    // ── 🧹 Registrare / eliminare una funzione: NON SI FA PIÙ DA QUI (rebuild §12.6, passo 7) ──
    //
    // Dal passo 2 `auth_features` è la PROIEZIONE di `catalogo-permessi.json`: a ogni avvio
    // `CatalogoPermessiSync.Allinea` registra le chiavi del file, ne riallinea le etichette,
    // marca le ritirate e segnala le orfane. Con quel meccanismo acceso, i due gesti che
    // stavano qui non erano più «amministrazione» ma un secondo elenco che litiga col primo:
    //
    //   · CREA  → una chiave che non sta nel file non la usa nessun endpoint (nessun
    //             [RequireFeature] la nomina) e resta orfana per sempre, segnalata a ogni
    //             avvio: sembra un permesso e non protegge niente;
    //   · CANCELLA → se la chiave sta nel file torna al riavvio successivo (con min_level 3),
    //             ma intanto ha portato via le sue righe di `auth_role_features` — cioè la
    //             configurazione del motore VECCHIO, che è la strada del rollback.
    //
    // Una funzione nuova nasce così: si aggiunge la voce a `ATEC.PM.Shared/catalogo-permessi.json`
    // (il censimento in `ATEC.PM.Tests` stampa lo stub pronto se un `[RequireFeature]` nomina
    // una chiave che non c'è) e al primo avvio è in tabella. Una che va in pensione si marca
    // `ritirata` nello stesso file. Restano modificabili di qui `min_level` e `behavior`, che
    // sono le manopole del motore vecchio e non stanno nel catalogo.
}
