# 証跡：人間試遊の報告「使用中に被弾しても回復した」の切り分け（対象コード `30bd6a6`）

記録の本文は `P6B_統合受入結果.md` 記録 005。前回の証跡は `Evidence/P6B_47b223e/`（記録 004。製品コードはそこから変更なし）。

- 実行はすべて Unity 6000.3.20f1 の Editor 常駐ブリッジから、作業ツリーを `30bd6a6` の内容にした状態で。最終確認は `le`→`lp`→`vlP6B`。
- `runs/` は各テスト実行の葉テスト全件。`results/` はブリッジの結果。

| ファイル | 内容 |
|---|---|
| `p6b_kibidango_hit_timeline.txt` | `lp` の実攻撃テストが書いた時系列（条件・試行ごとの使用開始／かばい／被弾結果／確定／終了の時刻、HP、残数） |
| `runs/le.json` | EditMode `P6B(Growth\|KibidangoUse)Tests` 24／24 |
| `runs/lp.json` | PlayMode `P6BWorldPlayTests` 9／9 |
| `results/vlP6B.json` | 必須一覧の照合 P6B 33／33、未説明 Skip なし |
| `runs/h3p.json` | 実攻撃テスト作成中の実行（確定後の被弾を観測した回。時系列ファイルは上書き済み） |
| `runs/kp.json` | `Kibidango_AtBoundary_*` が 1 回落ちた実行（記録 005 §7。未解明） |
| `runs/kh2.json` | 実攻撃テストで使用開始 0 回の試行を不合格にしていた版の実行（記録 005 §6） |

## sha256（この README の最後の編集の後に採取）

| ファイル | sha256 |
|---|---|
| `p6b_kibidango_hit_timeline.txt` | `243b586030a91f68f0152e53cd90a2d1d0b7f45f24493c1baa72a91cc9bdd522` |
| `results/vlP6B.json` | `1f90e03d1ab95e9921d48e31caec0eb9a242713baa866c6a6d39d8b8e8a5c50f` |
| `runs/h3p.json` | `689f146e2489c24229585241002f7250f061411f5152e033fa4a179e090c63ce` |
| `runs/kh2.json` | `a8a7b0d1509451ca961710bfdd1263c8c7a4b3776cd5db5690e4f671169f7342` |
| `runs/kp.json` | `3466bc970d5017a307913015dcc9fdeab0e024b5caf33eb066f2cf1bba570bb0` |
| `runs/le.json` | `c282d205c826d070431c8852f817ba46cb338727fa903fc68c4ef9046d5d3549` |
| `runs/lp.json` | `a75bd465b3d8bc1feb6d9e92f9c5ad5fd7e1a65d14f1afb5022059cf658fa039` |
