# P5 持ち越し台帳（Actor Transfer Inventory）

作成：2026-09-21（P5-03a）　正本仕様：`Momotaro_P5_Detailed_Spec_v1.1.md` §4.4〜§4.6

エリア遷移で Actor の値を持ち越すために、**状態を所有する単位ごとに、その可変フィールドを 1 つ残らず分類した表**。
受入テスト **P5-E28**（`Momotaro.Tests.EditMode.P5ContractTests.TransferInventory_ClassifiesEveryMutableRuntimeField`）が
本書を読み、Gameplay アセンブリを反射で走査して**双方向に照合**する。

## この台帳は凍結しない（裁定 4）

P5-05（再探索間隔・停滞時間）や P5-07（Encounter）で可変時間値が増える。
**E28 が後工程で落ちるのは正常な動作**であり、そのとき本書を更新するのが正しい対応。
「テストが落ちたから分類を緩める」のではなく、増えた値をここへ理由付きで足すこと。

## 読み方

- 対象型は**手書きのリストではなく反射で発見する**（裁定 1）。`ITransferableRuntime` を実装する型は
  本書に 1 行以上の分類を持たなければならず、逆に本書にあって実在しない型・フィールドも失敗にする。
- **`readonly` は不変を意味しない。** `CompanionVitals._flinch` が実例で、`readonly` フィールドが
  可変の内部 Runtime を指している。分類はフィールド修飾子ではなく**参照先が可変時間値を持つか**で決め、
  入れ子の所有型まで辿る。
- 自動プロパティのバッキングフィールドは、プロパティ名で記載する（`<Current>k__BackingField` → `Current`）。

## 分類

| 分類 | 意味 |
|---|---|
| `保持` | Snapshot に載せて持ち越す。 |
| `Capture前に終了` | 採取の前に行動を終わらせるので、採取時点で初期値。非初期値なら Snapshot 破損として Import が拒否する。 |
| `固定設定・参照` | Data から構築済み、または SerializeField。Snapshot に載せない。 |
| `再構築` | 新 Scene で作り直す。購読・Scene 参照・診断カウンタ・毎 Tick 再計算される値。 |
| `合成` | そのフィールドの参照先が本書の別項目。同じ値を親と子の両方へ複製しない（§4.5）。 |


## `Momotaro.Gameplay.Vitals.Vital`

生命値 1 本（HP・スタミナの基礎）

| フィールド | 分類 | 理由 |
|---|---|---|
| `Current` | 保持 | 現在値。持ち越しの本体。 |
| `Max` | 固定設定・参照 | Data から構築済み。Snapshot には載せない（§4.5）。 |
| `Changed` | 再構築 | 購読は Scene 側が OnEnable／OnDisable で張り直す。旧 Scene の購読を運ばない。 |

## `Momotaro.Gameplay.Combat.HitReactionState`

主人公の被弾リアクション

| フィールド | 分類 | 理由 |
|---|---|---|
| `_hurtRemaining` | Capture前に終了 | Hurt 硬直中は §6.1 が遷移を受付拒否する。採取時 0 で、非 0 は Snapshot 破損として Import が拒否する。 |
| `_invincibleRemaining` | 保持 | 被弾後無敵の残り。§4.5 が明示する持ち越し対象。 |
| `_hurtSeconds` | 固定設定・参照 | PlayerHitReaction の SerializeField から構築。 |
| `_invincibleSeconds` | 固定設定・参照 | 同上。Import の上限検査に使う。 |

## `Momotaro.Gameplay.Combat.StaminaState`

主人公のスタミナとガードブレイク

