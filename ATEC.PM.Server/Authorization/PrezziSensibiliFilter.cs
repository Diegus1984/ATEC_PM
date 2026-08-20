using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ATEC.PM.Server.Authorization;

/// <summary>
/// Il filtro UNICO dei dati sensibili (PIANO-PERMESSI-REBUILD.md §12.3): niente filtraggio
/// per-endpoint, la regola sta in un posto solo.
///
/// <para><b>Quando scatta.</b> Solo sugli endpoint le cui chiavi <c>[RequireFeature]</c>
/// appartengono a voci di catalogo col micro <c>prices</c>. Chi ha la chiave
/// <c>&lt;voce&gt;.prices</c> (basta una, in OR come l'accesso) passa senza costi: il filtro
/// non tocca niente.</para>
///
/// <para><b>In lettura</b> azzera le proprietà marcate <c>[DatoSensibile]</c> nell'oggetto di
/// risposta (envelope <c>ApiResponse</c> e liste comprese): col serializzatore in
/// <c>WhenWritingNull</c> i campi SPARISCONO dal JSON e il client li rende «—» via
/// <c>euro()</c>. Le calcolate senza setter (es. <c>TotalCost</c>) si annullano da sole
/// quando la loro sorgente è azzerata.</para>
///
/// <para><b>In scrittura</b> (la falla peggiore della revisione, §12.8): un salvataggio di chi
/// non vede i prezzi arriverebbe con i campi a <c>null</c> e cancellerebbe i valori veri. Un
/// membro sensibile VALORIZZATO da chi non ha il micro → <b>403</b>, forte e chiaro; membri a
/// <c>null</c> passano, e i percorsi di scrittura li trattano come «non toccare»
/// (<c>COALESCE</c>/flag — mai un null sopra un costo vero).</para>
///
/// <para>La riflessione sui tipi è cacheata per processo; i tipi senza membri sensibili
/// costano un lookup e basta.</para>
/// </summary>
public sealed class PrezziSensibiliFilter : IActionFilter, IResultFilter
{
    /// <summary>chiave di voce → chiave del micro prezzi, dal catalogo unico (statico).</summary>
    private static readonly IReadOnlyDictionary<string, string> VociConPrezzi =
        PermessiCatalogo.VociPrimarie()
            .Where(v => v.Micros.Contains("prices") && v.Chiave != null)
            .ToDictionary(v => v.Chiave!, v => $"{v.Chiave}.prices", StringComparer.OrdinalIgnoreCase);

    private readonly FeatureAccessService _access;

    /// <summary>null = endpoint non gestito dal filtro; false = l'utente NON vede i prezzi.</summary>
    private bool? _vedePrezzi;

    public PrezziSensibiliFilter(FeatureAccessService access) => _access = access;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        string[] chiaviEndpoint = context.ActionDescriptor.EndpointMetadata
            .OfType<RequireFeatureAttribute>()
            .SelectMany(a => a.Arguments is [string[] chiavi, ..] ? chiavi : Array.Empty<string>())
            .Distinct()
            .ToArray();

        var micros = chiaviEndpoint
            .Where(k => VociConPrezzi.ContainsKey(k))
            .Select(k => VociConPrezzi[k])
            .ToArray();
        if (micros.Length == 0) { _vedePrezzi = null; return; }

