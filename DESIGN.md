# ViolinPassthrough 設計書

Quest 3 + Virtual Desktop + SteamVR + VRChat 環境で、バイオリンの手元（左手〜指板〜駒）だけを外界映像で見えるようにする SteamVR オーバーレイアプリ。

この文書は Claude Code に実装させるための設計書です。実装時はこの文書を正とし、判断が必要な箇所は「未決事項」に追記してください。

---

## 1. 目的と前提

### 目的
- VRChat をプレイしたまま、バイオリンの手元だけを現実の映像で見る。
- 範囲は「左手＋指板〜駒」。将来的に楽器全体、さらに複数領域へ拡張できる構造にする。
- VRChat のアバターメニュー（OSC）でオン/オフを切り替える。
- SteamVR 起動時に自動で立ち上がる。配布はしない（個人用）。

### 前提環境
- ヘッドセット: Meta Quest 3
- PC 接続: Virtual Desktop（VD）経由で SteamVR
- 常用オーバーレイ: XSOverlay、OVR Advanced Settings（OVRAS）
- 入力: Quest のハンドトラッキング（VD 経由でコントローラーとしてエミュレートされる）

### 技術的な前提（重要）
- PC 側から Quest 3 のカメラ映像は取得できない。
- VD には「VR Passthrough」機能（Streaming タブ）があり、指定した色（クロマキー）の画素をヘッドセット側でパススルー映像に置き換える。
- よって本アプリは **映像処理をせず、「抜き色で塗った板」を頭に固定して表示するだけ** でよい。外界の映像合成は VD が行う。

---

## 2. 方式の全体像

```
[VRChat] ──描画──┐
                  ├─▶ [SteamVR Compositor] ──映像──▶ [VD Streamer] ──▶ [Quest 3 / VD Client]
[本アプリ]──Overlay┘     (抜き色の板を重ねる)                           抜き色の画素だけ
   ▲                                                                  パススルー映像に置換
   │ OSC (/avatar/parameters/...)
[VRChat アバターメニュー]
```

1. 本アプリは OpenVR Overlay を1つ（将来は複数）作成する。
2. 板は HMD 基準の固定位置に置き、マスク形状の内側をキー色（不透明）、外側を透明にしたテクスチャを貼る。
3. SteamVR が VRChat の映像にこの板を合成し、VD に送る。
4. VD クライアントがキー色の画素をパススルー映像に置き換える。
5. VRChat のアバターメニューのトグル → OSC → 本アプリが板の表示/非表示を切り替える。

### 先行事例
ハンド/コントローラー追従でクロマキー窓を出す SteamVR アプリ（ChromaTrackOverlay）が存在する。仕組みの検証用に試すのも有効。

---

## 3. 技術選定

| 項目 | 選定 | 理由 |
|---|---|---|
| 言語/ランタイム | C# / .NET 8（Windows, x64） | OSCQuery の公式ライブラリが C#。設定ファイルや非同期処理が書きやすい |
| OpenVR | Valve 公式 `openvr_api.cs` + `openvr_api.dll`（ValveSoftware/openvr の最新リリース） | 追加依存なし |
| テクスチャ転送 | `IVROverlay.SetOverlayRaw`（CPU バッファ） | マスクは静的で、パラメータ変更時だけ再生成すればよい。D3D11 コード不要 |
| OSC 受信 | OSCQuery（VRChat 公式 `vrc-oscquery-lib`）＋ UDP 受信 | XSOverlay など他の OSC アプリと 9001 番ポートの取り合いを避ける |
| OSC パース | 軽量ライブラリ、または自前の最小パーサ（`/avatar/parameters/*` の bool/int/float と `/avatar/change` のみ対応） | 必要な機能が少ない |
| 設定 | JSON ファイル＋ホットリロード（FileSystemWatcher） | VR 内で XSOverlay のデスクトップ窓から編集し、即座に反映できる |
| 実行形態 | WinExe（ウィンドウなし）＋ファイルログ | 自動起動時にコンソールが出ないように |

---

## 4. モジュール構成

