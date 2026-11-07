namespace Recorder.UI;

/// <summary>録音中のトレイ用アイコン。平らな質感（グラデーションや光沢なし）の大きい赤丸を、その場で描いて作る（PROJECT.md §3-8）。</summary>
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
            // 単色の赤い丸を、アイコンいっぱいに大きく
            using var red = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 232, 36, 40));
            g.FillEllipse(red, new System.Drawing.RectangleF(0.5f, 0.5f, size - 1f, size - 1f));
        }
        return bmp.GetHicon();
    }
}
