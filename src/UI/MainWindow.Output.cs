using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Recorder.Core;
using Recorder.Core.Win;

namespace Recorder.UI;

/// <summary>
/// メイン画面のうち、出力デバイス（M11-2）の配線。🔊ボタン・出力ウィンドウ・入力の持ち主（InputHub）・出力エンジン（OutputEngine）。
/// 録音・待機・入力チェック・出力は、同じ入力を InputHub から借りて共有する（録音の開始・終了で入力を開き直さないので出力が途切れない）。
/// </summary>
public partial class MainWindow
{
    private InputHub _hub = null!;
    private OutputEngine _outputs = null!;
    private IOutputDeviceProvider _outProvider = null!;
    private OutputWindow? _outWindow;
    private readonly DispatcherTimer _outTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>コンストラクターの早い段階（_settings・_provider が決まった直後）で呼ぶ。</summary>
    private void InitOutput()
    {
        _hub = new InputHub(_provider, () => _settings.AutoResume)
        {
            LabelOf = key => _devices.FirstOrDefault(d => d.Key == key)?.Label ?? key,
        };
        _outProvider = new WasapiOutputProvider();
        _outputs = new OutputEngine(_hub, _outProvider, _provider)
        {
            VolumeDb = _settings.OutputGainDb,
            DeviceId = _settings.OutputDevice,
        };
        _outTimer.Tick += (_, _) => UpdateOutputButton();
        _outTimer.Start();
    }

    /// <summary>メイン画面で選ばれている入力を出力エンジンへ渡す（入力・出力先が変わると、エンジンが開き直す）。</summary>
    private void SyncOutputInputs() => _outputs.SetInputs(Devices.Inputs);

    /// <summary>入力のゲインを変えた（index は選ばれている入力の順）。録音・待機・出力のどれが開いていても効く。</summary>
    private void OnInputGain(int index, double db)
    {
        var inputs = Devices.Inputs;
        if (index >= 0 && index < inputs.Count) _hub.SetGain(inputs[index].Key, db);
    }

    private void Output_Click(object sender, RoutedEventArgs e)
    {
        if (_outWindow != null)
        {
            if (_outWindow.WindowState == WindowState.Minimized) _outWindow.WindowState = WindowState.Normal;
            _outWindow.Activate();
            return;
        }
        var w = new OutputWindow(_settings, _outputs, _outProvider) { Owner = this };
        w.Closed += (_, _) => _outWindow = null;
        _outWindow = w;
        w.Show();
    }

    /// <summary>0.5秒ごと。出力中は🔊の色を変える（ウィンドウを閉じていても分かるように）。</summary>
    private void UpdateOutputButton()
    {
        if (_outputs.Running)
        {
            OutputButton.SetResourceReference(Control.ForegroundProperty, "Pastel");
            OutputButton.ToolTip = "🔊 出力中（クリックで出力ウィンドウを開く）";
        }
        else
        {
            OutputButton.ClearValue(Control.ForegroundProperty);
            var err = _outputs.Error;
            OutputButton.ToolTip = err.Length > 0 ? "出力: " + err : "出力（ミックスした音を再生デバイスへ出す）";
        }
    }

    /// <summary>終了時。出力を止め、開いている入力をすべて閉じる（デバイスを閉じるので別スレッドで）。</summary>
    private void ShutdownOutput()
    {
        _outTimer.Stop();
        try { _outWindow?.Close(); } catch (Exception) { }
        try { Task.Run(() => { _outputs.Shutdown(); _hub.Dispose(); }).Wait(3000); } catch (Exception) { }
    }
}