```
ViolinPassthrough/
├─ src/
│  ├─ Program.cs                 エントリポイント。各モジュールの組み立てとメインループ
│  ├─ App/
│  │  ├─ AppLifecycle.cs         OpenVR 初期化、VREvent 処理、終了処理、マニフェスト登録
│  │  └─ MainLoop.cs             約 30Hz の更新ループ（イベント処理・状態反映）
│  ├─ Config/
│  │  ├─ AppConfig.cs            設定のデータモデル
│  │  └─ ConfigStore.cs          JSON 読み書き、ホットリロード、検証
│  ├─ Mask/
│  │  ├─ MaskRegion.cs           1つのマスク領域（形状・アンカー・姿勢・サイズ）
│  │  ├─ MaskShape.cs            形状の抽象（Ellipse / RoundedRect / Polygon）
│  │  ├─ MaskRasterizer.cs       形状 → RGBA バッファ生成
│  │  └─ MaskOverlay.cs          1領域 = 1 OpenVR Overlay のラッパー
│  ├─ Anchor/
│  │  ├─ IAnchor.cs              領域の基準座標系の抽象
│  │  ├─ HmdAnchor.cs            HMD 基準（MVP はこれだけ実装）
│  │  ├─ WorldAnchor.cs          （将来）ルーム空間に固定
│  │  └─ DeviceAnchor.cs         （将来）手・トラッカーに追従
│  ├─ Osc/
│  │  ├─ OscService.cs           OSCQuery 広告、UDP 受信、VRChat への送信
│  │  └─ OscBindings.cs          パラメータ名 → アクションの対応付け
│  └─ State/
│     └─ PassthroughState.cs     現在の状態（有効/無効、プリセット、デバッグ表示）
├─ resources/
│  ├─ manifest.vrmanifest
│  └─ config.default.json
└─ DESIGN.md
```

### 責務と依存の方向
- `State` が唯一の状態の持ち主。`Osc` と `Config` は `State` を更新するだけ。
- `MaskOverlay` は `State` と `MaskRegion` を見て、OpenVR に反映する。
- OpenVR API を呼ぶのは `AppLifecycle` と `MaskOverlay` だけ（テストしやすくするため）。

---

## 5. 拡張性の設計

将来「楽器全体」「複数の窓」「手に追従」に広げられるよう、次の2軸で抽象化する。

### 5.1 マスク領域は複数持てる
- 設定では `regions` を配列で持つ。MVP では 1 要素のみ使う。
- 1 領域 = 1 OpenVR Overlay。オーバーレイのキーは `violinpassthrough.region.<id>` とする。
- プリセット（例: `fingerboard` / `whole`）は、有効にする領域 ID の集合として定義する。

### 5.2 アンカーを差し替えられる
- `IAnchor` は「どのデバイス基準で、どの相対姿勢に置くか」を返す。
  - `HmdAnchor`: `SetOverlayTransformTrackedDeviceRelative(k_unTrackedDeviceIndex_Hmd, pose)` を使う。毎フレームの更新は不要。
  - `WorldAnchor`（将来）: `SetOverlayTransformAbsolute`。
  - `DeviceAnchor`（将来）: 手のコントローラーのインデックスを `GetTrackedDeviceIndexForControllerRole` で解決し、相対姿勢で固定する。ハンドトラッキングの場合、VD がエミュレートするコントローラーの姿勢になる。ロスト時の扱い（最後の姿勢で保持／非表示）を設定で選べるようにする。

### 5.3 形状を差し替えられる
- `Ellipse`（MVP の既定）、`RoundedRect`、`Polygon`（頂点リスト）を用意する。
- 楽器全体に広げるときは、`Polygon` で斜めに長い形を作るか、複数領域を組み合わせる。

---

## 6. マスクの描画仕様

### 座標系
OpenVR の HMD 座標系に従う。単位はメートル。+X が右、+Y が上、**-Z が前方**。

