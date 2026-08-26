using System.Data;
using FirebirdSql.Data.FirebirdClient;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

// F2 del piano PIANO-MIGRAZIONE-DANEA-ATEC.md: trasferimento selettivo degli articoli
// dal vecchio archivio Danea (EftFilePathOld, SOLO lettura) al nuovo Atec_PM (EftFilePath).
//
// Regole imparate in F0/F1 (NON derogare):
//  - Charset WIN1252 (le colonne Danea sono tutte WIN1252): round-trip fedele. MAI NONE
//    in scrittura (mojibake + "string right truncation"), MAI ISO8859_1 (€ non translittera).
//  - IDArticolo PRESERVATO (il target parte vuoto): i riferimenti restano 1:1 col vecchio
//    e catalog_items.easyfatt_id resta valido senza attendere il sync. Dopo ogni lotto il
//    generatore TArticoli__IDArticolo viene riallineato al MAX.
//  - Copia dinamica delle colonne non calcolate comuni ai due schemi (come il bootstrap F1).
//  - Niente giacenze/movimenti/documenti. Con l'articolo viaggiano TArticoliForn e
//    TArticoliCodBarre (stesso IDArticolo). TDiba (distinte base) esclusa per ora.
//  - Immagini: file esterni in "<archivio> - Allegati\Prod[2]" (UNC in config), riferiti
//    per nome in PathImmagine_Import; si copia il .jpg + l'eventuale " Small.bmp".
public class DaneaMigrationService
{
    private readonly IConfiguration _config;
    private readonly ILogger<DaneaMigrationService> _log;
    private readonly NetworkShareConnector _share;

    public DaneaMigrationService(
        IConfiguration config, ILogger<DaneaMigrationService> log, NetworkShareConnector share)
    {
        _config = config;
        _log = log;
        _share = share;
    }

