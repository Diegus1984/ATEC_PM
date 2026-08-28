using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Segnalazione #135 — il <b>grezzo</b> di un particolare a disegno.
///
/// <para>Un 101 può essere ricavato da un 201 commerciale: la lavorazione è dell'officina, il
/// materiale lo compra qualcun altro. Prima della #135 quel materiale non arrivava a nessuno e
/// <b>senza nessun errore a dirlo</b>: la commessa partiva e il grezzo non era ordinato. Qui si
/// fissano le quattro regole decise il 28/08/2026, che sono tutte invisibili a schermo mentre
/// sbagliano.</para>
///
/// <para>I test chiamano <see cref="GrezziDerivazione.Sincronizza"/>, cioè la funzione vera che
/// gira dopo ogni scrittura sulla DDP Officina: non una copia della sua SQL.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class GrezzoDerivazioneTests
{
    private readonly SchemaCondiviso _schema;

    public GrezzoDerivazioneTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. NASCE, CON LA QUANTITÀ DEL 101
    // ═══════════════════════════════════════════════════════════════

    [FactRichiedeMySql]
    public void Il_101_con_derivazione_fa_nascere_il_grezzo_uno_a_uno()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        int grezzo = Articolo(c, "201240826001", "Barra alluminio 40x40", fornitore: "SMC Italia");
        Deriva(c, Particolare(c, "101240826001", "Piastra"), grezzo);

        RigaOfficina(c, commessa, "101240826.001", quantita: 4);
        GrezziDerivazione.Esito esito = GrezziDerivazione.Sincronizza(c, commessa, null);

        Assert.Equal(1, esito.Creati);
        Grezzo riga = SoloGrezzo(c, commessa);
        Assert.Equal(4m, riga.Quantity);          // 4 pezzi = 4 grezzi
        Assert.Equal(4m, riga.RawAutoQty);        // e il conto automatico lo ricorda
        Assert.Equal("Barra alluminio 40x40", riga.Description);
        Assert.Equal("101240826.001", riga.RawSources);
    }

    [FactRichiedeMySql]
    public void Il_101_senza_derivazione_non_porta_niente_in_commerciale()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        Particolare(c, "101240826002", "Flangia");   // esiste nel Codex, ma senza grezzo

        RigaOfficina(c, commessa, "101240826.002", quantita: 3);
        GrezziDerivazione.Esito esito = GrezziDerivazione.Sincronizza(c, commessa, null);

        Assert.True(esito.NienteDaFare);
        Assert.Empty(Grezzi(c, commessa));
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. PIÙ 101 SULLO STESSO GREZZO = UNA RIGA SOLA
    // ═══════════════════════════════════════════════════════════════

    [FactRichiedeMySql]
    public void Due_particolari_dallo_stesso_grezzo_fanno_una_riga_sola_con_le_quantita_sommate()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        int grezzo = Articolo(c, "201240826001", "Barra alluminio 40x40");
        Deriva(c, Particolare(c, "101240826001", "Piastra A"), grezzo);
        Deriva(c, Particolare(c, "101240826002", "Piastra B"), grezzo);

        RigaOfficina(c, commessa, "101240826.001", quantita: 4);
        RigaOfficina(c, commessa, "101240826.002", quantita: 6);
        GrezziDerivazione.Sincronizza(c, commessa, null);

        Grezzo riga = SoloGrezzo(c, commessa);
        Assert.Equal(10m, riga.Quantity);
        Assert.Equal("101240826.001, 101240826.002", riga.RawSources);
    }

    [FactRichiedeMySql]
    public void Tolto_uno_dei_due_particolari_la_quantita_del_grezzo_si_scala()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        int grezzo = Articolo(c, "201240826001", "Barra alluminio 40x40");
        Deriva(c, Particolare(c, "101240826001", "Piastra A"), grezzo);
        Deriva(c, Particolare(c, "101240826002", "Piastra B"), grezzo);

        int rigaA = RigaOfficina(c, commessa, "101240826.001", quantita: 4);
        RigaOfficina(c, commessa, "101240826.002", quantita: 6);
        GrezziDerivazione.Sincronizza(c, commessa, null);

        c.Execute("DELETE FROM ddp_officina_items WHERE id = @Id", new { Id = rigaA });
        GrezziDerivazione.Esito esito = GrezziDerivazione.Sincronizza(c, commessa, null);

        Assert.Equal(1, esito.Aggiornati);
        Assert.Equal(6m, SoloGrezzo(c, commessa).Quantity);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. SPARISCE COL 101 — MA NON SE È GIÀ STATO COMPRATO
    // ═══════════════════════════════════════════════════════════════

    [FactRichiedeMySql]
    public void Tolto_il_101_il_grezzo_ancora_libero_se_ne_va()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        Deriva(c, Particolare(c, "101240826001", "Piastra"),
                  Articolo(c, "201240826001", "Barra alluminio 40x40"));

        int riga = RigaOfficina(c, commessa, "101240826.001", quantita: 4);
        GrezziDerivazione.Sincronizza(c, commessa, null);
        Assert.Single(Grezzi(c, commessa));

        c.Execute("DELETE FROM ddp_officina_items WHERE id = @Id", new { Id = riga });
        GrezziDerivazione.Esito esito = GrezziDerivazione.Sincronizza(c, commessa, null);

        Assert.Equal(1, esito.Eliminati);
        Assert.Empty(Grezzi(c, commessa));
    }

    /// <summary>
    /// 🪤 Un grezzo già ordinato non si cancella: <c>purchase_rfq_items.bom_item_id</c> è
    /// ON DELETE CASCADE e se ne porterebbe via la gara d'acquisto. Resta a chi compra, ma
    /// sganciato dalla derivazione — cioè come una qualunque riga commerciale.
    /// </summary>
    [FactRichiedeMySql]
    public void Tolto_il_101_il_grezzo_gia_ordinato_resta_ma_si_sgancia()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        Deriva(c, Particolare(c, "101240826001", "Piastra"),
                  Articolo(c, "201240826001", "Barra alluminio 40x40"));

        int riga = RigaOfficina(c, commessa, "101240826.001", quantita: 4);
        GrezziDerivazione.Sincronizza(c, commessa, null);
        int idGrezzo = SoloGrezzo(c, commessa).Id;

        // L'ordine Danea è già partito su quella riga.
        c.Execute("UPDATE bom_items SET danea_order_iddoc = 4321, item_status = 'IO' WHERE id = @Id",
            new { Id = idGrezzo });

        c.Execute("DELETE FROM ddp_officina_items WHERE id = @Id", new { Id = riga });
        GrezziDerivazione.Esito esito = GrezziDerivazione.Sincronizza(c, commessa, null);

        Assert.Equal(1, esito.Sganciati);
        Assert.Equal(0, esito.Eliminati);
        Assert.Equal(1, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM bom_items WHERE id = @Id AND COALESCE(raw_codex_code,'') = ''",
            new { Id = idGrezzo }));
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. LA QUANTITÀ CORRETTA A MANO NON SI TOCCA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Da una barra escono quattro pezzi: chi compra scrive «1» dove il conto 1:1 dice «4», e
    /// il ricalcolo non deve rimettercene quattro al primo salvataggio in officina.
    /// </summary>
    [FactRichiedeMySql]
    public void La_quantita_corretta_a_mano_resta_quella_scritta_dalla_persona()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        Deriva(c, Particolare(c, "101240826001", "Piastra"),
                  Articolo(c, "201240826001", "Barra alluminio 40x40"));

        int riga = RigaOfficina(c, commessa, "101240826.001", quantita: 4);
        GrezziDerivazione.Sincronizza(c, commessa, null);
        int idGrezzo = SoloGrezzo(c, commessa).Id;

        // Acquisti: «di barre ne basta una».
        c.Execute("UPDATE bom_items SET quantity = 1 WHERE id = @Id", new { Id = idGrezzo });

        // In officina cambia la quantità del particolare: il grezzo NON torna al conto automatico.
        c.Execute("UPDATE ddp_officina_items SET quantity = 8 WHERE id = @Id", new { Id = riga });
        GrezziDerivazione.Sincronizza(c, commessa, null);

        Grezzo dopo = SoloGrezzo(c, commessa);
        Assert.Equal(1m, dopo.Quantity);      // la mano di chi compra vince
        Assert.Equal(8m, dopo.RawAutoQty);    // ma il conto della distinta resta leggibile
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. QUANTO PESA NEL BILANCIO
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// In officina il costo di un 101 è un campo solo: sulle lavorazioni <b>esterne</b> è già il
    /// prezzo del pezzo finito, materiale compreso. Il grezzo non deve sommarcisi.
    /// </summary>
    [FactRichiedeMySql]
    public void Il_grezzo_di_una_lavorazione_esterna_non_pesa_nel_bilancio()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        Deriva(c, Particolare(c, "101240826001", "Piastra"),
                  Articolo(c, "201240826001", "Barra alluminio 40x40"));

        RigaOfficina(c, commessa, "101240826.001", quantita: 4, workType: OfficinaWorkTypes.External);
        GrezziDerivazione.Sincronizza(c, commessa, null);

        Assert.Equal(0m, SoloGrezzo(c, commessa).RawInternalShare);
    }

    /// <summary>Fatto in casa il costo sono le ore: il materiale non lo conta nessuno, e il grezzo è un costo vero.</summary>
    [FactRichiedeMySql]
    public void Il_grezzo_di_una_lavorazione_in_casa_pesa_per_intero()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        Deriva(c, Particolare(c, "101240826001", "Piastra"),
                  Articolo(c, "201240826001", "Barra alluminio 40x40"));

        RigaOfficina(c, commessa, "101240826.001", quantita: 4, workType: OfficinaWorkTypes.Internal);
        GrezziDerivazione.Sincronizza(c, commessa, null);

        Assert.Equal(1m, SoloGrezzo(c, commessa).RawInternalShare);
    }

    [FactRichiedeMySql]
    public void Con_un_particolare_in_casa_e_uno_fuori_il_grezzo_pesa_per_la_sua_parte()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        int grezzo = Articolo(c, "201240826001", "Barra alluminio 40x40");
        Deriva(c, Particolare(c, "101240826001", "Piastra A"), grezzo);
        Deriva(c, Particolare(c, "101240826002", "Piastra B"), grezzo);

        RigaOfficina(c, commessa, "101240826.001", quantita: 3, workType: OfficinaWorkTypes.Internal);
        RigaOfficina(c, commessa, "101240826.002", quantita: 1, workType: OfficinaWorkTypes.External);
        GrezziDerivazione.Sincronizza(c, commessa, null);

        // 3 pezzi su 4 si fanno in casa: il materiale che pesa è quello.
        Assert.Equal(0.75m, SoloGrezzo(c, commessa).RawInternalShare);
    }

    /// <summary>
    /// La quota non è un numero da guardare: serve al conto economico. Qui si esegue la
    /// funzione vera del Bilancio — non una copia della sua SQL — su due commesse identiche
    /// tranne che per la natura della lavorazione.
    /// </summary>
    [FactRichiedeMySql]
    public void Nel_costo_materiali_il_grezzo_entra_solo_per_quello_fatto_in_casa()
    {
        using MySqlConnection c = _schema.Apri();
        string[] esclusi = ComposizioneDdp.StatiEsclusi(c).ToArray();
        if (esclusi.Length == 0) esclusi = [""];

        int fuori = Commessa(c);
        int inCasa = Commessa(c);
        foreach ((int commessa, string tipo) in new[]
                 { (fuori, OfficinaWorkTypes.External), (inCasa, OfficinaWorkTypes.Internal) })
        {
            int grezzo = Articolo(c, $"20124082{commessa}", "Barra alluminio 40x40", prezzo: 25m);
            Deriva(c, Particolare(c, $"10124082{commessa}", "Piastra"), grezzo);
            RigaOfficina(c, commessa, CodexListItem.FormatCodice($"10124082{commessa}"),
                quantita: 4, workType: tipo);
            GrezziDerivazione.Sincronizza(c, commessa, null);
        }

        // 4 × 25,00 € di barra: sulla lavorazione esterna quel materiale è già dentro il prezzo
        // del pezzo finito che sta sulla riga d'officina, quindi qui non si somma.
        Assert.Equal(0m, ProjectEconomics.GetCommercialMaterialCost(c, fuori, esclusi));
        Assert.Equal(100m, ProjectEconomics.GetCommercialMaterialCost(c, inCasa, esclusi));
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. GLI STATI ESCLUSI DAI CONTEGGI (A9)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Un particolare annullato esce dai conteggi: se il suo grezzo restasse, il Bilancio
    /// escluderebbe il 101 e non il materiale che gli serviva.
    /// </summary>
    [FactRichiedeMySql]
    public void Il_particolare_annullato_non_chiede_piu_il_suo_grezzo()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        Deriva(c, Particolare(c, "101240826001", "Piastra"),
                  Articolo(c, "201240826001", "Barra alluminio 40x40"));

        int riga = RigaOfficina(c, commessa, "101240826.001", quantita: 4);
        GrezziDerivazione.Sincronizza(c, commessa, null);
        Assert.Single(Grezzi(c, commessa));

        c.Execute("UPDATE ddp_officina_items SET item_status = 'ANN' WHERE id = @Id", new { Id = riga });
        GrezziDerivazione.Sincronizza(c, commessa, null);

        Assert.Empty(Grezzi(c, commessa));
    }

    /// <summary>
    /// «Questo grezzo ce l'abbiamo già a magazzino»: chi compra annulla la riga, e il
    /// ricalcolo non deve rimettercela in ordine al primo salvataggio in officina. Fuori dagli
    /// stati d'ingresso la quantità non è più cosa nostra.
    /// </summary>
    [FactRichiedeMySql]
    public void Il_grezzo_annullato_da_chi_compra_non_torna_indietro()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        Deriva(c, Particolare(c, "101240826001", "Piastra"),
                  Articolo(c, "201240826001", "Barra alluminio 40x40"));

        int riga = RigaOfficina(c, commessa, "101240826.001", quantita: 4);
        GrezziDerivazione.Sincronizza(c, commessa, null);
        int idGrezzo = SoloGrezzo(c, commessa).Id;

        c.Execute("UPDATE bom_items SET item_status = 'ANN' WHERE id = @Id", new { Id = idGrezzo });

        c.Execute("UPDATE ddp_officina_items SET quantity = 10 WHERE id = @Id", new { Id = riga });
        GrezziDerivazione.Sincronizza(c, commessa, null);

        Grezzo dopo = SoloGrezzo(c, commessa);
        Assert.Equal(4m, dopo.Quantity);       // la quantità resta quella annullata
        Assert.Equal(10m, dopo.RawAutoQty);    // il conto della distinta si vede lo stesso
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. IDEMPOTENZA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// La sincronizzazione gira dopo OGNI scrittura sulla distinta officina: se la seconda
    /// passata cambiasse qualcosa, toccherebbe <c>updated_at</c> — che è il token di
    /// concorrenza della riga — e chi ha la griglia aperta si vedrebbe rifiutare il salvataggio
    /// con «riga modificata da altri».
    /// </summary>
    [FactRichiedeMySql]
    public void Sincronizzare_due_volte_di_fila_non_cambia_niente()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c);
        Deriva(c, Particolare(c, "101240826001", "Piastra"),
                  Articolo(c, "201240826001", "Barra alluminio 40x40"));

        RigaOfficina(c, commessa, "101240826.001", quantita: 4);
        GrezziDerivazione.Sincronizza(c, commessa, null);

        GrezziDerivazione.Esito secondo = GrezziDerivazione.Sincronizza(c, commessa, null);

        Assert.True(secondo.NienteDaFare);
        Assert.Single(Grezzi(c, commessa));
    }

    // ═══════════════════════════════════════════════════════════════
    // SEMINA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 🪤 Cliente e responsabile si riusano: <c>customers</c> ha una UNIQUE sulla partita IVA e
    /// due clienti di prova con la partita vuota non ci stanno insieme. Serve a chi scrive un
    /// test con DUE commesse.
    /// </summary>
    private static int Commessa(MySqlConnection c)
    {
        int cliente = c.ExecuteScalar<int?>("SELECT id FROM customers ORDER BY id LIMIT 1")
            ?? Inserisci(c, "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int pm = c.ExecuteScalar<int?>("SELECT id FROM employees ORDER BY id DESC LIMIT 1")
            ?? Inserisci(c, "INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        return Inserisci(c,
            @"INSERT INTO projects (code, title, customer_id, pm_id, status)
              VALUES ('C20260828.135', 'Commessa di prova', @Cliente, @Pm, 'ACTIVE')",
            new { Cliente = cliente, Pm = pm });
    }

    /// <summary>Articolo Codex. Il codice si scrive SENZA punti, come sta in <c>codex_items</c>.</summary>
    private static int Articolo(
        MySqlConnection c, string codice, string descr, string fornitore = "", decimal prezzo = 0m) =>
        Inserisci(c, @"
            INSERT INTO codex_items (codice, descr, fornitore, prezzo_forn)
            VALUES (@Codice, @Descr, @Fornitore, @Prezzo)",
            new { Codice = codice, Descr = descr, Fornitore = fornitore, Prezzo = prezzo });

    private static int Particolare(MySqlConnection c, string codice, string descr) =>
        Articolo(c, codice, descr);

    /// <summary>Il particolare si ricava da quel grezzo commerciale.</summary>
    private static void Deriva(MySqlConnection c, int particolare, int grezzo) =>
        c.Execute(@"
            INSERT INTO codex_item_references (source_codex_id, ref_codex_id, ref_type)
            VALUES (@Src, @Ref, '201')",
            new { Src = particolare, Ref = grezzo });

    /// <summary>
    /// Riga di DDP Officina. Il <c>part_number</c> si scrive COL punto, come lo salva la
    /// distinta; <c>work_type</c> vuoto vuol dire «non ancora classificata», e allora decide
    /// lo stato (DO → esterna).
    /// </summary>
    private static int RigaOfficina(
        MySqlConnection c, int commessa, string partNumber, decimal quantita,
        string workType = "", string stato = "DO") =>
        Inserisci(c, @"
            INSERT INTO ddp_officina_items
                (project_id, part_number, description, quantity, item_status, work_type)
            VALUES (@P, @Codice, 'Particolare', @Q, @S, @T)",
            new { P = commessa, Codice = partNumber, Q = quantita, S = stato, T = workType });

    private static List<Grezzo> Grezzi(MySqlConnection c, int commessa) =>
        c.Query<Grezzo>(@"
            SELECT id AS Id, COALESCE(part_number,'') AS PartNumber,
                   COALESCE(description,'') AS Description, quantity AS Quantity,
                   raw_auto_qty AS RawAutoQty, COALESCE(raw_sources,'') AS RawSources,
                   raw_internal_share AS RawInternalShare
            FROM bom_items
            WHERE project_id = @P AND COALESCE(raw_codex_code,'') <> ''
            ORDER BY id", new { P = commessa }).ToList();

    private static Grezzo SoloGrezzo(MySqlConnection c, int commessa) => Assert.Single(Grezzi(c, commessa));

    private static int Inserisci(MySqlConnection c, string sql, object? param = null)
    {
        c.Execute(sql, param);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private sealed class Grezzo
    {
        public int Id { get; set; }
        public string PartNumber { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal? RawAutoQty { get; set; }
        public string RawSources { get; set; } = "";
        public decimal? RawInternalShare { get; set; }
    }
}
