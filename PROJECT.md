# PROJECT.md — LokuOnX（Windows録音アプリ）

> 仕様の正はこのファイル。チャット内の過去のやり取りより優先する。
> このファイルは Claude の Project「録音アプリ制作」のドキュメントに置く（新しいチャットから自動で参照できる）。
> **仕様が変わったら、Claudeがその場でこのファイルを更新して Project に書き戻す。** 現在のマイルストーン外の新要望は §9 に積むだけにする。

## 1. 目的・前提
- Windows 11 で動く録音アプリ。オンライン利用なし。**バグが少なく安定**が最優先。用途は自分用。
- 将来 Linux へ移植する可能性が「なくはない」（備考。今は実装しない）。→ §6 の「移植しやすさの規則」だけ守る。
- 開発者（ユーザー）はプログラミング未経験。コードは**Claudeが全部書く**。ユーザーは GitHub の Web 画面とビルド済み exe の実行だけ行う。
- ビルドは GitHub Actions（windows-latest）。配布は Releases の ZIP（**ポータブル版・インストール不要**）。
- リポジトリは非公開。ただし**Claudeが直接読み書きできるよう、公開にするか、GitHub連携でリポジトリへのアクセスを許可してよい**（ユーザー了承済み）。
- 実機テストはユーザーのみ。Claudeは実行できない（WPFはLinuxで動かせない）。

## 2. 技術スタック（変更不可）
- C# / .NET 8 / **WPF** / 自己完結型。**単一ファイル発行**（§6-2）。
- 録音: **NAudio**（WASAPI入力、WASAPIループバックでPC内部音）。
- エンコード: **FFmpeg**（標準入力にPCMを流し、mp3/aac/flacへ直接書き出し）。リポジトリには置かず、**Actionsのビルド時にダウンロードして同梱**（BtbN n8.1 LGPL。URLは env `FFMPEG_URL`。404ならmaster版へ）。
- 設定: exeと同じフォルダの `settings.ini`（UTF-8）。プリセットも同じiniに保存。プリセットの入出力はJSON。
- UIはコードビハインド中心（MVVMフレームワークなし）。

## 3. 確定仕様

### 3-1 録音
- 入力デバイスをプルダウンで選択。**マイク（入力）と、スピーカー等の出力デバイス（ループバック＝PC内部音）の両方を同じ一覧に表示**（出力側は先頭のアイコンで区別し、「（PC音）」は付けない: 🎤名前 / 🔊名前）。**並びはマイク→スピーカーの順で、それぞれ名前順。Windowsの既定のデバイス（入力・出力それぞれ）は名前の末尾に「＊」（全角アスタリスク）を付けて示す（表示だけで、ファイル名の `{Device}` には入れない）。初期値はWindowsの既定の入力デバイス**（前回選んだデバイスがあればそれ）。※初版でスピーカーが出なかった不具合の再発防止。
- **複数デバイス同時録音**: 初期値は「ミックスして1ファイルで出力」。切替で「デバイスごとに別ファイルで出力」（M4確認後に文言変更。無効時は透明度0.35でかなり薄いグレー）。（M4で確定: 入力デバイス欄は1行＝1デバイス（有効チェック・プルダウン・ゲイン−30〜+20dB（左端＝ミュート）・音量メーター・✕）、「＋デバイスを追加」で最大4台（追加した行の初期値は「（未選択）」。プルダウンの先頭に「（未選択）」がある。チェックを外すと行は残したまま録音に使わない。ミックス／別ファイルの切替は常に表示し、有効なデバイスが2台未満のとき（と録音中）はグレーアウト（表示が出たり消えたりしない）。「既定のデバイス（変更に追従）」と、その実際のデバイスの同時選択は音が二重になるので開始時にエラー。ファイル名の `{Device}` には擬似デバイス名でなく開始時点の実際のデバイス名を使う）。録音中はデバイスの選択・追加・削除・モード切替は不可、ゲインだけ変えられる。ミックスは最大ステレオ・共通の周波数は選んだデバイスの最大値、合計がクリップしないよう±1で制限。別ファイルでは `{Device}` をパターンへ自動付加し、分割・一時停止・停止は全ファイル同時。選んだデバイスは settings.ini の `Device`/`Gain`/`Enabled`（2台目以降は `Device2`/`Gain2`/`Enabled2`…。「未選択」は `Device=-`）、`SeparateFiles`。ユーザーが触るまで保存せず、触らなければ起動のたびにWindowsの既定の入力）
- **★エラー表示の規則（M4で追加・以後すべて）**: 音の鳴るエラーのポップアップは出さない。エラーは**画面内に赤文字で、原因の近く**に出す（デバイスの未選択・重複＝デバイス欄の下、それ以外＝ボタンの下の状態表示の上）。次の操作（開始・設定変更）で消える。例外は、確認が必要な はい／いいえ のダイアログ（異常終了の復旧確認・録音中の終了確認・テンプレート削除）と、想定外の例外（`App`）。
- **一覧の先頭に「既定の入力デバイス／既定の出力デバイス（変更に追従）」**を置く（M4）。選ぶとWindowsの既定の変更を検知して自動で切り替える（§3-2）。
- **波形**（M4）: 録音中に右から左へ流れるピーク波形（デバイスごとに1段）。ピーク警告は全体メーターの「クリップ!」＋デバイス行のメーターが赤。
- 形式: **mp3 / aac(.m4a。.aacも選択可) / flac**。**既定flac**。**wav・ogg・opus を追加（M5で実装。2026-11-02に要望）**: wav＝16bit PCM（長時間は `-rf64 auto` で4GB超に対応。異常終了時は長さ情報が壊れるので、復旧（Recovery）で `-c copy` で作り直す）、ogg＝Vorbis（ビルド時に `libvorbis` を検査項目へ追加。ビットレート指定可）。opus＝Ogg Opus（拡張子 `.opus`、`libopus`、ビットレート指定可。サンプリング周波数は48kHzへ変換）。タグはwavがLIST INFO、ogg/opusがVorbisコメント。ビルド時の検査に `libvorbis` `libopus` も加える。最後に選んだ形式を次回も維持。
- 音質指定: ビットレート／サンプリング周波数／チャンネル。デバイス側と違っても設定値へ変換。**圧縮率の設定欄（M5で実装。2026-11-02に要望。形式・音質の行に常時表示）**: 変更できる形式だけ表示する＝flac（圧縮レベル0〜12、既定5。大きいほど小さいが重い）、opus（圧縮レベル0〜10、既定10）、mp3（LAME品質0〜9、既定は「変更しない」）、ogg（Vorbis品質0〜10、既定は「ビットレートで指定」。品質を選ぶとビットレートは使われない）。wav・aacは圧縮率の設定なし。値は形式ごとに ini の `Compression_<形式>` へ保存。
- 長時間対応。**最長24時間**で強制自動分割。
- 直接エンコード方式。aacは録音中は断片化MP4（途中で落ちても再生可）。
- 異常終了後の起動時に、閉じられていない（名前未確定の）ファイルを検出して整える。（M3で確定: 保存先フォルダ（日付フォルダの中も）の `_rec_*` を探し、**確認ダイアログ（はい／いいえ）を出してから**リネームする。「いいえ」なら残して次回起動時にもう一度確認。他のプロセスが開いているファイルは対象外、空ファイルは削除。2つ目の起動（書き込まない側）では行わない。名前は終了時刻が分からないためユーザーのパターンは使わず `{Date} {Time} (No.{No}-Track{Track})＿restored.{ext}` 固定（日時はそのファイルの開始時刻）。m4aは通常のm4aへ整える）
- **一時停止／再開ボタン（M3で実装）**: 録音中に押すと止まり、もう一度押すと同じファイルへ続ける。一時停止中の時間はファイルに入れず（無音も入れない）、経過時間・`{Start}`/`{End}` も止まる。（M3で確定。デバイスは開いたまま、停止中の音は捨てる。一時停止中でも停止・分割はできる。つなぎ目のプツッという音を防ぐため、停止・再開の前後に約20msのフェードを掛ける）
- ゲイン調整、ピーク警告（クリップで赤）。波形表示と音量メーター。
- **ASIO**は希望あり。リスクが高いので最終マイルストーン。
- 録音中のスリープ防止（既定ON。M5で実装、「その他」のチェックで切替）。ディスク残量監視（M5で実装: 録音中30秒ごとに保存先のドライブを確認し、**1GB未満で赤文字の警告、100MB未満で録音を止めて保存**。開始時も100MB未満なら開始しない）。録音中にアプリを閉じようとしたら警告（M1から実装済み）。

