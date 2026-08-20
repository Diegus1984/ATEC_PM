using System.Data;
using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Le due sincronizzazioni che vivono <b>sulla riga della DDP Officina</b>.
///
/// <para>Fino alla v92 questa classe copiava ogni riga di distinta in una «bozza» di
/// <c>project_work_requests</c> e ne teneva allineati dieci campi. La segnalazione #83 ha
/// tolto le copie: la pagina Lavorazioni Officine guarda la riga vera, quindi non c'è più
/// niente da tenere allineato. Di quel motore resta quello che serviva davvero e che nessuno
/// aveva notato stesse lì dentro:</para>
///
/// <list type="number">
/// <item><see cref="CongelaTipoDaStato"/> — scrive <c>work_type</c> sulla riga la prima volta
/// che lo stato lo rivela. Serve al Bilancio (una riga chiusa non dice più se il pezzo era
/// costruito o comprato) e alle viste Interne/Esterne, che filtrano proprio per Tipo.</item>
/// <item><see cref="MarkOfficinaBuilt"/> — la lavorazione data per consegnata chiude la riga
/// di distinta collegata.</item>
/// </list>
/// </summary>
public static class OfficinaRowSync
{
    /// <summary>
    /// Tipo lavorazione dallo stato DDP Officina:
    /// DO / RO / IO → External; DC / COS → Internal; altri → "" (lo stato non lo dice).
    /// </summary>
    public static string TypeFromOfficinaStatus(string? itemStatus)
    {
        string key = (itemStatus ?? "").Trim().ToUpperInvariant();
        return key switch
        {
            "DO" or "RO" or "IO" => "External",
            "DC" or "COS" => "Internal",
            _ => "",
        };
    }

    /// <summary>
    /// Congela sulla riga DDP la natura della lavorazione (Internal/External) se non è
    /// ancora nota. Scrive una volta sola: una classificazione già presente (scelta a mano
    /// nella colonna Tipo, o congelata prima) non viene toccata.
    /// </summary>
    public static void FreezeOfficinaWorkType(IDbConnection c, int officinaItemId, string? workType)
    {
        if (string.IsNullOrWhiteSpace(workType)) return;
        c.Execute(@"
            UPDATE ddp_officina_items SET work_type = @Type
            WHERE id = @Id AND TRIM(COALESCE(work_type,'')) = ''",
            new { Id = officinaItemId, Type = workType });
    }

    /// <summary>
    /// Legge lo stato della riga e ne congela il Tipo se lo stato lo rivela. Da chiamare
    /// dopo ogni inserimento o cambio di stato di una riga officina: è il passo che tiene
    /// popolata la colonna su cui si reggono il Bilancio e le viste di Lavorazioni Officine.
    /// </summary>
    public static void CongelaTipoDaStato(IDbConnection c, int officinaItemId)
    {
        string? stato = c.ExecuteScalar<string?>(
            "SELECT item_status FROM ddp_officina_items WHERE id = @Id",
            new { Id = officinaItemId });
        FreezeOfficinaWorkType(c, officinaItemId, TypeFromOfficinaStatus(stato));
    }

    /// <summary>
    /// Ritorno di stato: lavorazione consegnata → la riga officina collegata si chiude.
    /// Dalla v75 (segnalazione #54) la chiusura positiva in officina è **COS se la lavorazione
    /// è interna, CON se è esterna**: un pezzo fatto in casa viene *costruito*, uno comprato
    /// fuori *consegnato*. Prima era DISP per tutti, stato che in officina non esiste più.
    /// Ritorna (projectId, officinaItemId) se lo stato è effettivamente cambiato.
    /// </summary>
    public static (int ProjectId, int OfficinaItemId)? MarkOfficinaBuilt(
        IDbConnection c, int workRequestId, ILogger? log = null)
    {
        var link = c.QueryFirstOrDefault<(int? OfficinaItemId, int ProjectId)>(@"
            SELECT ddp_officina_item_id AS OfficinaItemId, project_id AS ProjectId
            FROM project_work_requests WHERE id = @Id", new { Id = workRequestId });
        if (link.OfficinaItemId == null) return null;

        // Natura della lavorazione con la stessa cascata di ProjectEconomics: il tipo congelato
        // sulla riga vince sulla lavorazione collegata. Se non si sa, si chiude come CONSEGNATO
        // (è il caso della riga nata da un ordine, non da una costruzione interna).
        // ⚠ COLLATE esplicito: `project_work_requests.type` è utf8mb4_unicode_ci mentre
        // `ddp_officina_items.work_type` è utf8mb4_0900_ai_ci, e il COALESCE fra le due
        // produce una collation «NONE» che fa fallire qualunque confronto a valle.
        var riga = c.QueryFirstOrDefault<(string? Stato, string? WorkType)>(@"
            SELECT o.item_status AS Stato,
                   COALESCE(NULLIF(TRIM(COALESCE(o.work_type, '')), ''),
                            wr.type COLLATE utf8mb4_0900_ai_ci) AS WorkType
            FROM ddp_officina_items o
            LEFT JOIN project_work_requests wr ON wr.ddp_officina_item_id = o.id
            WHERE o.id = @Id", new { Id = link.OfficinaItemId });

        // La Stampa 3D (#87) si chiude come le interne: il pezzo è stato costruito in casa.
        string statoDopo = ATEC.PM.Shared.DTOs.OfficinaWorkTypes.IsInHouse(riga.WorkType?.Trim())
            ? "COS"
            : "CON";

        int rows = c.Execute(@"
            UPDATE ddp_officina_items SET item_status = @Stato, updated_at = NOW()
            WHERE id = @Id AND item_status <> @Stato",
            new { Id = link.OfficinaItemId, Stato = statoDopo });
        if (rows == 0) return null;

        // Cronistoria: questo passaggio lo fa il programma, non una persona.
        DdpItemEvents.Registra(c, DdpItemEvents.Officina, link.OfficinaItemId.Value, link.ProjectId,
            riga.Stato, statoDopo, utente: null, origine: DdpItemEvents.OrigineSistema,
            note: "lavorazione consegnata", log: log);

        return (link.ProjectId, link.OfficinaItemId.Value);
    }
}
