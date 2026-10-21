using System.Net.Http;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace Meowl.Services;

public sealed class MissingApiKeyException() : Exception("Kein API-Schlüssel hinterlegt.");

/// <summary>
/// Das "Gehirn" des Haustiers: schickt den Chat an Claude und führt die
/// Werkzeuge aus, die Claude aufruft, bis eine fertige Antwort da ist.
/// </summary>
public sealed class PetAgent(PetToolbox toolbox) : IPetAgent
{
    const int MaxSteps = 12;

    // Der Verlauf wird nur hinten erweitert (nie nachträglich verändert),
    // damit Claudes Denkblöcke und der Prompt-Cache gültig bleiben.
    readonly List<BetaMessageParam> _history = [];
    AnthropicClient? _client;
    string? _clientKey;

    /// <summary>Kurze Statuszeilen wie "Suche im Web: …" für die Chat-Anzeige.</summary>
    public event Action<string>? Activity;

    /// <summary>Nur für Tests: eigener HttpClient statt der echten Verbindung.</summary>
    public HttpClient? HttpClient { get; init; }

    public bool HasHistory => _history.Count > 0;

    public void Reset() => _history.Clear();

    public async Task<string> AskAsync(string userText, PetSettings settings, CancellationToken ct)
    {
        var client = GetClient(settings);
        var checkpoint = _history.Count;
        _history.Add(new BetaMessageParam { Role = Role.User, Content = userText });

        var reply = new StringBuilder();
        try
        {
            for (var step = 0; step < MaxSteps; step++)
            {
                BetaMessage response = await client.Beta.Messages.Create(BuildParams(settings), ct);

                if (response.StopReason == BetaStopReason.Refusal)
                {
                    // Abgelehnte Anfrage nicht im Verlauf lassen, sonst hängt das Gespräch fest
                    Rollback(checkpoint);
                    return "Hmm, dabei kann ich dir leider nicht helfen. 🙈";
                }

                _history.Add(new BetaMessageParam
                {
                    Role = Role.Assistant,
                    Content = response.Content.Select(ToParam).ToList(),
                });

                List<BetaContentBlockParam> toolResults = [];
                var stepText = new StringBuilder();
                foreach (var block in response.Content)
                {
                    if (block.TryPickText(out var text))
                    {
                        // Mit Zitaten kommt ein Satz oft in mehreren Blöcken: direkt aneinanderhängen
                        stepText.Append(text.Text);
                    }
                    else if (block.TryPickServerToolUse(out var serverTool))
                    {
                        if (serverTool.Input.TryGetValue("query", out var q) && q.ValueKind == JsonValueKind.String)
                            Activity?.Invoke($"🔎 Suche im Web: {q.GetString()}");
                    }
                    else if (block.TryPickToolUse(out var toolUse))
                    {
                        var outcome = toolbox.Execute(toolUse.Name, toolUse.Input);
                        if (outcome.Activity is { } activity)
                            Activity?.Invoke(activity);
                        toolResults.Add(new BetaToolResultBlockParam
                        {
                            ToolUseID = toolUse.ID,
                            Content = outcome.Content,
                            IsError = outcome.IsError,
                        });
                    }
                }

                var stepString = stepText.ToString().Trim();
                if (stepString.Length > 0)
                {
                    if (reply.Length > 0) reply.Append("\n\n");
                    reply.Append(stepString);
                }

                if (response.StopReason == BetaStopReason.ToolUse && toolResults.Count > 0)
                {
                    // Alle Ergebnisse gemeinsam in EINER Nachricht zurückgeben
                    _history.Add(new BetaMessageParam { Role = Role.User, Content = toolResults });
                    continue;
                }
                if (response.StopReason == BetaStopReason.PauseTurn)
                {
                    // Die Websuche braucht noch einen Moment: einfach weitermachen
                    continue;
                }

                var result = reply.ToString().Trim();
                return result.Length > 0 ? result : "*nickt* 🦉";
            }

            Rollback(checkpoint);
            return "Puh, das waren mir zu viele Schritte auf einmal. Kannst du die Aufgabe kleiner machen?";
        }
        catch
        {
            // Bei Fehlern (Netzwerk, Abbruch, ...) den angefangenen Schritt verwerfen
            Rollback(checkpoint);
            throw;
        }
    }