### 3-2 デバイス変更時の継続
- 初期値: **同じファイルに継続**（途切れ区間は無音で埋め、時間がずれない）。（M4で実装: 外れたデバイスは1秒ごとにつなぎ直し、戻れば自動で再開、その間は無音。他のデバイスは録音を続ける。全デバイスが最初から開けなければ開始しない。「戻るまで待機」「戻ったら別ファイルに分割」のオプションは M7）
- 「既定のデバイス」選択時は、Windowsの既定デバイス変更を検知して自動切替。
- 特定デバイス選択時: オプション「戻るまで待機」＋「戻ったら別ファイルに分割」。自動再開オプション（既定ON）。

### 3-3 分割
- 手動: ワンタッチの分割ボタン（途切れなく次ファイルへ）。自動: 指定時間ごと。オプション: 無音検出分割（既定OFF）。24時間で強制分割。
- （M3で確定）自動分割の間隔は**時:分:秒（00:00:00）の入力欄を常時表示**（00:00:00＝無効。最短00:00:10、最長24:00:00。既定は無効。▲▼と↑↓キーは、入力欄のカーソルがある桁（時・分・秒）を±1。まだ触っていないときは「分」。説明文は「(0＝無効。最長24時間)」。M3確認後に分単位から変更。旧版のiniの `SplitMinutes` も読む）。1つのファイルは間隔が0でも24時間で必ず分割。録音中は変更不可。直前の分割から1秒未満の手動分割は無視。分割で保存したファイルは状態表示に出す。次のFFmpegを先に起動してから出力先を切り替える（PCMはそのまま続くので途切れない）。

### 3-4 保存先・ファイル名
- 保存先フォルダ指定可。**既定の保存先は `ドキュメント\LokuOnX\Rec`**（`AppInfo.Name` + `Rec`）。その下に**日付フォルダ**（既定書式 `{yyyy}-{MM}-{dd}`、録音**開始日**で固定）を作って保存。オプションで日付フォルダなし直接保存。
- ファイル名はテンプレート（変数）。
  - **既定（日時形式）**: `{Date} {Time} (No.{No}).{ext}` → `2026_10_31 08_39_47 (No.917).flac`
  - **トラック形式（標準で用意）**: `No.{No}-Track{Track}_{Start}-{End}({Length}).{ext}` → `No.917-Track001_00：00-36：25(36：25).mp3`、分割後 `No.917-Track002_36：25-40：00(3：35).mp3`
  - プルダウンで切替。上記2つは初回起動時に登録されるサンプルで**削除・編集可**（設定の「初期テンプレートを復元」で戻せる）。独自テンプレートも名前を付けてiniに保存でき、プリセットに紐づく。
  - **日時変数（{Date}{Time}{yyyy}…{ss}）は「そのファイルを始めた時刻」**（M3確認後に確定）。分割したファイルは分割した時刻になる（例 `2026_11_01 09_19_02` → `2026_11_01 09_20_02`）。日付フォルダだけは録音開始日で固定。
  - 日時部品を直接書ける: `{yyyy}` `{yy}` `{MM}` `{dd}` `{HH}` `{mm}` `{ss}`。`{Date}`=`{yyyy}_{MM}_{dd}`、`{Time}`=`{HH}_{mm}_{ss}`。
  - 変数: `{No}` `{Track}` `{Start}` `{End}` `{Length}` `{Date}` `{Time}` `{Device}` `{Preset}`。
  - 同名は末尾に ` (2)` ` (3)`。別ファイル保存の同時録音は `{Device}` を自動付加。
  - コロンは全角「：」に自動変換。その他禁止文字も置換。
  - Start/End は「録音全体の0:00起点」の経過時間。`Length=End−Start`（秒単位で正確）。開始は前トラックの終了に一致。
  - ファイル名は**保存（閉じる）時に確定**。録音中は仮名で書き、閉じた後にリネーム。
- **変数の説明は入力欄の外側後方の「？」ボタンから開く別ウィンドウに出す**（常時表示しない）。ウィンドウには入力欄・例・変数ボタン（押すとカーソル位置に挿入）・説明を出す。ファイル名パターンと日付フォルダ名の両方に付ける。
- 時間表記: 既定 `1：05：30`（h：mm：ss）。変数の書換えで `125：30`（分のみ）も可。
  - （M2で確定）`{Start}`/`{End}` は1時間未満が `mm：ss`（例 `00：00`、`36：25`）、`{Length}` は `m：ss`（例 `3：35`）。1時間以上は `h：mm：ss`。`{Start:min}` のように `:min` を付けると `125：30`（分のみ）。`{Track}` は3桁ゼロ埋め（`001`）、`{No}` は桁埋めなし。`{Preset}` はプリセット実装（M5）までは「既定」。`{ext}` は小文字。
- `{No}`: 開始番号の入力欄あり。既定1から録音ごとに+1。**終了後も維持**。
- `{Track}`: 開始番号の入力欄あり。**新しい録音ごとに開始番号へ戻る**。分割ごとに+1。開始番号は再起動後も維持。
- 日本語（ファイル名・タグ・UI）に完全対応。**UIは日本語**。

### 3-5 タグ
- ID3等のメタデータ（タイトル、アーティスト、アルバム、ジャンル、年、コメント）入力欄を1か所（折りたたみ「日付フォルダ・タグ・その他」の中の `TagPanel`）にまとめる。既定は空。文字化けしないこと。（M5で実装。FFmpegの `-metadata`。mp3はID3v2.3＋UTF-16。`.aac`（ADTS）はタグを持てない。実機で日本語タグを要確認）
- トラック番号タグ: 既定OFF。ONなら分割した録音の全ファイルに付ける（`{Track}` と同じ番号）。

