# Changelog

## [0.2.0] - 2026-10-26

- 自動モードの判定方法を選ぶ「Auto Method」（`HandPassthroughAutoMethod`、Int、ローカル）を追加。
- バイオリンのモデルの「Violin」（Away / Hold / Play。`HandPassthroughViolin`、Int、ローカル）と、アプリが送るモデルの状態 `HandPassthroughViolinState`（Int、**同期**）を追加。画面の Violin で外せる。

## [0.1.0] - 2026-10-26

- 最初の版。Modular Avatar のコンポーネントで OSC 用メニュー（ローカル専用パラメータ 5 個とトグル、プリセットのサブメニュー）をアバターに追加するエディタウィンドウ（Tools > Violin Passthrough > Add OSC Menu to Avatar）。