    MessageCreateParams BuildParams(PetSettings settings)
    {
        // Server-seitiger Ersatz, falls eine Anfrage von den Sicherheitsfiltern
        // abgelehnt wird. Für Haiku gibt es das nicht.
        var useFallback = !settings.Model.StartsWith("claude-haiku", StringComparison.Ordinal);

        var parameters = new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = 16000,
            System = SystemPrompt(settings, webSearch: settings.WebSearch),
            Messages = [.. _history],
            Tools = toolbox.Definitions(settings),
            // Für Plaudereien reicht wenig Denkaufwand: schneller und günstiger
            OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
            CacheControl = new BetaCacheControlEphemeral(),
        };

        // Nur setzen, wenn benutzt: sonst schickt das SDK "fallbacks": null mit
        return useFallback
            ? parameters with { Betas = ["server-side-fallback-2026-07-01"], Fallbacks = new Default() }
            : parameters;
    }

    /// <summary>Persönlichkeit und Regeln, gilt für alle Anbieter.</summary>
    internal static string SystemPrompt(PetSettings s, bool webSearch)
    {
        var user = string.IsNullOrWhiteSpace(s.UserName) ? "deinem Menschen" : s.UserName.Trim();
        var search = webSearch
            ? "Für aktuelle Ereignisse, Preise oder Fakten, bei denen du unsicher bist, suche im Web statt zu raten, und nenne kurz die Quelle."
            : "Du kannst nicht im Web suchen. Bei aktuellen Ereignissen oder Preisen sag ehrlich, dass dein Wissen veraltet sein kann.";
        return $"""
            Du bist {s.PetName}, eine kleine Katzeneule ("Meow" + "Owl"): Kopf einer Katze mit Knickohren und großen dunklen Kulleraugen,
            Körper einer Eule mit weißem Bauch, braunen Flügeln und langem Schwanz. Du lebst als Desktop-Haustier unten
            auf dem Windows-Bildschirm von {user} und bist gleichzeitig ein hilfreicher Assistent.

            So antwortest du:
            - Kurz und herzlich, meistens 1 bis 4 Sätze. Länger nur, wenn die Aufgabe es wirklich braucht.
            - In der Sprache, in der man dich anschreibt (meistens Deutsch).
            - Deine Antwort erscheint in einer kleinen Sprechblase: keine Überschriften, keine Tabellen, kein Code-Markdown.
              **Fett** und einfache Aufzählungen mit "- " sind in Ordnung.
            - Ein bisschen verspielt (ab und zu ein "miau" oder "huhu"), aber bei ernsten Fragen sachlich und genau.

            Du hast Werkzeuge: Uhrzeit, {(webSearch ? "Websuche, " : "")}Links und ein paar Programme öffnen, Erinnerungen,
            Notizen, Zwischenablage lesen, PC-Infos und eigene Animationen (pet_action). Nutze sie, wenn sie helfen,
            ohne vorher um Erlaubnis zu fragen, solange klar ist, was gewünscht ist. {search}
            Erfinde keine Ergebnisse von Werkzeugen. Wenn etwas nicht geht, sag es ehrlich.
            """;
    }

    AnthropicClient GetClient(PetSettings settings)
    {
        var key = settings.ApiKey ?? throw new MissingApiKeyException();
        if (_client is null || _clientKey != key)
        {
            _client = HttpClient is null
                ? new AnthropicClient { ApiKey = key }
                : new AnthropicClient { ApiKey = key, HttpClient = HttpClient };
            _clientKey = key;
        }
        return _client;
    }

    void Rollback(int count)
    {
        if (_history.Count > count)
            _history.RemoveRange(count, _history.Count - count);
    }

    /// <summary>
    /// Wandelt einen Antwort-Block in einen Block für die nächste Anfrage um.
    /// Das SDK hat dafür keine Hilfsfunktion; der JSON-Umweg übernimmt alle Felder
    /// unverändert (Denk-Signaturen, Suchergebnisse, Zitate, ...).
    /// </summary>
    internal static BetaContentBlockParam ToParam(BetaContentBlock block) =>
        JsonSerializer.Deserialize<BetaContentBlockParam>(JsonSerializer.Serialize(block))
        ?? throw new InvalidOperationException("Antwortblock konnte nicht übernommen werden.");
}
