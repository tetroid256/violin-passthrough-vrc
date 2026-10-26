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

Modular Avatar を使う場合は、同梱の Unity 用エディタ拡張でパラメータとメニューをまとめて追加できる（§16.1、`docs/AVATAR_SETUP.md`）。手で設定する場合は次のとおり（Phase 6 の 2 つのパラメータは §15.5）。

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

### Phase 6: 演奏のための機能（§15）
- 手の位置からのキャリブレーション、構えたときだけ表示する自動モード、VR 内の通知表示。
- 完了条件: 設定ファイルを手で編集せずに、楽器を構えて窓の位置を合わせられる。アクションメニューを開かずに、楽器を構えると窓が出て、下ろすと消える。

### Phase 7: アバターへの組み込みと左手のトラッキング対策（§16）
- Modular Avatar 用のエディタ拡張（メニューとパラメータをアバターに追加する）。
- 手の代わりのトラッカー（`tracking`、`auto.deviceSerial`）、キャリブレーションの平均、デバイス基準の領域のキャリブレーション。
- 完了条件: Modular Avatar を入れたアバターに、エディタ拡張でメニューを追加してアップロードすると、メニューで本アプリを操作できる。指板の左手のハンドトラッキングが一瞬外れても、キャリブレーションが成功する。トラッカーを付けた場合は、ハンドトラッキングが外れていても自動モードが判定できる。

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

## 15. 演奏のための機能（Phase 6）

Phase 1〜5 を作った後に、「VRChat の上でバイオリンを弾く」という使い方から見直して追加した機能。

### 15.1 困りごと
| 困りごと | 理由 |
|---|---|
| 窓の位置合わせが大変 | 位置・角度・大きさを JSON の数値で調整するしかない。楽器を構えたまま VR 内で数値を試行錯誤するのは現実的でない |
| 演奏中にオン/オフしにくい | 切り替えは VRChat のアクションメニュー（ハンドジェスチャー）だが、楽器と弓で両手がふさがっている。オンのまま楽器を下ろすと、窓に床などが映り続ける |
| 反応が見えない | メニューで切り替えたとき、設定ファイルを保存したときに、効いたのか・エラーなのかが VR 内で分からない |

### 15.2 キャリブレーション（手の位置から窓を合わせる）
1. OSC の `HandPassthroughCalibrate`（Bool、Toggle）を true にすると始まる。カウントダウン中に false にすると取り消す。
2. `calibration.countdownSeconds` 秒（既定 5 秒）のカウントダウン。1 秒ごとにビープ音と通知表示で残り秒数を知らせる。この間に演奏の姿勢をとる: **左手は 1st ポジション（ネック側）、右手は弓の元（フロッグ）を駒の近くの弦に置く**。
3. 0 になった瞬間の HMD・左手・右手の姿勢を取る（VD のハンドトラッキングでは、エミュレートされたコントローラーの位置）。※ Phase 7 で、最後の 1 秒の平均に変えた（§16.3）。
4. 対象の領域（`calibration.regionId`、既定 `fingerboard`、HMD 基準のもの）の板を次のように置き直す（すべて HMD 座標、視点が原点）。
   - 視線 = 目から見た両手の方向の二等分線。板はこれに垂直で、目から「両手の中点までの距離」のところに置く。
   - 両手をそれぞれの視線に沿って板の面に投影する（目から見て、両手が板の左右の端の内側に来るように）。弓の手は近く、左手は遠いのが普通なので、真正面に投影すると近い方の手が板からはみ出すため。
   - 中心 = 投影した両手の中点。板の横方向（+X）= 投影した左手 → 右手。板の縦方向 = 表の向き × 横方向。表は視点の方を向く。
   - 幅 = 投影した両手の距離 + 2 × `calibration.padMeters`（既定 0.05 m）。高さ・形状・余白は変えない。
   - 目から見た両手の方向が 4° 未満しか離れていない、または手が目から 0.1 m 以内なら失敗にする。
