using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace CocoaRecorder;

/// <summary>
/// 히어로 영역의 밤하늘 — 픽셀 달과 별먼지.
/// 좌표와 난수열은 Moon Studio 시안의 moonCells / skyStars를 그대로 옮긴 것이다.
/// 시안이 JavaScript의 double 연산으로 별을 뿌리므로 여기서도 double로 같은 식을 돌린다.
/// </summary>
public static class NightSky
{
    public const int MoonRadius = 15;
    /// <summary>달 스프라이트의 한 변 (셀 수).</summary>
    public const int MoonSpan = MoonRadius * 2 + 1;

    // 초승달을 만드는 "빼내는 원"과 크레이터. 모두 격자 중심 기준 좌표다.
    private const int CutX = 9;
    private const int CutY = 2;
    private static readonly (double X, double Y, double R)[] Craters =
    {
        (-6, -7, 2.6), (-2, 6, 3.2), (-9, 2, 1.8), (1, -2, 1.6), (-4, 0, 1.2),
    };

    public static void DrawMoon(Canvas canvas, int cell)
    {
        canvas.Children.Clear();

        var rim  = Res("MoonRimBrush");
        var lit  = Res("MoonLitBrush");
        var mid  = Res("MoonMidBrush");
        var deep = Res("MoonDeepBrush");
        var term = Res("MoonTermBrush");

        const int R = MoonRadius;
        var filled = new HashSet<(int X, int Y)>();
        var cells = new List<(int X, int Y, Brush C)>();

        for (int y = -R; y <= R; y++)
        for (int x = -R; x <= R; x++)
        {
            double r = Math.Sqrt(x * x + y * y);
            if (r > R) continue;

            // 오른쪽 위에서 겹쳐 오는 원을 빼내 초승달을 만든다.
            double cut = Math.Sqrt((x - CutX) * (x - CutX) + (double)(y - CutY) * (y - CutY));
            if (cut <= R - 1) continue;

            filled.Add((x, y));

            double inward = R - r;
            double edge = cut - (R - 1);
            var c = inward < 1.4 ? rim : inward < 7 ? lit : inward < 12 ? mid : deep;
            if (edge < 1.3) c = term;
            else if (edge < 2.8) c = mid;

            foreach (var (cx, cy, cr) in Craters)
            {
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d <= cr) c = d > cr - 1 && y > cy ? rim : mid;
                else if (d <= cr + 0.9 && y < cy) c = deep;
            }

            cells.Add((x + R, y + R, c));
        }

        // 바깥 테두리 한 겹을 밝은 림으로 덧칠해 달의 윤곽을 살린다.
        var edges = new List<(int X, int Y, Brush C)>();
        foreach (var (x, y, _) in cells)
        {
            int gx = x - R, gy = y - R;
            bool open = !filled.Contains((gx + 1, gy)) || !filled.Contains((gx, gy + 1))
                     || !filled.Contains((gx - 1, gy)) || !filled.Contains((gx, gy - 1));
            if (open && Math.Sqrt(gx * gx + gy * gy) > R - 2) edges.Add((x, y, rim));
        }

