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
        ModelBox.SelectedValue = settings.Model;
        if (ModelBox.SelectedIndex < 0) ModelBox.SelectedIndex = 0;
        ScaleSlider.Value = settings.Scale;
        WanderBox.IsChecked = settings.Wander;
        WebSearchBox.IsChecked = settings.WebSearch;
        AutostartBox.IsChecked = PetSettings.StartWithWindows;

        KeyStatus.Text = settings.ApiKeyProtected is not null
            ? "✓ Ein Schlüssel ist gespeichert."
            : settings.HasApiKey ? "✓ Schlüssel aus der Umgebungsvariable ANTHROPIC_API_KEY wird benutzt." : "";
        if (KeyStatus.Text.Length == 0) KeyStatus.Visibility = Visibility.Collapsed;

        Loaded += (_, _) => ApiKeyBox.Focus();
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

        _settings.PetName = string.IsNullOrWhiteSpace(PetNameBox.Text) ? "Meowl" : PetNameBox.Text.Trim();
        _settings.UserName = UserNameBox.Text.Trim();
        _settings.Model = ModelBox.SelectedValue as string ?? "claude-opus-5-5";
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
