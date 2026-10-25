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
3. VRChat のアバターメニュー（OSC）でオン/オフする。設定は `%APPDATA%\ViolinPassthrough\config.json`。

| オプション | 意味 |
|---|---|
| `--console` | ログをコンソール窓にも出す（通常は窓を出さず、`%APPDATA%\ViolinPassthrough\logs\` にだけ書く） |
| `--config PATH` | 設定ファイルの場所を変える |
| `--unregister` | SteamVR の自動起動とマニフェストの登録を解除して終了する（SteamVR の起動が必要） |
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
| `regions[].anchor` | `{ "type": "hmd" }`（HMD 基準） |
| `regions[].shape` | `{ "type": "ellipse" }` / `{ "type": "roundedRect", "cornerRadius": 0.05 }`（メートル） |
| `regions[].position` | アンカー基準の板の中心 `[x, y, z]`（メートル。+X 右、+Y 上、-Z 前） |
| `regions[].rotationEulerDeg` | `[pitch, yaw, roll]`（度）。pitch 正で板が上を向く、yaw 正で板が右を向く、roll 正で板の右端が上がる。回す順番は roll → pitch → yaw |
| `regions[].widthMeters` / `heightMeters` | 形状の幅・高さ（メートル） |
| `regions[].marginMeters` | 縁に足す余白。板の実寸は `幅 + 2×余白` |
| `osc.useOscQuery` | OSCQuery でサービスを広告して受信する（推奨）。false なら `fallbackListenPort` で待ち受ける |
| `osc.fallbackListenPort` | OSCQuery を使わない（使えない）ときの受信ポート。VRChat の送信先をこのポートにする必要がある |
| `osc.vrchatSendHost` / `vrchatSendPort` | 状態を送り返す先（VRChat の受信ポート） |
| `osc.parameters.*` | アバターパラメータ名（`enabled` = Bool、`preset` = Int、`debug` = Bool）。変えると OSC を再起動する |
| `logLevel` | `Debug` / `Info` / `Warning` / `Error` |

## OSC（VRChat のアバターメニューで切り替え）

| アドレス | 型 | 動作 |
|---|---|---|
| `/avatar/parameters/HandPassthrough` | Bool | 有効/無効 |
| `/avatar/parameters/HandPassthroughPreset` | Int | プリセット番号（`presets` に書いた順。0 = fingerboard, 1 = whole） |
| `/avatar/parameters/HandPassthroughDebug` | Bool | デバッグ表示 |
| `/avatar/change` | String | アバター変更。状態は維持し、2 秒間は新しいアバターの既定値を無視してから、現在の状態を送り直す |

起動時・OSC 設定の変更時・アバター変更時、それと設定ファイルで状態が変わったときに、現在の状態を VRChat（既定 127.0.0.1:9000）に送ってメニューの表示を合わせる。
Bool のパラメータを Int / Float で作ってしまっても受け取れる（0.5 以上 / 0 以外を true とみなす）。

アバター側の設定は DESIGN.md §9 を参照（3つのパラメータを **Synced を外して** Expression Parameters に追加し、メニューに Toggle を置く。アニメーターの変更は不要）。

## 依存ライブラリ

| ライブラリ | バージョン | 取得元 |
|---|---|---|
| VRChat.OSCQuery（NuGet） | 0.0.7 | [vrchat-community/vrc-oscquery-lib](https://github.com/vrchat-community/vrc-oscquery-lib)。OSCQuery の広告（mDNS）と HTTP サーバー |
| OpenVR (`openvr_api.cs`, `openvr_api.dll` win64) | **v2.15.6**（SDK 2.15.6） | [ValveSoftware/openvr](https://github.com/ValveSoftware/openvr) のタグ `v2.15.6`。`third_party/openvr/` に同梱。ライセンスは `third_party/openvr/LICENSE`（BSD-3-Clause） |

`openvr_api.dll` の SHA-256: `bab8ac6ef64e68a9ca53315b0014d131088584b2efdfa6db511d67ec03cfcb4a`

## 構成

```
src/            本体（DESIGN.md の「モジュール構成」に対応）
tests/          単体テスト（xUnit）
resources/      マニフェストと既定の設定
third_party/    OpenVR の公式バインディング
```
