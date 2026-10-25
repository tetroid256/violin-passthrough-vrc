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
