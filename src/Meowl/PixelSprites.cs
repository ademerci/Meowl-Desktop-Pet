using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Meowl;

/// <summary>
/// Pixel-Art der Katzeneule: 32 x 36 Pixel, jedes Teil als Textraster.
/// Ein Zeichen = ein Pixel, '.' = durchsichtig, die Farben stehen in <see cref="Palette"/>.
/// </summary>
static class PixelSprites
{
    public const int Width = 32;
    public const int Height = 36;

    public static readonly Dictionary<char, uint> Palette = new()
    {
        ['K'] = 0xFF2B1A10, // Umriss
        ['O'] = 0xFFD8964F, // Fell orange
        ['S'] = 0xFF8A5326, // Tigerstreifen
        ['W'] = 0xFFFFFFFF, // weiß
        ['w'] = 0xFFE6DCCF, // weiß, Schatten
        ['P'] = 0xFFF09AA0, // Nase, Ohr innen
        ['B'] = 0xFFF7B9C3, // Wangen
        ['b'] = 0xFFF28DA0, // Wangen, verliebt
        ['E'] = 0xFF1A0F08, // Auge
        ['e'] = 0xFF5A3A26, // Auge, braun
        ['M'] = 0xFF7A4A3A, // Mund
        ['m'] = 0xFFC4566A, // Mund innen
        ['R'] = 0xFF9A6233, // Flügel
        ['r'] = 0xFF5E3A1F, // Flügel dunkel
        ['L'] = 0xFFC99766, // Flügel hell
        ['T'] = 0xFF7C6655, // Schwanz
        ['t'] = 0xFFA99D91, // Schwanz hell
        ['G'] = 0x50000000, // Schatten am Boden
        // Effekte
        ['H'] = 0xFFE8506A, // Herz
        ['h'] = 0xFFFFB3C1, // Herz Glanz
        ['Z'] = 0xFF5B8DC9, // Zzz
        ['N'] = 0xFF7A5CC8, // Noten
    };

    // ---------------- Kopf (24 x 18, liegt bei x=4, y=3) ----------------
    public static readonly string[] Head =
    [
        "..KKK..............KKK..",
        ".KPOK..............KOPK.",
        ".KPOOKKKKKKKKKKKKKKOOPK.",
        ".KOOOOOOOSOSSOSOOOOOOOK.",
        "KOOOOOOOOSOSSOSOOOOOOOOK",
        "KOOOOOSOOOOSSOOOOSOOOOOK",
        "KOOOOOOOOOOWWOOOOOOOOOOK",
        "KOOOOOOOOOWWWWOOOOOOOOOK",
        "KOOOWWWWWWWWWWWWWWWWOOOK",
        "KOOWWWWWWWWWWWWWWWWWWOOK",
        "KOSSWWWWWWWWWWWWWWWWSSOK",
        "KSSWWWWWWWWWWWWWWWWWWSSK",
        "KOWWWWWWWWWWWWWWWWWWWWOK",
        "KOWWWWWWWWWWWWWWWWWWWWOK",
        ".KWWWWWWWWWWWWWWWWWWWWK.",
        "..KwWWWWWWWWWWWWWWWWwK..",
        "...KwWWWWWWWWWWWWWWwK...",
        ".....KKKKKKKKKKKKKK.....",
    ];
    public const int HeadX = 4, HeadY = 3;

    // Kulleraugen (6 x 6), relativ zum Kopf bei (3,7) und (15,7)
    public static readonly string[] EyeOpen = [".EEEE.", "EWWEEE", "EWWEEE", "EEEEWE", "EEEEEE", ".EEEE."];
    public static readonly string[] EyeHalf = ["......", "......", "EEEEEE", "EWWEEE", "EEEEEE", ".EEEE."];
    public static readonly string[] EyeClosed = ["......", "......", "......", "E....E", ".EEEE.", "......"];
    public static readonly string[] EyeHappy = ["......", ".EEEE.", "E....E", "......", "......", "......"];
    public const int EyeLeftX = 3, EyeRightX = 15, EyeY = 7;

    // Nase und Mund, relativ zum Kopf bei (10,13)
    public static readonly string[] MouthClosed = [".PP.", "M..M", ".MM."];
    public static readonly string[] MouthOpen = [".PP.", "MmmM", ".MM."];

    // Wangen, relativ zum Kopf (Zeile 13)
    public static readonly int[] CheekColumns = [2, 3, 20, 21];
    public const int CheekRow = 13;

    // ---------------- Körper (18 x 14, liegt bei x=7, y=19) ----------------
    public static readonly string[] Body =
    [
        "...KKKKKKKKKKKK...",
        "..KWWWWWWWWWWWWK..",
        ".KWWWWWWWWWWWWWWK.",
        "KWWWWWWWWWWWWWWWWK",
        "KWWWWWWWWWWWWWWWWK",
        "KWWWWWWwWWwWWWWWWK",
        "KWWWWWWWwwWWWWWWWK",
        "KWWWWWWWWWWWWWWWwK",
        "KWWWWWWwWWwWWWWWwK",
        "KWWWWWWWwwWWWWWwwK",
        "KwWWWWWWWWWWWWwwwK",
        ".KwWWWWWWWWWWWwwK.",
        "..KwwWWWWWWWWwwK..",
        "...KKKKKKKKKKKK...",
    ];

