using FirebirdSql.Data.FirebirdClient;

namespace ATEC.PM.Server.Services;

// Creazione ORDINE FORNITORE direttamente nel Firebird di Danea (archivio Atec_PM),
// strada B decisa il 22/07/2026. Replica ESATTA di ciò che scrive Danea quando crei
// l'ordine a mano (reverse-engineering sul documento campione IDDoc=4 in Atec_PM):
//   TDocTestate (TipoDoc='E', StatoOrdine='Conf', totali + snapshot anagrafica)
//   + TDocRighe (MovMagazz=1)
//   + TDocIva (riepilogo per aliquota)
//   + TMovMagazz (QtaInArrivo: è ciò che alimenta la colonna «in arrivo»)
//   + 4 generatori GEN_ID e numerazione per (TipoDoc, anno, Numeraz vuota).
// Regole di scrittura Danea: Charset WIN1252 (mai NONE), transazione unica, fail-fast.
// Caveat runbook: preferibile che nessuno stia creando ordini in Danea nello stesso momento
// (la numerazione è MAX+1 dentro la stessa transazione: collisione possibile solo simultanea).
public class DaneaOrderService
{
    private readonly IConfiguration _config;
    private readonly ILogger<DaneaOrderService> _log;

    public DaneaOrderService(IConfiguration config, ILogger<DaneaOrderService> log)
    {
        _config = config;
        _log = log;
    }

    public record OrderLine(string CodArticolo, decimal Qta, decimal PrezzoNetto);
    public record OrderResult(int IdDoc, int Num);

    private string ConnStr(bool vecchioArchivio = false)
    {
        string chiave = vecchioArchivio ? "DaneaSync:EftFilePathOld" : "DaneaSync:EftFilePath";
        string filePath = _config[chiave] ?? "";
        if (string.IsNullOrEmpty(filePath))
            throw new InvalidOperationException($"{chiave} non configurato.");

        int serverType = int.TryParse(_config["DaneaSync:FbServerType"], out int st) ? st : 1;
        var csb = new FbConnectionStringBuilder
        {
            Database = filePath,
            ServerType = (FbServerType)serverType,
            UserID = _config["DaneaSync:FbUser"] ?? "SYSDBA",
            Password = _config["DaneaSync:FbPassword"] ?? "masterkey",
            ClientLibrary = _config["Easyfatt:FirebirdClientPath"]
                            ?? Path.Combine(AppContext.BaseDirectory, "Firebird", "fbclient.dll"),
            Charset = "WIN1252",
            WireCrypt = FbWireCrypt.Disabled,
            ConnectionTimeout = 5,
        };
        if (serverType == 0)
        {
            csb.DataSource = _config["DaneaSync:FbDataSource"] ?? "localhost";
            csb.Port = int.TryParse(_config["DaneaSync:FbPort"], out int p) ? p : 3050;
        }
        return csb.ToString();
    }

    private static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Crea l'ordine fornitore in Danea. Il fornitore si risolve per Partita IVA
    /// (fallback: ragione sociale esatta). Gli articoli DEVONO esistere in Atec_PM
    /// (per CodArticolo): in caso contrario errore chiaro, nessuna scrittura.
    /// </summary>
    public OrderResult CreateSupplierOrder(
        string supplierVat, string supplierName, List<OrderLine> lines,
        DateTime? expectedDate, string internalNote)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("Nessuna riga ordine.");

        using var c = new FbConnection(ConnStr());
        c.Open();

        // ── Fornitore (IDAnagr + snapshot per la testata) ──
        var anagr = FindSupplier(c, supplierVat, supplierName)
            ?? throw new InvalidOperationException(
                $"Fornitore «{supplierName}» non trovato in Atec_PM (P.IVA {supplierVat}).");

