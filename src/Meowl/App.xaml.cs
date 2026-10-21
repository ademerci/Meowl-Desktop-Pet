using System.Windows;
using Meowl.Services;

namespace Meowl;

public partial class App : Application
{
    PetSettings _settings = null!;
    PetWindow _pet = null!;
    ChatWindow _chat = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        PixelSprites.Validate();

        _settings = PetSettings.Load();
        _pet = new PetWindow(_settings);
        var agent = new PetAgent(new PetToolbox(_pet));
        _chat = new ChatWindow(_pet, agent, _settings);

        _pet.ChatRequested += (_, _) => _chat.Toggle();
        _pet.SettingsRequested += (_, _) => OpenSettings();
        _pet.ExitRequested += (_, _) => ExitApp();
        _chat.SettingsRequested += (_, _) => OpenSettings();

        _pet.Show();
        _chat.Owner = _pet; // erst nach Show() erlaubt

        _pet.ShowBubble(_settings.HasApiKey
            ? $"Miau-huu! {_settings.PetName} ist da. Klick mich an zum Chatten! 🦉"
            : "Miau-huu! Hallo! Rechtsklick auf mich → Einstellungen, damit ich sprechen kann. 🦉", 8);
    }

    void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings);
        if (dialog.ShowDialog() == true)
        {
            _pet.ApplyScale();
            _chat.RefreshSettings();
        }
    }

    void ExitApp()
    {
        _settings.LastX = _pet.CurrentX;
        _settings.Save();
        _chat.AllowClose = true;
        _chat.Close();
        _pet.Close();
        Shutdown();
    }
}
