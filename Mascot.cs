using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace CocoaRecorder;

public enum MascotMood { Idle, Countdown, Recording, Paused }

/// <summary>
/// 코코아 — 22칸 × 24칸 픽셀 고양이. 달 위에 앉아 있고, 상태에 따라 자세와 목걸이 색이 바뀐다.
/// 좌표는 Moon Studio 시안의 스프라이트를 그대로 옮긴 것이라 셀 하나가 곧 시안의 픽셀 하나다.
/// </summary>
public static class Mascot
{
    /// <summary>스프라이트 격자 크기 — 배치할 상자 크기를 계산할 때 쓴다.</summary>
    public const int Columns = 22;
    public const int Rows = 24;

    private enum Pose { Sit, Alert, Sleep }

    /// <param name="collar">목걸이 색을 직접 줄 때 — 도움말이 단계별 강조색을 입히는 데 쓴다.</param>
    public static void Draw(Canvas canvas, MascotMood mood, int cell, Brush? collar = null)
    {
        canvas.Children.Clear();

        var fur    = Res("CatFurBrush");
        var ink    = Res("InkBrush");
        var hi     = Res("CreamBrush");
        var pink   = Res("PinkSoftBrush");
        var blush  = Res("MuteBrush");
        var rim    = Res("CatRimBrush");
        var led    = Res("PinkBrush");
        collar ??= mood switch
        {
            MascotMood.Recording => Res("PinkBrush"),
            MascotMood.Paused    => Res("MuteBrush"),
            _                    => Res("AmberBrush"),
        };
        var pose = mood switch
        {
            MascotMood.Recording => Pose.Alert,
            MascotMood.Countdown => Pose.Alert,
            MascotMood.Paused    => Pose.Sleep,
            _                    => Pose.Sit,
        };

        // 나중에 찍은 셀이 위에 온다 — 시안의 그리기 순서를 그대로 따른다.
        void Put(int x, int y, Brush c)
        {
            var r = new WpfRectangle
            {
                Width = cell,
                Height = cell,
                Fill = c,
                SnapsToDevicePixels = true,
            };
            Canvas.SetLeft(r, x * cell);
            Canvas.SetTop(r, y * cell);
            canvas.Children.Add(r);
        }

        // 좌우 대칭으로 같은 셀을 두 번 찍는다.
        void Mirror(int x, int y, Brush c)
        {
            Put(x, y, c);
            Put(Columns - 1 - x, y, c);
        }

        // ---- 머리 ----
        var headSpans = new (int Y, int A, int B)[]
        {
            (5, 4, 17), (6, 3, 18), (7, 2, 19), (8, 2, 19), (9, 2, 19), (10, 2, 19),
            (11, 2, 19), (12, 2, 19), (13, 2, 19), (14, 3, 18), (15, 4, 17),
        };
        foreach (var (y, a, b) in headSpans)
        {
            for (int x = a; x <= b; x++) Put(x, y, fur);
            Put(a, y, ink);
            Put(b, y, ink);
        }
        for (int x = 4; x <= 17; x++) { Put(x, 4, ink); Put(x, 16, ink); }
        for (int x = 5; x <= 16; x++) Put(x, 5, hi);
        Mirror(3, 6, hi);

        // ---- 귀 ----
        var ears = new (int Y, int A, int B)[] { (1, 5, 6), (2, 4, 7), (3, 3, 8) };
        foreach (var (y, a, b) in ears)
        {
            for (int x = a; x <= b; x++) { Put(x, y, fur); Put(Columns - 1 - x, y, fur); }
            Mirror(a - 1, y, ink);
            Put(b + 1, y, ink);
            Put(Columns - 2 - b, y, ink);
        }
        for (int x = 5; x <= 6; x++) Mirror(x, 0, ink);
        Mirror(5, 3, pink); Mirror(6, 3, pink); Mirror(5, 2, pink);

        // ---- 눈 ----
        if (pose == Pose.Sleep)
        {
            for (int x = 5; x <= 8; x++) { Put(x, 10, ink); Put(Columns - 1 - x, 10, ink); }
            Put(5, 11, ink); Put(8, 9, ink);
            Put(Columns - 6, 11, ink); Put(Columns - 9, 9, ink);
        }
        else
        {
            int y0 = pose == Pose.Alert ? 8 : 9;
            foreach (var (a, b) in new[] { (6, 7), (14, 15) })
                for (int y = y0; y <= 11; y++)
                    for (int x = a; x <= b; x++)
                        Put(x, y, ink);
            Put(6, y0, hi); Put(14, y0, hi);
            if (pose == Pose.Alert) { Put(7, 11, hi); Put(15, 11, hi); }
        }

        // ---- 코 · 입 · 볼 · 수염 ----
        Mirror(10, 12, pink);
        if (pose == Pose.Alert)
        {
            Put(9, 13, ink); Put(12, 13, ink); Put(10, 13, ink); Put(11, 13, ink);
            Put(10, 14, pink); Put(11, 14, pink);
        }
        else
        {
            Put(9, 13, ink); Put(12, 13, ink); Put(10, 14, ink); Put(11, 14, ink);
        }
        foreach (var (x, y) in new[] { (4, 12), (5, 12), (4, 13) }) Mirror(x, y, blush);
        foreach (var (x, y) in new[] { (0, 11), (1, 11), (0, 13), (1, 13) }) Mirror(x, y, ink);

        // ---- 몸통 · 목걸이 ----
        var bodySpans = new (int Y, int A, int B)[]
        {
            (17, 6, 15), (18, 5, 16), (19, 5, 16), (20, 5, 16), (21, 5, 16), (22, 6, 15),
        };
        foreach (var (y, a, b) in bodySpans)
        {
            for (int x = a; x <= b; x++) Put(x, y, fur);
            Put(a, y, ink);
            Put(b, y, ink);
            if (y >= 19 && y <= 21) Put(a + 1, y, rim);
        }
        for (int x = 6; x <= 15; x++) Put(x, 23, ink);
        for (int x = 6; x <= 15; x++) Put(x, 18, collar);
        Mirror(10, 18, led);
        foreach (var (x, y) in new[] { (6, 21), (7, 21), (6, 22), (7, 22) }) Mirror(x, y, hi);

        // ---- 꼬리 ----
        (int X, int Y)[] tailFur, tailInk;
        if (pose == Pose.Alert)
        {
            tailFur = new[] { (17, 21), (18, 20), (19, 19), (20, 18), (20, 16), (20, 14), (19, 13) };
            tailInk = new[] { (17, 22), (18, 21), (19, 20), (21, 18), (21, 16), (21, 14), (19, 12), (18, 13) };
        }
        else if (pose == Pose.Sleep)
        {
            tailFur = new[] { (17, 21), (18, 21), (19, 20), (20, 20), (21, 21), (20, 22) };
            tailInk = new[] { (17, 22), (18, 22), (19, 21), (22, 21), (21, 22), (19, 19), (20, 19) };
        }
        else
        {
            tailFur = new[] { (17, 21), (18, 20), (19, 19), (20, 18), (20, 17), (19, 16) };
            tailInk = new[] { (17, 22), (18, 21), (19, 20), (21, 18), (21, 17), (19, 15), (18, 15) };
        }
        foreach (var (x, y) in tailFur) Put(x, y, fur);
        foreach (var (x, y) in tailInk) Put(x, y, ink);
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