### テクスチャ
- サイズは長辺 512px（設定で変更可）。短辺は領域の縦横比に合わせる。
- 形状の内側はキー色で α=255、外側は α=0。
- 縁の処理は `edgeMode` で選ぶ。
  - `hard`（既定）: 縁にアンチエイリアスをかけない。中間色ができないので、キーの抜けが安定する。
  - `aa`: 1〜2px のアンチエイリアス。縁のギザギザは減るが、中間色の画素が抜けきらず縁取りが残る可能性がある。実機で比較する。
- 再生成は、設定変更時とプリセット変更時だけ行う。

### オーバーレイの設定
- `SetOverlayWidthInMeters(region.widthMeters)`
- `SetOverlayAlpha(1.0)`、`SetOverlayColor(1,1,1)`（色の乗算でキー色が変わらないように）
- `SetOverlaySortOrder(config.sortOrder)`（既定は高め。XSOverlay の窓が手前に来ると窓の部分は抜けない点に注意）
- 表示/非表示は `ShowOverlay` / `HideOverlay`

### 初期配置の目安（実機で調整する前提）
バイオリンは顎で挟むので、頭と楽器の位置関係がほぼ一定になる。そのため HMD 固定で十分な精度が期待できる。

| パラメータ | 初期値 | 意味 |
|---|---|---|
| position | (-0.15, -0.15, -0.40) | 視点から左下前方 |
| rotation（オイラー角, 度） | (pitch 15, yaw 20, roll -15) | 指板の方向に沿わせる |
| width × height | 0.45 m × 0.25 m | 駒〜左手をカバー |
| shape | Ellipse | |
| margin | 0.03 m | 縁に足す余白 |

### 視差について
楽器は奥行き方向に 0.15〜0.6m ほど広がっているが、マスクは1枚の平面である。左右の目でマスクの見える位置がずれるため、`margin` で少し大きめに取って吸収する。将来は奥行きの違う複数領域や、曲面の Polygon で改善できる。

### デバッグ表示
`debugVisible = true` のとき、キー色ではなく半透明の赤＋白い枠で描画する（抜けないので、マスクの位置を VR 内で確認できる）。OSC と設定の両方から切り替えられる。

---

## 7. 設定ファイル

場所: `%APPDATA%\ViolinPassthrough\config.json`（初回起動時に `config.default.json` をコピーする）

```json
{
  "keyColor": { "r": 255, "g": 0, "b": 255 },
  "edgeMode": "hard",
  "textureLongSide": 512,
  "sortOrder": 100,
  "startEnabled": false,
  "activePreset": "fingerboard",
  "presets": {
    "fingerboard": ["fingerboard"],
    "whole": ["fingerboard", "body"]
  },
  "regions": [
    {
      "id": "fingerboard",
      "anchor": { "type": "hmd" },
      "shape": { "type": "ellipse" },
      "position": [-0.15, -0.15, -0.40],
      "rotationEulerDeg": [15, 20, -15],
      "widthMeters": 0.45,
      "heightMeters": 0.25,
      "marginMeters": 0.03
    },
    {
      "id": "body",
      "anchor": { "type": "hmd" },
      "shape": { "type": "roundedRect", "cornerRadius": 0.05 },
      "position": [-0.05, -0.22, -0.30],
      "rotationEulerDeg": [30, 10, -15],
      "widthMeters": 0.30,
      "heightMeters": 0.20,
      "marginMeters": 0.03
    }
  ],
  "osc": {
    "useOscQuery": true,
    "fallbackListenPort": 9011,
    "vrchatSendHost": "127.0.0.1",
    "vrchatSendPort": 9000,
    "parameters": {
      "enabled": "HandPassthrough",
      "preset": "HandPassthroughPreset",
      "debug": "HandPassthroughDebug"
    }
  },
  "logLevel": "Info"
}
```

- キー色は VD 側の設定と完全に一致させる。VRChat のワールドに出てきにくい色（マゼンタ系など）を選ぶ。
- 設定が壊れていたら、前回の正常な設定を維持してログに警告を出す（クラッシュさせない）。

---

## 8. OSC 仕様

