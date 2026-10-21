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

    public const string ProviderClaude = "claude";
    public const string ProviderGemini = "gemini";

    public string PetName { get; set; } = "Meowl";
    public string UserName { get; set; } = "";
    /// <summary>Welche KI antwortet: "claude" oder "gemini".</summary>
    public string Provider { get; set; } = ProviderClaude;
    public string Model { get; set; } = "claude-opus-5-5";
    public string GeminiModel { get; set; } = "gemini-3.6-flash";
    public bool WebSearch { get; set; } = true;
    public bool Wander { get; set; } = true;
    public double Scale { get; set; } = 0.8;
    public double? LastX { get; set; }
    public string? ApiKeyProtected { get; set; }
    public string? GeminiKeyProtected { get; set; }

    /// <summary>Gespeicherter Claude-Schlüssel, sonst die Umgebungsvariable ANTHROPIC_API_KEY.</summary>
    [JsonIgnore]
    public string? ApiKey
    {
        get => Unprotect(ApiKeyProtected) ?? FromEnvironment("ANTHROPIC_API_KEY");
        set => ApiKeyProtected = Protect(value);
    }

    /// <summary>Gespeicherter Gemini-Schlüssel, sonst GOOGLE_API_KEY oder GEMINI_API_KEY.</summary>
    [JsonIgnore]
    public string? GeminiApiKey
    {
        get => Unprotect(GeminiKeyProtected) ?? FromEnvironment("GOOGLE_API_KEY") ?? FromEnvironment("GEMINI_API_KEY");
        set => GeminiKeyProtected = Protect(value);
    }

    [JsonIgnore]
    public bool UsesGemini => Provider == ProviderGemini;

    /// <summary>Ist für den gewählten Anbieter ein Schlüssel vorhanden?</summary>
    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrEmpty(UsesGemini ? GeminiApiKey : ApiKey);

    static string? Protect(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));

    static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
    }

    static string? FromEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

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
