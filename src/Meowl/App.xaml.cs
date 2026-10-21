using System.Windows;
using Meowl.Services;

namespace Meowl;

public partial class App : Application
{
    PetSettings _settings = null!;
    PetWindow _pet = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        PixelSprites.Validate();

        _settings = PetSettings.Load();
        _pet = new PetWindow(_settings);
        _pet.ExitRequested += (_, _) => ExitApp();
        _pet.Show();
    }

    void ExitApp()
    {
        _settings.LastX = _pet.CurrentX;
        _settings.Save();
        _pet.Close();
        Shutdown();
    }
}
