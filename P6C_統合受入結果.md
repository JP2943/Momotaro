# P6C 統合受入結果

仕様の正本：`桃太郎プロジェクト P6C ジャスト回避と防御反撃実装仕様 v1.0.md`（2026-10-09、v1.0）。
受入条件との対応は `P6C_受入条件対応表.md`、必須テストの正本は `P6CRequiredTests.json`、触りかたは `README_Phase6C_防御反撃試遊.md`、
後続へ送る事項は `P6C_後続課題.md`。

---

## 記録 001：P6C 00（基点・既存実装の棚卸し・変更する契約）

### 1. 基点

- ブランチ `phase/6-progression-save`。**親 SHA `8bb112c73966d5ba0f2e9390c52690186e9c6e17`**（P6B 記録 005。origin と一致を確認）。
  仕様 §1 の作成時点の値と同じ。main（`8e7d3c9`）は P6B を含まないので基点にしない。
- 着手時に読んだもの：`CLAUDE.md`、P6B の修正・受入記録（`P6B_統合受入結果.md` 記録 001〜005、`Momotaro_P6B_Review_ddb2d19.md`）。
  P6B の受入完了はここで宣言しない（P6B 記録 005 のとおり、最終受入はオーナー判断待ち）。
- 実行の許可：オーナーから「PlayMode・EditMode 全件・Scene Builder・Windows 実ビルドを都度確認なしで実行してよい」（2026-10-09）。

### 2. 既存のジャスト回避（置換前）

| 箇所 | 置換前の実装 | P6C での扱い |
|---|---|---|
| `StepState` | `CanJustEvade`＝ステップ中・未成功・経過 < 受付窓。`NotifyJustEvadeSuccess` で同じステップ中は閉じる。`Begin` で開き直す | **そのまま再利用**（1 ステップ 1 回の成功管理） |
| `PlayerStateController` | `IJustEvadeState` を実装。受付窓 `_justEvadeWindowSeconds=0.12` と体幹反射 `_justEvadeCounterPoise=20` を**主人公 Prefab のコンポーネント**に持つ（Prefab・Scene には直列化されておらず、コード既定値で動いていた） | 2 つの項目を撤去し、**受付窓・倍率・有効時間は `StepData` に集約**（正本を 1 つに）。成功で反撃強化を付与 |
| `PlayerVitalsHolder.ReceiveHit` | 被弾後無敵 → ステップ無敵（Steppable）→ その中で `CanJustEvade` なら**攻撃者へ体幹反射 20 ＋ 近接攻撃者へ強制ひるみ 0.35 秒**、`JustEvade` を通知 | 体幹反射と強制ひるみを**削除**し、強化付与（`NotifyJustEvadeSuccess`）へ置換。成功の対象を「敵の攻撃」に限定 |
| `IJustEvadeState` | `CanJustEvade`・`JustEvadeCounterPoise`・`NotifyJustEvadeSuccess` | `JustEvadeCounterPoise` を撤去 |
| `HitInfo` | 攻撃の出所（敵の攻撃か・環境か）を持たない。`Guardable`／`JustGuardable`／`Steppable` は独立 | 「敵の攻撃である」を命中インスタンスに載せる（射手が退場した矢でも判断できる） |
| `EnemyHitFactory.Build` | Snapshot の `Guardable`・`JustGuardable`・`Steppable` をそのまま HitInfo へ | 敵の攻撃である印を付ける。可否は上書きしない（現状も上書きしていない） |
| `EnemyAttackData.Validate` | `Unblockable` は `Guardable=false` かつ `JustGuardable=false` を要求、`Steppable=true` を要求 | **`JustGuardable=false` の要求を撤去**。原則 false／true／true。JG も不可にするのは理由を明記した例外だけ |
| 通知と音 | `HitResultKind.JustEvade` → `CombatFeedbackMap`（`VFX_JustEvade`・`SE_JustEvade`・ヒットストップ 0.07）→ `CombatFeedbackPresenter`（点滅＋小さい揺れ）。音素材 `SE_JustEvade` は P3.5 の Builder だけが配線。**P6 の Scene の CombatFeedback には SE の再生役が無い** | 通知と表示を再利用。P6C の Scene に SE の再生役を置く。Presenter は反射・ひるみをしていない（確認済み） |