        foreach (var (x, y, c) in cells.Concat(edges))
            Add(canvas, x * cell, y * cell, cell, cell, c);
    }

    private static readonly string[] DustTints =
    {
        "#FFFBEA", "#FFFBEA", "#FFD86B", "#C9C2F0", "#FF8FB8", "#7BE3C4",
    };

    /// <summary>
    /// 별먼지 130개와 십자 별 11개를 뿌린다. 같은 seed면 언제나 같은 하늘이 나온다.
    /// xScale / yScale 은 시안이 도움말의 세로로 긴 하늘을 만들 때 쓴 것과 같은 좌표 변환이다.
    /// </summary>
    public static void DrawStars(Canvas canvas, int seed, double xScale = 1, double yScale = 1)
    {
        canvas.Children.Clear();

        double s = seed;
        double Rnd()
        {
            s = (s * 1103515245.0 + 12345.0) % 2147483648.0;
            return s / 2147483648.0;
        }
        static double Round(double v) => Math.Floor(v + 0.5);

        for (int i = 0; i < 130; i++)
        {
            double size = Rnd() < 0.78 ? 2 : Rnd() < 0.7 ? 3 : 4;
            double left = Round(Round(Rnd() * 1206) * xScale);
            double top = Round(Round(Rnd() * 330) * yScale);
            var fill = Freeze(DustTints[(int)Math.Floor(Rnd() * DustTints.Length)]);
            double delay = Rnd() * 4.2;
            double period = 1.8 + Rnd() * 2.6;
            double rest = 0.35 + Rnd() * 0.5;

            var dot = Add(canvas, left, top, size, size, fill);
            dot.Opacity = rest;
            Twinkle(dot, 0.18, 1.0, period, delay, frameRate: 30);
        }

        for (int i = 0; i < 11; i++)
        {
            double arm = Rnd() < 0.5 ? 3 : 4;
            double left = Round(Round(40 + Rnd() * 1130) * xScale);
            double top = Round(Round(14 + Rnd() * 300) * yScale);
            double span = arm * 2 + 2;
            var fill = Freeze(i % 3 == 0 ? "#FFD86B" : i % 3 == 1 ? "#FFFBEA" : "#FF8FB8");
            double delay = Rnd() * 5;
            double period = 2.6 + Rnd() * 2.4;

            var spark = new Canvas
            {
                Width = span,
                Height = span,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Add(spark, 0, arm, span, 2, fill);
            Add(spark, arm, 0, 2, span, fill);
            Canvas.SetLeft(spark, left);
            Canvas.SetTop(spark, top);
            canvas.Children.Add(spark);

            var scale = new ScaleTransform(0.6, 0.6);
            spark.RenderTransform = scale;
            Twinkle(spark, 0.25, 1.0, period, delay, frameRate: 30);
            Pulse(scale, ScaleTransform.ScaleXProperty, 0.6, 1.0, period, delay);
            Pulse(scale, ScaleTransform.ScaleYProperty, 0.6, 1.0, period, delay);
        }
    }

    // 달에 딱 붙어 반짝이는 큰 별 셋. 위치는 186×186 달 상자 기준이다.
    private static readonly (double L, double T, double Size, string C, double Period, double Delay)[] MoonSparks =
    {
        (-26,  44, 10, "#FFF8DC", 3.1, 0.4),
        (176, -18,  8, "#FFD86B", 2.4, 1.1),
        ( 22, 192,  8, "#FF8FB8", 3.6, 1.9),
    };

    /// <summary>달 둘레에서 크게 깜빡이는 십자별 셋.</summary>
    public static void DrawMoonSparks(Canvas canvas)
    {
        canvas.Children.Clear();
        foreach (var (l, t, size, hex, period, delay) in MoonSparks)
        {
            double arm = (size - 2) / 2;
            var fill = Freeze(hex);
            var spark = new Canvas
            {
                Width = size,
                Height = size,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Add(spark, 0, arm, size, 2, fill);
            Add(spark, arm, 0, 2, size, fill);
            Canvas.SetLeft(spark, l);
            Canvas.SetTop(spark, t);
            canvas.Children.Add(spark);

            var scale = new ScaleTransform(0.6, 0.6);
            spark.RenderTransform = scale;
            Twinkle(spark, 0.25, 1.0, period, delay, frameRate: 30);
            Pulse(scale, ScaleTransform.ScaleXProperty, 0.6, 1.0, period, delay);
            Pulse(scale, ScaleTransform.ScaleYProperty, 0.6, 1.0, period, delay);
        }
    }

    /// <summary>달 뒤에서 천천히 숨 쉬는 후광 — 밝기와 크기가 같이 움직인다.</summary>
    public static void Breathe(UIElement glow, ScaleTransform scale, double seconds = 7)
    {
        var half = TimeSpan.FromSeconds(seconds / 2);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        var fade = new DoubleAnimation(0.55, 0.9, half)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = ease,
        };
        Timeline.SetDesiredFrameRate(fade, 30);
        glow.BeginAnimation(UIElement.OpacityProperty, fade);

        Pulse(scale, ScaleTransform.ScaleXProperty, 1.0, 1.06, seconds, 0);
        Pulse(scale, ScaleTransform.ScaleYProperty, 1.0, 1.06, seconds, 0);
    }

    /// <summary>코코아가 옆으로 꼬리를 살랑이는 움직임 — 시안의 tailSway.</summary>
    public static void Sway(TranslateTransform transform, double seconds)
    {
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        var anim = new DoubleAnimation(0, 3, TimeSpan.FromSeconds(seconds / 2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(anim, 30);
        transform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <summary>제자리에서 위아래로 살짝 떠다니는 움직임 — 시안의 drift.</summary>
    public static void Drift(TranslateTransform transform, double seconds)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        var anim = new DoubleAnimation(0, -3, TimeSpan.FromSeconds(seconds / 2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(anim, 30);
        transform.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    private static void Twinkle(UIElement target, double from, double to, double period, double delay, int frameRate)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(period / 2))
        {
            BeginTime = TimeSpan.FromSeconds(delay),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(anim, frameRate);
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private static void Pulse(Animatable target, DependencyProperty property,
                              double from, double to, double period, double delay)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(period / 2))
        {
            BeginTime = TimeSpan.FromSeconds(delay),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(anim, 30);
        target.BeginAnimation(property, anim);
    }

    private static WpfRectangle Add(Canvas canvas, double left, double top, double w, double h, Brush fill)
    {
        var r = new WpfRectangle
        {
            Width = w,
            Height = h,
            Fill = fill,
            SnapsToDevicePixels = true,
        };
        Canvas.SetLeft(r, left);
        Canvas.SetTop(r, top);
        canvas.Children.Add(r);
        return r;
    }

    private static readonly Dictionary<string, Brush> BrushCache = new();

    private static Brush Freeze(string hex)
    {
        if (BrushCache.TryGetValue(hex, out var cached)) return cached;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        BrushCache[hex] = brush;
        return brush;
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
