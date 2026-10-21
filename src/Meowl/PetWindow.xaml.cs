using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Meowl.Services;

namespace Meowl;

/// <summary>
/// Das Fenster mit der Katzeneule. Enthält die gesamte Verhaltenslogik:
/// herumhüpfen, fliegen, schlafen, gestreichelt und herumgetragen werden.
/// </summary>
public partial class PetWindow : Window, IPetHost
{
    enum PetState { Idle, Walk, Fly, Fall, Drag, Sleep, Happy, Dance }

    // Position des Fußpunkts im Fenster (unskaliert), siehe PetWindow.xaml
    const double FootX = 130;
    const double FootY = 302;
    const double Gravity = 900;

    readonly PetSettings _settings;
    readonly Random _rng = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly DispatcherTimer _bubbleTimer = new();
    readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    PetState _state = PetState.Idle;
    double _stateTime, _stateDuration = 3;
    double _lastFrame, _t;

    // Fußposition auf dem Bildschirm (DIP)
    double _x, _y, _vx, _vy;
    double _fromX, _toX, _flyHeight;
    bool _facingRight;

    // Kleine Animationen
    double _nextBlink = 2, _blinkTime = -1;
    double _headTilt, _headTiltTarget, _nextHeadTilt = 4;
    double _lastInteraction;
    double _nextChatter = 120;
    double _particleTimer;
    double _talkUntil;
    bool _thinking;

    // Maus
    bool _mouseDown, _dragging;
    Point _dragStartScreen, _dragOffset;
    Point _lastDragPos;
    double _lastDragTime;
    double _petRub;
    Point? _lastHoverPos;

    public event EventHandler? ChatRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    /// <summary>Wird ausgelöst, wenn sich das Fenster bewegt (für das Chatfenster).</summary>
    public event EventHandler? Moved;

    public bool ChatOpen { get; set; }

    public PetWindow(PetSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        ApplyScale();

        _bubbleTimer.Tick += (_, _) => HideBubble();
        _topmostTimer.Tick += (_, _) => { Topmost = false; Topmost = true; };

        var wa = SystemParameters.WorkArea;
        _x = settings.LastX is double lx && lx > wa.Left && lx < wa.Right
            ? lx
            : wa.Right - 220 * Scale;
        _y = wa.Bottom;
        UpdateWindowPosition();

        SourceInitialized += (_, _) => NativeMethods.HideFromAltTab(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) =>
        {
            CompositionTarget.Rendering += OnRendering;
            _topmostTimer.Start();
        };
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    double Scale => _settings.Scale;

    public void ApplyScale()
    {
        RootScale.ScaleX = RootScale.ScaleY = Scale;
        UpdateWindowPosition();
    }

    /// <summary>Kopfbereich des Haustiers in Bildschirmkoordinaten (DIP).</summary>
    public Rect PetHeadRect => new(_x - 50 * Scale, _y - 160 * Scale, 100 * Scale, 80 * Scale);

    public double CurrentX => _x;

    // ------------------------------------------------------------------
    //  Hauptschleife
    // ------------------------------------------------------------------

    void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(now - _lastFrame, 0.05);
        _lastFrame = now;
        _t += dt;
        _stateTime += dt;

        UpdateState(dt);
        UpdateIdleDetails(dt);
        UpdateWindowPosition();
    }