### 3-6 プリセット・画面・多重起動
- プリセットを複数保存しプルダウンで選択。JSON入出力。（M5で確定: 画面最上段の「プリセット」行（`PresetBar`）。含める項目＝形式・音質・圧縮率・入力デバイスとゲイン（有効チェック含む）・ミックス/別ファイル・自動分割・ファイル名テンプレートとパターン・日付フォルダ・タグ。**保存先フォルダと番号（No/Track開始値）は含めない**。選ぶと即反映、名前を書いて「保存」で上書き/追加、「削除」は はい／いいえ の確認。「Presetsフォルダを開く」ボタンでフォルダを開く（ファイルを入れる・コピー・削除でプリセットを追加・共有・削除。プルダウンを開くと反映）。JSON書き出し／読み込みボタンはM5確認後に廃止。**exeと同じ場所の `Presets/名前.json` に1件ずつ自動保存**（保存・削除・切替のたびにファイルを読み書き。小さいので遅くない。M5確認後にiniから変更。初版のini内のプリセットは起動時にファイルへ移す。2つ目の起動では書かない）。使用中のプリセット名は `{Preset}`（未選択は「既定」）。録音中は操作不可。）
- **1画面**に集約。上段=設定、中段=波形・メーター・録音/分割ボタン、下段=ファイル一覧とプレイヤー。
- **コンパクトUI**: 幅1280px以内、高さ1440px以内を目安。最小幅1000px程度。詳細設定・タグ・オプションは折りたたみ（Expander）。
- **★メジャーな設定は折りたたみの外に常時表示、マイナーな設定は折りたたみ（M3で確定。「現在の設定」サマリー行は廃止）**: 常時表示 = 入力デバイス／形式・音質（ビットレート・サンプリング周波数・チャンネル）／保存先／自動分割の間隔／ファイル名テンプレート・パターン（例つき）／番号（No/Track開始値）。折りたたみ = 日付フォルダ・タグ（スリープ防止・画面の色は右上の歯車の設定ウィンドウへ移した。M5確認後）（M3確認後、ファイル名・パターン・番号を常時表示に変更。M5で圧縮率の欄を形式・音質の行に常時表示、プリセット行を最上段に追加）。オプション = M7以降。設定項目が増えるたびに、どれをメジャーにするかをマイルストーン開始時にユーザーと決める。録音ボタンは「録音開始／一時停止（再開）／分割／停止」を中段の下に横並び。
- **★多重起動の判定は「同じフォルダのexe」だけ（変更）**: ポータブル運用のため、別フォルダに置いた別コピーは設定ファイルも別なので**警告せず普通に起動**する。Mutex名は exeのフォルダパスから作ったハッシュ（例 `Local\LokuOnX_<hash>`）。
  - 同じフォルダで2つ目を起動した場合のみ、毎回ポップアップ: 「すでに『LokuOnX』が起動しています。設定は保存されませんが、それでも起動しますか？」（はい／いいえ、既定は「いいえ」＝終了）。
  - 「はい」の場合、2つ目は設定iniと`{No}`の**書込みを行わない**。2つ目で変更した設定はその起動中だけ有効で、終了時に破棄。`{No}`は更新されない（重複名は`(2)`で衝突しない）。
- ダークモード対応（M5で実装: 設定ウィンドウ（右上の歯車）の「画面の色」＝自動（Windowsに合わせる・既定）／ライト／ダーク。ダークは `UI/Theme/Dark.xaml`＋`DarkControls.xaml` のリソース差し替え（質感は **Windows 11 のアクリル（半透明）**。使えない環境は不透明のダーク）、ライトは標準の見た目のまま。タイトルバーも合わせる。Windowsの設定を実行中に変えた場合の自動追従はしない）。タスクトレイ常駐・最小化したままの録音（M7）。

### 3-7 生成ファイル一覧・再生（M6で実装・確定）
- **置き場所（M6確認後に変更）**: 画面を**左右に分割**（`GridSplitter`でドラッグ調整。左＝設定・録音ボタン・波形・状態、既定700px・最小560px／右＝ファイル一覧（`FileListPanel`）＋プレイヤー（`PlayerPanel`）、最小380px）。ウィンドウは既定 1280×900、最小 1000×760（作業領域より大きいときは収める）。プレイヤーは2段（再生・停止・シークバー・時間／速度）。
- **一覧**: ファイル名・長さ・サイズ・作成日時。保存先フォルダ（日付フォルダの中も、深さ3まで）の flac/mp3/m4a/aac/wav/ogg/opus を**新しい順**に出す。録音中の仮名（`_rec_*`）は出さない。見出しのクリックで並べ替え（もう一度で逆順）。複数選択可。読み込みのタイミング＝起動時・復旧後・録音停止後・保存先の変更・「更新」ボタン。長さは FFmpeg（`ffmpeg -i` の Duration 行）で読み、新しい順に200件まで自動（2本並行）、選んだファイルは必ず読む（読み込み前は「…」）。
- **録音中の表示**: 一覧の一番上に赤枠で「● 録音中… トラック N ― 停止か分割をすると、ここに追加されます」（一時停止中はその表示）。分割で閉じたファイルは「保存しました」の通知で一覧を読み直して出す。
- **起動後のファイル**: このアプリを起動してから作られたファイルは、一覧で**文字色（青系・太字）を変える**（選択中の行は変えない）。長さの読み込み前は「読込中」。
- **再生**: 再生／停止／シークバー（クリック・ドラッグで移動）。「停止」は位置を覚え、もう一度「再生」で続きから。最後まで行くと先頭に戻る。ダブルクリック／Enter／右クリック「再生」で再生。**速度 0.5〜2倍（0.05刻み、音程維持）は FFmpeg の `atempo`**（M6で確定。SoundTouch系は採用せず＝追加パッケージなし・全形式で同じ経路・Linux移植でも同じ）。ファイルは FFmpeg で読んで 44.1kHz/16bit/ステレオのPCMにし、NAudio の `WaveOutEvent` で鳴らす。シーク・速度変更は新しい位置・速度で FFmpeg を起こし直す（0.1〜0.3秒の間が空く）。速度は起動のたびに1倍（保存しない）。
- **操作（ボタン・右クリック・キー）**: エクスプローラーで開く（`explorer /select`。**複数選択のときは はい／いいえ の確認**）／名前の変更（F2。1つだけ。専用ダイアログに名前（拡張子なし）を入力。禁止文字は `FileNamer.Sanitize` と同じ規則で全角へ置換、同名があればダイアログ内に赤文字のエラー）／ごみ箱へ削除（Delete。複数選択可。**はい／いいえの確認あり**。`SHFileOperation`＋ALLOWUNDO で、完全削除にはならない）。名前の変更・削除の前に、そのファイルの再生を止めて手放す（§5-10）。失敗は一覧の下に赤文字（ポップアップなし）。
- 波形クリックでのシーク、簡易トリミングは対象外。

### 3-8 オプション機能（すべて既定OFF。自動再開のみ既定ON）
無音検出での自動分割／無音検出での自動録音開始／グローバルホットキー（開始・停止・分割）／予約録音／トラック番号タグ／デバイス復帰時に別ファイルへ分割。

## 4. 配布物・リポジトリの構成（★新規。M0で実装）

### 4-1 リポジトリ（直下は最小）
```
/
├─ .github/workflows/build.yml
├─ src/                  ← コードはすべてここ（LokuOnX.csproj、App.xaml、Core/、UI/ ほか）
├─ assets/
│   └─ icon/LokuOnX.ico      ← アプリアイコン（ここ。差し替えるときは同名で上書き）
├─ PROJECT.md
└─ .gitignore
```
- アイコン: `assets/icon/LokuOnX.ico`（16/32/48/256px入り）。csprojの `ApplicationIcon` とウィンドウの `Icon` が参照。M0でClaudeが仮アイコンを生成して渡す。自作アイコンは同名で上書きするだけ。
- GitHub Webで上げるときのため、`src/` はフォルダごとドラッグ＆ドロップ。`.github` は「Create new file」で手入力（§5-9）。

### 4-2 配布ZIP（展開後のフォルダ直下は最小）
```
LokuOnX/
├─ LokuOnX.exe        ← 単一ファイル（.NETランタイム込み）
├─ settings.ini       ← 初回起動時に生成
└─ ffmpeg/ffmpeg.exe（＋LICENSE.txt）
```
- 言語リソース（ja, ru など多数のフォルダ）は出さない: `SatelliteResourceLanguages=en`。必要な場合のみ `lang/` にまとめる。
- 単一ファイル化で残るネイティブDLLは `IncludeNativeLibrariesForSelfExtract=true` でexeに内包。**動かない場合の代替**: 本体を `app/` サブフォルダに入れ、直下は小さな起動exe＋iniだけにする（M0で実機確認し、どちらにしたか §11 に記録）。
- **ZIPの二重化を解消**: Releasesに添付するのは `LokuOnX-win-x64.zip`（展開すると `LokuOnX/` フォルダ）。Actionsの artifact（確認用）は**ZIPにせずフォルダのままアップロード**する（GitHubが自動でZIP化するため、ZIPを入れると二重になる）。

