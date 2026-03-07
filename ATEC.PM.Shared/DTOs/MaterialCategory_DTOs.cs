namespace ATEC.PM.Shared.DTOs;

public class MaterialCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string MarkupCode { get; set; } = "";
    public decimal MarkupValue { get; set; }  // risolto dal JOIN
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MaterialCategorySaveRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string MarkupCode { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
