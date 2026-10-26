# Violin Passthrough

Quest 3 + Virtual Desktop + SteamVR + VRChat で、バイオリンの手元だけを外界映像で見えるようにする SteamVR オーバーレイアプリ。

仕組みと仕様は [DESIGN.md](DESIGN.md)、実機での確認手順は [docs/VERIFICATION.md](docs/VERIFICATION.md) を参照。

## ビルド

### GitHub Actions（推奨）

push するたびに `windows-latest` でテストとビルドが走り、`ViolinPassthrough-win-x64` という Artifact ができる（保持期間 14 日）。
Actions タブ → 該当の実行 → Artifacts からダウンロードして展開する。self-contained なので .NET のインストールは不要。

### ローカル（Windows）

.NET 10 SDK を入れて、リポジトリのルートで実行する。

```powershell
dotnet test ViolinPassthrough.sln
dotnet publish src/ViolinPassthrough.csproj -c Release -r win-x64 --self-contained -o publish
```

`publish/ViolinPassthrough.exe` ができる。

## 使い方

1. ビルドしたフォルダを、ずっと置いておく場所に置く（例: `C:\Tools\ViolinPassthrough`）。
2. SteamVR を起動した状態で `ViolinPassthrough.exe` を一度起動する。SteamVR に登録され、次回から SteamVR と一緒に自動で起動・終了する。
3. VRChat のアバターメニュー（OSC）でオン/オフする。設定は `%APPDATA%\ViolinPassthrough\config.json`。アバターへのメニューの追加は、Modular Avatar を使うとエディタ拡張でまとめてできる（[docs/AVATAR_SETUP.md](docs/AVATAR_SETUP.md)）。

| オプション | 意味 |
|---|---|
| `--console` | ログをコンソール窓にも出す（通常は窓を出さず、`%APPDATA%\ViolinPassthrough\logs\` にだけ書く） |
| `--config PATH` | 設定ファイルの場所を変える |
| `--unregister` | SteamVR の自動起動とマニフェストの登録を解除して終了する（SteamVR の起動が必要。本アプリの起動中でも実行できる） |
| `--help` | オプションの説明 |

- SteamVR が起動していないときに起動すると、SteamVR が起動するまで待つ（SteamVR を勝手に起動しない）。
- 多重起動はしない（2つ目はすぐ終了する）。
- フォルダを移動したら、移動先で一度起動すると登録し直す。

## 設定

`%APPDATA%\ViolinPassthrough\config.json`（初回起動時に `resources/config.default.json` がコピーされる）。保存すると1秒以内に反映される。
不正な JSON や範囲外の値のときは前回の正常な設定を使い続け、ログに理由を出す。`//` コメントと末尾のカンマは使ってよい。知らない項目名はエラーになる（打ち間違い防止）。

| 項目 | 意味 |
|---|---|
| `keyColor` | キー色（0〜255）。VD の VR Passthrough の色と完全に一致させる |
| `edgeMode` | `hard`（縁にアンチエイリアスなし）/ `aa`（約 1.5px） |
| `textureLongSide` | テクスチャの長辺の画素数（16〜2048） |
| `sortOrder` | オーバーレイの重なり順（大きいほど手前） |
| `startEnabled` | 起動時に有効にするか（起動時だけ読む） |
| `debugVisible` | デバッグ表示（半透明の赤＋白枠。抜けない）。デバッグ表示中は無効でもマスクを表示する。保存時に値が変わったら反映 |
| `activePreset` | 起動時のプリセット。保存時に値が変わったら反映 |
| `presets` | プリセット名 → 表示する領域 ID の配列。書いた順番が OSC の番号（0, 1, ...）になる |
| `regions[].id` | 領域 ID（英数字・`_`・`-`、64 文字まで） |
| `regions[].anchor` | 基準座標系。下の「アンカー」参照 |
| `regions[].shape` | `{ "type": "ellipse" }` / `{ "type": "roundedRect", "cornerRadius": 0.05 }`（メートル）/ `{ "type": "polygon", "vertices": [[x, y], ...] }`（下の「多角形」参照） |
| `regions[].position` | アンカー基準の板の中心 `[x, y, z]`（メートル。HMD 基準なら +X 右、+Y 上、-Z 前） |
| `regions[].rotationEulerDeg` | `[pitch, yaw, roll]`（度）。pitch 正で板が上を向く、yaw 正で板が右を向く、roll 正で板の右端が上がる。回す順番は roll → pitch → yaw |
| `regions[].widthMeters` / `heightMeters` | 形状の幅・高さ（メートル） |
| `regions[].marginMeters` | 縁に足す余白。板の実寸は `幅 + 2×余白` |
| `osc.useOscQuery` | OSCQuery でサービスを広告して受信する（推奨）。false なら `fallbackListenPort` で待ち受ける |
| `osc.fallbackListenPort` | OSCQuery を使わない（使えない）ときの受信ポート。VRChat の送信先をこのポートにする必要がある |
| `osc.vrchatSendHost` / `vrchatSendPort` | 状態を送り返す先（VRChat の受信ポート） |
| `osc.parameters.*` | アバターパラメータ名（`enabled` = Bool、`preset` = Int、`debug` = Bool）。変えると OSC を再起動する |
| `logLevel` | `Debug` / `Info` / `Warning` / `Error` |