## 5. 想定される問題と対策
| # | 問題 | 対策 |
|---|---|---|
| 1 | ASIOはドライバ専有でループバックと両立しにくい | 最終マイルストーン。機器が無ければ見送り |
| 2 | ループバックは無音時にデータが来ない | 壁時計基準で無音を補填する層を最初から入れる |
| 3 | 複数デバイスのクロックずれ | デバイスごとにバッファ＋リサンプルしてミックス。M4で長時間確認 |
| 4 | 分割時のギャップ | PCMをリングバッファに溜め、次のFFmpegを先に起動してから切替 |
| 5 | FFmpeg死亡・標準入力詰まり | 専用スレッド＋キュー。死亡検知で自動再起動（新ファイルで継続） |
| 6 | 異常終了時の残骸・mp3ヘッダ欠落 | 仮名に開始時刻を含め、起動時に最終名へリネーム。必要ならFFmpegで再mux |
| 7 | 日本語タグの文字化け | mp3は ID3v2.3＋UTF-16（`-id3v2_version 3`）。実機で確認 |
| 8 | FFmpegのlibmp3lame有無・ライセンス | ビルド時に `libmp3lame` `aac` `flac` を自動検査して失敗させる |
| 9 | GitHub Webアップロード制限（25MB/ファイル、100ファイル/回、ZIP非展開、`.github`が漏れる） | FFmpegはActionsで取得。フォルダごとD&D。`.github/workflows/build.yml` は手入力で作成。**リポジトリ連携が使えれば、Claudeが直接pushして不要になる** |
| 10 | ファイル名の禁止文字・260文字・衝突・再生中リネーム | 名前生成を `FileNamer` 1クラスに集約。再生を止めてからリネーム |
| 11 | 再生速度で音程維持が必要 | FFmpeg `atempo`（M6で採用。§3-7） |
| 12 | 長時間でのメモリ増加 | 波形は間引いたピークのみ保持。バッファ上限固定。M4で数時間テスト |
| 13 | Claudeは実機テスト不可 | **Claudeはコードを渡す前に Linux 上でビルド確認する**（`dotnet build -p:EnableWindowsTargeting=true`。SDKが入れられない場合はその旨を明記）。マイルストーンごとの5項目チェックリストで報告 |
| 14 | 無料枠の上限 | §7のルール厳守 |
| 15 | iniの書込み破損・`{No}`巻き戻り | 値の確定時に即保存。一時ファイル→置換 |
| 16 | 画面の詰め込み | メジャーな設定は常時表示、マイナーはExpander（§3-6） |
| 17 | 多重起動でini・`{No}`が競合 | 同じフォルダのexeだけ Mutex で検出（§3-6）。2つ目は書き込まない |
| 18 | UI幅1280px内に収める | M1でレイアウトを固めて実機確認 |
| 19 | csprojに `ImplicitUsings` が無く `System.IO` 等が未解決（初版のビルドエラー原因） | csprojは `ImplicitUsings=enable`・`Nullable=enable` を必ず維持。さらに **WPFでは暗黙usingに `System.IO` が入らない**ため、csprojの `<Using Include="System.IO" />` も維持（M1で `Path`/`File` 未解決エラーになった）。書き換えるときも消さない |

## 6. コード構成の規則
1. 1ファイル**200行前後まで**。大きければ分割。
2. 配布は単一ファイル発行（§4-2）。
3. **移植しやすさの規則（Linux移植の備考への対応。実装コストはほぼゼロ）**:
   - `Core/` の純粋ロジック（`FileNamer`・`Settings`・`Presets`・時間計算）は **WPFやWindows APIに依存させない**。
   - 音声入力は `IAudioSource` のような小さなインターフェースの後ろに置き、NAudio（WASAPI）は Windows専用の1実装にする。
   - UI（WPF）は `UI/` に閉じ込め、Coreからは参照しない。
   - 現時点では Linux 版は作らない。UI差し替え（Avalonia等）が必要になったら §9 で検討。
4. 例: `Core/AudioCapture.cs` `Core/Mixer.cs` `Core/Encoder.cs` `Core/Splitter.cs` `Core/FileNamer.cs` `Core/Settings.cs` `Core/Presets.cs`／`UI/MainWindow.xaml(.cs)` `UI/SummaryBar.cs` `UI/FileListPanel.cs` `UI/Player.cs` `UI/Tray.cs`。
5. 公開クラス名・メソッド・引数は §11 に書き残す。

## 7. 開発の進め方（上限を回避するルール）
1. **1マイルストーン＝1チャット**。終わったら §11 を更新（Claudeが Project に書き戻す）して新規チャットへ。
2. 新しいチャットでは、Projectにある PROJECT.md を読む。**リポジトリを参照できるなら、触るファイルだけ読む**（全体を読まない）。参照できない場合は、今回触るファイルをユーザーが添付する（Claudeが手元に無いファイルを推測で書き直さない）。
3. コードは**変更したファイルを全文**で出す。GitHub連携が使えるなら、Claudeが直接コミットする。
4. 質問はマイルストーン開始時に1回だけ。以降は推奨案で進め、最後に1行で報告。
5. 動作確認は5項目以内のチェックリスト。エラーは Actions ログの `error` 行だけ貼る。
6. 新しい要望は §9 に積み、現在のマイルストーンには入れない（ただし**ユーザーが「今回の不具合」と明示したものは現マイルストーンで直す**）。
7. 実装系チャットで雑談や設計の再検討をしない。

## 8. マイルストーン（この順で）
| M | 内容 | 動作確認（ユーザー） |
|---|---|---|
| M0 | 新リポジトリ構成（§4-1）、アイコン、**Actionsでビルド→単一ファイル→FFmpeg取得と検査→ZIP（二重化なし）→Releases**。空のWPF画面（日本語、アプリ名定数 `AppInfo.Name`） | ZIPを展開すると直下が exe と ffmpeg フォルダだけ。起動できる。アイコンが出る |
| M1 | 画面骨格（幅1280以内）＋**現在設定サマリー行**、**同じフォルダだけの多重起動警告**、**デバイス一覧（マイク＋出力デバイスのループバック）**、WASAPI録音、FFmpeg直接エンコード（flac/mp3）、開始/停止、メーター、`settings.ini`の最小読み書き | スピーカー（PC音）も選べて録音・再生できる。別フォルダのコピーは警告なしで起動。同フォルダの2つ目は警告 |
| M2 | 無音補填、aac追加、音質指定、ファイル名テンプレート（既定/トラック形式）と日付フォルダ、`{No}`維持、`(2)`付与 | PC音を録音でき時間と名前が一致 |
| M3 | 分割（手動・時間・24h強制）、ギャップなし切替、最終名リネーム、異常終了復旧、**一時停止／再開** | 分割点で途切れない。一時停止で時間が進まない |
| M4 | 複数デバイス同時（ミックス／別ファイル）、クロックずれ補正、デバイス切替継続、波形、ゲイン、ピーク警告 | 数時間で破綻しない |
| M5 | プリセット、ini全項目、JSON入出力、タグ、**wav/ogg/opus形式の追加と圧縮率の設定欄**、ディスク残量、スリープ防止、終了時警告、ダークモード | 再起動後も設定維持 |
| M6 | ファイル一覧、プレイヤー（速度変更）、リネーム、ごみ箱削除、エクスプローラー（確認付き） | 一覧操作が動く |
| M7 | トレイ常駐、最小化録音、ホットキー、予約録音、無音検出、デバイス待機/復帰分割 | OFFなら既定動作に影響なし |
| M8 | ASIO（可能なら） | 対応機器で録音。無理なら見送り |
| M9 | 24時間級の長時間テスト、総点検、README | 完成 |

