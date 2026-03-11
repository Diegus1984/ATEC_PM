using System.Data;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Services;

public class DaneaSyncService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DaneaSyncService> _log;
    private readonly IConfiguration _config;
    private readonly string _fbClientPath;
    private readonly TimeSpan _interval;

    public DaneaSyncService(IServiceProvider sp, IConfiguration config, ILogger<DaneaSyncService> log)
    {
        _sp = sp;
        _log = log;
        _config = config;

        var appDir = AppContext.BaseDirectory;
        _fbClientPath = config["Easyfatt:FirebirdClientPath"]
                        ?? Path.Combine(appDir, "Firebird", "fbclient.dll");

        int hours = int.TryParse(config["DaneaSync:IntervalHours"], out int h) ? h : 6;
        _interval = TimeSpan.FromHours(hours);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(10000, ct);

        while (!ct.IsCancellationRequested)
        {
            try { await RunSync(); }
            catch (Exception ex) { _log.LogError(ex, "[DaneaSync] Errore critico"); }
            await Task.Delay(_interval, ct);
        }
    }

    private async Task SyncSuppliers(FbConnection fb, DbService db)
    {
        var remote = (await fb.QueryAsync(@"
            SELECT ""Nome"", ""Referente"", ""Email"", ""Tel"", ""Indirizzo"", ""Cap"", ""Citta"", ""Prov"", 
                   ""PartitaIva"", ""CodiceFiscale"", ""Note""
            FROM ""TAnagrafica"" WHERE ""Fornitore"" = 1")).ToList();

        using var local = db.Open();
        foreach (var s in remote)
        {
            string vat = s.PartitaIva?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(vat)) continue;

            string address = $"{s.Indirizzo}, {s.Cap} {s.Citta} ({s.Prov})".Trim(' ', ',');

            await local.ExecuteAsync(@"
                INSERT INTO suppliers (company_name, contact_name, email, phone, address, vat_number, fiscal_code, notes, is_active)
                VALUES (@Nome, @Referente, @Email, @Tel, @Address, @PartitaIva, @CodiceFiscale, @Note, 1)
                ON DUPLICATE KEY UPDATE 
                    company_name=@Nome, contact_name=@Referente, email=@Email, phone=@Tel, address=@Address, notes=@Note",
                new { s.Nome, s.Referente, s.Email, s.Tel, Address = address, s.PartitaIva, s.CodiceFiscale, s.Note });
        }
    }

    private async Task SyncCustomers(FbConnection fb, DbService db)
    {
        var remote = (await fb.QueryAsync(@"
            SELECT ""IDAnagr"", ""CodAnagr"", ""Nome"", ""Referente"", ""Email"", ""Pec"", ""Tel"", ""Cell"", 
                   ""Indirizzo"", ""Cap"", ""Citta"", ""Prov"", ""PartitaIva"", ""CodiceFiscale"", 
                   ""PagamentoDefault"", ""FE_CodUfficio"", ""Note""
            FROM ""TAnagrafica"" WHERE ""Cliente"" = 1")).ToList();

        using var local = db.Open();
        foreach (var c in remote)
        {
            string vat = c.PartitaIva?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(vat)) continue;

            string address = $"{c.Indirizzo}, {c.Cap} {c.Citta} ({c.Prov})".Trim(' ', ',');

            await local.ExecuteAsync(@"
                INSERT INTO customers (company_name, contact_name, email, pec, phone, cell, address, 
                                     vat_number, fiscal_code, payment_terms, sdi_code, easyfatt_code, 
                                     easyfatt_id, notes, is_active)
                VALUES (@Nome, @Referente, @Email, @Pec, @Tel, @Cell, @Address, @PartitaIva, 
                        @CodiceFiscale, @PagamentoDefault, @FE_CodUfficio, @CodAnagr, @IDAnagr, @Note, 1)
                ON DUPLICATE KEY UPDATE 
                    company_name=@Nome, email=@Email, pec=@Pec, phone=@Tel, address=@Address, 
                    sdi_code=@FE_CodUfficio, notes=@Note, easyfatt_id=@IDAnagr",
                new
                {
                    c.Nome,
                    c.Referente,
                    c.Email,
                    c.Pec,
                    c.Tel,
                    c.Cell,
                    Address = address,
                    c.PartitaIva,
                    c.CodiceFiscale,
                    c.PagamentoDefault,
                    c.FE_CodUfficio,
                    c.CodAnagr,
                    c.IDAnagr,
                    c.Note
                });
        }
    }

    private async Task SyncArticles(FbConnection fb, DbService db)
    {
        var remote = (await fb.QueryAsync(@"
            SELECT ""IDArticolo"", ""CodArticolo"", ""Desc"", ""NomeCategoria"", ""NomeSottocategoria"", 
                   ""Udm"", ""PrezzoNetto1"", ""PrezzoNettoForn"", ""CodArticoloForn"",
                   ""Produttore"", ""CodBarre"", ""Note""
            FROM ""TArticoli""")).ToList();

        using var local = db.Open();
        foreach (var a in remote)
        {
            string code = a.CodArticolo?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(code)) continue;

            await local.ExecuteAsync(@"
                INSERT INTO catalog_items (code, description, category, subcategory, unit, unit_cost, 
                                         list_price, supplier_code, manufacturer, barcode, notes, is_active, easyfatt_id)
                VALUES (@CodArticolo, @Desc, @NomeCategoria, @NomeSottocategoria, @Udm, @PrezzoNettoForn, 
                        @PrezzoNetto1, @CodArticoloForn, @Produttore, @CodBarre, @Note, 1, @IDArticolo)
                ON DUPLICATE KEY UPDATE 
                    description=@Desc, category=@NomeCategoria, unit_cost=@PrezzoNettoForn, 
                    list_price=@PrezzoNetto1, notes=@Note, easyfatt_id=@IDArticolo",
                new
                {
                    a.CodArticolo,
                    a.Desc,
                    a.NomeCategoria,
                    a.NomeSottocategoria,
                    a.Udm,
                    a.PrezzoNettoForn,
                    a.PrezzoNetto1,
                    a.CodArticoloForn,
                    a.Produttore,
                    a.CodBarre,
                    a.Note,
                    a.IDArticolo
                });
        }
    }

    // Proprietà statiche per lo stato (come in CodexSyncService)
    public static bool IsSyncing { get; private set; }
    public static DateTime? LastSync { get; private set; }
    public static string? LastError { get; private set; }
    public static string ProgressMessage { get; private set; } = "Pronto";

    public async Task RunSync()
    {
        if (IsSyncing) return;
        IsSyncing = true;
        LastError = null;

        try
        {
            ProgressMessage = "Sincronizzazione Fornitori...";
            // ... logica SyncSuppliers ...

            ProgressMessage = "Sincronizzazione Clienti...";
            // ... logica SyncCustomers ...

            ProgressMessage = "Sincronizzazione Articoli...";
            // ... logica SyncArticles ...

            LastSync = DateTime.Now;
            ProgressMessage = "Completato";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ProgressMessage = "Errore";
        }
        finally
        {
            IsSyncing = false;
        }
    }
}