### 受信（VRChat → 本アプリ）
| アドレス | 型 | 動作 |
|---|---|---|
| `/avatar/parameters/HandPassthrough` | bool | 有効/無効 |
| `/avatar/parameters/HandPassthroughPreset` | int | 0 = fingerboard, 1 = whole（設定の presets の順番に対応） |
| `/avatar/parameters/HandPassthroughDebug` | bool | デバッグ表示 |
| `/avatar/change` | string | アバター変更。状態は維持し、現在値を VRChat に送り返す |

### 送信（本アプリ → VRChat, 127.0.0.1:9000）
- アバター変更時と起動時に、現在の状態を上記パラメータに送る。メニューの表示と実際の状態を一致させるため。

### OSCQuery
- `vrc-oscquery-lib` でサービスを広告し、空いている UDP ポートで受信する。VRChat は広告されたポートに送ってくるので、9001 を他アプリと奪い合わない。
- OSCQuery が使えない場合は `fallbackListenPort` で待ち受ける（その場合、VRChat 側の送信先をそのポートにする必要がある）。

---

## 9. VRChat アバター側の設定（ユーザー作業）

1. Expression Parameters に追加する。
   - `HandPassthrough`（Bool、既定 false、**Synced のチェックを外す**。ローカル専用なので同期ビットを消費しない）
   - `HandPassthroughPreset`（Int、既定 0、Synced を外す）
   - `HandPassthroughDebug`（Bool、既定 false、Synced を外す）
2. Expressions Menu に Toggle（HandPassthrough）、Toggle（Debug）、必要なら Radial/Sub Menu（Preset）を追加する。
3. アニメーターの変更は不要（パラメータを OSC で読むだけ）。
4. 使うアバターすべてに同じパラメータを入れる。
5. VRChat の Action Menu → Options → OSC → Enabled を ON にする。アバターを更新したら OSC の Reset Config を実行する。

---

## 10. 周辺アプリの設定（ユーザー作業）

### Virtual Desktop
- Streaming タブで VR Passthrough を有効にし、クロマキーの色を `keyColor` と一致させる。
- Similarity は低め、Smoothness は小さめから調整する。
- 映像のシャープネスなど、色や縁に影響する補正はオフにして比較する。
- コーデックは 10bit（HEVC 10bit / AV1 10bit）のほうが色ずれが少なく、キーが安定しやすい。

### OVR Advanced Settings
- 明るさや色の調整機能は、合成後の色を変えてしまいキーが抜けなくなる。使う場合はオフにして比較する。

### XSOverlay
- XSOverlay の窓がマスクの手前に重なると、その部分は抜けない。演奏中はマスクの範囲に窓を置かない。
- 本アプリの設定ファイルを XSOverlay のデスクトップ窓で編集すると、VR 内で位置を調整できる。

### ハンドトラッキングの誤入力
- 演奏中の指の形がピンチなどのジェスチャーとして誤認され、VRChat に意図しない入力が入る可能性がある。VD 側のハンドトラッキングのジェスチャー設定を確認する。これは本アプリの範囲外。

---

## 11. 起動と終了

- 初回起動時に `IVRApplications.AddApplicationManifest` で `manifest.vrmanifest` を登録し、`SetApplicationAutoLaunch(appKey, true)` を設定する。
- `VR_Init` は `EVRApplicationType.VRApplication_Overlay` で行う。
- SteamVR が起動していなければ待機して再試行する（手動起動にも対応）。
- `VREvent_Quit` を受けたら `AcknowledgeQuit_Exiting` を呼び、オーバーレイを破棄して終了する。
- 多重起動は名前付き Mutex で防ぐ。

`manifest.vrmanifest` の例:
```json
{
  "source": "builtin",
  "applications": [{
    "app_key": "local.violinpassthrough",
    "launch_type": "binary",
    "binary_path_windows": "ViolinPassthrough.exe",
    "is_dashboard_overlay": true,
    "strings": { "en_us": { "name": "Violin Passthrough", "description": "Chroma-key hand passthrough mask" } }
  }]
}
```

---

## 12. 実装フェーズ

各フェーズの完了条件を満たしてから次へ進む。

