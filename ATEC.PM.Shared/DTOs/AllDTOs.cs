using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = "";

    public static ApiResponse<T> Ok(T data, string msg = "") => new() { Success = true, Data = data, Message = msg };
    public static ApiResponse<T> Fail(string msg) => new() { Success = false, Message = msg };
}

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginResponse
{
    public string Token { get; set; } = "";
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string UserRole { get; set; } = "";
}

public class EmployeeListItem
{
    public int Id { get; set; }
    public string BadgeNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string EmpType { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal HourlyCost { get; set; }
    public decimal WeeklyHours { get; set; }
    public string Username { get; set; } = "";
}

public class EmployeeSaveRequest
{
    public int Id { get; set; }
    public string BadgeNumber { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string EmpType { get; set; } = "INTERNAL";
    public int? SupplierId { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal WeeklyHours { get; set; } = 40;
    public DateTime? HireDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string Notes { get; set; } = "";
}

// === CUSTOMERS ===
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

// === SUPPLIERS ===
public class SupplierListItem
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public bool IsActive { get; set; }
}

public class SupplierSaveRequest
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

// === PROJECTS ===
public class ProjectListItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PmName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDatePlanned { get; set; }
    public decimal Revenue { get; set; }
    public decimal BudgetHoursTotal { get; set; }
}

public class ProjectSaveRequest
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int CustomerId { get; set; }
    public int PmId { get; set; }
    public string Description { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDatePlanned { get; set; }
    public decimal BudgetTotal { get; set; }
    public decimal BudgetHoursTotal { get; set; }
    public decimal Revenue { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string Priority { get; set; } = "MEDIUM";
    public string ServerPath { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool CreateDefaultPhases { get; set; } = true;
}

public class PhaseListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public string Status { get; set; } = "";
    public int ProgressPct { get; set; }
    public int SortOrder { get; set; }
    public decimal HoursWorked { get; set; }
}

public class LookupItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

// === TIMESHEET ===
public class TimesheetEntryDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int ProjectPhaseId { get; set; }
    public DateTime WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string EntryType { get; set; } = "REGULAR";
    public string Notes { get; set; } = "";
    public string PhaseDisplay { get; set; } = "";
}

public class TimesheetSaveRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int ProjectPhaseId { get; set; }
    public DateTime WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string EntryType { get; set; } = "REGULAR";
    public string Notes { get; set; } = "";
}

public class TimesheetPhaseOption
{
    public int PhaseId { get; set; }
    public string Display { get; set; } = "";
}

public class TimesheetSummaryRow
{
    public string PhaseDisplay { get; set; } = "";
    public decimal RegularHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal TravelHours { get; set; }
    public decimal TotalHours { get; set; }
}

// === DASHBOARD ===
public class DashboardData
{
    public int ActiveProjects { get; set; }
    public int DraftProjects { get; set; }
    public int CompletedProjects { get; set; }
    public int TotalEmployees { get; set; }
    public int TotalCustomers { get; set; }
    public decimal HoursThisMonth { get; set; }
    public decimal HoursThisWeek { get; set; }
    public decimal TotalRevenue { get; set; }
    public List<DashboardProjectRow> RecentProjects { get; set; } = new();
}

public class DashboardProjectRow
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal HoursWorked { get; set; }
    public decimal BudgetHours { get; set; }
}

// === FILES ===
public class FileItem
{
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public string RelativePath { get; set; } = "";
    public DateTime? Modified { get; set; }
}

public class FileTreeItem
{
    public string Name { get; set; } = "";
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public string RelativePath { get; set; } = "";
    public DateTime? Modified { get; set; }
    public List<FileTreeItem> Children { get; set; } = new();
}

// === IMPORT EASYFATT ===
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
    public string Status { get; set; } = "NUOVO";  // NUOVO o DUPLICATO
    public int ExistingId { get; set; }
    public string ExistingName { get; set; } = "";
    public string Action { get; set; } = "";  // INSERT, UPDATE, SKIP
}

public class ImportSuppliersRequest
{
    public List<EasyfattSupplierDto> Suppliers { get; set; } = new();
}

// === IMPORT CLIENTI EASYFATT ===
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

// === CATALOGO ARTICOLI ===
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
    // Risolto lato server
    public int? ResolvedSupplierId { get; set; }
    public string ResolvedSupplierName { get; set; } = "";
}

public class ImportArticlesRequest
{
    public List<EasyfattArticleDto> Articles { get; set; } = new();
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

public class SetCredentialsRequest
{
    public int EmployeeId { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class TemplateFolderInfo
{
    public List<string> Folders { get; set; } = new();
    public List<TemplateFileInfo> Files { get; set; } = new();
}

public class TemplateFileInfo
{
    public string RelativePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
}

// === DDP (Distinta Di Produzione) ===
public class BomItemListItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? CatalogItemId { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost => Quantity * UnitCost;
    public string SupplierName { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string ItemStatus { get; set; } = "TO_ORDER";
    public string RequestedBy { get; set; } = "";
    public string DaneaRef { get; set; } = "";
    public DateTime? DateNeeded { get; set; }
    public string Destination { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";
    public DateTime? CreatedAt { get; set; }
}

public class BomItemSaveRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? CatalogItemId { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "PZ";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public int? SupplierId { get; set; }
    public string Manufacturer { get; set; } = "";
    public string ItemStatus { get; set; } = "TO_ORDER";
    public string RequestedBy { get; set; } = "";
    public string DaneaRef { get; set; } = "";
    public DateTime? DateNeeded { get; set; }
    public string Destination { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DdpType { get; set; } = "COMMERCIAL";
}

