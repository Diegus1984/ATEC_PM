using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Shared;

/// <summary>
/// Motore centralizzato dei permessi.
/// Supporta sia il sistema a livelli (VisiWin-style, configurabile da DB)
/// sia i permessi specifici per contesto (reparto, competenza).
/// </summary>
public static class PermissionEngine
{
    // ================================================================
    // SISTEMA A LIVELLI (VisiWin-style) — caricato dal DB al login
    // ================================================================

    private static Dictionary<string, AuthFeatureDto> _features = new();
    private static List<AuthLevelDto> _levels = new();
    private static int _userLevel;

    /// <summary>Carica feature e livelli dal server (chiamato al login)</summary>
    public static void LoadFeatures(List<AuthFeatureDto> features, List<AuthLevelDto> levels, int userLevel)
    {
        _features = features.ToDictionary(f => f.FeatureKey, StringComparer.OrdinalIgnoreCase);
        _levels = levels;
        _userLevel = userLevel;
    }

    /// <summary>Resetta cache (chiamato al logout)</summary>
    public static void ClearFeatures()
    {
        _features.Clear();
        _levels.Clear();
        _userLevel = 0;
    }

    /// <summary>L'utente corrente può accedere a questa feature?</summary>
    public static bool CanAccess(string featureKey)
    {
        if (_features.Count == 0) return true; // fallback se non caricato
        if (!_features.TryGetValue(featureKey, out var f)) return true; // feature non registrata → accesso libero
        return _userLevel >= f.MinLevel;
    }

    /// <summary>La feature è in modalità DISABLED (visibile ma non cliccabile)?</summary>
    public static bool IsDisabledOnly(string featureKey)
    {
        if (_features.TryGetValue(featureKey, out var f))
            return f.Behavior == "DISABLED" && _userLevel < f.MinLevel;
        return false;
    }

    /// <summary>Livello numerico per un ruolo</summary>
    public static int GetLevelForRole(string roleName)
    {
        var level = _levels.FirstOrDefault(l => l.RoleName.Equals(roleName, StringComparison.OrdinalIgnoreCase));
        return level?.LevelValue ?? 0;
    }

    /// <summary>Livello corrente dell'utente loggato</summary>
    public static int CurrentUserLevel => _userLevel;

    /// <summary>Lista livelli caricati</summary>
    public static IReadOnlyList<AuthLevelDto> Levels => _levels;

    /// <summary>Lista feature caricate</summary>
    public static IReadOnlyDictionary<string, AuthFeatureDto> Features => _features;

    // ================================================================
    // NAVIGAZIONE — delegano al sistema a livelli
    // (mantenuti per retrocompatibilità con codice esistente)
    // ================================================================

    public static bool CanAccessDipendenti(UserContext u) => CanAccess("nav.utenti");
    public static bool CanAccessClienti(UserContext u) => CanAccess("nav.clienti");
    public static bool CanAccessFornitori(UserContext u) => CanAccess("nav.fornitori");
    public static bool CanAccessCatalogo(UserContext u) => CanAccess("nav.catalogo");
    public static bool CanAccessReport(UserContext u) => CanAccess("data.budget");
    public static bool CanAccessImpostazioni(UserContext u) => CanAccess("nav.config_sezioni");
    public static bool CanAccessUtenti(UserContext u) => CanAccess("nav.utenti");

    // ================================================================
    // DATI ECONOMICI — delegano al sistema a livelli
    // ================================================================

    public static bool CanSeeBudget(UserContext u) => CanAccess("data.budget");
    public static bool CanSeeCosts(UserContext u) => CanAccess("data.costs");
    public static bool CanSeeRevenue(UserContext u) => CanAccess("data.revenue");
    public static bool CanSeeHourlyCost(UserContext u) => CanAccess("data.hourly_cost");

    // ================================================================
    // COMMESSE — delegano al sistema a livelli
    // ================================================================

    public static bool CanCreateProject(UserContext u) => CanAccess("action.create_project");
    public static bool CanEditProject(UserContext u) => CanAccess("action.edit_project");
    public static bool CanDeleteProject(UserContext u) => CanAccess("action.delete_project");

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
