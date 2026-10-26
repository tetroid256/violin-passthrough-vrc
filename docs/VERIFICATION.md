# 実機での確認手順

この環境（Linux のクラウド環境）では SteamVR / VD / VRChat を動かせないため、以下は実機での確認が必要です。
各 Phase の完了条件は、ここに書いた手順で確認できたときに満たされます。

## 共通の準備

1. GitHub Actions の Artifact `ViolinPassthrough-win-x64` をダウンロードし、任意のフォルダに展開する（README の「ビルド」参照）。
2. Virtual Desktop の Streaming タブで **VR Passthrough** を有効にし、クロマキーの色を **R=255, G=0, B=255（マゼンタ）** にする。
3. Similarity は低め、Smoothness は小さめから始める。

## Phase 1: 抜け確認

最終版の既定の設定は `startEnabled: false`（起動しても窓は出ない）なので、Phase 1 の確認では一度だけ設定を変える。

1. VD で SteamVR を起動する。
2. `ViolinPassthrough.exe --console` を実行する（コンソール窓にログが出る）。ログに `Created ...config.json from the default config.` が出たら、Ctrl+C で一度終了する。
3. `%APPDATA%\ViolinPassthrough\config.json` を開き、`"startEnabled": false` を `true` にして保存する。
4. もう一度 `ViolinPassthrough.exe --console` を実行する。
5. 確認すること:
   - [ ] ログに `OpenVR initialized as an overlay application.` と `[fingerboard] Shown.` が出る。`failed` を含む行が出ない。
   - [ ] VR 内の左下前方に、楕円の「外界が見える窓」が出る。
   - [ ] パススルーを無効にすると、同じ場所にマゼンタの楕円が見える（キー色の確認）。
6. コンソールで Ctrl+C を押すと、楕円が消えてアプリが終了する。
7. SteamVR を終了した状態で `ViolinPassthrough.exe --console` を起動すると `Waiting for SteamVR` が出て待機し、SteamVR を起動すると窓が出る（SteamVR が勝手に起動しないこと）。

うまくいかないとき:
- 楕円の縁にマゼンタが残る → VD の Similarity / Smoothness を調整する。
- 楕円が全く抜けない → VD のキー色が完全に一致しているか、OVR Advanced Settings の色補正がオフかを確認する。
- 楕円の傾きが思ったのと逆に見える → Phase 2 以降で `rotationEulerDeg` を調整する（pitch 正で上を向く、yaw 正で右を向く、roll 正で右端が上がる。DESIGN.md の「未決事項」参照）。

## Phase 2: 設定ファイルとホットリロード

設定ファイルは `%APPDATA%\ViolinPassthrough\config.json`（エクスプローラーのアドレス欄に `%APPDATA%\ViolinPassthrough` と入力すると開ける）。初回起動時に既定の設定がコピーされる。項目の意味は README の「設定」を参照。

1. SteamVR を起動してから `ViolinPassthrough.exe --console` を実行する。ログに `Loaded config from ...config.json` が出る。
2. Phase 1 で `startEnabled` を `true` にした場合は `false` に戻してから起動し直す（何も表示されない状態から始める）。
3. 確認すること（XSOverlay のデスクトップ窓などで config.json を開き、保存するたびに確認する）:
   - [ ] `"debugVisible": true` にして保存すると、1秒以内に **半透明の赤＋白い枠** の楕円が出る（抜けない）。ログに `Config reloaded.` と `Debug display on (config).` が出る。
   - [ ] `position` や `rotationEulerDeg`、`widthMeters` を変えて保存すると、1秒以内に赤い楕円が動く・大きさが変わる。
   - [ ] `"activePreset": "whole"` にすると、角丸の長方形（body）も出る。`"fingerboard"` に戻すと消える。
   - [ ] `"debugVisible": false` にすると消える（`startEnabled` は起動時にしか読まないため）。
   - [ ] `"startEnabled": true` にしてアプリを再起動すると、キー色の楕円が出て外界が見える。
   - [ ] わざと JSON を壊して保存する（`}` を消す、`"widthMeters": "abc"` にするなど）。アプリは落ちず、表示もそのまま。ログに `is invalid; keeping the previous config` とエラーの内容が出る。直して保存すると反映される。
   - [ ] 項目名を打ち間違える（`"widhtMeters"`）と、その名前がログに出て反映されない。
   - [ ] `"edgeMode": "aa"` と `"hard"` で縁の抜け方を比較する（未決事項。結果を教えてください）。