### Phase 1: 抜け確認（最小構成）
- OpenVR 初期化、HMD 固定の楕円オーバーレイを1枚、キー色で表示する。設定はハードコードでよい。
- 完了条件: VD のパススルーを有効にした状態で、VR 内の左下に楕円の「外界が見える窓」が出る。

### Phase 2: 設定ファイルとホットリロード
- `AppConfig` / `ConfigStore` を実装し、位置・サイズ・形状・色を JSON から読む。
- デバッグ表示（赤＋枠）を実装する。
- 完了条件: 設定ファイルを保存すると1秒以内に VR 内の表示が変わる。不正な JSON でも落ちない。

### Phase 3: OSC 切り替え
- OSCQuery による受信、パラメータのバインド、状態の送り返しを実装する。
- 完了条件: VRChat のアバターメニューのトグルで窓が出たり消えたりする。XSOverlay と同時に起動しても動く。

### Phase 4: 自動起動と常駐
- マニフェスト登録、自動起動、終了処理、多重起動防止、ファイルログを実装する。
- 完了条件: SteamVR を起動するだけで本アプリが立ち上がり、SteamVR を終了すると本アプリも終了する。

### Phase 5（将来）: 拡張
- 複数領域とプリセット、`Polygon` 形状、`WorldAnchor` / `DeviceAnchor`。

---

## 13. リスクと対策

| リスク | 影響 | 対策 |
|---|---|---|
| ワールド内にキー色に近い色がある | 意図しない場所が透ける | 珍しい色を選ぶ。Similarity を下げる |
| 映像圧縮で色がずれる | 縁が抜けない、ノイズが出る | 10bit コーデック。`edgeMode: hard`。Similarity/Smoothness の調整 |
| 他アプリの色補正 | 全体が抜けない | OVRAS などの色補正をオフにする |
| 左右の目の視差 | 縁で楽器が少し切れる | `margin` を増やす。将来は奥行き別の複数領域 |
| 演奏中に頭と楽器の位置関係がずれる | 窓から楽器がはみ出す | 余白を取る。将来は `DeviceAnchor` で手に追従 |
| OSC ポートの競合 | トグルが効かない | OSCQuery を使う。フォールバックポートを設定可能にする |
| オーバーレイの重なり順 | XSOverlay の窓の部分が抜けない | `sortOrder` を設定可能にする。運用で窓をよける |

---

## 14. Claude Code への指示

- この設計書の Phase 1 から順に実装すること。各 Phase の完了時に、ユーザーが実機で確認する手順を短くまとめて提示すること。
- OpenVR API の呼び出しは `AppLifecycle` と `MaskOverlay` に閉じ込めること。
- 戻り値の `EVROverlayError` は必ず確認し、エラー時はログに出すこと。
- `openvr_api.cs` / `openvr_api.dll` は ValveSoftware/openvr の最新リリースから取得し、バージョンを README に記録すること。
- 実機（VR）でしか確認できない部分は、ユーザーに確認を依頼すること。推測で完了扱いにしないこと。
- 設計と異なる判断をした場合は、この文書の「未決事項」に理由とともに追記すること。

---

## 未決事項
- キー色の最終決定（実機で VRChat のワールドを見ながら決める）
- `edgeMode` の既定値（hard と aa を実機で比較して決める）
- 初期配置の数値（実機でデバッグ表示を使って調整する）

### 実装時の判断（設計からの変更・補足）
- **ランタイムを .NET 10 に変更**: .NET 8 のサポートは 2026-11-10 に終了するため、現行 LTS の .NET 10 を使う（ユーザー確認済み）。
- **回転の規約**: `rotationEulerDeg` は `[pitch, yaw, roll]`（度）。板は XY 平面にあり、表は +Z（視点の方）を向く。pitch = X 軸、yaw = Y 軸、roll = Z 軸まわりで、適用順は roll → pitch → yaw（R = Ry(yaw)·Rx(−pitch)·Rz(roll)）。
  - pitch 正で板が上を向く（上端が奥へ倒れる）。**右手系の X 軸まわりとは符号が逆**。
  - yaw 正で板が右を向く。roll 正で板の右端が上がる（この2つは右手系どおり）。
  - 理由: 設計書の初期値（fingerboard `[15, 20, -15]`、body `[30, 10, -15]`）は「視点の左下にある板を視点に向ける」角度になっている。右手系のままだと fingerboard の板は視線から 34° ずれるが、この規約なら 4° に収まる（レビューで計算）。初期値は設計どおりのまま使える。
