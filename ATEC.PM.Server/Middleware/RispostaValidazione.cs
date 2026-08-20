using ATEC.PM.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ATEC.PM.Server.Middleware;

/// <summary>
/// Traduce gli errori di validazione dei DTO nel contratto <see cref="ApiResponse{T}"/>.
///
/// <para><b>Perché non si lascia fare ad ASP.NET.</b> Di suo, quando un <c>[Required]</c> o un
/// <c>[Range]</c> non è soddisfatto, risponde 400 con un <c>ProblemDetails</c>: un formato che il
/// client web non sa leggere — cerca <c>success</c> e <c>message</c>, non trova né l'uno né
/// l'altro, e l'utente si ritrova un errore generico al posto di «l'importo non può essere
/// negativo». Sarebbe una validazione che funziona e non si capisce.</para>
///
/// <para>Sta in una classe a sé, e non inline in <c>Program.cs</c>, per poterla provare nei test.</para>
/// </summary>
public static class RispostaValidazione
{
    /// <summary>Il messaggio che vede l'utente: cosa non va, campo per campo.</summary>
    public static string Messaggio(ModelStateDictionary modelState)
    {
        string[] problemi = modelState
            .Where(kv => kv.Value?.Errors.Count > 0)
            .Select(kv =>
            {
                // ASP.NET nomina i campi del corpo JSON come "$.importo": il "$." non significa
                // niente per chi legge, e nei messaggi di errore del binding compare spesso.
                string campo = kv.Key.TrimStart('$', '.');
                string primo = kv.Value!.Errors[0].ErrorMessage;

                if (string.IsNullOrWhiteSpace(primo))
                    return string.IsNullOrWhiteSpace(campo) ? "valore non valido" : campo;

                return string.IsNullOrWhiteSpace(campo) ? primo : $"{campo}: {primo}";
            })
            .ToArray();

        return problemi.Length == 0
            ? "Dati non validi."
            : "Dati non validi — " + string.Join(" · ", problemi);
    }

    /// <summary>La risposta 400 completa, nel formato di tutte le altre.</summary>
    public static IActionResult Crea(ModelStateDictionary modelState) =>
        new BadRequestObjectResult(ApiResponse<string>.Fail(Messaggio(modelState)));
}
