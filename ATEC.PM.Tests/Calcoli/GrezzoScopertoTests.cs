using ATEC.PM.Server.Services;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Segnalazione #142 — il grezzo <b>scoperto</b> e la scelta del fornitore.
///
/// <para>Il progettista può creare un 2xx senza avere ancora l'articolo commerciale da
/// assegnargli: finché il 201 di derivazione non è associato a NESSUN articolo Danea, la
/// riga del grezzo è «da associare» — non cambia stato e non entra in RDO. La condizione
/// vive in <see cref="GrezziDerivazione.SqlGrezzoScoperto"/> (una copia sola, usata da
/// GET righe, PUT e creazione RDO): questi test la esercitano LETTERALMENTE, non una
/// copia. E con più articoli sullo stesso 201 la scelta del fornitore è dell'utente:
/// <see cref="GrezziDerivazione.ApplicaFornitore"/> — anche quello testato qui.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class GrezzoScopertoTests
{
    private readonly SchemaCondiviso _schema;

    public GrezzoScopertoTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. SCOPERTO FINCHÉ NESSUN ARTICOLO — POI SI SBLOCCA DA SOLO
    // ═══════════════════════════════════════════════════════════════

    [FactRichiedeMySql]
    public void Il_grezzo_senza_articoli_e_scoperto_e_l_associazione_lo_sblocca()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        int grezzo = Articolo(c, "201310826001", "Molla inox");
        Deriva(c, Articolo(c, "101120526004", "Molla lavorata"), grezzo);
        RigaOfficina(c, commessa, "101120526.004", quantita: 5);
        GrezziDerivazione.Sincronizza(c, commessa, null);

        Assert.Equal(1, Scoperti(c, commessa));

        // Arriva l'associazione (Catalogo → codice ATEC): la condizione si spegne da sola,
        // senza nessun campo da aggiornare a mano — è calcolata, non salvata.
        ArticoloDanea(c, "17RF", grezzo, Fornitore(c, "SODEMANN"));
        Assert.Equal(0, Scoperti(c, commessa));
    }

    [FactRichiedeMySql]
    public void Il_codice_atec_senza_articoli_e_scoperto_e_l_associazione_lo_sblocca()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        // Riga commerciale NORMALE (niente derivazione) con un codice ATEC che nessun
        // articolo Danea porta: la regola di Diego (01/09/2026) la vuole FERMA.
        c.Execute(@"
            INSERT INTO bom_items (project_id, part_number, description, quantity, item_status, ddp_type, atec_code)
            VALUES (@P, 'RND-1', 'Rondella con codice scoperto', 1, 'DO', 'COMMERCIAL', '301999990901')",
            new { P = commessa });

        Assert.Equal(1, AtecScoperti(c, commessa));

        // L'associazione vera (AssignCore) scrive atec_code sull'articolo: la condizione
        // si spegne da sola, senza campi da aggiornare sulla riga.
        c.Execute(@"
            INSERT INTO catalog_items (code, description, is_active, atec_code)
            VALUES ('ART-TEST-301', 'Articolo associato di prova', 1, '301999990901')");
        Assert.Equal(0, AtecScoperti(c, commessa));
    }

    [FactRichiedeMySql]
    public void Una_riga_commerciale_normale_non_e_mai_scoperta()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        // Riga commerciale qualunque, senza derivazione (raw_codex_code vuoto).
        c.Execute(@"
            INSERT INTO bom_items (project_id, part_number, description, quantity, item_status, ddp_type)
            VALUES (@P, 'X-123', 'Vite qualunque', 1, 'DO', 'COMMERCIAL')",
            new { P = commessa });

        Assert.Equal(0, Scoperti(c, commessa));
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. SCELTA DEL FORNITORE (multi-articolo sul 201)
    // ═══════════════════════════════════════════════════════════════

    [FactRichiedeMySql]
    public void Con_piu_articoli_la_scelta_si_applica_e_il_ricalcolo_non_la_tocca()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        int grezzo = Articolo(c, "201310826001", "Molla inox");
        Deriva(c, Articolo(c, "101120526004", "Molla lavorata"), grezzo);
        int artA = ArticoloDanea(c, "17RF", grezzo, Fornitore(c, "SODEMANN"), costo: 2.5m);
        ArticoloDanea(c, "MOLLA-B", grezzo, Fornitore(c, "VANEL"), costo: 3.1m);

        RigaOfficina(c, commessa, "101120526.004", quantita: 5);
        GrezziDerivazione.Sincronizza(c, commessa, null);

        // Due fornitori sullo stesso codice: il motore non sceglie (riga senza fornitore).
        var riga = Riga(c, commessa);
        Assert.Null(riga.SupplierId);

        GrezziDerivazione.EsitoFornitore esito =
            GrezziDerivazione.ApplicaFornitore(c, commessa, "201310826.001", artA, null);
        Assert.Null(esito.Errore);

        riga = Riga(c, commessa);
        Assert.NotNull(riga.SupplierId);
        Assert.Equal("17RF", riga.PartNumber);
        Assert.Equal(artA, riga.CatalogItemId);
        Assert.Equal(2.5m, riga.UnitCost);

        // Il ricalcolo successivo aggiorna quantità e snapshot, MAI il fornitore scelto.
        GrezziDerivazione.Sincronizza(c, commessa, null);
        Assert.Equal(artA, Riga(c, commessa).CatalogItemId);
    }

    [FactRichiedeMySql]
    public void Un_articolo_di_un_altro_codice_viene_rifiutato()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        int grezzo = Articolo(c, "201310826001", "Molla inox");
        Deriva(c, Articolo(c, "101120526004", "Molla lavorata"), grezzo);
        int estraneo = ArticoloDanea(c, "ALTRO-1",
            Articolo(c, "201990826001", "Un altro commerciale"), Fornitore(c, "ALTRI"));

        RigaOfficina(c, commessa, "101120526.004", quantita: 1);
        GrezziDerivazione.Sincronizza(c, commessa, null);

        GrezziDerivazione.EsitoFornitore esito =
            GrezziDerivazione.ApplicaFornitore(c, commessa, "201310826001", estraneo, null);
        Assert.NotNull(esito.Errore);
        Assert.Null(Riga(c, commessa).SupplierId);
    }

    [FactRichiedeMySql]
    public void Una_riga_gia_in_gara_non_si_tocca()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        int grezzo = Articolo(c, "201310826001", "Molla inox");
        Deriva(c, Articolo(c, "101120526004", "Molla lavorata"), grezzo);
        int art = ArticoloDanea(c, "17RF", grezzo, Fornitore(c, "SODEMANN"));

        RigaOfficina(c, commessa, "101120526.004", quantita: 1);
        GrezziDerivazione.Sincronizza(c, commessa, null);
        MettiInRdo(c, commessa, Riga(c, commessa).Id);

        GrezziDerivazione.EsitoFornitore esito =
            GrezziDerivazione.ApplicaFornitore(c, commessa, "201310826001", art, null);
        Assert.NotNull(esito.Errore);
    }

    // ═══════════════════════════════════════════════════════════════
    // SEMINA (stessa forma di GrezzoDerivazioneTests)
    // ═══════════════════════════════════════════════════════════════

    private static int Commessa(MySqlConnection c)
    {
        int cliente = c.ExecuteScalar<int?>("SELECT id FROM customers ORDER BY id LIMIT 1")
            ?? Inserisci(c, "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int pm = c.ExecuteScalar<int?>("SELECT id FROM employees ORDER BY id DESC LIMIT 1")
            ?? Inserisci(c, "INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        return Inserisci(c,
            @"INSERT INTO projects (code, title, customer_id, pm_id, status)
              VALUES ('C20260831.142', 'Commessa di prova', @Cliente, @Pm, 'ACTIVE')",
            new { Cliente = cliente, Pm = pm });
    }

    private static int Articolo(MySqlConnection c, string codice, string descr) =>
        Inserisci(c, "INSERT INTO codex_items (codice, descr) VALUES (@Codice, @Descr)",
            new { Codice = codice, Descr = descr });

    private static void Deriva(MySqlConnection c, int particolare, int grezzo) =>
        c.Execute(@"
            INSERT INTO codex_item_references (source_codex_id, ref_codex_id, ref_type)
            VALUES (@Src, @Ref, '201')",
            new { Src = particolare, Ref = grezzo });

    /// <summary>
    /// 🪤 `suppliers` ha una UNIQUE sulla partita IVA (`UQ_Supplier_Vat`) e due fornitori di
    /// prova con la VAT vuota non ci stanno insieme — il nome fa da partita IVA finta.
    /// </summary>
    private static int Fornitore(MySqlConnection c, string nome) =>
        Inserisci(c, "INSERT INTO suppliers (company_name, vat_number) VALUES (@Nome, @Nome)",
            new { Nome = nome });

    /// <summary>Articolo Danea (specchio) associato a un codice Codex — l'aggancio della #142.</summary>
    private static int ArticoloDanea(
        MySqlConnection c, string code, int codexId, int supplierId, decimal costo = 1m) =>
        Inserisci(c, @"
            INSERT INTO catalog_items (code, description, unit_cost, supplier_id, is_active, codex_item_id)
            VALUES (@Code, CONCAT('Articolo ', @Code), @Costo, @Sup, 1, @Codex)",
            new { Code = code, Costo = costo, Sup = supplierId, Codex = codexId });

    private static int RigaOfficina(
        MySqlConnection c, int commessa, string partNumber, decimal quantita) =>
        Inserisci(c, @"
            INSERT INTO ddp_officina_items
                (project_id, part_number, description, quantity, item_status, work_type)
            VALUES (@P, @Codice, 'Particolare', @Q, 'DO', '')",
            new { P = commessa, Codice = partNumber, Q = quantita });

    private static void MettiInRdo(MySqlConnection c, int commessa, int bomItemId)
    {
        int rfq = Inserisci(c, @"
            INSERT INTO purchase_rfqs (atec_code, description, status, updated_at)
            VALUES ('201310826001', 'Gara di prova', 'DRAFT', NOW())");
        c.Execute(@"
            INSERT INTO purchase_rfq_items (rfq_id, bom_item_id, project_id, quantity)
            VALUES (@Rfq, @Bom, @P, 1)", new { Rfq = rfq, Bom = bomItemId, P = commessa });
    }

    /// <summary>Conta le righe della commessa che la condizione condivisa dice «scoperte».</summary>
    private static int Scoperti(MySqlConnection c, int commessa) =>
        c.ExecuteScalar<int>($@"
            SELECT COUNT(*) FROM bom_items b
            WHERE b.project_id = @P AND {GrezziDerivazione.SqlGrezzoScoperto("b")}",
            new { P = commessa });

    /// <summary>Idem per il codice ATEC scoperto, con l'espressione dell'atec effettivo del PUT.</summary>
    private static int AtecScoperti(MySqlConnection c, int commessa) =>
        c.ExecuteScalar<int>($@"
            SELECT COUNT(*) FROM bom_items b
            WHERE b.project_id = @P AND {GrezziDerivazione.SqlAtecScoperto(
                "COALESCE(NULLIF(b.atec_code,''), (SELECT ci2.atec_code FROM catalog_items ci2 WHERE ci2.id = b.catalog_item_id))")}",
            new { P = commessa });

    private static RigaLetta Riga(MySqlConnection c, int commessa) =>
        c.QuerySingle<RigaLetta>(@"
            SELECT id AS Id, supplier_id AS SupplierId, catalog_item_id AS CatalogItemId,
                   COALESCE(part_number,'') AS PartNumber, unit_cost AS UnitCost
            FROM bom_items
            WHERE project_id = @P AND COALESCE(raw_codex_code,'') <> ''",
            new { P = commessa });

    private static int Inserisci(MySqlConnection c, string sql, object? param = null)
    {
        c.Execute(sql, param);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private sealed class RigaLetta
    {
        public int Id { get; set; }
        public int? SupplierId { get; set; }
        public int? CatalogItemId { get; set; }
        public string PartNumber { get; set; } = "";
        public decimal UnitCost { get; set; }
    }
}
