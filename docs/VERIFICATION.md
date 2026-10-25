# 実機での確認手順

この環境（Linux のクラウド環境）では SteamVR / VD / VRChat を動かせないため、以下は実機での確認が必要です。
各 Phase の完了条件は、ここに書いた手順で確認できたときに満たされます。

## 共通の準備

1. GitHub Actions の Artifact `ViolinPassthrough-win-x64` をダウンロードし、任意のフォルダに展開する（README の「ビルド」参照）。
2. Virtual Desktop の Streaming タブで **VR Passthrough** を有効にし、クロマキーの色を **R=255, G=0, B=255（マゼンタ）** にする。
3. Similarity は低め、Smoothness は小さめから始める。

## Phase 1: 抜け確認

1. VD で SteamVR を起動する。
2. `ViolinPassthrough.exe --console` を実行する（コンソール窓にログが出る。Phase 4 からは引数なしだと窓は出ず、ログはファイルに出る）。
3. 確認すること:
   - [ ] ログに `OpenVR initialized as an overlay application.` と `[fingerboard] Shown.` が出る。`failed` を含む行が出ない。
   - [ ] VR 内の左下前方に、楕円の「外界が見える窓」が出る。
   - [ ] パススルーを無効にすると、同じ場所にマゼンタの楕円が見える（キー色の確認）。
4. コンソールで Ctrl+C を押すと、楕円が消えてアプリが終了する。
5. SteamVR を起動していない状態で exe を起動すると `Waiting for SteamVR` が出て待機し、SteamVR を起動すると窓が出る（SteamVR が勝手に起動しないこと）。

うまくいかないとき:
- 楕円の縁にマゼンタが残る → VD の Similarity / Smoothness を調整する。
- 楕円が全く抜けない → VD のキー色が完全に一致しているか、OVR Advanced Settings の色補正がオフかを確認する。
- 楕円の傾きが思ったのと逆に見える → Phase 2 以降で `rotationEulerDeg` を調整する（pitch 正で上を向く、yaw 正で右を向く、roll 正で右端が上がる。DESIGN.md の「未決事項」参照）。

## Phase 2: 設定ファイルとホットリロード

設定ファイルは `%APPDATA%\ViolinPassthrough\config.json`（エクスプローラーのアドレス欄に `%APPDATA%\ViolinPassthrough` と入力すると開ける）。初回起動時に既定の設定がコピーされる。項目の意味は README の「設定」を参照。

1. SteamVR を起動してから `ViolinPassthrough.exe --console` を実行する。ログに `Loaded config from ...config.json` が出る。
2. 既定では `startEnabled: false` なので何も表示されない。
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
2. Expressions Menu に Toggle（HandPassthrough）、Toggle（HandPassthroughDebug）、プリセット用に Sub Menu の Button / Toggle（HandPassthroughPreset を 0 と 1 にする）を追加してアップロードする。
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
   - [ ] もう一度 exe をダブルクリックしても2つ目は起動しない（ログに `Another instance is already running.`）。
   - [ ] **SteamVR を終了すると、本アプリも終了する**（タスクマネージャーから ViolinPassthrough が消える。ログに `Received VREvent_Quit.` → `Acknowledged quit.` → `Violin Passthrough exiting.`）。
   - [ ] **SteamVR を起動するだけで、本アプリが立ち上がる**（ログに新しい `starting` の行が出る。VRChat のトグルが効く）。
   - [ ] SteamVR が異常終了したとき（タスクマネージャーで vrserver を終了するなど）も、5 秒以内に本アプリが終了する。
4. 自動起動をやめたいとき: SteamVR の設定で Violin Passthrough をオフにする（本アプリは再びオンにしない）。登録ごと消すときは SteamVR を起動した状態で `ViolinPassthrough.exe --unregister` を実行する。
