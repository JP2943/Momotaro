# P6A 最終確認の証跡（コード SHA `937ad6e9921cd80b3ab253859721d4df403544c8`）

`Momotaro_P6A_Review_720161d.md` の依頼 4（最終 SHA に対応する最小証跡一式）。記録は `P6A_統合受入結果.md` 記録 007 §6・§7。

## 対象と手順

- 対象：`phase/6-progression-save` の `937ad6e`（記録 007 のコードと記録）。この証跡はその次のコミットで足した（証跡のコミットはコード・テスト・Data・Scene を変えない）。
- 実行前に、コードとアセット定義のパス（`Assets/_Project/Scripts`・`Tests`・`Data/Tests`・`Scenes`、直下の `*.md`・`*.json`）が `937ad6e` と一致すること（`git status` で差分なし）を確かめた。
  LFS の素材は PC 側シェルの git に LFS が無いため `M` に見えるが、実体は変わっていない（CLAUDE.md「git の見え方について」）。
- 実行はすべて Unity 6000.3.20f1 の Editor 常駐ブリッジ（`_bridge/command.json`）から。順に `uc`→`u1`→`u2`→`u3`→`u4`→`ue`→`up1`〜`up5`→`uvP4`〜`uvP6A`→`us`。
- 注意：`u1`（`build-phase6-world`）は P6A の Scene を、`ue`（EditMode 全件）は P5 の試遊 Scene を作り直す（Builder の出力。内部 ID の並びが変わる）。
  テストはその作り直した Scene で走っている。実行後、作業ツリーは `937ad6e` の内容へ戻した。

## 中身

| パス | 内容 |
|---|---|
| `results/<id>.json` | 各実行の `result.json`（状態・件数・終端・失敗の詳細） |
| `runs/<id>.json` | テスト実行の**葉テスト全件**（完全名・状態・Skip 理由）。`ue`（EditMode 1872 件）、`up1`〜`up5`（PlayMode） |
| `results/uvP4.json`〜`uvP6A.json` | 必須テスト一覧（`P4RequiredTests.json` ほか）と `ue,up1,up2,up3,up4,up5` の照合結果 |
| `results/us.json` | 実ビルド確認（`p6a-player-smoke`）の出力全文（ビルド・各プロセスの結果・期待との照合） |
| `smoke/result_*.json` | 実ビルドの各プロセスの結果（new／continue／close／continue_after_close／play／play_nosave／play_probe／perf／perf_slow200） |
| `vfx/p6a27_vfx_observed.json` | 主人公の剣閃の実表示の観測（`up5` の `AttackVfx_RecordsShownTiersAndDirections`） |
| `vfx/p6a27_enemy_vfx_observed.json` | 敵側 VFX の実表示の観測（`up5` の `EnemySideVfx_ShowFromRealAttacks_InAAndAfterTransitionInC`） |

## 結果の要約

| 実行 | 結果 |
|---|---|
| `uc` | コンパイル済み（変更なし） |
| `u1`→`u2` | P6A 検証ワールドの生成と検査：合格（警告 1：A に遭遇戦なし＝構成どおり） |
| `u3`／`u4` | P5／P5.5 の検査：合格（警告 1／2。従来どおり） |
| `ue` | EditMode 全件 1872／1872（予定 1872、終端 completed） |
| `up1`／`up2`／`up3`／`up4`／`up5` | PlayMode 64／13／72／115／20（P6AWorldPlayTests）。全件 Passed |
| `uvP4`／`uvP5`／`uvP55`／`uvP6A` | 必須 226／226・93／93・293／293・73／73、未説明の Skip なし |
| `us` | 実ビルド確認：合格（正常終了・通常の終了要求の別プロセス Continue、実プレイ中と連打の性能） |
