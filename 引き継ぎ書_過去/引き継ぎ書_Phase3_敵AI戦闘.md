# 引き継ぎ書 — Momotaro Phase 3（敵AI・敵戦闘）

作成日: 2026-08-02 / ブランチ: `phase/3-enemy-combat`（base: `main`）

---

## 0. 最初に読むこと（要約）

- Unity 6（6000.3.20f1）製 2D アクション RPG「Momotaro」の **Phase 3（敵AI・敵戦闘）** 実装中。
- 仕様は `Phase3_敵AI・敵戦闘_タスク仕様書_v1.1.docx`（リポジトリ直下）。タスク **P3-01〜P3-12** を順番に、1タスクずつ実装 → テスト → 静的検証 → 報告 → ユーザーが Unity で確認・コミット → GPT が受入レビュー → 修正依頼、というループで進める。
- **現在地: P3-05（近接敵）まで実装済み。受入修正を3ラウンド適用した直後。** 次は **P3-06（ヘイト・ターゲット）** から。
- 直近ラウンドの変更は **ユーザーが Unity で TestRunner 全緑を確認してからコミット** する運用。未確認の変更が作業ツリーに残っている可能性あり（§5参照）。

---

## 1. 絶対に守る制約（過去に何度も問題になった点）

1. **このエージェント環境に Unity は入っていない。** ビルド・テスト実行はできない。**静的検証のみ**。テスト結果を推測で「緑」と報告しない。実行確認は必ずユーザーに依頼する。
2. **サンドボックスから GitHub へ push できない。** commit / push はユーザーがローカルの GitHub Desktop で行う。git の書き込み操作はしない。読み取りは `GIT_LFS_SKIP_SMUDGE=1 git --no-optional-locks -c core.fsmonitor=false status/diff/show` を使う（LFS smudge を避ける）。
3. **1 タスク = 1 コミット。** 先のタスクを勝手に先取りしない。
4. **ProjectSettings / Packages を勝手に変更しない**（要承認）。
5. **Phase 2 の Combat 契約を再利用する**（`IDamageable` / `ICombatActor` / `HitResultChannel` / `HitInfo`）。敵専用の別戦闘システムを作らない。
6. **git status に出る大量の `**.png` の `M` は Git-LFS smudge ノイズ。** 自分の変更ではない。ステージ対象は毎回 `Assets/` 配下の該当ファイルのみ。

---

## 2. アーキテクチャ / コーディング規約

### asmdef レイヤリング（依存方向を厳守）
`Momotaro.Core ← Data ← Gameplay ← Presentation`、加えて `Infrastructure` / `Editor` / `Tests`（EditMode/PlayMode）。
- Presentation は Gameplay を参照してよい。
- **Gameplay は InputSystem / Animator / Camera を直接参照禁止**（純粋ロジックに隔離）。

### 実装パターン
- **純粋な計算機・状態機械**（`deltaTime` を注入、Time/物理に非依存）＋ **薄い MonoBehaviour ドライバ**。テストは純粋クラスを決定的に検証する。
- MonoBehaviour は 1 ファイル 1 クラスでファイル名一致（Unity 制約）。

### 手書き Unity アセット（YAML 直書き）
- `.meta` は決定論的 GUID = `md5(assetpath)[:32]`。新規テスト .cs には必ず対の `.cs.meta` を同 GUID 規則で作る。
- `.anim`（`m_Sprite` は `fileID: 21300000`）、`.controller`（state `!u!1102` + stateMachine `!u!1107` + `!u!91 &9100000`）、`.prefab`、シーンの手書き編集経験あり。
- **シーンのルート追加時は `SceneRoots`（`!u!1660057539`）の `m_Roots:` に Transform fileID を追加すること**（§7 の Dispatcher 復元がこの例）。

### スプライト Import 規約（プレイヤーとミラー）
PPU=100 / Pivot=BottomCenter(alignment 7) / FullRect(meshType 0) / Bilinear / Uncompressed / Clamp / NoMip / alphaIsTransparency / spriteGenerateFallbackPhysicsShape=0。

---

## 3. 物理・接地の重要規約（P3-05 受入修正で確立。**今後の敵は必ず従う**）