4. 初期配置の調整: `debugVisible: true` のまま、実際にバイオリンを構えて楕円が駒〜左手を覆うように `position` / `rotationEulerDeg` / サイズを調整する（未決事項。決まった値を教えてください）。

## Phase 3: OSC 切り替え

準備（DESIGN.md §9）:
1. アバターの Expression Parameters に `HandPassthrough`（Bool）、`HandPassthroughPreset`（Int）、`HandPassthroughDebug`（Bool）を追加する。**3つとも Synced のチェックを外す**。
2. Expressions Menu に Toggle（HandPassthrough）、Toggle（HandPassthroughDebug）を置く。プリセット用には Sub Menu を作り、HandPassthroughPreset の値を 0 にする Toggle と 1 にする Toggle を置く（**Button は使わない**。Button は押している間だけ値が変わり、離すと 0 に戻るため）。アップロードする。
3. VRChat の Action Menu → Options → OSC → Enabled を ON。アバターを更新したら OSC の **Reset Config** を実行する。

確認すること:
- [ ] 起動ログに `OSCQuery advertised as "ViolinPassthrough" (HTTP ..., OSC UDP 127.0.0.1:...)` が出る（`OSCQuery could not start` が出た場合はフォールバックしている。理由が書いてあるので教えてください）。
- [ ] アバターメニューの HandPassthrough トグルで、窓が出たり消えたりする。ログに `Passthrough enabled (OSC).` / `disabled (OSC).` が出る。
- [ ] Debug トグルで赤い表示に切り替わる。Preset を 1 にすると body の窓も出る。
- [ ] **XSOverlay を起動したままでも** 上の切り替えが効く（9001 番ポートを取り合わないこと）。XSOverlay 側の OSC 機能も引き続き動く。
- [ ] アバターを変えても、窓の状態（出ている／消えている）がそのまま保たれる。2 秒ほどでメニューのトグルの表示も実際の状態に揃う。
- [ ] 本アプリを再起動すると、メニューのトグルが本アプリの状態（`startEnabled` の値）に揃う。
- [ ] `config.json` の `osc.parameters.enabled` を別の名前に変えて保存すると、ログに `OSC settings changed; restarting OSC.` が出る（元に戻しておく）。

OSCQuery がうまく動かないとき（フォールバック）:
- `"useOscQuery": false` にすると `fallbackListenPort`（既定 9011）で待ち受ける。この場合は VRChat の起動オプションに `--osc=9000:127.0.0.1:9011` を付けて送信先を変える必要がある（XSOverlay などが 9001 を使っていても競合しない）。

## Phase 4: 自動起動と常駐

ログは `%APPDATA%\ViolinPassthrough\logs\ViolinPassthrough-yyyyMMdd.log`（7 日分を残す）。

