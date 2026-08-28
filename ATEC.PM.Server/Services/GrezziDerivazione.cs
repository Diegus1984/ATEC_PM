using System.Data;
using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Segnalazione #135 — il <b>grezzo</b> di un particolare a disegno.
///
/// <para>Un 101 può essere ricavato da un 201 commerciale: la lavorazione è dell'officina, ma
/// il materiale qualcuno lo deve comprare. La derivazione sta in <c>codex_item_references</c>
/// (<c>ref_type = '201'</c>) e da qui diventa una riga vera in <b>DDP Commerciale</b>, così
/// Acquisti la vede, la mette in RDO e la ordina come qualunque altro articolo.</para>
///
/// <para><b>Il pezzo resta uno solo, visto da due griglie.</b> Non si duplica niente: in
/// officina c'è il 101 (disegno e ore), in commerciale il suo grezzo (materiale). Nel
/// Bilancio i due costi sono diversi e si sommano una volta sola.</para>
///
/// <para><b>Le regole, decise il 28/08/2026:</b></para>
/// <list type="number">
/// <item><b>Quantità 1:1</b> col 101 — 4 pezzi, 4 grezzi — <b>correggibile a mano</b>: da una
/// barra ne escono di più, e chi compra deve poter scrivere il numero vero. Il conto
/// automatico resta in <c>raw_auto_qty</c>: finché <c>quantity</c> gli è uguale, la riga
/// segue i 101; appena qualcuno la cambia, il ricalcolo non la tocca più.</item>
/// <item><b>Più 101 con lo stesso grezzo = UNA riga sola</b>, con le quantità sommate. Togli
/// un 101 e la quantità si scala da sé.</item>
/// <item><b>Un grezzo = un fornitore</b>: nessuno split di quantità. Con più articoli Danea
/// sullo stesso codice ATEC la riga nasce senza fornitore e sceglie la RDO.</item>
/// </list>
///
/// <para>🪤 <b>Il grezzo non è un figlio di composizione.</b> <c>parent_bom_item_id</c> e
/// <c>composition_qty</c> restano NULL: se avesse <c>composition_qty</c>, «comanda il padre»
/// (<see cref="ComposizioneDdp"/>) lo moltiplicherebbe una seconda volta a ogni cambio di
/// quantità dell'intestazione di gruppo, dopo che qui è già stato contato attraverso i suoi
/// 101. La sua quantità ha un padrone solo.</para>
///
/// <para>🪤 <b>Il ricalcolo non tocca le righe già impegnate</b> (in RDO, con un ordine Danea,
/// o uscite dagli stati d'ingresso VER/DO): cambiare la quantità di un pezzo già ordinato
/// sotto il naso di chi l'ha ordinato è peggio del disallineamento. Un grezzo impegnato che
/// non serve più non viene cancellato ma <b>sganciato</b> (si azzerano le colonne
/// <c>raw_*</c>): resta una normale riga di acquisto, che chi compra gestisce come vuole.</para>
/// </summary>
public static class GrezziDerivazione
{
    /// <summary>Cosa ha fatto una sincronizzazione: serve ai log e ai test.</summary>
    public sealed record Esito(int Creati, int Aggiornati, int Eliminati, int Sganciati)
    {
        public bool NienteDaFare => Creati == 0 && Aggiornati == 0 && Eliminati == 0 && Sganciati == 0;
    }

