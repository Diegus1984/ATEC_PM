using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ATEC.PM.Server;

using System.Runtime.Versioning;

// aggiungi questo attributo sopra la classe
/// <summary>
/// I segreti di configurazione (password SMTP, Ecos, VPS) passano da qui, non da
/// <see cref="ProtectedConfigHelper"/> direttamente: quello è marcato «solo Windows» (DPAPI) e ogni
/// chiamata diretta da codice neutro è un warning CA1416. Il programma gira solo su Windows
/// (servizio win-x64), ma il compilatore non lo sa: la guardia sta in un posto solo, qui.
/// </summary>
public static class Segreti
{
    public static string Cifra(string chiaro)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La cifratura DPAPI dei segreti esiste solo su Windows.");
        return ProtectedConfigHelper.Encrypt(chiaro);
    }

    /// <summary>Lancia se il testo non è decifrabile (altra macchina/utente): chi chiama decide cosa farne.</summary>
    public static string Decifra(string cifrataBase64)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La cifratura DPAPI dei segreti esiste solo su Windows.");
        return ProtectedConfigHelper.Decrypt(cifrataBase64);
    }
}

[SupportedOSPlatform("windows")]
public static class ProtectedConfigHelper
{
    private static readonly string SecretsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "appsettings.Secrets.json");

    // ============================================================
    // ENCRYPTION DPAPI (stessa logica del tuo SettingsManager VB)
    // ============================================================

    /// <summary>
    /// Ambito DPAPI da usare per cifrare. Di default CurrentUser (come sempre, sui PC di
    /// sviluppo). Sul server aziendale il programma gira come SERVIZIO Windows e i servizi
    /// non caricano il profilo utente: senza profilo la chiave DPAPI CurrentUser non è
    /// disponibile e i segreti non sarebbero né cifrabili né leggibili. Lì l'installatore
    /// imposta la variabile d'ambiente ATECPM_PROTECT_MACHINE=1 e si usa LocalMachine
    /// (il file resta protetto dalle ACL della cartella, accessibile solo ad Administrators
    /// e all'account del servizio).
    /// </summary>
    private static DataProtectionScope ProtectScope =>
        Environment.GetEnvironmentVariable("ATECPM_PROTECT_MACHINE") == "1"
            ? DataProtectionScope.LocalMachine
            : DataProtectionScope.CurrentUser;

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        byte[] data = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = ProtectedData.Protect(data, null, ProtectScope);
        return Convert.ToBase64String(encrypted);
    }

    public static string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return "";
        byte[] encrypted = Convert.FromBase64String(encryptedBase64);

        // Si prova prima l'ambito configurato, poi l'altro: così un file cifrato con lo
        // scope "vecchio" (es. segreti generati a mano prima di installare il servizio)
        // resta leggibile invece di rendere il server inavviabile.
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, ProtectScope));
        }
        catch (CryptographicException)
        {
            DataProtectionScope fallback = ProtectScope == DataProtectionScope.CurrentUser
                ? DataProtectionScope.LocalMachine
                : DataProtectionScope.CurrentUser;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, fallback));
        }
    }

    // ============================================================
    // GENERA FILE SEGRETI CRIPTATI
    // ============================================================

    public static void GenerateSecretsFile(string connectionString, string jwtKey)
    {
        // Si riparte da quello che c'e' gia': nel file possono esserci segreti aggiunti dopo
        // l'installazione (es. DaneaSync:SmbPassword, messa da deploy\imposta-credenziali-share.ps1).
        // Riscrivendo il dizionario da zero sparirebbero senza che nessuno se ne accorga.
        Dictionary<string, string> secrets = ReadRawSecrets();
        secrets["ConnectionStrings:Default"] = Encrypt(connectionString);
        secrets["Jwt:Key"] = Encrypt(jwtKey);

        string json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SecretsPath, json, Encoding.UTF8);
    }

    // ============================================================
    // CARICAMENTO SEGRETI (sovrascrive appsettings.json)
    // ============================================================

    /// <summary>Segreti come stanno sul disco (ancora cifrati); vuoto se il file non c'e'.</summary>
    private static Dictionary<string, string> ReadRawSecrets()
    {
        if (!File.Exists(SecretsPath)) return new Dictionary<string, string>();
        try
        {
            string json = File.ReadAllText(SecretsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

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
                        // Si maschera SOLO "Default" (l'unica cifrata): le altre connessioni
                        // — Codex in primis — vanno riscritte così come sono, altrimenti al
                        // primo avvio spariscono dal file e la sincronizzazione si spegne
                        // con "Connection string non configurata".
                        writer.WriteStartObject("ConnectionStrings");
                        foreach (JsonProperty conn in prop.Value.EnumerateObject())
                        {
                            if (conn.NameEquals("Default")) writer.WriteString("Default", "ENCRYPTED");
                            else conn.WriteTo(writer);
                        }
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