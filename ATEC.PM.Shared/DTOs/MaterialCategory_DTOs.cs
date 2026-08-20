using System.ComponentModel.DataAnnotations;

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

/// <summary>Limiti ricalcati sulle colonne di <c>material_categories</c> — vedi <see cref="DepartmentSaveRequest"/>.</summary>
public class MaterialCategorySaveRequest
{
    public int Id { get; set; }

    [MaxLength(200, ErrorMessage = "Il nome non può superare 200 caratteri")]
    public string Name { get; set; } = "";

    // DECIMAL(5,3) su entrambi i ricarichi.
    [Range(0, 99.999, ErrorMessage = "Il ricarico deve essere fra 0 e 99,999")]
    public decimal DefaultMarkup { get; set; } = 1.300m;

    [Range(0, 99.999, ErrorMessage = "Il ricarico provvigione deve essere fra 0 e 99,999")]
    public decimal DefaultCommissionMarkup { get; set; } = 1.100m;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
