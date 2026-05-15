using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace Reccoo;

public enum MascotMood { Idle, Recording, Paused }

public static class Mascot
{
    private const int Cell = 4;

    public static void Draw(Canvas canvas, MascotMood mood)
    {
        canvas.Children.Clear();

        Brush ink   = Brush("InkDarkBrush");
        Brush dark  = Brush("InkBrush");
        Brush white = Brush("PaperBrush");
        Brush body  = mood switch
        {
            MascotMood.Recording => Brush("AccentBrush"),
            MascotMood.Paused    => Brush("LilacBrush"),
            _                    => Brush("MintBrush"),
        };
        Brush cheek = Brush("CoralBrush");
        Brush gold  = Brush("GoldBrush");

        void Put(int x, int y, Brush c)
        {
            var r = new WpfRectangle
            {
                Width = Cell,
                Height = Cell,
                Fill = c,
                SnapsToDevicePixels = true,
            };
            Canvas.SetLeft(r, x * Cell);
            Canvas.SetTop(r, y * Cell);
            canvas.Children.Add(r);
        }

        // head outline
        for (int x = 7; x <= 16; x++) Put(x, 2, ink);
        for (int x = 6; x <= 17; x++) Put(x, 3, ink);
        for (int x = 5; x <= 18; x++) Put(x, 4, ink);
        for (int y = 5; y <= 13; y++) { Put(5, y, ink); Put(18, y, ink); }
        for (int x = 6; x <= 17; x++) Put(x, 14, ink);
        for (int x = 7; x <= 16; x++) Put(x, 15, ink);

        // body fill
        for (int y = 4; y <= 14; y++)
        {
            int xMin = 6, xMax = 17;
            if (y == 14) { xMin = 7; xMax = 16; }
            for (int x = xMin; x <= xMax; x++)
            {
                if (y >= 5 && y <= 13 && (x == 5 || x == 18)) continue;
                Put(x, y, body);
            }
        }

        // top-left highlight
        for (int x = 7; x <= 14; x++) Put(x, 5, white);
        Put(6, 6, white); Put(6, 7, white);

        // mic grille dots
        foreach (int gy in new[] { 7, 9, 11 })
            for (int gx = 8; gx <= 15; gx += 2)
                Put(gx, gy, dark);

        // cheeks
        Put(7, 11, cheek); Put(8, 11, cheek);
        Put(15, 11, cheek); Put(16, 11, cheek);
        Put(7, 12, cheek); Put(16, 12, cheek);

        // eyes (sparkle = white pixel inside)
        Put(9, 8, ink);  Put(10, 8, ink);
        Put(9, 9, ink);  Put(10, 9, ink);
        Put(10, 8, white);
        Put(13, 8, ink); Put(14, 8, ink);
        Put(13, 9, ink); Put(14, 9, ink);
        Put(14, 8, white);

        // mouth
        if (mood == MascotMood.Recording)
        {
            Put(11, 12, ink); Put(12, 12, ink);
            Put(11, 13, ink); Put(12, 13, ink);
        }
        else if (mood == MascotMood.Paused)
        {
            Put(10, 12, ink); Put(11, 12, ink); Put(12, 12, ink); Put(13, 12, ink);
        }
        else
        {
            Put(10, 12, ink); Put(13, 12, ink);
            Put(11, 13, ink); Put(12, 13, ink);
        }

        // stand connector
        Put(11, 16, ink); Put(12, 16, ink);
        Put(11, 17, ink); Put(12, 17, ink);

        // base
        for (int x = 7; x <= 16; x++) Put(x, 18, ink);
        for (int x = 6; x <= 17; x++) Put(x, 19, ink);
        for (int x = 6; x <= 17; x++) Put(x, 20, ink);
        for (int x = 7; x <= 16; x++) Put(x, 21, ink);
        for (int x = 7; x <= 16; x++) Put(x, 19, dark);
        for (int x = 7; x <= 16; x++) Put(x, 20, dark);

        // mood ornaments
        if (mood == MascotMood.Idle)
        {
            Put(20, 7, gold); Put(19, 8, gold); Put(21, 8, gold); Put(20, 9, gold);
            Put(2, 13, gold); Put(2, 14, gold);
        }
        if (mood == MascotMood.Recording)
        {
            int nx = 19, ny = 4;
            Put(nx + 1, ny, ink); Put(nx + 2, ny, ink);
            Put(nx, ny + 1, ink); Put(nx + 3, ny + 1, cheek);
            Put(nx, ny + 2, ink); Put(nx + 3, ny + 2, cheek);
            Put(nx + 1, ny + 3, ink); Put(nx + 2, ny + 3, ink);
            Put(nx + 4, ny + 3, ink);
            Put(nx + 4, ny + 4, ink);
        }
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
