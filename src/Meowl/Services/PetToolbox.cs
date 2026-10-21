using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Anthropic.Models.Beta.Messages;

namespace Meowl.Services;

/// <summary>Was die Werkzeuge mit dem Haustier selbst machen dürfen.</summary>
public interface IPetHost
{
    void PlayAction(string action);
    void Remind(string text);
}

/// <summary>Ergebnis eines Werkzeugaufrufs.</summary>
public sealed record ToolOutcome(string Content, bool IsError = false, string? Activity = null);

/// <summary>
/// Die Werkzeuge, die Claude auf deinem PC benutzen darf. Bewusst harmlos:
/// nichts löschen, keine beliebigen Befehle, nur http(s)-Links und eine feste
/// Liste von Programmen.
/// </summary>
public sealed class PetToolbox(IPetHost host)
{
    static readonly CultureInfo German = new("de-DE");
    static readonly string NotesFile = Path.Combine(PetSettings.DataDir, "notizen.md");

    static readonly Dictionary<string, string> Programs = new()
    {
        ["editor"] = "notepad.exe",
        ["rechner"] = "calc.exe",
        ["explorer"] = "explorer.exe",
        ["paint"] = "mspaint.exe",
        ["einstellungen"] = "ms-settings:",
        ["taskmanager"] = "taskmgr.exe",
        ["bildschirmausschnitt"] = "ms-screenclip:",
    };

    readonly List<DispatcherTimer> _reminders = [];

