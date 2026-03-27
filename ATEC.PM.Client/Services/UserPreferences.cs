using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ATEC.PM.Client.Services;

/// <summary>
/// Preferenze utente salvate in JSON locale (%AppData%/ATEC_PM/user_prefs.json).
/// Thread-safe, lazy-loaded, auto-save su ogni Set.
/// </summary>
public static class UserPreferences
{
    private static readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ATEC_PM", "user_prefs.json");

    private static readonly JsonSerializerOptions _jopt = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static Dictionary<string, JsonElement> _data = new();
    private static bool _loaded;
    private static readonly object _lock = new();

    // ── Read ──

    public static string GetString(string key, string defaultValue = "")
    {
        EnsureLoaded();
        lock (_lock)
        {
            if (_data.TryGetValue(key, out JsonElement el) && el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? defaultValue;
            return defaultValue;
        }
    }

    public static bool GetBool(string key, bool defaultValue = false)
    {
        EnsureLoaded();
        lock (_lock)
        {
            if (_data.TryGetValue(key, out JsonElement el))
            {
                if (el.ValueKind == JsonValueKind.True) return true;
                if (el.ValueKind == JsonValueKind.False) return false;
            }
            return defaultValue;
        }
    }

    public static int GetInt(string key, int defaultValue = 0)
    {
        EnsureLoaded();
        lock (_lock)
        {
            if (_data.TryGetValue(key, out JsonElement el) && el.ValueKind == JsonValueKind.Number)
                return el.GetInt32();
            return defaultValue;
        }
    }

    // ── Write ──

    public static void Set(string key, string value)
    {
        EnsureLoaded();
        lock (_lock)
        {
            _data[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }
    }

    public static void Set(string key, bool value)
    {
        EnsureLoaded();
        lock (_lock)
        {
            _data[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }
    }

    public static void Set(string key, int value)
    {
        EnsureLoaded();
        lock (_lock)
        {
            _data[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }
    }

    // ── Internals ──

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    _data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _jopt)
                            ?? new();
                }
            }
            catch
            {
                _data = new();
            }
            _loaded = true;
        }
    }

    private static void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(_data, _jopt);
            File.WriteAllText(_filePath, json);
        }
        catch { /* silent — non bloccare l'app per un salvataggio preferenze */ }
    }
}
