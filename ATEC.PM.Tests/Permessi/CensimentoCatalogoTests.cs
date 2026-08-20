using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ATEC.PM.Server.Authorization;
using ATEC.PM.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// Censimento del catalogo unico dei permessi (PIANO-PERMESSI-REBUILD.md §12.3).
///
/// <para>Tre garanzie, tutte automatiche:</para>
/// <list type="number">
/// <item>il catalogo (<c>catalogo-permessi.json</c>) è ben formato;</item>
/// <item>ogni chiave usata nel codice del server esiste a catalogo — se manca, il test
///   fallisce STAMPANDO lo stub JSON pronto da incollare (la mappa chiave → casa si
///   compila da sola);</item>
/// <item>ogni chiave di catalogo è usata da almeno un punto del server, oppure è
///   dichiarata <c>soloClient</c> con un motivo, oppure è <c>ritirata</c> — le chiavi
///   che non proteggono nulla non possono più nascondersi.</item>
/// </list>
///
/// <para>In più genera l'artefatto <c>PERMESSI-MAPPA-ENDPOINT.gen.md</c> alla radice del
/// repo: la mappa chiave → endpoint del §4 è un output, non un documento da mantenere.</para>
/// </summary>
public class CensimentoCatalogoTests
{
    private static readonly Regex ChiaveNelCodice =
        new(@"""(?<k>(nav|project|action|data|sal|resources)\.[a-z0-9_]+(\.[a-z0-9_]+)*)""",
            RegexOptions.Compiled);

    // ── 1. Il catalogo è ben formato ─────────────────────────────────────────────

    [Fact]
    public void Catalogo_valido_e_completo()
    {
        IReadOnlyList<string> errori = PermessiCatalogo.Valida();
        Assert.True(errori.Count == 0,
            "catalogo-permessi.json non valido:\n - " + string.Join("\n - ", errori));

        // Paracadute: se l'embedded resource sparisse o il file si svuotasse, meglio un
        // numero esplicito che un censimento silenziosamente vuoto.
        int chiavi = PermessiCatalogo.VociPrimarie().Count();
        Assert.True(chiavi >= 70, $"catalogo sospettosamente piccolo: {chiavi} chiavi primarie");
    }

    // ── 2. Ogni chiave usata nel codice sta a catalogo ───────────────────────────

    [Fact]
    public void Ogni_chiave_usata_nel_server_esiste_a_catalogo()
    {
        string? radice = TrovaRadiceRepo();
        if (radice == null) return; // fuori dal repo (runner senza sorgenti): niente da censire

        Dictionary<string, List<string>> usate = ChiaviUsateNeiSorgenti(radice);
        var aCatalogo = PermessiCatalogo.Piatte()
            .Select(c => c.Voce.Chiave)
            .Where(k => k != null)
            .Cast<string>()
            .ToHashSet();

        var mancanti = usate.Keys.Where(k => !aCatalogo.Contains(k)).OrderBy(k => k).ToList();
        if (mancanti.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"{mancanti.Count} chiavi usate nel server ma ASSENTI dal catalogo.");
        sb.AppendLine("Stub pronti da incollare in catalogo-permessi.json (dentro la voce che li ospita):");
        foreach (string chiave in mancanti)
        {
            sb.AppendLine();
            sb.AppendLine($"  {{ \"kind\": \"{KindSuggerito(chiave)}\", \"chiave\": \"{chiave}\", \"label\": \"?\" }}");
            sb.AppendLine($"  // vista in: {string.Join(", ", usate[chiave].Take(4))}");
        }
        Assert.Fail(sb.ToString());
    }

    // ── 3. Ogni chiave di catalogo è usata, soloClient (con motivo) o ritirata ───

    [Fact]
    public void Ogni_chiave_di_catalogo_e_usata_o_dichiarata()
    {
        string? radice = TrovaRadiceRepo();
        if (radice == null) return;

        var usate = ChiaviUsateNeiSorgenti(radice).Keys.ToHashSet();

        var scoperte = PermessiCatalogo.VociPrimarie()
            .Where(v => v.Kind is "voce" or "sezione-commessa" or "azione" or "ambito")
            .Where(v => v.Chiave != null && !v.Ritirata && !v.SoloClient)
            .Where(v => !usate.Contains(v.Chiave!))
            .Select(v => v.Chiave!)
            .OrderBy(k => k)
            .ToList();

        Assert.True(scoperte.Count == 0,
            "Chiavi di catalogo che nessun punto del server usa: o si marca soloClient con un " +
            "motivo (§12.8.6), o si marca ritirata, o si mette il gate che manca.\n - " +
            string.Join("\n - ", scoperte));
    }

