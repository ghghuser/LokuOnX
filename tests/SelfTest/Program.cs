using Recorder.Core;

// 簡易セルフテスト（外部のテストライブラリ不要）。 dotnet run --project tests/SelfTest
// 失敗が1件でもあれば終了コード1（Actions のテスト工程が落ちる）。
int fails = 0, passes = 0;
void Check(bool ok, string name, string detail = "")
{
    if (ok) { passes++; Console.WriteLine("  ok   " + name); }
    else { fails++; Console.WriteLine("  FAIL " + name + (detail.Length > 0 ? "  -> " + detail : "")); }
}
void Section(string s) => Console.WriteLine("\n# " + s);

RoutingTests.Run(new CheckFn(Check), Section);
EffectTests.Run(new CheckFn(Check), Section);
OutputFxTests.Run(new CheckFn(Check), Section);
GateTests.Run(new CheckFn(Check), Section);
BufferTests.Run(new CheckFn(Check), Section);
OutputTests.Run(new CheckFn(Check), Section);
HubTests.Run(new CheckFn(Check), Section);
EngineTests.Run(new CheckFn(Check), Section);
OutputHubTests.Run(new CheckFn(Check), Section);
OutputFxWiringTests.Run(new CheckFn(Check), Section);
IniTests.Run(new CheckFn(Check), Section);
FxTests.Run(new CheckFn(Check), Section);

Console.WriteLine($"\n結果: {passes} 件成功 / {fails} 件失敗");
return fails == 0 ? 0 : 1;

delegate void CheckFn(bool ok, string name, string detail = "");
