# Violin Passthrough（Modular Avatar メニュー）

VRChat のアバターに、Violin Passthrough を操作するメニュー（OSC 用のアバターパラメータ 5 個とトグル）を、[Modular Avatar](https://modular-avatar.nadena.dev/) を使って非破壊で追加する Unity エディタ拡張。

## 必要なもの

- Unity 2022.3（VRChat の指定バージョン）
- VRChat SDK - Avatars 3.5.0 以降
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
├─ Preset                    サブメニュー
│  ├─ Fingerboard            Toggle  HandPassthroughPreset = 0
│  └─ Whole                  Toggle  HandPassthroughPreset = 1
├─ Calibrate                 Toggle  HandPassthroughCalibrate = 1
└─ Debug                     Toggle  HandPassthroughDebug = 1
```

- パラメータはすべてローカル専用（同期しない）・保存しない・既定値 0。
- アバター本体の Expressions Menu / Expression Parameters アセットは変更しない。アップロード時に Modular Avatar が合成する。
- もう一度 Add / Update を押すと、同じ名前のオブジェクトを作り直す。**Remove**、オブジェクトの削除、Undo（Ctrl+Z）で元に戻せる。
- 作ったオブジェクトは Modular Avatar のコンポーネントだけでできているので、このパッケージを消しても動く。プレハブにして他のアバターに使い回してもよい。

詳しくは [docs/AVATAR_SETUP.md](https://github.com/tetroid256/violin-passthrough-vrc/blob/main/docs/AVATAR_SETUP.md)。

## 注意

Unity を動かせない環境で書いたもので、コンパイルは参照用の DLL とスタブに対してしか確認していない。うまく動かないときは [Issue](https://github.com/tetroid256/violin-passthrough-vrc/issues) で知らせてほしい。
