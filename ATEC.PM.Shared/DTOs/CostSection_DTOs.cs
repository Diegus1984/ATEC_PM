using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

// === GRUPPI SEZIONI COSTO ===
public class CostSectionGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CostSectionGroupSaveRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

// === TEMPLATE SEZIONI COSTO ===
public class CostSectionTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SectionType { get; set; } = "IN_SEDE";
    public int GroupId { get; set; }
    public string GroupName { get; set; } = "";
    public bool IsDefault { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<int> DepartmentIds { get; set; } = new();
    public List<string> DepartmentCodes { get; set; } = new();
}

public class CostSectionTemplateSaveRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SectionType { get; set; } = "IN_SEDE";
    public int GroupId { get; set; }
    public bool IsDefault { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<int> DepartmentIds { get; set; } = new();
}

// === SAVE REPARTI PER SEZIONE ===
public class SectionDepartmentsRequest
{
    public List<int> DepartmentIds { get; set; } = new();
}