| フィールド | 分類 | 理由 |
|---|---|---|
| `_current` | 保持 | 現在スタミナ。 |
| `_regenDelayRemaining` | 保持 | 回復待ちの残り。これを落とすと到着直後に回復が早まる。 |
| `_breakRemaining` | Capture前に終了 | Break 中は §6.1 が遷移を受付拒否する。採取時 0 を要求し制限を緩めない（裁定 6）。 |
| `_max` | 固定設定・参照 | Data から構築。 |
| `_regenPerSecond` | 固定設定・参照 | Data から構築。 |
| `_regenDelay` | 固定設定・参照 | Data から構築。 |
| `_zeroRegenDelay` | 固定設定・参照 | Data から構築。 |
| `_breakSeconds` | 固定設定・参照 | Data から構築。 |
| `_breakRestoreRatio` | 固定設定・参照 | Data から構築。 |
| `_breakHpMultiplier` | 固定設定・参照 | Data から構築。 |

## `Momotaro.Gameplay.Combat.FlinchState`

ひるみ（仲間の CompanionVitals が入れ子で所有）

| フィールド | 分類 | 理由 |
|---|---|---|
| `_accumulation` | 保持 | ひるみ蓄積。§4.5 が明示する持ち越し対象。 |
| `_holdRemaining` | 保持 | 蓄積の保持残り。落とすと蓄積が即座に無効化される。 |
| `_flinchRemaining` | 保持 | ひるみ残り。到着後 Stagger を維持するのに要る（§4.6）。 |
| `_immunityRemaining` | 保持 | ひるみ耐性の残り。落とすと到着直後に連続ひるみする。 |
| `_resistance` | 固定設定・参照 | CompanionData から構築。 |
| `_holdSeconds` | 固定設定・参照 | 構築時の設定秒。 |
| `_flinchSeconds` | 固定設定・参照 | CompanionData から構築。 |
| `_immunitySeconds` | 固定設定・参照 | 構築時の設定秒。 |

## `Momotaro.Gameplay.Enemy.Defense.EnemyGuardAbility`

構え能力（仲間の防御が流用）

| フィールド | 分類 | 理由 |
|---|---|---|
| `_cooldownRemaining` | 保持 | 解除後の Guard CD 残り（§4.5）。 |
| `_guarding` | Capture前に終了 | 構え中フラグ。Release 後に採取するため常に false。 |
| `_held` | Capture前に終了 | 構えの保持経過。Release で終了させてから採取する。 |
| `_cooldown` | 固定設定・参照 | Data から構築。Import の上限検査に使う。 |
| `_maxHold` | 固定設定・参照 | Data から構築。 |

## `Momotaro.Gameplay.Enemy.Defense.EnemyEvadeAbility`

回避能力（仲間の防御が流用）

| フィールド | 分類 | 理由 |
|---|---|---|
| `_cooldownRemaining` | 保持 | 中断後の Evade CD 残り（§4.5）。 |
| `_evading` | Capture前に終了 | 回避中フラグ。Interrupt 相当で終了させてから採取する。 |
| `_invulnRemaining` | Capture前に終了 | <b>回避由来の無敵</b>。被弾後無敵とは別物で、持ち越すと回避終了後に無敵だけ復活する（§4.5 末尾）。 |
| `_cooldown` | 固定設定・参照 | Data から構築。 |
| `_invulnSeconds` | 固定設定・参照 | Data から構築。 |

## `Momotaro.Gameplay.Companion.CompanionVitals`

仲間の生存値

| フィールド | 分類 | 理由 |
|---|---|---|
| `Health` | 合成 | Vital。readonly だが参照先は可変（裁定 1）。 |
| `IsDown` | 保持 | ダウン状態。§4.6 の復元表が HP 0 との整合を要求する。 |
| `_recoveryRemaining` | 保持 | 復帰待ちの残り。§4.5 が明示する持ち越し対象。 |
| `_postHitInvincibleRemaining` | 保持 | 被弾後無敵の残り。 |
| `_flinch` | 合成 | FlinchState。<b>readonly だが参照先は可変</b>。裁定 1 が名指しする実例。 |
| `_postHitInvincibleSeconds` | 固定設定・参照 | CompanionData から構築。 |
| `_recoverySeconds` | 固定設定・参照 | CompanionData から構築。 |
| `_reviveHpRatio` | 固定設定・参照 | CompanionData から構築。Import は Revive を呼ばないので使わない。 |