## 9. バックログ（現在の作業に混ぜない）
- 簡易トリミング（前後カット）
- 英語UI
- 画面構成の見直しはM3で実施（サマリー行を廃止し、音質・自動分割を常時表示に）。以後は「設定項目が増えるたびに、どれをメジャーにするか各マイルストーン開始時に確認」を継続
- **プリセットの対象範囲を枠で囲む（M5確認後に要望。保留・要検討）**: いまは「保存先」「番号」（プリセット対象外）が対象の項目の間に挟まり、タグは折りたたみの中にあるため、枠で囲むと分離してしまう。囲むなら、保存先・番号を枠の外（上か下）へまとめる再配置が先。M6の画面構成の確認時に相談（M6では一覧を最下段に足しただけで、設定側の配置は変えていない。実機で画面の高さ・見た目を見てから相談。M6確認後に左右分割にしたので、再度見直し）。
- ダークの質感: アクリルを採用済み。ニューモーフィズムは見送り（ボタンの影が多く重い）。
- Linux版（UI差し替えの検討。§6-3）
- ニューモーフィズム風の画面の色（M6確認後に提案。文字・境界のコントラストが落ちて見にくくなりやすい。**M9で、設定の歯車に第3の選択肢として試作**。文字のコントラストは落とさない）
- （M6で積んだ）ファイル一覧: 長さの永続キャッシュ（いまは起動のたびにFFmpegで読む）／再生速度の保存／一覧の列幅の保存／ファイル一覧のフォルダ列
- （思いついた要望を1行ずつ追記）

## 10. 決定済みで変えないこと
§2、§3、§4、§5の対策方針。アプリ名は **LokuOnX（仮）**、表示名は `AppInfo.Name` の1か所で管理、名前空間は `Recorder` のまま（改名で全ファイルを触らないため）。UIは日本語（Yu Gothic UI、なければ Meiryo UI）。

## 11. 現在の状態（毎回更新する）
- 状態: **M6 実機確認済み（問題なし）＋確認後の修正を実装・Actionsビルド成功（2026-11-04）。修正分の実機確認待ち。** （M5の実機確認でのダーク等の指摘は反映済み。M4は実機確認OK・完了）Claude側にはLinux上の .NET SDK が入れられないため、Actions が唯一のビルド確認（`gh run view` と `gh api repos/.../check-runs/<job>/annotations` でエラーを読む）。※旧版の `settings.ini` はそのまま読める（`SplitMinutes` は `SplitSeconds` へ読み替え）。
- 初版の指摘（M0で対応済み）: ZIPの二重化／直下ファイルが多い／言語フォルダが多い／リポジトリ直下が多い。M1で対応: スピーカー（ループバック）が出ない／現在設定が折りたたみで見えない／別フォルダのexeでも多重起動扱い。
- M0（完了）: 単一ファイル発行で動作確認済み（`app/` 方式は不要）。FFmpegのURLは n8.1 → 404 なら master へ自動切替。Releases の ZIP 添付は**未確認**（タグ `v*` を作ると `LokuOnX-win-x64.zip` が添付される設計。初回Release作成時に確認）。
- M1（実機確認OK）: デバイス一覧（🎤マイク＋🔊出力のループバック）、WASAPI録音、FFmpeg直接エンコード、開始/停止、音量メーター、現在設定サマリー行（1行・自動スクロール・ホバー全文）、同じフォルダのみの多重起動警告、`settings.ini`。録音中にウィンドウを閉じると確認して停止・保存してから終了。
- **M2で入れたもの**:
  - 無音補填: 壁時計基準。データ到着時に不足分を無音で埋め、データが来ない間（ループバックの無音）は100msごとのタイマーで埋める（0.3秒手前まで）。停止時に停止時刻ちょうどまで埋める。足りない分だけ補い、余った分は削らない（クロックずれ補正は M4で実装済み）。
  - 形式: flac / mp3 / aac(.m4a) / aac(.aac)。m4a は録音中は断片化MP4で、停止後に FFmpeg `-c copy -movflags +faststart` で通常のm4aへ整える（失敗しても断片化のまま残す＝再生可）。
  - 音質: ビットレート（64〜320kbps、mp3/aacのみ。既定192）、サンプリング周波数（自動／22.05〜96kHz）、チャンネル（自動／モノ／ステレオ）。デバイスと違っても設定値へ変換。mp3は48kHz超・3ch以上、aacは3ch以上を上限へ丸める。
  - ファイル名: `FileNamer` に集約。テンプレート（既定=日時形式／トラック形式。編集・削除・名前を付けて保存・初期テンプレート復元）、禁止文字の置換（コロンは全角）、予約名・260文字対策、`(2)` 付与。録音中は仮名 `_rec_<開始時刻>_No<n>.<ext>` で書き、閉じた後に最終名へリネーム。
  - 日付フォルダ（既定ON、書式 `{yyyy}-{MM}-{dd}`、録音開始日で固定）。`{No}` の開始値入力欄（終了後も維持）、`{Track}` の開始値入力欄（新しい録音ごとに戻る。分割は M3）。
  - 現在設定サマリー行は実際に書き出される形式・音質・保存先（日付フォルダ込み）・テンプレート名を表示（動かさない。収まらないときは「…」で省略、ホバーで全文）。
  - 既定デバイスの印: `AudioDeviceInfo.IsDefault`/`DisplayLabel`（末尾に ＊）。「一覧を更新」で既定の変更を反映（M4で、録音していないときは3秒ごとに自動反映）。
  - M2確認後の修正: 入力デバイスの初期値＝Windowsの既定の入力（前回選んだデバイスがあればそれを優先。`settings.ini` の `Device` 行を消すと既定に戻る）。一覧はマイク→スピーカー、それぞれ名前順、「（PC音）」なし。変数ヘルプは `？` ボタン→`VariableEditor` ウィンドウ（`VariableHelp` に変数一覧）。
- 既知の簡易実装（以降で置換）: FFmpeg死亡時は録音を止めてエラー表示（自動再開なし。M4の範囲外のため据え置き。§5-5。次に入れる場所はM5以降で相談）／タグなし（M5）／`{Preset}` は「既定」固定（M5）／分割の境界時刻は整数秒に切り捨て（ファイル名用。音声の切れ目は途切れなし）。
- **M3で入れたもの**:
  - 分割: 手動（分割ボタン。直前の分割から1秒未満は無視）、時間（時:分:秒の入力欄を常時表示。00:00:00＝無効。最短10秒、最長24時間）、24時間強制（間隔が0でも必ず）。次のFFmpegを先に起動してから出力先を切り替える（途切れなし）。閉じたファイルは別タスクで最終名へ確定し、状態表示に「保存しました」を出す。
  - `{Track}`: 録音ごとに開始値へ戻り、分割ごとに+1。`{Start}`/`{End}`/`{Length}` は録音全体の0:00起点の整数秒。仮名は `_rec_<日時>_No<n>_T<track>.<ext>`。
  - 一時停止／再開: デバイスは開いたまま、停止中の音は捨てる。経過時間・`{Start}`/`{End}`・自動分割の時計も止まる（無音も入れない）。停止中でも停止・分割は可。
  - 異常終了復旧: 起動時（2つ目の起動では行わない）に保存先（日付フォルダ内も、深さ3まで）の `_rec_*` を探し、確認ダイアログ後に `{Date} {Time} (No.{No}-Track{Track}) 復旧.{ext}` へリネーム（M3確認後に `復旧` → `＿restored`（全角の＿。直前の空白なし）へ変更）。他プロセスが開いているものは対象外、空ファイルは削除、「いいえ」なら残す。
  - 画面: サマリー行を廃止。入力デバイス／形式・音質／保存先／自動分割／名前の例を常時表示。録音ボタンは 開始・一時停止・分割・停止。
