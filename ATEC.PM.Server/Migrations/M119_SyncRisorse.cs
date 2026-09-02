using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Sincronizzazione Risorse ATEC PM ⇄ VPS (PIANO-SYNC-RISORSE.md §4.2, Fase 0): la mappa
/// degli id fra i due programmi e il registro dei giri del motore.
///
/// <para><c>res_sync_map</c>: per ogni oggetto (<c>kind</c> = EMPLOYEE · DEPARTMENT · PROJECT ·
/// ASSIGNMENT) l'id in PM e l'id sul VPS, più l'impronta dei campi all'ultimo allineamento.
/// Due UNIQUE (per lato) perché un id non può stare in due coppie: la mappa è una biiezione.</para>
///
/// <para><c>res_sync_log</c>: una riga per giro, con i contatori di cosa è stato fatto da che
/// parte. Le impostazioni (chiavi <c>sync.*</c>) vanno in <c>res_settings</c>, che esiste già.</para>
///
/// <para>Nessuna parola riservata MySQL fra i nomi di colonna, apposta.</para>
/// </summary>
public sealed class M119_SyncRisorse : IMigrazione
{
    public int Versione => 119;

    public string Descrizione =>
        "res_sync_map + res_sync_log: mappa id e registro giri della sincronizzazione Risorse (VPS)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS res_sync_map (
            id INT AUTO_INCREMENT PRIMARY KEY,
            kind VARCHAR(20) NOT NULL,
            local_id INT NOT NULL,
            remote_id INT NOT NULL,
            synced_hash VARCHAR(64) NULL,
            synced_at DATETIME NULL,
            UNIQUE KEY uq_sync_map_local (kind, local_id),
            UNIQUE KEY uq_sync_map_remote (kind, remote_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS res_sync_log (
            id INT AUTO_INCREMENT PRIMARY KEY,
            run_utc DATETIME NOT NULL,
            innesco VARCHAR(20) NOT NULL,
            esito VARCHAR(20) NOT NULL,
            durata_ms INT NOT NULL DEFAULT 0,
            righe_pm INT NOT NULL DEFAULT 0,
            righe_vps INT NOT NULL DEFAULT 0,
            create_pm INT NOT NULL DEFAULT 0,
            create_vps INT NOT NULL DEFAULT 0,
            aggiornate_pm INT NOT NULL DEFAULT 0,
            aggiornate_vps INT NOT NULL DEFAULT 0,
            cancellate_pm INT NOT NULL DEFAULT 0,
            cancellate_vps INT NOT NULL DEFAULT 0,
            conflitti INT NOT NULL DEFAULT 0,
            saltate INT NOT NULL DEFAULT 0,
            dettaglio TEXT NULL,
            KEY idx_sync_log_run (run_utc)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        log.LogInformation("[Migration v119] res_sync_map e res_sync_log verificate/create.");
    }
}
