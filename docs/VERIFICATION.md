# 実機での確認手順

この環境（Linux のクラウド環境）では SteamVR / VD / VRChat を動かせないため、以下は実機での確認が必要です。
各 Phase の完了条件は、ここに書いた手順で確認できたときに満たされます。

## 共通の準備

1. GitHub Actions の Artifact `ViolinPassthrough-win-x64` をダウンロードし、任意のフォルダに展開する（README の「ビルド」参照）。
2. Virtual Desktop の Streaming タブで **VR Passthrough** を有効にし、クロマキーの色を **R=255, G=0, B=255（マゼンタ）** にする。
3. Similarity は低め、Smoothness は小さめから始める。

## Phase 1: 抜け確認

1. VD で SteamVR を起動する。
2. `ViolinPassthrough.exe` を実行する（Phase 1 ではコンソール窓が出て、ログが表示される）。
3. 確認すること:
   - [ ] ログに `OpenVR initialized as an overlay application.` と `[fingerboard] Shown.` が出る。`failed` を含む行が出ない。
   - [ ] VR 内の左下前方に、楕円の「外界が見える窓」が出る。
   - [ ] パススルーを無効にすると、同じ場所にマゼンタの楕円が見える（キー色の確認）。
4. コンソールで Ctrl+C を押すと、楕円が消えてアプリが終了する。
5. SteamVR を起動していない状態で exe を起動すると `Waiting for SteamVR` が出て待機し、SteamVR を起動すると窓が出る（SteamVR が勝手に起動しないこと）。

うまくいかないとき:
- 楕円の縁にマゼンタが残る → VD の Similarity / Smoothness を調整する。
- 楕円が全く抜けない → VD のキー色が完全に一致しているか、OVR Advanced Settings の色補正がオフかを確認する。
- 楕円の傾きが逆に見える → Phase 2 以降で `rotationEulerDeg` の pitch の符号を反転して調整する（回転の規約は DESIGN.md の「未決事項」参照）。

## Phase 2: 設定ファイルとホットリロード

設定ファイルは `%APPDATA%\ViolinPassthrough\config.json`（エクスプローラーのアドレス欄に `%APPDATA%\ViolinPassthrough` と入力すると開ける）。初回起動時に既定の設定がコピーされる。項目の意味は README の「設定」を参照。

1. SteamVR を起動してから `ViolinPassthrough.exe` を実行する。ログに `Loaded config from ...config.json` が出る。
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
