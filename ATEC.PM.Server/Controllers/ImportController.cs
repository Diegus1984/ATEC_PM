using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FirebirdSql.Data.FirebirdClient;
using Dapper;
using System.Data;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/import")]
[AllowAnonymous]
public class ImportController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly DbService _db;

    public ImportController(IConfiguration config, DbService db)
    {
        _config = config;
        _db = db;
    }

    /// <summary>
    /// Legge i fornitori da Easyfatt e li confronta con quelli esistenti in ATEC PM
    /// </summary>
    [HttpGet("easyfatt/suppliers")]
    public IActionResult GetEasyfattSuppliers([FromQuery] string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return BadRequest("filePath richiesto");

        try
        {
            var connStr = BuildConnectionString(filePath);
            using var fbConn = new FbConnection(connStr);
            fbConn.Open();

            using var cmd = new FbCommand(@"
                SELECT ""Nome"", ""Referente"", ""Email"", ""Tel"", 
                       ""Indirizzo"", ""Cap"", ""Citta"", ""Prov"", 
                       ""PartitaIva"", ""CodiceFiscale"", ""Note""
                FROM ""TAnagrafica"" 
                WHERE ""Fornitore"" = 1
                ORDER BY ""Nome""", fbConn);

            using var reader = cmd.ExecuteReader();

            var easyfattSuppliers = new List<EasyfattSupplierDto>();
            while (reader.Read())
            {
                var indirizzo = reader["Indirizzo"]?.ToString() ?? "";
                var cap = reader["Cap"]?.ToString() ?? "";
                var citta = reader["Citta"]?.ToString() ?? "";
                var prov = reader["Prov"]?.ToString() ?? "";
                var address = $"{indirizzo}, {cap} {citta} ({prov})".Trim(' ', ',');

                easyfattSuppliers.Add(new EasyfattSupplierDto
                {
                    CompanyName = reader["Nome"]?.ToString() ?? "",
                    ContactName = reader["Referente"]?.ToString() ?? "",
                    Email = reader["Email"]?.ToString() ?? "",
                    Phone = reader["Tel"]?.ToString() ?? "",
                    Address = address,
                    VatNumber = reader["PartitaIva"]?.ToString() ?? "",
                    FiscalCode = reader["CodiceFiscale"]?.ToString() ?? "",
                    Notes = reader["Note"]?.ToString() ?? ""
                });
            }
            fbConn.Close();

            // Confronto duplicati con fornitori esistenti
            using var mysqlConn = _db.Open();
            var existing = mysqlConn.Query<SupplierListItem>(
                "SELECT id, company_name AS CompanyName, vat_number AS VatNumber FROM suppliers").ToList();

            var existingVats = new Dictionary<string, SupplierListItem>();
            foreach (var s in existing.Where(s => !string.IsNullOrEmpty(s.VatNumber)))
            {
                var vat = s.VatNumber.Trim();
                if (!existingVats.ContainsKey(vat))
                    existingVats[vat] = s;
            }

            foreach (var sup in easyfattSuppliers)
            {
                var vat = sup.VatNumber?.Trim() ?? "";
                if (!string.IsNullOrEmpty(vat) && existingVats.ContainsKey(vat))
                {
                    sup.Status = "DUPLICATO";
                    sup.ExistingId = existingVats[vat].Id;
                    sup.ExistingName = existingVats[vat].CompanyName;
                }
                else
                {
                    sup.Status = "NUOVO";
                }
            }

            var summary = new
            {
                TotalFound = easyfattSuppliers.Count,
                NewCount = easyfattSuppliers.Count(s => s.Status == "NUOVO"),
                DuplicateCount = easyfattSuppliers.Count(s => s.Status == "DUPLICATO"),
                Suppliers = easyfattSuppliers
            };

            return Ok(ApiResponse<object>.Ok(summary));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore: {ex.Message}"));
        }
    }

    /// <summary>
    /// Importa i fornitori selezionati in ATEC PM
    /// </summary>
    [HttpPost("easyfatt/suppliers")]
    public IActionResult ImportSuppliers([FromBody] ImportSuppliersRequest req)
    {
        try
        {
            using var c = _db.Open();
            int imported = 0;
            int updated = 0;
            int skipped = 0;

            foreach (var sup in req.Suppliers)
            {
                if (sup.Action == "SKIP")
                {
                    skipped++;
                    continue;
                }

                if (sup.Action == "UPDATE" && sup.ExistingId > 0)
                {
                    c.Execute(@"UPDATE suppliers SET company_name=@CompanyName, contact_name=@ContactName, 
                        email=@Email, phone=@Phone, address=@Address, vat_number=@VatNumber, 
                        fiscal_code=@FiscalCode, notes=@Notes WHERE id=@ExistingId", sup);
                    updated++;
                }
                else // INSERT
                {
                    c.Execute(@"INSERT INTO suppliers (company_name, contact_name, email, phone, address, vat_number, fiscal_code, notes, is_active) 
                        VALUES (@CompanyName, @ContactName, @Email, @Phone, @Address, @VatNumber, @FiscalCode, @Notes, 1)", sup);
                    imported++;
                }
            }

            return Ok(ApiResponse<object>.Ok(new { Imported = imported, Updated = updated, Skipped = skipped }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore import: {ex.Message}"));
        }
    }

    /// <summary>
    /// Anteprima generica di una tabella Easyfatt (per esplorazione)
    /// </summary>
    [HttpGet("easyfatt/preview")]
    public IActionResult PreviewTable([FromQuery] string filePath, [FromQuery] string tableName, [FromQuery] int maxRows = 10)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(tableName))
            return BadRequest("filePath e tableName richiesti");

        try
        {
            var connStr = BuildConnectionString(filePath);
            using var conn = new FbConnection(connStr);
            conn.Open();

            using var cmd = new FbCommand($"SELECT FIRST {maxRows} * FROM \"{tableName}\"", conn);
            using var reader = cmd.ExecuteReader();

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));

            var rows = new List<Dictionary<string, object?>>();
            while (reader.Read())
            {
                var dict = new Dictionary<string, object?>();
                foreach (var col in columns)
                {
                    var val = reader[col];
                    dict[col] = val == DBNull.Value ? null : val;
                }
                rows.Add(dict);
            }

            return Ok(ApiResponse<object>.Ok(new { Columns = columns, RowCount = rows.Count, Rows = rows }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore: {ex.Message}"));
        }
    }

    /// <summary>
    /// Colonne di una tabella Easyfatt
    /// </summary>
    [HttpGet("easyfatt/columns")]
    public IActionResult GetColumns([FromQuery] string filePath, [FromQuery] string tableName)
    {
        try
        {
            var connStr = BuildConnectionString(filePath);
            using var conn = new FbConnection(connStr);
            conn.Open();
            var colsData = conn.GetSchema("Columns", new[] { null, null, tableName, null });
            var columns = new List<object>();
            foreach (System.Data.DataRow colRow in colsData.Rows)
            {
                columns.Add(new
                {
                    Name = colRow["COLUMN_NAME"]?.ToString(),
                    Type = colRow["COLUMN_DATA_TYPE"]?.ToString(),
                    Size = colRow["COLUMN_SIZE"]
                });
            }
            return Ok(ApiResponse<object>.Ok(columns));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore: {ex.Message}"));
        }
    }

    /// <summary>
    /// Legge gli articoli da Easyfatt e li confronta con il catalogo ATEC PM
    /// </summary>
    [HttpGet("easyfatt/articles")]
    public IActionResult GetEasyfattArticles([FromQuery] string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return BadRequest("filePath richiesto");

        try
        {
            var connStr = BuildConnectionString(filePath);
            using var fbConn = new FbConnection(connStr);
            fbConn.Open();

            using var cmd = new FbCommand(@"
            SELECT ""IDArticolo"", ""CodArticolo"", ""Desc"", ""NomeCategoria"", 
                   ""NomeSottocategoria"", ""Udm"", ""PrezzoNetto1"", 
                   ""PrezzoNettoForn"", ""IDFornitore"", ""CodArticoloForn"",
                   ""Produttore"", ""CodBarre"", ""Note""
            FROM ""TArticoli""
            ORDER BY ""CodArticolo""", fbConn);

            using var reader = cmd.ExecuteReader();

            var articles = new List<EasyfattArticleDto>();
            while (reader.Read())
            {
                articles.Add(new EasyfattArticleDto
                {
                    EasyfattId = reader["IDArticolo"] != DBNull.Value ? Convert.ToInt32(reader["IDArticolo"]) : 0,
                    // PULIZIA IMMEDIATA DEL CODICE
                    Code = reader["CodArticolo"]?.ToString()?.Trim() ?? "",
                    Description = reader["Desc"]?.ToString()?.Trim() ?? "",
                    Category = reader["NomeCategoria"]?.ToString() ?? "",
                    Subcategory = reader["NomeSottocategoria"]?.ToString() ?? "",
                    Unit = reader["Udm"]?.ToString() ?? "PZ",
                    ListPrice = reader["PrezzoNetto1"] != DBNull.Value ? Convert.ToDecimal(reader["PrezzoNetto1"]) : 0,
                    UnitCost = reader["PrezzoNettoForn"] != DBNull.Value ? Convert.ToDecimal(reader["PrezzoNettoForn"]) : 0,
                    EasyfattSupplierId = reader["IDFornitore"] != DBNull.Value ? Convert.ToInt32(reader["IDFornitore"]) : 0,
                    SupplierCode = reader["CodArticoloForn"]?.ToString() ?? "",
                    Manufacturer = reader["Produttore"]?.ToString() ?? "",
                    Barcode = reader["CodBarre"]?.ToString() ?? "",
                    Notes = reader["Note"]?.ToString() ?? ""
                });
            }
            fbConn.Close();

            // --- LOGICA DI MAPPING FORNITORI (Invariata) ---
            using var fbConn2 = new FbConnection(connStr);
            fbConn2.Open();
            using var cmd2 = new FbCommand(@"SELECT ""IDAnagr"", ""Nome"", ""PartitaIva"" FROM ""TAnagrafica"" WHERE ""Fornitore"" = 1", fbConn2);
            using var reader2 = cmd2.ExecuteReader();
            var easyfattSupplierMap = new Dictionary<int, (string Name, string Vat)>();
            while (reader2.Read())
            {
                easyfattSupplierMap[Convert.ToInt32(reader2["IDAnagr"])] = (reader2["Nome"]?.ToString() ?? "", reader2["PartitaIva"]?.ToString()?.Trim() ?? "");
            }
            fbConn2.Close();

            using var mysqlConn = _db.Open();
            var atecSuppliers = mysqlConn.Query<SupplierListItem>("SELECT id, company_name AS CompanyName, vat_number AS VatNumber FROM suppliers").ToList();
            var vatToSupplier = new Dictionary<string, SupplierListItem>();
            foreach (var s in atecSuppliers.Where(s => !string.IsNullOrEmpty(s.VatNumber)))
            {
                var vat = s.VatNumber.Trim();
                if (!vatToSupplier.ContainsKey(vat)) vatToSupplier[vat] = s;
            }

            // --- CORREZIONE DUPLICATI ---
            // Recuperiamo ID e CODE (pulito) dal database ATEC
            var existingCatalog = mysqlConn.Query<(int Id, string Code)>(
                "SELECT id, TRIM(code) as Code FROM catalog_items WHERE is_active = 1").ToList();

            // Creiamo un dizionario per trovare velocemente l'ID partendo dal Codice
            var codeToIdMap = existingCatalog
                .Where(c => !string.IsNullOrEmpty(c.Code))
                .GroupBy(c => c.Code)
                .ToDictionary(g => g.Key, g => g.First().Id);

            foreach (var art in articles)
            {
                // Risolvi fornitore
                if (art.EasyfattSupplierId > 0 && easyfattSupplierMap.TryGetValue(art.EasyfattSupplierId, out var supInfo))
                {
                    art.ResolvedSupplierName = supInfo.Name;
                    if (!string.IsNullOrEmpty(supInfo.Vat) && vatToSupplier.TryGetValue(supInfo.Vat, out var atecSup))
                        art.ResolvedSupplierId = atecSup.Id;
                }

                // Confronto robusto su codice pulito
                if (!string.IsNullOrEmpty(art.Code) && codeToIdMap.TryGetValue(art.Code, out int existingId))
                {
                    art.Status = "DUPLICATO";
                    art.ExistingId = existingId; // FONDAMENTALE: permette l'UPDATE invece della INSERT
                    art.Action = "UPDATE";       // Suggeriamo già l'aggiornamento
                }
                else
                {
                    art.Status = "NUOVO";
                    art.Action = "INSERT";
                }
            }

            var summary = new
            {
                TotalFound = articles.Count,
                NewCount = articles.Count(a => a.Status == "NUOVO"),
                DuplicateCount = articles.Count(a => a.Status == "DUPLICATO"),
                Articles = articles
            };

            return Ok(ApiResponse<object>.Ok(summary));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore: {ex.Message}"));
        }
    }

    /// <summary>
    /// Importa gli articoli selezionati nel catalogo ATEC PM
    /// </summary>
    [HttpPost("easyfatt/articles")]
    public IActionResult ImportArticles([FromBody] ImportArticlesRequest req)
    {
        try
        {
            using var c = _db.Open();
            int imported = 0;
            int updated = 0;
            int skipped = 0;

            foreach (var art in req.Articles)
            {
                if (art.Action == "SKIP")
                {
                    skipped++;
                    continue;
                }

                if (art.Action == "UPDATE" && art.ExistingId > 0)
                {
                    c.Execute(@"UPDATE catalog_items SET code=@Code, description=@Description, 
                        category=@Category, subcategory=@Subcategory, unit=@Unit, 
                        unit_cost=@UnitCost, list_price=@ListPrice, supplier_id=@ResolvedSupplierId,
                        supplier_code=@SupplierCode, manufacturer=@Manufacturer, 
                        barcode=@Barcode, notes=@Notes, easyfatt_id=@EasyfattId
                        WHERE id=@ExistingId", art);
                    updated++;
                }
                else
                {
                    c.Execute(@"INSERT INTO catalog_items (code, description, category, subcategory, unit, 
                        unit_cost, list_price, supplier_id, supplier_code, manufacturer, barcode, notes, is_active, easyfatt_id) 
                        VALUES (@Code, @Description, @Category, @Subcategory, @Unit, 
                        @UnitCost, @ListPrice, @ResolvedSupplierId, @SupplierCode, @Manufacturer, @Barcode, @Notes, 1, @EasyfattId)", art);
                    imported++;
                }
            }

            return Ok(ApiResponse<object>.Ok(new { Imported = imported, Updated = updated, Skipped = skipped }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore import: {ex.Message}"));
        }
    }

    /// <summary>
    /// Legge i clienti da Easyfatt e li confronta con quelli esistenti in ATEC PM
    /// </summary>
    [HttpGet("easyfatt/customers")]
    public IActionResult GetEasyfattCustomers([FromQuery] string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return BadRequest("filePath richiesto");

        try
        {
            var connStr = BuildConnectionString(filePath);
            using var fbConn = new FbConnection(connStr);
            fbConn.Open();

            using var cmd = new FbCommand(@"
                SELECT ""IDAnagr"", ""CodAnagr"", ""Nome"", ""Referente"", ""Email"", ""Pec"", 
                       ""Tel"", ""Cell"", ""Indirizzo"", ""Cap"", ""Citta"", ""Prov"", 
                       ""PartitaIva"", ""CodiceFiscale"", ""PagamentoDefault"", 
                       ""FE_CodUfficio"", ""Note""
                FROM ""TAnagrafica"" 
                WHERE ""Cliente"" = 1
                ORDER BY ""Nome""", fbConn);

            using var reader = cmd.ExecuteReader();

            var easyfattCustomers = new List<EasyfattCustomerDto>();
            while (reader.Read())
            {
                var indirizzo = reader["Indirizzo"]?.ToString() ?? "";
                var cap = reader["Cap"]?.ToString() ?? "";
                var citta = reader["Citta"]?.ToString() ?? "";
                var prov = reader["Prov"]?.ToString() ?? "";
                var address = $"{indirizzo}, {cap} {citta} ({prov})".Trim(' ', ',');

                easyfattCustomers.Add(new EasyfattCustomerDto
                {
                    EasyfattId = reader["IDAnagr"] != DBNull.Value ? Convert.ToInt32(reader["IDAnagr"]) : 0,
                    EasyfattCode = reader["CodAnagr"]?.ToString()?.Trim() ?? "",
                    CompanyName = reader["Nome"]?.ToString()?.Trim() ?? "",
                    ContactName = reader["Referente"]?.ToString()?.Trim() ?? "",
                    Email = reader["Email"]?.ToString()?.Trim() ?? "",
                    Pec = reader["Pec"]?.ToString()?.Trim() ?? "",
                    Phone = reader["Tel"]?.ToString()?.Trim() ?? "",
                    Cell = reader["Cell"]?.ToString()?.Trim() ?? "",
                    Address = address,
                    VatNumber = reader["PartitaIva"]?.ToString()?.Trim() ?? "",
                    FiscalCode = reader["CodiceFiscale"]?.ToString()?.Trim() ?? "",
                    PaymentTerms = reader["PagamentoDefault"]?.ToString()?.Trim() ?? "",
                    SdiCode = reader["FE_CodUfficio"]?.ToString()?.Trim() ?? "",
                    Notes = reader["Note"]?.ToString() ?? ""
                });
            }
            fbConn.Close();

            using var mysqlConn = _db.Open();
            var existing = mysqlConn.Query<CustomerListItem>(
                "SELECT id, company_name AS CompanyName, vat_number AS VatNumber FROM customers").ToList();

            var existingVats = new Dictionary<string, CustomerListItem>();
            foreach (var s in existing.Where(s => !string.IsNullOrEmpty(s.VatNumber)))
            {
                var vat = s.VatNumber.Trim();
                if (!existingVats.ContainsKey(vat))
                    existingVats[vat] = s;
            }

            foreach (var cust in easyfattCustomers)
            {
                var vat = cust.VatNumber?.Trim() ?? "";
                if (!string.IsNullOrEmpty(vat) && existingVats.TryGetValue(vat, out var match))
                {
                    cust.Status = "DUPLICATO";
                    cust.ExistingId = match.Id;
                    cust.ExistingName = match.CompanyName;
                    cust.Action = "SKIP";
                }
                else
                {
                    cust.Status = "NUOVO";
                    cust.Action = "INSERT";
                }
            }

            var summary = new
            {
                TotalFound = easyfattCustomers.Count,
                NewCount = easyfattCustomers.Count(c => c.Status == "NUOVO"),
                DuplicateCount = easyfattCustomers.Count(c => c.Status == "DUPLICATO"),
                Customers = easyfattCustomers
            };

            return Ok(ApiResponse<object>.Ok(summary));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore: {ex.Message}"));
        }
    }

    /// <summary>
    /// Importa i clienti selezionati in ATEC PM
    /// </summary>
    [HttpPost("easyfatt/customers")]
    public IActionResult ImportCustomers([FromBody] ImportCustomersRequest req)
    {
        try
        {
            using var c = _db.Open();
            int imported = 0;
            int updated = 0;
            int skipped = 0;

            foreach (var cust in req.Customers)
            {
                if (cust.Action == "SKIP")
                {
                    skipped++;
                    continue;
                }

                if (cust.Action == "UPDATE" && cust.ExistingId > 0)
                {
                    c.Execute(@"UPDATE customers SET company_name=@CompanyName, contact_name=@ContactName, 
                        email=@Email, pec=@Pec, phone=@Phone, cell=@Cell, address=@Address, 
                        vat_number=@VatNumber, fiscal_code=@FiscalCode, payment_terms=@PaymentTerms, 
                        sdi_code=@SdiCode, easyfatt_code=@EasyfattCode, easyfatt_id=@EasyfattId, 
                        notes=@Notes WHERE id=@ExistingId", cust);
                    updated++;
                }
                else
                {
                    c.Execute(@"INSERT INTO customers (company_name, contact_name, email, pec, phone, cell, 
                        address, vat_number, fiscal_code, payment_terms, sdi_code, easyfatt_code, 
                        easyfatt_id, notes, is_active) 
                        VALUES (@CompanyName, @ContactName, @Email, @Pec, @Phone, @Cell, 
                        @Address, @VatNumber, @FiscalCode, @PaymentTerms, @SdiCode, @EasyfattCode, 
                        @EasyfattId, @Notes, 1)", cust);
                    imported++;
                }
            }

            return Ok(ApiResponse<object>.Ok(new { Imported = imported, Updated = updated, Skipped = skipped }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail($"Errore import: {ex.Message}"));
        }
    }

    private string BuildConnectionString(string filePath)
    {
        var appDir = AppContext.BaseDirectory;
        var fbClientPath = _config["Easyfatt:FirebirdClientPath"]
            ?? Path.Combine(appDir, "Firebird", "fbclient.dll");

        return $"Database={filePath};ServerType=1;User=SYSDBA;Password=masterkey;ClientLibrary={fbClientPath}";
    }
}
