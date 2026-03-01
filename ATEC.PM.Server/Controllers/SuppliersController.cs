using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly DbService _db;
    public SuppliersController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<SupplierListItem>(
            "SELECT id, company_name AS CompanyName, contact_name AS ContactName, email, phone, vat_number AS VatNumber, is_active AS IsActive FROM suppliers ORDER BY company_name").ToList();
        return Ok(ApiResponse<List<SupplierListItem>>.Ok(rows));
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var s = c.QueryFirstOrDefault<SupplierSaveRequest>(
            "SELECT id, company_name AS CompanyName, contact_name AS ContactName, email, phone, address, vat_number AS VatNumber, notes, is_active AS IsActive FROM suppliers WHERE id=@Id", new { Id = id });
        if (s == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<SupplierSaveRequest>.Ok(s));
    }

    [HttpPost]
    public IActionResult Create([FromBody] SupplierSaveRequest req)
    {
        using var c = _db.Open();
        var newId = c.ExecuteScalar<int>(
            "INSERT INTO suppliers (company_name,contact_name,email,phone,address,vat_number,notes,is_active) VALUES (@CompanyName,@ContactName,@Email,@Phone,@Address,@VatNumber,@Notes,@IsActive); SELECT LAST_INSERT_ID()", req);
        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] SupplierSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute("UPDATE suppliers SET company_name=@CompanyName,contact_name=@ContactName,email=@Email,phone=@Phone,address=@Address,vat_number=@VatNumber,notes=@Notes,is_active=@IsActive WHERE id=@Id", req);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        c.Execute("UPDATE suppliers SET is_active=0 WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
