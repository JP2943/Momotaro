# 引き継ぎ書：P6A 完了 → P6B 移行

作成：2026-10-02 ／ ブランチ `phase/6-progression-save`（main へは未マージ）

## 0. いまどこ

- **P6A（進行・休息・保存基盤）は受入**（GPT 検査合格。記録 008）。最終確認の対象は `937ad6e`、証跡は `Evidence/P6A_937ad6e/`（`86e1b70`）。
- **P6 全体は未完了。** P6B は「承認済みの成長ツリーと回復ゲームプレイを統合し、探索から成長への循環を受入」（仕様 §14）。
- **P6B の前提（並行設計）は未決**：成長ツリーの構造・技・数値・費用・振り直し・本番 UI、きびだんごの使用入力・使用時間・回復量・モーション、
  一般消耗品の使い方、章ボス後の進行。**未決のまま埋めて実装しない**（仕様 §12 末尾・§15）。P6B の仕様が承認されてから着手する。
- 人間確認（`README_Phase6A_進行保存試遊.md` §4）の結果は未記録。

## 1. 読むもの

| 目的 | ファイル |
|---|---|
| 仕様の正本 | `桃太郎プロジェクト P6先行実装仕様 v1.0 承認用.md`、`桃太郎プロジェクト コアループ定義 v1.0.md` |
| 受入記録 | `P6A_統合受入結果.md`（記録 001〜008） |
| 受入条件と証跡 | `P6A_受入条件対応表.md`、`P6ARequiredTests.json`（73 名）、`Evidence/P6A_937ad6e/` |
| 保存の台帳 | `P6_SaveInventory.md`（保存形式の版 2） |
| 残件 | `P6A_後続課題.md`（P6B へは F08 成長・F09 きびだんごと消耗品） |
| 性能 | `P6A_性能測定記録.md` |

## 2. P6B が繋ぐ先（P6A で用意した境界）

| 領域 | 入口 | 守ること |
|---|---|---|
| 成長 | `SkillNodeData`（既存資産を再利用）→ `CampaignCatalog.GrowthNodes`／`TryGetGrowth`、取得は `ShrineProcedures.PurchaseGrowth`（お地蔵様の操作中だけ） | 不成立は全体無変更。成功時だけ実支出を記録し、成長を反映した最大値で休息。**効果は基礎値と取得 ID から再計算**（`MaxHpBonusOf`。加算を繰り返さない）。保存は取得 ID と実支出（ツリーの形を保存形式に固定しない） |
| きびだんご | 残数 `GameSessionState.Kibidango`、`TryConsumeKibidango`、`RefillKibidango`、上限 `CampaignCatalog.KibidangoCapacityOf(progress)`（いまは基本値だけ） | 上限は定義と成長から算出して保存しない。使用は P6B の決定に従う |
| 一般消耗品 | `InventoryState`（取得・消費は原子的） | 未知 ID・負数・上限超過を拒否。休息・死亡で消費済みを戻さない |
| クエスト・章 | `GameSessionState.TrySetQuestStage`、保存の `questStages`（既知 ID は `AreaCatalogData.QuestIds`） | P7 の担当。死亡・休息・周期で戻らない |
| 保存 | `SaveSnapshot.Capture` → `SaveJsonCodec`（厳格な読み）→ `SaveSnapshotValidator` → `SessionRestorer` | **欄を足したら版を上げ、読みは版ごとに欄の一覧を分ける**（版 1→2 の `questStages` と同じ。欠けた欄を黙って補わない）。`P6_SaveInventory.md` を先に更新し、`RoundTrip_*` に非初期値を足す。既知 ID の一覧（報酬・仲間・クエスト）を Data と Validator で揃える |
| 休息 | `ShrineProcedures.Rest`（全回復・補充・周期 +1・普通敵の作り直し・保存要求を 1 回） | 回復の中身が増えても 1 回の確定にまとめる |

## 3. 踏んだもの（P6A で実際に起きた）

- **欠けた配線は単体テストでは見えない。** 遭遇戦の無い Area に命中 Feedback の配信役が無く、JG 閃光が一度も出ていなかった（記録 007 §2）。表示は実攻撃・実入力から確かめる。
- **仮 UI（IMGUI）の文字を増やしたら `PadMenuNavigator.PrewarmCharacters` に足す。** 日本語を初めて描くフレームで約 300ms 止まった（性能測定記録 §6）。
- **版の変わらない変化**（HP・残時間など）は「未保存か」で判断しない。終了要求は冒険中なら必ず保存を通す（記録 006 §1）。
- **旅立ちは出発を保持する**（別 Area は Additive、同じ Area は置き直し）。P5 の Single 経路へ戻さない。
- 実ビルドの自動確認の仮想キーボードは、窓が前面に無いと止まる（`IgnoreFocus` を設定済み）。
- テスト専用の調整（敵の攻撃 2 倍・HP 半分）は P6A のカタログだけ。P6B の検証 campaign で使うかは決めてから。
- P5・P5.5 の試遊 Scene には攻撃 VFX を入れない（オーナー判断 2026-10-02）。

## 4. 運用（これまでどおり）

- 常時許可：PlayMode／EditMode のテスト・Builder・検証器は都度確認なしで回してよい（実行したことは報告に書く）。
- PlayMode 全件は 5 分割（P5.5 スライド／往復／P5.5 その他／その他 26 クラス／`P6AWorldPlayTests`）。
- EditMode 全件は P5 の試遊 Scene を、`build-phase6-world` は P6A の Scene を作り直す（内部 ID の並び替えだけの差分）。
- PC 側シェルの git は `--no-optional-locks`。コミットで残るロックは `_to_delete/git_locks/` へ退避。
