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
| `regions[].rotationEulerDeg` | `[pitch, yaw, roll]`（度）。yaw 正で板が右を向く。pitch 正で板の上端が手前に倒れる。roll 正で板の右端が上がる |
| `regions[].widthMeters` / `heightMeters` | 形状の幅・高さ（メートル） |
| `regions[].marginMeters` | 縁に足す余白。板の実寸は `幅 + 2×余白` |
| `osc.*` | OSC の設定（Phase 3） |
| `logLevel` | `Debug` / `Info` / `Warning` / `Error` |

## 依存ライブラリ

| ライブラリ | バージョン | 取得元 |
|---|---|---|
| OpenVR (`openvr_api.cs`, `openvr_api.dll` win64) | **v2.15.6**（SDK 2.15.6） | [ValveSoftware/openvr](https://github.com/ValveSoftware/openvr) のタグ `v2.15.6`。`third_party/openvr/` に同梱。ライセンスは `third_party/openvr/LICENSE`（BSD-3-Clause） |

`openvr_api.dll` の SHA-256: `bab8ac6ef64e68a9ca53315b0014d131088584b2efdfa6db511d67ec03cfcb4a`

## 構成

```
src/            本体（DESIGN.md の「モジュール構成」に対応）
tests/          単体テスト（xUnit）
resources/      マニフェストと既定の設定
third_party/    OpenVR の公式バインディング
```
