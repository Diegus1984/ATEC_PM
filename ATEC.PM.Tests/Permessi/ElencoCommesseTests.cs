using System.Reflection;
using ATEC.PM.Server.Authorization;
using ATEC.PM.Server.Controllers;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// Il taglio fra <b>l'elenco commesse</b> (la pagina, coi soldi dentro) e <b>la tendina</b>
/// (il nome, per sceglierla da un'altra pagina) — PIANO-PERMESSI-REBUILD.md §6.
///
/// <para>Prima erano lo stesso endpoint: <c>GET /api/projects</c> serviva sia la pagina Commesse
/// sia le combo di SAL, verbali, chat, milestone, lavorazioni e dashboard. Siccome quelle combo
/// devono funzionare per tutti, l'endpoint era aperto a chiunque fosse autenticato — e con lui
/// il <c>revenue</c> di ogni commessa, comprese le tre persone a cui la voce «Commesse» è
/// negata apposta. Nessuna pagina lo mostrava a video, ma stava nel JSON: «non lo mostriamo»
/// non è un permesso.</para>
///
/// <para>Questi test tengono fermo il taglio. Il modo naturale di romperlo è gentile e
/// plausibile — «alla tendina serve anche la data di fine», «aggiungo revenue che tanto qui
/// serve» — ed è esattamente ciò che rimetterebbe gli importi in mano a tutti.</para>
/// </summary>
public class ElencoCommesseTests
{
    /// <summary>I soli campi che una tendina può avere. Allungare questo elenco è una decisione.</summary>
    private static readonly string[] CampiAmmessiTendina =
        { "Id", "Code", "Title", "CustomerName", "PmName", "Status" };

    [Fact]
    public void La_tendina_commesse_non_porta_soldi()
    {
        var campi = typeof(ProjectLookupItem)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        var intrusi = campi.Except(CampiAmmessiTendina, StringComparer.Ordinal).ToList();
        Assert.True(intrusi.Count == 0,
            "ProjectLookupItem ha campi che una tendina non deve portare: " + string.Join(", ", intrusi) +
            ".\nQuesto tipo lo serve GET /api/projects/lookup, che è APERTA a tutti gli autenticati: " +
            "ogni campo aggiunto qui finisce a chiunque, anche a chi la voce «Commesse» ce l'ha negata. " +
            "Se il campo serve davvero alla pagina che l'ha chiesto, o si usa l'endpoint della pagina " +
            "(GET /api/projects, dietro nav.commesse) o si decide che quel dato è pubblico.");
    }

    [Fact]
    public void L_elenco_commesse_con_gli_importi_sta_dietro_nav_commesse()
    {
        // GetDashboard è in questo elenco per una ragione imparata a caro prezzo: il primo giro
        // chiuse l'elenco e lasciò aperta la Dashboard, che dei soldi ne porta anche di più
        // (costo consuntivo, materiali, trasferta, totale). Chiudere UNA strada non basta.
        // Dal 04/09/2026 il cruscotto sta in ProjectDashboardController (stessa rotta): il
        // vincolo è lo stesso, cambia solo la classe in cui cercarlo.
        var strade = new (Type Controller, string Metodo)[]
        {
            (typeof(ProjectsController), "GetAll"), (typeof(ProjectsController), "GetTree"),
            (typeof(ProjectsController), "GetById"), (typeof(ProjectsController), "NextCode"),
            (typeof(ProjectDashboardController), "GetDashboard"),
        };
        foreach ((Type controller, string metodo) in strade)
        {
            string[] chiavi = Gate.ChiaviDi(controller, metodo);
            Assert.True(chiavi.Contains("nav.commesse"),
                $"{controller.Name}.{metodo} ha perso [RequireFeature(\"nav.commesse\")]: " +
                "i numeri della commessa (revenue, budget, costo consuntivo) tornerebbero leggibili a chiunque.");
        }
    }

    [Fact]
    public void La_tendina_resta_aperta_di_proposito()
    {
        // Non è una dimenticanza: la commessa si sceglie da mezza applicazione, e chiudere
        // questa spegnerebbe SAL, verbali, chat, milestone e lavorazioni a chi la commessa la
        // deve solo nominare. È il prezzo del taglio — e va pagato in un posto solo, qui.
        Assert.Empty(Gate.ChiaviDi(typeof(ProjectsController), "GetLookup"));
    }

    [Fact]
    public void Il_planner_risorse_si_legge_solo_con_la_voce_di_menu()
    {
        foreach (string metodo in new[]
                 { "GetAssignments", "GetServices", "GetOthers", "GetResourceLookups", "GetProjectLookups" })
        {
            string[] chiavi = Gate.ChiaviDi(typeof(ResourcesController), metodo);
            Assert.True(chiavi.Contains("nav.risorse"),
                $"ResourcesController.{metodo} ha perso [RequireFeature(\"nav.risorse\")]: " +
                "il planner tornerebbe leggibile anche a chi la voce «Risorse» ce l'ha negata. " +
                "Basta il livello READ, quindi chi ce l'ha in sola lettura non perde niente.");
        }
    }
}