    void UpdateState(double dt)
    {
        var ground = SystemParameters.WorkArea.Bottom;
        var breath = 1 + Math.Sin(_t * 2.2) * 0.012;
        var tail = Math.Sin(_t * 1.6) * 5;
        double wingL = 0, wingR = 0, squashX = 1, squashY = 1, bodyTilt = 0, headY = 0, feet = 0;
        var shadow = Math.Clamp(1 - (ground - _y) / 120, 0, 1);

        switch (_state)
        {
            case PetState.Idle:
                _y = ground;
                if (!_thinking && _stateTime > _stateDuration)
                    ChooseNextAction();
                // ab und zu kurz mit den Flügeln zucken
                var flick = Math.Max(0, Math.Sin(_t * 0.7) - 0.97) * 600;
                wingL = wingR = flick;
                break;

            case PetState.Walk:
            {
                // Vögel hüpfen statt zu laufen
                const double hopPeriod = 0.34;
                var dir = Math.Sign(_toX - _x);
                _x += dir * 75 * dt;
                var phase = _stateTime % hopPeriod / hopPeriod;
                var hop = Math.Sin(phase * Math.PI);
                _y = ground - hop * 11;
                squashY = phase < 0.12 ? 0.92 : 1.03;
                squashX = phase < 0.12 ? 1.06 : 0.98;
                wingL = wingR = hop * 12;
                tail += hop * 6;
                if (dir == 0 || Math.Abs(_toX - _x) < 3)
                    SetState(PetState.Idle, RandomIdle());
                break;
            }

            case PetState.Fly:
            {
                var u = Math.Clamp(_stateTime / _stateDuration, 0, 1);
                var eased = u * u * (3 - 2 * u);
                _x = _fromX + (_toX - _fromX) * eased;
                _y = ground - 4 * _flyHeight * u * (1 - u);
                var flap = Math.Sin(_t * 26);
                wingL = wingR = 55 + flap * 35;
                bodyTilt = (_toX > _fromX ? 1 : -1) * 8 * Math.Sin(u * Math.PI);
                feet = 5 * Math.Sin(u * Math.PI);
                tail = -6;
                if (u >= 1)
                {
                    Land();
                }
                break;
            }

            case PetState.Fall:
            {
                // Mit flatternden Flügeln langsam zu Boden gleiten
                _vy = Math.Min(_vy + Gravity * dt, 170);
                _vx *= Math.Pow(0.2, dt);
                _x += _vx * dt;
                _y += _vy * dt;
                var flap = Math.Sin(_t * 24);
                wingL = wingR = 60 + flap * 30;
                feet = 4;
                if (_y >= ground)
                {
                    _y = ground;
                    Land();
                }
                break;
            }

            case PetState.Drag:
            {
                var flap = Math.Sin(_t * 14);
                wingL = wingR = 40 + flap * 25;
                feet = 6 + Math.Sin(_t * 9) * 2;
                bodyTilt = Math.Clamp(-_vx * 0.03, -18, 18);
                tail = -bodyTilt * 0.8;
                break;
            }

            case PetState.Sleep:
                _y = ground;
                breath = 1 + Math.Sin(_t * 1.1) * 0.025;
                headY = 3;
                _headTiltTarget = 7;
                tail = Math.Sin(_t * 0.6) * 2;
                _particleTimer += dt;
                if (_particleTimer > 1.6)
                {
                    _particleTimer = 0;
                    SpawnParticle(ZzzSprite, 30);
                }
                break;

            case PetState.Happy:
            {
                var hop = Math.Abs(Math.Sin(_stateTime * Math.PI * 2.4));
                _y = ground - hop * 16;
                wingL = wingR = 30 + hop * 40;
                tail = Math.Sin(_t * 12) * 12;
                squashY = 1 + hop * 0.04;
                _particleTimer += dt;
                if (_particleTimer > 0.25)
                {
                    _particleTimer = 0;
                    SpawnParticle(HeartSprite, 0);
                }
                if (_stateTime > _stateDuration)
                    SetState(PetState.Idle, RandomIdle());
                break;
            }

            case PetState.Dance:
            {
                var beat = _stateTime * Math.PI * 2 * 1.8;
                var bounce = Math.Abs(Math.Sin(beat));
                _y = ground - bounce * 8;
                _headTiltTarget = Math.Sin(beat / 2) * 14;
                wingL = 20 + Math.Max(0, Math.Sin(beat)) * 70;
                wingR = 20 + Math.Max(0, -Math.Sin(beat)) * 70;
                bodyTilt = Math.Sin(beat / 2) * 6;
                tail = Math.Sin(beat) * 14;
                squashY = 1 - bounce * 0.05;
                _facingRight = Math.Sin(beat / 4) > 0;
                _particleTimer += dt;
                if (_particleTimer > 0.45)
                {
                    _particleTimer = 0;
                    SpawnParticle(NoteSprite, 0);
                }
                if (_stateTime > _stateDuration)
                    SetState(PetState.Idle, RandomIdle());
                break;
            }
        }

        var wa = SystemParameters.WorkArea;
        var margin = 55 * Scale;
        if (_state != PetState.Drag)
            _x = Math.Clamp(_x, wa.Left + margin, wa.Right - margin);

        Pet.SetFacing(_facingRight);
        Pet.SetWings(wingL, wingR);
        Pet.SetTail(tail);
        Pet.SetBreath(breath);
        Pet.SetSquash(squashX, squashY);
        Pet.SetBodyTilt(bodyTilt);
        Pet.SetShadow(shadow);
        Pet.SetFeetOffset(feet);

        _headTilt += (_headTiltTarget - _headTilt) * Math.Min(1, dt * 6);
        Pet.SetHead(_headTilt, headY);
    }

