using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Authorization;

/// <summary>
/// Richiede che il ruolo dell'utente abbia un livello &gt;= <c>min_level</c> della feature
/// indicata (tabelle <c>auth_levels</c>/<c>auth_features</c>). È l'enforcement server-side
/// speculare al controllo di navigazione del client web.
///
/// Va usato INSIEME a <c>[Authorize]</c>: l'autenticazione resta a carico di [Authorize],
/// qui si valuta solo l'autorizzazione per livello. Su accesso negato → 403 con envelope ApiResponse.
///
/// <para><b>Più chiavi = OR (via d'uscita).</b> Passando più chiavi l'accesso è consentito se
/// <b>almeno una</b> lo consente, e la scrittura se almeno una la consente. Serve agli endpoint
/// che stanno su una rotta di commessa ma sono usati anche da una pagina globale con una chiave
/// propria: <c>GET /api/projects/{id}/ddp</c> è la sezione «DDP Commerciali» della commessa
/// <b>e</b> la sorgente della Sintesi del Gestore DDP; con la sola chiave di sezione la Sintesi
/// si sarebbe svuotata senza un errore a video (segnalazione #63, Fase 1).</para>
/// </summary>
public class RequireFeatureAttribute : TypeFilterAttribute
{
    private readonly string[] _featureKeys;
    private bool _accessOnly;

    public RequireFeatureAttribute(params string[] featureKeys) : base(typeof(RequireFeatureFilter))
    {
        _featureKeys = featureKeys;
        Arguments = new object[] { featureKeys, false };
    }

    /// <summary>
    /// Salta il controllo di scrittura: l'endpoint si limita a registrare uno stato di
    /// consultazione, quindi una concessione in sola lettura deve poterlo chiamare.
    /// Caso reale: <c>POST /api/chat/{id}/mark-read</c> parte da sola a ogni apertura di una
    /// chat — con il ramo di scrittura attivo, chi ha la chat «in sola lettura» prenderebbe 403
    /// a ogni clic e il badge dei non letti non si azzererebbe mai. Il client ingoia quell'errore,
    /// quindi il difetto sarebbe invisibile.
    /// </summary>
    public bool AccessOnly
    {
        get => _accessOnly;
        set
        {
            _accessOnly = value;
            Arguments = new object[] { _featureKeys, value };
        }
    }

    private sealed class RequireFeatureFilter : IAuthorizationFilter
    {
        private readonly string[] _featureKeys;
        private readonly bool _accessOnly;
        private readonly FeatureAccessService _access;

        public RequireFeatureFilter(string[] featureKeys, bool accessOnly, FeatureAccessService access)
        {
            _featureKeys = featureKeys;
            _accessOnly = accessOnly;
            _access = access;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            ClaimsPrincipal user = context.HttpContext.User;

            // Se non autenticato lascia decidere alla pipeline standard (→ 401), non 403.
            if (user?.Identity?.IsAuthenticated != true)
                return;

            string role = user.FindFirst(ClaimTypes.Role)?.Value ?? "";
            // Id del dipendente, non il nome: `Identity.Name` è il NOME COMPLETO (vedi
            // AuthController.Login) e non combacia con nessuna colonna di `employees`.
            _ = int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int employeeId);

            // OR fra le chiavi: basta che una conceda l'accesso.
            if (!_featureKeys.Any(key => _access.CanAccessUser(employeeId, role, key)))
            {
                context.Result = new ObjectResult(
                    ApiResponse<string>.Fail("Accesso negato: privilegi insufficienti per questi dati."))
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }

            // Concessione in sola lettura (auth_role_features.access = 'READ'): la pagina si
            // apre e i dati si leggono, ma qualsiasi verbo di scrittura viene respinto.
            // Anche qui in OR: se una sola delle chiavi dà la scrittura, si scrive.
            if (_accessOnly || IsReadMethod(context.HttpContext.Request.Method)) return;

            if (!_featureKeys.Any(key => _access.CanWriteUser(employeeId, role, key)))
            {
                context.Result = new ObjectResult(
                    ApiResponse<string>.Fail("Accesso in sola lettura: il tuo ruolo non può modificare questi dati."))
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }

        private static bool IsReadMethod(string method) =>
            HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
    }
}
