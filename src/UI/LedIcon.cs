namespace Recorder.UI;

/// <summary>録音中のトレイ用アイコン。LED風の大きい赤丸を、その場で描いて作る（PROJECT.md §3-8）。</summary>
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

            // 外周（暗い赤の縁）。アイコンいっぱいに大きく
            var outer = new System.Drawing.RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
            using (var rim = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 120, 10, 10)))
                g.FillEllipse(rim, outer);

            // 本体（中心が明るい赤のグラデーション）
            var inner = System.Drawing.RectangleF.Inflate(outer, -size * 0.09f, -size * 0.09f);
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddEllipse(inner);
                using var body = new System.Drawing.Drawing2D.PathGradientBrush(path)
                {
                    CenterPoint = new System.Drawing.PointF(inner.X + inner.Width * 0.42f, inner.Y + inner.Height * 0.36f),
                    CenterColor = System.Drawing.Color.FromArgb(255, 255, 120, 110),
                    SurroundColors = new[] { System.Drawing.Color.FromArgb(255, 225, 15, 15) },
                };
                g.FillPath(body, path);
            }

            // ハイライト（LEDの光沢）
            var shine = new System.Drawing.RectangleF(size * 0.27f, size * 0.15f, size * 0.32f, size * 0.20f);
            using (var hb = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(150, 255, 255, 255)))
                g.FillEllipse(hb, shine);
        }
        return bmp.GetHicon();
    }
}