    private string ConnStr(bool old)
    {
        string key = old ? "DaneaSync:EftFilePathOld" : "DaneaSync:EftFilePath";
        string filePath = _config[key] ?? "";
        if (string.IsNullOrEmpty(filePath))
            throw new InvalidOperationException($"{key} non configurato.");

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

    private string? AllegatiDir(bool old) =>
        _config[old ? "DaneaSync:AllegatiPathOld" : "DaneaSync:AllegatiPathNew"];

    /// <summary>
    /// Apre la sessione SMB autenticata verso la share degli allegati (vedi
    /// <see cref="NetworkShareConnector"/>). Il servizio gira come account locale, che il
    /// server della share non conosce: senza credenziali esplicite ogni accesso ai file
    /// finisce in "accesso negato" anche se il database Danea risponde benissimo.
    /// Restituisce il messaggio d'errore, o null se va tutto bene o non c'e' nulla da fare.
    /// </summary>
    private string? EnsureAllegatiShare()
    {
        string? utente = _config["DaneaSync:SmbUser"];
        if (string.IsNullOrWhiteSpace(utente)) return null;   // non configurate: come prima
        string? password = _config["DaneaSync:SmbPassword"];

        string? errore = null;
        foreach (bool old in new[] { true, false })
            errore ??= _share.Connect(AllegatiDir(old), utente, password);
        return errore;
    }

    /// <summary>
    /// Perche' le cartelle immagini non si raggiungono: la pagina mostrava solo il badge
    /// rosso, e la causa (sessione SMB anonima rifiutata) si scopriva solo sul server.
    /// </summary>
    private string DiagnosiImmagini(string? shareError, string? src, string? dst, bool srcOk, bool dstOk)
    {
        if (!string.IsNullOrEmpty(shareError)) return shareError;
        if (srcOk && dstOk) return "";
        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
            return "Cartelle allegati non configurate (DaneaSync:AllegatiPathOld/New).";

        bool rete = NetworkShareConnector.ShareRoot(src) != null ||
                    NetworkShareConnector.ShareRoot(dst) != null;
        bool credenziali = !string.IsNullOrWhiteSpace(_config["DaneaSync:SmbUser"]);
        string quali = !srcOk && !dstOk ? "sorgente e destinazione"
                     : !srcOk ? "sorgente" : "destinazione";

        return rete && !credenziali
            ? $"Share di rete ({quali}) non raggiungibile dal servizio e nessuna credenziale " +
              @"configurata: impostare DaneaSync:SmbUser/SmbPassword con deploy\imposta-credenziali-share.ps1."
            : $"Cartella allegati {quali} non raggiungibile: verificare percorso e permessi.";
    }

    // ── STATO ─────────────────────────────────────────────────────────────

    public DaneaMigrationStatus GetStatus()
    {
        string? shareError = EnsureAllegatiShare();

        using var src = new FbConnection(ConnStr(old: true));
        src.Open();
        using var dst = new FbConnection(ConnStr(old: false));
        dst.Open();

        var filters = ReadFilterOptions(src);
        string? srcImg = AllegatiDir(old: true);
        string? dstImg = AllegatiDir(old: false);
        var status = new DaneaMigrationStatus
        {
            OldArticles = Count(src, "TArticoli"),
            NewArticles = Count(dst, "TArticoli"),
            ImagesSourceReachable = srcImg != null && Directory.Exists(Path.Combine(srcImg, "Prod")),
            // Il target può non esistere ancora: basta che esista la cartella Allegati padre o il suo genitore.
            ImagesTargetReachable = dstImg != null &&
                (Directory.Exists(dstImg) || Directory.Exists(Path.GetDirectoryName(dstImg.TrimEnd('\\')) ?? "")),
            OldArchive = _config["DaneaSync:EftFilePathOld"] ?? "",
            NewArchive = _config["DaneaSync:EftFilePath"] ?? "",
            Categories = filters.Categories,
            Subcategories = filters.Subcategories,
            Suppliers = filters.Suppliers,
            Manufacturers = filters.Manufacturers,
        };
        status.ImagesError = DiagnosiImmagini(
            shareError, srcImg, dstImg, status.ImagesSourceReachable, status.ImagesTargetReachable);
        return status;
    }

    public DaneaFilterOptions GetFilterOptions()
    {
        using var src = new FbConnection(ConnStr(old: true));
        src.Open();
        return ReadFilterOptions(src);
    }

    private static DaneaFilterOptions ReadFilterOptions(FbConnection src)
    {
        var categories = new List<string>();
        using (var cmd = new FbCommand(@"
            SELECT DISTINCT TRIM(a.""NomeCategoria"")
            FROM ""TArticoli"" a
            WHERE a.""NomeCategoria"" IS NOT NULL AND TRIM(a.""NomeCategoria"") <> ''
            ORDER BY 1", src))
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string cat = Convert.ToString(r[0]) ?? "";
                if (!string.IsNullOrWhiteSpace(cat)) categories.Add(cat.Trim());
            }
        }

        var subcategories = new List<string>();
        using (var cmd = new FbCommand(@"
            SELECT DISTINCT TRIM(a.""NomeSottocategoria"")
            FROM ""TArticoli"" a
            WHERE a.""NomeSottocategoria"" IS NOT NULL AND TRIM(a.""NomeSottocategoria"") <> ''
            ORDER BY 1", src))
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string sub = Convert.ToString(r[0]) ?? "";
                if (!string.IsNullOrWhiteSpace(sub)) subcategories.Add(sub.Trim());
            }
        }

        var suppliers = new List<string>();
        using (var cmd = new FbCommand(@"
            SELECT DISTINCT TRIM(f.""Nome"")
            FROM ""TArticoli"" a
            JOIN ""TAnagrafica"" f ON f.""IDAnagr"" = a.""IDFornitore""
            WHERE f.""Nome"" IS NOT NULL AND TRIM(f.""Nome"") <> ''
            ORDER BY 1", src))
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string forn = Convert.ToString(r[0]) ?? "";
                if (!string.IsNullOrWhiteSpace(forn)) suppliers.Add(forn.Trim());
            }
        }

        var manufacturers = new List<string>();
        using (var cmd = new FbCommand(@"
            SELECT DISTINCT TRIM(a.""Produttore"")
            FROM ""TArticoli"" a
            WHERE a.""Produttore"" IS NOT NULL AND TRIM(a.""Produttore"") <> ''
            ORDER BY 1", src))
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string prod = Convert.ToString(r[0]) ?? "";
                if (!string.IsNullOrWhiteSpace(prod)) manufacturers.Add(prod.Trim());
            }
        }

        return new DaneaFilterOptions
        {
            Categories = categories,
            Subcategories = subcategories,
            Suppliers = suppliers,
            Manufacturers = manufacturers
        };
    }

    // ── LISTA ARTICOLI DEL VECCHIO ────────────────────────────────────────

    public PagedResult<DaneaOldArticle> GetOldArticles(
        int page, int pageSize, string? search, bool onlyMissing,
        string? codArticolo = null, string? descrizione = null, string? categoria = null,
        string? sottocategoria = null, string? fornitore = null, string? produttore = null,
        string? extra1 = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        using var src = new FbConnection(ConnStr(old: true));
        src.Open();
        using var dst = new FbConnection(ConnStr(old: false));
        dst.Open();

        var transferred = CodesInNew(dst);

        // Passo 1: id+codice filtrati sul vecchio (leggero), filtro "da trasferire" in memoria
        // (il NOT EXISTS cross-database non esiste).
        var ids = new List<(int Id, string Code)>();
        var clauses = new List<string>();
        using (var cmd = new FbCommand("", src))
        {
            // CONTAINING = case-insensitive, niente wildcard da comporre.
            void AddContains(string column, string? value, string param)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                clauses.Add($"{column} CONTAINING @{param}");
                cmd.Parameters.AddWithValue("@" + param, value.Trim());
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                clauses.Add(@"(a.""CodArticolo"" CONTAINING @s OR a.""Desc"" CONTAINING @s
                           OR a.""Extra1"" CONTAINING @s OR f.""Nome"" CONTAINING @s)");
                cmd.Parameters.AddWithValue("@s", search.Trim());
            }
            AddContains(@"a.""CodArticolo""", codArticolo, "fCod");
            AddContains(@"a.""Desc""", descrizione, "fDesc");
            AddContains(@"a.""NomeCategoria""", categoria, "fCat");
            AddContains(@"a.""NomeSottocategoria""", sottocategoria, "fSubCat");
            AddContains(@"f.""Nome""", fornitore, "fForn");
            AddContains(@"a.""Produttore""", produttore, "fProd");
            AddContains(@"a.""Extra1""", extra1, "fExtra");

            string where = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";
            cmd.CommandText = $@"
                SELECT a.""IDArticolo"", a.""CodArticolo""
                FROM ""TArticoli"" a
                LEFT JOIN ""TAnagrafica"" f ON f.""IDAnagr"" = a.""IDFornitore""
                {where}
                ORDER BY a.""CodArticolo""";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string code = (Convert.ToString(r[1]) ?? "").Trim();
                if (onlyMissing && transferred.Contains(code)) continue;
                ids.Add((Convert.ToInt32(r[0]), code));
            }
        }

        int total = ids.Count;
        var pageIds = ids.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToList();
        var items = new List<DaneaOldArticle>();
        if (pageIds.Count > 0)
        {
            string inList = string.Join(",", pageIds);
            using var cmd = new FbCommand($@"
                SELECT a.""IDArticolo"", a.""CodArticolo"", a.""Desc"", a.""NomeCategoria"",
                       a.""NomeSottocategoria"",
                       a.""Udm"", f.""Nome"", a.""Produttore"", a.""PrezzoNettoForn"", a.""Extra1"",
                       a.""PathImmagine_Import""
                FROM ""TArticoli"" a
                LEFT JOIN ""TAnagrafica"" f ON f.""IDAnagr"" = a.""IDFornitore""
                WHERE a.""IDArticolo"" IN ({inList})
                ORDER BY a.""CodArticolo""", src);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string code = Str(r, 1);
                items.Add(new DaneaOldArticle
                {
                    IdArticolo = Convert.ToInt32(r[0]),
                    CodArticolo = code,
                    Descrizione = Str(r, 2),
                    Categoria = Str(r, 3),
                    Sottocategoria = Str(r, 4),
                    Udm = Str(r, 5),
                    Fornitore = Str(r, 6),
                    Produttore = Str(r, 7),
                    PrezzoForn = r.IsDBNull(8) ? 0 : Convert.ToDecimal(r[8]),
                    Extra1 = Str(r, 9),
                    HasImage = Str(r, 10).Length > 0,
                    Transferred = transferred.Contains(code),
                });
            }
        }

        return new PagedResult<DaneaOldArticle>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            HasMore = page * pageSize < total,
        };
    }

    // ── TRASFERIMENTO ─────────────────────────────────────────────────────

    public DaneaTransferReport Transfer(List<int> articleIds)
    {
        // Sessione autenticata una volta per lotto: le immagini si copiano fuori transazione,
        // ma se la share non si apre gli articoli passano lo stesso (senza foto).
        string? shareError = EnsureAllegatiShare();

        using var src = new FbConnection(ConnStr(old: true));
        src.Open();
        using var dst = new FbConnection(ConnStr(old: false));
        dst.Open();

        var transferred = CodesInNew(dst);
        // Metadati PRIMA di aprire transazioni: FbCommand su una connessione con
        // transazione pendente esige il tx esplicito (il driver rifiuta i comandi nudi).
        var artCols = CommonColumns(src, dst, "TArticoli");
        var fornCols = CommonColumns(src, dst, "TArticoliForn");
        var barcodeCols = CommonColumns(src, dst, "TArticoliCodBarre");
        var report = new DaneaTransferReport();
        long maxId = 0;

        foreach (int id in articleIds.Distinct())
        {
            var res = new DaneaTransferResult { IdArticolo = id };
            report.Results.Add(res);
            try
            {
                var row = ReadRow(src, "TArticoli", artCols, "IDArticolo", id);
                if (row == null)
                {
                    res.Outcome = "error";
                    res.Error = "Articolo non trovato nel vecchio archivio.";
                    report.Errors++;
                    continue;
                }
                string code = (Convert.ToString(row["CodArticolo"]) ?? "").Trim();
                res.CodArticolo = code;
                if (transferred.Contains(code))
                {
                    res.Outcome = "skipped";
                    report.Skipped++;
                    continue;
                }

                using (var tx = dst.BeginTransaction())
                {
                    InsertRow(dst, tx, "TArticoli", row);
                    CopyChildRows(src, dst, tx, "TArticoliForn", fornCols, id);
                    CopyChildRows(src, dst, tx, "TArticoliCodBarre", barcodeCols, id);
                    tx.Commit();
                }
                transferred.Add(code);
                maxId = Math.Max(maxId, id);

                // Immagini FUORI transazione: un file mancante non annulla l'articolo.
                string img = (Convert.ToString(row.GetValueOrDefault("PathImmagine_Import")) ?? "").Trim();
                if (img.Length > 0)
                    (res.ImagesCopied, res.ImageWarning) = CopyImages(img, shareError);

                report.Ok++;
                report.ImagesCopied += res.ImagesCopied;
            }
            catch (Exception ex)
            {
                res.Outcome = "error";
                res.Error = ex.Message;
                report.Errors++;
                _log.LogWarning("[DaneaMigration] Trasferimento articolo {Id} fallito: {Msg}", id, ex.Message);
            }
        }

        // Generatore sempre sopra il massimo ID presente (gli articoli creati in Danea
        // sul nuovo archivio non devono collidere con quelli trasferiti).
        if (maxId > 0)
        {
            using var cur = new FbCommand("SELECT GEN_ID(\"TArticoli__IDArticolo\", 0) FROM RDB$DATABASE", dst);
            long current = Convert.ToInt64(cur.ExecuteScalar());
            if (maxId > current)
            {
                using var gen = new FbCommand($"SET GENERATOR \"TArticoli__IDArticolo\" TO {maxId}", dst);
                gen.ExecuteNonQuery();
            }
        }

        _log.LogInformation("[DaneaMigration] Trasferiti {Ok}, saltati {Skipped}, errori {Errors}, immagini {Img}",
            report.Ok, report.Skipped, report.Errors, report.ImagesCopied);
        return report;
    }

    // ── IMMAGINI ──────────────────────────────────────────────────────────

    /// <summary>Copia il jpg riferito da PathImmagine_Import + l'eventuale miniatura " Small.bmp".</summary>
    private (int Copied, string Warning) CopyImages(string fileName, string? shareError)
    {
        string? srcBase = AllegatiDir(old: true);
        string? dstBase = AllegatiDir(old: false);
        if (string.IsNullOrEmpty(srcBase) || string.IsNullOrEmpty(dstBase))
            return (0, "Cartelle allegati non configurate (AllegatiPathOld/New): immagine non copiata.");
        if (!string.IsNullOrEmpty(shareError))
            return (0, $"Share immagini non accessibile: {shareError}");

        string dstProd = Path.Combine(dstBase, "Prod");
        try
        {
            Directory.CreateDirectory(dstProd);
        }
        catch (Exception ex)
        {
            return (0, $"Cartella destinazione non creabile: {ex.Message}");
        }

        var names = new List<string> { fileName };
        string thumb = Path.GetFileNameWithoutExtension(fileName) + " Small.bmp";
        names.Add(thumb);

        int copied = 0;
        var missing = new List<string>();
        foreach (string name in names)
        {
            string? found = null;
            foreach (string sub in new[] { "Prod", "Prod2" })
            {
                string candidate = Path.Combine(srcBase, sub, name);
                if (File.Exists(candidate)) { found = candidate; break; }
            }
            if (found == null)
            {
                // La miniatura può legittimamente mancare; il jpg principale no.
                if (name == fileName) missing.Add(name);
                continue;
            }
            string target = Path.Combine(dstProd, name);
            if (!File.Exists(target))
            {
                File.Copy(found, target);
                copied++;
            }
        }
        return (copied, missing.Count > 0 ? $"File immagine non trovato: {missing[0]}" : "");
    }

    // ── HELPER COPIA GENERICA (pattern bootstrap F1) ──────────────────────

    private static int Count(FbConnection c, string table)
    {
        using var cmd = new FbCommand($"SELECT COUNT(*) FROM \"{table}\"", c);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static HashSet<string> CodesInNew(FbConnection dst)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new FbCommand("SELECT \"CodArticolo\" FROM \"TArticoli\"", dst);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            set.Add((Convert.ToString(r[0]) ?? "").Trim());
        return set;
    }

    private static List<string> Columns(FbConnection c, string table)
    {
        var list = new List<string>();
        using var cmd = new FbCommand(@"
            SELECT TRIM(rf.RDB$FIELD_NAME)
            FROM RDB$RELATION_FIELDS rf
            JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
            WHERE rf.RDB$RELATION_NAME = @t AND f.RDB$COMPUTED_BLR IS NULL
            ORDER BY rf.RDB$FIELD_POSITION", c);
        cmd.Parameters.AddWithValue("@t", table);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Convert.ToString(r[0]) ?? "");
        return list;
    }

    private static List<string> CommonColumns(FbConnection src, FbConnection dst, string table)
    {
        var dstCols = Columns(dst, table);
        return Columns(src, table).Where(dstCols.Contains).ToList();
    }

    private static Dictionary<string, object>? ReadRow(
        FbConnection c, string table, List<string> cols, string keyCol, int keyValue)
    {
        string colList = string.Join(", ", cols.Select(x => $"\"{x}\""));
        using var cmd = new FbCommand($"SELECT {colList} FROM \"{table}\" WHERE \"{keyCol}\" = @k", c);
        cmd.Parameters.AddWithValue("@k", keyValue);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < cols.Count; i++)
            row[cols[i]] = r.IsDBNull(i) ? DBNull.Value : r[i];
        return row;
    }

    private static void InsertRow(FbConnection c, FbTransaction tx, string table, Dictionary<string, object> row)
    {
        var cols = row.Keys.ToList();
        string colList = string.Join(", ", cols.Select(x => $"\"{x}\""));
        string parList = string.Join(", ", cols.Select((_, i) => "@p_" + i));
        using var cmd = new FbCommand($"INSERT INTO \"{table}\" ({colList}) VALUES ({parList})", c, tx);
        for (int i = 0; i < cols.Count; i++)
            cmd.Parameters.AddWithValue("@p_" + i, row[cols[i]]);
        cmd.ExecuteNonQuery();
    }

    private static void CopyChildRows(
        FbConnection src, FbConnection dst, FbTransaction tx, string table, List<string> cols, int idArticolo)
    {
        if (!cols.Contains("IDArticolo")) return;

        string colList = string.Join(", ", cols.Select(x => $"\"{x}\""));
        using var rc = new FbCommand($"SELECT {colList} FROM \"{table}\" WHERE \"IDArticolo\" = @id", src);
        rc.Parameters.AddWithValue("@id", idArticolo);
        using var r = rc.ExecuteReader();
        string parList = string.Join(", ", cols.Select((_, i) => "@p_" + i));
        while (r.Read())
        {
            using var ins = new FbCommand($"INSERT INTO \"{table}\" ({colList}) VALUES ({parList})", dst, tx);
            for (int i = 0; i < cols.Count; i++)
                ins.Parameters.AddWithValue("@p_" + i, r.IsDBNull(i) ? DBNull.Value : r[i]);
            ins.ExecuteNonQuery();
        }
    }

    private static string Str(IDataReader r, int i) =>
        r.IsDBNull(i) ? "" : (Convert.ToString(r[i]) ?? "").Trim();
}
