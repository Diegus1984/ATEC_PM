namespace ATEC.PM.Shared.DTOs;

public class AuthLevelDto
{
    public int Id { get; set; }
    public int LevelValue { get; set; }
    public string RoleName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int SortOrder { get; set; }
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

public class CreateAuthFeatureRequest
{
    public string FeatureKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "navigation";
    public int MinLevel { get; set; }
    public string Behavior { get; set; } = "HIDDEN";
}
