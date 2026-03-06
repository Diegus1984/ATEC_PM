namespace ATEC.PM.Shared.DTOs;

public class CatalogItemListItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal UnitCost { get; set; }
    public decimal ListPrice { get; set; }
    public int? SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string SupplierCode { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public bool IsActive { get; set; }
}

public class CatalogItemSaveRequest
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subcategory { get; set; } = "";
    public string Unit { get; set; } = "PZ";
    public decimal UnitCost { get; set; }
    public decimal ListPrice { get; set; }
    public int? SupplierId { get; set; }
    public string SupplierCode { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class CatalogItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subcategory { get; set; } = "";
    public string Unit { get; set; } = "PZ";
    public decimal UnitCost { get; set; }
    public decimal ListPrice { get; set; }
    public int? SupplierId { get; set; }
    public string SupplierCode { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