- **接地面はワールド Y=0。** すべてのキャラの足元原点を Y=0 に合わせる。
- **敵ルート = 物理・AI 用の接地基準。** root local Y=0、BoxCollider `center=(0,0.5,0)` `size=(1,1,1)`（=ワールド Y 0〜1）、Collider/親階層に **負の Scale・負の Size を使わない**（"BoxCollider does not support negative scale or size" 警告条件を排除）。
- **Rigidbody 制約 = 116**（`FreezeRotation`(112) + `FreezePositionY`(4)）。`useGravity=0`。押し出しによる浮き上がりを防ぐ（浮くと Collider が地面から離れ、主人公攻撃が空振りする）。
  - 参考: プレイヤーは 112（Y 固定なし・重力あり運用）。値ビット: PosX2/PosY4/PosZ8/RotX16/RotY32/RotZ64。
- **向きはルート Transform を回さない（論理値）。** `EnemyActor.Forward` は `_facing` フィールド、`EnemyActor.SetFacing(dir)` で更新。ルートを回すと Collider ごと回ってしまうため。`EnemyMotor` は移動方向/指定方向を `actor.SetFacing` に委譲（`MoveRotation` は撤去済み）。左右反転は **4方向スプライト**で表現（`flipX`・負 Scale 不使用）。表示のカメラ追従は `CameraFacingBillboard` が **VisualRoot 子だけ**を回す（親・Scale に触れない）。
- **VisualRoot local Y = −0.08**：スプライト底部に 8px（=0.08unit）の透明余白があるため、足元をワールド Y=0（ルート原点）に合わせる補正。
- **シーンの床（Floor）上面も Y=0 に合わせる。** SCN_VS_Field の Floor は `Position Y=-0.5, Scale Y=1, BoxCollider size1 center0` → 上面 0。床上面が 0 でないと敵 Collider がめり込み、FreezePositionY で押し戻せず水平移動も阻害され、継続的な `path blocked` 警告が出る。
- **物理レイヤ**: Player / Enemy / Default(壁・床)。`Physics.IgnoreLayerCollision(Player,Enemy)`（すり抜け）、Enemy↔Default は衝突（壁・床で停止）。`CombatLayers.ConfigureEnemy` / `EnsureCollisionPolicy`。攻撃側 `_targetMask = ~0`。

### テストで踏む物理の落とし穴
- **PlayMode の物理**: プロジェクトの `DynamicsManager` は `m_SimulationMode: 0`(FixedUpdate) かつ **`m_AutoSyncTransforms: 0`**。
  - 手動決定的テスト: `Physics.simulationMode = SimulationMode.Script` + 毎ステップ `Physics.Simulate(0.02f)`（`Physics.Simulate` は MonoBehaviour の FixedUpdate を呼ばない点に注意）。
  - 実挙動テスト（IsBlocked や実移動）: simulationMode を変えず `yield return new WaitForFixedUpdate()` で実ループを回す。
  - **OverlapBox の前に必ず `Physics.SyncTransforms()`**（autoSync=0 のため移動中の対象を取りこぼす）。実装側（`PlayerStateController.PollHitbox` / `EnemyAttackController.PollHitbox`）にも挿入済み。
- **EditMode**: Awake/OnEnable が確実に走らないため、リフレクションで private フィールド注入や公開シーム（`EvaluateOnce`/`TickBrain`/`TickAttack`/`TryApplyHit`）を使う。`Time.deltaTime` は 0 のこともある。

---

## 4. 進捗（P3-01〜P3-05 完了 + スプライト受入 + 受入修正）

