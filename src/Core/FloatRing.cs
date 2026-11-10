namespace Recorder.Core;

/// <summary>float の並びを先入れ先出しで溜める簡単なバッファ（チャンクの列）。スレッドセーフではない（呼び出し側でロックする）。</summary>
public sealed class FloatRing
{
    private readonly Queue<float[]> _chunks = new();
    private int _headOffset;

    /// <summary>溜まっている float の数。</summary>
    public long Count { get; private set; }

    /// <summary>data の先頭 count 個を末尾に足す。</summary>
    public void Append(float[] data, int count)
    {
        if (count <= 0) return;
        var chunk = count == data.Length ? data : data.AsSpan(0, count).ToArray();
        _chunks.Enqueue(chunk);
        Count += count;
    }

    /// <summary>溜まっているものをすべて捨てる。</summary>
    public void Clear()
    {
        _chunks.Clear();
        _headOffset = 0;
        Count = 0;
    }

    /// <summary>無音（0）を count 個足す。</summary>
    public void AppendZeros(long count)
    {
        while (count > 0)
        {
            int n = (int)Math.Min(count, 1 << 18);
            _chunks.Enqueue(new float[n]);
            Count += n;
            count -= n;
        }
    }

    /// <summary>先頭から最大 count 個を dest の先頭へ取り出す。取り出せた数を返す。</summary>
    public int Take(float[] dest, int count)
    {
        int copied = 0;
        while (copied < count && _chunks.Count > 0)
        {
            var head = _chunks.Peek();
            int n = Math.Min(head.Length - _headOffset, count - copied);
            Array.Copy(head, _headOffset, dest, copied, n);
            copied += n;
            _headOffset += n;
            if (_headOffset >= head.Length)
            {
                _chunks.Dequeue();
                _headOffset = 0;
            }
        }
        Count -= copied;
        return copied;
    }
}
