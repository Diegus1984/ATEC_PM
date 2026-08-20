namespace ATEC.PM.Shared.DTOs;

public class AuthLevelDto
{
    public int Id { get; set; }
    public int LevelValue { get; set; }
    public string RoleName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int SortOrder { get; set; }

    /// <summary>
    /// 'LEVEL' = ruolo della gerarchia (vede tutto ciò che sta al suo livello o sotto);
    /// 'GRANTS' = ruolo di reparto, vede SOLO le funzioni concesse in auth_role_features.
    /// </summary>
    public string AccessMode { get; set; } = "LEVEL";
}

/// <summary>Concessione di una funzione a un singolo ruolo ('READ' o 'FULL').</summary>
public class AuthRoleFeatureDto
{
    public string RoleName { get; set; } = "";
    public string FeatureKey { get; set; } = "";
    public string Access { get; set; } = "FULL";
}

/// <summary>Assegna o revoca (Access vuoto/null) una concessione.</summary>
public class SetRoleFeatureRequest
{
    public string RoleName { get; set; } = "";
    public string FeatureKey { get; set; } = "";
    /// <summary>'READ', 'FULL' oppure vuoto per togliere la concessione.</summary>
    public string? Access { get; set; }
}

public class AuthFeatureDto
{
    public int Id { get; set; }
    public string FeatureKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "navigation";
    public int MinLevel { get; set; }
    public string Behavior { get; set; } = "HIDDEN";
}

public class UpdateAuthFeatureRequest
{
    public int MinLevel { get; set; }
    public string Behavior { get; set; } = "HIDDEN";
}

// 🧹 `CreateAuthFeatureRequest` è uscito col passo 7 del rebuild: le funzioni si registrano
// aggiungendo la voce a `catalogo-permessi.json` (EnsureCatalogo le mette in tabella al primo
// avvio), non da un form — vedi AuthLevelController.

public class AuthFeaturesContextDto
{
    public int UserLevel { get; set; }
    public List<AuthFeatureDto> Features { get; set; } = new();
    public List<AuthLevelDto> Levels { get; set; } = new();

    /// <summary>Modalità del ruolo dell'utente: 'LEVEL' o 'GRANTS' (ruolo di reparto).</summary>
    public string AccessMode { get; set; } = "LEVEL";

    /// <summary>Concessioni del ruolo dell'utente (feature_key → 'READ'/'FULL').</summary>
    public Dictionary<string, string> Grants { get; set; } = new();
}
