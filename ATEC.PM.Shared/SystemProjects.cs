namespace ATEC.PM.Shared;

/// <summary>
/// Commesse di sistema (contenitori, non operative).
/// </summary>
public static class SystemProjects
{
    /// <summary>Commessa fittizia per lavorazioni generiche non legate a una commessa reale.</summary>
    public const string InternaCode = "INTERNA";

    public const string InternaTitle = "Lavorazioni interne generiche";

    public const string InternaNotes = "[SYSTEM] Commessa contenitore lavorazioni generiche";

    public static bool IsSystemCode(string? code) =>
        string.Equals(code?.Trim(), InternaCode, StringComparison.OrdinalIgnoreCase);
}