## `Momotaro.Gameplay.Player.PlayerVitals`

主人公の生命値の容れ物（入れ子の所有者）

| フィールド | 分類 | 理由 |
|---|---|---|
| `Health` | 合成 | Vital。 |
| `Stamina` | 合成 | Vital。現行の主人公スタミナ実体は PlayerVitalsHolder の StaminaState 側で、こちらは表示用の値。 |

## `Momotaro.Gameplay.Player.PlayerHitReaction`

被弾リアクションの保持コンポーネント

| フィールド | 分類 | 理由 |
|---|---|---|
| `_state` | 合成 | HitReactionState が実体。Controller 側に CD を複製しない（§4.5）。 |
| `_hurtSeconds` | 固定設定・参照 | SerializeField。 |
| `_postHitInvincibleSeconds` | 固定設定・参照 | SerializeField。 |

## `Momotaro.Gameplay.Player.PlayerVitalsHolder`

主人公の生存値の窓口

| フィールド | 分類 | 理由 |
|---|---|---|
| `_vitals` | 合成 | PlayerVitals（HP）。 |
| `_stamina` | 合成 | StaminaState（スタミナの実体）。 |
| `_defeated` | 保持 | Import 後に HP から導出して整合させる。Snapshot には載せず HP を正本とする。 |
| `_data` | 固定設定・参照 | SerializeField の PlayerData。 |
| `_transferInProgress` | Capture前に終了 | 守護転送の再入ガード。遷移手順 §6.2 の 4 で行動を停止してから採取する。 |
| `_transferHitId` | Capture前に終了 | 進行中の転送の命中 id。同上。 |
| `_incomingObservers` | 再構築 | 被弾入口の購読者（R3-02）。OnEnable／OnDisable 対称で新 Scene が張り直す。 |
| `_guardState` | 再構築 | 同一 GameObject から解決し直す参照。 |
| `_guardStateResolved` | 再構築 | 上の解決済みフラグ。 |
| `_justGuardState` | 再構築 | 同上。 |
| `_justGuardStateResolved` | 再構築 | 同上。 |
| `_evadeState` | 再構築 | 同上。 |
| `_evadeStateResolved` | 再構築 | 同上。 |
| `_justEvadeState` | 再構築 | 同上。 |
| `_justEvadeStateResolved` | 再構築 | 同上。 |
| `_specialCancel` | 再構築 | 同上。 |
| `_specialCancelResolved` | 再構築 | 同上。 |
| `_hurtReaction` | 再構築 | 同上。値は PlayerHitReaction 側が持ち越す。 |
| `_hurtReactionResolved` | 再構築 | 同上。 |
| `_reactionMotor` | 再構築 | 同上。 |
| `_reactionMotorResolved` | 再構築 | 同上。 |
| `_guardianResolver` | 再構築 | 守護の判断先。新 Scene の仲間から解決し直す。 |
| `_guardianResolverResolved` | 再構築 | 同上。 |
| `Defeats` | 再構築 | 通知チャネル。旧 Scene の購読を運ばない。 |
| `Results` | 再構築 | 同上。 |
| `GuardianTransfers` | 再構築 | 同上。 |

## `Momotaro.Gameplay.Companion.CompanionStateMachine`

仲間の状態機（CompanionActor が入れ子で所有）

