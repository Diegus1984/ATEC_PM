using System.Data;
using System.Security.Claims;
using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Cronistoria delle righe di distinta: registra ogni cambio di stato (da → a, quando, chi).
///
/// Nasce per rispondere a «quando è stato consegnato?» senza aggiungere una colonna-data per
/// ogni stato: gli stati sono configurabili dalla Conf. DDP, le colonne no. Da questi eventi
/// si ricava qualunque «X il» e in più si sa chi ha fatto il cambio.
/// </summary>
public static class DdpItemEvents
{
    public const string Commerciale = "COMMERCIAL";
    public const string Officina = "OFFICINA";

    /// <summary>Cambio fatto da una persona dall'interfaccia.</summary>
    public const string OrigineUtente = "UTENTE";

    /// <summary>Cambio applicato dal programma (sincronizzazioni, ordini Danea, lavorazioni).</summary>
    public const string OrigineSistema = "SISTEMA";

    /// <summary>
    /// Scrive una voce di cronistoria. Non fa nulla se lo stato non è cambiato davvero:
    /// un salvataggio che tocca solo note o quantità non deve sporcare la storia.
    /// </summary>
    /// <param name="tx">
    /// La transazione in corso, se chi chiama ne ha una. <b>Va passata</b>: su una connessione con
    /// una transazione aperta, MySqlConnector rifiuta il comando che non la dichiara, e qui il
    /// rifiuto finisce nel <c>catch</c> — cioè la riga di storia non viene scritta.
    /// Dal 26/08/2026 <c>PurchaseRfqController.SelectWinner</c> chiama DENTRO una transazione
    /// (e infatti passa <c>tx:</c> e <c>log:</c>); gli altri chiamanti (ProjectsController
    /// UpdateItem/UpdateOfficinaItem, WorkRequestDdpSync.MarkOfficinaBuilt) restano fuori
    /// transazione. Chi apre una tx e non passa <c>tx:</c> perde il «Consegnato il» senza
    /// nessun errore visibile.
    /// </param>
    /// <param name="log">
    /// Se c'è, un fallimento viene scritto nel log del server. Prima non lo era: il <c>catch</c> era
    /// vuoto, e una cronistoria persa non lasciava traccia da nessuna parte.
    /// </param>
    public static void Registra(
        IDbConnection c,
        string tipo,
        int itemId,
        int projectId,
        string? statoPrima,
        string? statoDopo,
        ClaimsPrincipal? utente = null,
        string origine = OrigineUtente,
        string note = "",
        IDbTransaction? tx = null,
        ILogger? log = null)
    {
        string dopo = (statoDopo ?? "").Trim();
        if (dopo.Length == 0) return;

        string prima = (statoPrima ?? "").Trim();
        if (string.Equals(prima, dopo, StringComparison.OrdinalIgnoreCase)) return;

        int? utenteId = null;
        string nomeUtente = "";
        if (utente != null)
        {
            nomeUtente = utente.FindFirst(ClaimTypes.Name)?.Value ?? "";
            if (int.TryParse(utente.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id)) utenteId = id;
        }

        try
        {
            c.Execute(@"
                INSERT INTO ddp_item_events
                    (item_type, item_id, project_id, from_status, to_status,
                     changed_at, changed_by_id, changed_by_name, origin, note)
                VALUES
                    (@Tipo, @ItemId, @ProjectId, @Prima, @Dopo,
                     NOW(), @UtenteId, @NomeUtente, @Origine, @Note)",
                new
                {
                    Tipo = tipo,
                    ItemId = itemId,
                    ProjectId = projectId,
                    Prima = prima.Length == 0 ? null : prima,
                    Dopo = dopo,
                    UtenteId = utenteId,
                    NomeUtente = nomeUtente,
                    Origine = origine,
                    Note = note
                }, tx);
        }
        catch (Exception ex)
        {
            // La cronistoria non deve MAI far fallire il salvataggio della riga: è un di più, non
            // il dato di lavoro. Ma «non far fallire» non vuol dire «non dirlo a nessuno»: da qui
            // esce il «Consegnato il», e prima questo catch era vuoto — una storia persa spariva
            // senza lasciare traccia né a video né nel log.
            // Error e non Warning per InvalidOperationException: quella è la transazione non
            // dichiarata, cioè un difetto di programmazione, non un guaio del database.
            if (ex is InvalidOperationException)
                log?.LogError(ex, "[DDP] Cronistoria non scritta (riga {ItemId}, {Tipo}): {Msg}",
                    itemId, tipo, ex.Message);
            else
                log?.LogWarning("[DDP] Cronistoria non scritta (riga {ItemId}, {Tipo}): {Msg}",
                    itemId, tipo, ex.Message);
        }
    }
}
