using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M107 — Fondamenta del modulo HR presenze (piano: <c>PIANO-HR-PRESENZE.md</c>, Fase 1).
///
/// <para>Due tabelle e una colonna, con una regola sola che le tiene insieme:
/// <b>il grezzo non si corregge mai</b>.</para>
///
/// <list type="number">
/// <item><c>hr_timbrature</c> — le timbrature come arrivano dal rilevatore, <b>append-only</b>.
/// Non si aggiornano e non si cancellano: una timbratura sbagliata resta, e accanto le si mette
/// una rettifica. La chiave naturale <c>(origine, id_esterno)</c> rende l'import <b>idempotente</b>:
/// reimportare lo stesso periodo non duplica niente, che è indispensabile perché i rilevatori
/// rimandano i dati dopo un'interruzione di rete.</item>
/// <item><c>hr_giornate</c> — il cartellino calcolato, <b>rigenerabile</b> dal grezzo in
/// qualunque momento. Non è una seconda verità: è un risultato che si può buttare e rifare.
/// Per questo porta <c>calcolato_il</c> e <c>regole_versione</c>: se domani cambia una soglia
/// del CCNL si sa quali giornate vanno ricalcolate.</item>
/// <item><c>employees.ecos_empl_code</c> — il ponte con EcosAgile. Senza questo le timbrature
/// non sanno a chi appartengono: il codice dipendente di Ecos (<c>EmplCode</c>) è l'unico
/// identificativo stabile che viaggia con ogni timbratura.</item>
/// </list>
///
/// <para>🪤 <b>Le rettifiche non stanno qui.</b> Sono righe di <c>hr_timbrature</c> con
/// <c>origine = 'RETTIFICA'</c>, autore e motivo: così la cronistoria di chi ha cambiato cosa
/// è nel grezzo insieme al resto, e il ricalcolo le vede come vede le altre.</para>
///
/// <para>🪤 <b>Niente vincolo di unicità su (dipendente, giorno) nelle timbrature</b>: in una
/// giornata ce ne sono da 1 a N, ed è proprio la loro molteplicità che il motore interpreta.
/// L'unicità sta su <c>hr_giornate</c>, che di righe per giorno ne ha una sola.</para>
/// </summary>
public sealed class M107_HrPresenze : IMigrazione
{
    public int Versione => 107;

    public string Descrizione =>
        "HR presenze: hr_timbrature (grezzo append-only), hr_giornate (cartellino rigenerabile), employees.ecos_empl_code";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // ── 1. Il ponte con EcosAgile ────────────────────────────────────────────────
        // (la colonna resta qui anche a schema già rinominato: è idempotente e serve comunque)
        bool nuovaColonna = AddColumnIfMissing(
            c, "employees", "ecos_empl_code", "VARCHAR(20) NULL AFTER username");
        if (nuovaColonna)
        {
            CreaIndiceSeManca(c, "employees", "idx_employees_ecos", "ecos_empl_code");
            log.LogInformation("[M107] employees.ecos_empl_code aggiunta: il collegamento con Ecos " +
                               "va compilato dalla pagina Timbrature, dipendente per dipendente.");
        }

        // 🪤 Se M111 ha già rinominato lo schema in inglese, qui NON c'è più niente da
        // creare. `RENAME TABLE` non tocca il nome dei vincoli: le FK restano
        // `fk_hr_timbrature_*` su `hr_punches`, quindi ricreare `hr_timbrature` fallisce con
        // «Duplicate foreign key constraint name» e, se anche passasse, lascerebbe due
        // tabelle — una vuota col nome vecchio. Una migrazione deve poter rigirare su uno
        // schema già avanti (lo pretende MotoreMigrazioniTests): si esce e basta.
        if (EsisteTabella(c, "hr_punches"))
        {
            log.LogInformation("[M107] Tabelle presenze già presenti col nome inglese (M111): niente da creare.");
            return;
        }

        // ── 2. Il grezzo: append-only, idempotente sull'origine ──────────────────────
        c.Execute(@"
            CREATE TABLE IF NOT EXISTS hr_timbrature (
                id              BIGINT       NOT NULL AUTO_INCREMENT,
                employee_id     INT          NOT NULL,
                giorno          DATE         NOT NULL,
                orario          DATETIME     NOT NULL,
                verso           VARCHAR(20)  NOT NULL,
                origine         VARCHAR(20)  NOT NULL DEFAULT 'ECOS',
                id_esterno      VARCHAR(50)  NULL,
                luogo           VARCHAR(100) NULL,
                motivo          VARCHAR(255) NULL,
                creata_da       INT          NULL,
                creata_il       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (id),
                UNIQUE KEY uq_hr_timbrature_origine (origine, id_esterno),
                KEY idx_hr_timbrature_giorno (employee_id, giorno),
                KEY idx_hr_timbrature_orario (orario),
                CONSTRAINT fk_hr_timbrature_dip FOREIGN KEY (employee_id)
                    REFERENCES employees (id) ON DELETE CASCADE,
                CONSTRAINT fk_hr_timbrature_autore FOREIGN KEY (creata_da)
                    REFERENCES employees (id) ON DELETE SET NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

        // ── 3. Il cartellino: si butta e si rifà ─────────────────────────────────────
        c.Execute(@"
            CREATE TABLE IF NOT EXISTS hr_giornate (
                id               BIGINT      NOT NULL AUTO_INCREMENT,
                employee_id      INT         NOT NULL,
                giorno           DATE        NOT NULL,
                entrata1         VARCHAR(10) NULL,
                uscita1          VARCHAR(10) NULL,
                entrata2         VARCHAR(10) NULL,
                uscita2          VARCHAR(10) NULL,
                minuti_ordinari  INT         NOT NULL DEFAULT 0,
                minuti_straord   INT         NOT NULL DEFAULT 0,
                minuti_pausa     INT         NOT NULL DEFAULT 0,
                fasce_json       JSON        NULL,
                nota             VARCHAR(255) NULL,
                anomalia         TINYINT(1)  NOT NULL DEFAULT 0,
                calcolato_il     DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
                regole_versione  INT         NOT NULL DEFAULT 1,
                PRIMARY KEY (id),
                UNIQUE KEY uq_hr_giornate (employee_id, giorno),
                KEY idx_hr_giornate_giorno (giorno),
                KEY idx_hr_giornate_anomalia (anomalia, giorno),
                CONSTRAINT fk_hr_giornate_dip FOREIGN KEY (employee_id)
                    REFERENCES employees (id) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci");

        int timbrature = c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_timbrature");
        int giornate = c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_giornate");
        log.LogInformation("[M107] Tabelle presenze pronte (timbrature {T}, giornate {G}). " +
                           "Il primo import le popola.", timbrature, giornate);
    }

    private static bool EsisteTabella(MySqlConnection c, string tabella) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = @Tabella", new { Tabella = tabella }) > 0;
}