1. 展開したフォルダを **ずっと置いておく場所** に移す（例: `C:\Tools\ViolinPassthrough`）。SteamVR はこの場所を覚えるので、後で動かしたら一度手で起動し直す（自動で登録し直す）。
2. SteamVR を起動し、`ViolinPassthrough.exe` を **引数なしでダブルクリック** する（窓は出ない）。
3. 確認すること:
   - [ ] ログに `Registered ...manifest.vrmanifest with SteamVR and enabled auto launch.` が出る（2回目以降は `Already registered with SteamVR (auto launch: True).`）。
   - [ ] SteamVR の設定 → スタートアップ／シャットダウン → 「起動時のオーバーレイアプリを選択」に **Violin Passthrough** があり、オンになっている。
   - [ ] もう一度 exe をダブルクリックしても2つ目は起動しない（タスクマネージャーの ViolinPassthrough は1つのまま。`--console` を付けて2つ目を起動すると `Another instance is already running.` が出てすぐ終わる。2つ目はログファイルには書かない）。
   - [ ] **SteamVR を終了すると、本アプリも終了する**（タスクマネージャーから ViolinPassthrough が消える。ログに `Received VREvent_Quit.` → `Acknowledged quit.` → `Violin Passthrough exiting.`）。
   - [ ] **SteamVR を起動するだけで、本アプリが立ち上がる**（ログに新しい `starting` の行が出る。VRChat のトグルが効く）。
   - [ ] SteamVR が異常終了したとき（タスクマネージャーで vrserver を終了するなど）も、5 秒以内に本アプリが終了する。
4. 自動起動をやめたいとき: SteamVR の設定で Violin Passthrough をオフにする（本アプリは再びオンにしない）。登録ごと消すときは SteamVR を起動した状態で `ViolinPassthrough.exe --unregister` を実行する（本アプリが起動中でも実行できる。結果はコンソール窓に出る）。
   - [ ] `--unregister` の後、SteamVR の「起動時のオーバーレイアプリ」から Violin Passthrough が消える。もう一度登録するには、引数なしで一度起動する。
   - [ ] SteamVR を終了したときに、Windows のイベントビューアー（Windows ログ → Application）に ViolinPassthrough のエラーが出ていない。

## Phase 5: 拡張（複数領域・Polygon・WorldAnchor・DeviceAnchor）

どれも `debugVisible: true` で形と位置を確かめてから、キー色で抜けを確認する。

- [ ] **複数領域とプリセット**: `activePreset: "whole"`（または OSC で Preset=1）で fingerboard と body の2つの窓が出る。2つの窓が重なっても、重なった部分も抜ける。
- [ ] **Polygon**: fingerboard の `shape` を `{ "type": "polygon", "vertices": [[-0.5, -0.5], [-0.2, -0.5], [0.5, 0.3], [0.5, 0.5], [0.2, 0.5], [-0.5, -0.3]] }` にすると、左下から右上へ斜めに長い形になる（**上下が逆に見えたら教えてください**。テクスチャの行の向きの確認）。
- [ ] **WorldAnchor**: 1つの領域の `anchor` を `{ "type": "world" }`、`position` を `[0, 1.0, -0.5]`、`rotationEulerDeg` を `[0, 0, 0]` にすると、頭を動かしても部屋の同じ場所（床の中心から高さ 1m、前に 0.5m）に窓が残る。
- [ ] **DeviceAnchor**: `anchor` を `{ "type": "device", "role": "leftHand" }`、`position` を `[0, 0, -0.1]` にすると、窓が左手（ハンドトラッキングでは VD のエミュレートしたコントローラー）に付いて動く。
  - [ ] 手を視界の外に出して見失わせると、`onLost: "hold"` では最後の場所に残り、`"hide"` では消える。手が戻ると再び追従する。
- [ ] DeviceAnchor を使っていても、HMD 基準の領域だけのときと比べてフレームレートが落ちない（毎フレームの姿勢取得は DeviceAnchor があるときだけ行う）。

## Phase 6: 演奏のための機能

準備: アバターの Expression Parameters に `HandPassthroughAuto`（Bool）と `HandPassthroughCalibrate`（Bool）を **Synced を外して** 追加し、メニューに Toggle を置いてアップロードする。VRChat の OSC の **Reset Config** を実行する。

