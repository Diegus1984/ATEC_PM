using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FirebirdSql.Data.FirebirdClient;
using Dapper;
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

            var existingVats = existing
                .Where(s => !string.IsNullOrEmpty(s.VatNumber))
                .ToDictionary(s => s.VatNumber.Trim(), s => s);

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

    private string BuildConnectionString(string filePath)
    {
        var appDir = AppContext.BaseDirectory;
        var fbClientPath = _config["Easyfatt:FirebirdClientPath"]
            ?? Path.Combine(appDir, "Firebird", "fbclient.dll");

        return $"Database={filePath};ServerType=1;User=SYSDBA;Password=masterkey;ClientLibrary={fbClientPath}";
    }
}