    // ── 4. La mappa chiave → endpoint si genera, non si scrive ───────────────────

    [Fact]
    public void Mappa_chiave_endpoint_generata()
    {
        List<EndpointCensito> endpoints = EndpointsCensiti();
        Assert.True(endpoints.Count >= 40,
            $"censiti solo {endpoints.Count} endpoint con [RequireFeature]: la riflessione ha smesso di vedere i controller?");

        string? radice = TrovaRadiceRepo();
        if (radice == null) return;

        Dictionary<string, List<string>> usateInline = ChiaviUsateNeiSorgenti(radice);
        var conAttributo = endpoints.SelectMany(e => e.Chiavi).ToHashSet();
        var voci = PermessiCatalogo.Piatte().Select(c => c.Voce).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Mappa chiave → endpoint (GENERATA)");
        sb.AppendLine();
        sb.AppendLine("> Generata da `CensimentoCatalogoTests.Mappa_chiave_endpoint_generata` a ogni run dei test.");
        sb.AppendLine("> NON MODIFICARE A MANO — PIANO-PERMESSI-REBUILD.md §12.3.");
        sb.AppendLine($"> Fotografia del {DateTime.Now:dd/MM/yyyy HH:mm}.");

        sb.AppendLine();
        sb.AppendLine("## Chiavi con endpoint");
        foreach (var gruppo in endpoints
                     .SelectMany(e => e.Chiavi.Select(k => (Chiave: k, Endpoint: e)))
                     .GroupBy(x => x.Chiave)
                     .OrderBy(g => g.Key))
        {
            string label = voci.FirstOrDefault(v => v.Chiave == gruppo.Key && !v.ChiaveCondivisa)?.Label ?? "(fuori catalogo!)";
            sb.AppendLine();
            sb.AppendLine($"### `{gruppo.Key}` — {label}");
            foreach (var x in gruppo.OrderBy(x => x.Endpoint.Rotta))
                sb.AppendLine($"- `{x.Endpoint.Rotta}` ({x.Endpoint.Metodo}){(x.Endpoint.AccessOnly ? " · AccessOnly" : "")}");
        }

        sb.AppendLine();
        sb.AppendLine("## Chiavi usate solo inline (CanAccessUser/CanWriteUser, senza attributo)");
        foreach (string k in usateInline.Keys.Where(k => !conAttributo.Contains(k)).OrderBy(k => k))
            sb.AppendLine($"- `{k}` — {string.Join(", ", usateInline[k].Take(3))}");

        sb.AppendLine();
        sb.AppendLine("## Chiavi solo client (motivo dichiarato a catalogo)");
        foreach (VoceCatalogo v in voci.Where(v => v.SoloClient).OrderBy(v => v.Chiave))
            sb.AppendLine($"- `{v.Chiave}` — {v.Motivo}");

        sb.AppendLine();
        sb.AppendLine("## Chiavi ritirate");
        foreach (VoceCatalogo v in voci.Where(v => v.Ritirata).OrderBy(v => v.Chiave))
            sb.AppendLine($"- `{v.Chiave}` — {v.Nota}");

        sb.AppendLine();
        sb.AppendLine("## Chiavi condivise menu/albero (da sdoppiare al passo 3, §12.4)");
        foreach (VoceCatalogo v in voci.Where(v => v.ChiaveCondivisa).OrderBy(v => v.Chiave))
            sb.AppendLine($"- `{v.Chiave}` — {v.Label}");

        sb.AppendLine();
        sb.AppendLine("## Controller con endpoint senza nessuna chiave (solo [Authorize])");
        foreach (var gruppo in EndpointsSenzaChiave().GroupBy(e => e.Metodo.Split('.')[0]).OrderBy(g => g.Key))
            sb.AppendLine($"- {gruppo.Key}: {gruppo.Count()} endpoint");

        File.WriteAllText(Path.Combine(radice, "PERMESSI-MAPPA-ENDPOINT.gen.md"), sb.ToString(), Encoding.UTF8);
    }

