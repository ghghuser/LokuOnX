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

    /// <summary>
    /// 通常（停止中）のトレイ用アイコン。アプリのアイコン（暗い角丸の四角＋青い波形）を、小さなトレイでも見えるように描き直す:
    /// 波形の棒を大きく（高さを約2倍）・彩度の高い青にし、背景の四角いっぱいに広げる。録音中の赤と紛らわしい赤い点は付けない。
    /// </summary>
    public static IntPtr CreateIdle(int size)
    {
        size = Math.Clamp(size, 16, 64);
        using var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            // 暗い角丸の四角（明るい・暗いどちらのタスクバーでも青い棒が見えるよう、真っ黒にはしない）。縁線は引かず、棒との間に余白を残す
            using (var tile = RoundRect(0f, 0f, size, size, size * 0.22f))
            {
                using var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 28, 34, 52));
                g.FillPath(bg, tile);
            }
            // 波形の5本の棒（短・中・長・中・短）。16pxで幅2px・すき間1pxになり、にじまない
            float bw = size * 0.125f, gap = size * 0.0625f;
            float x = (size - (5 * bw + 4 * gap)) / 2f;
            using var blue = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 46, 160, 255));
            foreach (var f in new[] { 0.30f, 0.50f, 0.72f, 0.50f, 0.30f })
            {
                float h = 2f * (float)Math.Round(size * f / 2f);   // 偶数にして上下の中央に置く（にじみ防止）
                using var bar = RoundRect(x, (size - h) / 2f, bw, h, bw / 2f);
                g.FillPath(blue, bar);
                x += bw + gap;
            }
            // アプリのアイコンと同じ、右上の赤い丸。背景色の縁を付けて波形の棒と分け、16pxでも潰れない大きさにする
            float dr = size * 0.20f, dx = size * 0.74f, dy = size * 0.26f, ring = Math.Max(1f, size * 0.06f);
            using (var edge = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 28, 34, 52)))
                g.FillEllipse(edge, dx - dr - ring, dy - dr - ring, (dr + ring) * 2, (dr + ring) * 2);
            using (var dot = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 232, 36, 40)))
                g.FillEllipse(dot, dx - dr, dy - dr, dr * 2, dr * 2);
        }
        return bmp.GetHicon();
    }

    /// <summary>
    /// 録音を一時停止しているときのトレイ用アイコン。黄色い丸の中に白い一時停止マーク（2本の縦棒。濃い茶色の細い縁取り付き）。
    /// 丸と棒の位置が描画のくせで半ピクセルずれないよう、8倍の大きさで描いてから縮小する。
    /// </summary>
    public static IntPtr CreatePaused(int size)
    {
        size = Math.Clamp(size, 16, 64);
        const int K = 8;
        // 棒の大きさ（アイコンの1ピクセル単位）。全体の幅・高さの偶奇を size にそろえ、中心に置く
        int bw = (int)Math.Round(size * 0.17f), gap = (int)Math.Round(size * 0.14f);
        if ((size - (2 * bw + gap)) % 2 != 0) gap++;
        int h = (int)Math.Round(size * 0.50f);
        if ((size - h) % 2 != 0) h++;
        int left = (size - (2 * bw + gap)) / 2, top = (size - h) / 2;
        int big = size * K;
        using var hi = new System.Drawing.Bitmap(big, big, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(hi))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var amber = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 255, 160, 18));   // 一時停止の黄色（わずかにオレンジ寄り）
            g.FillEllipse(amber, K / 2f, K / 2f, big - K, big - K);
            using var edge = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 110, 60, 0));
            using var white = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            foreach (var x in new[] { left, left + bw + gap })
            {
                g.FillRectangle(edge, (x - 1) * K, (top - 1) * K, (bw + 2) * K, (h + 2) * K);
                g.FillRectangle(white, x * K, top * K, bw * K, h * K);
            }
        }
        using var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            using var attrs = new System.Drawing.Imaging.ImageAttributes();
            attrs.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
            g.DrawImage(hi, new System.Drawing.Rectangle(0, 0, size, size), 0, 0, big, big, System.Drawing.GraphicsUnit.Pixel, attrs);
        }
        return bmp.GetHicon();
    }

    /// <summary>角丸の四角（x, y, 幅, 高さ, 角の半径）。</summary>
    private static System.Drawing.Drawing2D.GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2f);
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        if (r <= 0.01f)
        {
            path.AddRectangle(new System.Drawing.RectangleF(x, y, w, h));
            return path;
        }
        float d = r * 2f;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
