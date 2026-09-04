using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace CocoaRecorder;

public enum MascotMood { Idle, Countdown, Recording, Paused, Vibing }

/// <summary>
/// 코코아 — 22칸 × 24칸 픽셀 고양이. 달 위에 앉아 있고, 상태에 따라 자세와 목걸이 색이 바뀐다.
/// 좌표는 Moon Studio 시안의 스프라이트를 그대로 옮긴 것이라 셀 하나가 곧 시안의 픽셀 하나다.
/// </summary>
public static class Mascot
{
    /// <summary>스프라이트 격자 크기 — 배치할 상자 크기를 계산할 때 쓴다.</summary>
    public const int Columns = 22;
    public const int Rows = 24;

    private enum Pose { Sit, Alert, Sleep, Vibe }

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
            MascotMood.Vibing    => Res("PinkBrush"),
            MascotMood.Paused    => Res("MuteBrush"),
            _                    => Res("AmberBrush"),
        };
        var pose = mood switch
        {
            MascotMood.Recording => Pose.Alert,
            MascotMood.Countdown => Pose.Alert,
            MascotMood.Paused    => Pose.Sleep,
            MascotMood.Vibing    => Pose.Vibe,
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

        // ---- 눈 ---- (Vibe 는 음악에 폭 빠져 눈을 감고 있다)
        if (pose is Pose.Sleep or Pose.Vibe)
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
        if (pose is Pose.Alert or Pose.Vibe)
        {
            Put(9, 13, ink); Put(12, 13, ink); Put(10, 13, ink); Put(11, 13, ink);
            Put(10, 14, pink); Put(11, 14, pink);
        }
        else
        {
            Put(9, 13, ink); Put(12, 13, ink); Put(10, 14, ink); Put(11, 14, ink);
        }
        foreach (var (x, y) in new[] { (4, 12), (5, 12), (4, 13) }) Mirror(x, y, blush);
        // 수염 — Vibe 는 헤드폰 귀마개가 이 자리를 덮으므로 그리지 않는다 (남는 픽셀이 얼룩처럼 보인다).
        if (pose != Pose.Vibe)
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
        else if (pose == Pose.Vibe)
        {
            // 신나서 꼿꼿이 선 꼬리 — Alert 와 같다.
            tailFur = new[] { (17, 21), (18, 20), (19, 19), (20, 18), (20, 16), (20, 14), (19, 13) };
            tailInk = new[] { (17, 22), (18, 21), (19, 20), (21, 18), (21, 16), (21, 14), (19, 12), (18, 13) };
        }
        else
        {
            tailFur = new[] { (17, 21), (18, 20), (19, 19), (20, 18), (20, 17), (19, 16) };
            tailInk = new[] { (17, 22), (18, 21), (19, 20), (21, 18), (21, 17), (19, 15), (18, 15) };
        }
        foreach (var (x, y) in tailFur) Put(x, y, fur);
        foreach (var (x, y) in tailInk) Put(x, y, ink);

        // ---- 헤드폰 ---- (Vibe 전용, 맨 나중에 찍어서 귀와 수염 위에 얹는다)
        if (pose == Pose.Vibe)
        {
            var shell = Res("MuteBrush");
            var pad   = Res("PinkBrush");

            // 귀마개 — 볼 옆을 감싸는 3×5 쉘에 분홍 패드
            foreach (var y in new[] { 8, 9, 10, 11, 12 })
                for (int x = 0; x <= 2; x++) Mirror(x, y, shell);
            foreach (var y in new[] { 9, 10, 11 })
                for (int x = 1; x <= 2; x++) Mirror(x, y, pad);

            // 밴드 — 귀 바깥을 타고 올라가 귀 사이를 가로지른다
            foreach (var (x, y) in new[] { (2, 7), (2, 6), (2, 5), (3, 4), (3, 3), (4, 2), (5, 1), (6, 1) })
                Mirror(x, y, shell);
            for (int x = 7; x <= 14; x++) Put(x, 0, shell);
        }
    }

    /// <summary>
    /// 8비트 음표 하나 — 4칸 × 4칸. 미니 모드에서 음악감상 중인 코코아 주변에 뿅뿅 떠오른다.
    /// </summary>
    public static void DrawNote(Canvas canvas, int cell, Brush color)
    {
        canvas.Children.Clear();
        foreach (var (x, y) in new[] { (2, 0), (3, 0), (2, 1), (2, 2), (1, 3), (2, 3) })
        {
            var r = new WpfRectangle
            {
                Width = cell,
                Height = cell,
                Fill = color,
                SnapsToDevicePixels = true,
            };
            Canvas.SetLeft(r, x * cell);
            Canvas.SetTop(r, y * cell);
            canvas.Children.Add(r);
        }
    }

    /// <summary>스위치 얼굴 스프라이트의 격자 크기.</summary>
    public const int FaceColumns = 16;
    public const int FaceRows = 9;

    /// <summary>
    /// 냥 소리 스위치에 쓰는 작은 얼굴 — 16칸 × 9칸. 깨어 있으면 앰버 얼굴에 음표, 잠들면 회색 얼굴에 z.
    /// </summary>
    /// <param name="mark">음표/z 표시까지 그릴지 — 타이틀바의 미니 모드 버튼은 얼굴만 쓴다.</param>
    public static void DrawFace(Canvas canvas, int cell, bool awake, bool mark = true)
    {
        canvas.Children.Clear();

        // 얼굴과 오른쪽 위 표시는 같은 색 — 깨어 있으면 앰버, 잠들면 회색
        var fur = awake ? Res("AmberBrush") : Res("MuteBrush");
        var ink = Res("InkBrush");

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
        void Row(int y, int from, int to, Brush c)
        {
            for (int x = from; x <= to; x++) Put(x, y, c);
        }

        // 귀 → 머리 → 턱 순서로 11칸 너비의 얼굴
        Put(0, 0, fur); Put(10, 0, fur);
        Row(1, 0, 1, fur); Row(1, 9, 10, fur);
        for (int y = 2; y <= 6; y++) Row(y, 0, 10, fur);
        Row(7, 1, 9, fur);
        Row(8, 2, 8, fur);

        // 눈 — 깨어 있으면 점, 잠들면 감은 선
        if (awake)
        {
            Put(2, 4, ink); Put(8, 4, ink);
        }
        else
        {
            Row(4, 1, 2, ink); Row(4, 8, 9, ink);
        }

        // 코와 입
        Put(5, 5, ink);
        Put(4, 6, ink); Put(6, 6, ink);

        if (!mark) return;

        // 오른쪽 위 — 음표 또는 z (12~15열, 얼굴과 한 칸 띄운다)
        if (awake)
        {
            Put(14, 0, fur); Put(15, 0, fur);
            Put(14, 1, fur);
            Put(14, 2, fur);
            Put(13, 3, fur); Put(14, 3, fur);
        }
        else
        {
            Row(0, 12, 15, fur);
            Put(14, 1, fur);
            Put(13, 2, fur);
            Row(3, 12, 15, fur);
        }
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