    /// <summary>Blinzeln, Kopf neigen, dem Mauszeiger nachschauen, sprechen.</summary>
    void UpdateIdleDetails(double dt)
    {
        var eyeMode = _state switch
        {
            PetState.Sleep => EyeMode.Closed,
            PetState.Happy => EyeMode.Happy,
            PetState.Dance => EyeMode.Happy,
            _ => EyeMode.Open,
        };
        Pet.SetEyeMode(eyeMode);

        // Blinzeln
        if (_blinkTime < 0 && _t > _nextBlink)
        {
            _blinkTime = 0;
            _nextBlink = _t + 2 + _rng.NextDouble() * 4;
        }
        if (_blinkTime >= 0)
        {
            _blinkTime += dt;
            var b = _blinkTime / 0.16;
            Pet.SetEyeOpenness(b < 0.5 ? 1 - b * 2 : (b - 0.5) * 2);
            if (b >= 1) { _blinkTime = -1; Pet.SetEyeOpenness(1); }
        }
        else
        {
            Pet.SetEyeOpenness(_state == PetState.Drag ? 1.08 : 1);
        }

        // Kopf neigen
        if (_state is PetState.Idle or PetState.Walk)
        {
            if (_thinking)
            {
                _headTiltTarget = -12 + Math.Sin(_t * 1.5) * 3;
            }
            else if (_t > _nextHeadTilt)
            {
                _headTiltTarget = _rng.NextDouble() < 0.4 ? (_rng.NextDouble() - 0.5) * 26 : 0;
                _nextHeadTilt = _t + 2 + _rng.NextDouble() * 5;
            }
        }
        else if (_state is PetState.Fly or PetState.Fall or PetState.Drag or PetState.Happy)
        {
            _headTiltTarget = 0;
        }

        // Dem Mauszeiger nachschauen
        var cursor = NativeMethods.GetCursorPosDip(this);
        var head = new Point(_x, _y - 115 * Scale);
        var dx = cursor.X - head.X;
        var dy = cursor.Y - head.Y;
        var len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        if (_thinking)
            Pet.SetLook(-1.5, -2.5);
        else if (len < 700)
            Pet.SetLook(dx / len * 2.6, dy / len * 2.2);
        else
            Pet.SetLook(0, 0);

        // Sprechen (Mund auf/zu)
        Pet.SetMouthOpen(_t < _talkUntil && Math.Sin(_t * 18) > 0);
        Pet.SetBlush(_state is PetState.Happy or PetState.Dance ? 0.75 : 0.4);

        // Nach längerer Ruhe einschlafen
        if (_state == PetState.Idle && !ChatOpen && !_thinking && _t - _lastInteraction > 240)
            SetState(PetState.Sleep, double.MaxValue);

        // Gelegentlich etwas sagen
        if (_t > _nextChatter)
        {
            _nextChatter = _t + 180 + _rng.NextDouble() * 300;
            if (_state == PetState.Idle && !ChatOpen && Bubble.Visibility != Visibility.Visible)
                ShowBubble(Chatter[_rng.Next(Chatter.Length)], 4);
        }
    }

    static readonly string[] Chatter =
    [
        "Huhu! 🦉", "Miau?", "*putzt Federn*", "Brauchst du Hilfe? Klick mich an!",
        "Ich bin halb Katze, halb Eule. Ganz süß.", "Zeit für eine Pause? ☕",
        "*schaut neugierig*", "Prrr-huu!", "Trink mal einen Schluck Wasser 💧",
    ];

    void ChooseNextAction()
    {
        if (ChatOpen || !_settings.Wander)
        {
            SetState(PetState.Idle, RandomIdle());
            return;
        }

        var wa = SystemParameters.WorkArea;
        var roll = _rng.NextDouble();
        if (roll < 0.45)
        {
            var dist = (80 + _rng.NextDouble() * 280) * (_rng.Next(2) == 0 ? -1 : 1);
            _toX = Math.Clamp(_x + dist, wa.Left + 80, wa.Right - 80);
            _facingRight = _toX > _x;
            SetState(PetState.Walk, double.MaxValue);
        }
        else if (roll < 0.62)
        {
            FlyTo(wa.Left + 80 + _rng.NextDouble() * (wa.Width - 160));
        }
        else
        {
            SetState(PetState.Idle, RandomIdle());
        }
    }

    void FlyTo(double targetX)
    {
        _fromX = _x;
        _toX = targetX;
        _facingRight = _toX > _x;
        var dist = Math.Abs(_toX - _fromX);
        _flyHeight = Math.Min(80 + dist * 0.35, SystemParameters.WorkArea.Height * 0.6);
        SetState(PetState.Fly, Math.Max(1.2, dist / 260));
    }

