using Dapper;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Enforcement server-side dei permessi per livello (sistema VisiWin-style),
/// speculare a <c>ATEC.PM.Shared.PermissionEngine</c> usato dal client.
///
/// Risolve ruolo→livello (tabella <c>auth_levels</c>) e feature→livello minimo
/// (tabella <c>auth_features</c>). Cache in memoria con ricarica esplicita
/// (<see cref="Reload"/>) quando un ADMIN modifica le feature.
/// </summary>
public class FeatureAccessService
{
    private readonly DbService _db;
    private readonly object _lock = new();
    private Dictionary<string, int>? _roleLevels;        // role_name (UPPER) → level_value
    private Dictionary<string, int>? _featureMinLevels;  // feature_key      → min_level

    public FeatureAccessService(DbService db) => _db = db;

    private class LevelRow { public string RoleName { get; set; } = ""; public int LevelValue { get; set; } }
    private class FeatureRow { public string FeatureKey { get; set; } = ""; public int MinLevel { get; set; } }

    private void EnsureLoaded()
    {
        if (_roleLevels != null && _featureMinLevels != null) return;
        lock (_lock)
        {
            if (_roleLevels != null && _featureMinLevels != null) return;

            using var c = _db.Open();
            var levels = c.Query<LevelRow>(
                "SELECT role_name AS RoleName, level_value AS LevelValue FROM auth_levels").ToList();
            var features = c.Query<FeatureRow>(
                "SELECT feature_key AS FeatureKey, min_level AS MinLevel FROM auth_features").ToList();

            _roleLevels = levels.ToDictionary(l => l.RoleName.ToUpperInvariant(), l => l.LevelValue);
            _featureMinLevels = features.ToDictionary(f => f.FeatureKey, f => f.MinLevel, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Invalida la cache: la prossima richiesta ricarica da DB.</summary>
    public void Reload()
    {
        lock (_lock)
        {
            _roleLevels = null;
            _featureMinLevels = null;
        }
    }

    /// <summary>Livello numerico del ruolo (0 se sconosciuto), come PermissionEngine.GetLevelForRole.</summary>
    public int GetLevelForRole(string? role)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(role)) return 0;
        return _roleLevels!.TryGetValue(role.ToUpperInvariant(), out int lvl) ? lvl : 0;
    }

    /// <summary>
    /// Accesso consentito? Coerente con PermissionEngine.CanAccess:
    /// feature non registrata → consentito; altrimenti livello-ruolo &gt;= min_level.
    /// </summary>
    public bool CanAccess(string? role, string featureKey)
    {
        EnsureLoaded();
        if (!_featureMinLevels!.TryGetValue(featureKey, out int min))
            return true; // feature non registrata → accesso libero (stesso fallback del client)
        return GetLevelForRole(role) >= min;
    }
}
