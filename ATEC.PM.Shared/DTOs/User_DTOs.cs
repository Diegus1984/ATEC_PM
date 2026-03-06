using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

public class UserListItem
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string UserRole { get; set; } = "";
    public string Status { get; set; } = "";
    public bool HasCredentials { get; set; }
    public string Username { get; set; } = "";
    public List<string> DepartmentCodes { get; set; } = new();
    public List<string> CompetenceCodes { get; set; } = new();
}

public class UserDetailDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string UserRole { get; set; } = "";
    public string Username { get; set; } = "";
    public List<EmployeeDepartmentDto> Departments { get; set; } = new();
    public List<EmployeeCompetenceDto> Competences { get; set; } = new();
}

public class EmployeeDepartmentDto
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public bool IsResponsible { get; set; }
    public bool IsPrimary { get; set; }
}

public class EmployeeCompetenceDto
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public string Notes { get; set; } = "";
}

public class SaveUserRoleRequest
{
    public int EmployeeId { get; set; }
    public string UserRole { get; set; } = "";
}

public class SaveEmployeeDepartmentsRequest
{
    public int EmployeeId { get; set; }
    public List<EmployeeDepartmentDto> Departments { get; set; } = new();
}

public class SaveEmployeeCompetencesRequest
{
    public int EmployeeId { get; set; }
    public List<EmployeeCompetenceDto> Competences { get; set; } = new();
}

public class UserContext
{
    public int EmployeeId { get; set; }
    public string UserRole { get; set; } = "";
    public List<string> DepartmentCodes { get; set; } = new();
    public List<string> ResponsibleDepartmentCodes { get; set; } = new();
    public List<string> CompetenceCodes { get; set; } = new();

    public bool IsAdmin => UserRole == "ADMIN";
    public bool IsPm => UserRole == "PM" || UserRole == "ADMIN";
    public bool IsResponsible => UserRole == "RESP_REPARTO" || IsPm;
}

public class SetCredentialsRequest
{
    public int EmployeeId { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class TemplateFolderInfo
{
    public List<string> Folders { get; set; } = new();
    public List<TemplateFileInfo> Files { get; set; } = new();
}

public class TemplateFileInfo
{
    public string RelativePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
}
