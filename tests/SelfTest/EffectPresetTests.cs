using Recorder.Core;

/// <summary>エフェクトのプリセット（M16）: 保存・読み込み・削除・既定の書き出しと復元。効果の中身だけを保存する。</summary>
static class EffectPresetTests
{
    public static void Run(CheckFn check, Action<string> section)
    {
        section("エフェクトのプリセット（M16）");
        string dir = Path.Combine(Path.GetTempPath(), "lokuonx-presets-" + Guid.NewGuid().ToString("N"), "EffectPresets");
        try
        {
            var store = new EffectPresetStore(dir, false);
            check(store.List().Count == 0, "フォルダが無いときは一覧が空");

            store.EnsureDefaults();
            var names = store.List().Select(p => p.Name).ToList();
            check(names.Count == 4 && names.Contains("ボーカル低減（カラオケ）") && names.Contains("声をはっきり") && names.Contains("ノイズ除去") && names.Contains("音量そろえ（リミッター）"),
                "初回は既定の4件が書き出される", string.Join("/", names));
            var kara = store.Get("ボーカル低減（カラオケ）");
            check(kara != null && kara.Sound.VocalOn && kara.Sound.IsActive, "既定のボーカル低減はボーカル低減がオン");
            check(store.Get("声をはっきり") is { Sound: { EqOn: true, HpfHz: 100, LimiterOn: true } }, "既定「声をはっきり」の中身");

            // フォルダが既にあれば、既定は書き出さない（削除したものを勝手に戻さない）
            store.Delete("ノイズ除去");
            store.EnsureDefaults();
            check(store.Get("ノイズ除去") == null, "削除した既定プリセットは、再起動相当（EnsureDefaults）では戻らない");
            store.RestoreDefaults();
            check(store.Get("ノイズ除去") != null, "「初期プリセットを復元」で戻る");

            // 保存: 効果の中身だけ（反映先・バイパスは入れない）
            var fx = new EffectSetting { ReverbOn = true, ReverbMix = 0.4, Bypass = true, ToRecording = true, ToOutput = false };
            check(store.Save("  自分用:1  ", fx), "名前を付けて保存できる");
            var got = store.Get("自分用＿1");
            check(got != null && got.Sound.ReverbOn && got.Sound.ReverbMix == 0.4, "禁止文字は置き換わり、中身が保存される");
            check(got != null && !got.Sound.Bypass && !got.Sound.ToRecording && got.Sound.ToOutput, "反映先とバイパスは保存しない");
            fx.EchoOn = true;
            store.Save("自分用＿1", fx);
            check(store.List().Count(p => p.Name == "自分用＿1") == 1 && store.Get("自分用＿1")!.Sound.EchoOn, "同じ名前は上書き");
            check(store.Get("自分用＿1")!.Sound.SameSound(new EffectSetting { ReverbOn = true, ReverbMix = 0.4, EchoOn = true }), "保存した中身が往復で同じ");

            // 適用: 効果の中身だけ置き換え、反映先は変えない
            var target = new EffectSetting { ToRecording = true, ToOutput = false, GateOn = true };
            target.CopySoundFrom(store.Get("ボーカル低減（カラオケ）")!.Sound);
            check(target.VocalOn && !target.GateOn && target.ToRecording && !target.ToOutput, "プリセットの適用は効果だけ置き換える");

            check(!store.Save("   ", fx) && !store.Save("", fx), "名前が空のときは保存しない");
            check(EffectPresetStore.CleanName(new string('あ', 80)).Length == EffectPresetStore.MaxNameLength, "名前は40文字まで");

            // 壊れたファイルは飛ばす
            File.WriteAllText(Path.Combine(dir, "壊れた.json"), "{ not json");
            check(store.List().All(p => p.Name != "壊れた"), "壊れたファイルは一覧に出さない（落ちない）");

            store.Delete("自分用＿1");
            check(store.Get("自分用＿1") == null, "削除できる");

            // 書き込まない側の起動
            var ro = new EffectPresetStore(dir + "-ro", true);
            ro.EnsureDefaults();
            check(!Directory.Exists(dir + "-ro") && !ro.Save("x", fx), "書き込まない側の起動では何も書かない");
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(dir)!, true); } catch { } }
    }
}
