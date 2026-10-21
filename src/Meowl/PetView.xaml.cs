using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static Meowl.PixelSprites;

namespace Meowl;

public enum EyeMode { Open, Closed, Happy }

/// <summary>
/// Die Grafik der Katzeneule in Pixel-Art. Enthält keine Logik, nur Stellschrauben
/// für die Animation. Stufenlose Werte (Flügelwinkel, Schwanz, ...) werden auf
/// passende Einzelbilder abgebildet, damit alles pixelgenau bleibt.
/// </summary>
public partial class PetView : UserControl
{
    const int W = PixelSprites.Width, H = PixelSprites.Height;

    readonly WriteableBitmap _bitmap = new(W, H, 96, 96, PixelFormats.Bgra32, null);
    readonly uint[] _pixels = new uint[W * H];

    double _wingL, _wingR, _tail, _breath = 1, _eyeOpenness = 1, _lookX, _lookY, _headTilt, _headY;
    double _blush = 0.4, _shadow = 1, _feet;
    EyeMode _eyeMode = EyeMode.Open;
    bool _mouthOpen, _redrawQueued;
    string? _lastFrame;

    public PetView()
    {
        InitializeComponent();
        Sprite.Source = _bitmap;
        Redraw();
    }

    /// <summary>true = schaut nach rechts, false = nach links.</summary>
    public void SetFacing(bool right) => FacingTransform.ScaleX = right ? -1 : 1;

    /// <summary>Flügel in Grad, 0 = angelegt, ~80 = ausgebreitet.</summary>
    public void SetWings(double left, double right) { _wingL = left; _wingR = right; Invalidate(); }

    public void SetTail(double angle) { _tail = angle; Invalidate(); }

    /// <summary>Atmen: lässt den Kopf um einen Pixel wippen.</summary>
    public void SetBreath(double scaleY) { _breath = scaleY; Invalidate(); }

    /// <summary>1 = Augen offen, 0 = zu (Blinzeln).</summary>
    public void SetEyeOpenness(double openness) { _eyeOpenness = openness; Invalidate(); }

    public void SetEyeMode(EyeMode mode) { _eyeMode = mode; Invalidate(); }

    /// <summary>Blickrichtung, etwa -2.5 bis 2.5 (wird zu ±1 Pixel).</summary>
    public void SetLook(double dx, double dy)
    {
        // Spiegelung durch die Blickrichtung ausgleichen, damit die Augen
        // immer zur echten Position (z. B. des Mauszeigers) schauen.
        _lookX = dx * FacingTransform.ScaleX;
        _lookY = dy;
        Invalidate();
    }

    public void SetHead(double tiltDegrees, double offsetY) { _headTilt = tiltDegrees; _headY = offsetY; Invalidate(); }

    // In Pixel-Art wird nicht verzerrt oder gedreht: diese beiden bleiben bewusst ohne Wirkung.
    public void SetSquash(double scaleX, double scaleY) { }
    public void SetBodyTilt(double degrees) { }

    public void SetMouthOpen(bool open) { _mouthOpen = open; Invalidate(); }

    public void SetBlush(double opacity) { _blush = opacity; Invalidate(); }

    /// <summary>Schatten ausblenden, wenn Meowl in der Luft ist.</summary>
    public void SetShadow(double opacity) { _shadow = opacity; Invalidate(); }

    /// <summary>Beine baumeln lassen (beim Tragen/Fliegen).</summary>
    public void SetFeetOffset(double y) { _feet = y; Invalidate(); }

    // ------------------------------------------------------------------

    void Invalidate()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        Dispatcher.BeginInvoke(Redraw, DispatcherPriority.Render);
    }

    static string[] WingFrame(double angle, out int x, out int y)
    {
        if (angle < 22) { x = 4; y = 21; return WingDown; }
        if (angle < 55) { x = 0; y = 21; return WingMid; }
        x = 0; y = 13; return WingUp;
    }

    void Redraw()
    {
        _redrawQueued = false;

        // Stufenlose Werte in Pixel-Schritte umrechnen
        var wingL = WingFrame(_wingL, out var wlx, out var wly);
        var wingR = WingFrame(_wingR, out var wrx, out var wry);
        var tailUp = _tail > 4;
        var eyes = _eyeMode switch
        {
            EyeMode.Closed => EyeClosed,
            EyeMode.Happy => EyeHappy,
            _ => _eyeOpenness > 0.6 ? EyeOpen : _eyeOpenness > 0.25 ? EyeHalf : EyeClosed,
        };
        var lookX = Math.Abs(_lookX) > 1.2 ? Math.Sign(_lookX) : 0;
        var lookY = Math.Abs(_lookY) > 1.1 ? Math.Sign(_lookY) : 0;
        var headX = Math.Abs(_headTilt) > 6 ? Math.Sign(_headTilt) : 0;
        var headY = (int)Math.Round(_headY / 3) - (_breath > 1.006 ? 1 : 0);
        var shadow = _shadow > 0.66 ? 2 : _shadow > 0.2 ? 1 : 0;
        var feet = _feet > 3 ? 1 : 0;
        var blush = _blush > 0.6;

        // Nur neu zeichnen, wenn sich wirklich ein Pixel ändert
        var frame = $"{wingL.GetHashCode()}|{wingR.GetHashCode()}|{tailUp}|{eyes.GetHashCode()}|{lookX},{lookY}|{headX},{headY}|{_mouthOpen}|{blush}|{shadow}|{feet}";
        if (frame == _lastFrame) return;
        _lastFrame = frame;

        Array.Clear(_pixels);

        // Schatten am Boden
        if (shadow > 0)
        {
            var shade = shadow == 2 ? Palette['G'] : 0x28000000u;
            for (var x = 9; x <= 22; x++)
                _pixels[(H - 1) * W + x] = shade;
        }

        // Von hinten nach vorne
        if (tailUp) PixelSprites.Draw(_pixels, TailUp, 20, 22);
        else PixelSprites.Draw(_pixels, TailDown, 20, 28);
        // In der Luft werden die Pfoten eingezogen (ein Pixel unter den Bauch)
        PixelSprites.Draw(_pixels, Paws, 10, 33 - feet);
        PixelSprites.Draw(_pixels, Body, 7, 19);
        PixelSprites.Draw(_pixels, wingL, wlx, wly);
        PixelSprites.Draw(_pixels, wingR, wrx, wry, mirror: true);

        var hx = HeadX + headX;
        var hy = HeadY + headY;
        PixelSprites.Draw(_pixels, Head, hx, hy);
        PixelSprites.Draw(_pixels, eyes, hx + EyeLeftX + lookX, hy + EyeY + lookY);
        PixelSprites.Draw(_pixels, eyes, hx + EyeRightX + lookX, hy + EyeY + lookY);
        PixelSprites.Draw(_pixels, _mouthOpen ? MouthOpen : MouthClosed, hx + 10, hy + 13);

        var cheek = Palette[blush ? 'b' : 'B'];
        foreach (var cx in CheekColumns)
            _pixels[(hy + CheekRow) * W + hx + cx] = cheek;

        _bitmap.WritePixels(new Int32Rect(0, 0, W, H), _pixels, W * 4, 0);
    }
}
