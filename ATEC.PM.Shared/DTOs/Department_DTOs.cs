namespace ATEC.PM.Shared.DTOs;

public class DepartmentDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal HourlyCost { get; set; }
    public decimal DefaultMarkup { get; set; } = 1.450m;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class DepartmentSaveRequest
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal HourlyCost { get; set; }
    public decimal DefaultMarkup { get; set; } = 1.450m;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
