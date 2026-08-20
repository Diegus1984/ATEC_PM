using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #88 — le due chiavi del cancello sulle commesse non attive.
///
/// <para>Il codice che arriva con questo stesso deploy (<c>ProjectWriteGuard</c> +
/// <c>[RequireProjectWritable]</c>) blocca ogni scrittura su una commessa in bozza, in stand-by
/// o chiusa a chi non possiede <c>action.project_locked_write</c>; la seconda chiave,
/// <c>data.project_drafts</c>, deciderà chi vede le commesse in bozza. «Solo PM e
/// Amministratore» — parole della segnalazione — qui diventa una chiave seminata su chi ha quel
/// ruolo, non un ruolo cablato nel codice: domani si allarga o si restringe dalla pagina
/// «Permessi», senza un deploy.</para>
///
/// <para>⚠️ <b>Questa migrazione è l'unica cosa che separa il cancello da un incidente.</b>
/// Dal 14/08 (fallback invertito, v85) una chiave nuova nasce spenta per CHIUNQUE: se il codice
/// del cancello arrivasse in produzione senza queste righe, la prima commessa messa in stand-by
/// diventerebbe intoccabile per tutti, Paolo e l'amministratore compresi. Per lo stesso motivo
/// <b>non è <c>Facoltativa</c></b>: se fallisse ma il server partisse lo stesso, il cancello
/// sarebbe attivo con le chiavi non seminate — è lo scenario da evitare, quindi un fallimento
/// deve fermare l'avvio e far scattare il rollback del deploy.</para>
///
/// <para>Oggi (16/08/2026) la semina prende <b>Paolo Zanoni e Alessandra Abatangelo</b> (ruolo
/// PM, entrambi con utenza — verificato sul database di produzione); l'<b>Admin</b> ha il jolly
/// <c>*</c> e l'helper lo salta apposta: il jolly copre anche le chiavi nuove, la riga esplicita
/// sarebbe solo rumore nella sua scheda. Il giorno del deploy nessun comportamento cambia:
/// tutte le commesse in produzione sono ATTIVE, il cancello resta a riposo finché qualcuno non
/// userà gli stati.</para>
/// </summary>
public sealed class M096_ChiaviCancelloCommessa : IMigrazione
{
    public int Versione => 96;

    public string Descrizione =>
        "#88: chiavi del cancello commesse (opera su sospese/chiuse, vede le bozze) seminate su PM e ADMIN";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Nel catalogo con un nome che si capisce nella pagina «Permessi». `min_level` è solo
        // descrittivo dalla Fase E, ma si scrive coerente (2 = livello PM di una volta);
        // DISABLED = a chi non ha la chiave il comando appare spento, non invisibile.
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior) VALUES
            ('action.project_locked_write', 'Opera su commesse sospese o chiuse', 'action', 2, 'DISABLED'),
            ('data.project_drafts',         'Vede le commesse in bozza',          'data',   2, 'HIDDEN')");

        int righe = SeminaChiaviPerLivello(c, 2, new[]
        {
            "action.project_locked_write", "data.project_drafts",
        });

        log.LogInformation(
            "[Migration v96] #88: 2 chiavi del cancello commesse registrate, {Righe} concessioni seminate su PM/ADMIN (il jolly '*' resta com'e).",
            righe);
    }
}
