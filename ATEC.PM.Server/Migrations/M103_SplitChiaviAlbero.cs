using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Rebuild permessi, passo 3 (PIANO-PERMESSI-REBUILD.md §12.4) — lo split delle 5 chiavi
/// condivise fra menu PM e albero commessa, come FOTOGRAFIA: ogni <c>project.X</c> nasce con
/// lo stato effettivo di <c>nav.X</c> — stessa riga per ogni persona (accesso E origine,
/// dinieghi <c>NO</c> compresi), stessi pacchetti di classe, stesse liste del motore vecchio,
/// stesso <c>min_level</c>. <b>Il giorno del cutover nessuno vede niente di diverso, per
/// costruzione.</b> Il jolly <c>*</c> copre le chiavi nuove da solo.
///
/// <para>Da qui in poi menu e albero si concedono separatamente: il caso #77 (l'albero MoM a
/// un tecnico senza il menu PM) diventa un gesto admin sulla scheda, non una migrazione. Gli
/// endpoint restano in OR (<c>project.X</c>, <c>nav.X</c>): servono sia l'albero sia le pagine
/// globali, e con una chiave sola una delle due si svuoterebbe senza errore a video — la
/// lezione della Sintesi DDP (§12.8.4).</para>
///
/// <para>Tutto <c>INSERT IGNORE</c>: rieseguibile senza danni, e le righe già esistenti
/// (es. una concessione data a mano fra un tentativo e l'altro) non si sovrascrivono.</para>
/// </summary>
public sealed class M103_SplitChiaviAlbero : IMigrazione
{
    public int Versione => 103;

    public string Descrizione =>
        "Split fotografico menu/albero: project.{mom,checklist,milestones,sal,work_requests} da nav.* (rebuild §12, passo 3)";

    private static readonly (string Nuova, string Vecchia)[] Coppie =
    {
        ("project.mom", "nav.mom"),
        ("project.checklist", "nav.checklist"),
        ("project.milestones", "nav.milestones"),
        ("project.sal", "nav.sal"),
        ("project.work_requests", "nav.work_requests"),
    };

    public void Applica(MySqlConnection c, ILogger log)
    {
        foreach ((string nuova, string vecchia) in Coppie)
        {
            // Registrazione col min_level FOTOGRAFATO dal motore vecchio: in un rollback la
            // sezione deve rispondere come ieri, non «solo Admin» (che è il default giusto
            // per le chiavi inventate da zero, non per quelle nate da uno split).
            c.Execute(@"
                INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                SELECT @Nuova, display_name, 'project', min_level, behavior
                FROM auth_features WHERE feature_key = @Vecchia",
                new { Nuova = nuova, Vecchia = vecchia });

            int persone = c.Execute(@"
                INSERT IGNORE INTO employee_feature_access (employee_id, feature_key, access, origin)
                SELECT employee_id, @Nuova, access, origin
                FROM employee_feature_access WHERE feature_key = @Vecchia",
                new { Nuova = nuova, Vecchia = vecchia });

            int classi = c.Execute(@"
                INSERT IGNORE INTO auth_class_features (class_name, feature_key, access)
                SELECT class_name, @Nuova, access
                FROM auth_class_features WHERE feature_key = @Vecchia",
                new { Nuova = nuova, Vecchia = vecchia });

            int ruoli = c.Execute(@"
                INSERT IGNORE INTO auth_role_features (role_name, feature_key, access)
                SELECT role_name, @Nuova, access
                FROM auth_role_features WHERE feature_key = @Vecchia",
                new { Nuova = nuova, Vecchia = vecchia });

            log.LogInformation(
                "[Migration v103] {Nuova} fotografata da {Vecchia}: {Persone} righe persona, {Classi} di classe, {Ruoli} del motore vecchio. Il jolly copre da solo.",
                nuova, vecchia, persone, classi, ruoli);
        }
    }
}
