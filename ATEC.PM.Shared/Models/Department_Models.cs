namespace ATEC.PM.Shared.Models;

public class Department
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal HourlyCost { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
