using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Shared;

/// <summary>
/// Motore centralizzato dei permessi.
/// Tutte le regole di accesso vivono qui — server e client usano la stessa logica.
/// Per cambiare un permesso si tocca solo questo file.
/// </summary>
public static class PermissionEngine
{
    // ================================================================
    // NAVIGAZIONE — quali sezioni può vedere ogni ruolo
    // ================================================================

    public static bool CanAccessDipendenti(UserContext u) => u.IsPm;
    public static bool CanAccessClienti(UserContext u) => u.IsPm;
    public static bool CanAccessFornitori(UserContext u) => u.IsPm;
    public static bool CanAccessCatalogo(UserContext u) => u.IsPm || u.IsResponsible;
    public static bool CanAccessReport(UserContext u) => u.IsPm;
    public static bool CanAccessImpostazioni(UserContext u) => u.IsAdmin;
    public static bool CanAccessUtenti(UserContext u) => u.IsAdmin;

    // ================================================================
    // DATI ECONOMICI — budget, costi, ricavi
    // ================================================================

    public static bool CanSeeBudget(UserContext u) => u.IsPm;
    public static bool CanSeeCosts(UserContext u) => u.IsPm;
    public static bool CanSeeRevenue(UserContext u) => u.IsPm;
    public static bool CanSeeHourlyCost(UserContext u) => u.IsAdmin;

    // ================================================================
    // COMMESSE
    // ================================================================

    public static bool CanCreateProject(UserContext u) => u.IsPm;
    public static bool CanEditProject(UserContext u) => u.IsPm;
    public static bool CanDeleteProject(UserContext u) => u.IsAdmin;

    /// <summary>
    /// Un tecnico vede una commessa se almeno una fase appartiene
    /// a un reparto di sua competenza o appartenenza.
    /// PM e ADMIN vedono tutto.
    /// </summary>
    public static bool CanSeeProject(UserContext u, IEnumerable<string> phaseDepartmentCodes)
    {
        if (u.IsPm) return true;

        foreach (string code in phaseDepartmentCodes)
        {
            if (u.DepartmentCodes.Contains(code)) return true;
            if (u.CompetenceCodes.Contains(code)) return true;
        }
        return false;
    }

    // ================================================================
    // FASI
    // ================================================================

    public static bool CanEditPhase(UserContext u) => u.IsPm || u.IsResponsible;
    public static bool CanUpdatePhaseProgress(UserContext u) => true; // tutti

    // ================================================================
    // TIMESHEET
    // ================================================================

    /// <summary>
    /// Un tecnico vede solo il proprio timesheet.
    /// Responsabile vede il suo reparto. PM/ADMIN vedono tutti.
    /// </summary>
    public static bool CanSeeEmployeeTimesheet(UserContext u, int targetEmployeeId,
        IEnumerable<string> targetDepartmentCodes)
    {
        if (u.IsPm) return true;
        if (u.EmployeeId == targetEmployeeId) return true;
        if (u.IsResponsible)
        {
            foreach (string code in targetDepartmentCodes)
                if (u.ResponsibleDepartmentCodes.Contains(code)) return true;
        }
        return false;
    }

    // ================================================================
    // DIPENDENTI
    // ================================================================

    public static bool CanEditEmployee(UserContext u) => u.IsAdmin;
    public static bool CanSetCredentials(UserContext u) => u.IsAdmin;
    public static bool CanChangeUserRole(UserContext u) => u.IsAdmin;

    /// <summary>
    /// Responsabile vede solo i dipendenti del suo reparto. PM/ADMIN vedono tutti.
    /// </summary>
    public static bool CanSeeEmployee(UserContext u, IEnumerable<string> targetDepartmentCodes)
    {
        if (u.IsPm) return true;
        if (u.IsResponsible)
        {
            foreach (string code in targetDepartmentCodes)
                if (u.ResponsibleDepartmentCodes.Contains(code)) return true;
        }
        return false;
    }

    // ================================================================
    // DOCUMENTI
    // ================================================================

    public static bool CanUploadDocument(UserContext u) => u.IsPm || u.IsResponsible;
    public static bool CanDeleteDocument(UserContext u) => u.IsPm;

    // ================================================================
    // CHAT
    // ================================================================

    public static bool CanSendMessage(UserContext u) => true; // tutti
    public static bool CanDeleteMessage(UserContext u, int authorId)
        => u.IsAdmin || u.EmployeeId == authorId;

    // ================================================================
    // HELPER — costruisce UserContext dal token JWT già parsato
    // ================================================================

    public static UserContext BuildContext(
        int employeeId,
        string userRole,
        IEnumerable<string> departmentCodes,
        IEnumerable<string> responsibleDepartmentCodes,
        IEnumerable<string> competenceCodes)
    {
        return new UserContext
        {
            EmployeeId = employeeId,
            UserRole = userRole,
            DepartmentCodes = departmentCodes.ToList(),
            ResponsibleDepartmentCodes = responsibleDepartmentCodes.ToList(),
            CompetenceCodes = competenceCodes.ToList()
        };
    }
}