    /// <summary>
    /// Riallinea TUTTI i grezzi di una commessa alle righe 101 che ci sono adesso in DDP
    /// Officina. È idempotente: chiamarla due volte di fila non cambia niente, quindi la si
    /// può appendere a qualunque scrittura sulle righe d'officina senza ragionarci troppo.
    /// </summary>
    public static Esito Sincronizza(IDbConnection c, int projectId, int? updatedBy, string? requestedBy = null)
    {
        // Le righe negli stati dell'aggregazione A9 («escluse dal totale») non contano: sono
        // le stesse che «comanda il padre» salta quando propaga le quantità.
        List<string> esclusi = ComposizioneDdp.StatiEsclusi(c);
        if (esclusi.Count == 0) esclusi = new List<string> { "" };

        // Quanto grezzo serve, per codice 201, sommando i 101 che lo usano.
        // 🪤 `codex_items.codice` sta in DB senza punti, `part_number` col punto: il match è
        // sempre sul codice normalizzato, come in tutto il resto delle due DDP.
        List<Richiesta> richieste = c.Query<Richiesta>($@"
            SELECT rif.codice AS Codice, COALESCE(rif.descr,'') AS Descr,
                   COALESCE(rif.codice_nuovo,'') AS CodiceNuovo,
                   COALESCE(rif.fornitore,'') AS Fornitore,
                   COALESCE(rif.prezzo_forn,0) AS PrezzoForn,
                   SUM(o.quantity) AS Totale,
                   SUM(CASE WHEN {ProjectEconomics.OfficinaWorkTypeExpr} IN ('Internal','Print3D')
                            THEN o.quantity ELSE 0 END) AS TotaleInCasa,
                   GROUP_CONCAT(DISTINCT o.part_number ORDER BY o.part_number SEPARATOR ', ') AS Origini
            FROM ddp_officina_items o
            LEFT JOIN project_work_requests wr ON wr.ddp_officina_item_id = o.id
            JOIN codex_items src
              ON src.codice = REPLACE(REPLACE(COALESCE(o.part_number,''), '.', ''), ' ', '')
            JOIN codex_item_references r
              ON r.source_codex_id = src.id AND r.ref_type = '201'
            JOIN codex_items rif ON rif.id = r.ref_codex_id
            WHERE o.project_id = @ProjectId AND o.item_status NOT IN @Esclusi
            GROUP BY rif.id, rif.codice, rif.descr, rif.codice_nuovo, rif.fornitore, rif.prezzo_forn",
            new { ProjectId = projectId, Esclusi = esclusi }).ToList();

        List<RigaGrezzo> esistenti = c.Query<RigaGrezzo>(@"
            SELECT b.id AS Id, COALESCE(b.raw_codex_code,'') AS RawCodexCode,
                   b.quantity AS Quantity, b.raw_auto_qty AS RawAutoQty,
                   COALESCE(b.item_status,'') AS ItemStatus,
                   b.danea_order_iddoc AS DaneaOrderIddoc,
                   (SELECT COUNT(*) FROM purchase_rfq_items i WHERE i.bom_item_id = b.id) AS RigheRdo
            FROM bom_items b
            WHERE b.project_id = @ProjectId AND b.raw_codex_code IS NOT NULL AND b.raw_codex_code <> ''",
            new { ProjectId = projectId }).ToList();

        var perCodice = esistenti.ToDictionary(r => r.RawCodexCode, r => r);
        int creati = 0, aggiornati = 0, eliminati = 0, sganciati = 0;

        foreach (Richiesta richiesta in richieste)
        {
            string chiave = ComposizioneDdp.Chiave(richiesta.Codice);
            if (chiave.Length == 0) continue;

            if (!perCodice.TryGetValue(chiave, out RigaGrezzo? riga))
            {
                if (richiesta.Totale <= 0) continue;
                Crea(c, projectId, chiave, richiesta, updatedBy, requestedBy);
                creati++;
                continue;
            }

            if (Aggiorna(c, riga, richiesta, updatedBy)) aggiornati++;
            perCodice.Remove(chiave);
        }

        // Quel che resta nel dizionario è grezzo che non serve più: nessun 101 in distinta lo
        // chiede. Se la riga è ancora libera se ne va col suo 101; se è già impegnata resta,
        // ma sganciata dalla derivazione.
        foreach (RigaGrezzo orfana in perCodice.Values)
        {
            if (orfana.Libera)
            {
                c.Execute("DELETE FROM bom_items WHERE id = @Id", new { Id = orfana.Id });
                eliminati++;
            }
            else
            {
                c.Execute(@"UPDATE bom_items
                    SET raw_codex_code = NULL, raw_auto_qty = NULL, raw_sources = NULL,
                        updated_at = NOW(), updated_by = @By
                    WHERE id = @Id", new { Id = orfana.Id, By = updatedBy });
                sganciati++;
            }
        }

        return new Esito(creati, aggiornati, eliminati, sganciati);
    }

    /// <summary>
    /// Sincronizza la commessa di una riga d'officina. Comodità per i punti in cui si ha in
    /// mano l'id della riga e non quello della commessa (la riga può anche essere già stata
    /// cancellata: in quel caso non c'è niente da fare).
    /// </summary>
    public static Esito SincronizzaPerRiga(IDbConnection c, int officinaItemId, int? updatedBy)
    {
        int projectId = c.ExecuteScalar<int?>(
            "SELECT project_id FROM ddp_officina_items WHERE id = @Id", new { Id = officinaItemId }) ?? 0;
        return projectId > 0
            ? Sincronizza(c, projectId, updatedBy)
            : new Esito(0, 0, 0, 0);
    }

    /// <summary>
    /// Sincronizza tutte le commesse che hanno in DDP Officina un dato 101. Serve quando la
    /// derivazione cambia sull'<b>articolo</b> (compilata o tolta dalla scheda Codex): senza
    /// questo, la riga del grezzo comparirebbe solo alla prossima scrittura sulla distinta di
    /// quella commessa, cioè a sorpresa e chissà quando.
    /// </summary>
    /// <returns>Le commesse toccate, con l'esito di ognuna.</returns>
    public static List<(int ProjectId, Esito Esito)> SincronizzaCommesseCon101(
        IDbConnection c, string? codice101, int? updatedBy)
    {
        var toccate = new List<(int, Esito)>();
        string chiave = ComposizioneDdp.Chiave(codice101);
        if (chiave.Length == 0) return toccate;

        List<int> commesse = c.Query<int>(@"
            SELECT DISTINCT project_id FROM ddp_officina_items
            WHERE REPLACE(REPLACE(COALESCE(part_number,''), '.', ''), ' ', '') = @Chiave",
            new { Chiave = chiave }).ToList();

        foreach (int projectId in commesse)
        {
            Esito esito = Sincronizza(c, projectId, updatedBy);
            if (!esito.NienteDaFare) toccate.Add((projectId, esito));
        }
        return toccate;
    }

    /// <summary>Riga nuova: nasce come una qualunque riga commerciale importata dal Codex.</summary>
    private static void Crea(
        IDbConnection c, int projectId, string chiave, Richiesta richiesta, int? updatedBy, string? requestedBy)
    {
        ArticoloDaCodex.Esito art = ArticoloDaCodex.Risolvi(c, richiesta.Codice, richiesta.CodiceNuovo);

        c.Execute(@"
            INSERT INTO bom_items
                (project_id, catalog_item_id, part_number, description, unit, quantity, unit_cost,
                 supplier_id, manufacturer, item_status, requested_by, danea_ref,
                 destination, destination_spec, notes, ddp_type, atec_code,
                 raw_codex_code, raw_auto_qty, raw_sources, raw_internal_share, created_by, updated_at)
            VALUES
                (@ProjectId, @CatalogItemId, @PartNumber, @Description, 'PZ', @Quantity, @UnitCost,
                 @SupplierId, '', 'DO', @RequestedBy, '',
                 '', '', @Notes, 'COMMERCIAL', NULLIF(@AtecCode,''),
                 @RawCodexCode, @Quantity, @RawSources, @Quota, @CreatedBy, NOW())",
            new
            {
                ProjectId = projectId,
                art.CatalogItemId,
                art.PartNumber,
                AtecCode = art.AtecCode,
                Description = richiesta.Descr,
                Quantity = richiesta.Totale,
                // Col prezzo dell'articolo Danea si resta omogenei al resto della distinta
                // commerciale; senza articolo vale quello del Codex.
                UnitCost = art.UnitCost ?? richiesta.PrezzoForn,
                // In officina il fornitore è testo, qui serve la FK: stessa regola di aggancio
                // dell'import di composizione, in un posto solo.
                SupplierId = art.SupplierId ?? FornitoreLookup.TrovaPerNome(c, richiesta.Fornitore),
                RequestedBy = requestedBy ?? "",
                // La nota si scrive SOLO qui, alla nascita: `notes` è di chi compra e
                // riscriverla a ogni ricalcolo cancellerebbe quello che ci ha messo.
                Notes = $"Grezzo di {richiesta.Origini}",
                RawCodexCode = chiave,
                RawSources = Tronca(richiesta.Origini, 300),
                Quota = richiesta.QuotaBilancio,
                CreatedBy = updatedBy
            });
    }

    /// <summary>Riga già presente: si aggiorna solo quello che è ancora nostro.</summary>
    /// <returns><c>true</c> se qualcosa è cambiato davvero.</returns>
    private static bool Aggiorna(IDbConnection c, RigaGrezzo riga, Richiesta richiesta, int? updatedBy)
    {
        string origini = Tronca(richiesta.Origini, 300);

        // La quantità la si riscrive solo se nessuno l'ha corretta a mano (quantity ancora
        // uguale all'ultimo conto automatico) e la riga non è impegnata in un acquisto.
        bool seguiLaDistinta = riga.Libera && riga.RawAutoQty.HasValue && riga.Quantity == riga.RawAutoQty.Value;

        // 🪤 L'UPDATE è condizionato apposta: `updated_at` è il token di concorrenza ottimistica
        // della riga (pattern v5). Toccarlo a ogni sincronizzazione — anche quando non cambia
        // niente — farebbe fallire con «riga modificata da altri» il salvataggio di chi ha la
        // griglia aperta su quella riga.
        int toccate = c.Execute(@"
            UPDATE bom_items
            SET quantity = CASE WHEN @Segui = 1 THEN @Totale ELSE quantity END,
                raw_auto_qty = @Totale,
                raw_sources = @Origini,
                raw_internal_share = @Quota,
                updated_at = NOW(), updated_by = @By
            WHERE id = @Id
              AND ((@Segui = 1 AND quantity <> @Totale)
                   OR NOT (raw_auto_qty <=> @Totale)
                   OR NOT (raw_internal_share <=> @Quota)
                   OR COALESCE(raw_sources,'') <> @Origini)",
            new
            {
                Id = riga.Id,
                Totale = richiesta.Totale,
                Origini = origini,
                Quota = richiesta.QuotaBilancio,
                Segui = seguiLaDistinta ? 1 : 0,
                By = updatedBy
            });

        return toccate > 0;
    }

    private static string Tronca(string? testo, int max)
    {
        string s = (testo ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>Quanto grezzo serve per un 201, sommando i 101 della commessa che lo usano.</summary>
    private sealed class Richiesta
    {
        /// <summary>Codice Codex del 201, come sta in <c>codex_items.codice</c> (senza punti).</summary>
        public string Codice { get; set; } = "";
        public string Descr { get; set; } = "";
        public string CodiceNuovo { get; set; } = "";
        public string Fornitore { get; set; } = "";
        public decimal PrezzoForn { get; set; }
        public decimal Totale { get; set; }
        /// <summary>Quanta di quella quantità la chiedono lavorazioni fatte in casa.</summary>
        public decimal TotaleInCasa { get; set; }
        /// <summary>I 101 che lo chiedono, già formattati col punto come stanno in distinta.</summary>
        public string Origini { get; set; } = "";

        /// <summary>
        /// Quota del grezzo che pesa nel Bilancio (0…1): la frazione chiesta da lavorazioni in
        /// casa. Sulle esterne il costo del pezzo finito è già sulla riga d'officina, quindi
        /// contare anche il grezzo raddoppierebbe il materiale.
        /// </summary>
        public decimal QuotaBilancio => Totale > 0 ? Math.Round(TotaleInCasa / Totale, 4) : 0m;
    }

    /// <summary>Riga di grezzo già in DDP Commerciale.</summary>
    private sealed class RigaGrezzo
    {
        public int Id { get; set; }
        public string RawCodexCode { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal? RawAutoQty { get; set; }
        public string ItemStatus { get; set; } = "";
        public int? DaneaOrderIddoc { get; set; }
        public int RigheRdo { get; set; }

        /// <summary>
        /// Riga ancora «nostra»: nessuna RDO, nessun ordine Danea e stato d'ingresso. Solo su
        /// queste il ricalcolo può scrivere quantità o cancellare.
        /// </summary>
        public bool Libera =>
            RigheRdo == 0
            && DaneaOrderIddoc == null
            && (string.Equals(ItemStatus, "VER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ItemStatus, "DO", StringComparison.OrdinalIgnoreCase));
    }
}
