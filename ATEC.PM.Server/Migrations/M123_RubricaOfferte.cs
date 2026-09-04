using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Registro Offerte, fase 2 — le colonne che servono a non buttare via niente dello storico.
///
/// <para><b>Sulla rubrica.</b> L'esportazione «Soggetti» del gestionale, da cui vengono i 422
/// clienti dell'archivio offerte, porta sede e recapiti che in <c>customers</c> non avevano un
/// posto: città, CAP, provincia, nazione, fax e sito. Senza queste colonne l'import li
/// scarterebbe in silenzio.</para>
///
/// <para><b>Sulle offerte.</b> Tre campi del vecchio Excel che il piano non aveva previsto e che
/// dai dati risultano pieni: il <b>codice commessa</b> (84 righe — ed è il ponte verso
/// <c>projects</c>: i codici sono nello stesso formato <c>C221111_093</c>), il <b>numero
/// commessa</b> (38 righe) e la <b>segnalazione OK</b> grezza (405 righe), da cui <c>seed.py</c>
/// aveva già ricavato lo stato ma che resta l'originale di quella decisione.</para>
///
/// <para>Come per la v122, il DDL delle tabelle nuove vive in <see cref="SalesOffersDbService"/> e
/// in <c>DbService</c>: qui ci sono solo le ALTER per i database che quelle tabelle ce le hanno
/// già.</para>
/// </summary>
public sealed class M123_RubricaOfferte : IMigrazione
{
    public int Versione => 123;

    public string Descrizione =>
        "Rubrica: sede e recapiti dall'esportazione Soggetti · offerte: codice commessa, n. commessa, OK grezzo";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // customers — i campi dell'esportazione «Soggetti» che mancavano.
        int rubrica = 0;
        if (AddColumnIfMissing(c, "customers", "postal_code", "VARCHAR(20) NOT NULL DEFAULT '' AFTER address")) rubrica++;
        if (AddColumnIfMissing(c, "customers", "city", "VARCHAR(100) NOT NULL DEFAULT '' AFTER postal_code")) rubrica++;
        if (AddColumnIfMissing(c, "customers", "province", "VARCHAR(10) NOT NULL DEFAULT '' AFTER city")) rubrica++;
        if (AddColumnIfMissing(c, "customers", "country", "VARCHAR(100) NOT NULL DEFAULT '' AFTER province")) rubrica++;
        if (AddColumnIfMissing(c, "customers", "fax", "VARCHAR(50) NOT NULL DEFAULT '' AFTER cell")) rubrica++;
        if (AddColumnIfMissing(c, "customers", "website", "VARCHAR(200) NOT NULL DEFAULT '' AFTER pec")) rubrica++;

        // sales_offers — i campi dello storico, più la riga di provenienza.
        int offerte = 0;
        if (AddColumnIfMissing(c, "sales_offers", "project_code", "VARCHAR(30) NOT NULL DEFAULT '' AFTER project_id")) offerte++;
        if (AddColumnIfMissing(c, "sales_offers", "comm_number", "VARCHAR(20) NOT NULL DEFAULT '' AFTER project_code")) offerte++;
        if (AddColumnIfMissing(c, "sales_offers", "legacy_ok", "VARCHAR(50) NOT NULL DEFAULT '' AFTER legacy_number")) offerte++;

        // legacy_id = l'identificativo della riga nell'archivio di origine (`r4` = riga 4 del
        // foglio Detail). È la chiave con cui l'import riconosce ciò che ha già inserito: NON si
        // può usare anno+numero+sigla, perché i quattro duplicati 282-285 del 2025 hanno tutti
        // lo stesso anno, lo stesso numero e lo stesso venditore — la seconda esecuzione ne
        // scarterebbe la metà buona.
        if (AddColumnIfMissing(c, "sales_offers", "legacy_id", "VARCHAR(20) NOT NULL DEFAULT '' AFTER comm_number")) offerte++;
        CreaIndiceSeManca(c, "sales_offers", "idx_so_legacy", "legacy_id");

        log.LogInformation("[Migration v123] Rubrica: {R} colonne aggiunte · Registro Offerte: {O}.", rubrica, offerte);
    }
}
