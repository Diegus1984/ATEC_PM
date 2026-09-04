using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

public class EasyfattSupplierDto
{
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "NUOVO";
    public int ExistingId { get; set; }
    public string ExistingName { get; set; } = "";
    public string Action { get; set; } = "";
}

public class ImportSuppliersRequest
{
    public List<EasyfattSupplierDto> Suppliers { get; set; } = new();
}

public class EasyfattCustomerDto
{
    public int EasyfattId { get; set; }
    public string EasyfattCode { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Pec { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Cell { get; set; } = "";
    public string Address { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public string PaymentTerms { get; set; } = "";
    public string SdiCode { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "NUOVO";
    public int ExistingId { get; set; }
    public string ExistingName { get; set; } = "";
    public string Action { get; set; } = "";
}

public class ImportCustomersRequest
{
    public List<EasyfattCustomerDto> Customers { get; set; } = new();
}

public class EasyfattArticleDto
{
    public int EasyfattId { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subcategory { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal UnitCost { get; set; }
    public decimal ListPrice { get; set; }
    public int EasyfattSupplierId { get; set; }
    public string SupplierCode { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "NUOVO";
    public int ExistingId { get; set; }
    public string Action { get; set; } = "";
    public bool IsSelected { get; set; }
    public int? ResolvedSupplierId { get; set; }
    public string ResolvedSupplierName { get; set; } = "";
}

public class ImportArticlesRequest
{
    public List<EasyfattArticleDto> Articles { get; set; } = new();
}