    void Land()
    {
        _vx = _vy = 0;
        SetState(PetState.Idle, RandomIdle());
        Pet.SetSquash(1.08, 0.9);
        Moved?.Invoke(this, EventArgs.Empty);
    }

    double RandomIdle() => 3 + _rng.NextDouble() * 6;

    void SetState(PetState state, double duration)
    {
        _state = state;
        _stateTime = 0;
        _stateDuration = duration;
        _particleTimer = 0;
        if (state != PetState.Dance && state != PetState.Sleep)
            _headTiltTarget = 0;
        MenuSleep.Header = state == PetState.Sleep ? "☀  Aufwecken" : "💤  Schlafen";
    }

    void UpdateWindowPosition()
    {
        var left = _x - FootX * Scale;
        var top = _y - FootY * Scale;
        if (Math.Abs(Left - left) > 0.01 || Math.Abs(Top - top) > 0.01)
        {
            Left = left;
            Top = top;
            Moved?.Invoke(this, EventArgs.Empty);
        }
    }

    // ------------------------------------------------------------------
    //  Effekte
    // ------------------------------------------------------------------

    static readonly BitmapSource HeartSprite = PixelSprites.ToBitmap(PixelSprites.Heart);
    static readonly BitmapSource ZzzSprite = PixelSprites.ToBitmap(PixelSprites.Zzz);
    static readonly BitmapSource NoteSprite = PixelSprites.ToBitmap(PixelSprites.Note);

    void SpawnParticle(BitmapSource sprite, double offsetX)
    {
        // 4 DIP pro Pixel, etwas kleiner als Meowl (5 DIP)
        var tb = new Image
        {
            Source = sprite,
            Width = sprite.PixelWidth * 4,
            Height = sprite.PixelHeight * 4,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(tb, BitmapScalingMode.NearestNeighbor);
        var startX = 130 + offsetX + (_rng.NextDouble() - 0.5) * 50;
        var startY = 140.0;
        Canvas.SetLeft(tb, startX);
        Canvas.SetTop(tb, startY);
        FxLayer.Children.Add(tb);

        var dur = TimeSpan.FromSeconds(1.4);
        var up = new DoubleAnimation(startY, startY - 70, dur) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var side = new DoubleAnimation(startX, startX + (_rng.NextDouble() - 0.5) * 30, dur);
        var fade = new DoubleAnimation(1, 0, dur) { BeginTime = TimeSpan.Zero };
        fade.Completed += (_, _) => FxLayer.Children.Remove(tb);
        tb.BeginAnimation(Canvas.TopProperty, up);
        tb.BeginAnimation(Canvas.LeftProperty, side);
        tb.BeginAnimation(OpacityProperty, fade);
    }

    public void ShowBubble(string text, double seconds)
    {
        BubbleText.Text = text;
        Bubble.Visibility = ChatOpen ? Visibility.Collapsed : Visibility.Visible;
        var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new BackEase { Amplitude = 0.5 } };
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        _bubbleTimer.Stop();
        _bubbleTimer.Interval = TimeSpan.FromSeconds(seconds);
        _bubbleTimer.Start();
        Talk(Math.Min(2.5, 0.4 + text.Length * 0.04));
    }

    public void HideBubble()
    {
        _bubbleTimer.Stop();
        Bubble.Visibility = Visibility.Collapsed;
    }

    /// <summary>Mund für eine Weile bewegen.</summary>
    public void Talk(double seconds) => _talkUntil = _t + seconds;

    /// <summary>Nachdenk-Pose, während Claude antwortet.</summary>
    public void SetThinking(bool thinking)
    {
        _thinking = thinking;
        if (thinking)
        {
            WakeUp();
            if (_state is PetState.Walk or PetState.Dance)
                SetState(PetState.Idle, RandomIdle());
        }
    }

    void WakeUp()
    {
        _lastInteraction = _t;
        if (_state == PetState.Sleep)
            SetState(PetState.Idle, RandomIdle());
    }

    // ------------------------------------------------------------------
    //  IPetHost (wird von den Agent-Werkzeugen benutzt)
    // ------------------------------------------------------------------

    public void PlayAction(string action)
    {
        _lastInteraction = _t;
        switch (action)
        {
            case "happy": SetState(PetState.Happy, 1.8); break;
            case "dance": SetState(PetState.Dance, 5); break;
            case "sleep": SetState(PetState.Sleep, double.MaxValue); break;
            case "fly":
                var wa = SystemParameters.WorkArea;
                var target = _x + (_x > wa.Left + wa.Width / 2 ? -1 : 1) * (200 + _rng.NextDouble() * 300);
                FlyTo(Math.Clamp(target, wa.Left + 80, wa.Right - 80));
                break;
        }
    }