### キャリブレーション
- [ ] HandPassthroughCalibrate を ON にすると、視界の少し上に `CALIBRATE 5` … `1` が出て、1 秒ごとにビープ音が鳴る（**ヘッドセットで聞こえるか**。聞こえなければ教えてください）。
- [ ] 左手を 1st ポジション、右手（弓の元）を駒の近くに置いて待つと、`CALIBRATED` と出て高い音が鳴り、5 秒間、新しい窓が赤く表示される。窓が左手から駒までを覆っている。
- [ ] メニューの Calibrate トグルが自動で OFF に戻る。
- [ ] `%APPDATA%\ViolinPassthrough\config.json` の fingerboard の `position` / `rotationEulerDeg` / `widthMeters` と `auto.leftHandPosition` / `auto.headUp` が書き換わる。`config.json.bak` に直前の内容、`config.json.orig` に最初の内容が残る。
- [ ] 弓の手（近い方）と左手（遠い方）の両方が、目から見て窓の内側に入っている（窓は目から見た両手の方向で合わせる）。
- [ ] カウントダウン中に Calibrate を OFF にすると `CAL CANCELLED` と出て、設定は変わらない。
- [ ] 片手を背中に回すなどして見失わせると `CAL FAIL: NO LEFT HAND`（または RIGHT）と出て、設定は変わらない。
- [ ] 何度か繰り返して、窓の位置が毎回ほぼ同じになる（ハンドトラッキングの精度の確認）。窓が短い・ずれるときは結果を教えてください（`padMeters` の既定値や手の位置の補正を決めるため）。

### 自動モード
- [ ] HandPassthrough と HandPassthroughAuto を ON にすると `AUTO ON` と出る。楽器を下ろしていると窓は出ない。
- [ ] 楽器を構えると、すぐに（約 0.3 秒で）窓が出る。演奏中に窓が消えない（**左手のハンドトラッキングが途切れても消えないか**）。
- [ ] 楽器を下ろすと、約 2 秒で窓が消える。
- [ ] 歩き回ったり、向きを変えたりしても、構えたときだけ出る。
- [ ] 演奏中に窓が出たり消えたりするときは、`"useHeadTilt": true`（キャリブレーションを一度してから）や `radiusMeters` を大きくして試し、結果を教えてください。

### 通知
- [ ] Preset を切り替えると `PRESET WHOLE` などと出る。
- [ ] config.json を保存すると `CONFIG RELOADED`、壊して保存すると `CONFIG ERROR`（理由はログに出る） と出る。
- [ ] 通知の文字が読める大きさで、演奏の邪魔にならない位置にある（`feedback.position` / `textHeightMeters` で調整できる）。

## Phase 7: アバターへの組み込みと左手のトラッキング対策

### Modular Avatar のエディタ拡張（Unity）
このパッケージは Unity を動かせない環境で書いたので、**まだ一度も Unity で動かしていません**。エラーや見た目の問題があれば、Console のメッセージごと教えてください。

準備: VCC で Modular Avatar を入れたアバターのプロジェクトに、`unity/com.tetroid256.violin-passthrough` フォルダを `Packages/` の下にコピーする（docs/AVATAR_SETUP.md）。

- [ ] Unity の Console にコンパイルエラーが出ない。
- [ ] `Tools/Violin Passthrough/Add OSC Menu to Avatar` で画面が開き、選んでいたアバターが入っている。
- [ ] 実行すると、アバターの直下に GameObject ができ、MA Parameters / MA Menu Installer / MA Menu Item が付いている。Ctrl+Z で元に戻せる。もう一度実行すると、増えずに置き換わる。
- [ ] Play モードに入る（または NDMF の Manual Bake）と、Expression Parameters にパラメータが入り、メニューに「Violin Passthrough」のサブメニューができる（Phase 7 の版は 5 つ、すべて **Synced なし**。Phase 8 の版は 8 つで、`HandPassthroughViolinState` だけ **Synced あり**。画面の Violin を外すと 6 つ）。
- [ ] アップロードして VRChat でメニューを開き、Passthrough / Auto / Calibrate / Debug / Preset が本アプリを操作できる（Phase 3・Phase 6 の確認と同じ）。本アプリが状態を送り返すので、トグルの表示が本アプリの状態と合う。
- [ ] 手で設定していたパラメータがあるアバターでは、手で入れたものを消してから試す（同じ名前が二重になったときの動きも、分かれば教えてください）。

