using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ATEC.PM.Server;

using System.Runtime.Versioning;

// aggiungi questo attributo sopra la classe
[SupportedOSPlatform("windows")]
public static class ProtectedConfigHelper
{
    private static readonly string SecretsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "appsettings.Secrets.json");

    // ============================================================
    // ENCRYPTION DPAPI (stessa logica del tuo SettingsManager VB)
    // ============================================================

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        byte[] data = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return "";
        byte[] encrypted = Convert.FromBase64String(encryptedBase64);
        byte[] data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(data);
    }

    // ============================================================
    // GENERA FILE SEGRETI CRIPTATI
    // ============================================================

    public static void GenerateSecretsFile(string connectionString, string jwtKey)
    {
        Dictionary<string, string> secrets = new()
        {
            ["ConnectionStrings:Default"] = Encrypt(connectionString),
            ["Jwt:Key"] = Encrypt(jwtKey)
        };

        string json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SecretsPath, json, Encoding.UTF8);
    }

    // ============================================================
    // CARICAMENTO SEGRETI (sovrascrive appsettings.json)
    // ============================================================

    public static Dictionary<string, string?> LoadSecrets()
    {
        if (!File.Exists(SecretsPath))
            return new Dictionary<string, string?>();

        try
        {
            string json = File.ReadAllText(SecretsPath, Encoding.UTF8);
            Dictionary<string, string>? encrypted = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (encrypted is null)
                return new Dictionary<string, string?>();

            Dictionary<string, string?> decrypted = new();
            foreach (KeyValuePair<string, string> kvp in encrypted)
            {
                decrypted[kvp.Key] = Decrypt(kvp.Value);
            }
            return decrypted;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProtectedConfig] Errore caricamento segreti: {ex.Message}");
            return new Dictionary<string, string?>();
        }
    }

    // ============================================================
    // CHECK CONFIGURAZIONE
    // ============================================================

    public static bool IsConfigured()
    {
        return File.Exists(SecretsPath);
    }

    /// <summary>
    /// Pulisce i valori sensibili da appsettings.json dopo la cifratura
    /// </summary>
    public static void CleanAppSettings()
    {
        try
        {
            string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(appSettingsPath)) return;

            string json = File.ReadAllText(appSettingsPath, Encoding.UTF8);
            using JsonDocument doc = JsonDocument.Parse(json);

            using MemoryStream ms = new();
            using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "ConnectionStrings")
                    {
                        writer.WriteStartObject("ConnectionStrings");
                        writer.WriteString("Default", "ENCRYPTED");
                        writer.WriteEndObject();
                    }
                    else if (prop.Name == "Jwt")
                    {
                        writer.WriteStartObject("Jwt");
                        writer.WriteString("Key", "ENCRYPTED");
                        writer.WriteEndObject();
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            File.WriteAllText(appSettingsPath, Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Errore pulizia appsettings.json: {ex.Message}");
        }
    }
}