### アンカー

| 設定 | 意味 |
|---|---|
| `{ "type": "hmd" }` | HMD 基準（既定）。頭に固定 |
| `{ "type": "world", "universe": "standing" }` | ルーム空間に固定。`position` / `rotationEulerDeg` はプレイエリアの座標（standing は床の中心が原点、seated は着座位置が原点） |
| `{ "type": "device", "role": "leftHand", "onLost": "hold" }` | 手に追従。`role` は `leftHand` / `rightHand`。ハンドトラッキングでは VD がエミュレートするコントローラーの姿勢になる |
| `{ "type": "device", "serial": "LHR-XXXXXXXX", "onLost": "hide" }` | トラッカーなどにシリアル番号で追従。シリアル番号は起動時（とデバイスの接続が変わったとき）のログの `Tracked devices:` に出る |

`onLost`: デバイスを見失ったとき `hold`（既定。最後に見えていた場所に置いておく）/ `hide`（隠す）。一度も見えていないときは、どちらでも隠す。

### 多角形

`vertices` は板の幅・高さに対する割合で書く。中心が `[0, 0]`、左下が `[-0.5, -0.5]`、右上が `[0.5, 0.5]`（+x 右、+y 上）。3〜128 点。自己交差する形（8 の字など）は偶奇規則で塗る。余白（`marginMeters`）は多角形の外側に一定の幅で足す（角は丸くなる）。
例: 左下から右上へ斜めに長い形 `[[-0.5, -0.5], [-0.2, -0.5], [0.5, 0.3], [0.5, 0.5], [0.2, 0.5], [-0.5, -0.3]]`

## 演奏のための機能（Phase 6）

楽器と弓で両手がふさがっていても使えるようにする機能。詳しくは DESIGN.md §15。

### キャリブレーション（構えて窓を合わせる）
1. アバターメニューの **HandPassthroughCalibrate** を ON にする。ビープ音と視界の文字（`CALIBRATE 5` … `1`）でカウントダウンが始まる。
2. その間に演奏の姿勢をとる: **左手は 1st ポジション、右手は弓の元（フロッグ）を駒の近くの弦に置く**。
3. 最後の 1 秒の両手の位置の平均から（その間に一瞬でも見えていればよい。カウントダウンが 1 秒なら最後の 0.5 秒）、`calibration.regionId`（既定 `fingerboard`）の窓の位置・角度・長さを計算して config.json に書き込む。窓は「目から見て」両手を覆うように置く（近い弓の手も、遠い左手も窓に入る）。直前の内容は `config.json.bak`、最初の内容（コメント入りの手書きのもの）は `config.json.orig` に残る。書き込んだ config.json からは JSON のコメントが消える。設定ファイルにエラーがあるときは書き込まない（`CAL FAIL: FIX CONFIG FIRST`）。
4. `CALIBRATED` と出て、5 秒間、新しい窓が赤いデバッグ表示で見える。失敗したら `CAL FAIL: ...` と理由が出て、設定は変わらない。
5. カウントダウン中にメニューを OFF にすると取り消す。

窓の高さ・形・余白は変えない（必要なら手で直す）。窓を長めにしたいときは `calibration.padMeters`（両端に足す長さ）を増やす。

