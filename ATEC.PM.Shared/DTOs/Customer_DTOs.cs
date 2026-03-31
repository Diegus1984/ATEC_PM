namespace ATEC.PM.Shared.DTOs;

public class CustomerListItem
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Pec { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Cell { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public string SdiCode { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public string PaymentTerms { get; set; } = "";
    public bool IsActive { get; set; }
}

public class CustomerSaveRequest
{
    public int Id { get; set; }
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
    public bool IsActive { get; set; } = true;
}
