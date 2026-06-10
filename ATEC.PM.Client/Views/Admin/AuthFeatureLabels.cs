using System.Globalization;

namespace ATEC.PM.Client.Views.Admin;

/// <summary>Etichette italiane per categorie, chiavi feature e comportamenti permessi.</summary>
public static class AuthFeatureLabels
{
    private static readonly Dictionary<string, string> CategoryLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["navigation"] = "Navigazione (menu)",
        ["action"] = "Azioni",
        ["data"] = "Dati economici",
    };

    public static string LevelDisplay(int levelValue, string fallbackName)
    {
        return levelValue switch
        {
            0 => "0 · Tecnico",
            1 => "1 · Responsabile reparto",
            2 => "2 · Project Manager",
            3 => "3 · Amministratore",
            4 => "4 · Developer",
            _ => fallbackName
        };
    }

    public static string ImproveDisplayName(string featureKey, string displayName)
    {
        if (FeatureDisplayNameOverrides.TryGetValue(featureKey, out string? label))
            return label;
        return displayName;
    }

    private static readonly Dictionary<string, string> FeatureDisplayNameOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nav.cat_preventivi"] = "Catalogo preventivi",
        ["nav.backup"] = "Backup database",
        ["nav.codex_composizione"] = "Composizione codex",
        ["data.budget"] = "Budget e consuntivi",
        ["data.costs"] = "Costi e margini",
        ["data.revenue"] = "Ricavi e fatturato",
        ["data.hourly_cost"] = "Costo orario risorse",
        ["action.create_project"] = "Creare commessa",
        ["action.edit_project"] = "Modificare commessa",
        ["action.delete_project"] = "Eliminare commessa",
        ["resources.edit"] = "Modificare allocazioni risorse",
    };

    private static readonly Dictionary<string, string> FeatureKeyLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nav.dashboard"] = "Menu · Dashboard",
        ["nav.timesheet"] = "Menu · Timesheet",
        ["nav.commesse"] = "Menu · Commesse",
        ["nav.preventivi"] = "Menu · Preventivi",
        ["nav.cat_preventivi"] = "Menu · Catalogo preventivi",
        ["nav.clienti"] = "Menu · Clienti",
        ["nav.fornitori"] = "Menu · Fornitori",
        ["nav.catalogo"] = "Menu · Catalogo articoli",
        ["nav.codex"] = "Menu · Codex articoli",
        ["nav.codex_composizione"] = "Menu · Composizione codex",
        ["nav.utenti"] = "Menu · Utenti",
        ["nav.config_sezioni"] = "Menu · Configurazione sezioni",
        ["nav.ddp_destinazioni"] = "Menu · Destinazioni DDP",
        ["nav.project_templates"] = "Menu · Template commesse",
        ["nav.backup"] = "Menu · Backup database",
        ["nav.permessi"] = "Menu · Gestione permessi",
        ["action.create_project"] = "Azione · Creare commessa",
        ["action.edit_project"] = "Azione · Modificare commessa",
        ["action.delete_project"] = "Azione · Eliminare commessa",
        ["resources.edit"] = "Azione · Modificare allocazioni risorse",
        ["data.budget"] = "Dato · Budget e consuntivi",
        ["data.costs"] = "Dato · Costi e margini",
        ["data.revenue"] = "Dato · Ricavi e fatturato",
        ["data.hourly_cost"] = "Dato · Costo orario risorse",
    };

    public static string CategoryDisplay(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return "—";
        return CategoryLabels.TryGetValue(category, out string? label) ? label : category;
    }

    public static string FeatureKeyDisplay(string featureKey)
    {
        if (string.IsNullOrWhiteSpace(featureKey)) return "—";
        if (FeatureKeyLabels.TryGetValue(featureKey, out string? label)) return label;

        string[] parts = featureKey.Split('.', 2);
        if (parts.Length == 2)
        {
            string area = parts[0].ToLowerInvariant() switch
            {
                "nav" => "Menu",
                "action" => "Azione",
                "data" => "Dato",
                _ => parts[0]
            };
            string name = HumanizeToken(parts[1]);
            return $"{area} · {name}";
        }

        return HumanizeToken(featureKey);
    }

    public static string BehaviorDisplay(string behavior) => behavior.ToUpperInvariant() switch
    {
        "HIDDEN" => "Nascosto",
        "DISABLED" => "Disabilitato",
        _ => behavior
    };

    public static string CategoryValueFromDisplay(string display)
    {
        foreach (KeyValuePair<string, string> kv in CategoryLabels)
        {
            if (kv.Value.Equals(display, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }
        return display;
    }

    public static IReadOnlyList<CategoryOption> CategoryOptions { get; } = new List<CategoryOption>
    {
        new("navigation", "Navigazione (menu)"),
        new("action", "Azioni"),
        new("data", "Dati economici"),
    };

    public static IReadOnlyList<BehaviorOption> BehaviorOptions { get; } = new List<BehaviorOption>
    {
        new("HIDDEN", "Nascosto"),
        new("DISABLED", "Disabilitato"),
    };

    private static string HumanizeToken(string token)
    {
        string spaced = token.Replace('_', ' ').Replace('.', ' ');
        TextInfo ti = CultureInfo.GetCultureInfo("it-IT").TextInfo;
        return ti.ToTitleCase(spaced.ToLowerInvariant());
    }
}

public sealed class CategoryOption
{
    public string Value { get; }
    public string Label { get; }
    public CategoryOption(string value, string label) { Value = value; Label = label; }
}

public sealed class BehaviorOption
{
    public string Value { get; }
    public string Label { get; }
    public BehaviorOption(string value, string label) { Value = value; Label = label; }
}
