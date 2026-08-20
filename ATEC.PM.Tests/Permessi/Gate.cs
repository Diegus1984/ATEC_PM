using System.Reflection;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// Attrezzo condiviso dai test che sorvegliano i cancelli: le chiavi che protteggono un'azione.
///
/// <para>🪤 Legge gli attributi <b>del metodo E della classe</b>. La prima versione guardava
/// solo il metodo, e su un controller protetto sulla classe (è il caso di
/// <c>UsersController</c>) avrebbe giurato «nessuna chiave» su un endpoint blindato — cioè
/// avrebbe mentito nel verso peggiore: un test tranquillizzante su una porta che invece è
/// chiusa, e domani il contrario.</para>
/// </summary>
internal static class Gate
{
    /// <summary>Le chiavi dichiarate per un'azione, metodo + classe. Vuoto = nessun cancello.</summary>
    public static string[] ChiaviDi(Type controller, string metodo)
    {
        MethodInfo azione = controller.GetMethod(metodo, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"{controller.Name}.{metodo} non esiste più: se è stato rinominato, va aggiornato anche il test.");

        // Le chiavi non sono esposte da una proprietà: stanno in Arguments[0], che è come le
        // legge anche il censimento (§12.3). Se un domani l'attributo cambia forma, cambiano
        // insieme — meglio di due modi diversi di leggere la stessa cosa.
        return azione.GetCustomAttributes<RequireFeatureAttribute>(inherit: true)
            .Concat(controller.GetCustomAttributes<RequireFeatureAttribute>(inherit: true))
            .SelectMany(a => (string[])a.Arguments![0]!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>I nomi delle proprietà pubbliche di un DTO.</summary>
    public static string[] CampiDi(Type dto) =>
        dto.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();

    /// <summary>
    /// Verifica che un DTO da tendina non sia cresciuto oltre i campi ammessi. Il modo naturale
    /// di riaprire uno di questi buchi è gentile: «aggiungo un campo che tanto qui serve».
    /// </summary>
    public static void SoloQuestiCampi(Type dto, string[] ammessi, string endpointAperto)
    {
        var intrusi = CampiDi(dto).Except(ammessi, StringComparer.Ordinal).ToList();
        Assert.True(intrusi.Count == 0,
            $"{dto.Name} ha campi che una tendina non deve portare: {string.Join(", ", intrusi)}.\n" +
            $"Questo tipo lo serve {endpointAperto}, che è APERTA a tutti gli autenticati: ogni campo " +
            "aggiunto qui finisce a chiunque. Se serve davvero a chi l'ha chiesto, si usa l'endpoint " +
            "della sua pagina (che ha la chiave), oppure si decide che quel dato è pubblico.");
    }
}
