# アバターへのメニューの追加

本アプリは、VRChat のアバターパラメータを OSC で受け取って動く。また、今の状態を VRChat に送り返すので、アバターメニューの表示が実際の状態と合う。そのため、使うアバターすべてに次の 5 つのパラメータと、それを操作するメニューを入れる。アニメーター（レイヤー）の変更は要らない。

- **A. Modular Avatar を使う（おすすめ）**: 同梱のエディタ拡張でまとめて追加できる。
- **B. 手で設定する**: Modular Avatar を使わない場合。

## パラメータ

| パラメータ名（既定） | 型 | 内容 | config.json のキー |
|---|---|---|---|
| `HandPassthrough` | Bool | パススルーの窓の表示 ON/OFF | `osc.parameters.enabled` |
| `HandPassthroughPreset` | Int | プリセット番号。`config.json` の `presets` に書いた順（0 始まり）。既定の設定では 0 = fingerboard、1 = whole。範囲外の番号は無視する | `osc.parameters.preset` |
| `HandPassthroughDebug` | Bool | マスクの窓を色付きで表示する（位置合わせ用） | `osc.parameters.debug` |
| `HandPassthroughAuto` | Bool | 自動モード（楽器を構えている間だけ窓を出す） | `osc.parameters.auto` |
| `HandPassthroughCalibrate` | Bool | ON でカウントダウン（既定 5 秒）の後にキャリブレーションする。終わると本アプリが OFF に戻す。カウントダウン中に OFF にすると取り消す | `osc.parameters.calibrate` |

5 つとも次の設定にする。

- **同期しない**（Synced のチェックを外す＝ローカル専用）。他のプレイヤーには見えず、同期パラメータの容量（256 bit）を消費しない。
- **保存しない**（Saved のチェックを外す）。
- **既定値 0**（Bool は false）。

パラメータ名は `config.json` の `osc.parameters` と完全に一致させる（大文字・小文字も区別する）。名前を変えるときは、アバターと `config.json` の両方を変える。

## A. Modular Avatar を使う

