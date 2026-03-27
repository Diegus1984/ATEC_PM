using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Preventivi.Models;

public class QuoteProductGroup : INotifyPropertyChanged
{
    public int ParentId { get; set; }
    public int? ProductId { get; set; }
    public string ParentName { get; set; } = "";
    public string ParentCode { get; set; } = "";
    public string ItemType { get; set; } = "product";
    public string DescriptionRtf { get; set; } = "";
    public bool IsAutoInclude { get; set; }
    public int SortOrder { get; set; }

    public ObservableCollection<QuoteVariantRow> Variants { get; set; } = new();

    // Expand/collapse
    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; Notify(); Notify(nameof(ExpandAngle)); }
    }
    public double ExpandAngle => _isExpanded ? 0 : -90;

    // Display
    public string TypeBadgeLabel => ItemType == "content" ? "Cont." : "Prod.";
    public SolidColorBrush TypeBadgeColor => new(
        (Color)ColorConverter.ConvertFromString(ItemType == "content" ? "#7C3AED" : "#2563EB"));

    public string TotalDisplay
    {
        get
        {
            decimal total = Variants.Where(v => v.IsActive).Sum(v => v.LineTotal);
            return total > 0 ? $"{total:N2}\u20ac" : "";
        }
    }

    public QuoteProductGroup(QuoteItemDto parent, System.Collections.Generic.List<QuoteVariantRow> variants)
    {
        ParentId = parent.Id;
        ProductId = parent.ProductId;
        ParentName = parent.Name;
        ParentCode = parent.Code;
        ItemType = parent.ItemType;
        DescriptionRtf = parent.DescriptionRtf;
        IsAutoInclude = parent.IsAutoInclude;
        SortOrder = parent.SortOrder;
        Variants = new ObservableCollection<QuoteVariantRow>(variants);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class QuoteVariantRow : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public int? VariantId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DescriptionRtf { get; set; } = "";
    public string Unit { get; set; } = "nr.";
    public decimal Quantity { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal VatPct { get; set; }
    public decimal LineTotal { get; set; }
    public decimal LineProfit { get; set; }
    public int SortOrder { get; set; }
    public int? ParentItemId { get; set; }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; Notify(); Notify(nameof(TotalColor)); Notify(nameof(LineTotalDisplay)); }
    }

    private bool _isConfirmed;
    public bool IsConfirmed
    {
        get => _isConfirmed;
        set { _isConfirmed = value; Notify(); }
    }

    // Display & edit helpers
    public string QuantityText { get; set; } = "1";
    public string SellPriceText { get; set; } = "0";
    public string DiscountPctText { get; set; } = "0";
    public string CostPriceText { get; set; } = "0";
    public string MarkupText { get; set; } = "1.000";
    public decimal MarkupValue { get; set; } = 1.0m;

    public string CostTotalDisplay => $"{CostPrice * Quantity:N2}";
    public string LineTotalDisplay => IsActive ? $"{LineTotal:N2}\u20ac" : "\u2014";
    public SolidColorBrush TotalColor => new(
        (Color)ColorConverter.ConvertFromString(IsActive ? "#111827" : "#9CA3AF"));

    public QuoteVariantRow(QuoteItemDto dto)
    {
        Id = dto.Id;
        ProductId = dto.ProductId;
        VariantId = dto.VariantId;
        Code = dto.Code;
        Name = dto.Name;
        DescriptionRtf = dto.DescriptionRtf;
        Unit = dto.Unit;
        Quantity = dto.Quantity;
        CostPrice = dto.CostPrice;
        SellPrice = dto.SellPrice;
        DiscountPct = dto.DiscountPct;
        VatPct = dto.VatPct;
        LineTotal = dto.LineTotal;
        LineProfit = dto.LineProfit;
        SortOrder = dto.SortOrder;
        ParentItemId = dto.ParentItemId;
        _isActive = dto.IsActive;
        _isConfirmed = dto.IsConfirmed;

        QuantityText = dto.Quantity.ToString("G");
        SellPriceText = dto.SellPrice.ToString("N2");
        DiscountPctText = dto.DiscountPct.ToString("G");
        CostPriceText = dto.CostPrice.ToString("N2");
        MarkupValue = dto.CostPrice > 0 ? dto.SellPrice / dto.CostPrice : 1.0m;
        MarkupText = MarkupValue.ToString("N3");
    }

    public void ParseTexts()
    {
        if (decimal.TryParse(QuantityText, out decimal q)) Quantity = q;
        if (decimal.TryParse(CostPriceText, out decimal cp)) CostPrice = cp;
        if (decimal.TryParse(MarkupText, out decimal k)) { MarkupValue = k; SellPrice = CostPrice * k; }
        if (decimal.TryParse(SellPriceText, out decimal p)) SellPrice = p;
        if (decimal.TryParse(DiscountPctText, out decimal d)) DiscountPct = d;
        LineTotal = Quantity * SellPrice * (1 - DiscountPct / 100);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
