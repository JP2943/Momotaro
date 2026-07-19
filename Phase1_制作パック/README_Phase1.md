# Phase 1 制作パック

Version 1.4：桃太郎の仮Guard素材（4方向×4枚）を追加し、Phase 1でIdle／Move／Guardの実素材を使用する構成へ変更。

## 目的

狭い検証マップを、4方向表示の桃太郎で気持ちよく移動できる状態まで完成させます。Phase 1では戦闘を実装しません。

## 使用順

1. Idle 16枚を`ArtSource/Prototype/Player/Momotaro/Idle`、Move 24枚を`ArtSource/Prototype/Player/Momotaro/Move`、Guard 16枚を`ArtSource/Prototype/Player/Momotaro/Guard`へ配置する。
2. `Sprite_Handoff_Phase1.md`に従い、Claudeへ素材受入タスクを依頼する。
3. `Phase1_プレイヤー基本操作_タスク票.docx`をClaudeへ共有する。
4. `Claude_Phase1_実装依頼.md`の共通指示と、今回実行するTaskだけをClaudeへ渡す。
5. Task完了後、Unityで自動テストと手動受入を行う。
6. 問題がなければ1 Task 1 Commitで記録する。
7. P1-12受入後にPhase 1ブランチをmainへ統合する。

## 最初の作業

`P1-01 Player基礎Prefab`から開始してください。P1-01の受入が終わるまでP1-02以降は依頼しません。

## 保留事項

- 不要Packageの整理
- Debug Action Mapの常時併用方式
- 本番Spriteおよび完成Animation
- 攻撃、ガード判定、ステップ等の戦闘機能
