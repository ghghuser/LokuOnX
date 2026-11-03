using System.Runtime.InteropServices;

namespace Recorder.Core.Win;

/// <summary>録音中にPCをスリープさせない（Windows専用）。同じスレッド（UIスレッド）から ON/OFF すること。</summary>
public static class SleepGuard
{
    private const uint Continuous = 0x80000000;
    private const uint SystemRequired = 0x00000001;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    public static void Prevent(bool on)
    {
        try { SetThreadExecutionState(on ? Continuous | SystemRequired : Continuous); }
        catch (Exception) { }
    }
}
