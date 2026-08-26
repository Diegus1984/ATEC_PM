using Dapper;
using MySqlConnector;
using ATEC.PM.Server.Services;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M105 — Segnalazione #119: importando un gruppo (5xx) in commessa, i componenti si dividono
/// fra le due DDP invece di finire tutti in officina. Qui si preparano le fondamenta:
///
/// <list type="number">
/// <item><b>Struttura</b>: <c>bom_items</c> (DDP Commerciale) prende
/// <c>parent_bom_item_id</c> + <c>composition_qty</c>, gemelli di quelli che
/// <c>ddp_officina_items</c> ha dalla v28 («comanda il padre»). Stessa forma, stessa FK
/// <c>ON DELETE SET NULL</c>: eliminato il padre i figli restano come righe libere.</item>
/// <item><b>Dati</b>: le composizioni già importate vengono ri-smistate. I componenti
/// commerciali (2xx/3xx, vedi <see cref="DdpSmistamento"/>) escono dalla DDP Officina ed
/// entrano nella Commerciale sotto una riga padre nuova, che è la stessa intestazione
/// collassabile presente di là.</item>
/// </list>
///
/// <para>Al 25/08/2026 il ri-smistamento tocca <b>un solo caso reale</b>: la commessa
/// C260805_500, gruppo <c>501140621.001</c> con 23 componenti, di cui 14 commerciali
/// (3 righe 2xx + 11 righe 3xx). Le 9 righe 101 restano dove sono.</para>
///
/// <para>🪤 Il fornitore non si trasferisce per copia: in officina è testo libero
/// (<c>supplier_name</c>), in commerciale è una FK all'anagrafica (<c>supplier_id</c>). Si
/// aggancia per nome normalizzato (senza punti, senza maiuscole) e, quando l'anagrafica non
/// ha quel fornitore, si lascia vuoto — meglio un campo da compilare che un aggancio
/// sbagliato. Quanti sono lo dice il log.</para>
/// </summary>
public sealed class M105_SplitComposizioneDdp : IMigrazione
{
    public int Versione => 105;