    /// <summary>Beschreibung eines Werkzeugs, unabhängig vom KI-Anbieter.</summary>
    sealed record ToolSpec(string Name, string Description, Dictionary<string, object>? Properties = null, string[]? Required = null)
    {
        public Dictionary<string, JsonElement> SchemaProperties =>
            (Properties ?? []).ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));
    }

    static readonly ToolSpec[] Specs =
    [
        new("get_datetime", "Returns the user's current local date, time and time zone. Use it whenever the answer depends on today's date or the current time."),
        new("open_url", "Opens a web page in the user's default browser. Only http and https URLs are allowed.",
            new() { ["url"] = new { type = "string", description = "Full URL starting with http:// or https://" } },
            ["url"]),
        new("open_program", "Starts one of a few safe Windows programs for the user.",
            new() { ["program"] = new { type = "string", @enum = Programs.Keys.ToArray(), description = "Which program to open" } },
            ["program"]),
        new("set_reminder", "Sets a reminder. When it is due, the pet wakes up, plays a sound and shows the text in a speech bubble. Reminders only work while the pet app is running.",
            new()
            {
                ["minutes"] = new { type = "number", description = "Minutes from now, between 0.5 and 1440" },
                ["text"] = new { type = "string", description = "What to remind the user of, written in the user's language" },
            },
            ["minutes", "text"]),
        new("save_note", "Saves a short note for the user to their notes file (notizen.md). Use it when the user asks you to remember or note something.",
            new() { ["text"] = new { type = "string", description = "The note text" } },
            ["text"]),
        new("read_notes", "Reads the user's saved notes."),
        new("read_clipboard", "Reads the text currently on the user's clipboard, e.g. when they say 'translate what I copied'."),
        new("get_system_info", "Returns information about the user's PC: Windows version, uptime, battery, memory and free disk space."),
        new("pet_action", "Makes the desktop pet (you) do an animation. Use it playfully, e.g. dance when the user is happy or asks you to dance.",
            new() { ["action"] = new { type = "string", @enum = new[] { "happy", "dance", "fly", "sleep" } } },
            ["action"]),
    ];

    /// <summary>Werkzeuge im Format der Claude-API (plus Claudes eingebaute Websuche).</summary>
    public List<BetaToolUnion> Definitions(PetSettings settings)
    {
        List<BetaToolUnion> tools = [.. Specs.Select(spec => (BetaToolUnion)new BetaTool
        {
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = new() { Properties = spec.SchemaProperties, Required = spec.Required ?? [] },
        })];

        if (settings.WebSearch)
        {
            // Haiku unterstützt nur die ältere Variante der Websuche
            tools.Add(settings.Model.StartsWith("claude-haiku", StringComparison.Ordinal)
                ? new BetaToolUnion(new BetaWebSearchTool20250305 { MaxUses = 5 })
                : new BetaToolUnion(new BetaWebSearchTool20260209 { MaxUses = 5 }));
        }
        return tools;
    }

    /// <summary>Werkzeuge im OpenAI-kompatiblen Format (Gemini, Groq, Ollama, ...).</summary>
    public static JsonArray OpenAiDefinitions()
    {
        var tools = new JsonArray();
        foreach (var spec in Specs)
        {
            var properties = new JsonObject();
            foreach (var (key, value) in spec.SchemaProperties)
                properties[key] = JsonNode.Parse(value.GetRawText());
            tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = spec.Name,
                    ["description"] = spec.Description,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                        ["required"] = new JsonArray([.. (spec.Required ?? []).Select(r => JsonValue.Create(r))]),
                    },
                },
            });
        }
        return tools;
    }

    public ToolOutcome Execute(string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        try
        {
            return name switch
            {
                "get_datetime" => GetDateTime(),
                "open_url" => OpenUrl(GetString(input, "url")),
                "open_program" => OpenProgram(GetString(input, "program")),
                "set_reminder" => SetReminder(GetNumber(input, "minutes"), GetString(input, "text")),
                "save_note" => SaveNote(GetString(input, "text")),
                "read_notes" => ReadNotes(),
                "read_clipboard" => ReadClipboard(),
                "get_system_info" => GetSystemInfo(),
                "pet_action" => PetAction(GetString(input, "action")),
                _ => new ToolOutcome($"Unbekanntes Werkzeug: {name}", IsError: true),
            };
        }
        catch (ToolInputException ex)
        {
            return new ToolOutcome(ex.Message, IsError: true);
        }
        catch (Exception ex)
        {
            return new ToolOutcome($"Fehler: {ex.Message}", IsError: true);
        }
    }

    // ------------------------------------------------------------------

    static ToolOutcome GetDateTime()
    {
        var now = DateTime.Now;
        return new ToolOutcome($"{now.ToString("dddd, d. MMMM yyyy, HH:mm 'Uhr'", German)} (Zeitzone: {TimeZoneInfo.Local.DisplayName})");
    }

    static ToolOutcome OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new ToolOutcome("Nur http- und https-Links sind erlaubt.", IsError: true);
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return new ToolOutcome("Seite geöffnet.", Activity: $"🌐 Öffne {uri.Host}");
    }

    static ToolOutcome OpenProgram(string program)
    {
        if (!Programs.TryGetValue(program, out var target))
            return new ToolOutcome($"Nicht erlaubt. Möglich sind: {string.Join(", ", Programs.Keys)}", IsError: true);
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return new ToolOutcome($"{program} gestartet.", Activity: $"🚀 Starte {program}");
    }

    ToolOutcome SetReminder(double minutes, string text)
    {
        if (minutes is < 0.5 or > 1440)
            return new ToolOutcome("minutes muss zwischen 0.5 und 1440 liegen.", IsError: true);
        if (string.IsNullOrWhiteSpace(text))
            return new ToolOutcome("text darf nicht leer sein.", IsError: true);

        var due = DateTime.Now.AddMinutes(minutes);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(minutes) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _reminders.Remove(timer);
            host.Remind(text);
        };
        _reminders.Add(timer);
        timer.Start();
        var at = due.ToString("HH:mm", German);
        return new ToolOutcome($"Erinnerung gestellt für {at} Uhr.", Activity: $"⏰ Erinnerung um {at}: {text}");
    }

    static ToolOutcome SaveNote(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ToolOutcome("Die Notiz ist leer.", IsError: true);
        Directory.CreateDirectory(PetSettings.DataDir);
        if (!File.Exists(NotesFile))
            File.WriteAllText(NotesFile, "# Notizen von Meowl\n\n", Encoding.UTF8);
        File.AppendAllText(NotesFile, $"- **{DateTime.Now:yyyy-MM-dd HH:mm}** {text.Trim().ReplaceLineEndings(" ")}\n", Encoding.UTF8);
        return new ToolOutcome("Notiz gespeichert.", Activity: "📝 Notiz gespeichert");
    }

    static ToolOutcome ReadNotes()
    {
        if (!File.Exists(NotesFile))
            return new ToolOutcome("Es gibt noch keine Notizen.");
        var text = File.ReadAllText(NotesFile);
        if (text.Length > 6000)
            text = "(ältere Notizen gekürzt)\n" + text[^6000..];
        return new ToolOutcome(text, Activity: "📖 Lese Notizen");
    }

    static ToolOutcome ReadClipboard()
    {
        if (!System.Windows.Clipboard.ContainsText())
            return new ToolOutcome("Die Zwischenablage enthält keinen Text.");
        var text = System.Windows.Clipboard.GetText();
        if (text.Length > 8000)
            text = text[..8000] + "\n(gekürzt)";
        return new ToolOutcome(text, Activity: "📋 Lese Zwischenablage");
    }

    static ToolOutcome GetSystemInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString}, Rechner: {Environment.MachineName}");
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        sb.AppendLine($"Läuft seit: {(int)up.TotalHours} Std. {up.Minutes} Min.");

        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery))
            sb.AppendLine("Akku: keiner (Desktop-PC)");
        else
            sb.AppendLine($"Akku: {power.BatteryLifePercent:P0}, {(power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "am Netzteil" : "im Akkubetrieb")}");

        if (NativeMethods.GetMemoryStatus() is { } mem)
            sb.AppendLine($"Arbeitsspeicher: {mem.dwMemoryLoad} % belegt, {mem.ullAvailPhys / 1024.0 / 1024 / 1024:F1} von {mem.ullTotalPhys / 1024.0 / 1024 / 1024:F1} GB frei");

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            sb.AppendLine($"Laufwerk {drive.Name}: {drive.AvailableFreeSpace / 1024.0 / 1024 / 1024:F0} von {drive.TotalSize / 1024.0 / 1024 / 1024:F0} GB frei");

        return new ToolOutcome(sb.ToString(), Activity: "💻 Schaue mir den PC an");
    }

    ToolOutcome PetAction(string action)
    {
        if (action is not ("happy" or "dance" or "fly" or "sleep"))
            return new ToolOutcome("Unbekannte Aktion.", IsError: true);
        host.PlayAction(action);
        return new ToolOutcome("Erledigt.");
    }

    public static void OpenNotesFile()
    {
        Directory.CreateDirectory(PetSettings.DataDir);
        if (!File.Exists(NotesFile))
            File.WriteAllText(NotesFile, "# Notizen von Meowl\n\n", Encoding.UTF8);
        Process.Start(new ProcessStartInfo(NotesFile) { UseShellExecute = true });
    }

    // ------------------------------------------------------------------

    sealed class ToolInputException(string message) : Exception(message);

    static string GetString(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new ToolInputException($"Parameter '{key}' fehlt oder ist kein Text.");

    static double GetNumber(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        if (input.TryGetValue(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), CultureInfo.InvariantCulture, out var d)) return d;
        }
        throw new ToolInputException($"Parameter '{key}' fehlt oder ist keine Zahl.");
    }
}
