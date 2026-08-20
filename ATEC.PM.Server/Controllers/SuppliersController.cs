using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Anagrafica fornitori. Dal 20/08 la LETTURA dell'anagrafica (contatti, P.IVA, codice fiscale)
// e la scrittura stanno entrambe dietro «nav.fornitori»; per i picker e le griglie c'e'
// GET /api/suppliers/lookup, che di quei dati non ne porta.
// 🪤 Prima il commento qui diceva «lettura libera (serve ai picker)»: era vero e per questo
// faceva danno — i picker avevano bisogno dei NOMI e si portavano via l'anagrafica fiscale.
[ApiController]
[Route("api/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly DbService _db;
    public SuppliersController(DbService db) => _db = db;

    /// <summary>
    /// L'anagrafica fornitori completa (contatti, P.IVA, codice fiscale): dietro
    /// <c>nav.fornitori</c> come le scritture. Chi deve solo SCEGLIERE un fornitore usa
    /// <see cref="GetLookup"/>.
    /// </summary>
    [HttpGet]
    [RequireFeature("nav.fornitori")]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<SupplierListItem>(
            "SELECT id, company_name AS CompanyName, contact_name AS ContactName, email, phone, vat_number AS VatNumber, fiscal_code AS FiscalCode, is_active AS IsActive FROM suppliers ORDER BY company_name").ToList();
        return Ok(ApiResponse<List<SupplierListItem>>.Ok(rows));
    }

    /// <summary>Il fornitore intero (aggiunge indirizzo e note): stessa casa dell'elenco.
    /// Chiudere solo la lista e lasciare aperto il dettaglio per id è l'errore già pagato una
    /// volta con la Dashboard commessa.</summary>
    [HttpGet("{id}")]
    [RequireFeature("nav.fornitori")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var s = c.QueryFirstOrDefault<SupplierSaveRequest>(
            "SELECT id, company_name AS CompanyName, contact_name AS ContactName, email, phone, address, vat_number AS VatNumber, fiscal_code AS FiscalCode, notes, is_active AS IsActive FROM suppliers WHERE id=@Id", new { Id = id });
        if (s == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<SupplierSaveRequest>.Ok(s));
    }

    /// <summary>
    /// I fornitori per una <b>combo</b>: ragione sociale, referente, attivo. Niente email,
    /// niente telefono, niente P.IVA, niente codice fiscale.
    ///
    /// <para>Aperta a tutti gli autenticati di proposito: il fornitore si sceglie dalla riga
    /// di una DDP e dalla scheda articolo, pagine che con l'anagrafica fiscale non c'entrano
    /// niente.</para>
    /// </summary>
    [HttpGet("lookup")]
    public IActionResult GetLookup()
    {
        using var c = _db.Open();
        var rows = c.Query<SupplierLookupItem>(
            @"SELECT id AS Id, company_name AS CompanyName, contact_name AS ContactName,
                     is_active AS IsActive
              FROM suppliers ORDER BY company_name").ToList();
        return Ok(ApiResponse<List<SupplierLookupItem>>.Ok(rows));
    }

    [RequireFeature("nav.fornitori")]
    [HttpPost]
    public IActionResult Create([FromBody] SupplierSaveRequest req)
    {
        using var c = _db.Open();
        var newId = c.ExecuteScalar<int>(
            "INSERT INTO suppliers (company_name,contact_name,email,phone,address,vat_number,fiscal_code,notes,is_active) VALUES (@CompanyName,@ContactName,@Email,@Phone,@Address,@VatNumber,@FiscalCode,@Notes,@IsActive); SELECT LAST_INSERT_ID()", req);
        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    [RequireFeature("nav.fornitori")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] SupplierSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute("UPDATE suppliers SET company_name=@CompanyName,contact_name=@ContactName,email=@Email,phone=@Phone,address=@Address,vat_number=@VatNumber,fiscal_code=@FiscalCode,notes=@Notes,is_active=@IsActive WHERE id=@Id", req);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [RequireFeature("nav.fornitori")]
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        c.Execute("UPDATE suppliers SET is_active=0 WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }
}