        ClaimsPrincipal user = context.HttpContext.User;
        string role = user.FindFirst(ClaimTypes.Role)?.Value ?? "";
        _ = int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int employeeId);

        _vedePrezzi = micros.Any(m => _access.CanAccessUser(employeeId, role, m));
        if (_vedePrezzi == true) return;

        // Scrittura senza micro: un membro sensibile VALORIZZATO è un tentativo di scrivere
        // un prezzo senza poterlo vedere → si respinge, non si ignora in silenzio.
        string metodo = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo))
            return;

        foreach (object? argomento in context.ActionArguments.Values)
        {
            if (argomento != null && ContieneSensibiliValorizzati(argomento, depth: 0))
            {
                context.Result = new ObjectResult(
                    ApiResponse<string>.Fail("Non hai il permesso di scrivere i prezzi: il salvataggio è stato rifiutato per non sovrascriverli."))
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (_vedePrezzi != false) return;
        if (context.Result is ObjectResult { Value: not null } risultato)
            AzzeraSensibili(risultato.Value, depth: 0, visitati: new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    // ── camminata sugli oggetti ──────────────────────────────────────────────────

    private sealed record PianoTipo(PropertyInfo[] Sensibili, PropertyInfo[] DaAttraversare, bool HaSensibiliInProfondita);

    private static readonly ConcurrentDictionary<Type, PianoTipo> Piani = new();

    private static void AzzeraSensibili(object oggetto, int depth, HashSet<object> visitati)
    {
        if (depth > 8 || !visitati.Add(oggetto)) return;

        if (oggetto is IEnumerable lista and not string)
        {
            foreach (object? voce in lista)
                if (voce != null && !EPrimitivo(voce.GetType()))
                    AzzeraSensibili(voce, depth + 1, visitati);
            return;
        }

        PianoTipo piano = PianoDi(oggetto.GetType());
        if (!piano.HaSensibiliInProfondita) return;

        foreach (PropertyInfo p in piano.Sensibili)
            p.SetValue(oggetto, null);

        foreach (PropertyInfo p in piano.DaAttraversare)
        {
            object? figlio = p.GetValue(oggetto);
            if (figlio != null) AzzeraSensibili(figlio, depth + 1, visitati);
        }
    }

    private static bool ContieneSensibiliValorizzati(object oggetto, int depth)
    {
        if (depth > 8) return false;

        if (oggetto is IEnumerable lista and not string)
        {
            foreach (object? voce in lista)
                if (voce != null && !EPrimitivo(voce.GetType()) && ContieneSensibiliValorizzati(voce, depth + 1))
                    return true;
            return false;
        }

        PianoTipo piano = PianoDi(oggetto.GetType());
        if (!piano.HaSensibiliInProfondita) return false;

        if (piano.Sensibili.Any(p => p.GetValue(oggetto) != null)) return true;

        return piano.DaAttraversare
            .Select(p => p.GetValue(oggetto))
            .Any(figlio => figlio != null && ContieneSensibiliValorizzati(figlio, depth + 1));
    }

    private static PianoTipo PianoDi(Type tipo) => Piani.GetOrAdd(tipo, t =>
    {
        var proprieta = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Marcate E scrivibili: le calcolate (TotalCost) non hanno setter e si annullano da sole.
        PropertyInfo[] sensibili = proprieta
            .Where(p => p.GetCustomAttribute<DatoSensibileAttribute>() != null && p.CanWrite)
            .ToArray();

        PropertyInfo[] daAttraversare = proprieta
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !EPrimitivo(p.PropertyType))
            .Where(p => p.GetCustomAttribute<DatoSensibileAttribute>() == null)
            .ToArray();

        // Un tipo che non può contenere sensibili da nessuna parte si salta in blocco: la
        // risposta tipica (liste di DTO "normali") costa un lookup di dizionario e basta.
        bool inProfondita = sensibili.Length > 0 || daAttraversare.Any(p => PuoContenereSensibili(p.PropertyType, 0));
        return new PianoTipo(sensibili, daAttraversare, inProfondita);
    });

    private static bool PuoContenereSensibili(Type tipo, int depth)
    {
        if (depth > 6 || EPrimitivo(tipo)) return false;

        if (tipo.IsArray)
            return PuoContenereSensibili(tipo.GetElementType()!, depth + 1);
        if (tipo.IsGenericType)
        {
            // Per i contenitori si guardano gli argomenti generici (List<T>, Dictionary<K,V>, ApiResponse<T>…).
            if (tipo.GetGenericArguments().Any(a => PuoContenereSensibili(a, depth + 1)))
                return true;
        }
        if (typeof(IEnumerable).IsAssignableFrom(tipo) && tipo != typeof(string))
            return true; // IEnumerable non generico: non si può escludere, si attraverserà a runtime

        return tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.GetCustomAttribute<DatoSensibileAttribute>() != null
                      || (p.GetIndexParameters().Length == 0 && p.PropertyType != tipo && PuoContenereSensibili(p.PropertyType, depth + 1)));
    }

    private static bool EPrimitivo(Type t)
    {
        Type nudo = Nullable.GetUnderlyingType(t) ?? t;
        return nudo.IsPrimitive || nudo.IsEnum || nudo == typeof(string) || nudo == typeof(decimal)
               || nudo == typeof(DateTime) || nudo == typeof(DateTimeOffset) || nudo == typeof(TimeSpan)
               || nudo == typeof(Guid);
    }
}
