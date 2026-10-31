using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Recorder.UI;

/// <summary>
/// 1行に収まらない文字を左右にゆっくり流す（サマリー行用）。収まるときはアニメーションを止めて左端に置く。
/// host は横並びの StackPanel など、中身の幅を制限しない要素。
/// </summary>
public static class Marquee
{
    public static void Apply(FrameworkElement host, double viewportWidth)
    {
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var overflow = host.DesiredSize.Width - viewportWidth;

        if (host.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform();
            host.RenderTransform = tt;
        }

        tt.BeginAnimation(TranslateTransform.XProperty, null);
        tt.X = 0;
        if (overflow <= 1 || viewportWidth <= 0) return;

        var seconds = Math.Max(2.0, overflow / 40.0);
        var anim = new DoubleAnimation(0, -overflow, TimeSpan.FromSeconds(seconds))
        {
            BeginTime = TimeSpan.FromSeconds(1),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        tt.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
