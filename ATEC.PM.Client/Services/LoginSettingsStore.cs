using System;
using System.IO;
using System.Text.Json;

namespace ATEC.PM.Client.Services;

public sealed class LoginSettings
{
    public string LastUsername { get; set; } = "";
    public string LastServerUrl { get; set; } = "";
}

public static class LoginSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATEC_PM",
            "login-settings.json");

    public static LoginSettings Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path))
                return new LoginSettings();

            string json = File.ReadAllText(path);
            LoginSettings? settings = JsonSerializer.Deserialize<LoginSettings>(json, JsonOptions);
            return settings ?? new LoginSettings();
        }
        catch
        {
            return new LoginSettings();
        }
    }

    public static void Save(LoginSettings settings)
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
            // Best-effort: non bloccare il login se il disco non è scrivibile.
        }
    }
}
