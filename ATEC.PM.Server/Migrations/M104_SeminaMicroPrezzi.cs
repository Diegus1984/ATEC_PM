using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Rebuild permessi, passo 4 (PIANO-PERMESSI-REBUILD.md §12.8, falla 1) — la SEMINA FOTOGRAFICA
/// dei micro «vede prezzi». Sotto «default nega», accendere il filtro dei dati sensibili senza
/// seminare spegnerebbe i prezzi a tutti tranne il jolly — la lezione della v85.
///
/// <para>Regola: chi oggi vede la voce, vede i suoi numeri. Quindi <c>&lt;voce&gt;.prices</c>
/// nasce fotografando le righe della voce stessa: stesse persone, stesso accesso, stessa
/// origine (dinieghi <c>NO</c> compresi), stessi pacchetti di classe e liste del motore
/// vecchio, stesso <c>min_level</c>. <b>Il giorno del deploy il filtro non cambia niente a
/// nessuno, per costruzione.</b> Poi togliere i prezzi a una persona è una riga di diniego
/// sulla sua scheda.</para>
///
/// <para>La lista è FISSA (fotografia di questo momento, non del catalogo futuro): ogni nuova
/// voce sensibile porta la sua migrazione di semina nello stesso PR, come da regola
/// <c>.cursor/rules/permessi-catalogo-sensitive.mdc</c>.</para>
/// </summary>
public sealed class M104_SeminaMicroPrezzi : IMigrazione
{
    public int Versione => 104;

    public string Descrizione =>
        "Semina fotografica dei micro prezzi (<voce>.prices) per le 6 voci DDP/officina/acquisti (rebuild §12, passo 4)";

    private static readonly string[] Voci =
    {
        "project.ddp_commerciale",
        "project.ddp_officina",
        "nav.gestore_ddp",
        "nav.acquisti_inbox",
        "nav.work_requests",
        "nav.officina_inbox",
    };

    public void Applica(MySqlConnection c, ILogger log)
    {
        foreach (string voce in Voci)
        {
            string micro = $"{voce}.prices";

            // Registrazione col min_level della voce (rollback al motore vecchio fedele);
            // EnsureCatalogo poi allinea l'etichetta al catalogo, qui basta che la riga esista.
            c.Execute(@"
                INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                SELECT @Micro, CONCAT(display_name, ' — vede prezzi'), 'data', min_level, behavior
                FROM auth_features WHERE feature_key = @Voce",
                new { Micro = micro, Voce = voce });

            int persone = c.Execute(@"
                INSERT IGNORE INTO employee_feature_access (employee_id, feature_key, access, origin)
                SELECT employee_id, @Micro, access, origin
                FROM employee_feature_access WHERE feature_key = @Voce",
                new { Micro = micro, Voce = voce });

            int classi = c.Execute(@"
                INSERT IGNORE INTO auth_class_features (class_name, feature_key, access)
                SELECT class_name, @Micro, access
                FROM auth_class_features WHERE feature_key = @Voce",
                new { Micro = micro, Voce = voce });

            int ruoli = c.Execute(@"
                INSERT IGNORE INTO auth_role_features (role_name, feature_key, access)
                SELECT role_name, @Micro, access
                FROM auth_role_features WHERE feature_key = @Voce",
                new { Micro = micro, Voce = voce });

            log.LogInformation(
                "[Migration v104] {Micro} seminato da {Voce}: {Persone} righe persona, {Classi} di classe, {Ruoli} del motore vecchio. Il jolly copre da solo.",
                micro, voce, persone, classi, ruoli);
        }
    }
}
