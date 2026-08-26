using System.Data;
using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// «Comanda il padre» attraverso le due DDP (#119).
///
/// <para>Dalla v28 i componenti importati da una composizione Codex seguono la quantità
/// della riga padre. Finché stavano tutti in officina bastava una UPDATE su una tabella;
/// dal 25/08/2026 i figli si dividono — 2xx/3xx in <c>bom_items</c>, il resto in
/// <c>ddp_officina_items</c> — e l'intestazione esiste in <b>entrambe</b> le griglie.</para>
///
/// <para>Le due copie dell'intestazione non sono legate da una FK: stanno su tabelle
/// diverse e si riconoscono per <b>commessa + codice normalizzato</b>, che è la stessa
/// chiave con cui l'import le crea e le ritrova. Toccare la quantità su una delle due deve
/// muovere i componenti di tutte e due, altrimenti metà distinta resta indietro senza che
/// nessuno se ne accorga.</para>
/// </summary>
public static class ComposizioneDdp
{
    /// <summary>Codice senza punti né spazi: il part_number è salvato col punto, il Codex no.</summary>
    public static string Chiave(string? partNumber) =>
        (partNumber ?? "").Replace(".", "").Replace(" ", "").Trim();

    /// <summary>
    /// Riporta il cambio di quantità di una riga padre su tutti i suoi componenti, nelle due
    /// DDP, e allinea la quantità della copia gemella dell'intestazione.
    /// </summary>
    /// <param name="delta">Quanto è cambiata la quantità del padre (può essere negativa).</param>
    /// <param name="nuovaQuantita">Quantità del padre DOPO la modifica: serve per allineare la gemella.</param>
    /// <param name="statiEsclusi">Stati dell'aggregazione A9: quelle righe hanno la quantità bloccata.</param>
    /// <returns>Gli id dei componenti d'officina toccati (servono al chiamante per le lavorazioni).</returns>
    public static List<int> PropagaQuantita(
        IDbConnection c,
        int projectId,
        string? partNumber,
        decimal delta,
        decimal nuovaQuantita,
        int? updatedBy,
        IReadOnlyCollection<string> statiEsclusi)
    {
        var toccatiOfficina = new List<int>();
        string chiave = Chiave(partNumber);
        if (chiave.Length == 0 || delta == 0) return toccatiOfficina;

        // `IN` su lista vuota non è SQL valido: una stringa che non è nessuno stato.
        var esclusi = statiEsclusi.Count > 0 ? statiEsclusi.ToList() : new List<string> { "" };

        int padreOfficina = c.ExecuteScalar<int?>(@"
            SELECT id FROM ddp_officina_items
            WHERE project_id = @ProjectId AND REPLACE(part_number, '.', '') = @Chiave
              AND parent_officina_item_id IS NULL
            ORDER BY id LIMIT 1", new { ProjectId = projectId, Chiave = chiave }) ?? 0;

        int padreCommerciale = c.ExecuteScalar<int?>(@"
            SELECT id FROM bom_items
            WHERE project_id = @ProjectId AND REPLACE(COALESCE(part_number,''), '.', '') = @Chiave
              AND parent_bom_item_id IS NULL
            ORDER BY id LIMIT 1", new { ProjectId = projectId, Chiave = chiave }) ?? 0;

        if (padreOfficina > 0)
        {
            toccatiOfficina = c.Query<int>(@"
                SELECT id FROM ddp_officina_items
                WHERE parent_officina_item_id = @ParentId AND composition_qty IS NOT NULL
                  AND item_status NOT IN @Esclusi",
                new { ParentId = padreOfficina, Esclusi = esclusi }).ToList();
            foreach (int childId in toccatiOfficina)
            {
                c.Execute(@"UPDATE ddp_officina_items
                    SET quantity = GREATEST(0, quantity + composition_qty * @Delta),
                        updated_at = NOW(), updated_by = @UpdatedBy
                    WHERE id = @Id", new { Delta = delta, Id = childId, UpdatedBy = updatedBy });
                OfficinaRowSync.CongelaTipoDaStato(c, childId);
            }
        }

        if (padreCommerciale > 0)
        {
            c.Execute(@"UPDATE bom_items
                SET quantity = GREATEST(0, quantity + composition_qty * @Delta),
                    updated_at = NOW(), updated_by = @UpdatedBy
                WHERE parent_bom_item_id = @ParentId AND composition_qty IS NOT NULL
                  AND item_status NOT IN @Esclusi",
                new { ParentId = padreCommerciale, Delta = delta, UpdatedBy = updatedBy, Esclusi = esclusi });
        }

        // Le due intestazioni devono dire lo stesso numero: sono la stessa riga vista da due
        // griglie. Si allinea SEMPRE quella che il chiamante non ha appena scritto, con una
        // UPDATE diretta e non passando dall'endpoint — altrimenti si rientrerebbe qui.
        if (padreOfficina > 0)
            c.Execute(@"UPDATE ddp_officina_items SET quantity = @Q, updated_at = NOW(), updated_by = @By
                        WHERE id = @Id AND quantity <> @Q",
                new { Q = nuovaQuantita, Id = padreOfficina, By = updatedBy });
        if (padreCommerciale > 0)
            c.Execute(@"UPDATE bom_items SET quantity = @Q, updated_at = NOW(), updated_by = @By
                        WHERE id = @Id AND quantity <> @Q",
                new { Q = nuovaQuantita, Id = padreCommerciale, By = updatedBy });

        return toccatiOfficina;
    }

    /// <summary>Stati dell'aggregazione A9 («escluso dal totale»): quantità bloccata.</summary>
    public static List<string> StatiEsclusi(IDbConnection c) =>
        c.Query<string>(@"
            SELECT s.status_key FROM ddp_aggregation_states s
            JOIN ddp_aggregations a ON a.id = s.aggregation_id
            WHERE a.code = 'A9'").ToList();

    /// <summary>
    /// Cancellazione dell'intestazione: porta via i componenti in tutte e due le DDP e la
    /// copia gemella del padre. Chiamata PRIMA di eliminare la riga su cui si è cliccato.
    /// </summary>
    /// <returns>(componenti officina, componenti commerciali) rimossi.</returns>
    public static (int Officina, int Commerciale) EliminaComponenti(
        IDbConnection c, int projectId, string? partNumber, int rigaCliccata)
    {
        string chiave = Chiave(partNumber);
        if (chiave.Length == 0) return (0, 0);

        int padreOfficina = c.ExecuteScalar<int?>(@"
            SELECT id FROM ddp_officina_items
            WHERE project_id = @ProjectId AND REPLACE(part_number, '.', '') = @Chiave
              AND parent_officina_item_id IS NULL
            ORDER BY id LIMIT 1", new { ProjectId = projectId, Chiave = chiave }) ?? 0;
        int padreCommerciale = c.ExecuteScalar<int?>(@"
            SELECT id FROM bom_items
            WHERE project_id = @ProjectId AND REPLACE(COALESCE(part_number,''), '.', '') = @Chiave
              AND parent_bom_item_id IS NULL
            ORDER BY id LIMIT 1", new { ProjectId = projectId, Chiave = chiave }) ?? 0;

        int off = padreOfficina > 0
            ? c.Execute("DELETE FROM ddp_officina_items WHERE parent_officina_item_id = @Id",
                new { Id = padreOfficina })
            : 0;
        int comm = padreCommerciale > 0
            ? c.Execute("DELETE FROM bom_items WHERE parent_bom_item_id = @Id",
                new { Id = padreCommerciale })
            : 0;

        // La gemella dell'intestazione se ne va con l'originale: lasciarla lì darebbe una
        // riga padre senza figli in una griglia e nessuna nell'altra.
        if (padreOfficina > 0 && padreOfficina != rigaCliccata)
            c.Execute("DELETE FROM ddp_officina_items WHERE id = @Id", new { Id = padreOfficina });
        if (padreCommerciale > 0 && padreCommerciale != rigaCliccata)
            c.Execute("DELETE FROM bom_items WHERE id = @Id", new { Id = padreCommerciale });

        return (off, comm);
    }
}