- M3確認後の修正（2026-11-01）: ①日時変数を分割ごとのファイル開始時刻に ②復旧名を `_restored` に ③既定デバイスの印を ＊ に ④一時停止・再開に20msフェード ⑤`{Start}/{End}` の秒は四捨五入（59.9秒→1：00。以前は切り捨てで59秒に見えた） ⑥録音開始の直前に詳細設定の入力途中の欄（パターン等）を設定へ反映（「形式を変えても分割で変更前の名前になる」の原因候補。再現できなかったため、再発したら手順を教えてもらう）。タグは未実装（M5）。
- M3再確認後の修正（2026-11-01）: 分割の境界ちょうどのフレーム数まで無音で埋めてから切り替える（`SilenceFiller.CutAt`。前のファイルがちょうどの長さになり、エクスプローラーで59秒に見える問題の原因＝約0.3秒不足を解消）。保存時の表示に実際の音声の長さ（秒）を追加（ファイル名の時間とのずれの切り分け用）。復旧名を `＿restored`（全角、空白なし）に。ファイル名テンプレート・パターン・番号を常時表示に。**未解決**: 一部ファイルで 00:32:59・00:01:03 と表示される件（原因未特定。形式・表示した場所・一時停止の有無を確認中。FFmpeg単体では flac/mp3/aac/m4a とも60秒で正確に出ることを確認済み）。
- M3再々確認後の修正（2026-11-01）: ①分割をフレーム単位で正確に（`SilenceFiller.ScheduleCut`/`CutNow`。自動分割は境界の1秒前に次のFFmpegを準備し、書き込みフレーム数が境界ちょうどに達したところで切り替える。これでPCMの長さが各ファイルちょうど。手動分割は押した時点）②数字だけの入力欄（自動分割・No・Track開始値）はIMEを切り、貼り付けた全角数字は半角へ自動変換（`Digits`/`NumericBox`）。
- **AACの長さ表示について（確認済みの事実）**: `.aac`（ADTS）は形式上、長さを書く場所が無く、再生ソフト/エクスプローラーが推定するため 00:00:58 や 00:59:39 のようにずれる（音声自体は正しい）。mp3は長さ情報（Xingヘッダー）が付くので正常、m4a（MP4）は正確な長さを持つ。ヘッダー修復・タイムスタンプ再構築はADTSには効果がなく（同じ中身を書き直すだけ）、m4aへの変換（`-c copy`、再エンコードなし）なら正確になる。**長さを正しく表示したいなら m4a を使う。**`.aac` は残すが非推奨。
- M3追加（2026-11-02）: ①中段の時間表示は「いまのファイルを始めてから」を大きく、分割後は横に「（連続 録音全体の時間）」を小さく出す（`RecordingSession.TrackElapsed`/`SplitCount`）。一時停止中は両方止まる。②自動分割の準備と手動分割が重なったときに `_rec_*` の仮ファイルが残る不具合の対策（取り消すFFmpegの終了を待ってから削除。`FfmpegEncoder.Abort`）。再現性が低いため再発したらファイル名を教えてもらう。※手動分割のファイルの名前の長さは四捨五入、エクスプローラーは切り捨てなので 0：42 が 00:00:41 に見えることがある（仕様）。
- M3追加2（2026-11-02）: ①数字だけの入力欄（自動分割・No・Track開始値）に右端の ▲▼（±1、押し続けで連続）を付けた（`UI/NumberBox`。入力幅は変えず矢印の分だけ広がる。のち幅13・6ptへ。全角→半角・IME無効は継続）。②手動分割が自動分割の準備（境界の1秒前から）と重なったとき、準備済みのファイルをそのまま手動分割に使う（捨てない。以前は捨てて消し損ね→`_rec_` が残った疑い）。停止時に残った準備ファイルは消す（消せなければ最後にもう一度）。
- M3追加3（2026-11-02）: 自動分割の入力を時:分:秒に変更（`Core/SplitTime`、`Settings.SplitSeconds`、`UI/TimeBox`。数字だけ入力すると右詰めで時分秒、コロン1つは分:秒）。▲▼は幅13・6pt。入力欄の幅は自動分割＝00:00:00が入る幅（86）、No/Track＝5桁が入る幅（68）。
- M3追加4（2026-11-02）: 自動分割欄の幅を70に（No/Trackも70に統一）。メイン画面の入力欄はすべて Enter で確定（`App.OnTextBoxKeyDown`：Enterでウィンドウへフォーカスを移し、LostFocus の処理を走らせる。複数行・読み取り専用・別ウィンドウは対象外）。
- M3追加5（2026-11-02）: Enterで確定されなかったため修正（クラスハンドラをPreviewKeyDownにし、Enterで次の欄へフォーカスを移して LostFocus を走らせる方式に変更。実機確認待ち）。
- **M4で入れたもの（2026-11-02）**:
  - 音の流れ: デバイスごとに `DeviceInput`（つなぐ・外れたら1秒ごとにつなぎ直す・既定に追従）→ `DeviceStream`（float変換・ゲイン・周波数変換・壁時計への時間合わせ）→ `MixedSource`（20msごとに全デバイスを混ぜて、壁時計より0.2秒遅れで流す。1台のときも同じ経路）→ 従来の `RecordingSession`。`SilenceFiller` は大きな欠落（1秒超）だけを補う設定（`toleranceSec:1.0, idleMarginSec:1.5`）にした。
  - クロックずれ補正: 時間軸が壁時計より0.04秒以上ずれたら±0.1%の伸縮（3次補間 `StreamResampler`）で少しずつ合わせ、0.01秒以内で止める。0.08秒を超えて遅れたら無音で埋め直し、0.5秒以上進んだら届いたブロックを捨てる。
  - 別ファイル: 仮名は `_rec_<日時>_No<n>_T<track>_D<デバイス番号>.<ext>`（`FileNamer.TempName` の `lane`）。復旧は名前に `-Dev<n>` を入れる（`Recovery`）。
  - 手動分割は音声を実際に書いた位置（`SilenceFiller.Position`）で分ける。
  - 画面: 入力デバイス欄（`UI/DeviceList`）、中段に波形（`UI/WaveView`）。`MainWindow.Recording.cs` は `RecordingGroup` を使う。
  - 未実装で据え置き: FFmpegの死亡検知と自動再開（§5-5）、「戻るまで待機」「戻ったら別ファイルに分割」（M7）、ミックスのパン・デバイスごとの形式。
- M4実機確認（2026-11-03。追加修正の確認も全項目OK。その後、エラーを赤文字表示へ、切替を常時表示のグレーアウトへ、「（なし）」→「（未選択）」、✕を小さく、ツールチップの文言を変更）: 1・3・5は問題なし。4（デバイスの抜き差し）の確認は最終段階（M9）でよい。5の長時間テストもM9。数分のマイク＋スピーカーのミックスはずれなし。
- **M5で入れたもの（2026-11-03）**:
  - 形式: wav（16bit PCM、`-rf64 auto`）、ogg（Vorbis）、opus（Ogg Opus。`.opus`、48kHz固定）を追加（`OutputFormat`）。ビルド時の検査に `libvorbis` `libopus` `pcm_s16le` を追加（Actionsで通過済み）。復旧（`Recovery`）はwavのヘッダーを `-c copy` で作り直す（`FileFinalizer.Run(..., repair:true)`）。**wavの復旧は実機で未確認**（壊れたヘッダーをFFmpegが読めない場合は、そのまま保存して警告）。
  - 圧縮率: `CompressionSpec`（形式ごとのラベル・範囲・既定）、`EncodeOptions.Compression`、`Settings.Compression`（形式ごとに保存）、`QualityBar` の欄（flac・mp3・ogg・opusのみ表示）。
  - タグ: `TagInfo`（`Core/TagInfo.cs`）、`Settings.Tags`（ini `[Tags]`）、`UI/TagPanel`、`FfmpegArgs` が `-metadata` を渡す。トラック番号タグは `RecordingPlan.OptionsFor(index)` でファイルごとに付ける。
  - プリセット: `Core/PresetData.cs`（`PresetData`/`PresetInput`/`PresetJson`）、`Core/Settings.Presets.cs`（`Presets`/`SavePreset`/`DeletePreset`/`CapturePreset`/`ApplyPreset`）、`UI/PresetBar`（最上段）。`Settings.CurrentPreset` が `{Preset}`。`DeviceList.Rebuild` で行を作り直す。
  - ディスク残量: `Core/DiskSpace.cs`。録音中30秒ごと、1GB未満で赤文字、100MB未満で停止して保存。スリープ防止: `Core/Win/SleepGuard.cs`（UIスレッドで ON/OFF）。終了時警告はM1から実装済み。
  - ダークモード: `UI/ThemeManager.cs`、`UI/Theme/Light.xaml`・`Dark.xaml`・`DarkControls.xaml`、設定 `Theme`（auto/light/dark）。MainWindow・VariableEditor の背景/文字色は `DynamicResource WindowBg/WindowFg`。**見た目は実機で未確認**（ComboBox・Buttonを作り直しているため、崩れがあれば連絡）。
  - 画面: 最上段にプリセット行、形式・音質行は `DockPanel`＋`WrapPanel`（幅が足りなければ折り返す）、「日付フォルダ・タグ」の折りたたみを「日付フォルダ・タグ・その他」に。ウィンドウの既定の高さを 880、最小 760 に。
  - 据え置き: 入力欄の「ini全項目」は、新項目（`Compression_*`/`[Tags]`/`[Presets]`/`Theme`/`PreventSleep`/`Preset`）を含め、値を変えた時点で保存される。FFmpeg死亡時の自動再開（§5-5）はM5の範囲外のため据え置き。
