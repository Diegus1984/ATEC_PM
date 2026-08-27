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
//    generatore TArticoli__IDArticolo viene riallineato al MAX. I due archivi pero'
//    avanzano i contatori in parallelo (#129): se l'ID e' gia' occupato in Atec_PM
//    l'articolo entra con un ID nuovo dal generatore, mai sovrascrivendo.
//  - Fornitore al seguito (#129): l'anagrafica riferita dall'articolo che in Atec_PM non
//    esiste ancora si copia nella stessa transazione; se l'ID e' occupato da un'anagrafica
//    DIVERSA (stessi contatori paralleli) si copia con ID nuovo e si rimappano i riferimenti.
//  - Copia dinamica delle colonne non calcolate comuni ai due schemi (come il bootstrap F1).
//  - Niente giacenze/movimenti/documenti. Con l'articolo viaggiano TArticoliForn e
//    TArticoliCodBarre (stesso IDArticolo). TDiba (distinte base) esclusa per ora.
//  - Immagini: file esterni in "<archivio> - Allegati\Prod[2]" (UNC in config), riferiti
//    per nome in PathImmagine_Import; si copia il .jpg + l'eventuale " Small.bmp".
public class DaneaMigrationService
{
    /// <summary>Errore non bloccante per il ripescaggio: l'articolo non esiste piu' nel
    /// vecchio (cancellato dopo la lettura degli ID) — il cursore puo' superarlo.</summary>
    public const string ErroreArticoloAssente = "Articolo non trovato nel vecchio archivio.";

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
        string? extra1 = null, bool recentFirst = false)
    {
        // #129: IDArticolo discendente = ordine di creazione — gli articoli codificati
        // nel vecchio archivio DOPO la migrazione finiscono in cima (TArticoli non ha date).
        string orderBy = recentFirst ? @"a.""IDArticolo"" DESC" : @"a.""CodArticolo""";
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
                ORDER BY {orderBy}";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string code = (Convert.ToString(r[1]) ?? "").Trim();
                if (onlyMissing && transferred.ContainsKey(code)) continue;
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
                ORDER BY {orderBy}", src);
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
                    Transferred = transferred.ContainsKey(code),
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

    /// <summary>
    /// Ripescaggio (#129): MAX(IDArticolo) del vecchio archivio e gli ID oltre il cursore,
    /// in ordine crescente. Con cursore null si legge solo il MAX (serve a fissare lo
    /// spartiacque al primo giro senza toccare lo storico).
    /// </summary>
    public (long MaxId, List<int> NewIds) OldArticlesAfter(long? lastSeenId)
    {
        using var src = new FbConnection(ConnStr(old: true));
        src.Open();
        long max = MaxId(src, "TArticoli", "IDArticolo");
        var ids = new List<int>();
        if (lastSeenId.HasValue && max > lastSeenId.Value)
        {
            using var cmd = new FbCommand(
                "SELECT \"IDArticolo\" FROM \"TArticoli\" WHERE \"IDArticolo\" > @c ORDER BY \"IDArticolo\"", src);
            cmd.Parameters.AddWithValue("@c", lastSeenId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(Convert.ToInt32(r[0]));
        }
        return (max, ids);
    }

    // ── TRASFERIMENTO ─────────────────────────────────────────────────────

    /// <summary>Un solo trasferimento alla volta: griglia e ripescaggio (#129) leggono le
    /// snapshot (codici, nomi anagrafiche) a inizio lotto — due lotti intrecciati possono
    /// duplicare articolo o fornitore SENZA violare nessuna PK, grazie alla rimappatura.</summary>
    private static readonly SemaphoreSlim TransferGate = new(1, 1);

    public DaneaTransferReport Transfer(List<int> articleIds)
    {
        if (!TransferGate.Wait(0))
            throw new InvalidOperationException(
                "Un altro trasferimento e' gia' in corso (griglia o ripescaggio automatico): " +
                "riprovare tra qualche istante.");
        try
        {
            return TransferCore(articleIds);
        }
        finally
        {
            TransferGate.Release();
        }
    }

    private DaneaTransferReport TransferCore(List<int> articleIds)
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
        var anagrCols = CommonColumns(src, dst, "TAnagrafica");
        bool anagrGenOk = GeneratorExists(dst, "TAnagrafica__IDAnagr");
        // PK di riga delle tabelle figlie: si assegna sempre nuova dal generatore, cosi'
        // non collide con le righe nate in Atec_PM (nessuno referenzia quelle PK).
        var childMeta = new Dictionary<string, (string? PkCol, bool PkGenOk)>();
        foreach (string table in new[] { "TArticoliForn", "TArticoliCodBarre" })
        {
            string? pk = SingleIntPk(dst, table);
            bool genOk = pk != null && !pk.Equals("IDArticolo", StringComparison.OrdinalIgnoreCase)
                         && GeneratorExists(dst, $"{table}__{pk}");
            if (pk != null && !genOk && !pk.Equals("IDArticolo", StringComparison.OrdinalIgnoreCase))
                _log.LogWarning("[DaneaMigration] Generatore {Table}__{Pk} non trovato: " +
                                "le PK figlie passano preservate (rischio collisione).", table, pk);
            childMeta[table] = (pk, genOk);
        }

        // Generatori mai sotto il MAX gia' in tabella PRIMA del lotto: gli ID preservati
        // dei trasferimenti passati (e delle righe figlie copiate verbatim in F2) possono
        // stare sopra il contatore, e un GEN_ID durante il lotto colliderebbe.
        RealignGenerator(dst, null, "TArticoli__IDArticolo", MaxId(dst, "TArticoli", "IDArticolo"));
        if (anagrGenOk)
            RealignGenerator(dst, null, "TAnagrafica__IDAnagr", MaxId(dst, "TAnagrafica", "IDAnagr"));
        foreach (var (table, meta) in childMeta)
            if (meta.PkGenOk && meta.PkCol != null)
                RealignGenerator(dst, null, $"{table}__{meta.PkCol}", MaxId(dst, table, meta.PkCol));

        // Nome → IDAnagr e memoria dei fornitori gia' sistemati nel lotto: senza, un
        // fornitore in collisione verrebbe ricopiato a OGNI articolo che lo referenzia.
        var anagrByName = AnagrNamesInDst(dst);
        var anagrMemo = new Dictionary<int, int>();
        var report = new DaneaTransferReport();

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
                    res.Error = ErroreArticoloAssente;
                    report.Errors++;
                    continue;
                }
                string code = (Convert.ToString(row["CodArticolo"]) ?? "").Trim();
                res.CodArticolo = code;
                if (transferred.TryGetValue(code, out int existingId))
                {
                    res.IdInAtecPm = existingId;
                    res.Outcome = "skipped";
                    report.Skipped++;
                    continue;
                }

                var note = new List<string>();
                int insertId = id;
                SupplierPlan suppliers;
                using (var tx = dst.BeginTransaction())
                {
                    // Fornitore al seguito (#129): le anagrafiche nate nel vecchio dopo
                    // il bootstrap non esistono in Atec_PM — si copiano qui, prima
                    // dell'articolo (FK), nella stessa transazione.
                    suppliers = EnsureSuppliers(src, dst, tx, anagrCols, anagrGenOk,
                        row, id, fornCols, note, anagrMemo, anagrByName);
                    if (row.TryGetValue("IDFornitore", out object? forn) && forn is not DBNull
                        && suppliers.Remap.TryGetValue(Convert.ToInt32(forn), out int fornNew))
                        row["IDFornitore"] = fornNew;

                    // Collisione IDArticolo (#129): ID gia' occupato → l'articolo entra
                    // con un ID nuovo dal generatore.
                    if (RowExists(dst, tx, "TArticoli", "IDArticolo", id))
                    {
                        insertId = (int)NextId(dst, tx, "TArticoli__IDArticolo");
                        row["IDArticolo"] = insertId;
                        note.Add($"ID {id} gia' occupato in Atec_PM: inserito con ID {insertId}.");
                    }

                    InsertRow(dst, tx, "TArticoli", row);
                    // Subito, non a fine lotto: un ID preservato sopra il contatore
                    // renderebbe collidente il prossimo GEN_ID dentro lo stesso lotto.
                    RealignGenerator(dst, tx, "TArticoli__IDArticolo", insertId);
                    CopyChildRows(src, dst, tx, "TArticoliForn", fornCols, id, insertId,
                        childMeta["TArticoliForn"].PkCol, childMeta["TArticoliForn"].PkGenOk, suppliers.Remap);
                    CopyChildRows(src, dst, tx, "TArticoliCodBarre", barcodeCols, id, insertId,
                        childMeta["TArticoliCodBarre"].PkCol, childMeta["TArticoliCodBarre"].PkGenOk, null);
                    tx.Commit();
                }
                // La memoria fornitori si aggiorna SOLO a commit riuscito (il rollback
                // porta via anche le anagrafiche copiate).
                foreach (var (k, v) in suppliers.Memo) anagrMemo[k] = v;
                foreach (var (n, v) in suppliers.Names) anagrByName[n] = v;
                transferred[code] = insertId;
                res.IdInAtecPm = insertId;
                res.Note = string.Join(" ", note);

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

    /// <summary>Codice → IDArticolo in Atec_PM: badge «In Atec_PM» e ID effettivo degli skipped.</summary>
    private static Dictionary<string, int> CodesInNew(FbConnection dst)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new FbCommand("SELECT \"CodArticolo\", \"IDArticolo\" FROM \"TArticoli\"", dst);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[(Convert.ToString(r[0]) ?? "").Trim()] = Convert.ToInt32(r[1]);
        return map;
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

    /// <summary>
    /// Copia le righe figlie rimappando IDArticolo sull'ID effettivo, la PK di riga
    /// (sempre nuova dal generatore: le righe nate in Atec_PM avanzano lo stesso contatore)
    /// e gli IDAnagr dei fornitori rimappati (#129).
    /// </summary>
    private static void CopyChildRows(
        FbConnection src, FbConnection dst, FbTransaction tx, string table, List<string> cols,
        int idArticolo, int targetIdArticolo, string? pkCol, bool pkGenOk,
        Dictionary<int, int>? anagrRemap)
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
            {
                object value = r.IsDBNull(i) ? DBNull.Value : r[i];
                string col = cols[i];
                if (col.Equals("IDArticolo", StringComparison.OrdinalIgnoreCase))
                    value = targetIdArticolo;
                else if (pkGenOk && pkCol != null && col.Equals(pkCol, StringComparison.OrdinalIgnoreCase))
                    value = (int)NextId(dst, tx, $"{table}__{pkCol}");
                else if (anagrRemap != null && value is not DBNull
                         && col.Equals("IDAnagr", StringComparison.OrdinalIgnoreCase)
                         && anagrRemap.TryGetValue(Convert.ToInt32(value), out int mapped))
                    value = mapped;
                ins.Parameters.AddWithValue("@p_" + i, value);
            }
            ins.ExecuteNonQuery();
        }
    }

    /// <summary>Esito fornitori di UN articolo: Remap riscrive i riferimenti; Memo e Names
    /// entrano nella memoria di lotto solo a commit riuscito.</summary>
    private sealed class SupplierPlan
    {
        /// <summary>IDAnagr vecchio → ID effettivo, solo dove diverso (per riscrivere i riferimenti).</summary>
        public Dictionary<int, int> Remap { get; } = new();
        /// <summary>IDAnagr vecchio → ID effettivo, per TUTTI i fornitori risolti.</summary>
        public Dictionary<int, int> Memo { get; } = new();
        /// <summary>Nome → ID delle anagrafiche copiate (da aggiungere a anagrByName).</summary>
        public Dictionary<string, int> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// #129: porta con l'articolo le anagrafiche fornitore (IDFornitore + IDAnagr delle
    /// righe TArticoliForn) che in Atec_PM non esistono ancora. Ordine dei tentativi:
    /// memoria di lotto → stesso ID con stesso Nome → riuso per Nome sotto altro ID →
    /// copia (ID preservato se libero, altrimenti nuovo dal generatore).
    /// </summary>
    private SupplierPlan EnsureSuppliers(
        FbConnection src, FbConnection dst, FbTransaction tx, List<string> anagrCols,
        bool anagrGenOk, Dictionary<string, object> row, int idArticolo, List<string> fornCols,
        List<string> note, IReadOnlyDictionary<int, int> anagrMemo,
        IReadOnlyDictionary<string, int> anagrByName)
    {
        var ids = new HashSet<int>();
        if (row.TryGetValue("IDFornitore", out object? forn) && forn is not DBNull)
        {
            int f = Convert.ToInt32(forn);
            if (f > 0) ids.Add(f);
        }
        if (fornCols.Contains("IDAnagr"))
        {
            using var cmd = new FbCommand(
                "SELECT DISTINCT \"IDAnagr\" FROM \"TArticoliForn\" WHERE \"IDArticolo\" = @id", src);
            cmd.Parameters.AddWithValue("@id", idArticolo);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (!r.IsDBNull(0) && Convert.ToInt32(r[0]) > 0)
                    ids.Add(Convert.ToInt32(r[0]));
        }

        var plan = new SupplierPlan();
        foreach (int idAnagr in ids)
        {
            // Gia' risolto in questo lotto (o in questo articolo).
            if (anagrMemo.TryGetValue(idAnagr, out int fromMemo) ||
                plan.Memo.TryGetValue(idAnagr, out fromMemo))
            {
                if (fromMemo != idAnagr) plan.Remap[idAnagr] = fromMemo;
                continue;
            }

            var (found, dstNome) = AnagrInDst(dst, tx, idAnagr);
            var anagrafica = ReadRow(src, "TAnagrafica", anagrCols, "IDAnagr", idAnagr);
            string srcNome = (Convert.ToString(anagrafica?.GetValueOrDefault("Nome")) ?? "").Trim();

            if (anagrafica == null)
            {
                // Riferimento pendente nel vecchio: non c'e' nulla da copiare, e se l'ID
                // in Atec_PM appartiene a qualcun altro va almeno detto.
                note.Add(found
                    ? $"Anagrafica {idAnagr}: assente nel vecchio archivio, in Atec_PM l'ID " +
                      $"appartiene a \"{dstNome}\" — verificare il fornitore."
                    : $"Anagrafica {idAnagr} non trovata in nessuno dei due archivi.");
                continue;
            }

            // Stesso ID e stesso Nome = stessa anagrafica (bootstrap F1 o gia' copiata).
            // Nomi vuoti da entrambe le parti: ci si fida dell'ID (rimappare creerebbe
            // un doppione senza nome a ogni lotto), ma si avvisa.
            if (found && (srcNome.Length > 0
                    ? string.Equals(dstNome, srcNome, StringComparison.OrdinalIgnoreCase)
                    : dstNome.Length == 0))
            {
                plan.Memo[idAnagr] = idAnagr;
                if (srcNome.Length == 0)
                    note.Add($"Anagrafica {idAnagr} senza nome: agganciata per ID, verificare.");
                continue;
            }

            // Nata in parallelo nei DUE archivi: stessa anagrafica sotto un altro ID —
            // si riusa quella di Atec_PM, mai un doppione per nome.
            if (srcNome.Length > 0 &&
                (plan.Names.TryGetValue(srcNome, out int byName) ||
                 anagrByName.TryGetValue(srcNome, out byName)))
            {
                plan.Memo[idAnagr] = byName;
                if (byName != idAnagr) plan.Remap[idAnagr] = byName;
                note.Add($"Fornitore \"{srcNome}\" gia' presente in Atec_PM (ID {byName}): agganciato.");
                continue;
            }

            if (!found)
            {
                InsertRow(dst, tx, "TAnagrafica", anagrafica);   // ID preservato
                if (anagrGenOk)
                    RealignGenerator(dst, tx, "TAnagrafica__IDAnagr", idAnagr);
                else
                    _log.LogWarning("[DaneaMigration] Generatore TAnagrafica__IDAnagr non trovato: " +
                                    "riallineamento saltato dopo la copia dell'anagrafica {Id}.", idAnagr);
                plan.Memo[idAnagr] = idAnagr;
                if (srcNome.Length > 0) plan.Names[srcNome] = idAnagr;
                note.Add($"Fornitore \"{srcNome}\" copiato in Atec_PM.");
            }
            else
            {
                // Stesso ID ma anagrafica DIVERSA (contatori paralleli): senza questa
                // guardia l'articolo si aggancerebbe in silenzio all'anagrafica sbagliata.
                if (!anagrGenOk)
                    throw new InvalidOperationException(
                        $"Fornitore \"{srcNome}\": ID {idAnagr} occupato da \"{dstNome}\" " +
                        "e generatore TAnagrafica__IDAnagr non trovato.");
                int newId = (int)NextId(dst, tx, "TAnagrafica__IDAnagr");
                anagrafica["IDAnagr"] = newId;
                InsertRow(dst, tx, "TAnagrafica", anagrafica);
                plan.Memo[idAnagr] = newId;
                plan.Remap[idAnagr] = newId;
                if (srcNome.Length > 0) plan.Names[srcNome] = newId;
                note.Add($"Fornitore \"{srcNome}\": ID {idAnagr} occupato da \"{dstNome}\" — copiato con ID {newId}.");
            }
        }
        return plan;
    }

    /// <summary>Nome → IDAnagr in Atec_PM: per riusare un'anagrafica gia' presente sotto altro ID.</summary>
    private static Dictionary<string, int> AnagrNamesInDst(FbConnection dst)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new FbCommand("SELECT \"Nome\", \"IDAnagr\" FROM \"TAnagrafica\"", dst);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string nome = (Convert.ToString(r[0]) ?? "").Trim();
            if (nome.Length > 0) map[nome] = Convert.ToInt32(r[1]);
        }
        return map;
    }

    private static (bool Found, string Nome) AnagrInDst(FbConnection dst, FbTransaction tx, int idAnagr)
    {
        using var cmd = new FbCommand(
            "SELECT \"Nome\" FROM \"TAnagrafica\" WHERE \"IDAnagr\" = @id", dst, tx);
        cmd.Parameters.AddWithValue("@id", idAnagr);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (false, "");
        return (true, r.IsDBNull(0) ? "" : (Convert.ToString(r[0]) ?? "").Trim());
    }

    private static bool RowExists(FbConnection c, FbTransaction tx, string table, string col, int value)
    {
        using var cmd = new FbCommand($"SELECT 1 FROM \"{table}\" WHERE \"{col}\" = @v", c, tx);
        cmd.Parameters.AddWithValue("@v", value);
        return cmd.ExecuteScalar() != null;
    }

    private static long NextId(FbConnection c, FbTransaction tx, string generator)
    {
        using var cmd = new FbCommand($"SELECT GEN_ID(\"{generator}\", 1) FROM RDB$DATABASE", c, tx);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static bool GeneratorExists(FbConnection c, string name)
    {
        using var cmd = new FbCommand(
            "SELECT 1 FROM RDB$GENERATORS WHERE TRIM(RDB$GENERATOR_NAME) = @g", c);
        cmd.Parameters.AddWithValue("@g", name);
        return cmd.ExecuteScalar() != null;
    }

    /// <summary>PK a colonna singola di tipo intero (null se assente, composita o non numerica).</summary>
    private static string? SingleIntPk(FbConnection c, string table)
    {
        var cols = new List<(string Name, int Type)>();
        using var cmd = new FbCommand(@"
            SELECT TRIM(sg.RDB$FIELD_NAME), f.RDB$FIELD_TYPE
            FROM RDB$RELATION_CONSTRAINTS rc
            JOIN RDB$INDEX_SEGMENTS sg ON sg.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
            JOIN RDB$RELATION_FIELDS rf ON rf.RDB$RELATION_NAME = rc.RDB$RELATION_NAME
                                       AND rf.RDB$FIELD_NAME = sg.RDB$FIELD_NAME
            JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
            WHERE rc.RDB$RELATION_NAME = @t AND rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'", c);
        cmd.Parameters.AddWithValue("@t", table);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            cols.Add((Convert.ToString(r[0]) ?? "", Convert.ToInt32(r[1])));
        // 7 = smallint, 8 = integer (niente bigint: il cast a int in CopyChildRows troncherebbe)
        return cols.Count == 1 && cols[0].Type is 7 or 8 ? cols[0].Name : null;
    }

    /// <summary>
    /// Generatore mai sotto minValue (sale soltanto). L'avanzamento usa GEN_ID col delta,
    /// non SET GENERATOR: e' fuori transazione per natura, quindi sicuro anche con tx
    /// pendente (e un rollback non lo riporta indietro — i buchi sono innocui).
    /// </summary>
    private static void RealignGenerator(FbConnection dst, FbTransaction? tx, string generator, long minValue)
    {
        using var cur = new FbCommand($"SELECT GEN_ID(\"{generator}\", 0) FROM RDB$DATABASE", dst, tx);
        long current = Convert.ToInt64(cur.ExecuteScalar());
        if (minValue > current)
        {
            using var bump = new FbCommand(
                $"SELECT GEN_ID(\"{generator}\", {minValue - current}) FROM RDB$DATABASE", dst, tx);
            bump.ExecuteScalar();
        }
    }

    private static long MaxId(FbConnection c, string table, string col)
    {
        using var cmd = new FbCommand($"SELECT MAX(\"{col}\") FROM \"{table}\"", c);
        object? v = cmd.ExecuteScalar();
        return v == null || v is DBNull ? 0 : Convert.ToInt64(v);
    }

    private static string Str(IDataReader r, int i) =>
        r.IsDBNull(i) ? "" : (Convert.ToString(r[i]) ?? "").Trim();
}
