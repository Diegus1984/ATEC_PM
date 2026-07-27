using FirebirdSql.Data.FirebirdClient;
using Dapper;

namespace ATEC.PM.Server.Services;

// Scrittura del mapping codice ATEC sul DB Danea Easyfatt: Extra1 dell'articolo
// (TArticoli.IDArticolo = catalog_items.easyfatt_id). Operazione "sicura" del runbook
// ATEC_Warehouse/docs/danea.md: un UPDATE di Extra1 non tocca giacenze, generator né cache.
// Regole: identificatori quotati case-sensitive (regola #6), Charset NONE + WireCrypt
// Disabled (regola #7). Scrittura sincrona e FAIL-FAST: se Danea non risponde l'operazione
// fallisce e lo specchio locale NON va aggiornato (niente code, niente divergenze).
// Clausola di reversibilità ("gestione interna senza Danea"): DaneaSync:MappingMaster=false
// disattiva la scrittura remota → il mapping vive solo in ATEC PM.
public class DaneaMappingService
{
    private readonly IConfiguration _config;
    private readonly ILogger<DaneaMappingService> _log;

    public DaneaMappingService(IConfiguration config, ILogger<DaneaMappingService> log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>False = «gestione interna»: il mapping non viene scritto su Danea.</summary>
    public bool MappingMaster =>
        !string.Equals(_config["DaneaSync:MappingMaster"], "false", StringComparison.OrdinalIgnoreCase);

    private string BuildConnectionString()
    {
        string filePath = _config["DaneaSync:EftFilePath"] ?? "";
        if (string.IsNullOrEmpty(filePath))
            throw new InvalidOperationException("DaneaSync:EftFilePath non configurato.");

        string appDir = AppContext.BaseDirectory;
        string fbClientPath = _config["Easyfatt:FirebirdClientPath"]
                              ?? Path.Combine(appDir, "Firebird", "fbclient.dll");

        int serverType = int.TryParse(_config["DaneaSync:FbServerType"], out int st) ? st : 1;
        var csb = new FbConnectionStringBuilder
        {
            Database = filePath,
            ServerType = (FbServerType)serverType,
            UserID = _config["DaneaSync:FbUser"] ?? "SYSDBA",
            Password = _config["DaneaSync:FbPassword"] ?? "masterkey",
            ClientLibrary = fbClientPath,
            Charset = "NONE",
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

    /// <summary>
    /// Scrive il codice ATEC (o lo svuota, con atecCode vuoto) nell'Extra1 dell'articolo Danea.
    /// Lancia eccezione se Danea non è raggiungibile o l'articolo non esiste (fail-fast).
    /// No-op se MappingMaster=false.
    /// </summary>
    public void WriteExtra1(int easyfattId, string atecCode)
    {
        if (!MappingMaster) return;

        using var fb = new FbConnection(BuildConnectionString());
        fb.Open();
        int rows = fb.Execute(
            @"UPDATE ""TArticoli"" SET ""Extra1"" = @Code WHERE ""IDArticolo"" = @Id",
            new { Code = atecCode.Length > 0 ? atecCode : null, Id = easyfattId });
        if (rows == 0)
            throw new InvalidOperationException(
                $"Articolo Danea non trovato (IDArticolo {easyfattId}): sincronizzare il catalogo.");
        _log.LogInformation("[DaneaMapping] Extra1 di IDArticolo {Id} → '{Code}'", easyfattId, atecCode);
    }
}
