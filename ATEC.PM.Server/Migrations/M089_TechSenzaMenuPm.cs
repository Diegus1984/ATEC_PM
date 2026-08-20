using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #77 — opzione B: il blocco menu «PM» (MoM / Milestone) è solo dei PM.
///
/// <para>La Fase D (v87) aveva dato MoM e Milestone in lettura a Tecnico e Responsabile.
/// Paolo chiede invece: quella sezione del menu si chiama PM perché ci devono entrare
/// solo i PM (e l'Admin). TECH e RESP_REPARTO perdono <c>nav.mom</c> e
/// <c>nav.milestones</c> dal pacchetto e dalle righe CLASSE; le eccezioni MANO restano.</para>
/// </summary>
public sealed class M089_TechSenzaMenuPm : IMigrazione
{
    public int Versione => 89;

    public string Descrizione =>
        "TECH/RESP senza nav.mom/nav.milestones (#77: blocco menu PM solo per i PM)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int toltePkg = c.Execute(@"
            DELETE FROM auth_class_features
            WHERE class_name IN ('TECH', 'RESP_REPARTO')
              AND feature_key IN ('nav.mom', 'nav.milestones')");

        int toltePersone = c.Execute(@"
            DELETE a FROM employee_feature_access a
            JOIN employees e ON e.id = a.employee_id
            WHERE e.user_role IN ('TECH', 'RESP_REPARTO')
              AND a.origin = 'CLASSE'
              AND a.feature_key IN ('nav.mom', 'nav.milestones')");

        log.LogInformation(
            "[Migration v89] #77: tolte {Pkg} chiavi MoM/Milestone dai pacchetti TECH/RESP; " +
            "rimosse {Persone} righe CLASSE. PM/ADMIN invariati. Logout/login per il menu.",
            toltePkg, toltePersone);
    }
}
