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


    private string BuildConnectionString()
    {
        string filePath = _config["DaneaSync:EftFilePath"] ?? "";
        if (string.IsNullOrEmpty(filePath)) return "";

        string appDir = AppContext.BaseDirectory;
        string fbClientPath = _config["Easyfatt:FirebirdClientPath"]
                              ?? Path.Combine(appDir, "Firebird", "fbclient.dll");

        int serverType = int.TryParse(_config["DaneaSync:FbServerType"], out int st) ? st : 1;
        string user = _config["DaneaSync:FbUser"] ?? "SYSDBA";
        string password = _config["DaneaSync:FbPassword"] ?? "masterkey";

        var csb = new FbConnectionStringBuilder
        {
            Database = filePath,
            ServerType = (FbServerType)serverType,
            UserID = user,
            Password = password,
            ClientLibrary = fbClientPath,
            Charset = "NONE"
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

    private async Task SyncArticles(FbConnection fb, DbService db)
    {
        var remote = (await fb.QueryAsync(@"
            SELECT ""IDArticolo"", ""CodArticolo"", ""Desc"", ""NomeCategoria"", ""NomeSottocategoria"", 
                   ""Udm"", ""PrezzoNetto1"", ""PrezzoNettoForn"", ""CodArticoloForn"",
                   ""Produttore"", ""CodBarre"", ""Note""
            FROM ""TArticoli""")).ToList();

        using var local = db.Open();
        int count = 0;
        foreach (var a in remote)
        {
            string code = ((string?)a.CodArticolo)?.Trim() ?? "";
            if (string.IsNullOrEmpty(code)) continue;

            await local.ExecuteAsync(@"
                INSERT INTO catalog_items (code, description, category, subcategory, unit, unit_cost, 
                                         list_price, supplier_code, manufacturer, barcode, notes, is_active, easyfatt_id)
                VALUES (@Code, @Desc, @Cat, @SubCat, @Udm, @CostoForn, 
                        @Listino, @CodForn, @Produttore, @Barcode, @Note, 1, @EftId)
                ON DUPLICATE KEY UPDATE 
                    description=@Desc, category=@Cat, unit_cost=@CostoForn, 
                    list_price=@Listino, notes=@Note, easyfatt_id=@EftId",
                new
                {
                    Code = code,
                    Desc = ((string?)a.Desc)?.Trim() ?? "",
                    Cat = ((string?)a.NomeCategoria)?.Trim() ?? "",
                    SubCat = ((string?)a.NomeSottocategoria)?.Trim() ?? "",
                    Udm = ((string?)a.Udm)?.Trim() ?? "",
                    CostoForn = (decimal?)a.PrezzoNettoForn ?? 0m,
                    Listino = (decimal?)a.PrezzoNetto1 ?? 0m,
                    CodForn = ((string?)a.CodArticoloForn)?.Trim() ?? "",
                    Produttore = ((string?)a.Produttore)?.Trim() ?? "",
                    Barcode = ((string?)a.CodBarre)?.Trim() ?? "",
                    Note = ((string?)a.Note)?.Trim() ?? "",
                    EftId = (int?)a.IDArticolo
                });
            count++;
        }
        ArticlesCount = count;
        _log.LogInformation($"[DaneaSync] Articoli: {count} sincronizzati");
    }
}
