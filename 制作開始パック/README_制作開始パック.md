# 桃太郎アクションRPG 制作開始パック

対象：Unity 6.3 LTS / `momotaro_action_rpg` / Claude Cowork

## ファイル

- `桃太郎アクションRPG_ゲーム仕様書_v1.4.docx`：企画・技術上の正本
- `Claude_Cowork_制作開始ガイド.docx`：Claudeへ毎回適用する共通規則
- `Phase0_タスク票.docx`：P0-01～P0-12の作業・受入条件
- `命名・データ規約.docx`：C#、Asset、Stable ID、Prefab、SO規約
- `実装・検証チェックリスト.xlsx`：DoDとレビュー項目
- `試遊記録.xlsx`：垂直スライスの試遊結果
- `プロジェクト台帳.xlsx`：Package、外部Asset、Stable ID、章Content、Bug
- `Claude_タスク依頼テンプレート.md`：個別作業の依頼書
- `Bug_Report_Template.md`：不具合報告書

## 開始順

1. マスター仕様書と制作開始ガイドをClaudeへ提示する。
2. `Phase0_タスク票.docx`のP0-01から、小さな作業単位で依頼する。
3. 各Taskで`Claude_タスク依頼テンプレート.md`を複製して記入する。
4. 完了時に`実装・検証チェックリスト.xlsx`を確認する。
5. Package・Asset・ID・Bugを`プロジェクト台帳.xlsx`へ記録する。
6. Phase 0受入後に、マスター仕様書のPhase 1へ進む。

## 重要

- 最新のユーザー指示が最優先。
- Phase 0ではGameplayを実装しない。
- ClaudeはUnity版、Render Pipeline、Package、Stable ID、Save fieldを無断変更しない。
- Console Error 0、対象・既存テスト成功、手動確認手順の報告を完了条件とする。