| Task | 内容 | 状態 |
|---|---|---|
| P3-01 | 敵共通契約・データ（EnemyState 16種, StateMachine, Vitals, Actor, ArchetypeData, AttackData） | 完了・受入済 |
| P3-02 | 認識・警戒共有（Noise 聴覚 / Vision 視覚 / PerceptionState / Registry / EnemyPerception） | 完了・受入済 |
| P3-03 | 追跡・間合い・帰還（EnemyEngagementDecider / EnemyMotor / EnemyBrain） | 完了・受入済 |
| P3-04 | 敵攻撃パイプライン（Aiming / AttackMachine / Selector / HitFactory / Telegraph / AttackController） | 完了・受入済（修正3件済） |
| 骸骨剣士スプライト受入 | 77枚配置・21 anim clip・21 state Animator・VisualAdapter・Prefab 接続 | 完了・受入済 |
| P3-05 | 近接敵（攻撃後待機 0.7–1.2s / 近接値調整 / DebugLogger） | 実装完了 |
| P3-05 受入修正1 | 実 Hitbox 経路で Stagger/Stunned/Down 遷移 + PlayMode 統合テスト | 完了 |
| P3-05 受入修正2 | **物理ルート接地**（root Y0 / Collider center0.5 / constraints116 / 論理Facing / VisualRoot -0.08 / シーン stray override 除去） | 完了 |
| P3-05 受入修正3 | **床上面 Y=0 整合**（Floor Y-0.5）+ 接地テスト群 | 完了 |
| P3-05 受入修正4 | **CombatFeedbackDispatcher 復元**（Cube 削除の巻き添え回帰の修正） | 完了 |

### 残タスク（未着手）
| Task | タイトル | 目的（仕様書より） |
|---|---|---|
| **P3-06** | ヘイト・ターゲット | Phase 4 の犬を追加しても AI を書き直さない Target 選択基盤を作る |
| P3-07 | 攻撃スロット・画面内制御 | 複数敵が同時に理不尽な攻撃を始めない集団制御 |
| P3-08 | 遠距離敵 | 接近判断と Projectile 対応の検証 |
| P3-09 | 強敵 | 複数の予兆を見分け対処を選ぶ上位敵 |
| P3-10 | 敵防御・回避・撃破 | 防御・回避・終了処理の共通化 |
| P3-11 | 仮UI・Debug・性能検証 | AI 誤動作と集団戦負荷を人力で発見できる検証環境 |
| P3-12 | 統合受入 | Phase 2 と接続した戦闘ループとして正式受入（→ main へ PR） |

---

## 5. 現在の未コミット状態（次スレッド開始時に確認）

直近の受入修正（修正2〜4）のファイルが **作業ツリーに残っている可能性が高い**（ユーザーの TestRunner 確認・コミット待ち）。次のように確認してからコミット指示する。

```
GIT_LFS_SKIP_SMUDGE=1 git --no-optional-locks -c core.fsmonitor=false status --short -- Assets/
```

想定される関連ファイル（存在は確認済み）:
- `Assets/_Project/Scripts/Gameplay/Enemy/EnemyActor.cs`（論理 Facing / SetFacing）
- `Assets/_Project/Scripts/Gameplay/Enemy/Locomotion/EnemyMotor.cs`（constraints116 / ルート非回転 / Y速度0）
- `Assets/_Project/Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab`（接地基準）
- `Assets/_Project/Scenes/SCN_VS_Field.unity`（Floor Y-0.5 / stray override 除去 / Dispatcher 復元）
- テスト: `EnemyPrefabGroundingTests.cs`, `SceneGroundPlaneTests.cs`(EditMode), `EnemyGroundingPlayTests.cs`, `EnemyFloorTraversalPlayTests.cs`(PlayMode), `EnemyActorTests.cs`(facing追記), `EnemyMeleeHitPathPlayTests.cs`(接地構成へ更新)

**直近の未確認事項**: P3-05 受入の最終確認（敵が追跡移動する / root Y=0 維持 / Collider・sprite 一致 / 移動中命中 / 壁のみ blocked 警告 / `VsFieldScene_ContainsFeedbackDispatcher` 含む全テスト緑）をユーザーが Unity で取れているか。緑が取れたら P3-05 をコミットして P3-06 へ。

---

## 6. 主要ファイル地図

