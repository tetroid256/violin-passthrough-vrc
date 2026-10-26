# Violin Passthrough（Modular Avatar メニュー）

VRChat のアバターに、Violin Passthrough を操作するメニュー（OSC 用のアバターパラメータとトグル）を、[Modular Avatar](https://modular-avatar.nadena.dev/) を使って非破壊で追加する Unity エディタ拡張。

## 必要なもの

- Unity 2022.3（VRChat の指定バージョン）
- VRChat SDK - Avatars 3.7.4 以降（Modular Avatar 1.11.0 が必要とするもの）
- Modular Avatar 1.11.0 以降、2.0 未満

Modular Avatar か VRChat SDK が入っていない（またはバージョンが範囲外の）ときは、この拡張はコンパイルされず、メニューにも出ない。プロジェクトにコンパイルエラーは出ない。

## インストール

1. VCC（VRChat Creator Companion）か ALCOM で、プロジェクトに Modular Avatar を追加する。
2. このフォルダ（`com.tetroid256.violin-passthrough`）を、Unity プロジェクトの `Packages/` フォルダにそのままコピーする。
   - Unity の Window > Package Manager > ＋ > Add package from disk... でこのフォルダの `package.json` を選んでもよい。この場合、フォルダを移動・削除しないこと。

## 使い方

1. シーン上のアバターを選び、メニューの **Tools > Violin Passthrough > Add OSC Menu to Avatar** を開く（ヒエラルキーで右クリック > **Violin Passthrough > Add OSC Menu** でも開ける）。
2. パラメータ名がアプリの `config.json` の `osc.parameters` と同じか確認する（既定のままなら変更不要）。
3. **Add / Update** を押す。アバターの直下に `ViolinPassthrough OSC Menu` ができる。
4. そのままアップロードする。

作られるもの（各オブジェクトは MA Menu Item）:

```
ViolinPassthrough OSC Menu   MA Parameters + MA Menu Installer + サブメニュー「Violin Passthrough」
├─ Passthrough               Toggle  HandPassthrough = 1
├─ Auto                      Toggle  HandPassthroughAuto = 1
├─ Auto Method               サブメニュー（Left Hand = 0 / Hand + Head = 1 / Head = 2。HandPassthroughAutoMethod）
├─ Preset                    サブメニュー
│  ├─ Fingerboard            Toggle  HandPassthroughPreset = 0
│  └─ Whole                  Toggle  HandPassthroughPreset = 1
├─ Calibrate                 Toggle  HandPassthroughCalibrate = 1
├─ Debug                     Toggle  HandPassthroughDebug = 1
└─ Violin                    サブメニュー（Away = 0 / Hold = 1 / Play = 2。HandPassthroughViolin）
```

- パラメータは保存しない・既定値 0。`HandPassthroughViolinState`（アプリが送るバイオリンのモデルの状態）だけ同期し（8 bit）、他はローカル専用（同期しない）。バイオリンのモデルを使わないアバターでは、画面の **Violin** を外す（同期するパラメータも入れない）。
- バイオリンのモデルのアニメーター（出し入れ・追従先の切り替え・左手の固定）はまだ作らない。
- アバター本体の Expressions Menu / Expression Parameters アセットは変更しない。アップロード時に Modular Avatar が合成する。
- もう一度 Add / Update を押すと、同じ名前のオブジェクトを作り直す。**Remove**、オブジェクトの削除、Undo（Ctrl+Z）で元に戻せる。
- 作ったオブジェクトは Modular Avatar のコンポーネントだけでできているので、このパッケージを消しても動く。プレハブにして他のアバターに使い回してもよい。

詳しくは [docs/AVATAR_SETUP.md](https://github.com/tetroid256/violin-passthrough-vrc/blob/main/docs/AVATAR_SETUP.md)。

## 注意

Unity を動かせない環境で書いたもので、コンパイルは参照用の DLL（Unity 2021.1 の UnityEngine / UnityEditor、VRChat SDK 3.7.6）と、Modular Avatar 1.11.0 と 1.18.7 のソースから作ったものに対してしか確認していない。うまく動かないときは [Issue](https://github.com/tetroid256/violin-passthrough-vrc/issues) で知らせてほしい。
