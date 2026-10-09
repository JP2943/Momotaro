# 引き継ぎ書：Phase 3 完了 → Phase 3.5（戦闘縦切り試遊版）移行

作成日 2026-08-15 / 対象 Unity 6000.3.20f1(Windows) / ブランチ `phase/3-enemy-combat` / 最新 Commit `0305949 SpriteFix Player`

このスレッドが長大化したため、次スレッドへ引き継ぐ。次の担当は **Phase 3.5 タスク仕様書 v1.0**（`桃太郎プロジェクト_Phase3.5_戦闘縦切り試遊版_タスク仕様書_v1.0.docx`）の P3.5-01 から着手する。

---

## 1. これまでの到達点（Phase 3：敵AI・敵戦闘）

P3-06〜P3-12 を実装・受入済み。各機能は本ブランチにコミット済み（P3-09/P3-11 の GPT 受入は一部「後回し」指示で進めた経緯があるが、実装とテストは完了）。

- **P3-06 ヘイト/ターゲット**：`EnemyThreatTracker`／`EnemyThreatTable`／`IThreatTarget`／`ThreatSource`。`CurrentTarget` を照準に接続。
- **P3-07 攻撃スロット/画面内制御**：`AttackSlotCoordinator`／`SlotCapacities`(1/1/1)／`EnemyEncounter`／包囲リング(`SurroundRing`)／`ScreenBoundsProvider`／`OffscreenAttackPolicy`。
- **P3-08 遠距離敵**：`EnemyProjectile`(SphereCast連続判定)／`EnemyProjectileLauncher`／射線・画面端警告(`EdgeWarningMath`)／矢4方向ビルボード。
- **P3-09 強敵（侍骸骨）**：突進(Chase開始可・`EnemyMotor.SetCharge`・壁停止)／ガード不能頻度上限(`AttackFrequencyGovernor` ≤20%・初回未解禁)／Elite命名(`EnemyVisualNames` style)。
- **P3-10 敵防御・回避・撃破**：`EnemyGuardMath`(正面180°HP×0.1/体幹×1.5・背後/Special貫通)／`EnemyGuardAbility`／`EnemyEvadeAbility`／`IEnemyDangerSense`＋`PhysicsEnemyDangerSense`／`EnemyDefenseController`／撃破1回性 `EnemyDefeatChannel`＋`EnemyRewardRequest`＋`IEnemyDefeatCleanup`。
- **P3-11 仮UI・Debug・性能**：`EnemyOverheadBars`(頭上HP/体幹)／`EnemyAiDebugOverlay`(Dev限定切替)／`EnemyDebugToggle`／`EnemyProfilerMarkers`(Perception/Selection/Threat/Slot/Projectile)。
- **P3-12 統合受入**：`EnemyTestFieldController`(編成の一元管理・Context Menu)／`EnemyTestFormation`(Clear/近接1/遠距離1/強敵1/3体混成/近接6/混成6/最大8)／Editorツール `Phase3EnemyTestFieldBuilder`（メニュー `Momotaro > Phase 3 > Generate Enemy Test Field` → `Assets/_Project/Scenes/Tests/SCN_Phase3_EnemyTest.unity`）。
- **スプライトサイズ調整（直近）**：Momotaro 全モーションを新素材へ差し替え済み。**下記2章の寸法・PPU規約が確定**。

現状 `main` 未マージ。Phase 3 + 3.5 をまとめて PR する方針（仕様書§16）。独断で別基点へ Rebase しない。

---

## 2. スプライト取り込みパイプライン（重要・Phase3.5でも踏襲）

ArtSource(`ArtSource/Prototype/Player/Momotaro/<Motion>/`) が正本。Unity 反映の型は2種類。

- **シート型**（Idle/Move/Guard）：`Assets/.../Sprites/momotaro_<motion>_4dir_Nframe.png` を、`.meta` のスライス矩形どおりに個別フレームで**再パック**。フレーム寸法がグリッドと一致すれば `.meta` 不変。寸法が変わる場合は `.meta` の rect も更新（spriteID は保持＝アニメクリップ不変）。
- **個別型**（Hurt/GuardBreak/Step/Attack1-3/Special）：`Sprites/<Motion>/*.png` を1枚ずつ**上書きコピー**。Single sprite なので寸法変化でも `.meta` 不変で可（例外は下記 Special）。

**確定した寸法・規約（全て PPU100・alignment 7=BottomCenter・pivot(0.5,0)・FullRect・接地下端4px）**：

| モーション | 形式 | フレーム | 寸法 |
|---|---|---|---|
| Idle | シート 512×512 | 4方向×4 | 128 |
| Move | シート 768×512 | 4方向×6 | 128 |
| Guard | シート 768×768 | 4方向×4 | 192 |
| Hurt | 個別 | 4方向×3 | 192 |
| GuardBreak | 個別 | 4方向×4 | 192 |
| Step | 個別 | 4方向×4 | 192 |
| Attack1/2/3 | 個別 | ×5/×5/×6 | 192 |
| Special/Attack | 個別 | 4方向×7 | 256 |
| Special/Charge | 個別 | 4方向×4 | 192 |

