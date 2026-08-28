using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #131 — pagina «SAL / SAP Acconti»: la riconciliazione fra gli acconti
/// che risultano dal gestionale e il saldo del conto SAP <c>1501600001</c>.
///
/// <para>Due delle tre tabelle della pagina si calcolano (quella dal SAL e la differenza);
/// la terza — quella del conto SAP — <b>si compila a mano</b>, perché quel numero lo legge
/// una persona dentro SAP e nel gestionale non esiste nessun dato da cui ricavarlo. Serve
/// quindi un posto dove scriverlo, ed è questa tabella.</para>
///
/// <para><b>Una riga sola, id = 1.</b> Non è un elenco: è la fotografia corrente del conto,
/// condivisa da tutti. La chiave primaria fissa è quello che impedisce che due salvataggi
/// contemporanei creino due «fotografie» diverse; a difendere il contenuto dalle scritture
/// incrociate c'è invece <c>row_version</c>, come su tutto il resto del gestionale.</para>
///
/// <para>🪤 I due valori nascono <b>NULL</b>, non 0: «non ancora compilato» e «il conto è a
/// zero» sono due cose diverse, e la tabella della differenza deve poter dire «—» invece di
/// mostrare uno scarto inventato pari all'intero importo del SAL.</para>
/// </summary>
public sealed class M115_SalSapAcconti : IMigrazione
{
    public int Versione => 115;

    public string Descrizione =>
        "#131: tabella sal_sap_acconti (totali del conto SAP acconti, compilati a mano)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS sal_sap_acconti (
            id INT PRIMARY KEY,
            tot_fatture INT NULL,
            importo_acconti DECIMAL(14,2) NULL,
            row_version INT NOT NULL DEFAULT 0,
            updated_at DATETIME NULL,
            updated_by INT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;", commandTimeout: 600);

        // La riga unica esiste sempre: così la PUT è un UPDATE e basta, e non deve
        // decidere ogni volta se creare o aggiornare (due client che ci provano insieme
        // sarebbero due INSERT in corsa).
        int creata = c.Execute("INSERT IGNORE INTO sal_sap_acconti (id) VALUES (1)");

        log.LogInformation(
            "[v115] sal_sap_acconti pronta ({Stato}).",
            creata > 0 ? "riga unica creata" : "riga unica già presente");
    }
}