5. 結果を config.json に書き込む（領域の `position` / `rotationEulerDeg` / `widthMeters`、自動モードの基準 `auto.leftHandPosition` / `auto.headUp`）。書く前の内容を `config.json.bak`（毎回）に、最初に書き換える前の内容を `config.json.orig`（一度だけ）に残す。書き込んだファイルはホットリロードで反映される。**JSON のコメントは消える**（`.orig` には残る）。今の設定ファイルにエラーがあるときは書き込まない。
6. 反映後 `calibration.previewSeconds` 秒（既定 5 秒）、デバッグ表示（赤）で結果を見せる。無効の状態でも見せる。
7. 失敗（手を見失っている、両手が近すぎる、対象の領域が無い・HMD 基準でない）したら、通知表示とビープ音で知らせ、設定は変えない。対象の領域の確認は実行時に行う（キャリブレーションを使わない人の設定をエラーにしないため）。

### 15.3 自動モード（構えたときだけ表示）
- OSC の `HandPassthroughAuto`（Bool）または設定の `auto.enabled` で切り替える。
- 表示の条件は `(有効 かつ (自動モードでない または 構えている)) または デバッグ表示 または プレビュー中`。有効/無効は全体のスイッチのまま。自動モードはその内側の条件。
- 「構えている」の判定（有効にした条件をすべて満たす）:
  - `auto.useLeftHand`（既定 true）: HMD から見た左手の位置が `auto.leftHandPosition` から `auto.radiusMeters`（既定 0.15 m）以内。
  - `auto.useHeadTilt`（既定 false）: HMD から見た「上」の向き（重力の逆）が `auto.headUp` から `auto.headTiltToleranceDeg`（既定 15°）以内。頭の傾き（顎当てに顎を乗せた姿勢）で判定するので、手を見失っても使える。向いている方角（ヨー）には左右されない。
- チャタリングを防ぐ: 構えた状態が `auto.enterDelaySeconds`（既定 0.3 秒）続いたら表示、外れた状態が `auto.exitDelaySeconds`（既定 2 秒）続いたら非表示。外れたと判定する距離・角度は、入るときの 1.3 倍。
- 判定に必要な姿勢が取れない（トラッキングを見失った）間は、直前の判定を保つ。ただし外れ始めていたら（楽器を下ろした直後に手がカメラの外に出た場合など）その待ち時間は数え続け、時間が来たら隠す。
- 左手と頭の傾きの両方を使っていて左手を見失っているときは、頭の傾きだけで決める。
- 基準（`leftHandPosition` / `headUp`）はキャリブレーションで記録する。`leftHandPosition` には目安の既定値がある。`headUp` は `useHeadTilt` を使うときに必要。

### 15.4 VR 内の通知表示
- HMD 基準の小さな板（既定は視点の少し上、前方 0.5 m、文字の高さ 1.5 cm）に、英大文字の短い文字列を出す（自前の 5×7 ドットのフォント）。文字の色はキー色から離れた色を選ぶ（キー色に近いと文字が抜けて読めないため）。フォントに無い文字を含むプリセット名は番号で出す。
- 出すもの: キャリブレーションのカウントダウンと結果、自動モードのオン/オフ、OSC でのプリセット切り替え、自動モード中の有効/無効、設定ファイルのエラー（`CONFIG ERROR`）。
- `feedback.toasts: false` で、カウントダウンと結果以外を出さない。ビープ音は `calibration.beep` で切り替える（PC の既定の音声出力に出る。VD 経由でヘッドセットで聞こえる）。
- 通知表示のオーバーレイも OpenVR の呼び出しは `MaskOverlay.cs` の中に置く（§14 の「AppLifecycle と MaskOverlay に閉じ込める」を守るため）。

### 15.5 追加する設定と OSC パラメータ
```json
"auto": {
  "enabled": false,
  "useLeftHand": true,
  "useHeadTilt": false,
  "leftHandPosition": [-0.35, -0.10, -0.35],
  "radiusMeters": 0.15,
  "headTiltToleranceDeg": 15,
  "enterDelaySeconds": 0.3,
  "exitDelaySeconds": 2.0
},
"calibration": {
  "regionId": "fingerboard",
  "countdownSeconds": 5,
  "padMeters": 0.05,
  "previewSeconds": 5,
  "beep": true
},
"feedback": {
  "toasts": true,
  "position": [0, 0.08, -0.5],
  "textHeightMeters": 0.015
},
"osc": { "parameters": { "auto": "HandPassthroughAuto", "calibrate": "HandPassthroughCalibrate" } }
```
- 項目が無ければ既定値を使う（Phase 5 までの config.json はそのまま動く）。
- アバターには `HandPassthroughAuto`（Bool）と `HandPassthroughCalibrate`（Bool）を Synced を外して追加し、メニューに Toggle を置く。

