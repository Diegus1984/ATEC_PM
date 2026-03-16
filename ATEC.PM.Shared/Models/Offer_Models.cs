using System;

namespace ATEC.PM.Shared.Models;

public class Offer
{
    public int Id { get; set; }
    public string OfferCode { get; set; } = "";
    public int Revision { get; set; } = 1;
    public int? ParentOfferId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = "";
    public string Status { get; set; } = "BOZZA";
    public int? ConvertedProjectId { get; set; }
    public string? ConvertedProjectCode { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalSale { get; set; }
    public decimal FinalOfferPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class OfferCreateDto
{
    public int CustomerId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

public class OfferUpdateDto
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "BOZZA";
}
