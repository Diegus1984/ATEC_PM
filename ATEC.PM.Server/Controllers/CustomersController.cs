using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly DbService _db;
    public CustomersController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<CustomerListItem>(@"
            SELECT id, company_name AS CompanyName, contact_name AS ContactName, 
                   email, pec AS Pec, phone, cell AS Cell, 
                   vat_number AS VatNumber, fiscal_code AS FiscalCode, 
                   sdi_code AS SdiCode, is_active AS IsActive 
            FROM customers ORDER BY company_name").ToList();
        return Ok(ApiResponse<List<CustomerListItem>>.Ok(rows));
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var cust = c.QueryFirstOrDefault<CustomerSaveRequest>(@"
            SELECT id, company_name AS CompanyName, contact_name AS ContactName, 
                   email, pec AS Pec, phone, cell AS Cell, address, 
                   vat_number AS VatNumber, fiscal_code AS FiscalCode, 
                   payment_terms AS PaymentTerms, sdi_code AS SdiCode, 
                   notes, is_active AS IsActive 
            FROM customers WHERE id=@Id", new { Id = id });
        if (cust == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<CustomerSaveRequest>.Ok(cust));
    }

    [HttpPost]
    public IActionResult Create([FromBody] CustomerSaveRequest req)
    {
        using var c = _db.Open();
        var newId = c.ExecuteScalar<int>(@"
            INSERT INTO customers (company_name,contact_name,email,pec,phone,cell,address,
                                   vat_number,fiscal_code,payment_terms,sdi_code,notes,is_active) 
            VALUES (@CompanyName,@ContactName,@Email,@Pec,@Phone,@Cell,@Address,
                    @VatNumber,@FiscalCode,@PaymentTerms,@SdiCode,@Notes,@IsActive); 
            SELECT LAST_INSERT_ID()", req);
        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] CustomerSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"UPDATE customers SET company_name=@CompanyName,contact_name=@ContactName,
            email=@Email,pec=@Pec,phone=@Phone,cell=@Cell,address=@Address,
            vat_number=@VatNumber,fiscal_code=@FiscalCode,payment_terms=@PaymentTerms,
            sdi_code=@SdiCode,notes=@Notes,is_active=@IsActive WHERE id=@Id", req);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        c.Execute("UPDATE customers SET is_active=0 WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