### 自動モード（構えたときだけ表示）
アバターメニューの **HandPassthroughAuto** を ON にすると、HandPassthrough が ON でも、**楽器を構えているときだけ** 窓を出す。楽器を下ろして 2 秒たつと消え、構えて 0.3 秒たつと出る。
- 判定: HMD から見た左手が `auto.leftHandPosition` から `auto.radiusMeters`（0.15 m）以内。キャリブレーションでこの位置も記録される。
- ハンドトラッキングで左手を見失いやすいときは、`"useHeadTilt": true` にすると頭の傾き（顎当てに顎を乗せた姿勢）でも判定できる（キャリブレーションを一度してから使う）。`useLeftHand` と両方 true なら両方を満たすときで、左手を見失っている間は頭の傾きだけで決める。
- 姿勢が取れない間は直前の判定を保つ（演奏中に手を見失っても急に消えない）。ただし、楽器を下ろし始めてから手を見失ったときは、2 秒たてば消える。

### 視界の通知
カウントダウンと結果のほか、`AUTO ON` / `AUTO OFF`、`PRESET ...`、自動モード中の `ON - RAISE TO SHOW`、設定ファイルの `CONFIG RELOADED` / `CONFIG ERROR`（理由はログ） を視界の少し上に短く出す。結果以外を出したくなければ `"feedback": { "toasts": false }`。

| 設定 | 既定 | 意味 |
|---|---|---|
| `auto.enabled` | false | 起動時に自動モードにするか（保存時に値が変わったら反映） |
| `auto.useLeftHand` / `auto.useHeadTilt` | true / false | 判定に使う条件 |
| `auto.leftHandPosition` | [-0.35, -0.10, -0.35] | 構えたときの左手（HMD 基準）。キャリブレーションで書き込む |
| `auto.headUp` | なし | 構えたときの頭の傾き。キャリブレーションで書き込む |
| `auto.radiusMeters` / `auto.headTiltToleranceDeg` | 0.15 / 15 | 構えているとみなす範囲 |
| `auto.enterDelaySeconds` / `auto.exitDelaySeconds` | 0.3 / 2.0 | 表示する・隠すまでの待ち時間 |
| `auto.deviceSerial` | null | 左手の代わりに判定に使うデバイス（楽器に付けたトラッカー）のシリアル番号 |
| `calibration.regionId` | fingerboard | 置き直す領域（HMD 基準かデバイス基準のもの） |
| `calibration.countdownSeconds` | 5 | カウントダウンの秒数 |
| `calibration.padMeters` | 0.05 | 両手の外側に足す長さ（片側） |
| `calibration.previewSeconds` | 5 | 結果を赤く見せる時間 |
| `calibration.beep` | true | ビープ音（PC の既定の音声出力。VD 経由でヘッドセットに聞こえる） |
| `feedback.toasts` | true | 結果以外の通知も出すか |
| `feedback.position` / `feedback.textHeightMeters` | [0, 0.08, -0.5] / 0.015 | 通知の位置（HMD 基準）と文字の高さ |
| `tracking.leftHandSerial` / `tracking.rightHandSerial` | null / null | 自動モードとキャリブレーションで、手の代わりに使うデバイス（手首に付けたトラッカー）のシリアル番号 |

## 左手のトラッキングが外れるとき（Phase 7）

指板を押さえる左手は、ハンドトラッキングが外れやすい。対策は [docs/TRACKING.md](docs/TRACKING.md) にまとめた。要点:
- キャリブレーションは最後の 1 秒の平均を使うので、一瞬外れても失敗しない（設定不要）。
- 機材なしなら、自動モードを頭の傾きで判定する（`"auto": { "useLeftHand": false, "useHeadTilt": true }`）。
- 楽器に SteamVR のトラッカーを付けたら `auto.deviceSerial` に、手首に付けたら `tracking.leftHandSerial` にシリアル番号を書く（番号は `--console` のログの `Tracked devices:` に出る）。窓を楽器のトラッカーに追従させて（`"anchor": { "type": "device", "serial": "..." }`）キャリブレーションすることもできる。
- 見つからないときは視界に `NO LEFT TRACKER` / `NO AUTO TRACKER` と出る。
- VRChat のアバターの手が飛ぶ問題は、本アプリでは直せない（docs/TRACKING.md の 4）。

