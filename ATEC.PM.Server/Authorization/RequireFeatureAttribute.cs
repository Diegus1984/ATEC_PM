using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Authorization;

/// <summary>
/// Richiede che il ruolo dell'utente abbia un livello &gt;= <c>min_level</c> della feature
/// indicata (tabelle <c>auth_levels</c>/<c>auth_features</c>). È l'enforcement server-side
/// speculare a <c>PermissionEngine</c>, finora applicato solo lato client.
///
/// Va usato INSIEME a <c>[Authorize]</c>: l'autenticazione resta a carico di [Authorize],
/// qui si valuta solo l'autorizzazione per livello. Su accesso negato → 403 con envelope ApiResponse.
/// </summary>
public class RequireFeatureAttribute : TypeFilterAttribute
{
    public RequireFeatureAttribute(string featureKey) : base(typeof(RequireFeatureFilter))
    {
        Arguments = new object[] { featureKey };
    }

    private sealed class RequireFeatureFilter : IAuthorizationFilter
    {
        private readonly string _featureKey;
        private readonly FeatureAccessService _access;

        public RequireFeatureFilter(string featureKey, FeatureAccessService access)
        {
            _featureKey = featureKey;
            _access = access;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            ClaimsPrincipal user = context.HttpContext.User;

            // Se non autenticato lascia decidere alla pipeline standard (→ 401), non 403.
            if (user?.Identity?.IsAuthenticated != true)
                return;

            string role = user.FindFirst(ClaimTypes.Role)?.Value ?? "";
            if (!_access.CanAccess(role, _featureKey))
            {
                context.Result = new ObjectResult(
                    ApiResponse<string>.Fail("Accesso negato: privilegi insufficienti per questi dati."))
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }
    }
}