### 15.6 見送ったもの
- チューナー・メトロノーム: マスクとは別の機能で、マイク入力や音の出し方の検討が要る。パススルーでクリップチューナーを見る方が手軽（`padMeters` を増やすか、スクロール用の領域とプリセットを作る）。
- ラジアルメニューでの大きさの微調整: 演奏中は操作できないので、キャリブレーションで代える。
- コントローラーのボタンでの切り替え（SteamVR Input）: ハンドトラッキングではボタンが無い。

## 16. アバターへの組み込みと左手のトラッキング対策（Phase 7）

Phase 1〜2 を実機で確かめた後の、ユーザーの要望から追加した機能。

### 16.1 Modular Avatar 対応
- 困りごと: §9・§15.5 のパラメータ（5 個）とメニューを、アバターごとに手で設定するのは手間で、間違えやすい（Synced を外し忘れる、名前を打ち間違える）。
- [Modular Avatar](https://github.com/bdunderscore/modular-avatar)（MA）は、アバターに置いたコンポーネントから、アップロード時にパラメータとメニューを組み込む（元のアバターの Expression Parameters / Menu を書き換えない）。
- 本リポジトリの `unity/com.tetroid256.violin-passthrough/` に、Unity のエディタ拡張（VPM / UPM 形式のパッケージ）を置く。メニュー `Tools/Violin Passthrough/Add OSC Menu to Avatar` で、選んだアバターの直下に次を持つ GameObject を作る（Undo できる）。
  - `MA Parameters`: 5 つのパラメータ（Bool / Int、**ローカルのみ（Synced しない）**、保存しない、既定 0）。同期ビットを消費しない。
  - `MA Menu Installer` と `MA Menu Item`（サブメニュー）。中に Toggle（Passthrough / Auto / Calibrate / Debug）と、プリセットのサブメニュー（番号ごとの Toggle）。
- パラメータ名とプリセットはツールの画面で変えられる（config.json の `osc.parameters` と `presets` の順番に合わせる）。
- プレハブ（YAML）を手で書くと MA の内部形式の変更に弱いので、MA の公開コンポーネントをエディタのコードで組み立てる。
- 本アプリ（OSC 側）は変更しない。パラメータ名と型は §15.5 のまま。
- 詳しい手順は `docs/AVATAR_SETUP.md`。

### 16.2 左手のトラッキングが外れる
- 困りごと: 指板を押さえる左手は、ハンドトラッキングが外れやすい姿勢（手のひらが顔の方を向き、指がネックと弦に隠れ、頭のすぐ近くにある）。外れると、自動モードが判定できない、キャリブレーションが失敗する、左手に追従させた窓が止まる。
- VRChat のアバターの左手が飛ぶ問題は、VRChat がコントローラー（VD のエミュレート）の姿勢を使うので、本アプリでは直せない（範囲外。`docs/TRACKING.md` に試せることを書く）。
- 対策（`docs/TRACKING.md` にユーザー向けにまとめる）:

| 対策 | 機材 | 本アプリの設定 | 効くもの |
|---|---|---|---|
| キャリブレーションの平均（§16.3） | なし | 不要 | キャリブレーション |
| 頭の傾きで判定 | なし | `auto.useHeadTilt`（Phase 6） | 自動モード |
| 楽器にトラッカー | SteamVR トラッカー | `auto.deviceSerial`、領域の `anchor.serial` | 自動モード、窓を楽器に追従 |
| 手首にトラッカー | SteamVR トラッカー | `tracking.leftHandSerial` | 自動モード、キャリブレーション |

- 追加する設定:
```json
"auto": { "deviceSerial": null },
"tracking": {
  "leftHandSerial": null,
  "rightHandSerial": null
}
```
  - `tracking.leftHandSerial` / `rightHandSerial`: 自動モードとキャリブレーションで、手（ロール）の代わりにこのシリアル番号のデバイスを使う。見つからなくても手には戻らない（手首と手では位置がずれるので、混ぜると判定やキャリブレーションが狂う）。
  - `auto.deviceSerial`: 自動モードの判定（`useLeftHand`）を、左手の代わりにこのデバイスの位置で行う（楽器に付けたトラッカー）。`tracking.leftHandSerial` より優先する。キャリブレーションでは、このデバイスの位置を `auto.leftHandPosition` に記録する（窓は両手の位置から決める）。自動モードが位置で判定する（`useLeftHand`）ときは、このデバイスが見えないとキャリブレーションは失敗する（`NO AUTO TRACKER`）。頭の傾きだけで判定するときは、見えなくても窓は合わせ、`auto.leftHandPosition` は書き換えない（左手の位置を、このデバイスの基準として記録しないため）。
  - シリアル番号は大文字・小文字を区別しない（SteamVR のデバイス一覧と同じ）。左右に同じデバイスは指定できない。
  - 設定したシリアル番号のデバイスが無いときは、自動モードではログ（接続中のシリアル番号の一覧つき）と通知表示（`NO LEFT TRACKER` / `NO AUTO TRACKER`）で一度だけ知らせる。見つかったらログに出し、また無くなったら知らせる。キャリブレーション中はログだけにする（結果の `CAL FAIL: NO LEFT TRACKER` などで知らせ、カウントダウンの表示を上書きしないため）。
- トラッカーを Quest と一緒に使うには、座標合わせ（OpenVR Space Calibrator など）が要る。本アプリは SteamVR の座標をそのまま使う。

### 16.3 キャリブレーションの平均
- カウントダウンの最後の 1 秒（撮るフレームを含む。`countdownSeconds` が 1 のときは最後の 0.5 秒。メニューを操作した直後の、構える前の位置を混ぜないため）の間、毎フレーム HMD・左右の手（または代わりのデバイス）・`auto.deviceSerial` のデバイスの位置を **HMD 座標で** 集め、平均する。HMD 座標で平均するので、その間に頭が動いても結果はぶれない。
- 窓の間に一度でも見えていたデバイスは使える。一度も見えなかったら失敗（`NO LEFT HAND` / `NO LEFT TRACKER` など）。HMD が見えていないフレームは使わない。
- 「上」の向き（`auto.headUp`）も平均して長さ 1 にする。
- 失敗したときのログに、窓の間に見えていたフレーム数を出す（HMD・左右の手、使うときはアンカーと `auto.deviceSerial` のデバイスも）。
- 窓の間に、集めるデバイスの設定（`tracking`、`auto.deviceSerial`、対象の領域のアンカー）が再読み込みで変わったら、それまでの分は捨てる（違うデバイスの位置を混ぜない）。

### 16.4 デバイス基準の領域のキャリブレーション
- Phase 6 では HMD 基準の領域だけだった。Phase 7 で、デバイス基準（`anchor.type: device`）の領域もキャリブレーションできるようにする（楽器に付けたトラッカーに窓を追従させるため）。
- 板の姿勢は今までどおり HMD 座標で求め、アンカーのデバイスの姿勢（HMD 座標、窓の間で最後に見えていたもの）で割って、デバイスから見た姿勢にする: `local = inverse(deviceInHmd) × plateInHmd`。その瞬間は HMD 基準と同じ場所に来て、その後はデバイスに追従する。
- アンカーのデバイスの向きは平均しない（回転の平均は単純な足し算ではできず、じっと構えていれば最後の姿勢で十分なため）。
- アンカーのデバイスが窓の間に一度も見えなかったら失敗（`NO ANCHOR DEVICE`）。部屋に固定した領域（world）は手の位置から決める意味が薄いので失敗（`WORLD REGION`）。

### 16.5 見送ったもの
- VRChat のアバターの手をトラッカーで動かす: VRChat の OSC トラッカー（`/tracking/trackers/...`）は体のトラッカー用で、手には使えない。手の姿勢を差し替えるには SteamVR のドライバーが要り、本アプリ（オーバーレイ）の範囲を超える。
- 手首のトラッカーと手の位置の差（オフセット）の設定: キャリブレーションでは手首の位置を窓の端にすれば足り、`padMeters` で調整できるため。必要になったら追加する。
- Unity パッケージのプレハブ: §16.1 のとおり、エディタのコードで組み立てる。

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
- **Polygon の頂点の単位**: 板の幅・高さに対する割合（中心 0、範囲 -0.5〜0.5）。他の形状と同じく `widthMeters` × `heightMeters` の箱に収まり、大きさを変えると形もそのまま拡大縮小する。余白は多角形から一定の距離（角は丸くなる）。自己交差は偶奇規則。すべての点が一直線上にあるものはエラー。頂点は 128 点まで（ラスタライズはメインループで行い、時間が頂点数に比例するため）。
- **WorldAnchor**: `universe` で standing（既定）/ seated を選ぶ。位置と回転はその空間の座標。
- **DeviceAnchor**: `role`（leftHand / rightHand）か `serial`（トラッカーなど）のどちらか一方で選ぶ。見えている間は `SetOverlayTransformTrackedDeviceRelative` で置き（SteamVR が追従させる）、`onLost: hold` で見失ったら最後の姿勢 × 相対姿勢を `SetOverlayTransformAbsolute` で置く。`hide` なら隠す。一度も見えていないときは、どちらでも隠す。ロールの付け替え（左右の入れ替わり）にも追従する。
- **トラッキング情報の取得**: DeviceAnchor があるときだけ、毎フレーム `GetDeviceToAbsoluteTrackingPose`（standing）とロールの解決を行う。シリアル番号は起動時・デバイスの接続が変わったとき・10 秒ごとに取り直し、一覧が変わったらログ（Info）に出す。設定したシリアル番号のデバイスが無いときは一度だけ警告する。`serial` の前後の空白は取り除く。
- **DeviceAnchor の描き直し**: 設定や状態の変更で描き直すときは、直前のフレームのトラッキング情報で置き直す（次のフレームまで一瞬消えないように）。
- **Phase 6 の追加（§15）**: Phase 1〜5 の後に、演奏の使い方から見直して追加した（ユーザーの依頼）。
- **キャリブレーションの開始・取り消し**: Calibrate パラメータの true で始め、カウントダウン中の false で取り消す。本アプリは「キャリブレーション中か」を Calibrate の値として送り返すので、終わるとメニューのトグルが自動で OFF に戻る。Button コントロールは離すと false になって取り消してしまうので、Toggle を使う。
- **キャリブレーションの書き込み**: config.json を JsonNode で読み直して必要な項目だけ書き換え、自前の書き出しで 2 文字下げに整形する（数値だけの配列は1行）。項目名の大文字・小文字と、他の数値の書き方は元のまま。数値は位置・幅を mm、角度を 0.1° に丸める。書く前に元のファイルと結果の両方を検証し、だめなら書かない。一時ファイルは成否にかかわらず消す。
- **プレビュー**: キャリブレーション後のデバッグ表示は状態（PassthroughState）を変えずに、メインループが一時的に重ねる。対象の領域がプリセットに入っていなくても見せる。
- **トラッキング情報の取得（Phase 6）**: 自動モードは、有効かつ自動モードのときだけ毎フレーム姿勢を取る。キャリブレーションは取るときに1回だけ。
- **ビープ音**: Windows の kernel32 `Beep` を別スレッドで鳴らす（既定の音声出力に出るので VD 経由で聞こえる見込み。実機で確認する）。
- **通知表示**: OpenVR の呼び出しは `MaskOverlay.cs` の `VrStatusOverlay` に置く。文字は自前の 5×7 ドットのフォントで描き、半透明の黒の背景に白（成功は緑、失敗は赤）。通知のオーバーレイの重なり順はマスクの `sortOrder + 1`。設定の再読み込み（`CONFIG RELOADED`）は、キャリブレーションの結果を見せている間は出さない。
- **ホットリロードの待ち時間**: 更新日時の確認で変更を見つけたとき、すでに変更を検知済みなら待ち時間を数え直さない（キャリブレーションの書き込み直後に反映が遅れないように）。
- **Phase 7 の追加（§16）**: Phase 1〜2 を実機で確認した後の、ユーザーの依頼（Modular Avatar 対応、左手のトラッキング対策）。
- **Unity パッケージは未検証**: このリポジトリの CI では Unity を動かせないので、エディタ拡張は Unity と MA の型を真似たスタブに対してコンパイルを確かめただけ。実機の Unity で確かめる（`docs/VERIFICATION.md` Phase 7）。
- **トラッキング情報の取得（Phase 7）**: キャリブレーションは、カウントダウンの最後の 1 秒だけ毎フレーム姿勢を取る（Phase 6 は撮るときに1回）。
- **メインループの例外**: 1フレームの処理で想定外の例外が出ても、ログに出して次のフレームへ進む（常駐アプリなので落とさない）。