    // ── Attrezzi ─────────────────────────────────────────────────────────────────

    private sealed record EndpointCensito(string Rotta, string Metodo, string[] Chiavi, bool AccessOnly);

    private static List<EndpointCensito> EndpointsCensiti() =>
        EndpointsDelServer().Where(e => e.Chiavi.Length > 0).ToList();

    private static List<EndpointCensito> EndpointsSenzaChiave() =>
        EndpointsDelServer().Where(e => e.Chiavi.Length == 0).ToList();

    private static List<EndpointCensito> EndpointsDelServer()
    {
        var risultato = new List<EndpointCensito>();
        Assembly server = typeof(RequireFeatureAttribute).Assembly;

        foreach (Type controller in server.GetTypes()
                     .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            string nome = controller.Name.EndsWith("Controller")
                ? controller.Name[..^"Controller".Length] : controller.Name;
            string rottaBase = controller.GetCustomAttribute<RouteAttribute>()?.Template?
                .Replace("[controller]", nome, StringComparison.OrdinalIgnoreCase) ?? "";

            string[] chiaviClasse = ChiaviDi(controller.GetCustomAttributes<RequireFeatureAttribute>());

            foreach (MethodInfo azione in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var http = azione.GetCustomAttributes().OfType<HttpMethodAttribute>().ToList();
                if (http.Count == 0) continue;

                var attributi = azione.GetCustomAttributes<RequireFeatureAttribute>().ToList();
                string[] chiavi = ChiaviDi(attributi).Union(chiaviClasse).ToArray();
                bool accessOnly = attributi.Any(a => a.Arguments is [_, true]);

                foreach (HttpMethodAttribute h in http)
                {
                    string rotta = string.Join('/', new[] { rottaBase, h.Template }
                        .Where(p => !string.IsNullOrEmpty(p)));
                    string verbo = h.HttpMethods.FirstOrDefault() ?? "?";
                    risultato.Add(new EndpointCensito(
                        $"{verbo} /{rotta}", $"{controller.Name}.{azione.Name}", chiavi, accessOnly));
                }
            }
        }
        return risultato;
    }

    private static string[] ChiaviDi(IEnumerable<RequireFeatureAttribute> attributi) =>
        attributi.SelectMany(a => a.Arguments is [string[] chiavi, ..] ? chiavi : Array.Empty<string>())
            .Distinct().ToArray();

    /// <summary>
    /// Le chiavi che compaiono come stringhe nei sorgenti del server (Migrations escluse:
    /// sono storia, non enforcement) con i file in cui compaiono. Le righe di commento sono
    /// scartate per non tenere in vita una chiave solo perché qualcuno la cita.
    /// </summary>
    private static Dictionary<string, List<string>> ChiaviUsateNeiSorgenti(string radiceRepo)
    {
        var usate = new Dictionary<string, List<string>>();
        string dirServer = Path.Combine(radiceRepo, "ATEC.PM.Server");

        foreach (string file in Directory.EnumerateFiles(dirServer, "*.cs", SearchOption.AllDirectories))
        {
            string relativo = Path.GetRelativePath(radiceRepo, file).Replace('\\', '/');
            if (relativo.Contains("/Migrations/") || relativo.Contains("/bin/") || relativo.Contains("/obj/"))
                continue;

            foreach (string rigaGrezza in File.ReadLines(file))
            {
                string riga = rigaGrezza.TrimStart();
                if (riga.StartsWith("//") || riga.StartsWith("*") || riga.StartsWith("///")) continue;

                foreach (Match m in ChiaveNelCodice.Matches(riga))
                {
                    string chiave = m.Groups["k"].Value;
                    if (!usate.TryGetValue(chiave, out List<string>? dove))
                        usate[chiave] = dove = new List<string>();
                    if (!dove.Contains(relativo)) dove.Add(relativo);
                }
            }
        }
        return usate;
    }

    private static string KindSuggerito(string chiave) => chiave.Split('.')[0] switch
    {
        "nav" => "voce",
        "project" => "sezione-commessa",
        _ => "azione",
    };

    private static string? TrovaRadiceRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ATEC.PM.sln")))
            dir = dir.Parent;
        return dir?.FullName;
    }
}