**注意（Special の meta 正規化）**：Special は旧来トリミング前提の可変 pivot＋可変 PPU(253/95・alignment 9 Custom) だった。新一律フレームに合わせ **全44枚の meta を alignment7/pivot(0.5,0)/PPU100 へ正規化**した（他モーション統一）。これに伴い import テストを更新済み（3章参照）。今後 Special を触る際はこの規約を維持。

素材差し替え時のテスト整合：`AttackAssetImportTests`／`PlayerActionSpriteImportTests`／`PlayerSpecialSpriteImportTests` が寸法・PPU・pivot をアサートするので、寸法変更時はこれらも更新する。

---

## 3. 既存の主要契約（Phase 3.5 で再利用すべきもの）

- 命中経路：`IDamageable.ReceiveHit(HitInfo)` 単一経路。`HitResultChannel`／`HitResult`／`HitInfo`(DefenseIgnoreRatio=Special・Steppable 等)。
- Player 側：`PlayerStateController`（`ICombatActor`/`IGuardState`/`IJustGuardState`/`IEvadeState`/`ISpecialChargeCancel`/`IAttackThreatSource` 実装）、`PlayerVitalsHolder`／`PlayerVitals`、`AttackComboMachine`、`StepState`、`SpecialChargeState`、`JustGuardState`、`GuardResolver`／`GuardGeometry`。
  - **PlayerState 現状 = Idle/Move/GuardIdle/GuardMove/Attack/GuardBreak/Step/SpecialCharge/Special**。**Hurt/Defeated は未実装**（P3.5-01/02 で追加する対象）。
- 攻撃側の危険観測契約 `IAttackThreatSource`（IsThreateningAttack / IsUnblockableThreat / ThreatForward）を Player が実装済み。敵防御はこれを読む。
- 敵側：`EnemyActor`(`Defeats`=`EnemyDefeatChannel`、`States`=`EnemyStateChannel`、`Results`=`HitResultChannel`)、`EnemyBrain`／`EnemyMotor`／`EnemyPerception`／`EnemyAttackController`／`EnemyThreatTracker`／`EnemyDefenseController`。`PoiseState`(Stun3s/HP×1.25/復帰耐性)。
- モード：`GameModeProvider`/`IGameModeService`/`GameMode`(Exploration/Combat)。`GameplaySceneMode` がシーン進入時に Exploration を要求し Player 操作可能化。
- 診断：`EnemyOverheadBars`(P3.5でも維持)、`CombatDebugHud`/`EnemyAiDebugOverlay`(開発者向け・試遊HUDとは分離すること=§6.3)。
- Scene生成の作法：`Phase3EnemyTestFieldBuilder` を範とする（`NewSceneMode.Single`で生成→保存、資産欠落は保存せず失敗、再生成で非増殖、テストは `GetSceneManagerSetup`/`RestoreSceneManagerSetup` で元Sceneを保護し未保存Sceneは `Assert.Ignore`）。P3.5-06 の `Phase35CombatTrialBuilder` は既存を変更せず安全Helperのみ共有。
- 敵Prefab：`PF_Enemy_Melee_Prototype`(骸骨剣士 guid `7f9bddb6…`)／`PF_Enemy_Ranged_Prototype`(骸骨弓兵 `d7ca9deb…`)／`PF_Enemy_Elite_Prototype`(侍骸骨 `3949dd5a…`)。Player `PF_Player_Momotaro`(`a0e50895…`)。

---

## 4. Phase 3.5 タスク概要（次スレッドのロードマップ）

1回1 Task・1 Commit。後続 Task を先回りしない。基準は `phase/3-enemy-combat` 最新受入 Commit。