### Gameplay / Enemy
- `Scripts/Gameplay/Enemy/` … `EnemyActor`(guid 6e8e500b…), `EnemyState`(16状態), `EnemyStateMachine`, `EnemyStatePriority`(Down100>…>Idle5), `EnemyVitals`, `EnemyAttackSnapshot`, `EnemyStateChanged/Channel`
- `Scripts/Gameplay/Enemy/Perception/` … `EnemyNoise`(NoiseCatalog Table8), `EnemyVision`, `PerceptionState`(0.25s認識/3s喪失), `PerceptionTargetRegistry`, `EnemyPerception`, `PhysicsLineOfSightProbe`, `PlayerNoiseEmitter`
- `Scripts/Gameplay/Enemy/Locomotion/` … `EnemyEngagementDecider`, `EnemyMotor`(guid be797302…), `EnemyBrain`(guid b5f4bb63…, 状態の所有者), `EnemyPostAttackWait`
- `Scripts/Gameplay/Enemy/Combat/` … `EnemyAiming`, `EnemyAttackMachine`(Prepare/Active/Recovery), `EnemyAttackSelector`, `EnemyHitFactory`, `EnemyTelegraph*`, `EnemyAttackController`(guid af19f1d7…)
- `Scripts/Data/Characters/` … `EnemyArchetypeData`(IEnemyVitalsConfig), `EnemyRole`, `EnemyAttackData`(guid a7b96158…), enums
- Prototype 敵SO: `SO_Enemy_Melee_Prototype.asset`（HP40/Poise40/Flinch40/defense10/stun3/待機0.7–1.2, guid 7158b576…）

### Presentation
- `Presentation/Enemy/` … `EnemyVisualNames`(4方向clip解決), `EnemyVisualAdapter`(guid 3245b75c…, `actor.Forward`→方向, LateUpdate)
- `Presentation/Characters/CameraFacingBillboard`(guid 30e91949…, VisualRoot のみ回す)
- `Presentation/Diagnostics/` … `EnemyTelegraphView`, `EnemyCombatDebugLogger`(opt-in `_logEnabled`), `CombatFeedbackDispatcher`(guid 4d5e6f70…)

### アート / Prefab / Scene
- 骸骨剣士: `Art/Characters/Enemies/SkeletonSwordsman/Prototype/{Sprites,Animations,Controllers}`、`AC_SkeletonSwordsman.controller`(guid a03e4cfc…, 21 state)
- Prefab: `Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab`
- 検証シーン: `Scenes/SCN_VS_Field.unity`（Player + 敵 + Floor + Wall + CombatFeedbackDispatcher）

### テスト
- EditMode: `EnemyDataValidation/State/Vitals/AttackSnapshot/Actor/Vision/PerceptionState/Noise/…/EnemyPrefabGrounding/SceneGroundPlane/CombatFeedbackDispatcher Tests` ほか多数
- PlayMode: `EnemyMeleeHitPathPlayTests`, `EnemyGroundingPlayTests`, `EnemyFloorTraversalPlayTests`, `PlayerStepCollisionTests` ほか

---

## 7. 直近で解決した回帰（教訓）

- **Cube 削除の巻き添え**: ユーザーが Unity で床調整の際に旧テスト用「Cube」(CombatDummy+CombatDebugHud+CombatFeedbackDispatcher) を削除 → `CombatFeedbackDispatcher` が消え `VsFieldScene_ContainsFeedbackDispatcher` が fail。**Dispatcher を単独 GameObject として復元**（CombatDummy は P3 で不要なので復活させない）。シーンにルート追加する際は `SceneRoots.m_Roots` への登録を忘れない。
- **シーンの stray override**: 敵インスタンスの Sprite 子に紛れ込んだ位置 override（x0.392,z-2.647）が Scene ビューの Collider/sprite ずれの原因だった。PrefabInstance の `m_Modifications` を確認する癖をつける。

---

## 8. 次スレッドの立ち上げ手順（推奨）

1. 本書と `Phase3_敵AI・敵戦闘_タスク仕様書_v1.1.docx` を読む。
2. `git status`（上記コマンド）で未コミット確認。P3-05 が未コミットなら、ユーザーに Unity 全緑確認 → コミットを依頼。
3. **P3-06（ヘイト・ターゲット）** 開始: 仕様書の該当節を精読 → 既存（PerceptionTargetRegistry / EnemyBrain のターゲット参照）を監査 → 設計 → 純粋ロジック実装 → MonoBehaviour 接続 → EditMode/PlayMode テスト → 静的検証 → 報告。§3 の接地・物理規約と §1 の制約を厳守。
4. 各タスクは TaskCreate で細分し、最後に検証タスク（静的検証／必要なら subagent）を必ず入れる。