    public string Descrizione =>
        "bom_items: parent_bom_item_id + composition_qty; componenti commerciali spostati dalla DDP Officina";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // ── 1. Struttura (stessa forma della v28 sull'officina) ───────────────────────
        bool nuove = AddColumnIfMissing(c, "bom_items", "parent_bom_item_id", "INT NULL AFTER notes");
        AddColumnIfMissing(c, "bom_items", "composition_qty", "DECIMAL(10,3) NULL AFTER parent_bom_item_id");
        if (nuove)
        {
            try
            {
                c.Execute(@"ALTER TABLE bom_items
                    ADD CONSTRAINT fk_bom_parent FOREIGN KEY (parent_bom_item_id)
                        REFERENCES bom_items(id) ON DELETE SET NULL");
            }
            catch (Exception ex)
            {
                // Non bloccante come la FK della v101: senza vincolo il raggruppamento
                // funziona lo stesso, si perde solo la pulizia automatica dei riferimenti.
                log.LogWarning("[Migration v105] FK bom_items.parent_bom_item_id non creata (non bloccante): {Msg}", ex.Message);
            }
        }

        // ── 2. Ri-smistamento delle composizioni già in commessa ──────────────────────
        var figli = c.Query<FiglioDaSpostare>(@"
            SELECT o.id AS Id, o.project_id AS ProjectId, o.parent_officina_item_id AS ParentOfficinaId,
                   COALESCE(o.part_number,'') AS PartNumber, COALESCE(o.description,'') AS Description,
                   o.quantity AS Quantity, o.composition_qty AS CompositionQty,
                   o.unit_cost AS UnitCost, COALESCE(o.supplier_name,'') AS SupplierName,
                   COALESCE(o.item_status,'DO') AS ItemStatus, COALESCE(o.requested_by,'') AS RequestedBy,
                   COALESCE(o.danea_ref,'') AS DaneaRef, o.date_needed AS DateNeeded,
                   COALESCE(o.destination,'') AS Destination, COALESCE(o.destination_spec,'') AS DestinationSpec,
                   o.notes AS Notes, o.created_by AS CreatedBy
            FROM ddp_officina_items o
            WHERE o.parent_officina_item_id IS NOT NULL
            ORDER BY o.parent_officina_item_id, o.id").ToList();

        var daSpostare = figli.Where(f => DdpSmistamento.VaInCommerciale(f.PartNumber)).ToList();
        if (daSpostare.Count == 0)
        {
            log.LogInformation("[Migration v105] Nessun componente commerciale da spostare.");
            return;
        }

        // Un padre commerciale per ogni padre d'officina toccato: creato una volta sola.
        var padriCommerciali = new Dictionary<int, int>();
        int spostati = 0, senzaFornitore = 0;

        foreach (FiglioDaSpostare figlio in daSpostare)
        {
            if (!padriCommerciali.TryGetValue(figlio.ParentOfficinaId, out int parentBomId))
            {
                parentBomId = CreaPadreCommerciale(c, figlio.ParentOfficinaId, log);
                padriCommerciali[figlio.ParentOfficinaId] = parentBomId;
            }

            int? supplierId = FornitoreLookup.TrovaPerNome(c, figlio.SupplierName);
            if (supplierId == null && figlio.SupplierName.Length > 0) senzaFornitore++;

            c.Execute(@"
                INSERT INTO bom_items
                    (project_id, part_number, description, unit, quantity, unit_cost, supplier_id,
                     manufacturer, item_status, requested_by, danea_ref, date_needed,
                     destination, destination_spec, notes, ddp_type,
                     parent_bom_item_id, composition_qty, created_by, updated_at)
                VALUES
                    (@ProjectId, @PartNumber, @Description, 'PZ', @Quantity, @UnitCost, @SupplierId,
                     '', @ItemStatus, @RequestedBy, @DaneaRef, @DateNeeded,
                     @Destination, @DestinationSpec, @Notes, 'COMMERCIAL',
                     @ParentBomId, @CompositionQty, @CreatedBy, NOW())",
                new
                {
                    figlio.ProjectId, figlio.PartNumber, figlio.Description, figlio.Quantity,
                    figlio.UnitCost, SupplierId = supplierId, figlio.ItemStatus, figlio.RequestedBy,
                    figlio.DaneaRef, figlio.DateNeeded, figlio.Destination, figlio.DestinationSpec,
                    figlio.Notes, ParentBomId = parentBomId, figlio.CompositionQty, figlio.CreatedBy
                });

            // La riga se ne va dall'officina solo dopo che la gemella esiste di là.
            c.Execute("DELETE FROM ddp_officina_items WHERE id = @Id", new { figlio.Id });
            spostati++;
        }

        log.LogInformation(
            "[Migration v105] Spostati {Spostati} componenti commerciali sotto {Padri} intestazioni; {Senza} senza fornitore in anagrafica",
            spostati, padriCommerciali.Count, senzaFornitore);
    }

    /// <summary>
    /// Riga padre nella DDP Commerciale: è la stessa intestazione collassabile che sta in
    /// officina, riscritta di là con lo stesso codice e la stessa quantità. Costo a zero
    /// di proposito — il costo del gruppo è la somma dei suoi componenti, che sono righe
    /// vere: metterlo anche sul padre lo conterebbe due volte.
    /// </summary>
    private static int CreaPadreCommerciale(MySqlConnection c, int parentOfficinaId, ILogger log)
    {
        // 🪤 Lo stato NON si copia dal padre d'officina: quello vive in DC («da costruire»),
        // che nella matrice COMMERCIAL non esiste — la riga nascerebbe bloccata, senza
        // nessuna transizione possibile. L'intestazione nasce in DO come i suoi componenti.
        // (Corretto dalla M106 dopo che era già successo in produzione.)
        var padre = c.QueryFirstOrDefault<PadreOfficina>(@"
            SELECT project_id AS ProjectId, COALESCE(part_number,'') AS PartNumber,
                   COALESCE(description,'') AS Description, quantity AS Quantity,
                   'DO' AS ItemStatus, COALESCE(requested_by,'') AS RequestedBy,
                   created_by AS CreatedBy
            FROM ddp_officina_items WHERE id = @Id", new { Id = parentOfficinaId })
            ?? throw new InvalidOperationException($"Padre officina {parentOfficinaId} non trovato");

        // Se in DDP Commerciale c'è già una riga con quel codice la si riusa come intestazione:
        // due padri identici sulla stessa commessa sarebbero due gruppi diversi per l'utente.
        string chiave = padre.PartNumber.Replace(".", "").Trim();
        int esistente = c.ExecuteScalar<int?>(@"
            SELECT id FROM bom_items
            WHERE project_id = @ProjectId AND REPLACE(COALESCE(part_number,''), '.', '') = @Chiave
              AND parent_bom_item_id IS NULL
            ORDER BY id LIMIT 1", new { padre.ProjectId, Chiave = chiave }) ?? 0;
        if (esistente > 0)
        {
            log.LogInformation("[Migration v105] Intestazione {Codice} già presente in DDP Commerciale (riga {Id})",
                padre.PartNumber, esistente);
            return esistente;
        }

        return c.ExecuteScalar<int>(@"
            INSERT INTO bom_items
                (project_id, part_number, description, unit, quantity, unit_cost,
                 item_status, requested_by, ddp_type, created_by, updated_at)
            VALUES
                (@ProjectId, @PartNumber, @Description, 'PZ', @Quantity, 0,
                 @ItemStatus, @RequestedBy, 'COMMERCIAL', @CreatedBy, NOW());
            SELECT LAST_INSERT_ID()",
            new { padre.ProjectId, padre.PartNumber, padre.Description, padre.Quantity, padre.ItemStatus, padre.RequestedBy, padre.CreatedBy });
    }

    private sealed class FiglioDaSpostare
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public int ParentOfficinaId { get; set; }
        public string PartNumber { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal? CompositionQty { get; set; }
        public decimal UnitCost { get; set; }
        public string SupplierName { get; set; } = "";
        public string ItemStatus { get; set; } = "DO";
        public string RequestedBy { get; set; } = "";
        public string DaneaRef { get; set; } = "";
        public DateTime? DateNeeded { get; set; }
        public string Destination { get; set; } = "";
        public string DestinationSpec { get; set; } = "";
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
    }

    private sealed class PadreOfficina
    {
        public int ProjectId { get; set; }
        public string PartNumber { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public string ItemStatus { get; set; } = "DO";
        public string RequestedBy { get; set; } = "";
        public int? CreatedBy { get; set; }
    }
}