[Modular Avatar](https://modular-avatar.nadena.dev/)（MA）は、アバターに置いたコンポーネントから、アップロード時にパラメータとメニューを組み込むツール。元のアバターの Expressions Menu / Expression Parameters アセットは書き換えない。

### 準備

1. VCC（VRChat Creator Companion）か ALCOM で、アバターのプロジェクトに Modular Avatar（1.11.0 以降、2.0 未満）を追加する。
2. このリポジトリの `unity/com.tetroid256.violin-passthrough/` フォルダ（GitHub の Code > Download ZIP か、Actions の Artifact `ViolinPassthrough-unity-package` で取得できる）を、Unity プロジェクトの `Packages/` フォルダにフォルダごとコピーする。
   - Unity の Window > Package Manager > ＋ > Add package from disk... で、このフォルダの `package.json` を選んでもよい。この場合は、フォルダを移動・削除しないこと。
3. Unity に戻ると読み込まれ、メニューに **Tools > Violin Passthrough** が出る。出ないときは、Modular Avatar と VRChat SDK - Avatars が入っているか、Console にエラーが出ていないかを確認する。

### メニューの追加

1. シーン上のアバターを選び、**Tools > Violin Passthrough > Add OSC Menu to Avatar** を開く。ヒエラルキーでアバター（またはその中のオブジェクト）を右クリック > **Violin Passthrough > Add OSC Menu** でも開ける。
2. 画面の項目を確認する。
   - **Avatar**: 追加先のアバター（VRC Avatar Descriptor があるオブジェクト）。
   - **Menu label**: ルートメニューに出るサブメニューの名前（既定 `Violin Passthrough`）。
   - **Auto / Calibrate / Debug**: それぞれのトグルをメニューに出すか。外してもパラメータは宣言する（OSC での状態の送り返しはそのまま）。
   - **Avatar parameters**: パラメータ名。`config.json` の `osc.parameters` に合わせる（既定のままなら変更不要）。
   - **Presets**: プリセットのトグル（表示名と番号）。番号は `config.json` の `presets` の順番。既定は Fingerboard = 0、Whole = 1。プリセットを増やしたら行を足す。
3. **Add / Update** を押す。アバターの直下に `ViolinPassthrough OSC Menu` ができて選択される。
4. いつもどおりアップロードする。

作られるもの:

```
ViolinPassthrough OSC Menu   MA Parameters（5 個）+ MA Menu Installer + MA Menu Item（サブメニュー「Violin Passthrough」）
├─ Passthrough               MA Menu Item: Toggle  HandPassthrough = 1
├─ Auto                      MA Menu Item: Toggle  HandPassthroughAuto = 1
├─ Preset                    MA Menu Item: サブメニュー（子オブジェクトから作る）
│  ├─ Fingerboard            MA Menu Item: Toggle  HandPassthroughPreset = 0
│  └─ Whole                  MA Menu Item: Toggle  HandPassthroughPreset = 1
├─ Calibrate                 MA Menu Item: Toggle  HandPassthroughCalibrate = 1
└─ Debug                     MA Menu Item: Toggle  HandPassthroughDebug = 1
```

### 補足

- もう一度 **Add / Update** を押すと、`ViolinPassthrough OSC Menu` を消して画面の設定から作り直す（パラメータ名やプリセットを変えたとき）。**ヒエラルキーで変えたトグルの名前、付けたアイコン、中に足したものは消える**（Undo（Ctrl+Z）で戻せる）。手で直したものを残したいときは、Add / Update を使わずに MA Parameters などを直接編集する。**Remove**、オブジェクトの削除、Undo で元に戻せる。
- プリセットのトグルは、画面に並べた順にメニューに並ぶ。
- トグルの表示名は GameObject の名前。ヒエラルキーで名前を変えればメニューの表示も変わる（ルートのサブメニューの名前だけは MA Menu Item の Name 欄）。
- プリセットのトグル: 押すとその番号になる。選ばれているトグルをもう一度押すと 0（先頭のプリセット）に戻る（VRChat の Int パラメータと Toggle の仕様）。
- アバターの Expression Parameters アセットに同じ名前のパラメータが既にあると、MA はそちらと合わせる（Synced / Saved はどちらかが ON なら ON、型が違うとエラー）。手で追加していたものは消しておく。ツールはこの場合に警告を出す。
- ルートメニューが 8 項目を超えると、MA が自動で「More」に分ける。
- アバターがプレハブのインスタンスなら、作ったオブジェクトはプレハブへの追加（オーバーライド）になる。プレハブ本体に入れたいときは Overrides > Apply する。プレハブ本体に入った後で作り直すときは、プレハブを開いて（Prefab Mode）そこでツールを使う。
- 作ったオブジェクトは MA のコンポーネントだけでできているので、このパッケージを消しても動く。プレハブにして他のアバターに使い回してもよい。
- MA 2.0 以降では、互換性を確認していないのでツールが無効になる（メニューに出ない）。

## B. 手で設定する（Modular Avatar を使わない）

1. アバターの VRC Avatar Descriptor > Expressions > **Parameters** のアセットに、5 つを追加する。

   | Name | Type | Default | Saved | Synced |
   |---|---|---|---|---|
   | `HandPassthrough` | Bool | false | オフ | オフ |
   | `HandPassthroughPreset` | Int | 0 | オフ | オフ |
   | `HandPassthroughDebug` | Bool | false | オフ | オフ |
   | `HandPassthroughAuto` | Bool | false | オフ | オフ |
   | `HandPassthroughCalibrate` | Bool | false | オフ | オフ |

2. Expressions Menu を 2 つ新しく作る（Project ウィンドウで右クリック > Create > VRChat > Avatars > Expressions Menu）。ここでは「Violin Passthrough」と「Violin Passthrough Preset」とする。
3. アバターの **Menu**（ルートメニュー）に、Type = Sub Menu、Sub Menu = 「Violin Passthrough」のコントロールを追加する。
4. 「Violin Passthrough」に次を追加する。

   | Name | Type | Parameter | Value |
   |---|---|---|---|
   | Passthrough | Toggle | `HandPassthrough` | 1 |
   | Auto | Toggle | `HandPassthroughAuto` | 1 |
   | Preset | Sub Menu（「Violin Passthrough Preset」） | なし | - |
   | Calibrate | Toggle | `HandPassthroughCalibrate` | 1 |
   | Debug | Toggle | `HandPassthroughDebug` | 1 |

5. 「Violin Passthrough Preset」に、プリセットごとに Toggle を追加する（Parameter は `HandPassthroughPreset`、Value はプリセット番号）。既定の設定なら Fingerboard（Value 0）と Whole（Value 1）。
6. アップロードする。

1 つのメニューに置けるのは 8 項目まで。ルートメニューが埋まっているときは、既存のサブメニューの中に置く。

## アップロード後の確認

1. VRChat の Action Menu > Options > OSC > **Enabled** を ON にする。
2. 本アプリを起動し、アバターメニューの **Violin Passthrough > Passthrough** を ON にすると窓が出る。本アプリが状態を送り返すので、メニューの表示も実際の状態に合う（アバターを変えても状態は保たれ、本アプリが送り直す）。

### うまくいかないとき

- **メニューを操作しても本アプリが反応しない／メニューの表示が合わない**:
  - パラメータ名が `config.json` の `osc.parameters` と一致しているか確認する。
  - VRChat は、アバターごとの OSC の設定（どのパラメータを送受信するか）をファイルに保存して使い回す。パラメータを追加・変更してアップロードし直しても、古い設定のままになることがある。アバターを更新したら、Action Menu > Options > OSC > **Reset Config** を実行する（DESIGN.md §9）。それでも直らなければ、VRChat を終了して `%USERPROFILE%\AppData\LocalLow\VRChat\VRChat\OSC\` の中にある、そのアバターの設定ファイル（`usr_...\Avatars\avtr_....json`）を消してから起動し直す。
- **他のプレイヤーに見えないか心配**: パラメータはローカル専用なので同期されない。窓は自分の VR 表示にだけ出る。
- **Modular Avatar のビルドでエラーが出る**: Console と MA のエラー表示を確認する。よくあるのは、アバターの Expression Parameters に同じ名前で型の違うパラメータがある場合（上の「補足」）。

## このエディタ拡張について

Unity を動かせない環境で書いたもので、コンパイルは参照用の DLL（Unity 2021.1 の UnityEngine / UnityEditor、VRChat SDK 3.7.6）と、Modular Avatar 1.11.0 と 1.18.7 のソースから作ったものに対してしか確認していない。実際の Unity での動作は未確認。うまく動かないときは [Issue](https://github.com/tetroid256/violin-passthrough-vrc/issues) で知らせてほしい。
