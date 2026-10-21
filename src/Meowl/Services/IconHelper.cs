using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Meowl.Services;

/// <summary>Erzeugt das Programm- und Tray-Symbol aus dem Pixel-Sprite.</summary>
static class IconHelper
{
    /// <summary>Kopf von Meowl als PNG in der gewünschten Größe.</summary>
    static byte[] RenderHeadPng(int size)
    {
        var pet = new PetView();
        // Quadratischer Ausschnitt um den Kopf: volle Sprite-Breite (32 Pixel = 160 DIP),
        // so ist das 32er-Symbol pixelgenau
        var scale = size / 160.0;
        pet.RenderTransform = new TransformGroup
        {
            Children = { new TranslateTransform(0, 20), new ScaleTransform(scale, scale) },
        };
        pet.Measure(new Size(160, 180));
        pet.Arrange(new Rect(0, 0, 160, 180));
        pet.UpdateLayout();

        var host = new System.Windows.Controls.Canvas { Width = size, Height = size };
        host.Children.Add(pet);
        host.Measure(new Size(size, size));
        host.Arrange(new Rect(0, 0, size, size));
        host.UpdateLayout();

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>ICO-Datei mit mehreren Größen (PNG-komprimiert, ab Windows Vista unterstützt).</summary>
    public static byte[] CreateIco(params int[] sizes)
    {
        if (sizes.Length == 0) sizes = [16, 24, 32, 48, 64, 256];
        var images = sizes.Select(RenderHeadPng).ToArray();

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0);            // reserviert
        w.Write((short)1);            // Typ: Icon
        w.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);         // keine Palette
            w.Write((byte)0);
            w.Write((short)1);        // Farbebenen
            w.Write((short)32);       // Bit pro Pixel
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images)
            w.Write(img);
        w.Flush();
        return ms.ToArray();
    }

    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var ms = new MemoryStream(CreateIco(16, 20, 24, 32, 48));
        return new System.Drawing.Icon(ms, System.Windows.Forms.SystemInformation.SmallIconSize);
    }
}