| Task | 名称 | 主成果 |
|---|---|---|
| P3.5-01 | プレイヤーHurt | PlayerStateへHurt追加(0.30s硬直/0.50s被弾後無敵)、全行動中断・Cleanup共通化、4方向Hurt表示 |
| P3.5-02 | プレイヤー死亡 | Defeated状態(入力停止)、致死1回通知、敵Target/Perception無効化・攻撃停止・Slot解放、Hurt最終Frame保持+低彩度の仮死亡 |
| P3.5-03 | 戦闘セッション基盤 | `CombatSessionController`：Preparing/Playing/Intermission/Victory/Defeat/Reloading、型付き購読、敵登録・生存数、二重防止 |
| P3.5-04 | 試遊HUD | `CombatTrialHud`(Debug HUDと分離)：HP/Stamina/GuardBreak/Special/Wave/勝敗/Retry、入力設定と整合した操作ガイド |
| P3.5-05 | 戦闘フィードバック | `CombatFeedbackPresenter`：HitResult別 Flash/HitStop/CameraShake/仮SE、JGを強調、Enemy Defeat仮Fade |
| P3.5-06 | 試遊Scene生成 | `Phase35CombatTrialBuilder` → `Assets/_Project/Scenes/Tests/SCN_Phase35_CombatTrial.unity` |
| P3.5-07 | 連続ウェーブ | Wave1剣士×1/2弓兵×1/3剣士×2+弓兵×1/4侍骸骨×1、全滅1.0s→Intermission3.0s→次Wave、Wave間HP/Stamina全回復+中立化+Special0 |
| P3.5-08 | 勝敗・リトライ統合 | 最終全滅Victory/死亡Defeat、結果パネル(0.75s後)、入力ロック、Retry誤入力防止0.50s、同一SceneのAsync再読込・二重防止 |
| P3.5-09 | バランス・安定性 | 実機記録で数値調整(Dataへ)、Profiler/60秒/3周Retry/Pause/Focus復帰/残留なし |
| P3.5-10 | 配布・統合受入 | README・起動手順・既知制約・フィードバック票、Validator、重複HUD/Session除去 |

Wave/Sessionの詳細状態・許可入力・フィードバック表現・時間境界は仕様書 §3〜§10 と Table3〜7 が正本。**対象外**（§0.3）：犬猿雉/仲間AI、本編マップ/会話/成長/装備、完成セーブ・UI・VFX・SE・BGM、Player体幹ゲージ、完成死亡演出、製品版Encounter。

---

## 5. 実装・環境の作法（この環境固有の注意）

- **Unity は本環境で実行不可**。コンパイル・TestRunner・Editorツール実行・Scene生成はすべて**ユーザーが Unity 上で実施**。Claude は静的検証（brace一致・meta整合・GUID重複・参照）まで行い、結果は推測で「成功」と報告しない。
- **.cs.meta の GUID = md5(assetパス)[:32]**（決定的）。フォルダ meta は folderAsset。新規 .cs 追加時は必ず meta を生成。
- **ファイル削除**：bash `rm` は初回 `Operation not permitted`。`mcp__cowork__allow_cowork_file_delete` を1回呼ぶと以降 `rm` 可能。上書き(`cp`/Write)は常時可。
- **層規約**：Core←Data←Gameplay←Presentation(+Infrastructure/Editor/Tests)。Gameplay は Animator/Canvas/Scene API/Camera を直接触らない（Adapter/Provider/interface 経由）。static 万能マネージャ禁止。
- **テスト**：PlayMode asmdef は UnityEditor 非参照（AssetDatabase不可）。実Prefab数値検証は EditMode(AssetDatabase)で、PlayModeは実行時ライフサイクル/例外なしで担保する分担。時間境界は直前/一致/直後、`yield return null`の偶然に依存しない。
- **後始末**：Disable/Scene離脱/Retry で購読・Coroutine・Hitbox・Projectile・Slot・Feedback を残さない（§2.3/§5.2）。
- **Git**（§16）：Push は都度ユーザー。P3.5-10 受入後に Phase3+3.5 の PR。ブランチ削除はユーザー判断まで行わない。

---

## 6. 既知の制約・申し送り

- Phase 3 の一部（P3-09/P3-11）は GPT 受入を「後回し」で進行。PR 前に再確認が必要な可能性。
- Special の meta を PPU100/BottomCenter へ正規化した（従来「metaは触らない」方針からの意図的逸脱、素材一律化のため必須だった）。見た目サイズが意図と違う場合は PPU で調整可能。
- 試遊 HUD は入力キー名を**コードに重複定義せず**実 Input 設定から取得する（§6.1）。現行 Input は Input System パッケージ。旧 `UnityEngine.Input` を使うと実行時例外の恐れ。
- 死亡表示は仮（現Facing Hurt最終Frame保持＋低彩度）。README へ仮仕様明記（§4.2）。
- `SCN_VS_Field`（ユーザー手動配置）と `SCN_Phase3_EnemyTest` は Phase3.5 で変更しない。試遊は新規 `SCN_Phase35_CombatTrial`。

---

## 7. 次スレッドの最初の一手（推奨）

1. `phase/3-enemy-combat` を Pull し、未Push・未追跡がないか確認。Unity でコンパイル0・EditMode/PlayMode 全緑を確認。
2. 依頼テンプレ（§15）で **P3.5-01 のみ**着手：PlayerState へ Hurt 追加＋被弾中断共通化＋4方向Hurt表示＋境界テスト。既存 `PlayerStateController`/`PlayerVitalsHolder`/`HitResultChannel` を再利用し、Animator/UI を正本化しない。
3. 完了報告に Commit hash・変更ファイル・設計判断・テスト件数/結果・Unity操作・既知制約を記載。
