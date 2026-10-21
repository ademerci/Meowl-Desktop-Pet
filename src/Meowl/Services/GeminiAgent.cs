using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meowl.Services;

/// <summary>
/// Meowls Gehirn mit Google Gemini (kostenloses Kontingent über Google AI Studio).
/// Spricht den OpenAI-kompatiblen Zugang von Gemini an und benutzt dieselben
/// Werkzeuge wie die Claude-Variante, nur ohne Websuche.
/// </summary>
public sealed class GeminiAgent(PetToolbox toolbox) : IPetAgent
{
    const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/";
    const int MaxSteps = 12;

    // Nur hinten erweitern: Geminis Antworten bleiben unverändert im Verlauf,
    // damit mitgeschickte Denk-Signaturen bei Werkzeugaufrufen gültig bleiben.
    readonly List<JsonNode> _history = [];

    /// <summary>Austauschbar für Tests.</summary>
    public HttpClient Http { get; init; } = new() { Timeout = TimeSpan.FromSeconds(90) };

    public event Action<string>? Activity;

    public void Reset() => _history.Clear();

    public async Task<string> AskAsync(string userText, PetSettings settings, CancellationToken ct)
    {
        var key = settings.GeminiApiKey ?? throw new MissingApiKeyException();
        var checkpoint = _history.Count;
        _history.Add(new JsonObject { ["role"] = "user", ["content"] = userText });

        var reply = new StringBuilder();
        try
        {
            for (var step = 0; step < MaxSteps; step++)
            {
                var message = await SendAsync(key, settings, ct);
                _history.Add(message.DeepClone());

                var text = ContentText(message["content"]).Trim();
                if (text.Length > 0)
                {
                    if (reply.Length > 0) reply.Append("\n\n");
                    reply.Append(text);
                }

                if (message["tool_calls"] is not JsonArray { Count: > 0 } toolCalls)
                {
                    var result = reply.ToString().Trim();
                    return result.Length > 0 ? result : "*nickt* 🦉";
                }

                // Alle Werkzeuge ausführen, jedes Ergebnis als eigene "tool"-Nachricht
                foreach (var call in toolCalls)
                {
                    var id = call?["id"]?.GetValue<string>() ?? "";
                    var name = call?["function"]?["name"]?.GetValue<string>() ?? "";
                    var outcome = ParseArguments(call?["function"]?["arguments"]) is { } input
                        ? toolbox.Execute(name, input)
                        : new ToolOutcome("Die Parameter waren kein gültiges JSON.", IsError: true);
                    if (outcome.Activity is { } activity)
                        Activity?.Invoke(activity);
                    _history.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = id,
                        ["content"] = outcome.IsError ? "Fehler: " + outcome.Content : outcome.Content,
                    });
                }
            }

            Rollback(checkpoint);
            return "Puh, das waren mir zu viele Schritte auf einmal. Kannst du die Aufgabe kleiner machen?";
        }
        catch
        {
            Rollback(checkpoint);
            throw;
        }
    }

    async Task<JsonObject> SendAsync(string key, PetSettings settings, CancellationToken ct)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = PetAgent.SystemPrompt(settings, webSearch: false) } };
        foreach (var m in _history)
            messages.Add(m.DeepClone());

        var body = new JsonObject
        {
            ["model"] = settings.GeminiModel,
            ["messages"] = messages,
            ["tools"] = PetToolbox.OpenAiDefinitions(),
            // Für Plaudereien reicht wenig Nachdenken: schneller und schont das Gratis-Kontingent
            ["reasoning_effort"] = "low",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        HttpResponseMessage response;
        string json;
        try
        {
            response = await Http.SendAsync(request, ct);
            json = await response.Content.ReadAsStringAsync(ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PetApiException("Gemini hat zu lange gebraucht. Versuch es gleich nochmal.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new PetApiException(Explain(response.StatusCode, json, settings.GeminiModel));
        }

        var root = JsonNode.Parse(json);
        return root?["choices"]?[0]?["message"] as JsonObject
            ?? throw new PetApiException("Gemini hat eine leere Antwort geschickt. Versuch es nochmal.");
    }

    static string Explain(HttpStatusCode status, string body, string model)
    {
        var detail = ErrorMessage(body);
        return (int)status switch
        {
            400 when detail.Contains("API key", StringComparison.OrdinalIgnoreCase)
                => "Der Gemini-Schlüssel ist ungültig. Prüfe ihn in den Einstellungen (⚙).",
            400 => $"Gemini hat die Anfrage abgelehnt: {detail}",
            401 or 403 => "Der Gemini-Schlüssel ist ungültig oder hat keinen Zugriff. Prüfe ihn in den Einstellungen (⚙).",
            404 => $"Das Modell \"{model}\" gibt es bei Gemini nicht (mehr). Wähle in den Einstellungen ein anderes.",
            429 => "Das Gratis-Limit von Gemini ist gerade erreicht. Warte eine Minute oder wähle in den Einstellungen Flash-Lite.",
            >= 500 => "Gemini ist gerade überlastet. Versuch es gleich nochmal.",
            _ => $"Fehler von Gemini ({(int)status}): {detail}",
        };
    }

    /// <summary>Fehlermeldung aus der Antwort ziehen, egal ob als Objekt oder Liste verpackt.</summary>
    static string ErrorMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            if (node is JsonArray { Count: > 0 } list) node = list[0];
            if (node?["error"]?["message"]?.GetValue<string>() is { Length: > 0 } message)
                return message;
        }
        catch (JsonException) { }
        return body.Length > 200 ? body[..200] : body;
    }

    static string ContentText(JsonNode? content) => content switch
    {
        JsonValue value when value.TryGetValue<string>(out var s) => s,
        JsonArray parts => string.Concat(parts.Select(p => p?["text"]?.GetValue<string>() ?? "")),
        _ => "",
    };

    static Dictionary<string, JsonElement>? ParseArguments(JsonNode? arguments)
    {
        try
        {
            // Normalerweise ein JSON-Text, manche Anbieter schicken aber direkt ein Objekt
            var json = arguments is JsonValue v && v.TryGetValue<string>(out var s) ? s : arguments?.ToJsonString();
            if (string.IsNullOrWhiteSpace(json)) return [];
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    void Rollback(int count)
    {
        if (_history.Count > count)
            _history.RemoveRange(count, _history.Count - count);
    }
}
