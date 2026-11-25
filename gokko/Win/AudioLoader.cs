using Gokko.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Gokko.Win;

/// <summary>SEのファイル（wav・mp3・aac・flac など NAudio が読めるもの）を、48kHz・ステレオ・16bit に直して読み込む。長さは最大 <see cref="MaxSeconds"/> 秒まで。</summary>
public static class AudioLoader
{
    public const int MaxSeconds = 120;

    private static readonly Dictionary<string, (DateTime stamp, PadClip clip)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>読み込む（同じファイルは、更新されていなければキャッシュを返す）。失敗は null と理由。</summary>
    public static PadClip? Load(string path, out string error)
    {
        error = "";
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (Cache.TryGetValue(path, out var c) && c.stamp == stamp) return c.clip;
            using var reader = new AudioFileReader(path);
            ISampleProvider sp = reader;
            if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
            else if (sp.WaveFormat.Channels > 2)
            {
                var mux = new MultiplexingSampleProvider(new[] { sp }, 2);
                mux.ConnectInputToOutput(0, 0);
                mux.ConnectInputToOutput(1, 1);
                sp = mux;
            }
            if (sp.WaveFormat.SampleRate != PadClip.SampleRate) sp = new WdlResamplingSampleProvider(sp, PadClip.SampleRate);
            var max = PadClip.SampleRate * 2 * MaxSeconds;
            var pcm = new List<short>(PadClip.SampleRate * 2 * 4);
            var buf = new float[PadClip.SampleRate * 2];
            while (pcm.Count < max)
            {
                var n = sp.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                for (var i = 0; i < n && pcm.Count < max; i++)
                    pcm.Add((short)Math.Clamp((int)Math.Round(buf[i] * 32767f), short.MinValue, short.MaxValue));
            }
            if (pcm.Count % 2 == 1) pcm.RemoveAt(pcm.Count - 1);
            var clip = new PadClip(pcm.ToArray());
            Cache[path] = (stamp, clip);
            return clip;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static void ClearCache() => Cache.Clear();
}