| フィールド | 分類 | 理由 |
|---|---|---|
| `Current` | 保持 | 配置状態。値は運ぶが、反映は CompanionStateArbiter.TryRestoreState だけを窓口にする（§4.6）。 |
| `LastReason` | 再構築 | 復元時は Restored になる。旧 Scene の理由を運ばない。 |
| `IllegalTransitionCount` | 再構築 | 診断カウンタ。新 Scene で 0 から。E29 がこの 0 を検査する。 |
| `_loggedIllegal` | 再構築 | 不正遷移の重複記録抑止。診断用。 |
| `_actorId` | 固定設定・参照 | 生成時に決まる識別子。 |
| `_onChanged` | 再構築 | 状態通知。新 Scene が張り直す。 |
| `_illegalLogger` | 再構築 | 診断ログの出力先。 |

## `Momotaro.Gameplay.Companion.CompanionActor`

仲間の Actor

| フィールド | 分類 | 理由 |
|---|---|---|
| `_machine` | 合成 | CompanionStateMachine。配置状態はここが持つ。 |
| `_data` | 固定設定・参照 | SerializeField の CompanionData。 |
| `_initialState` | 固定設定・参照 | SerializeField。未加入開始（Away）の指定に使う。 |
| `_facing` | 再構築 | 到着 Entry の向きを採用する（§3.1）。旧 Scene の向きを運ばない。 |
| `_slotIndex` | 再構築 | 隊列位置は到着時に安全な位置へ割り当て直す（§10.2）。 |
| `States` | 再構築 | 状態通知チャネル。新 Scene が張り直す。 |

## `Momotaro.Gameplay.Companion.CompanionCombatController`

仲間の通常攻撃

| フィールド | 分類 | 理由 |
|---|---|---|
| `_cooldownRemaining` | 保持 | 攻撃 CD 残り。<b>CancelAttack 完了後</b>に採取する（§4.4）。 |
| `_attack` | Capture前に終了 | CompanionAttackState。進捗・Phase は CancelAttack で終了させる。 |
| `_plan` | Capture前に終了 | 攻撃 Plan。開始時 Snapshot なので持ち越さない。 |
| `_snapshot` | Capture前に終了 | AttackSnapshot。同上。 |
| `_attackPower` | Capture前に終了 | 攻撃開始時に固定した攻撃力。同上。 |
| `_currentSwing` | Capture前に終了 | 進行中の命中 id。 |
| `_action` | Capture前に終了 | 行動の引換券。遷移で無効化する。 |
| `_allocator` | 再構築 | 命中 id の採番器。Scene 単位で作り直す。 |
| `_hitTracker` | 再構築 | 多段命中の重複排除。1 振りごとの記録。 |
| `_overlapBuffer` | 固定設定・参照 | 使い回しの判定バッファ。状態を持たない。 |
| `_actor` | 再構築 | Scene 参照。 |
| `_motor` | 再構築 | Scene 参照。 |
| `_arbiter` | 再構築 | Scene 参照。 |
| `_states` | 再構築 | Scene 参照。 |
| `_tracker` | 再構築 | Scene 参照。 |
| `_defense` | 再構築 | 同一 GameObject から解決し直す。 |
| `_investigation` | 再構築 | 同上（R3-03）。 |
| `_subscribedActor` | 再構築 | 購読先。対称管理で張り直す。 |
| `_targetMask` | 固定設定・参照 | SerializeField。 |
| `_hitboxHalfWidth` | 固定設定・参照 | SerializeField。 |
| `_hitboxHalfHeight` | 固定設定・参照 | SerializeField。 |
| `_hitboxHeight` | 固定設定・参照 | SerializeField。 |
| `_logDecision` | 固定設定・参照 | SerializeField（診断ログの有無）。 |
| `_wasEngaged` | 再構築 | エッジ検出用。到着後に再評価する。 |
| `_loggedDecision` | 再構築 | 診断ログの重複抑止。 |
| `_lastLogTime` | 再構築 | 同上。 |
| `Decision` | 再構築 | 毎 Tick 再計算される判断結果。 |
| `HitCount` | 再構築 | 診断カウンタ。 |
| `AttackCount` | 再構築 | 診断カウンタ。 |
| `LastDistance` | 再構築 | 毎 Tick 再計算。 |
| `LastAngle` | 再構築 | 毎 Tick 再計算。 |