### 3. 実値（着手時）

- `SO_Step_Momotaro`：移動 0.20 秒・後硬直 0.10 秒・**無敵 [0.05, 0.20)**・消費 25・連続窓 0.12・距離 3。コード既定値と同じ。
- 受付終端 0.12（コード既定）→ **ジャスト成功が起こりうるのは [0.05, 0.12) の 0.07 秒**。無敵開始前（0〜0.05）は無敵でないので回避自体が起きない。
- 主人公 Prefab `PF_Player_Momotaro` は `_stepData` を参照。`_justEvadeWindowSeconds`／`_justEvadeCounterPoise` は Prefab にも Scene にも直列化されていない（全 `.unity`／`.prefab` を検索して 0 件）。

### 4. 防御可否 Data の棚卸し

| アセット | 分類 | Guardable／JustGuardable／Steppable | 種別 |
|---|---|---|---|
| `SO_EnemyAttack_Melee_Normal` | Normal | 1／1／1 | 斬撃 |
| `SO_EnemyAttack_Elite_Normal` | Normal | 1／1／1 | 斬撃 |
| `SO_EnemyAttack_Elite_Heavy` | Heavy | 1／1／1 | 斬撃 |
| `SO_EnemyAttack_Elite_Charge` | Charge | 1／1／1 | 突進斬り |
| `SO_EnemyAttack_Ranged_Shot` | Projectile | 1／1／1 | 矢 |
| **`SO_EnemyAttack_Elite_Unblockable`** | Unblockable | **0／0／1** | 刀の突き（表示名 `UnguardableThrust`）＝打撃・斬撃系 → **0／1／1 へ変更** |

コードで攻撃 Data を生成する Builder は無い（Editor 以下に `_justGuardable` の書き込みなし）。仲間（犬丸）の被弾解決は `Guardable` だけを見て JG を持たない
（`CompanionHitReceiver`）ので、共有 Data の変更は犬丸の挙動に及ばない。敵側の JG は主人公の攻撃（`AttackData`）が対象で、敵攻撃 Data とは無関係。

### 5. 入力・時間・中立化

- 回避は既存のステップ（Space／パッドの回避）。新ボタンなし。
- `PlayerStateController.Tick` は遷移凍結（`GameplayClockProvider.IsFrozen`）で止まる。ヒットストップは `timeScale` を下げるので `deltaTime` も縮む。
  Pause・メニュー中は入力が閉じる（`Active=false`）。
- 入場・死亡再開・休息（成長・払い戻し・旅立ちの成功を含む）はすべて `PlayerStateController.ResetForAreaEntry` を通る
  （`AreaInitializer` の入場・同 Area 内の旅立ち、`AreaActorTransferPort.RestoreForCampaignRespawn`／`RestoreForRest`）。強化の消去はここ 1 か所に置く。

### 6. 置換で期待値が変わる既存テスト

| テスト | 旧期待 | 新期待 |
|---|---|---|
| `PlayerEvadeTests.JustEvade_Window_ReflectsPoise_ForcesFlinch_AndPublishesJustEvade` | 体幹反射 25・強制ひるみ 0.35 | 反射・ひるみなし、成功通知 1 回、`JustEvade` 通知 1 回（名前も変える） |
| `PlayerEvadeTests.Invincible_ButOutsideJustWindow_IsPlainEvade_NoCounter` | 反射・ひるみなし | 同じ（Fake の `JustEvadeCounterPoise` を撤去するだけ） |
| `EliteAttackDataTests.Unblockable_NotGuardable_NotJustGuardable_ButSteppable` | 出荷アセットが JG 不可 | JG 可（名前も変える） |
| `EnemyDataValidationTests.Attack_Unblockable_MustDisableGuardAndJustGuard` | Guardable=true でエラー | 同じ（Guardable=true はエラーのまま）。JG 可は合格、理由なしの JG 不可はエラーを追加 |

`HitInfoTests`・`AttackSnapshotTests`・`HitBuilderTests` の `JustGuardable=false` は**フラグが保持されること**の検査で、分類と無関係なので変更しない。
