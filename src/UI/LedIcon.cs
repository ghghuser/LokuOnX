namespace Recorder.UI;

/// <summary>録音中のトレイ用アイコン。フレネルレンズ風（同心円の溝）の大きい赤丸を、その場で描いて作る（PROJECT.md §3-8）。</summary>
public static class LedIcon
{
    /// <summary>アイコンのハンドルを返す（使い終わったら DestroyIcon）。size はトレイの小アイコンの大きさ（通常16〜32）。</summary>
    public static IntPtr Create(int size)
    {
        size = Math.Clamp(size, 16, 64);
        using var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            // 赤い丸（フレネルレンズ風：同心円の細い溝と、中心の明るい丸）を、アイコンいっぱいに大きく
            float c = size / 2f, r = size / 2f - 0.5f;
            using var red = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 232, 36, 40));
            g.FillEllipse(red, c - r, c - r, r * 2, r * 2);
            float lw = Math.Max(1f, size / 14f);
            using var groove = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 150, 12, 20), lw);
            foreach (var k in new[] { 0.74f, 0.48f })
            {
                float rr = r * k;
                g.DrawEllipse(groove, c - rr, c - rr, rr * 2, rr * 2);
            }
            float rc = r * 0.24f;
            using var core = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 255, 120, 110));
            g.FillEllipse(core, c - rc, c - rc, rc * 2, rc * 2);
        }
        return bmp.GetHicon();
    }

    /// <summary>
    /// 待機中（「音が出たら自動で録音開始」で音を待っているとき）のトレイ用アイコン。
    /// 黄色（琥珀色）の太いリングと中心の点。録音中の赤い丸とも、通常のアプリのアイコンとも見分けがつく。
    /// </summary>
    public static IntPtr CreateStandby(int size)
    {
        size = Math.Clamp(size, 16, 64);
        using var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            var amber = System.Drawing.Color.FromArgb(255, 255, 184, 28);
            float c = size / 2f;
            float lw = Math.Max(2.5f, size / 5f);          // リングの太さ（小さいアイコンでも太く）
            float r = size / 2f - lw / 2f - 0.5f;          // リングの中心線の半径
            using var ring = new System.Drawing.Pen(amber, lw);
            g.DrawEllipse(ring, c - r, c - r, r * 2, r * 2);
            float rc = size * 0.16f;                       // 中心の点
            using var core = new System.Drawing.SolidBrush(amber);
            g.FillEllipse(core, c - rc, c - rc, rc * 2, rc * 2);
        }
        return bmp.GetHicon();
    }
}
