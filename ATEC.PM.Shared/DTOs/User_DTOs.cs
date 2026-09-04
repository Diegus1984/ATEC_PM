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

public class SaveUserStatusRequest
{
    public int EmployeeId { get; set; }
    public bool IsActive { get; set; }
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

public class SetCredentialsRequest
{
    public int EmployeeId { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class ChangePasswordRequest
{
    /// <summary>Valorizzato solo per il cambio password dalla schermata di login (senza sessione).</summary>
    public string Username { get; set; } = "";
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
    public string ConfirmNewPassword { get; set; } = "";
}

public class ResetPasswordRequest
{
    public int EmployeeId { get; set; }
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
