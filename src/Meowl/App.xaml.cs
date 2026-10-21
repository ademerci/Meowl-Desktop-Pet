using System.IO;
using System.Windows;
using System.Windows.Threading;
using Meowl.Services;
using Forms = System.Windows.Forms;

namespace Meowl;

public partial class App : Application
{
    Mutex? _singleInstance;
    PetSettings _settings = null!;
    PetWindow _pet = null!;
    ChatWindow _chat = null!;
    Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        PixelSprites.Validate();

        // "--export-icon pfad.ico" erzeugt das Programmsymbol aus dem Pixel-Sprite
        if (e.Args is ["--export-icon", var iconPath])
        {
            File.WriteAllBytes(iconPath, IconHelper.CreateIco());
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, "Meowl.DesktopPet.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            // Läuft schon: dort einfach den Chat öffnen
            if (EventWaitHandle.TryOpenExisting(OpenChatSignal, out var signal))
                signal.Set();
            Shutdown();
            return;
        }
        ListenForOpenChat();

        DispatcherUnhandledException += OnUnhandledException;

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
        CreateTrayIcon();

        _pet.ShowBubble(_settings.HasApiKey
            ? $"Miau-huu! {_settings.PetName} ist da. Klick mich an zum Chatten! 🦉"
            : "Miau-huu! Hallo! Rechtsklick auf mich → Einstellungen, damit ich sprechen kann. 🦉", 8);
    }

    const string OpenChatSignal = "Meowl.DesktopPet.OpenChat";

    /// <summary>Ein zweiter Start der App öffnet den Chat der laufenden Instanz.</summary>
    void ListenForOpenChat()
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenChatSignal);
        var thread = new Thread(() =>
        {
            while (signal.WaitOne())
                Dispatcher.BeginInvoke(() => _chat?.Open());
        })
        { IsBackground = true, Name = "OpenChatListener" };
        thread.Start();
    }

    void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Chatten", null, (_, _) => _chat.Open());
        menu.Items.Add("Einstellungen", null, (_, _) => OpenSettings());
        menu.Items.Add("Notizen öffnen", null, (_, _) => PetToolbox.OpenNotesFile());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon
        {
            Icon = IconHelper.CreateTrayIcon(),
            Text = "Meowl",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                _chat.Toggle();
        };
    }

    void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings);
        if (dialog.ShowDialog() == true)
        {
            _pet.ApplyScale();
            _chat.RefreshSettings();
            if (_tray is not null)
                _tray.Text = _settings.PetName;
        }
    }

    void ExitApp()
    {
        _settings.LastX = _pet.CurrentX;
        _settings.Save();
        _chat.AllowClose = true;
        _chat.Close();
        _pet.Close();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        Shutdown();
    }

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(PetSettings.DataDir);
            File.AppendAllText(Path.Combine(PetSettings.DataDir, "fehler.log"), $"[{DateTime.Now:s}] {e.Exception}\n\n");
        }
        catch (IOException) { }
        MessageBox.Show($"Ups, da ist etwas schiefgegangen:\n{e.Exception.Message}", "Meowl", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