- M5確認後の修正（2026-11-03）: ①ダークでコンボボックスに型名（`OutputFormat(K…`）が出る不具合（各 record に `ToString` を追加、表示部は `ContentSource`）②アイコンを `assets/icon/LokuOnX.ico` に改名③プリセットを `Presets/` フォルダのJSONへ④右上の歯車＝`UI/SettingsWindow`（画面の色・スリープ防止・ヘルプ・バージョン情報・GitHubリンク。今後めったに使わない設定はここへ）⑤ダークをアクリルに（`ThemeManager.Style`）。チェックリスト2（opusなど）は保留。
- M6確認後の修正（2026-11-04）: ①画面を左右分割（§3-7）②録音中の表示を一覧の一番上に ③分割したファイルを一覧へ（「保存しました」通知で更新）④起動後のファイルを文字色で区別（`LibraryFile.IsNew`/`SessionStart`、`Theme/*.xaml` の `NewFileFg`）⑤「（連続 …）」→ 小さい「Total Time」の下に時間（大きさ20）を表示（`TotalPanel`/`TotalText`。分割後のみ）⑥作成日時は秒なし、「…」→「読込中」、ボタン行は折り返し。実機未確認。
- **M6で入れたもの（2026-11-04）**:
  - 一覧: `Core/LibraryFile.cs`（`LibraryFile(Path,Name,Size,Created,Length)`＝長さが後から入るので変更通知あり／`AudioLibrary.Scan(folder)`＝深さ3・`_rec_*`除外・新しい順）、`Core/MediaInfo.cs`（`ParseDuration(text)`／`Get(path)`＝`ffmpeg -i` の Duration を読む。パス+サイズ+更新時刻でメモリにキャッシュ）、`UI/FileListPanel.xaml(.cs)`＋`.Actions.cs`（`Init(settings,folderFunc,ffmpegPath)`／`Refresh(keepPath?)`、イベント `Selected`（ちょうど1つ選んだとき）`PlayRequested`・`Releasing`）。見出しクリックで並べ替え。
  - 再生: `Core/PlaybackArgs.cs`（`Build(path,startSec,speed)`／`ClampSpeed`。`atempo`、PCM 44.1kHz/16bit/2ch）、`Core/Win/FfmpegPlayer.cs`（`Play(path,startSec,speed)`／`Stop()`／`Position`（元ファイル上の秒）／`IsPlaying`、`Ended`・`Failed`。FFmpeg→パイプ→`WaveOutEvent`。止める順は FFmpeg を先に Kill→出力を止める）、`UI/PlayerPanel.xaml(.cs)`（`Init(ffmpegPath)`／`Load(file)`／`Play()`／`Release(paths)`／`Shutdown()`）。
  - 名前の変更・削除: `Core/FileOps.cs`（`PlanRename(path,typed,out error)`／`Rename(path,target)`）、`UI/RenameDialog.xaml(.cs)`（`Target`）、`Core/Win/RecycleBin.cs`（`Send(paths)`＝失敗したものを返す）。
  - 画面: `MainWindow.xaml` の行を整理（中段は Auto・波形 Height 90、最下段 `*` に `Files`＋`Player`）。ダーク用に `UI/Theme/DarkList.xaml`（ListView・行・見出し）を追加し `ThemeManager` が読み込む。
  - 据え置き・未実装: 長さの永続キャッシュ、再生速度・列幅の保存、一覧のフォルダ列、録音中に再生した音が（PC音の録音では）録音に入ること（再生を止める等の制御はしていない）。FFmpeg死亡時の自動再開（§5-5）は引き続き据え置き。
