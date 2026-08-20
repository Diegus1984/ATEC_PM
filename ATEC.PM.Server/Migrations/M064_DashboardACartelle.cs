using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// v64: dashboard a cartelle (blocco 7 del piano V32).
//  - projects.in_dashboard: la spunta «In dashboard» del prototipo. Default 1, quindi
//    all'aggiornamento tutte le commesse aperte compaiono già in dashboard e la pagina
//    si comporta come se il flag non esistesse: chi vuole snellirla toglie la spunta.
//    È un flag CONDIVISO (sta sulla commessa, non sull'utente): chi la toglie la toglie
//    a tutti, come nel prototipo. Nessuna feature key nuova: la pagina è la Dashboard,
//    che ha già `nav.dashboard`.
// Il limite di cartelle (DASH_MAX = 10 cablato nel prototipo) NON è una colonna: sta in
// res_settings come la soglia del Bilancio, così l'ADMIN lo cambia senza migrazioni.
public sealed class M064_DashboardACartelle : IMigrazione
{
    public int Versione => 64;

    public string Descrizione => "Dashboard a cartelle: projects.in_dashboard";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool added = AddColumnIfMissing(c, "projects", "in_dashboard",
            "TINYINT(1) NOT NULL DEFAULT 1 AFTER status");

        log.LogInformation(
            "[Migration v64] Dashboard a cartelle: colonna in_dashboard su projects ({Added})",
            added ? "nuova" : "già presente");
    }
}