## OSC（VRChat のアバターメニューで切り替え）

| アドレス | 型 | 動作 |
|---|---|---|
| `/avatar/parameters/HandPassthrough` | Bool | 有効/無効 |
| `/avatar/parameters/HandPassthroughPreset` | Int | プリセット番号（`presets` に書いた順。0 = fingerboard, 1 = whole） |
| `/avatar/parameters/HandPassthroughDebug` | Bool | デバッグ表示 |
| `/avatar/parameters/HandPassthroughAuto` | Bool | 自動モード（構えたときだけ表示） |
| `/avatar/parameters/HandPassthroughCalibrate` | Bool | true でキャリブレーションを始める、カウントダウン中に false で取り消す。終わると本アプリが false に戻す |
| `/avatar/change` | String | アバター変更。状態は維持し、2 秒間は新しいアバターの既定値を無視してから、現在の状態を送り直す |

起動時・OSC 設定の変更時・アバター変更時、それと設定ファイルで状態が変わったときに、現在の状態を VRChat（既定 127.0.0.1:9000）に送ってメニューの表示を合わせる。
Bool のパラメータを Int / Float で作ってしまっても受け取れる（Int は 0 以外、Float は 0.5 以上を true とみなす）。

アバター側の設定は [docs/AVATAR_SETUP.md](docs/AVATAR_SETUP.md) を参照。Modular Avatar を使うなら、`unity/com.tetroid256.violin-passthrough/` のエディタ拡張でパラメータとメニューをまとめて追加できる。手で設定する場合は、5つのパラメータを **Synced を外して** Expression Parameters に追加し、メニューに Toggle を置く（DESIGN.md §9 と §15.5。アニメーターの変更は不要）。

## 依存ライブラリ

| ライブラリ | バージョン | 取得元 |
|---|---|---|
| VRChat.OSCQuery（NuGet） | 0.0.7 | [vrchat-community/vrc-oscquery-lib](https://github.com/vrchat-community/vrc-oscquery-lib)。OSCQuery の広告（mDNS）と HTTP サーバー |
| OpenVR (`openvr_api.cs`, `openvr_api.dll` win64) | **v2.15.6**（SDK 2.15.6） | [ValveSoftware/openvr](https://github.com/ValveSoftware/openvr) のタグ `v2.15.6`。`third_party/openvr/` に同梱。ライセンスは `third_party/openvr/LICENSE`（BSD-3-Clause） |

`openvr_api.dll` の SHA-256: `bab8ac6ef64e68a9ca53315b0014d131088584b2efdfa6db511d67ec03cfcb4a`

## 構成

```
src/
  Program.cs              エントリポイント（コマンドライン、多重起動防止、ログ、組み立て）
  App/AppLifecycle.cs     OpenVR の初期化・イベント・終了・マニフェスト登録・トラッキング情報
  App/MainLoop.cs         約 30Hz の更新ループ（設定・OSC・状態をオーバーレイに反映）
  Config/                 設定のデータモデル・検証・ホットリロード
  Mask/                   形状・ラスタライズ・オーバーレイ（1領域 = 1 OpenVR Overlay）
  Anchor/                 HMD / World / Device アンカーと姿勢の計算
  Osc/                    OSC のエンコード/デコード、OSCQuery、パラメータの対応付け
  State/                  現在の状態（有効/無効、プリセット、デバッグ表示、自動モード）
  Calibration/            手の位置からのキャリブレーション（計算とカウントダウン）
  Auto/                   構えているかの判定
  Feedback/               視界の通知（5×7 ドットのフォント）とビープ音
  Logging/                ログ（コンソール、日付ごとのファイル）
tests/                    単体テスト（xUnit）
resources/                manifest.vrmanifest と config.default.json
third_party/openvr/       OpenVR の公式バインディング
unity/                    Unity のエディタ拡張（Modular Avatar でアバターにメニューを追加する）
docs/VERIFICATION.md      実機での確認手順
docs/AVATAR_SETUP.md      アバターへのメニューの追加（Modular Avatar / 手作業）
docs/TRACKING.md          左手のトラッキングが外れるときの対策
```

OpenVR の API を呼ぶのは `AppLifecycle` と `MaskOverlay` だけ（DESIGN.md §4, §14）。それ以外はテストで偽物に差し替えて確認している。
