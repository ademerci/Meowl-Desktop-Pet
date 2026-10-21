using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Meowl.Services;

/// <summary>
/// Einstellungen, gespeichert unter %APPDATA%\Meowl\settings.json.
/// Der API-Schlüssel wird mit Windows DPAPI verschlüsselt (nur dein
/// Windows-Benutzer kann ihn wieder entschlüsseln).
/// </summary>
public sealed class PetSettings
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Meowl");

    static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "Meowl";

    public string PetName { get; set; } = "Meowl";
    public string UserName { get; set; } = "";
    public string Model { get; set; } = "claude-opus-5-5";
    public bool WebSearch { get; set; } = true;
    public bool Wander { get; set; } = true;
    public double Scale { get; set; } = 0.8;
    public double? LastX { get; set; }
    public string? ApiKeyProtected { get; set; }

    /// <summary>Gespeicherter Schlüssel, sonst die Umgebungsvariable ANTHROPIC_API_KEY.</summary>
    [JsonIgnore]
    public string? ApiKey
    {
        get
        {
            if (!string.IsNullOrEmpty(ApiKeyProtected))
            {
                try
                {
                    var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ApiKeyProtected), null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch (CryptographicException) { }
                catch (FormatException) { }
            }
            var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            return string.IsNullOrWhiteSpace(env) ? null : env;
        }
        set
        {
            ApiKeyProtected = string.IsNullOrWhiteSpace(value)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }
    }

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrEmpty(ApiKey);

    public static PetSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(SettingsFile)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Autostart über HKCU\...\Run (nur für deinen Benutzer).</summary>
    [JsonIgnore]
    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}