- 次にやること: 新規チャットで「今回はM7」（トレイ常駐・最小化録音・ホットキー・予約録音・無音検出・デバイス待機/復帰分割）。**まずM6の実機確認**（チェックリスト5項目）で出た不具合を直す。M7の開始時に確認すること＝オプション欄（`ExpOption`）に置く項目と、メジャー／マイナーの振り分け。未確認の既知事項: `_rec_` 仮ファイルの残り（再発したらファイル名を連絡）、`.aac` の長さ表示は仕様上ずれる（m4a推奨）、M3追加5のEnter確定の実機確認、M4の4（デバイスの抜き差し）と5（長時間）はM9、wavの復旧は実機未確認（M5）。**M6確認後の修正（左右分割の見た目・左側の幅・色分け・録音中の表示）は実機で未確認。**
- ファイル一覧とインターフェース（M2で変更・追加のもの＋据え置き）:
  - `.github/workflows/build.yml` … windows-latest。publish → FFmpeg取得 → `libmp3lame`/`aac`/`flac` 検査 → `dist/LokuOnX/` を組む → artifact（フォルダ）→ タグ時のみZIP作成とRelease添付
  - `src/LokuOnX.csproj`、`src/app.manifest`、`src/App.xaml(.cs)`、`src/Core/AppInfo.cs`、`AppPaths.cs`、`InstanceGuard.cs`、`PeakMeter.cs` … M1のまま
  - `src/Core/IAudioSource.cs` … `AudioDeviceInfo(Key,Label,IsLoopback,SampleRate,Channels)`、`AudioFormatInfo(SampleRate,Channels,BitsPerSample,IsFloat)`、`IAudioSource`、`IAudioDeviceProvider`（`List()`/`DefaultCaptureKey()`/`Create(key)`）。Key は `capture:{ID}` または `render:{ID}`
  - `src/Core/Win/WasapiDeviceProvider.cs`、`WasapiSource.cs` … NAudio(WASAPI)実装（Windows専用）。`Stop()` はUIスレッドで直接呼ばず `Task.Run` 経由。M4で `IAudioDeviceProvider` に `DefaultRenderKey()`/`Signature()` を追加、一覧の先頭に擬似デバイス（`AudioKeys.CaptureDefault`/`RenderDefault`）
  - （M4）`src/Core/AudioKeys.cs`（`IsDefault`/`IsLoopback`/`Resolve`）、`DeviceInput.cs`（`Start()`/`Stop()`/`Stream`/`Notice`/`Failed`）、`DeviceStream.cs`（`Push`/`MixInto`/`SetGainDb`/`TakePeak`）、`MixedSource.cs`（`MixedSource(provider,MixInput[],devices,autoResume)`、`IAudioSource`、`SetGain`/`TakeDevicePeaks`）、`RecordingGroup.cs`（`Create(provider,settings,inputs,devices,ffmpegPath,now)`、`Start`/`Pause`/`Resume`/`Split`/`Stop`/`SetGain`/`TakePeak`/`TakeDevicePeaks`/`OutputText`/`Results`/`Seconds`/`Failure`、`Error`/`Notice`）、`FloatRing.cs`、`PcmConvert.cs`、`StreamResampler.cs`、`Settings.Devices.cs`（`Inputs`/`SeparateFiles`/`AutoResume`/`InputSetting`）
  - （M4）`src/UI/DeviceList.xaml(.cs)`（`Init`/`Reload`/`Inputs`/`NameLabel`/`SetRecording`/`SetLevels`、`Changed`/`GainChanged`）、`src/UI/WaveView.cs`（`SetRows`/`Push`）
  - `src/Core/OutputFormat.cs` … `OutputFormat(Key,Label,Ext,Muxer,Lossy,MaxSampleRate,MaxChannels)`、`All`、`Get(key)`
  - `src/Core/EncodeOptions.cs` … `EncodeOptions(Format,BitrateKbps,SampleRate,Channels)`、`Fmt`、`Resolve(deviceRate,deviceCh)`、`Bitrates`/`SampleRates`
  - `src/Core/FfmpegArgs.cs` … `Build(options,inputFormat,outputPath)`
  - `src/Core/FfmpegEncoder.cs` … `FfmpegEncoder(ffmpegPath,outputPath,EncodeOptions,AudioFormatInfo)`、`Start()`/`Write(byte[])`/`Finish()`/`Failure`/`Failed`。専用スレッド＋上限付きキュー
  - `src/Core/SilenceFiller.cs` … `Feed(buf,count)`/`FillIdle()`/`Finish()`
  - `src/Core/RecordingSession.cs`（＋`.Split.cs`） … `RecordingSession(source,ffmpegPath,RecordingPlan,splitInterval)`、`Start()`/`Stop()`/`Pause()`/`Resume()`/`Split()`、`Elapsed`/`Paused`/`CurrentTrackNumber`/`TakePeak()`/`OutputPath`（仮名）/`Results`/`Failure`、イベント `Error`/`Notice`。`Stop()`/`Split()` はUIスレッドで直接呼ばず `Task.Run` 経由
  - `src/Core/FileNamer.cs` … `NameContext(Time,No,Track,Start,End,Device,Preset,Ext)`、`Expand`/`FileName`/`Folder`/`Sanitize`/`CleanDevice`/`TempName`/`Unique`、`DefaultPattern`/`DefaultDateFormat`
  - `src/Core/NameTemplates.cs` … `NameTemplate(Name,Pattern)`、`NameTemplates.Defaults`
  - `src/Core/RecordingPlan.cs` … `Create(settings,deviceLabel,now)`/`PreviewFolder(settings,now)`、`TempPathFor(index)`/`TrackNumber(index)`/`FinishTrack(tempPath,index,start,end,ffmpegPath)`、`Folder`/`Options`、`PresetName`
  - `src/Core/FileFinalizer.cs` … `Run(tempPath,folder,fileName,options,ffmpegPath)` → `FinalizeResult(Path,Warning)`。m4aの整形もここ
  - `src/Core/IniFile.cs` … `Get`/`Set`/`Remove`/`Save`（一時ファイル→置換）
  - `src/Core/Settings.cs` … `Device`/`Format`/`BitrateKbps`/`SampleRate`/`Channels`/`Encode`/`Folder`/`NextNo`。`ReadOnly` なら書かない
  - `src/Core/Settings.Naming.cs` … `Templates`/`SaveTemplate`/`DeleteTemplate`/`RestoreDefaultTemplates`/`Template`/`Pattern`/`DateFolder`/`DateFolderFormat`/`TrackStart`。ini のセクション `[Templates]`（`Count`,`Name<i>`,`Pattern<i>`）
  - `src/Core/Splitter.cs` … `Splitter(interval)`、`Due`/`Rolled`/`Retry`/`TrackStart`/`NextSplitAt`、`Floor`、`MaxLength`（24h）。純粋ロジック
  - `src/Core/Settings.Split.cs` … `SplitMinutes`（0〜1440）
  - `src/Core/Recovery.cs` … `Find(baseFolder)`/`Repair(files,ffmpegPath)`
  - `src/UI/MainWindow.xaml(.cs)`（＋`MainWindow.Recording.cs`） … 上段=常時表示の設定（入力・形式音質・保存先・自動分割・名前の例）、詳細設定Expander（`DetailPanel`）、オプションExpander（中身はM7以降）、中段=経過時間・メーター、録音/一時停止/分割/停止ボタン、下段=状態表示
  - `src/UI/DetailPanel.xaml(.cs)` … 音質・ファイル名テンプレート・番号・日付フォルダ。`Init(settings,deviceLabel)`/`Reload()`/`Refresh()`/`Changed`
  - `src/UI/QualityBar.xaml(.cs)` … 形式の横のビットレート・周波数・チャンネル。`src/UI/RecoveryPrompt.cs` … `RunAsync(owner,settings,busy,status)`。（`SummaryBar.cs` は廃止）
  - `src/UI/VariableEditor.xaml(.cs)` … `VariableEditor(title, initial, vars, preview)`、`Result`。`src/UI/VariableHelp.cs` … `VarInfo(Token,Description)`、`VariableHelp.Pattern`/`Date`
  - （M5）`src/Core/TagInfo.cs`、`PresetData.cs`、`Settings.Extras.cs`（`Compression`/`Tags`/`CurrentPreset`/`Theme`/`PreventSleep`）、`Settings.Presets.cs`、`DiskSpace.cs`（`FreeBytes`/`WarnBytes`/`StopBytes`）、`Win/SleepGuard.cs`（`Prevent(bool)`）
  - （M5）`src/UI/PresetBar.xaml(.cs)`（`Init`/`Reload`、`Applied`/`Acting`/`Error`、`Prepare`）、`TagPanel.xaml(.cs)`（`Init`/`Reload`/`Commit`）、`ThemeManager.cs`（`Apply(mode)`/`Attach(window,mode)`）、`Theme/Light.xaml`・`Dark.xaml`・`DarkControls.xaml`
  - （M6）`src/Core/LibraryFile.cs`・`MediaInfo.cs`・`PlaybackArgs.cs`・`FileOps.cs`・`Win/FfmpegPlayer.cs`・`Win/RecycleBin.cs`／`src/UI/FileListPanel.xaml(.cs)`・`FileListPanel.Actions.cs`・`PlayerPanel.xaml(.cs)`・`RenameDialog.xaml(.cs)`・`Theme/DarkList.xaml`（詳細は上の「M6で入れたもの」）
  - `assets/icon/LokuOnX.ico` … 仮アイコン。同名で上書きすれば差し替え可

## 付録A. 固定プロンプト（各チャットの最初に貼る）
```
あなたはLokuOnXの開発担当です。ProjectのPROJECT.mdが仕様です（チャット内の記憶より優先）。
- 今回のマイルストーンは「M◯」だけ。範囲外は作らない。
- 開始時の未確定事項は、まとめて1回だけ質問。以降は推奨案で進めて最後に1行で報告。
- 触るファイルだけ読む（リポジトリに入れるなら直接、入れないなら私が添付する）。手元に無いファイルを推測で書かない。
- 渡す前に可能ならLinuxでビルド確認し、できなかった場合はそう書く。
- 変更ファイルは全文で出し、ファイル名を明記。説明や計画は書かない。
- 仕様が変わったらPROJECT.mdを更新してProjectに書き戻す。
- 最後に動作確認チェックリスト（5項目以内）と、更新後の§11を出す。
```

## 付録B. 動作確認の報告書式
```
M◯の確認結果
□ 起動した  □ 録音できた  □ 再生できた  □ ファイル名が想定どおり
問題: （あれば。エラーは error の行だけ貼る）
```
