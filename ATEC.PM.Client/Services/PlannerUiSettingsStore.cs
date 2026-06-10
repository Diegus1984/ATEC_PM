using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ATEC.PM.Client.Services;

/// <summary>Stato UI del planner Gantt risorse (finestra, filtri, scroll). Persistito per utente.</summary>
public sealed class PlannerUiSettings
{
    public int WindowDays { get; set; } = 28;
    public string WindowStart { get; set; } = "";
    public string Search { get; set; } = "";
    public bool ConflictsOnly { get; set; }
    public bool OccupiedOnly { get; set; }
    public string TipoFilter { get; set; } = "";
    public int ProjectFilterId { get; set; }
    public double ScrollHorizontal { get; set; }
    public double ScrollVertical { get; set; }

    /// <summary>Filtro risorse attivo: mostra solo <see cref="SelectedResourceIds"/>.</summary>
    public bool ResourceFilterActive { get; set; }

    /// <summary>Id delle risorse da mostrare quando il filtro è attivo.</summary>
    public List<int> SelectedResourceIds { get; set; } = new();
}

public static class PlannerUiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATEC_PM",
            "planner-ui.json");

    public static PlannerUiSettings Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path))
                return new PlannerUiSettings();

            string json = File.ReadAllText(path);
            PlannerUiSettings? settings = JsonSerializer.Deserialize<PlannerUiSettings>(json, JsonOptions);
            return settings ?? new PlannerUiSettings();
        }
        catch
        {
            return new PlannerUiSettings();
        }
    }

    public static void Save(PlannerUiSettings settings)
    {
        try
        {
            string path = FilePath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Persistenza best-effort: non bloccare l'app se il disco è pieno o il path non è scrivibile.
        }
    }
}
