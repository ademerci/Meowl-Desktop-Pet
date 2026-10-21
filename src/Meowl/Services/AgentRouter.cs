namespace Meowl.Services;

/// <summary>Ein "Gehirn" für Meowl, egal welche KI dahintersteckt.</summary>
public interface IPetAgent
{
    /// <summary>Kurze Statuszeilen wie "Notiz gespeichert" für die Chat-Anzeige.</summary>
    event Action<string>? Activity;

    Task<string> AskAsync(string userText, PetSettings settings, CancellationToken ct);

    void Reset();
}

/// <summary>Fehler eines Anbieters, die Meldung ist schon für Menschen formuliert.</summary>
public sealed class PetApiException(string message) : Exception(message);

/// <summary>Leitet jede Frage an den Anbieter weiter, der in den Einstellungen gewählt ist.</summary>
public sealed class AgentRouter : IPetAgent
{
    readonly PetAgent _claude;
    readonly GeminiAgent _gemini;

    public AgentRouter(PetToolbox toolbox)
    {
        _claude = new PetAgent(toolbox);
        _gemini = new GeminiAgent(toolbox);
        _claude.Activity += text => Activity?.Invoke(text);
        _gemini.Activity += text => Activity?.Invoke(text);
    }

    public event Action<string>? Activity;

    public Task<string> AskAsync(string userText, PetSettings settings, CancellationToken ct) =>
        settings.UsesGemini
            ? _gemini.AskAsync(userText, settings, ct)
            : _claude.AskAsync(userText, settings, ct);

    public void Reset()
    {
        _claude.Reset();
        _gemini.Reset();
    }
}
