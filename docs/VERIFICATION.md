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
