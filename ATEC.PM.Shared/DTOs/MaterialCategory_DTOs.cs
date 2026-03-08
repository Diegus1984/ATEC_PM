namespace ATEC.PM.Shared.DTOs;

// === TEMPLATE CATEGORIE (configurazione globale) ===
public class MaterialCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string MarkupCode { get; set; } = ""; // legacy, può restare per riferimento
    public decimal DefaultMarkup { get; set; } = 1.300m;
    public decimal DefaultCommissionMarkup { get; set; } = 1.100m;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MaterialCategorySaveRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal DefaultMarkup { get; set; } = 1.300m;
    public decimal DefaultCommissionMarkup { get; set; } = 1.100m;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