        // ── Articoli + aliquote ──
        var articles = new List<(OrderLine Line, int IdArticolo, string Desc, string Udm, string CodIva, string CodForn, decimal Perc)>();
        foreach (OrderLine line in lines)
        {
            using var cmd = new FbCommand(@"
                SELECT a.""IDArticolo"", a.""Desc"", a.""Udm"", a.""CodIva"", a.""CodArticoloForn"", i.""PercIva""
                FROM ""TArticoli"" a
                LEFT JOIN ""TIva"" i ON i.""CodIva"" = a.""CodIva""
                WHERE a.""CodArticolo"" = @c", c);
            cmd.Parameters.AddWithValue("@c", line.CodArticolo);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
                throw new InvalidOperationException(
                    $"Articolo «{line.CodArticolo}» non presente in Atec_PM: trasferiscilo prima dal vecchio catalogo.");
            if (r.IsDBNull(5))
                throw new InvalidOperationException(
                    $"Aliquota IVA «{(r.IsDBNull(3) ? "" : r.GetString(3))}» dell'articolo «{line.CodArticolo}» assente in Atec_PM.");
            articles.Add((line, r.GetInt32(0),
                r.IsDBNull(1) ? "" : r.GetString(1).Trim(),
                r.IsDBNull(2) ? "pz" : r.GetString(2).Trim(),
                r.IsDBNull(3) ? "" : r.GetString(3).Trim(),
                r.IsDBNull(4) ? "" : r.GetString(4).Trim(),
                Convert.ToDecimal(r[5])));
        }

        DateTime today = DateTime.Today;
        using var tx = c.BeginTransaction();
        try
        {
            // Numerazione: MAX+1 per tipo E, anno corrente, numerazione base (vuota).
            int num;
            using (var nc = new FbCommand(@"
                SELECT COALESCE(MAX(""Num""), 0) + 1 FROM ""TDocTestate""
                WHERE ""TipoDoc"" = 'E' AND EXTRACT(YEAR FROM ""Data"") = @y
                  AND (""Numeraz"" IS NULL OR ""Numeraz"" = '')", c, tx))
            {
                nc.Parameters.AddWithValue("@y", today.Year);
                num = Convert.ToInt32(nc.ExecuteScalar());
            }

            int idDoc = NextId(c, tx, "TDocTestate__IDDoc");

            // Totali (arrotondamento a 2 decimali come Danea).
            decimal totNetto = 0, totIva = 0;
            var ivaGroups = new Dictionary<string, (decimal Netto, decimal Iva)>();
            foreach (var a in articles)
            {
                decimal netto = R2(a.Line.PrezzoNetto * a.Line.Qta);
                decimal iva = R2(netto * a.Perc / 100m);
                totNetto += netto;
                totIva += iva;
                ivaGroups[a.CodIva] = ivaGroups.TryGetValue(a.CodIva, out var cur)
                    ? (cur.Netto + netto, cur.Iva + iva)
                    : (netto, iva);
            }
            decimal totDoc = totNetto + totIva;

            using (var ins = new FbCommand(@"
                INSERT INTO ""TDocTestate"" (
                    ""IDDoc"", ""TipoDoc"", ""IDAnagr"", ""Magazz"", ""Data"", ""Num"", ""DataDoc"", ""NumDoc"", ""DescDoc"",
                    ""ContrPrev_SoggettoRA"", ""Spese_SoggettoRA"",
                    ""TotNetto"", ""TotIva"", ""TotIvaDetr"", ""TotDoc"", ""TotIvaNonDovuta"", ""TotIvaSplitPaym"",
                    ""RevCharge"", ""TotRitAcconto"", ""TotRitVarie"", ""TotDocNoRit"", ""TotPrezzoAcquisto"", ""TotGuadagno"",
                    ""ImportoSoggettoRA"", ""PercRitAcconto"", ""PercRitAcconto2"", ""AnnoCompetenzaRitVarie"",
                    ""TotEcoContribNetto"", ""IDListino"", ""UsaPrezzoIvato"", ""Includibile"", ""FatturatoDaScorporare"",
                    ""Pagam_AccontoFatturato"", ""Pagam_ShowForzaSaldato"", ""Pagam_ForzaSaldato"", ""Pagam_Migrati"", ""Pagam_Saldato"",
                    ""NoteInterne"",
                    ""Anagr_Nome"", ""Anagr_Indirizzo"", ""Anagr_Cap"", ""Anagr_Citta"", ""Anagr_Prov"", ""Anagr_Nazione"",
                    ""Anagr_CodiceFiscale"", ""Anagr_PartitaIva"",
                    ""Anagr_DestNome"", ""Anagr_DestIndirizzo"", ""Anagr_DestCap"", ""Anagr_DestCitta"", ""Anagr_DestProv"", ""Anagr_DestNazione"",
                    ""StatoOrdine"", ""DataPrevistaConclOrdine"", ""Ord_InclusoInIDDoc_Multi"",
                    ""Rinnovo_Intervallo"", ""IvaPerCassa"", ""IvaDovutaEntroUnAnno"",
                    ""Fidelity_ImportoIvato"", ""Fidelity_Punti"",
                    ""Tmp_RicalcolaTotali"", ""Tmp_SyncTMovMagazz"", ""Tmp_DelMe"")
                VALUES (
                    @IdDoc, 'E', @IdAnagr, 'Principale', @Data, @Num, @Data, @Num, @DescDoc,
                    0, 0,
                    @TotNetto, @TotIva, @TotIva, @TotDoc, 0, 0,
                    0, 0, 0, @TotDoc, 0, 0,
                    0, 0, 0, @Anno,
                    0, 'Forn', 0, 1, 0,
                    0, 0, 0, 0, 0,
                    @NoteInterne,
                    @Nome, @Indirizzo, @Cap, @Citta, @Prov, @Nazione, @Cf, @Piva,
                    @Nome, @Indirizzo, @Cap, @Citta, @Prov, @Nazione,
                    'Conf', @DataPrevista, 0,
                    'Mesi_12', 0, 1,
                    0, 0,
                    0, 0, 0)", c, tx))
            {
                ins.Parameters.AddWithValue("@IdDoc", idDoc);
                ins.Parameters.AddWithValue("@IdAnagr", anagr.IdAnagr);
                ins.Parameters.AddWithValue("@Data", today);
                ins.Parameters.AddWithValue("@Num", num);
                ins.Parameters.AddWithValue("@DescDoc", $"Ordine forn. {num} del {today:d/M/yy}");
                ins.Parameters.AddWithValue("@TotNetto", totNetto);
                ins.Parameters.AddWithValue("@TotIva", totIva);
                ins.Parameters.AddWithValue("@TotDoc", totDoc);
                ins.Parameters.AddWithValue("@Anno", today.Year);
                ins.Parameters.AddWithValue("@NoteInterne", internalNote ?? "");
                ins.Parameters.AddWithValue("@Nome", anagr.Nome);
                ins.Parameters.AddWithValue("@Indirizzo", anagr.Indirizzo);
                ins.Parameters.AddWithValue("@Cap", anagr.Cap);
                ins.Parameters.AddWithValue("@Citta", anagr.Citta);
                ins.Parameters.AddWithValue("@Prov", anagr.Prov);
                ins.Parameters.AddWithValue("@Nazione", anagr.Nazione.Length > 0 ? anagr.Nazione : "Italia");
                ins.Parameters.AddWithValue("@Cf", anagr.CodiceFiscale);
                ins.Parameters.AddWithValue("@Piva", anagr.PartitaIva);
                ins.Parameters.AddWithValue("@DataPrevista", (object?)expectedDate ?? DBNull.Value);
                ins.ExecuteNonQuery();
            }

            foreach (var a in articles)
            {
                int idRiga = NextId(c, tx, "TDocRighe__IDDocRiga");
                decimal nettoRiga = R2(a.Line.PrezzoNetto * a.Line.Qta);
                decimal ivatoUnit = R2(a.Line.PrezzoNetto * (1 + a.Perc / 100m));
                decimal ivatoRiga = R2(nettoRiga * (1 + a.Perc / 100m));

                using (var ins = new FbCommand(@"
                    INSERT INTO ""TDocRighe"" (
                        ""IDDocRiga"", ""IDDoc"", ""IDArticoloScaricato"", ""CodArticolo"", ""CodArticoloForn"",
                        ""Desc"", ""QtaShown"", ""Qta"", ""Udm"", ""PrezzoNetto"", ""PrezzoIvato"", ""RitAcconto"",
                        ""CodIva"", ""ImportoNettoRiga"", ""ImportoIvatoRiga"",
                        ""EcoContribNettoRiga"", ""EcoContribIvatoRiga"", ""MovMagazz"")
                    VALUES (@IdRiga, @IdDoc, @IdArt, @Cod, @CodForn, @Desc, @Qta, @Qta, @Udm,
                            @PrezzoNetto, @PrezzoIvato, 0, @CodIva, @NettoRiga, @IvatoRiga, 0, 0, 1)", c, tx))
                {
                    ins.Parameters.AddWithValue("@IdRiga", idRiga);
                    ins.Parameters.AddWithValue("@IdDoc", idDoc);
                    ins.Parameters.AddWithValue("@IdArt", a.IdArticolo);
                    ins.Parameters.AddWithValue("@Cod", a.Line.CodArticolo);
                    ins.Parameters.AddWithValue("@CodForn", a.CodForn.Length > 0 ? a.CodForn : a.Line.CodArticolo);
                    ins.Parameters.AddWithValue("@Desc", a.Desc);
                    ins.Parameters.AddWithValue("@Qta", a.Line.Qta);
                    ins.Parameters.AddWithValue("@Udm", a.Udm);
                    ins.Parameters.AddWithValue("@PrezzoNetto", a.Line.PrezzoNetto);
                    ins.Parameters.AddWithValue("@PrezzoIvato", ivatoUnit);
                    ins.Parameters.AddWithValue("@CodIva", a.CodIva);
                    ins.Parameters.AddWithValue("@NettoRiga", nettoRiga);
                    ins.Parameters.AddWithValue("@IvatoRiga", ivatoRiga);
                    ins.ExecuteNonQuery();
                }

                // Movimento «in arrivo»: è lui che popola la colonna In arrivo di Danea.
                int idMov = NextId(c, tx, "TMovMagazz__IDMovMagazz");
                using (var ins = new FbCommand(@"
                    INSERT INTO ""TMovMagazz"" (
                        ""IDMovMagazz"", ""IDArticolo"", ""Magazz"", ""IDAnagr"", ""Data"",
                        ""QtaInArrivo"", ""PrezzoNetto"", ""IDDocRiga"", ""Tmp_DelMe"")
                    VALUES (@IdMov, @IdArt, 'Principale', @IdAnagr, @Data, @Qta, @PrezzoNetto, @IdRiga, 0)", c, tx))
                {
                    ins.Parameters.AddWithValue("@IdMov", idMov);
                    ins.Parameters.AddWithValue("@IdArt", a.IdArticolo);
                    ins.Parameters.AddWithValue("@IdAnagr", anagr.IdAnagr);
                    ins.Parameters.AddWithValue("@Data", today);
                    ins.Parameters.AddWithValue("@Qta", a.Line.Qta);
                    ins.Parameters.AddWithValue("@PrezzoNetto", a.Line.PrezzoNetto);
                    ins.Parameters.AddWithValue("@IdRiga", idRiga);
                    ins.ExecuteNonQuery();
                }
            }

            foreach (var g in ivaGroups)
            {
                int idIva = NextId(c, tx, "TDocIva__IDDocIva");
                using var ins = new FbCommand(@"
                    INSERT INTO ""TDocIva"" (""IDDocIva"", ""IDDoc"", ""CodIva"", ""ImportoNetto"", ""Iva"", ""IvaDetr"")
                    VALUES (@IdIva, @IdDoc, @CodIva, @Netto, @Iva, @Iva)", c, tx);
                ins.Parameters.AddWithValue("@IdIva", idIva);
                ins.Parameters.AddWithValue("@IdDoc", idDoc);
                ins.Parameters.AddWithValue("@CodIva", g.Key);
                ins.Parameters.AddWithValue("@Netto", g.Value.Netto);
                ins.Parameters.AddWithValue("@Iva", g.Value.Iva);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
            _log.LogInformation("[DaneaOrder] Ordine fornitore {Num}/{Year} creato (IDDoc {IdDoc}, {Lines} righe, tot {Tot})",
                num, today.Year, idDoc, articles.Count, totDoc);
            return new OrderResult(idDoc, num);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private sealed record Anagrafica(int IdAnagr, string Nome, string Indirizzo, string Cap,
        string Citta, string Prov, string Nazione, string CodiceFiscale, string PartitaIva);

    private static Anagrafica? FindSupplier(FbConnection c, string vat, string name)
    {
        const string select = @"
            SELECT FIRST 1 ""IDAnagr"", ""Nome"", ""Indirizzo"", ""Cap"", ""Citta"", ""Prov"",
                   ""Nazione"", ""CodiceFiscale"", ""PartitaIva""
            FROM ""TAnagrafica"" WHERE ""Fornitore"" = 1 AND ";

        foreach ((string where, string param) in new[]
        {
            (@"TRIM(""PartitaIva"") = @p", vat?.Trim() ?? ""),
            (@"TRIM(""Nome"") = @p", name?.Trim() ?? ""),
        })
        {
            if (param.Length == 0) continue;
            using var cmd = new FbCommand(select + where, c);
            cmd.Parameters.AddWithValue("@p", param);
            using var r = cmd.ExecuteReader();
            if (r.Read())
                return new Anagrafica(r.GetInt32(0),
                    S(r, 1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), S(r, 8));
        }
        return null;

        static string S(FbDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i).Trim();
    }

    /// <summary>Alloca il prossimo id dal generatore Firebird (atomico, fuori dal controllo tx).</summary>
    private static int NextId(FbConnection c, FbTransaction tx, string generator)
    {
        using var cmd = new FbCommand($"SELECT GEN_ID(\"{generator}\", 1) FROM RDB$DATABASE", c, tx);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Legge un ordine fornitore da Atec_PM per il rendering nel client web
    /// (testata + righe + riepilogo IVA). Sola lettura, nessuna transazione.
    /// Ritorna null se l'IDDoc non esiste o non è un ordine fornitore (TipoDoc 'E').
    /// </summary>
    public ATEC.PM.Shared.DTOs.DaneaOrderView? GetOrder(int idDoc)
    {
        using var c = new FbConnection(ConnStr());
        c.Open();
        return CaricaOrdine(c, idDoc);
    }

    /// <summary>
    /// Rif. Danea scritto a mano («123», «123/26», «123/2026») → numero e anno.
    /// La numerazione Danea riparte ogni anno, quindi l'anno conta quando c'è.
    /// </summary>
    public static bool TryParseRif(string? rif, out int num, out int? anno)
    {
        num = 0;
        anno = null;
        // [0-9] e non \d: nel regex .NET \d accetta anche le cifre Unicode (es. «１２３»
        // da un copia-incolla) che poi int.Parse rifiuta — e un Try* non deve lanciare.
        var m = System.Text.RegularExpressions.Regex.Match(
            (rif ?? "").Trim(), @"^([0-9]{1,6})(?:\s*/\s*([0-9]{2}|[0-9]{4}))?$");
        if (!m.Success) return false;
        num = int.Parse(m.Groups[1].Value);
        if (m.Groups[2].Success)
        {
            int a = int.Parse(m.Groups[2].Value);
            anno = a < 100 ? 2000 + a : a;
        }
        return num > 0;
    }

    /// <summary>
    /// Cerca un ordine fornitore per numero (e anno, se noto) nell'archivio scelto:
    /// serve durante la migrazione, quando un Rif. Danea scritto a mano può puntare
    /// al vecchio archivio. Senza anno si prende il documento più recente.
    /// </summary>
    public ATEC.PM.Shared.DTOs.DaneaOrderView? GetOrderByNumero(int num, int? anno, bool vecchioArchivio)
    {
        using var c = new FbConnection(ConnStr(vecchioArchivio));
        c.Open();

        int? idDoc = TrovaIdDoc(c, num, anno);
        return idDoc.HasValue ? CaricaOrdine(c, idDoc.Value) : null;
    }

    /// <summary>
    /// C'è un ordine fornitore con questo numero nell'archivio scelto? Serve al
    /// controllo di ambiguità fra archivio attuale e vecchio durante la migrazione.
    /// </summary>
    public bool EsisteOrdine(int num, int? anno, bool vecchioArchivio)
    {
        using var c = new FbConnection(ConnStr(vecchioArchivio));
        c.Open();
        return TrovaIdDoc(c, num, anno).HasValue;
    }

    private static int? TrovaIdDoc(FbConnection c, int num, int? anno)
    {
        // Danea riparte da 1 per ogni serie di numerazione («Numeraz»): a parità di
        // numero si PREFERISCE la serie base (vuota, quella che usa ATEC PM), ma le
        // serie con suffisso non si escludono — nel VECCHIO archivio gli ordini 2026
        // stanno proprio in una serie nominata, e il «123/26» scritto a mano è quello.
        string sql = @"
            SELECT FIRST 1 ""IDDoc"" FROM ""TDocTestate""
            WHERE ""TipoDoc"" = 'E' AND ""Num"" = @Num" +
            (anno.HasValue ? @" AND EXTRACT(YEAR FROM ""Data"") = @Anno" : "") +
            @" ORDER BY CASE WHEN ""Numeraz"" IS NULL OR ""Numeraz"" = '' THEN 0 ELSE 1 END, ""Data"" DESC";
        using var cmd = new FbCommand(sql, c);
        cmd.Parameters.AddWithValue("@Num", num);
        if (anno.HasValue) cmd.Parameters.AddWithValue("@Anno", anno.Value);
        var res = cmd.ExecuteScalar();
        if (res == null || res == DBNull.Value) return null;
        return Convert.ToInt32(res);
    }

    private static ATEC.PM.Shared.DTOs.DaneaOrderView? CaricaOrdine(FbConnection c, int idDoc)
    {
        ATEC.PM.Shared.DTOs.DaneaOrderView view;
        using (var cmd = new FbCommand(@"
            SELECT ""IDDoc"", ""Num"", ""Data"", ""DescDoc"", ""StatoOrdine"", ""Magazz"",
                   ""DataPrevistaConclOrdine"", ""NoteInterne"",
                   ""Anagr_Nome"", ""Anagr_Indirizzo"", ""Anagr_Cap"", ""Anagr_Citta"",
                   ""Anagr_Prov"", ""Anagr_Nazione"", ""Anagr_PartitaIva"",
                   ""TotNetto"", ""TotIva"", ""TotDoc""
            FROM ""TDocTestate""
            WHERE ""IDDoc"" = @Id AND ""TipoDoc"" = 'E'", c))
        {
            cmd.Parameters.AddWithValue("@Id", idDoc);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            view = new ATEC.PM.Shared.DTOs.DaneaOrderView
            {
                IdDoc = r.GetInt32(0),
                Num = r.IsDBNull(1) ? 0 : Convert.ToInt32(r[1]),
                Date = r.IsDBNull(2) ? null : r.GetDateTime(2),
                DescDoc = S(r, 3),
                OrderStatus = S(r, 4),
                Warehouse = S(r, 5),
                ExpectedDate = r.IsDBNull(6) ? null : r.GetDateTime(6),
                InternalNote = S(r, 7),
                SupplierName = S(r, 8),
                SupplierAddress = S(r, 9),
                SupplierZip = S(r, 10),
                SupplierCity = S(r, 11),
                SupplierProvince = S(r, 12),
                SupplierCountry = S(r, 13),
                SupplierVat = S(r, 14),
                TotNet = r.IsDBNull(15) ? 0 : Convert.ToDecimal(r[15]),
                TotVat = r.IsDBNull(16) ? 0 : Convert.ToDecimal(r[16]),
                TotDoc = r.IsDBNull(17) ? 0 : Convert.ToDecimal(r[17]),
            };
        }

        using (var cmd = new FbCommand(@"
            SELECT ""CodArticolo"", ""CodArticoloForn"", ""Desc"", ""Qta"", ""Udm"",
                   ""PrezzoNetto"", ""CodIva"", ""ImportoNettoRiga"", ""ImportoIvatoRiga""
            FROM ""TDocRighe""
            WHERE ""IDDoc"" = @Id
            ORDER BY ""IDDocRiga""", c))
        {
            cmd.Parameters.AddWithValue("@Id", idDoc);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                view.Rows.Add(new ATEC.PM.Shared.DTOs.DaneaOrderRowView
                {
                    Code = S(r, 0),
                    SupplierCode = S(r, 1),
                    Description = S(r, 2),
                    Quantity = r.IsDBNull(3) ? 0 : Convert.ToDecimal(r[3]),
                    Unit = S(r, 4),
                    UnitPrice = r.IsDBNull(5) ? 0 : Convert.ToDecimal(r[5]),
                    VatCode = S(r, 6),
                    NetAmount = r.IsDBNull(7) ? 0 : Convert.ToDecimal(r[7]),
                    GrossAmount = r.IsDBNull(8) ? 0 : Convert.ToDecimal(r[8]),
                });
        }

        using (var cmd = new FbCommand(@"
            SELECT ""CodIva"", ""ImportoNetto"", ""Iva""
            FROM ""TDocIva""
            WHERE ""IDDoc"" = @Id
            ORDER BY ""IDDocIva""", c))
        {
            cmd.Parameters.AddWithValue("@Id", idDoc);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                view.VatSummary.Add(new ATEC.PM.Shared.DTOs.DaneaOrderVatView
                {
                    VatCode = S(r, 0),
                    NetAmount = r.IsDBNull(1) ? 0 : Convert.ToDecimal(r[1]),
                    VatAmount = r.IsDBNull(2) ? 0 : Convert.ToDecimal(r[2]),
                });
        }

        return view;

        static string S(FbDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i).Trim();
    }
}
