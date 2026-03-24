namespace ATEC.PM.Shared.DTOs;

// ══════════════════════════════════════════════════════════
// CATALOG — Listini → Gruppi → Categorie → Prodotti → Varianti
// ══════════════════════════════════════════════════════════

public class QuotePriceListDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public string Locale { get; set; } = "it";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public int GroupCount { get; set; }
}

public class QuotePriceListSaveDto
{
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public string Locale { get; set; } = "it";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class QuoteGroupDto
{
    public int Id { get; set; }
    public int? PriceListId { get; set; }
    public string PriceListName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int CategoryCount { get; set; }
    public int ProductCount { get; set; }
    public List<QuoteCategoryDto> Categories { get; set; } = new();
}

public class QuoteCategoryDto
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int? ParentId { get; set; }
    public string GroupName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int ProductCount { get; set; }
    public List<QuoteCategoryDto> Children { get; set; } = new();
    public List<QuoteProductDto> Products { get; set; } = new();
}

public class QuoteProductDto
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string ItemType { get; set; } = "product";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DescriptionRtf { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string AttachmentPath { get; set; } = "";
    public bool AutoInclude { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<QuoteProductVariantDto> Variants { get; set; } = new();
}

public class QuoteProductVariantDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal CostPrice { get; set; }
    public decimal MarkupValue { get; set; } = 1.300m;
    public decimal SellPrice => CostPrice * MarkupValue;
    public int SortOrder { get; set; }
}

