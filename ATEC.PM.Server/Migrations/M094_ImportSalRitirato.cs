using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #85 — l'import SAL da backup del vecchio gestionale non esiste più.
///
/// <para>Serviva a travasare i piani di fatturazione dal prototipo «Gestione Commesse»
/// (dump di localStorage): il travaso è finito, e la funzione restava come un pulsante che
/// scriveva a raffica su tutte le commesse. Tolti pagina, endpoint e DTO, qui si ritira la
/// chiave che li proteggeva: lasciarla nel catalogo vorrebbe dire tenere nella scheda dei
/// permessi una manopola che non accende più niente.</para>
///
/// <para><b>Facoltativa</b>: è pulizia di righe di permesso, non schema. Se salta, resta una
/// voce morta nell'elenco delle funzioni — fastidiosa, non dannosa: il codice che la leggeva
/// non c'è più.</para>
/// </summary>
public sealed class M094_ImportSalRitirato : IMigrazione
{
    public int Versione => 94;

    public bool Facoltativa => true;

    public string Descrizione => "#85: chiave action.import_sal ritirata (funzione import SAL eliminata)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int persone = c.Execute(
            "DELETE FROM employee_feature_access WHERE feature_key = 'action.import_sal'");
        int classi = c.Execute(
            "DELETE FROM auth_class_features WHERE feature_key = 'action.import_sal'");
        int ruoli = c.Execute(
            "DELETE FROM auth_role_features WHERE feature_key = 'action.import_sal'");
        int catalogo = c.Execute(
            "DELETE FROM auth_features WHERE feature_key = 'action.import_sal'");

        log.LogInformation(
            "[v94] Import SAL ritirato: {Persone} concessioni personali, {Classi} di classe, {Ruoli} di ruolo, {Catalogo} voce di catalogo.",
            persone, classi, ruoli, catalogo);
    }
}