    public void Remind(string text)
    {
        WakeUp();
        SetState(PetState.Happy, 1.8);
        SystemSounds.Asterisk.Play();
        ShowBubble("⏰ " + text, 30);
    }

    // ------------------------------------------------------------------
    //  Maus
    // ------------------------------------------------------------------

    void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = true;
        _dragging = false;
        _dragStartScreen = NativeMethods.GetCursorPosDip(this);
        _dragOffset = new Point(_dragStartScreen.X - _x, _dragStartScreen.Y - _y);
        Pet.CaptureMouse();
        e.Handled = true;
    }

    void Pet_MouseMove(object sender, MouseEventArgs e)
    {
        var p = NativeMethods.GetCursorPosDip(this);

        if (_mouseDown)
        {
            if (!_dragging && (Math.Abs(p.X - _dragStartScreen.X) > 5 || Math.Abs(p.Y - _dragStartScreen.Y) > 5))
            {
                _dragging = true;
                WakeUp();
                SetState(PetState.Drag, double.MaxValue);
                _lastDragPos = p;
                _lastDragTime = _t;
            }
            if (_dragging)
            {
                var dt = Math.Max(0.001, _t - _lastDragTime);
                _vx = _vx * 0.6 + (p.X - _lastDragPos.X) / dt * 0.4;
                _vy = _vy * 0.6 + (p.Y - _lastDragPos.Y) / dt * 0.4;
                _lastDragPos = p;
                _lastDragTime = _t;
                _x = p.X - _dragOffset.X;
                _y = Math.Min(p.Y - _dragOffset.Y, SystemParameters.WorkArea.Bottom);
                if (Math.Abs(_vx) > 30)
                    _facingRight = _vx > 0;
            }
            return;
        }

        // Streicheln: Maus ohne Klick über Meowl hin und her bewegen
        if (_lastHoverPos is Point last)
        {
            _petRub = Math.Max(0, _petRub - 0.5) + Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y);
            if (_petRub > 450 && _state is PetState.Idle or PetState.Sleep)
            {
                _petRub = 0;
                WakeUp();
                SetState(PetState.Happy, 1.8);
                if (_rng.Next(3) == 0)
                    ShowBubble(_rng.Next(2) == 0 ? "Prrrr… 💕" : "Hihi, das kitzelt!", 2.5);
            }
        }
        _lastHoverPos = p;
    }

    void Pet_MouseEnter(object sender, MouseEventArgs e)
    {
        _lastHoverPos = null;
        _petRub = 0;
    }

    void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        _mouseDown = false;
        Pet.ReleaseMouseCapture();
        _lastInteraction = _t;

        if (_dragging)
        {
            _dragging = false;
            _vx = Math.Clamp(_vx, -600, 600);
            _vy = Math.Clamp(_vy, -300, 100);
            SetState(PetState.Fall, double.MaxValue);
            return;
        }

        if (_state == PetState.Sleep)
        {
            SetState(PetState.Happy, 1.2);
            ShowBubble("*gähn* Oh, hallo! 🌞", 3);
            return;
        }

        HideBubble();
        ChatRequested?.Invoke(this, EventArgs.Empty);
    }

    void Bubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        HideBubble();
        e.Handled = true;
    }

    // ------------------------------------------------------------------
    //  Kontextmenü
    // ------------------------------------------------------------------

    void PetMenu_Opened(object sender, RoutedEventArgs e)
    {
        MenuStay.IsChecked = !_settings.Wander;
        WakeUp();
    }

    void MenuChat_Click(object sender, RoutedEventArgs e) => ChatRequested?.Invoke(this, EventArgs.Empty);
    void MenuPet_Click(object sender, RoutedEventArgs e) => PlayAction("happy");
    void MenuFly_Click(object sender, RoutedEventArgs e) => PlayAction("fly");
    void MenuDance_Click(object sender, RoutedEventArgs e) => PlayAction("dance");

    void MenuSleep_Click(object sender, RoutedEventArgs e)
    {
        if (_state == PetState.Sleep) WakeUp();
        else PlayAction("sleep");
    }

    void MenuStay_Click(object sender, RoutedEventArgs e)
    {
        _settings.Wander = !MenuStay.IsChecked;
        _settings.Save();
    }

    void MenuNotes_Click(object sender, RoutedEventArgs e) => PetToolbox.OpenNotesFile();
    void MenuSettings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    void MenuExit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _settings.LastX = _x;
        base.OnClosing(e);
    }
}
