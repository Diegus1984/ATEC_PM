namespace ATEC.PM.Shared.DTOs;

public class MarkupCoefficientDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoefficientType { get; set; } = "MATERIAL"; // MATERIAL | RESOURCE
    public decimal MarkupValue { get; set; } = 1.0m;
    public decimal? HourlyCost { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MarkupCoefficientSaveRequest
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoefficientType { get; set; } = "MATERIAL";
    public decimal MarkupValue { get; set; } = 1.0m;
    public decimal? HourlyCost { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
