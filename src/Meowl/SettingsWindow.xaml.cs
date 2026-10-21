using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Meowl.Services;

namespace Meowl;

public partial class SettingsWindow : Window
{
    readonly PetSettings _settings;

    public SettingsWindow(PetSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        PetNameBox.Text = settings.PetName;
        UserNameBox.Text = settings.UserName;
        ProviderBox.SelectedValue = settings.Provider;
        if (ProviderBox.SelectedIndex < 0) ProviderBox.SelectedIndex = 0;
        ModelBox.SelectedValue = settings.Model;
        if (ModelBox.SelectedIndex < 0) ModelBox.SelectedIndex = 0;
        GeminiModelBox.SelectedValue = settings.GeminiModel;
        if (GeminiModelBox.SelectedIndex < 0) GeminiModelBox.SelectedIndex = 0;
        ScaleSlider.Value = settings.Scale;
        WanderBox.IsChecked = settings.Wander;
        WebSearchBox.IsChecked = settings.WebSearch;
        AutostartBox.IsChecked = PetSettings.StartWithWindows;

        ShowKeyStatus(KeyStatus, settings.ApiKeyProtected is not null, settings.ApiKey is not null, "ANTHROPIC_API_KEY");
        ShowKeyStatus(GeminiKeyStatus, settings.GeminiKeyProtected is not null, settings.GeminiApiKey is not null, "GOOGLE_API_KEY / GEMINI_API_KEY");

        Loaded += (_, _) => (settings.UsesGemini ? GeminiKeyBox : ApiKeyBox).Focus();
    }

    static void ShowKeyStatus(System.Windows.Controls.TextBlock status, bool saved, bool available, string envName)
    {
        status.Text = saved
            ? "✓ Ein Schlüssel ist gespeichert."
            : available ? $"✓ Schlüssel aus der Umgebungsvariable {envName} wird benutzt." : "";
        status.Visibility = status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void ProviderBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ClaudePanel is null || GeminiPanel is null) return;
        var gemini = ProviderBox.SelectedValue as string == PetSettings.ProviderGemini;
        ClaudePanel.Visibility = gemini ? Visibility.Collapsed : Visibility.Visible;
        GeminiPanel.Visibility = gemini ? Visibility.Visible : Visibility.Collapsed;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (key.Length > 0)
        {
            if (!key.StartsWith("sk-ant-", StringComparison.Ordinal))
            {
                MessageBox.Show(this, "Das sieht nicht wie ein Claude API-Schlüssel aus (er beginnt mit \"sk-ant-\").",
                    "Meowl", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.ApiKey = key;
        }

        var geminiKey = GeminiKeyBox.Password.Trim();
        if (geminiKey.Length > 0)
        {
            if (geminiKey.StartsWith("sk-", StringComparison.Ordinal) || geminiKey.Contains(' '))
            {
                MessageBox.Show(this, "Das sieht nicht wie ein Gemini API-Schlüssel aus. Kopiere ihn unter aistudio.google.com → \"Get API key\".",
                    "Meowl", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.GeminiApiKey = geminiKey;
        }

        _settings.PetName = string.IsNullOrWhiteSpace(PetNameBox.Text) ? "Meowl" : PetNameBox.Text.Trim();
        _settings.UserName = UserNameBox.Text.Trim();
        _settings.Provider = ProviderBox.SelectedValue as string ?? PetSettings.ProviderClaude;
        _settings.Model = ModelBox.SelectedValue as string ?? "claude-opus-5-5";
        _settings.GeminiModel = GeminiModelBox.SelectedValue as string ?? "gemini-3.6-flash";
        _settings.Scale = Math.Round(ScaleSlider.Value, 2);
        _settings.Wander = WanderBox.IsChecked == true;
        _settings.WebSearch = WebSearchBox.IsChecked == true;
        _settings.Save();

        try
        {
            if (PetSettings.StartWithWindows != (AutostartBox.IsChecked == true))
                PetSettings.StartWithWindows = AutostartBox.IsChecked == true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Autostart konnte nicht geändert werden: {ex.Message}", "Meowl");
        }

        DialogResult = true;
    }

    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