// DTO per creazione/modifica
public class QuoteGroupSaveDto
{
    public int? PriceListId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class QuoteCategorySaveDto
{
    public int GroupId { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CategoryMoveRequest
{
    public int? NewParentId { get; set; }
    public int NewGroupId { get; set; }
}

public class ProductMoveRequest
{
    public int CategoryId { get; set; }
}

public class QuoteProductSaveDto
{
    public int CategoryId { get; set; }
    public string ItemType { get; set; } = "product";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DescriptionRtf { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string AttachmentPath { get; set; } = "";
    public bool AutoInclude { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<QuoteProductVariantSaveDto> Variants { get; set; } = new();
}

public class QuoteProductVariantSaveDto
{
    public int Id { get; set; }  // 0 = nuovo, >0 = aggiorna
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal CostPrice { get; set; }
    public decimal MarkupValue { get; set; } = 1.300m;
    public int SortOrder { get; set; }
}

// ═══════════════════════════════════════════════
// Albero completo per il TreeView
// ═══════════════════════════════════════════════

public class QuoteCatalogTreeDto
{
    public List<QuoteGroupDto> Groups { get; set; } = new();
    public int TotalGroups { get; set; }
    public int TotalCategories { get; set; }
    public int TotalProducts { get; set; }
}

// ══════════════════════════════════════════════════════════
// QUOTES — Preventivi
// ══════════════════════════════════════════════════════════

public class QuoteDto
{
    public int Id { get; set; }
    public string QuoteNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string ContactName1 { get; set; } = "";
    public string ContactName2 { get; set; } = "";
    public string ContactName3 { get; set; } = "";
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; } = 60;
    public string PaymentType { get; set; } = "";
    public string Language { get; set; } = "it";
    public string Status { get; set; } = "draft";
    public string QuoteType { get; set; } = "SERVICE";
    public int Revision { get; set; }
    public int? ParentQuoteId { get; set; }
    public int? PriceListId { get; set; }
    public string PriceListName { get; set; } = "";
    public int? GroupId { get; set; }
    public string GroupName { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal DiscountAbs { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public decimal TotalWithVat { get; set; }
    public decimal CostTotal { get; set; }
    public decimal Profit { get; set; }
    public bool ShowItemPrices { get; set; } = true;
    public bool ShowSummary { get; set; } = true;
    public bool ShowSummaryPrices { get; set; } = true;
    public bool HideQuantities { get; set; }
    public string NotesInternal { get; set; } = "";
    public string NotesQuote { get; set; } = "";
    public int? ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public int? AssignedTo { get; set; }
    public string AssignedToName { get; set; } = "";
    public int CreatedBy { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? ConvertedAt { get; set; }
    public List<QuoteItemDto> Items { get; set; } = new();
}

public class QuoteItemDto
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public int? ProductId { get; set; }
    public int? VariantId { get; set; }
    public string ItemType { get; set; } = "product";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DescriptionRtf { get; set; } = "";
    public string Unit { get; set; } = "nr.";
    public decimal Quantity { get; set; } = 1;
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal VatPct { get; set; } = 22.00m;
    public decimal LineTotal { get; set; }
    public decimal LineProfit { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsConfirmed { get; set; }
    public int? ParentItemId { get; set; }
    public bool IsAutoInclude { get; set; }
}

public class QuoteSaveDto
{
    public string QuoteType { get; set; } = "SERVICE";
    public int? PriceListId { get; set; }
    public string Title { get; set; } = "";
    public int CustomerId { get; set; }
    public string ContactName1 { get; set; } = "";
    public string ContactName2 { get; set; } = "";
    public string ContactName3 { get; set; } = "";
    public int DeliveryDays { get; set; }
    public int ValidityDays { get; set; } = 60;
    public string PaymentType { get; set; } = "";
    public string Language { get; set; } = "it";
    public int? GroupId { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal DiscountAbs { get; set; }
    public bool ShowItemPrices { get; set; } = true;
    public bool ShowSummary { get; set; } = true;
    public bool ShowSummaryPrices { get; set; } = true;
    public bool HideQuantities { get; set; }
    public string NotesInternal { get; set; } = "";
    public string NotesQuote { get; set; } = "";
    public int? AssignedTo { get; set; }
}

public class QuoteItemSaveDto
{
    public int? ProductId { get; set; }
    public int? VariantId { get; set; }
    public string ItemType { get; set; } = "product";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DescriptionRtf { get; set; } = "";
    public string Unit { get; set; } = "nr.";
    public decimal Quantity { get; set; } = 1;
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal VatPct { get; set; } = 22.00m;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsConfirmed { get; set; }
    public int? ParentItemId { get; set; }
    public bool IsAutoInclude { get; set; }
}

public class QuoteStatusChangeDto
{
    public string NewStatus { get; set; } = "";
    public string Notes { get; set; } = "";
}

public class QuoteRevisionDto
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public int Revision { get; set; }
    public string ChangeNotes { get; set; } = "";
    public int CreatedBy { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class QuoteDocumentDto
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
    public string MimeType { get; set; } = "";
    public int UploadedBy { get; set; }
    public string UploadedByName { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}

public class QuoteStatusLogDto
{
    public int Id { get; set; }
    public string OldStatus { get; set; } = "";
    public string NewStatus { get; set; } = "";
    public int ChangedBy { get; set; }
    public string ChangedByName { get; set; } = "";
    public DateTime ChangedAt { get; set; }
    public string Notes { get; set; } = "";
}

// ══════════════════════════════════════════════════════════
// IMPORT
// ══════════════════════════════════════════════════════════

public class QuoteCatalogImportDto
{
    public List<QuoteCatalogImportListino> PriceLists { get; set; } = new();
}

public class QuoteCatalogImportListino
{
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public string Locale { get; set; } = "it";
    public List<QuoteCatalogImportGroup> Groups { get; set; } = new();
}

public class QuoteCatalogImportGroup
{
    public string Name { get; set; } = "";
    public List<QuoteCatalogImportCategory> Categories { get; set; } = new();
}

public class QuoteCatalogImportCategory
{
    public string Name { get; set; } = "";
    public List<QuoteCatalogImportProduct> Products { get; set; } = new();
}

public class QuoteCatalogImportProduct
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ItemType { get; set; } = "product";
    public string Description { get; set; } = "";
    public string Position { get; set; } = "";
    public List<QuoteCatalogImportVariant> Variants { get; set; } = new();
}

public class QuoteCatalogImportVariant
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal CostPrice { get; set; }
    public decimal MarkupValue { get; set; } = 1.300m;
}

public class QuoteStatsDto
{
    public int TotalQuotes { get; set; }
    public int QuotesDraft { get; set; }
    public int QuotesSent { get; set; }
    public int QuotesAccepted { get; set; }
    public int QuotesRejected { get; set; }
    public int QuotesConverted { get; set; }
    public decimal TotalValue { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal ConversionRate { get; set; }
    public decimal AvgProfit { get; set; }
}