## `Momotaro.Gameplay.Companion.CompanionDefenseController`

仲間の防御（構え・回避）

| フィールド | 分類 | 理由 |
|---|---|---|
| `_guard` | 合成 | EnemyGuardAbility。CD の実体はこちら。 |
| `_evade` | 合成 | EnemyEvadeAbility。同上。 |
| `_action` | Capture前に終了 | 構え・回避の引換券。 |
| `_hasHoldFacing` | Capture前に終了 | 構え中の向き固定。Release で終了させてから採取する。 |
| `_holdFacing` | Capture前に終了 | 同上。 |
| `_built` | 再構築 | 能力の遅延生成フラグ。新 Scene で組み直す。 |
| `_canGuard` | 再構築 | Data から再判定する。 |
| `_canEvade` | 再構築 | 同上。 |
| `_actor` | 再構築 | Scene 参照。 |
| `_arbiter` | 再構築 | Scene 参照。 |
| `_states` | 再構築 | Scene 参照。 |
| `_danger` | 再構築 | 危険察知の解決先。 |
| `_investigation` | 再構築 | 同一 GameObject から解決し直す（R3-03）。 |
| `_dangerMask` | 固定設定・参照 | SerializeField。 |
| `_dangerRadius` | 固定設定・参照 | SerializeField。 |
| `SawDanger` | 再構築 | 毎 Tick 再評価される診断値。 |

## `Momotaro.Gameplay.Companion.CompanionGuardianController`

仲間の守護（かばう）

| フィールド | 分類 | 理由 |
|---|---|---|
| `_cooldownRemaining` | 保持 | 守護 CD 残り（§4.5）。 |
| `_protect` | Capture前に終了 | 守護の実行券。遷移で無効化する。 |
| `_protectedTarget` | Capture前に終了 | 守護中の対象 Transform。Scene 由来なので運ばない。 |
| `_actor` | 再構築 | Scene 参照。 |
| `_follow` | 再構築 | Scene 参照。 |
| `_host` | 再構築 | 守護先（主人公）。新 Scene で解決し直す。 |
| `_receiver` | 再構築 | Scene 参照。 |
| `_states` | 再構築 | Scene 参照。 |
| `AcceptedCount` | 再構築 | 診断カウンタ。 |
| `TransferCount` | 再構築 | 診断カウンタ。 |
| `Transfers` | 再構築 | 通知チャネル。新 Scene が張り直す。 |

## 対象外にしたもの（理由）

`ITransferableRuntime` を付けていない＝持ち越さない、という判断そのものも記録しておく。

| 型 | 理由 |
|---|---|
| `StepState` / `AttackInputBuffer` / `AttackComboMachine` / `JustGuardState` / `SpecialChargeState` | 主人公の行動中状態。§6.1 が攻撃・Guard・Step・Hurt・GuardBreak 中の遷移を受付拒否するため、採取時点で行動中ではない。到着時に行動の途中へ復元しない（§4.6）。 |
| `CompanionAttackState` | 仲間の攻撃進捗。遷移手順 §6.2 の 4 で中断し、生じた CD だけを `CompanionCombatController` が持ち越す。 |
| `CompanionFollowModel` | 停滞秒・前回距離・Warp 要求数。Pause／Loading 中の時間を停滞へ加算しない（§10.1）ため、到着後に再評価する。 |
| `CompanionTargetTracker` の再評価間隔 | センサーの周期。新 Scene で敵の集合が変わるため理由付きで再初期化する（§4.5 末尾）。 |
| `HitInstanceAllocator` / `MultiHitTracker` | 1 振りごとの採番・重複排除。攻撃の終了とともに意味を失う。 |
| 敵の Runtime 一式 | 遷移で敵は破棄される（§4.1）。通常 Encounter のクリア記録だけが `AreaRuntimeState` に残る。 |