- **余白と板の大きさ**: `SetOverlayWidthInMeters` には `widthMeters + 2 × marginMeters` を渡す。板（テクスチャ全面）の大きさは余白込みで `(width + 2m) × (height + 2m)`、形状は余白の分だけ外に広げてから描く。画素数の丸めで縦横比がずれないように `SetOverlayTexelAspect` で補正する。
- **縁の aa の幅**: `edgeMode: aa` は符号付き距離で約 1.5px かけて α を落とす。
- **SteamVR の起動待ち**: `VRApplication_Background` で接続を試し（SteamVR が起動していなければ起動させずに失敗する）、成功したら `VRApplication_Overlay` で初期化し直す。失敗したら 3 秒ごとに再試行する。
- **Phase 1〜3 はコンソールアプリ**: ログを見やすくするため。Phase 4 で WinExe とファイルログに切り替える。
- **設定に `debugVisible` を追加**: §6 の「設定からも切り替えられる」ため。§7 の例には無い項目。
- **デバッグ表示中は無効でも表示する**: 表示条件は `enabled || debug`。位置合わせのときに有効/無効を気にしなくて済むように。
- **設定の状態項目の反映**: `startEnabled` は起動時だけ読む。`debugVisible` と `activePreset` は、ファイル上の値が前回から変わったときだけ状態に反映する（OSC で変えた状態を、関係ない項目の保存で上書きしないため）。
- **設定の検証を厳しめにする**: 知らない項目名（打ち間違い）、範囲外の値、存在しない領域 ID を参照するプリセットは、すべて「壊れた設定」として前回の正常な設定を維持する。`//` コメントと末尾のカンマは許す。
- **プリセットの順番**: `presets` は JSON のオブジェクトだが、書いた順番を保って読む（OSC の番号に対応させるため）。
- **ホットリロードの検出**: FileSystemWatcher に加えて、0.5 秒ごとに更新日時とサイズを確認する。変更を検出してから 0.2 秒待って読む（保存途中のファイルを読まないため）。どちらの経路でも1秒以内に反映される。
- **`cornerRadius` の単位はメートル**。短辺の半分を超える値は短辺の半分に切り詰める。
- **領域 ID の制約**: 英数字・`_`・`-` の 1〜64 文字（オーバーレイのキーの長さ制限 128 に収めるため）。
- **透明な画素の色**: α=0 の画素の RGB もキー色にする（デバッグ表示では枠の白）。オーバーレイは premultiplied ではない α で合成され、テクスチャはフィルタで補間されるので、黒にすると縁に暗い紫が混ざって抜けなくなるため（レビューで指摘）。
- **OSCQuery の広告内容**: `/avatar/change`（s）と3つのパラメータ（T/F, i, T/F）を WriteOnly のエンドポイントとして登録する。VRChat は `/avatar` を持つサービスにアバターの値を送る。HTTP サーバーが応答するかを起動時に自分で確かめ、だめならフォールバックする。CI（Windows）では OSCQuery で起動し、HOST_INFO とエンドポイントの型が正しいことをテストで確かめる。
- **受信するアドレスを絞る**: VRChat はアバターの全パラメータを送ってくるので、受信スレッドで使う4つのアドレス以外を捨てる。
- **送り返すタイミング**: 「VRChat 側が持っているはずの値」を覚えておき、状態と違うときだけ送る。起動時・OSC 設定の変更時・アバター変更時は「不明」に戻すので全部送る。OSC で受け取った値はそのまま覚えるので、エコーは起きない。範囲外のプリセット番号を受け取ったときは、現在の番号を送り返してメニューを直す。
- **アバター変更直後の猶予**: `/avatar/change` の後 2 秒間は、パラメータの値を受け取らない（新しいアバターの既定値で状態が変わらないように）。猶予が過ぎたら現在の状態をもう一度送る。
- **型の許容**: Bool のパラメータに Int / Float が来ても受け取る（0 以外 / 0.5 以上を true）。Int のプリセットに Float が来たら四捨五入する（0.5 は切り上げ）。
- **OSCQuery のサービス名**: `ViolinPassthrough-xxxx`（起動ごとに乱数4桁）。OSC の再起動やアプリの再起動の後、VRChat に残った古いポートの広告と混ざらないように。終了時は mDNS の取り消し（TTL 0）を送る。HTTP サーバーが応答することを確かめてから広告する。
- **OSC 設定のホットリロード**: `osc` の項目が変わったら OSC を作り直す。
- **自動起動の扱い**: 初めて登録したとき（と、別の場所から登録し直したとき）だけ `SetApplicationAutoLaunch(true)` を呼ぶ。登録済みなら触らない（SteamVR の設定で自動起動を切ったら、その設定を尊重する）。
- **フォルダを移動したとき**: 登録済みの `BinaryPath` が今の exe と違えば、古い場所のマニフェストを `RemoveApplicationManifest` してから登録し直す。
- **登録の解除**: `--unregister` で自動起動を切り、マニフェストを外す。SteamVR が自動起動したインスタンスが動いていても実行できるように、多重起動防止の対象にしない。ログはコンソールにだけ出す（動いているインスタンスのログファイルを壊さないため）。失敗したら終了コード 1。
- **2つ目のインスタンス**: ログファイルを開く前に Mutex を確かめて終了する（同じログファイルに書き込んで内容が混ざらないように）。
- **終了処理**: Ctrl+C のハンドラは使い終わったら外す。`ProcessExit` は使わない（Main の後に呼ばれて破棄済みのオブジェクトを触るため。ウィンドウの無いアプリはログオフ時に通知を受けずに終了される）。
- **SteamVR の異常終了**: VREvent_Quit が来ないので、5 秒ごとに `vrserver` プロセスがあるかを確かめ、無ければ終了する。
- **コマンドライン**: `--console`（親のコンソールに繋ぐか新しく開いてログを出す）、`--config PATH`、`--unregister`、`--help`。
- **ログ**: `%APPDATA%\ViolinPassthrough\logs\ViolinPassthrough-yyyyMMdd.log`。日付で分け、7 日より古いものは消す。実行中でも開けるように共有モードで書く。
- **多重起動防止**: Mutex 名は `Local\ViolinPassthrough.SingleInstance`。前のインスタンスが異常終了して放棄された Mutex は、そのまま引き継ぐ。
- **Polygon の頂点の単位**: 板の幅・高さに対する割合（中心 0、範囲 -0.5〜0.5）。他の形状と同じく `widthMeters` × `heightMeters` の箱に収まり、大きさを変えると形もそのまま拡大縮小する。余白は多角形から一定の距離（角は丸くなる）。自己交差は偶奇規則。面積のない多角形はエラー。
- **WorldAnchor**: `universe` で standing（既定）/ seated を選ぶ。位置と回転はその空間の座標。
- **DeviceAnchor**: `role`（leftHand / rightHand）か `serial`（トラッカーなど）のどちらか一方で選ぶ。見えている間は `SetOverlayTransformTrackedDeviceRelative` で置き（SteamVR が追従させる）、`onLost: hold` で見失ったら最後の姿勢 × 相対姿勢を `SetOverlayTransformAbsolute` で置く。`hide` なら隠す。一度も見えていないときは、どちらでも隠す。ロールの付け替え（左右の入れ替わり）にも追従する。
- **トラッキング情報の取得**: DeviceAnchor があるときだけ、毎フレーム `GetDeviceToAbsoluteTrackingPose`（standing）とロールの解決を行う。シリアル番号はデバイスの接続が変わったときと 10 秒ごとに取り直す。
- **メインループの例外**: 1フレームの処理で想定外の例外が出ても、ログに出して次のフレームへ進む（常駐アプリなので落とさない）。
