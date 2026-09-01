using FirebirdSql.Data.FirebirdClient;
using Dapper;

namespace ATEC.PM.Server.Services;

public class DaneaSyncService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DaneaSyncService> _log;
    private readonly IConfiguration _config;
    private readonly TimeSpan _interval;

    public static bool IsSyncing { get; private set; }
    public static DateTime? LastSync { get; private set; }
    public static string? LastError { get; private set; }
    public static string ProgressMessage { get; private set; } = "Pronto";
    public static int SuppliersCount { get; private set; }
    public static int CustomersCount { get; private set; }
    public static int ArticlesCount { get; private set; }

    public DaneaSyncService(IServiceProvider sp, IConfiguration config, ILogger<DaneaSyncService> log)
    {
        _sp = sp;
        _log = log;
        _config = config;
        int hours = int.TryParse(config["DaneaSync:IntervalHours"], out int h) ? h : 6;
        _interval = TimeSpan.FromHours(hours);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(15000, ct);
        await RunSync();

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_interval, ct);
            await RunSync();
        }
    }

    public async Task RunSync()
    {
        if (IsSyncing) return;

        string connStr = BuildConnectionString();
        if (string.IsNullOrEmpty(connStr))
        {
            _log.LogWarning("[DaneaSync] DaneaSync:EftFilePath non configurato, skip sync.");
            return;
        }

        IsSyncing = true;
        LastError = null;
        _log.LogInformation("[DaneaSync] Inizio sincronizzazione...");

        try
        {
            ProgressMessage = "Connessione a Easyfatt...";
            using var fb = new FbConnection(connStr);
            await fb.OpenAsync();

            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DbService>();

            ProgressMessage = "Sincronizzazione Fornitori...";
            await SyncSuppliers(fb, db);

            ProgressMessage = "Sincronizzazione Clienti...";
            await SyncCustomers(fb, db);

            ProgressMessage = "Sincronizzazione Articoli...";
            await SyncArticles(fb, db);

            LastSync = DateTime.Now;
            ProgressMessage = "Completato";

            using var local = db.Open();
            await local.ExecuteAsync(@"
                INSERT INTO app_config (config_key, config_value) VALUES ('danea_last_sync', @Val)
                ON DUPLICATE KEY UPDATE config_value = @Val",
                new { Val = LastSync.Value.ToString("yyyy-MM-dd HH:mm:ss") });

            _log.LogInformation($"[DaneaSync] Completato: {SuppliersCount} fornitori, {CustomersCount} clienti, {ArticlesCount} articoli.");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ProgressMessage = $"Errore: {ex.Message}";
            _log.LogError(ex, "[DaneaSync] Errore durante sincronizzazione");
        }
        finally
        {
            IsSyncing = false;
        }
    }


    public string BuildConnectionString()
    {
        string filePath = _config["DaneaSync:EftFilePath"] ?? "";
        if (string.IsNullOrEmpty(filePath)) return "";

        string appDir = AppContext.BaseDirectory;
        string fbClientPath = _config["Easyfatt:FirebirdClientPath"]
                              ?? Path.Combine(appDir, "Firebird", "fbclient.dll");

        int serverType = int.TryParse(_config["DaneaSync:FbServerType"], out int st) ? st : 1;
        string user = _config["DaneaSync:FbUser"] ?? "SYSDBA";
        string password = _config["DaneaSync:FbPassword"] ?? "masterkey";

        // Charset NONE + WireCrypt Disabled: regola #7 del runbook Danea (ATEC_Warehouse/docs/danea.md),
        // senza WireCrypt disabilitato i caratteri non ASCII diventano '?'.
        var csb = new FbConnectionStringBuilder
        {
            Database = filePath,
            ServerType = (FbServerType)serverType,
            UserID = user,
            Password = password,
            ClientLibrary = fbClientPath,
            Charset = "NONE",
            WireCrypt = FbWireCrypt.Disabled
        };

        if (serverType == 0)
        {
            csb.DataSource = _config["DaneaSync:FbDataSource"] ?? "localhost";
            csb.Port = int.TryParse(_config["DaneaSync:FbPort"], out int p) ? p : 3050;
        }

        return csb.ToString();
    }

    private async Task SyncSuppliers(FbConnection fb, DbService db)
    {
        var remote = (await fb.QueryAsync(@"
            SELECT ""Nome"", ""Referente"", ""Email"", ""Tel"", ""Indirizzo"", ""Cap"", ""Citta"", ""Prov"", 
                   ""PartitaIva"", ""CodiceFiscale"", ""Note""
            FROM ""TAnagrafica"" WHERE ""Fornitore"" = 1")).ToList();

        using var local = db.Open();
        int count = 0;
        foreach (var s in remote)
        {
            string vat = ((string?)s.PartitaIva)?.Trim() ?? "";
            if (string.IsNullOrEmpty(vat)) continue;

            string indirizzo = ((string?)s.Indirizzo)?.Trim() ?? "";
            string cap = ((string?)s.Cap)?.Trim() ?? "";
            string citta = ((string?)s.Citta)?.Trim() ?? "";
            string prov = ((string?)s.Prov)?.Trim() ?? "";
            string address = $"{indirizzo}, {cap} {citta} ({prov})".Trim(' ', ',');

            await local.ExecuteAsync(@"
                INSERT INTO suppliers (company_name, contact_name, email, phone, address, vat_number, fiscal_code, notes, is_active)
                VALUES (@Nome, @Referente, @Email, @Tel, @Address, @Vat, @Cf, @Note, 1)
                ON DUPLICATE KEY UPDATE 
                    company_name=@Nome, contact_name=@Referente, email=@Email, phone=@Tel, address=@Address, notes=@Note",
                new
                {
                    Nome = ((string?)s.Nome)?.Trim() ?? "",
                    Referente = ((string?)s.Referente)?.Trim() ?? "",
                    Email = ((string?)s.Email)?.Trim() ?? "",
                    Tel = ((string?)s.Tel)?.Trim() ?? "",
                    Address = address,
                    Vat = vat,
                    Cf = ((string?)s.CodiceFiscale)?.Trim() ?? "",
                    Note = ((string?)s.Note)?.Trim() ?? ""
                });
            count++;
        }
        SuppliersCount = count;
        _log.LogInformation($"[DaneaSync] Fornitori: {count} sincronizzati");
    }

    private async Task SyncCustomers(FbConnection fb, DbService db)
    {
        var remote = (await fb.QueryAsync(@"
            SELECT ""IDAnagr"", ""CodAnagr"", ""Nome"", ""Referente"", ""Email"", ""Pec"", ""Tel"", ""Cell"", 
                   ""Indirizzo"", ""Cap"", ""Citta"", ""Prov"", ""PartitaIva"", ""CodiceFiscale"", 
                   ""PagamentoDefault"", ""FE_CodUfficio"", ""Note""
            FROM ""TAnagrafica"" WHERE ""Cliente"" = 1")).ToList();

        using var local = db.Open();
        int count = 0;
        foreach (var c in remote)
        {
            string vat = ((string?)c.PartitaIva)?.Trim() ?? "";
            if (string.IsNullOrEmpty(vat)) continue;

            string indirizzo = ((string?)c.Indirizzo)?.Trim() ?? "";
            string cap = ((string?)c.Cap)?.Trim() ?? "";
            string citta = ((string?)c.Citta)?.Trim() ?? "";
            string prov = ((string?)c.Prov)?.Trim() ?? "";
            string address = $"{indirizzo}, {cap} {citta} ({prov})".Trim(' ', ',');

            await local.ExecuteAsync(@"
                INSERT INTO customers (company_name, contact_name, email, pec, phone, cell, address, 
                                     vat_number, fiscal_code, payment_terms, sdi_code, easyfatt_code, 
                                     easyfatt_id, notes, is_active)
                VALUES (@Nome, @Referente, @Email, @Pec, @Tel, @Cell, @Address, @Vat, 
                        @Cf, @Pagamento, @Sdi, @CodAnagr, @IDAnagr, @Note, 1)
                ON DUPLICATE KEY UPDATE 
                    company_name=@Nome, email=@Email, pec=@Pec, phone=@Tel, address=@Address, 
                    sdi_code=@Sdi, notes=@Note, easyfatt_id=@IDAnagr",
                new
                {
                    Nome = ((string?)c.Nome)?.Trim() ?? "",
                    Referente = ((string?)c.Referente)?.Trim() ?? "",
                    Email = ((string?)c.Email)?.Trim() ?? "",
                    Pec = ((string?)c.Pec)?.Trim() ?? "",
                    Tel = ((string?)c.Tel)?.Trim() ?? "",
                    Cell = ((string?)c.Cell)?.Trim() ?? "",
                    Address = address,
                    Vat = vat,
                    Cf = ((string?)c.CodiceFiscale)?.Trim() ?? "",
                    Pagamento = ((string?)c.PagamentoDefault)?.Trim() ?? "",
                    Sdi = ((string?)c.FE_CodUfficio)?.Trim() ?? "",
                    CodAnagr = ((string?)c.CodAnagr)?.Trim() ?? "",
                    IDAnagr = (int?)c.IDAnagr,
                    Note = ((string?)c.Note)?.Trim() ?? ""
                });
            count++;
        }
        CustomersCount = count;
        _log.LogInformation($"[DaneaSync] Clienti: {count} sincronizzati");
    }

    /// <summary>
    /// Riallinea nello specchio SOLO gli articoli indicati (IDArticolo dell'archivio
    /// sincronizzato), senza il giro completo e senza spegnere nient'altro.
    ///
    /// <para>Serve al trasferimento catalogo Danea: <c>catalog_items</c> e' uno specchio, e
    /// finche' non gira il sync l'articolo appena trasferito resta spento — la pagina Catalogo
    /// articoli non lo mostra e il trasferimento sembra fallito anche quando e' andato benissimo
    /// (25/08/2026). Il giro completo sono ~10.000 righe: qui se ne toccano N, quindi si puo'
    /// fare prima di rispondere alla richiesta invece di sperare nel giro delle 6 ore.</para>
    /// </summary>
    public async Task<int> AllineaArticoli(IReadOnlyCollection<int> idArticoli)
    {
        if (idArticoli.Count == 0) return 0;

        string connStr = BuildConnectionString();
        if (string.IsNullOrEmpty(connStr))
            throw new InvalidOperationException("DaneaSync:EftFilePath non configurato.");

        using var fb = new FbConnection(connStr);
        await fb.OpenAsync();
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbService>();
        return await UpsertArticoli(fb, db, idArticoli);
    }

    private Task<int> SyncArticles(FbConnection fb, DbService db) => UpsertArticoli(fb, db, null);

    /// <summary>
    /// Porta nello specchio gli articoli dell'archivio Danea. Con <paramref name="soloQuestiId"/>
    /// null e' il giro COMPLETO (spegne tutto e riaccende quello che trova); con una lista tocca
    /// solo quelle righe. Il mapping delle colonne sta qui una volta sola.
    /// </summary>
    private async Task<int> UpsertArticoli(
        FbConnection fb, DbService db, IReadOnlyCollection<int>? soloQuestiId)
    {
        // Scopri colonne di TArticoli per capire se esiste IDFornitore
        var artCols = (await fb.QueryAsync<string>(@"
            SELECT TRIM(rf.RDB$FIELD_NAME) FROM RDB$RELATION_FIELDS rf
            WHERE rf.RDB$RELATION_NAME = 'TArticoli'")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool hasIDFornitore = artCols.Contains("IDFornitore");

        _log.LogInformation("[DaneaSync] TArticoli ha IDFornitore: {Has}", hasIDFornitore);

        // Leggi fornitori da TAnagrafica per lookup nome
        var fornitori = new Dictionary<int, string>();
        if (hasIDFornitore)
        {
            try
            {
                // Scopri PK di TAnagrafica
                var anagCols = (await fb.QueryAsync<string>(@"
                    SELECT TRIM(rf.RDB$FIELD_NAME) FROM RDB$RELATION_FIELDS rf
                    WHERE rf.RDB$RELATION_NAME = 'TAnagrafica'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // La PK vera di TAnagrafica è IDAnagr (numerica, la stessa usata da SyncCustomers).
                // NIENTE fallback su CodAnagr: è il codice anagrafica testuale ("bmt gmbh") e
                // faceva esplodere il Convert.ToInt32 → fornitori mai agganciati agli articoli.
                string pkCol = anagCols.Contains("IDAnagr") ? "IDAnagr" :
                               anagCols.Contains("Id") ? "Id" : "";

                _log.LogInformation("[DaneaSync] TAnagrafica PK candidata: '{Pk}', colonne: {Cols}",
                    pkCol, string.Join(", ", anagCols.Take(15)));

                if (!string.IsNullOrEmpty(pkCol))
                {
                    var anag = (await fb.QueryAsync(
                        $"SELECT \"{pkCol}\", \"Nome\" FROM \"TAnagrafica\" WHERE \"Fornitore\" = 1")).ToList();
                    foreach (var f in anag)
                    {
                        var dict = (IDictionary<string, object>)f;
                        int id = Convert.ToInt32(dict[pkCol]);
                        string nome = ((string?)dict["Nome"])?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(nome))
                            fornitori[id] = nome;
                    }
                    _log.LogInformation("[DaneaSync] Caricati {N} fornitori per match articoli", fornitori.Count);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("[DaneaSync] Impossibile leggere fornitori: {Msg}", ex.Message);
            }
        }

        string artQuery = hasIDFornitore
            ? @"SELECT ""IDArticolo"", ""CodArticolo"", ""Desc"", ""NomeCategoria"", ""NomeSottocategoria"",
                       ""Udm"", ""PrezzoNetto1"", ""PrezzoNettoForn"", ""CodArticoloForn"",
                       ""Produttore"", ""CodBarre"", ""Note"", ""Extra1"", ""IDFornitore""
                FROM ""TArticoli"""
            : @"SELECT ""IDArticolo"", ""CodArticolo"", ""Desc"", ""NomeCategoria"", ""NomeSottocategoria"",
                       ""Udm"", ""PrezzoNetto1"", ""PrezzoNettoForn"", ""CodArticoloForn"",
                       ""Produttore"", ""CodBarre"", ""Note"", ""Extra1""
                FROM ""TArticoli""";

        // Giro mirato: gli id arrivano dal trasferimento, sono interi, nessuna concatenazione
        // pericolosa (e Firebird non gradisce liste come parametro singolo).
        if (soloQuestiId != null)
            artQuery += " WHERE \"IDArticolo\" IN (" + string.Join(",", soloQuestiId) + ")";

        var remote = (await fb.QueryAsync(artQuery)).ToList();

        using var local = db.Open();

        // Mapping codice ATEC (piano Acquisti): Extra1 dell'articolo Danea → codice NUOVO Codex.
        // Risoluzione SOLO contro codice_nuovo (convenzione: in Extra1 vanno solo codici nuovi).
        // Con MappingMaster=false ("gestione interna senza Danea") il sync NON tocca il mapping locale.
        bool mappingMaster = !string.Equals(_config["DaneaSync:MappingMaster"], "false", StringComparison.OrdinalIgnoreCase);
        var codexByNewCode = new Dictionary<string, int>(StringComparer.Ordinal);
        if (mappingMaster)
        {
            foreach (var row in await local.QueryAsync<(int Id, string CodiceNuovo)>(
                "SELECT id, codice_nuovo FROM codex_items WHERE codice_nuovo IS NOT NULL AND codice_nuovo <> ''"))
            {
                codexByNewCode.TryAdd(row.CodiceNuovo, row.Id);
            }
        }

        // Prepara lookup fornitori locali per nome (primo match in caso di duplicati)
        var supplierLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in await local.QueryAsync<(int Id, string Name)>("SELECT id, company_name FROM suppliers"))
        {
            var key = s.Name.Trim();
            if (!string.IsNullOrEmpty(key))
                supplierLookup.TryAdd(key, s.Id);
        }

        // SPECCHIO VERO (F3 migrazione Atec_PM, 22/07/2026): il catalogo locale riflette
        // SOLO gli articoli presenti nell'archivio Danea sincronizzato. In transazione:
        // si disattiva tutto, poi ogni upsert riattiva la propria riga → alla fine gli
        // articoli spariti dall'archivio (o mai trasferiti) restano is_active=0 (nessuna
        // DELETE: le distinte che li referenziano non perdono nulla). Guardia: se il
        // remoto è vuoto non si tocca niente (archivio sbagliato ≠ svuotare lo specchio).
        if (remote.Count == 0)
        {
            if (soloQuestiId == null)
            {
                _log.LogWarning("[DaneaSync] Archivio senza articoli: specchio locale NON toccato");
                ArticlesCount = 0;
            }
            return 0;
        }
        using var artTx = local.BeginTransaction();
        // Lo svuotamento vale SOLO per il giro completo: in modalita' mirata si allineano le
        // righe richieste e basta, senza spegnere il resto del catalogo.
        if (soloQuestiId == null)
            await local.ExecuteAsync(
                "UPDATE catalog_items SET is_active = 0 WHERE is_active = 1", transaction: artTx);

        int count = 0;
        foreach (var a in remote)
        {
            string code = ((string?)a.CodArticolo)?.Trim() ?? "";
            if (string.IsNullOrEmpty(code)) continue;

            // Match fornitore: Easyfatt IDFornitore → nome → supplier locale
            int? supplierId = null;
            if (hasIDFornitore)
            {
                int idForn = 0;
                try { idForn = (int?)a.IDFornitore ?? 0; } 
                catch (Exception ex) { _log.LogDebug(ex, "Impossibile convertire IDFornitore per l'articolo {CodArticolo}", code); }
                if (idForn > 0 && fornitori.TryGetValue(idForn, out string? fornNome)
                    && !string.IsNullOrEmpty(fornNome)
                    && supplierLookup.TryGetValue(fornNome.ToLower(), out int sid))
                {
                    supplierId = sid;
                }
            }

            // Extra1 = codice ATEC (nuova codifica), normalizzato senza punti; vuoto = non mappato.
            string atecCode = ((string?)a.Extra1)?.Replace(".", "").Trim() ?? "";
            int? codexItemId = null;
            if (mappingMaster && atecCode.Length > 0 && codexByNewCode.TryGetValue(atecCode, out int cid))
                codexItemId = cid;

            string mappingSet = mappingMaster
                ? ", atec_code=@AtecCode, codex_item_id=@CodexItemId"
                : "";
            await local.ExecuteAsync($@"
                INSERT INTO catalog_items (code, description, category, subcategory, unit, unit_cost,
                                         list_price, supplier_id, supplier_code, manufacturer, barcode, notes, is_active, easyfatt_id,
                                         atec_code, codex_item_id)
                VALUES (@Code, @Desc, @Cat, @SubCat, @Udm, @CostoForn,
                        @Listino, @SuppId, @CodForn, @Produttore, @Barcode, @Note, 1, @EftId,
                        @AtecCode, @CodexItemId)
                ON DUPLICATE KEY UPDATE
                    is_active=1, description=@Desc, category=@Cat, subcategory=@SubCat, unit=@Udm,
                    unit_cost=@CostoForn, list_price=@Listino, supplier_id=@SuppId,
                    -- Specchio VERO anche su questi (01/09/2026, Diego): il «Cod. prod. forn.»
                    -- di Danea (CodArticoloForn) arrivava solo alla NASCITA della riga — chi
                    -- era nato col campo vuoto restava vuoto per sempre. Idem produttore,
                    -- barcode e UM: l'UPDATE ora riflette tutto quello che scrive l'INSERT.
                    supplier_code=@CodForn, manufacturer=@Produttore, barcode=@Barcode,
                    notes=@Note, easyfatt_id=@EftId{mappingSet}",
                new
                {
                    Code = code,
                    Desc = ((string?)a.Desc)?.Trim() ?? "",
                    Cat = ((string?)a.NomeCategoria)?.Trim() ?? "",
                    SubCat = ((string?)a.NomeSottocategoria)?.Trim() ?? "",
                    Udm = ((string?)a.Udm)?.Trim() ?? "",
                    CostoForn = (decimal?)a.PrezzoNettoForn ?? 0m,
                    Listino = (decimal?)a.PrezzoNetto1 ?? 0m,
                    SuppId = supplierId,
                    CodForn = ((string?)a.CodArticoloForn)?.Trim() ?? "",
                    Produttore = ((string?)a.Produttore)?.Trim() ?? "",
                    Barcode = ((string?)a.CodBarre)?.Trim() ?? "",
                    Note = ((string?)a.Note)?.Trim() ?? "",
                    EftId = (int?)a.IDArticolo,
                    AtecCode = atecCode.Length > 0 ? atecCode : null,
                    CodexItemId = codexItemId
                }, transaction: artTx);
            count++;
        }
        artTx.Commit();
        if (soloQuestiId != null)
        {
            _log.LogInformation("[DaneaSync] Allineati {Count} articoli mirati (trasferimento catalogo).", count);
            return count;
        }

        int inactive = await local.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM catalog_items WHERE is_active = 0");
        ArticlesCount = count;
        _log.LogInformation("[DaneaSync] Articoli: {Count} sincronizzati (specchio: {Inactive} disattivati perché assenti dall'archivio)",
            count, inactive);
        return count;
    }
}