### キャリブレーションの平均
- [ ] カウントダウンの最後に、左手を一瞬だけ弦から離して（ハンドトラッキングが外れるように）待っても `CALIBRATED` になる。
- [ ] 最後の 1 秒ずっと片手を隠すと `CAL FAIL: NO LEFT HAND` になる。ログに `frames in the last 1 s: hmd 30, left 0, right 30` のような数が出る。
- [ ] 何度か繰り返したときの窓の位置のばらつきが、Phase 6 のときより小さい（体感でよい）。

### トラッカー（持っている場合）
- [ ] `--console` のログの `Tracked devices:` にトラッカーのシリアル番号が出る。
- [ ] 楽器にトラッカーを付けて `auto.deviceSerial` を設定し、キャリブレーションしてから自動モードにすると、左手のハンドトラッキングが外れていても、構えたら窓が出て、下ろしたら消える。
- [ ] 手首にトラッカーを付けて `tracking.leftHandSerial` を設定すると、キャリブレーションと自動モードがトラッカーの位置で動く。
- [ ] シリアル番号を打ち間違えると、視界に `NO LEFT TRACKER`（または `NO AUTO TRACKER`）と出て、ログに接続中のシリアル番号の一覧が出る。
- [ ] 窓をトラッカー基準（`"anchor": { "type": "device", "serial": "..." }`）にしてキャリブレーションすると、その瞬間は HMD 基準と同じ場所に出て、楽器を動かすと窓が楽器についてくる。

## Phase 8: 判定方法の切り替えとバイオリンのモデル

準備: エディタ拡張でメニューを作り直す（**Add / Update**）か、手で `HandPassthroughAutoMethod`（Int）・`HandPassthroughViolin`（Int）・`HandPassthroughViolinState`（Int、**Synced をオン**）を追加してアップロードし、VRChat の OSC の **Reset Config** を実行する（docs/AVATAR_SETUP.md）。

### 判定方法
- [ ] キャリブレーションを一度してから、自動モードをオンにする。
- [ ] **Auto Method > Hand + Head** にすると `AUTO: HAND+HEAD` と出る。左手を隠したまま（または弦を押さえてハンドトラッキングが外れた状態で）構えても窓が出る。
- [ ] **Head** にすると、手の位置に関係なく、顎当てに顎を乗せて頭を傾けたときだけ窓が出る。首を少し傾けただけで出てしまうときは、`auto.headTiltToleranceDeg`（既定 15°）を小さくして結果を教えてください。
- [ ] **Left Hand** に戻すと、今までどおり左手で判定する。
- [ ] 設定ファイルの `auto.headUp` を消してから Head を選ぶと `CALIBRATE FOR HEAD` と出る。

### バイオリンのモデルの状態（アニメーターが無くても確かめられる）
- [ ] `--console` で起動しておき、**Violin > Hold** にすると `VIOLIN: HOLD` と出て、ログに `Violin model: Away -> Held.` と出る。
- [ ] 構えるとログに `Held -> Playing.`、下ろして約 2 秒で `Playing -> Held.` と出る（自動モードがオフでも）。
- [ ] **Play** にすると、下ろしていても `Playing` のまま。自動モードがオンなら窓も出る。
- [ ] **Away** にすると `Away` に戻る。
- [ ] Hold か Play のまま本アプリを終了する（SteamVR を終了して本アプリも終わらせるか、`--console` で起動して Ctrl+C。窓の × で閉じたときは送れない場合がある）と、ログに `Violin model put away on exit.` と出て、メニューの Violin が Away に戻る。
- [ ] （確かめられれば）VRChat のパラメータのデバッグ表示や、別のアカウント・友人の画面で、`HandPassthroughViolinState` が 0 / 1 / 2 に変わる（同期しているか）。
- [ ] 判定が出たり消えたりして状態がばたつくときは、どの判定方法で起きたかを教えてください（他のプレイヤーにはモデルが行ったり来たりして見えるため）。