    // Pfoten (12 x 3, liegen bei x=10, y=33)
    public static readonly string[] Paws =
    [
        "KWWWK..KWWWK",
        "KWwWK..KWwWK",
        ".KKK....KKK.",
    ];

    // ---------------- Linker Flügel, der rechte ist gespiegelt ----------------
    public static readonly string[] WingDown =    // bei x=4, y=21
    [
        "....KK..",
        "...KLRK.",
        "..KLRRK.",
        "..KRRrK.",
        ".KRRrRK.",
        ".KRrRRK.",
        ".KRrRK..",
        ".KrRRK..",
        ".KrRK...",
        ".KrK....",
        "..K.....",
    ];

    public static readonly string[] WingMid =     // bei x=0, y=21
    [
        "........KK..",
        "......KKLRK.",
        "....KKLRRRK.",
        "..KKLRRRrRK.",
        "KKRRRRrRRK..",
        "KrRrRrRKK...",
        ".KrKrKK.....",
        "..K.K.......",
    ];

    public static readonly string[] WingUp =      // bei x=0, y=13
    [
        "K...........",
        "KK..........",
        "KrK.........",
        "KRrK........",
        "KRRrK.......",
        "KrRRRK......",
        ".KRrRLK.....",
        ".KrRRRLK....",
        "..KRrRRLKK..",
        "...KRRRRLRK.",
        "....KKRRRRK.",
        "......KKKK..",
    ];

    // ---------------- Schwanz ----------------
    public static readonly string[] TailDown =    // bei x=20, y=28
    [
        "..KKKK......",
        "..KTTTKK....",
        "...KTTTTTKK.",
        "....KTTTTTtK",
        ".....KKTTttK",
        ".......KKttK",
        ".........KK.",
    ];

    public static readonly string[] TailUp =      // bei x=20, y=22
    [
        "..........KK",
        ".........KtK",
        "........KTtK",
        ".......KTTtK",
        "......KTTTK.",
        ".....KTTTK..",
        "...KKTTTK...",
        "..KTTTTK....",
        "...KKKK.....",
    ];

    // ---------------- Effekte ----------------
    public static readonly string[] Heart =
    [
        ".HH.HH.",
        "HhHHHHH",
        "HHHHHHH",
        ".HHHHH.",
        "..HHH..",
        "...H...",
    ];

    public static readonly string[] Zzz =
    [
        "ZZZZZ",
        "...Z.",
        "..Z..",
        ".Z...",
        "ZZZZZ",
    ];

    public static readonly string[] Note =
    [
        "..NN.",
        "..N.N",
        "..N..",
        "..N..",
        "NNN..",
        "NNN..",
    ];

    /// <summary>Zeichnet ein Teil in den Pixelpuffer (ARGB).</summary>
    public static void Draw(uint[] buffer, string[] part, int x, int y, bool mirror = false)
    {
        for (var row = 0; row < part.Length; row++)
        {
            var line = part[row];
            var py = y + row;
            if (py < 0 || py >= Height) continue;
            for (var col = 0; col < line.Length; col++)
            {
                var c = line[col];
                if (c == '.') continue;
                var px = x + col;
                if (mirror) px = Width - 1 - px;
                if (px < 0 || px >= Width) continue;
                buffer[py * Width + px] = Palette[c];
            }
        }
    }

    /// <summary>Kleines eigenständiges Sprite (z. B. ein Herz) als Bild.</summary>
    public static BitmapSource ToBitmap(string[] sprite)
    {
        var w = sprite.Max(r => r.Length);
        var h = sprite.Length;
        var pixels = new uint[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < sprite[y].Length; x++)
                if (sprite[y][x] != '.')
                    pixels[y * w + x] = Palette[sprite[y][x]];
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Prüft beim Start, ob alle Raster rechteckig sind (Tippfehler finden).</summary>
    public static void Validate()
    {
        foreach (var (name, part) in new (string, string[])[]
        {
            (nameof(Head), Head), (nameof(Body), Body), (nameof(Paws), Paws),
            (nameof(WingDown), WingDown), (nameof(WingMid), WingMid), (nameof(WingUp), WingUp),
            (nameof(TailDown), TailDown), (nameof(TailUp), TailUp),
            (nameof(EyeOpen), EyeOpen), (nameof(EyeHalf), EyeHalf), (nameof(EyeClosed), EyeClosed), (nameof(EyeHappy), EyeHappy),
        })
        {
            if (part.Any(r => r.Length != part[0].Length))
                throw new InvalidOperationException($"Sprite {name} ist nicht rechteckig.");
            if (part.SelectMany(r => r).Any(c => c != '.' && !Palette.ContainsKey(c)))
                throw new InvalidOperationException($"Sprite {name} enthält eine unbekannte Farbe.");
        }
    }
}
