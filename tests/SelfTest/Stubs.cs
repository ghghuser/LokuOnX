namespace Recorder.Core;

// テストでは Settings.cs（ini・プリセットなど大きな依存がある）を取り込まない。
// Core が参照する定数と型だけを、本物（src/Core/Settings.Devices.cs）と同じ値・同じ形で用意する。本物を変えたらここも合わせること。
public sealed partial class Settings
{
    public const double MinGainDb = -30;
    public const double MaxGainDb = 20;
    public const int MaxInputs = 4;
}

public sealed record InputSetting(string Key, double GainDb, bool Enabled = true);
